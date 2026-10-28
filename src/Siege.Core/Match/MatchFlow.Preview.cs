using System.Collections.Immutable;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Preview;
using Siege.Core.Recruit;
using Siege.Core.Scoring;

namespace Siege.Core.Match;

/// <summary>
/// 表现层的只读入口（tactical-ui 裁决 9）：富预演与公开补充载荷。本文件只<b>读</b>对局状态、只调用既有唯一实现，
/// 任何需要"试算"的步骤都在副本上进行（盘面副本由预演内部 Clone，信物层随副本复制），
/// 正式盘面、历史、势力榜、事件与随机子流一律不动。
/// </summary>
public sealed partial class MatchFlow
{
    /// <summary>
    /// 对当前暂放批次做富预演（tactical-ui D2 / D3）：合法性、提子、结算后己方棋串气位、军势明细、势力与名次变化。
    /// 只在部署阶段可用；零副作用，可在每次暂放变更后调用。结果含当前行动玩家的暂放，只交给该玩家本人。
    /// </summary>
    public BatchPreview PreviewCurrentBatch()
    {
        RequireStage(TurnStage.Deploy);
        return BatchPreviewBuilder.Build(Board, _batch!.Context, _batch.Placements, History, Roster, ScoringVersion);
    }

    /// <summary>
    /// 当前行动玩家的单子禁手（forbidden-marks D1）：合法落子范围内、未被暂放占用的每个空格，在当前暂放批次之上再落一子是否会被判
    /// 破坏活形 / 自杀手 / 同形。没有当前行动玩家（插旗、终局）为 <c>null</c>。任何阶段可用：不在部署阶段时暂放为空、范围取
    /// <see cref="LegalRangeFor"/>。结果依赖当前行动玩家的暂放，只交给该玩家本人。零副作用。
    /// </summary>
    public ForbiddenMoveReport? ForbiddenMovesOfCurrentPlayer()
    {
        if (CurrentPlayer is not { } player)
        {
            return null;
        }

        return _batch is { } batch
            ? ForbiddenMoves.Query(Board, player, batch.Context.LegalRange, batch.Placements, History)
            : ForbiddenMoves.Query(Board, player, LegalRangeFor(player), [], History);
    }

    /// <summary>
    /// 发布公开补充载荷：全盘棋串与气、每名玩家的结构参数、本轮顺序明细与下一轮顺序预测。
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
    /// 结构参数（restore-go-core-rules D7）：relic-redesign D4 起只有默认基础值，数值取自 <see cref="EffectSnapshot.Defaults"/>（与小回合开始时生成快照的同一份实现），
    /// 当前值恒等于基础值。非参赛中的玩家为 <c>null</c>。
    /// </summary>
    private ImmutableArray<PlayerStructure> StructuresOf(IReadOnlyDictionary<PlayerId, PlayerStatus> roster)
    {
        ImmutableArray<PlayerStructure>.Builder result = ImmutableArray.CreateBuilder<PlayerStructure>(_players.Length);
        foreach (PlayerId player in _players)
        {
            PlayerStatus status = roster[player];
            if (status != PlayerStatus.Active)
            {
                result.Add(new PlayerStructure(player, status, null));
                continue;
            }

            EffectSnapshot snapshot = EffectSnapshot.Defaults(player, MajorRound, Hands.HeldTypeCount(player));
            result.Add(new PlayerStructure(player, status, new StructureParameters(
                new StructureParameter(EffectSnapshot.BaseRevealCount, snapshot.RevealCount),
                new StructureParameter(EffectSnapshot.BaseFreePickCount, snapshot.FreePickCount),
                new StructureParameter(EffectSnapshot.BaseTypeSlots, snapshot.TypeSlots),
                new StructureParameter(EffectSnapshot.BaseDeployLimitFor(snapshot.MajorRound), snapshot.DeployLimit))));
        }

        return result.MoveToImmutable();
    }

    /// <summary>
    /// 顺序预测（tactical-ui D7）：假设本大回合此刻结束，按 <see cref="EndMajorRound"/> 的同一组输入生成先手值明细与下一轮顺序。
    /// 势力走 <see cref="PowerCalculator.Compute(GameBoard, IReadOnlyDictionary{PlayerId, PlayerStatus}, ScoringVersion)"/>
    /// （不经势力榜，避免推进版本号与峰值遥测；信物从盘面信物层读，与 <see cref="EndMajorRound"/> 同一口径），公式与同值链走 <see cref="InitiativeOrder"/>。
    /// </summary>
    /// <remarks>
    /// 战场结算（board-link-mode 裁决 S10 / D6）：大回合结束会先做战场结算，所以预测先用与 <c>SettleAtRoundEnd</c> <b>同一份</b>判定（<c>JudgeAtRoundEnd</c>，无副作用）
    /// 算出此刻结束时会落败的玩家——口径"假设此刻结束、未行动者按未 Pass 算"（本大回合尚未行动的玩家不在 Pass 记录里）；落败者不进下一轮顺序，
    /// 其余玩家的势力名次按剔除后的名册重算（与结算后 <see cref="EndMajorRound"/> 的重算同口径）。此刻结束会以"最后战场结算"终局时没有下一轮，返回 <c>null</c>。
    /// </remarks>
    private InitiativeReport? ForecastInitiative(IReadOnlyDictionary<PlayerId, PlayerStatus> roster)
    {
        if (Phase != MatchPhase.InProgress || ActiveCount == 0)
        {
            return null;
        }

        int completed = MajorRound;
        PowerSnapshot power = PowerCalculator.Compute(Board, roster, ScoringVersion);
        ImmutableArray<BattlefieldVerdict> verdicts = JudgeAtRoundEnd(power, _passedThisRound);
        if (verdicts.Length > 0)
        {
            ImmutableHashSet<PlayerId> losers = [.. verdicts.SelectMany(v => v.Losers).Select(e => e.Player)];
            roster = roster.ToImmutableSortedDictionary(kv => kv.Key, kv => losers.Contains(kv.Key) ? PlayerStatus.Eliminated : kv.Value);
            ImmutableArray<int> occupied = [.. _players.Where(p => roster[p] == PlayerStatus.Active).Select(BattlefieldOf).Distinct()];
            if (occupied is [int only] && (_settledBattlefields.Contains(only) || verdicts.Any(v => v.Battlefield == only)))
            {
                return null;
            }

            power = PowerCalculator.Compute(Board, roster, ScoringVersion);
        }

        int active = roster.Count(kv => kv.Value == PlayerStatus.Active);
        if (active == 0)
        {
            return null;
        }

        ImmutableArray<InitiativeEntry>.Builder entries = ImmutableArray.CreateBuilder<InitiativeEntry>(active);
        foreach (PlayerId player in _players)
        {
            if (roster[player] != PlayerStatus.Active)
            {
                continue;
            }

            PlayerRecord record = _records[player];
            int rank = power.RankOf(player) ?? throw new SiegeRuleException($"参赛玩家 {player} 没有势力名次。");
            int? previous = completed >= 2 ? _order.IndexOf(player) : null;
            entries.Add(new InitiativeEntry(player, rank, power.Of(player).Total,
                InitiativeOrder.ValueOf(active, rank), previous, record.SeedRank));
        }

        return InitiativeOrder.Generate(completed, entries.MoveToImmutable());
    }
}
