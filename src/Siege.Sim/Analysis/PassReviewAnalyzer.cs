using System.Numerics;
using Siege.Core.Ai;
using Siege.Core.Match;
using Siege.Core.Scoring;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Sim.Analysis;

// 本文件属于离线分析输出层：允许浮点（裁决 14）。

/// <summary>
/// 战场落败复盘（ai-pass-review 1.2，规格 match-telemetry「战场落败复盘与等待者评估」第 1 条）的汇总。
/// 复盘对象 = 以"战场落败"出局、且其战场因整轮 Pass 结算的玩家；时刻 = 结算那一大回合本人最后一次 Pass（分析时按日志回放到该时刻）。
/// </summary>
/// <param name="Logs">回放的链接模式局数（有结果、有小回合的纳入局；2.4 起全部回放以收集决胜一手）。</param>
/// <param name="SkippedLogs">未能复盘的局：原因 → 局数（地图 / AI 版本不一致、回放分歧等）。</param>
/// <param name="Defeats">整轮 Pass 结算落败总人次（全部纳入局，含未能复盘的局）。</param>
/// <param name="MissingPass">结算那一大回合找不到本人 Pass 快照的人次（应为 0）。</param>
/// <param name="Entries">逐人复盘明细（按种子、小回合序号）。</param>
public sealed record DefeatReviewSection(
    int Logs,
    SortedDictionary<string, int> SkippedLogs,
    int Defeats,
    int MissingPass,
    IReadOnlyList<DefeatReviewEntry> Entries)
{
    /// <summary>复盘到的人次。</summary>
    public int Reviewed => Entries.Count;

    /// <summary>回放中出现的决胜一手（ai-pass-review 2.4）及其后该玩家是否赢下所在战场的下一次结算。</summary>
    public IReadOnlyList<DecisiveOutcome> Decisive { get; init; } = [];

    /// <summary>最好一手"本可翻盘"的人次（对手不回应）。</summary>
    public int Comebacks => Entries.Count(e => e.Review.Best?.ComesBack == true);

    /// <summary>合法候选里存在单点"本可翻盘"的人次（不限最好一手）。</summary>
    public int AnyComebacks => Entries.Count(e => e.Review.ComebackCandidates > 0);

    /// <summary>强制落子批次"本可翻盘"的人次。</summary>
    public int ForcedComebacks => Entries.Count(e => e.Review.Forced?.ComesBack == true);

    /// <summary>最好一手能提子的人次。</summary>
    public int BestCaptures => Entries.Count(e => e.Review.Best is { Captures: > 0 });

    /// <summary>合法候选里存在提子手的人次。</summary>
    public int AnyCaptures => Entries.Count(e => e.Review.CaptureCandidates > 0);

    /// <summary>无任何可落的一手的人次，按原因。</summary>
    public SortedDictionary<string, int> NoMove => Histogram(Entries.Where(e => e.Review.NoMove != NoMoveReason.None).Select(e => e.Review.NoMove.ToString()));

    /// <summary>无合法候选者（<see cref="NoMoveReason.NoLegalCandidate"/>）的落选单点按原因合计。</summary>
    public SortedDictionary<string, int> NoMoveRejections
    {
        get
        {
            var h = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (DefeatReviewEntry e in Entries.Where(e => e.Review.NoMove == NoMoveReason.NoLegalCandidate))
            {
                foreach ((string key, int count) in e.Review.Rejections)
                {
                    h[key] = h.GetValueOrDefault(key) + count;
                }
            }

            return h;
        }
    }

    /// <summary>复盘时实际生效的停手阈值分布。</summary>
    public SortedDictionary<int, int> Thresholds => Histogram(Entries.Select(e => e.Review.PassThreshold));

    /// <summary>复盘结论与实际 Pass 一致（<see cref="BestMoveReview.ImpliesPass"/>）的人次；应等于 <see cref="Reviewed"/>。</summary>
    public int Consistent => Entries.Count(e => e.Review.ImpliesPass);

    /// <summary>有合法候选者的最好一手收益（与停手阈值同口径）。</summary>
    public IReadOnlyList<BigInteger> BestGains => [.. Entries.Where(e => e.Review.Best is not null).Select(e => e.Review.Best!.Gain).Order()];

    /// <summary>复盘时刻的势力差（本战场最高者 − 己方），有对手者。</summary>
    public IReadOnlyList<BigInteger> GapsBefore => [.. Entries.Where(e => e.Review.RivalBefore is not null).Select(e => e.Review.RivalBefore!.Value - e.Review.PowerBefore).Order()];

    private static SortedDictionary<TKey, int> Histogram<TKey>(IEnumerable<TKey> keys)
        where TKey : notnull
    {
        var h = new SortedDictionary<TKey, int>();
        foreach (TKey key in keys)
        {
            h[key] = h.GetValueOrDefault(key) + 1;
        }

        return h;
    }
}

