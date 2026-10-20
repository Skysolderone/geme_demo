using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Relics;
using Siege.Core.Scoring;

namespace Siege.Core.Match;

/// <summary>
/// 战场结算（battlefield「战场结算」「阶段推进」）。open-map（负责人 2026-10-19 裁决 O3 / O5）：5 块出生棋盘建局时即紧贴成一张连通地图，整张地图是<b>唯一战场</b>；
/// 配对、链接（拼接）、等待者与决赛倒计时随段 B 删除，对局阶段只剩 插旗 → 构筑（第 1–3 大回合）→ 交战（第 4 大回合起）→ 终局，构筑与交战只差合法落子范围与改造范围。
/// </summary>
/// <remarks>
/// <para><b>战场结算</b>：大回合结束时对唯一战场检查整轮 Pass / 填满（<see cref="SettleAtRoundEnd"/>），出局 / 弃赛之后即时检查减员（<see cref="SettleByAttrition"/>）；
/// 结算即终局"最后战场结算"（大回合结束判定），"只剩一名参赛玩家"即时判定且优先。</para>
/// <para>战场仍按几何四邻由地图按需导出（<see cref="PublicRules.BattlefieldOf"/>），开局即恰 1 个、标识 0；"战场"概念与 <see cref="EndReason.LastBattlefieldSettled"/> 的命名保留（open-map 裁决 B1）。</para>
/// </remarks>
public sealed partial class MatchFlow
{
    private readonly SortedSet<int> _settledBattlefields = [];
    private readonly SortedSet<PlayerId> _passedThisRound = [];
    private readonly List<BattlefieldSettlementRecord> _settlements = [];
    private ImmutableSortedDictionary<int, int> _lockedFlagCounts = ImmutableSortedDictionary<int, int>.Empty;

    /// <summary>锁定时每块出生棋盘上的旗数（出生棋盘编号 → 旗数，只含有旗的棋盘；match-telemetry 第 2 项，离线统计同区率与各棋盘被选次数的数据源）。插旗锁定前为空。</summary>
    public ImmutableSortedDictionary<int, int> LockedFlagCounts => _lockedFlagCounts;

    /// <summary>已结算的战场标识（升序）。</summary>
    public ImmutableArray<int> SettledBattlefields => [.. _settledBattlefields];

    /// <summary>全部战场结算记录，按发生顺序（含减员结算）。</summary>
    public IReadOnlyList<BattlefieldSettlementRecord> Settlements => _settlements;

    /// <summary>本大回合内已 Pass 的在局玩家（整轮 Pass 结算的依据；大回合结束时清空）。不复用全局的连续 Pass 计数。</summary>
    public ImmutableArray<PlayerId> PassedThisRound => [.. _passedThisRound];

    /// <summary>某战场是否已结算。</summary>
    public bool IsBattlefieldSettled(int battlefieldId) => _settledBattlefields.Contains(battlefieldId);

    /// <summary>某玩家所在战场的标识（= 含其出生棋盘的连通分量里最小的出生棋盘编号；open-map 起恒为 0）。须已锁定出生区。</summary>
    public int BattlefieldOf(PlayerId player)
    {
        int zone = Require(player).BirthZone ?? throw new SiegeRuleException($"玩家 {player} 尚未锁定出生区。");
        return PublicRules.BattlefieldOf(Board, zone).Id;
    }

    /// <summary>某战场里仍在局的玩家（升序）。</summary>
    public ImmutableArray<PlayerId> ActivePlayersIn(int battlefieldId) =>
        [.. _players.Where(p => _records[p].Status == PlayerStatus.Active && BattlefieldOf(p) == battlefieldId)];

    /// <summary>全部在局玩家所在的战场（升序；open-map 起至多一个）。</summary>
    public ImmutableArray<int> OccupiedBattlefields() =>
        [.. _players.Where(p => _records[p].Status == PlayerStatus.Active).Select(BattlefieldOf).Distinct().Order()];

    // ---------- 内部 ----------

    /// <summary>插旗锁定时记下每块出生棋盘上的旗数（遥测用）。</summary>
    private void RecordLockedFlagCounts(ImmutableSortedDictionary<PlayerId, int> zones) =>
        _lockedFlagCounts = zones.Values.GroupBy(z => z).ToImmutableSortedDictionary(g => g.Key, g => g.Count());

    // ---------- 战场结算（2.6；裁决 S10 / D6：判定与执行两步） ----------

