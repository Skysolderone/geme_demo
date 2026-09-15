using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Preview;
using Siege.Core.Relics;
using Siege.Core.Scoring;

namespace Siege.Core.Match;

/// <summary>
/// 表现层的只读入口（tactical-ui 裁决 9）：富预演与公开补充载荷。本文件只<b>读</b>对局状态、只调用既有唯一实现，
/// 任何需要"试算"的步骤都在副本上进行（盘面副本由预演内部 Clone，信物账本副本经 <see cref="RelicLedger.Restore"/> 构造），
/// 正式盘面、历史、账本、势力榜、事件与随机子流一律不动。
/// </summary>
public sealed partial class MatchFlow
{
    /// <summary>
    /// 对当前暂放批次做富预演（tactical-ui D2 / D3）：合法性、提子、结算后己方棋串气位、军势明细、势力与名次变化、将揭示格。
    /// 只在部署阶段可用；零副作用，可在每次暂放变更后调用。结果含当前行动玩家的暂放，只交给该玩家本人。
    /// </summary>
    public BatchPreview PreviewCurrentBatch()
    {
        RequireStage(TurnStage.Deploy);
        return BatchPreviewBuilder.Build(Board, _batch!.Context, _batch.Placements, History, Roster, Relics, MajorRound);
    }

    /// <summary>
    /// 发布公开补充载荷：全盘棋串与气、每名玩家的结构参数及信物来源、本轮顺序明细与下一轮顺序预测。
    /// 与 <see cref="Publish"/> 在同一时刻调用，二者的 <c>BoardSerialized</c> 一致。任何阶段可用，零副作用。
    /// </summary>
    public PublicSupplement PublishSupplement()
    {
        IReadOnlyDictionary<PlayerId, PlayerStatus> roster = Roster;
        return new PublicSupplement(
            Board.Serialize(),
            MajorRound,
            LibertySnapshot.Compute(Board),
            StructuresOf(roster),
            _initiative.Count > 0 && MajorRound == _initiative[^1].CompletedMajorRound + 1 ? _initiative[^1] : null,
            ForecastInitiative(roster));
    }

    /// <summary>
    /// 结构参数：数值取自账本副本上的 <see cref="RelicLedger.SnapshotFor(PlayerId, GameBoard, IReadOnlyDictionary{PlayerId, PlayerStatus}, int, int)"/>（唯一实现，
    /// 副本上的遥测峰值变化不回写正式账本）；来源取同一副本中由该玩家控制的已揭示信物。二者不一致即抛出——
    /// 控制而未揭示只会出现在绕过结算直接改盘的测试局面里，此时列出来源会泄漏内容，宁可响亮失败。
    /// </summary>
    private ImmutableArray<PlayerStructure> StructuresOf(IReadOnlyDictionary<PlayerId, PlayerStatus> roster)
    {
        RelicLedger copy = RelicLedger.Restore(Relics.Generation, Relics.ExportState());
        ImmutableArray<PlayerStructure>.Builder result = ImmutableArray.CreateBuilder<PlayerStructure>(_players.Length);
        foreach (PlayerId player in _players)
        {
            PlayerStatus status = roster[player];
            if (status != PlayerStatus.Active)
            {
                result.Add(new PlayerStructure(player, status, null));
                continue;
            }

            EffectSnapshot snapshot = copy.SnapshotFor(player, Board, roster, Hands.HeldTypeCount(player), MajorRound);
            ImmutableArray<RelicPublicState> granted = [.. copy.PublicStates().Where(s => s.Control.GrantsEffectTo(player))];
            if (granted.FirstOrDefault(s => !s.IsRevealed) is { } hidden)
            {
                throw new SiegeRuleException($"信物 {hidden.Coord.ToNotation()} 由 {player} 控制却未揭示：盘面未经结算被直接修改。");
            }

            result.Add(new PlayerStructure(player, status, new StructureParameters(
                Parameter(EffectSnapshot.BaseRevealCount, snapshot.RevealCount, RelicType.Prospecting, granted),
                Parameter(EffectSnapshot.BaseFreePickCount, snapshot.FreePickCount, RelicType.Conscription, granted),
                Parameter(EffectSnapshot.BaseTypeSlots, snapshot.TypeSlots, RelicType.Depot, granted),
                Parameter(EffectSnapshot.BaseDeployLimitFor(snapshot.MajorRound), snapshot.DeployLimit, RelicType.Command, granted))));
        }

        return result.MoveToImmutable();
    }

    private static StructureParameter Parameter(int baseValue, int value, RelicType type, ImmutableArray<RelicPublicState> granted)
    {
        ImmutableArray<ParameterSource> sources =
        [
            .. granted.Where(s => s.Content!.Value.Type == type).Select(s => new ParameterSource(s.Coord, type, s.Content!.Value.Magnitude)),
        ];
        int sum = baseValue + sources.Sum(s => s.Magnitude);
        if (sum != value)
        {
            throw new SiegeRuleException($"结构参数来源与效果快照不一致：{type} 基础 {baseValue} + 来源 {sum - baseValue} ≠ 快照 {value}。");
        }

        return new StructureParameter(baseValue, value, sources);
    }

    /// <summary>
    /// 顺序预测（tactical-ui D7）：假设本大回合此刻结束，按 <see cref="EndMajorRound"/> 的同一组输入生成先手值明细与下一轮顺序。
    /// 势力走 <see cref="PowerCalculator.Compute(GameBoard, IReadOnlyDictionary{PlayerId, PlayerStatus})"/>（不经势力榜，避免推进版本号与峰值遥测），
    /// 先手修正在账本副本上读取，公式与同值链走 <see cref="InitiativeOrder"/>。
    /// </summary>
    private InitiativeReport? ForecastInitiative(IReadOnlyDictionary<PlayerId, PlayerStatus> roster)
    {
        int active = ActiveCount;
        if (Phase != MatchPhase.InProgress || active == 0)
        {
            return null;
        }

        int completed = MajorRound;
        PowerSnapshot power = PowerCalculator.Compute(Board, roster);
        ImmutableSortedDictionary<PlayerId, int> bonuses =
            RelicLedger.Restore(Relics.Generation, Relics.ExportState()).ReadInitiativeBonuses(Board, roster);

        ImmutableArray<InitiativeEntry>.Builder entries = ImmutableArray.CreateBuilder<InitiativeEntry>(active);
        foreach (PlayerId player in _players)
        {
            PlayerRecord record = _records[player];
            if (record.Status != PlayerStatus.Active)
            {
                continue;
            }

            int rank = power.RankOf(player) ?? throw new SiegeRuleException($"参赛玩家 {player} 没有势力名次。");
            int bonus = bonuses.TryGetValue(player, out int b) ? b : 0;
            int? previous = completed >= 2 ? _order.IndexOf(player) : null;
            entries.Add(new InitiativeEntry(player, rank, power.Of(player).Total, bonus,
                InitiativeOrder.ValueOf(active, rank, bonus), previous, record.SeedRank));
        }

        return InitiativeOrder.Generate(completed, entries.MoveToImmutable());
    }
}