/// <summary>决赛参与者在等待者评估里的分组（ai-pass-review 1.3，design D3）。</summary>
public enum FinalistGroup
{
    /// <summary>等待者：进入决赛时作为单人战场的在局玩家等过 ≥ 1 个大回合（首轮未配对、同区构筑期结算后无对手、或首轮提前结算后等对面）。</summary>
    Waiter,

    /// <summary>首轮刚结算的胜者：等待大回合数为 0，其战场在决赛链接前的那个大回合才结算。</summary>
    FreshWinner,
}

/// <summary>等待者的来由（只细分等待者；首轮刚结算的胜者记 <see cref="FirstRoundWinner"/>）。</summary>
public enum WaiterOrigin
{
    /// <summary>首轮配对时被指定为等待者（单人战场未配对）。</summary>
    Unpaired,

    /// <summary>同区（锁定旗数 ≥ 2）的战场在首轮配对之前（构筑期）已结算、之后无对手。</summary>
    SharedSettledEarly,

    /// <summary>参加了首轮交战（配对或同区就地），所在战场先于其他战场结算，随后等待。</summary>
    FirstRoundSettledEarly,

    /// <summary>其余：首轮没有配对事件（候选 ≤ 1）时独处单人战场等。</summary>
    Other,

    /// <summary>首轮刚结算的胜者（不是等待者）。</summary>
    FirstRoundWinner,
}

/// <summary>一名决赛参与者。</summary>
/// <param name="PowerGap">决赛开始时势力差 = 己方 − 其余决赛参与者的最高势力（决赛链接后的那条快照）。</param>
public sealed record FinalistEntry(string Seed, int Player, FinalistGroup Group, WaiterOrigin Origin, int WaitingRounds, BigInteger Power, BigInteger PowerGap, bool Won);

/// <summary>等待者评估（ai-pass-review 1.3）：只计到达决赛、未被截断、有胜者的链接模式局。</summary>
/// <param name="Finals">纳入的决赛局数。</param>
/// <param name="Participants">逐人明细。</param>
public sealed record WaiterSection(int Finals, IReadOnlyList<FinalistEntry> Participants);

/// <summary>停手复盘与等待者评估的唯一实现（见 <see cref="DefeatReviewSection"/> / <see cref="WaiterSection"/>）。</summary>
public static class PassReviewAnalyzer
{
    /// <summary>
    /// 战场落败复盘：对纳入局中的每局链接模式日志按日志回放复盘（<see cref="DefeatReplay.Review"/>）。没有整轮 Pass 结算落败的样本时返回 <c>null</c>（报告整段不出现）。
    /// 回放要重跑对局，耗时约等于跑局本身，所以只由 <c>analyze --review-defeats</c> 开启（<see cref="AnalysisOptions.ReviewDefeats"/>）。
    /// </summary>
    public static DefeatReviewSection? ReviewDefeats(IReadOnlyList<MatchLog> included)
    {
        ArgumentNullException.ThrowIfNull(included);
        List<MatchLog> logs = [.. included.Where(l => l.IsLinkMode && l.Result is not null && l.Turns.Count > 0)];
        if (logs.Count == 0)
        {
            return null;
        }

        var skipped = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var entries = new List<DefeatReviewEntry>();
        var decisive = new List<DecisiveOutcome>();
        int defeats = 0, missing = 0;
        foreach (MatchLog log in logs.OrderBy(l => l.Seed))
        {
            DefeatReplayResult result = DefeatReplay.Review(log);
            defeats += result.Targets.Count + result.MissingPass;
            missing += result.MissingPass;
            if (result.Skipped is { } reason)
            {
                skipped[reason] = skipped.GetValueOrDefault(reason) + 1;
                continue;
            }

            entries.AddRange(result.Entries);
            decisive.AddRange(result.Decisive.Select(d => new DecisiveOutcome(d, WonNextSettlement(log, d))));
        }

        return new DefeatReviewSection(logs.Count, skipped, defeats, missing, entries) { Decisive = decisive };
    }