    /// <summary>
    /// 大回合结束时的战场结算（battlefield「战场结算」，构筑期同样生效）：对每个未结算、在局玩家 ≥ 2 的战场按标识升序检查（a）本大回合内全员 Pass、（b）无可落子空格、
    /// （c）本大回合序号 ≥ 大回合上限安全网（cycle-stop D2，<see cref="MatchOptions.RoundCap"/> &gt; 0 才启用），满足即结算。
    /// 只有 1 名在局玩家的战场不检查：既不因 Pass、也不因填满结算（裁决 S9）。
    /// 同一大回合结束时结算的全部落败者共享一个出局序号，名次按结算时的比较链统一排序（裁决 S7，见 <see cref="FinalStandings.Compute"/>）。最后清空本大回合的 Pass 记录。
    /// </summary>
    /// <remarks>
    /// 裁决 S10：判定（<see cref="JudgeAtRoundEnd"/>，无副作用）与执行（<see cref="ExecuteSettlement"/>）分开——先手顺序预测（<c>ForecastInitiative</c>）
    /// 复用同一份判定，口径"假设此刻结束、未行动者按未 Pass 算"。判定只读本大回合开始前就定下的盘面、势力快照与 Pass 记录，执行只改落败者的状态。
    /// </remarks>
    private void SettleAtRoundEnd()
    {
        // 结算用的势力按此刻盘面现算（与终局名次同一口径）。
        PowerSnapshot power = Scoreboard.Recalculate(Board, Roster, Math.Max(MajorRound, 1), Relics.TrueContents());
        MarkEstablishedPower(power);
        int? order = null;
        foreach (BattlefieldVerdict verdict in JudgeAtRoundEnd(power, _passedThisRound))
        {
            order ??= ++_eliminationSequence;
            ExecuteSettlement(verdict, order);
        }

        _passedThisRound.Clear();
    }

    /// <summary>
    /// 大回合结束时的结算判定（裁决 S10，<b>无副作用</b>）：按 <see cref="OccupiedBattlefields"/> 升序，对每个未结算、在局玩家 ≥ 2 的战场判
    /// （a）<paramref name="passed"/> 含其全部在局玩家 → 整轮 Pass；（b）无可落子空格 → 填满；（c）<see cref="MajorRound"/> ≥ <see cref="MatchOptions.RoundCap"/>（&gt; 0 才启用）→ 上限；
    /// 判定顺序 (a)、(b)、(c)，同时满足记靠前者；满足即给出判决（胜者与落败者按比较链排好）。
    /// 先手预测（<c>ForecastInitiative</c>，"假设此刻结束"）复用同一份判定，所以上限大回合里的预测同样剔除将落败者（cycle-stop D2）。
    /// <paramref name="power"/> 是此刻盘面的势力快照：大回合结束时由势力榜重算给出，顺序预测时由 <see cref="PowerCalculator"/> 在不推进势力榜的情况下算出，二者同一口径。
    /// </summary>
    private ImmutableArray<BattlefieldVerdict> JudgeAtRoundEnd(PowerSnapshot power, IReadOnlySet<PlayerId> passed)
    {
        ImmutableArray<RelicPublicState> relics = Relics.PublicStates();
        ImmutableArray<BattlefieldVerdict>.Builder verdicts = ImmutableArray.CreateBuilder<BattlefieldVerdict>();
        foreach (int id in OccupiedBattlefields())
        {
            ImmutableArray<PlayerId> members = ActivePlayersIn(id);
            if (_settledBattlefields.Contains(id) || members.Length < 2)
            {
                continue;
            }

            SettlementReason? reason = members.All(passed.Contains) ? SettlementReason.AllPassed
                : !HasPlayableEmptyCell(id) ? SettlementReason.BoardFull
                : Options.RoundCap > 0 && MajorRound >= Options.RoundCap ? SettlementReason.RoundCap
                : null;
            if (reason is { } r)
            {
                verdicts.Add(Judge(id, r, power, relics, winner: null));
            }
        }

        return verdicts.ToImmutable();
    }

    /// <summary>战场内是否还有可落子空格（逐格读 <c>IsPlayableEmpty</c>，与全盘的 <see cref="GameBoard.HasPlayableEmptyCell"/> 同一判据）。</summary>
    private bool HasPlayableEmptyCell(int battlefieldId) =>
        (Board.Battlefields.ById(battlefieldId) ?? throw new SiegeRuleException($"没有战场 {BirthZoneLabel.Of(battlefieldId)}。")).Cells.Any(c => Board[c].IsPlayableEmpty);

    /// <summary>每个含在局玩家的战场 → 在局人数。出局 / 弃赛之前取一次，供减员结算比较。</summary>
    private Dictionary<int, int> OccupancyCounts() =>
        OccupiedBattlefields().ToDictionary(id => id, id => ActivePlayersIn(id).Length);

    /// <summary>
    /// 减员结算（battlefield「战场结算」）：出局 / 弃赛之前在局 ≥ 2 的战场，此刻只剩 1 名在局玩家且未结算 → 即时标记已结算、该玩家为胜者。
    /// 由它引起的终局仍等到大回合结束（小回合内只剩一名参赛玩家的即时终局除外）。
    /// </summary>
    private void SettleByAttrition(Dictionary<int, int> before)
    {
        foreach ((int id, int count) in before.OrderBy(kv => kv.Key))
        {
            if (count < 2 || _settledBattlefields.Contains(id))
            {
                continue;
            }

            ImmutableArray<PlayerId> members = ActivePlayersIn(id);
            if (members.Length == 1)
            {
                SettleBattlefield(id, SettlementReason.Attrition, order: null, winner: members[0]);
            }
        }
    }

    /// <summary>全局终局条件 2：只剩一个含在局玩家的战场，且它已结算（无人的出生棋盘不计）。</summary>
    private bool LastBattlefieldSettled() =>
        OccupiedBattlefields() is [int only] && _settledBattlefields.Contains(only);

    /// <summary>
    /// 以指定胜者结算一个战场（减员结算与测试接缝）：判定 + 执行各一次（<see cref="Judge"/> / <see cref="ExecuteSettlement"/>），不另写比较链。
    /// <paramref name="order"/> 为同批结算共享的出局序号；为 <c>null</c> 且有落败者时新取一个。
    /// </summary>
    private BattlefieldSettlementRecord SettleBattlefield(int battlefieldId, SettlementReason reason, int? order, PlayerId? winner)
    {
        PowerSnapshot power = Scoreboard.Latest ?? throw new SiegeRuleException("尚无势力快照，不能结算战场。");
        return ExecuteSettlement(Judge(battlefieldId, reason, power, Relics.PublicStates(), winner), order);
    }

    /// <summary>
    /// 结算判定的<b>唯一</b>比较链（battlefield「战场结算」，<b>无副作用</b>）：战场内在局玩家按 势力 → 控制信物数 → 独占空格数 → 盘面棋子数 排序
    /// （链全同时按种子兜底顺序 <c>SeedRank</c> 靠前者为胜者、不因并列而不结算，裁决 S6），第一名为胜者、其余为落败者并给出名次（比较链全同者并列）。
    /// <paramref name="winner"/> 非空时直接以它为胜者（减员结算与测试接缝），其余照常排序。
    /// </summary>
    private BattlefieldVerdict Judge(int battlefieldId, SettlementReason reason, PowerSnapshot power, ImmutableArray<RelicPublicState> relics, PlayerId? winner)
    {
        if (_settledBattlefields.Contains(battlefieldId))
        {
            throw new SiegeRuleException($"战场 {BirthZoneLabel.Of(battlefieldId)} 已结算，不得再次结算。");
        }

        ImmutableArray<PlayerId> members = ActivePlayersIn(battlefieldId);
        if (winner is { } forced && !members.Contains(forced))
        {
            throw new SiegeRuleException($"胜者 {forced} 不是战场 {BirthZoneLabel.Of(battlefieldId)} 的在局玩家。");
        }

        List<SettlementEntry> chain = [.. members
            .Select(p => new SettlementEntry(p, 0, power.Of(p).Total, relics.Count(s => s.Control.GrantsEffectTo(p)),
                power.Of(p).ExclusiveCells.Length, Board.GroupsOf(p).Sum(g => g.Size)))
            .OrderBy(e => e, SettlementChain.Instance)
            .ThenBy(e => _records[e.Player].SeedRank)];
        SettlementEntry top = winner is { } w ? chain.Single(e => e.Player == w) : chain[0];
        chain.Remove(top);

        ImmutableArray<SettlementEntry>.Builder entries = ImmutableArray.CreateBuilder<SettlementEntry>(members.Length);
        entries.Add(top with { Rank = 1 });
        for (int i = 0; i < chain.Count; i++)
        {
            int rank = i > 0 && SettlementChain.Instance.Compare(chain[i - 1], chain[i]) == 0 ? entries[^1].Rank : i + 2;
            entries.Add(chain[i] with { Rank = rank });
        }

        return new BattlefieldVerdict(battlefieldId, reason, entries.MoveToImmutable());
    }