    /// <summary>
    /// 决胜一手之后该玩家所在战场的下一次结算（大回合 ≥ 决胜一手所在大回合、名单里有该玩家）是否由他胜出；之后没有含他的结算（截断等）为 <c>null</c>。
    /// </summary>
    private static bool? WonNextSettlement(MatchLog log, DecisiveEntry d) =>
        log.Events
            .Where(e => e.Settlement is { } s && s.MajorRound >= d.MajorRound && s.Entries.Any(x => x.Player == d.Player))
            .OrderBy(e => e.Seq)
            .Select(e => (bool?)(e.Settlement!.Winner == d.Player))
            .FirstOrDefault();

    /// <summary>等待者评估：只读日志，不回放。没有到达决赛的纳入局时返回 <c>null</c>（旧日志与无决赛样本的报告不出现这一段）。</summary>
    public static WaiterSection? Waiters(IReadOnlyList<MatchLog> included)
    {
        ArgumentNullException.ThrowIfNull(included);
        var participants = new List<FinalistEntry>();
        int finals = 0;
        foreach (MatchLog log in included.Where(l => l.IsLinkMode && l.Result is { Truncated: false } && l.Result.Winners.Count == 1).OrderBy(l => l.Seed))
        {
            List<PairingLogEntry> pairings = [.. log.Events.Where(e => e.Pairing is not null).OrderBy(e => e.Seq).Select(e => e.Pairing!)];
            PairingLogEntry? final = pairings.FirstOrDefault(p => p.Kind == nameof(PairingKind.Final));
            if (final is null)
            {
                continue;
            }

            // 决赛开始时 = 决赛链接发生的那个小回合（上一大回合的最后一手）结束时的快照：链接在该小回合内完成，快照已按链接后的盘面记势力。
            TurnSnapshot? start = log.Turns.LastOrDefault(t => t.MajorRound < final.MajorRound);
            if (start is null)
            {
                continue;
            }

            List<PlayerEntry> finalists = [.. start.PlayersState.Where(p => p.Status == nameof(PlayerStatus.Active))];
            if (finalists.Count < 2)
            {
                continue;
            }

            finals++;
            PairingLogEntry? firstRound = pairings.FirstOrDefault(p => p.Kind == nameof(PairingKind.FirstRound));
            List<int> flags = log.Header.LockedFlagCounts ?? [];
            int winner = log.Result!.Winners[0];
            foreach (PlayerEntry p in finalists)
            {
                int waiting = p.WaitingRounds ?? 0;
                FinalistGroup group = waiting > 0 ? FinalistGroup.Waiter : FinalistGroup.FreshWinner;
                WaiterOrigin origin = group == FinalistGroup.FreshWinner ? WaiterOrigin.FirstRoundWinner : OriginOf(log, p.Player, firstRound, flags);
                BigInteger rival = finalists.Where(o => o.Player != p.Player).Max(o => o.Total);
                participants.Add(new FinalistEntry(log.Header.Seed, p.Player, group, origin, waiting, p.Total, p.Total - rival, p.Player == winner));
            }
        }

        return finals == 0 ? null : new WaiterSection(finals, participants);
    }

    /// <summary>等待者的来由：首轮配对的等待者 → 未配对；同区棋盘在首轮配对之前已结算 → 同区提前结算；参加过首轮配对 → 首轮提前结算；其余 → 其他。</summary>
    private static WaiterOrigin OriginOf(MatchLog log, int player, PairingLogEntry? firstRound, List<int> flags)
    {
        int zone = player < log.Header.Zones.Count ? log.Header.Zones[player] : -1;
        if (firstRound is not null && firstRound.Waiter == zone)
        {
            return WaiterOrigin.Unpaired;
        }

        bool shared = zone >= 0 && zone < flags.Count && flags[zone] >= 2;
        if (shared)
        {
            int pairingRound = firstRound?.MajorRound ?? int.MaxValue;
            bool settledEarly = log.Events.Any(e => e.Settlement is { } s && s.Winner == player && s.MajorRound < pairingRound && s.MajorRound <= LinkModeAnalyzer.BuildRounds);
            return settledEarly ? WaiterOrigin.SharedSettledEarly : WaiterOrigin.FirstRoundSettledEarly;
        }

        return firstRound is not null && firstRound.Candidates.Contains(zone) ? WaiterOrigin.FirstRoundSettledEarly : WaiterOrigin.Other;
    }
}

/// <summary>一次决胜一手与其后果：该玩家所在战场的下一次结算是否由他胜出（之后没有含他的结算为 <c>null</c>）。</summary>
public sealed record DecisiveOutcome(DecisiveEntry Entry, bool? WonBattlefield);