    /// <summary>
    /// 执行一份判决（battlefield「战场结算」）：标记战场已结算、写结算记录与事件；落败者以"战场落败"出局，记录结算时的比较量（出局时势力可以大于 0）。
    /// <paramref name="order"/> 为同批结算共享的出局序号；为 <c>null</c> 且有落败者时新取一个。
    /// </summary>
    private BattlefieldSettlementRecord ExecuteSettlement(BattlefieldVerdict verdict, int? order)
    {
        int? eliminationOrder = verdict.Losers.Length == 0 ? null : order ?? ++_eliminationSequence;
        _settledBattlefields.Add(verdict.Battlefield);
        var record = new BattlefieldSettlementRecord(_settlements.Count + 1, MajorRound, verdict.Battlefield, verdict.Reason, verdict.Winner, verdict.Entries, eliminationOrder);
        _settlements.Add(record);
        Emit(FlowEventKind.BattlefieldSettled, verdict.Winner,
            $"战场 {BirthZoneLabel.Of(verdict.Battlefield)} 结算（{verdict.Reason}）；胜者 {verdict.Winner}；{string.Join(" ", record.Entries.Select(e => $"{e.Rank}.{e.Player}={e.Power}"))}");
        foreach (SettlementEntry loser in verdict.Losers)
        {
            PlayerRecord r = _records[loser.Player];
            r.Status = PlayerStatus.Eliminated;
            r.EliminationOrder = eliminationOrder;
            r.EliminatedInMajorRound = MajorRound;
            r.EliminationReason = EliminationReason.BattlefieldDefeat;
            r.Defeat = loser;
            Emit(FlowEventKind.PlayerEliminated, loser.Player, $"第 {eliminationOrder} 个出局（战场 {BirthZoneLabel.Of(verdict.Battlefield)} 落败，势力 {loser.Power}）");
        }

        return record;
    }

    /// <summary>一个战场的结算判决（裁决 S10 的"判定"产物，尚未执行）：战场、原因、按比较链排好且带名次的全部在局玩家（胜者在前）。</summary>
    private sealed record BattlefieldVerdict(int Battlefield, SettlementReason Reason, ImmutableArray<SettlementEntry> Entries)
    {
        /// <summary>胜者。</summary>
        public PlayerId Winner => Entries[0].Player;

        /// <summary>落败者（胜者之外的全部条目）。</summary>
        public ImmutableArray<SettlementEntry> Losers => Entries[1..];
    }

    internal int DebugSeedRankOf(PlayerId player) => Require(player).SeedRank;

    /// <summary>
    /// 测试接缝：在小回合边界以给定胜者结算一个战场（原因记为整轮 Pass），随后立即做终局检查（最后战场结算 → 只剩一名）。
    /// 正式流程的结算只在大回合结束（<see cref="SettleAtRoundEnd"/>）与出局 / 弃赛之后（减员）发生。
    /// </summary>
    internal void DebugSettleBattlefield(int battlefieldId, PlayerId winner)
    {
        RequireStage(TurnStage.Idle);
        MarkEstablishedPower(Scoreboard.Recalculate(Board, Roster, Math.Max(MajorRound, 1), Relics.TrueContents()));
        SettleBattlefield(battlefieldId, SettlementReason.AllPassed, order: null, winner);
        RecalculateDerived();
        if (LastBattlefieldSettled())
        {
            Finish(EndReason.LastBattlefieldSettled);
            return;
        }

        CheckEndConditions();
        if (_pendingEnd is { } reason)
        {
            Finish(reason);
        }
        else
        {
            EnsureCurrentActive();
        }
    }

    /// <summary>战场结算与名次的比较链：势力 → 控制信物数 → 独占空格数 → 盘面棋子数，全部"高者在前"；四级全同返回 0。</summary>
    private sealed class SettlementChain : IComparer<SettlementEntry>
    {
        internal static readonly SettlementChain Instance = new();

        public int Compare(SettlementEntry? a, SettlementEntry? b)
        {
            ArgumentNullException.ThrowIfNull(a);
            ArgumentNullException.ThrowIfNull(b);
            int c = b.Power.CompareTo(a.Power);
            if (c == 0)
            {
                c = b.ControlledRelics.CompareTo(a.ControlledRelics);
            }

            if (c == 0)
            {
                c = b.ExclusiveCells.CompareTo(a.ExclusiveCells);
            }

            return c != 0 ? c : b.Stones.CompareTo(a.Stones);
        }
    }
}
