using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Relics;
using Siege.Core.Scoring;
using Siege.Sim.Logging;

namespace Siege.Sim.Analysis;

// 本文件属于离线分析输出层：允许浮点（裁决 14）。它只读日志对象（来自文件），不引用任何对局运行时对象（design.md D6）。

public sealed record AnalysisOptions
{
    /// <summary>是否把调试 AI 局 / 人工接管局也纳入（design.md D7 默认排除）。</summary>
    public bool IncludeContaminated { get; init; }

    /// <summary>胜率类指标的最低样本数（裁决 5：200 局是快速回归下限，默认基线 2000 局）。</summary>
    public int RequiredMatches { get; init; } = 200;
}

/// <summary>收敛情况（裁决 13 → round-cap D5）：非达上限终局（含势力碾压）vs 以规则原因 <c>MajorRoundLimit</c>（达大回合上限）终局，单独成段。</summary>
public sealed record ConvergenceSection(
    int Converged,
    int Capped,
    Proportion CappedRate,
    SortedDictionary<string, int> Reasons,
    double MeanMajorRoundsConverged,
    double MeanMajorRoundsAll,
    double MeanTurnsPerMatch);

/// <summary>
/// 势力碾压（match-telemetry 平衡分析方向 8，dominance-victory）：碾压胜占比（分母 = 纳入分析的局）、这些局碾压成立（获胜）的平均大回合、
/// 触发时获胜者势力与第 2 名势力之比。第 2 名势力为 0 时比值无定义，不计入均值，单独计数。
/// </summary>
public sealed record DominanceSection(
    int Matches,
    int DominanceWins,
    Proportion Rate,
    double MeanTriggerRound,
    double MeanPowerRatio,
    int RatioSamples,
    int RatioUndefined);

/// <summary>
/// 落后者征募补偿（catch-up-recruit 裁决 3 的报告口径）：占比分母是<b>纳入局</b>的小回合总数，不是全部局。
/// 纳入 = 日志首部记录本局开启补偿，且每条小回合快照都带补偿留痕；旧日志（<see cref="TurnSnapshot.CatchUpReveal"/> 为 <c>null</c>）
/// 与关闭补偿的局整局计入 <see cref="Skipped"/>，MUST NOT 把缺字段回填成 0——那会把旧局错算成"从未补偿"。
/// </summary>
/// <param name="FinalRankOfCompensated">至少获得过一次补偿的玩家，其终局名次 → 人次。</param>
public sealed record CatchUpSection(
    int Matches,
    int Skipped,
    int Turns,
    int CompensatedTurns,
    Proportion CompensatedTurnRate,
    int RevealTriggers,
    int PickTriggers,
    SortedDictionary<int, int> FinalRankOfCompensated);

/// <summary>§16 六项数值目标。</summary>
public sealed record TargetsSection(
    SortedDictionary<int, int> DeployLimitRounds1To3,
    SortedDictionary<int, int> DeployLimitRounds4To6,
    SortedDictionary<int, int> DeployLimitRounds7Plus,
    Deviation DeployPhase1,
    Deviation DeployPhase2,
    Deviation DeployPhase3,
    List<(int Round, double MeanPower, long MaxGroupPower, int Samples)> PowerCurve,
    Deviation PowerOpening,
    Deviation PowerMid,
    SortedDictionary<int, int> FirstConflictRounds,
    int MatchesWithoutConflict,
    Deviation FirstConflict,
    Deviation MajorRoundMinutes,
    double MeanTurnsPerMajorRound,
    double MeanAiMsPerMajorRound,
    SortedDictionary<int, int> EndRounds,
    Deviation EndRound,
    Deviation MatchMinutes,
    double MeanAiMsPerMatch,
    Deviation LeaderWinRate);

/// <summary>第 3 大回合领先者（裁决 3 双口径）。</summary>
public sealed record LeaderSection(
    int Samples,
    int TiedSamples,
    Proportion AnyOfGroupWins,
    Proportion SpecificPlayerWins,
    Proportion NonLeaderWins,
    double Baseline,
    bool EnoughSamples);

/// <summary>先手滚雪球：按下一大回合行动位置统计名次变化。</summary>
public sealed record SnowballSection(
    List<(int Position, int Improved, int Same, int Worse)> RankChangeByPosition,
    Proportion FirstMoverStaysFirst,
    List<(int Position, double MeanFinalRank, int Samples)> FinalRankByRound3Position);

public sealed record PieceStat(string Type, int Offered, int Picked, Proportion SelectionRate, Proportion WinRateOfPickers);

public sealed record RelicStat(string Type, int Count, int Revealed, int ControlledTurns, double MeanControlledTurnsPerRelic, Proportion WinRateOfControllers);

public sealed record SelectionSection(List<PieceStat> Pieces, List<RelicStat> Relics);

/// <summary>§17.4 高倍率棋串。<see cref="PeakCountDistribution"/> 按原始倍增子数量（堆了多少），<see cref="PeakEffectiveExponentDistribution"/> 按生效倍率指数（封顶后生效了多少）。</summary>
public sealed record MultiplierSection(
    int MatchesWithPeak,
    SortedDictionary<int, int> PeakCountDistribution,
    SortedDictionary<int, int> PeakEffectiveExponentDistribution,
    double MeanFormationRound,
    double MeanPeakPower,
    long MaxPeakPower,
    Proportion DestroyedRate);

/// <summary>一种棋子的势力归因：盘面枚数、盘面占比、归因势力、势力占比、每颗平均贡献。</summary>
public sealed record PieceShare(string Type, long Stones, double StoneShare, long Power, double PowerShare, double MeanPerStone);

/// <summary>
/// 各棋子势力占比（multiplier-rebalance 裁决 3，proposal 表格口径）：逐局取终局快照（最后一条小回合快照）中参赛玩家（<c>Active</c>）的全部棋串；
/// 普通 / 堡垒 / 连珠 / 协同计各自基础军势，连珠与协同再分得本串对应的位置加值；倍增子计各自基础军势 1，再分得本串"放大出来的部分"
/// <c>⌊基础 × 倍率⌋ − 基础</c>（整数，经唯一的 <see cref="Siege.Core.Scoring.Multiplier.Apply"/> 按日志里的生效指数算）。势力占比的分母是纳入局归因势力之和（棋串军势，不含领地分）。
/// 终局快照里有任何参赛玩家棋串缺 <see cref="GroupEntry.PieceCounts"/>（旧日志）或整局无快照的局计入 <see cref="Skipped"/>，不参与任何分子分母。
/// </summary>
public sealed record PieceShareSection(int Matches, int Skipped, long TotalStones, long TotalPower, List<PieceShare> Pieces);

public sealed record ZoneStat(int Zone, Proportion WinRate, bool Significant);

public sealed record BirthZoneSection(
    double Baseline,
    List<ZoneStat> All,
    List<ZoneStat> RelicsConverged,
    List<ZoneStat> RelicsNotConverged,
    int ConvergedMatches,
    int NotConvergedMatches);

public sealed record GrowthAxisSection(
    int WinnerSamples,
    SortedDictionary<string, int> SequenceCounts,
    SortedDictionary<string, int> FirstAxisCounts,
    string? DominantSequence,
    double DominantShare);

public sealed record StallingSection(int SignalTurns, int TotalTurns, Proportion Ratio);

public sealed record AiQualitySection(
    int SettledBatches,
    int Captures,
    double MeanCapturesPerBatch,
    int SuicideAttempts,
    Proportion SuicideAttemptRate,
    Proportion PassRate,
    bool Unreliable,
    string Verdict);

/// <summary>完整平衡报告。</summary>
public sealed record BalanceReport(
    int TotalLogs,
    int Included,
    int ExcludedContaminated,
    int ExcludedFailed,
    int PlayerCount,
    AnalysisOptions Options,
    ConvergenceSection Convergence,
    DominanceSection Dominance,
    TargetsSection Targets,
    LeaderSection Leader,
    SnowballSection Snowball,
    SelectionSection Selection,
    MultiplierSection Multiplier,
    PieceShareSection PieceShares,
    CatchUpSection CatchUp,
    BirthZoneSection BirthZones,
    GrowthAxisSection GrowthAxes,
    StallingSection Stalling,
    AiQualitySection AiQuality);

/// <summary>
/// 离线平衡分析（match-telemetry 三条 Requirement）：只读日志，不重跑对局，改口径无需重跑（design.md D6）。
/// </summary>
public static class BalanceAnalyzer
{
    /// <summary>"近乎随机"的旁证阈值：自杀手尝试率 ≥ 25% 或 Pass 率 ≥ 60%（初值，随基线数据校准）。</summary>
    public const double SuicideUnreliableThreshold = 0.25;

    public const double PassUnreliableThreshold = 0.60;

    private static readonly string[] AxisNames = ["供给", "部署", "槽位", "倍率"];

    public static BalanceReport Analyze(IReadOnlyList<MatchLog> logs, AnalysisOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(logs);
        options ??= new AnalysisOptions();
        int failed = logs.Count(l => l.IsFailed);
        var included = new List<MatchLog>();
        int contaminated = 0;
        foreach (MatchLog log in logs.Where(l => !l.IsFailed))
        {
            if (log.IsContaminated && !options.IncludeContaminated)
            {
                contaminated++;
                continue;
            }

            included.Add(log);
        }

        int playerCount = included.Count == 0 ? 0 : included[0].Header.Players.Count;
        return new BalanceReport(
            logs.Count, included.Count, contaminated, failed, playerCount, options,
            Convergence(included),
            Dominance(included),
            Targets(included, options),
            Leader(included, options),
            Snowball(included),
            Selection(included),
            Multiplier(included),
            PieceShares(included),
            CatchUp(included),
            BirthZones(included),
            GrowthAxes(included),
            Stalling(included),
            AiQuality(included));
    }

    // ---------- 落后者征募补偿 ----------

    private static CatchUpSection CatchUp(List<MatchLog> logs)
    {
        int matches = 0;
        int skipped = 0;
        int turns = 0;
        int compensated = 0;
        int revealTriggers = 0;
        int pickTriggers = 0;
        var finalRanks = new SortedDictionary<int, int>();
        foreach (MatchLog log in logs)
        {
            // 被排除样本：关闭补偿的局、以及 catch-up-recruit 之前没有留痕字段的旧日志。
            if (log.Header.CatchUpRecruit != true || log.Turns.Count == 0 || log.Turns.Any(t => t.CatchUpReveal is null || t.CatchUpPick is null))
            {
                skipped++;
                continue;
            }

            matches++;
            var compensatedPlayers = new HashSet<int>();
            foreach (TurnSnapshot turn in log.Turns)
            {
                turns++;
                int reveal = turn.CatchUpReveal!.Value;
                int pick = turn.CatchUpPick!.Value;
                revealTriggers += reveal;
                pickTriggers += pick;
                if (reveal + pick > 0)
                {
                    compensated++;
                    compensatedPlayers.Add(turn.Player);
                }
            }

            foreach (StandingEntry entry in log.Result!.Standings.Where(s => compensatedPlayers.Contains(s.Player)))
            {
                finalRanks[entry.Rank] = finalRanks.TryGetValue(entry.Rank, out int n) ? n + 1 : 1;
            }
        }

        return new CatchUpSection(
            matches, skipped, turns, compensated, Statistics.Wilson(compensated, turns), revealTriggers, pickTriggers, finalRanks);
    }

    // ---------- 收敛 ----------

    private static ConvergenceSection Convergence(List<MatchLog> logs)
    {
        var reasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (MatchLog log in logs)
        {
            string r = log.Result!.Reason;
            reasons[r] = reasons.TryGetValue(r, out int n) ? n + 1 : 1;
        }

        // round-cap D5：不收敛口径 = 终局原因为规则级 MajorRoundLimit 的局占比。
        int capped = logs.Count(l => l.Result!.Reason == nameof(EndReason.MajorRoundLimit));
        return new ConvergenceSection(
            logs.Count - capped,
            capped,
            Statistics.Wilson(capped, logs.Count),
            reasons,
            Statistics.Mean(logs.Where(l => l.Result!.Converged).Select(l => (double)l.Result!.MajorRound)),
            Statistics.Mean(logs.Select(l => (double)l.Result!.MajorRound)),
            Statistics.Mean(logs.Select(l => (double)l.Result!.TurnCount)));
    }

    // ---------- 势力碾压 ----------

    private static DominanceSection Dominance(List<MatchLog> logs)
    {
        List<LogResult> wins = [.. logs.Select(l => l.Result!).Where(r => r.Reason == nameof(EndReason.PowerDominance))];
        var ratios = new List<double>();
        int undefined = 0;
        foreach (LogResult r in wins)
        {
            // 名次序列即 FinalStandings 的输出顺序：碾压获胜者第 1，其后第一项即第 2 名。
            List<StandingEntry> ordered = [.. r.Standings.OrderBy(s => s.Rank)];
            if (ordered.Count < 2 || ordered[1].Power == 0)
            {
                undefined++;
                continue;
            }

            ratios.Add((double)ordered[0].Power / ordered[1].Power);
        }

        return new DominanceSection(
            logs.Count,
            wins.Count,
            Statistics.Wilson(wins.Count, logs.Count),
            Statistics.Mean(wins.Select(r => (double)r.MajorRound)),
            Statistics.Mean(ratios),
            ratios.Count,
            undefined);
    }

    // ---------- §16 ----------

    private static TargetsSection Targets(List<MatchLog> logs, AnalysisOptions options)
    {
        var phase1 = new SortedDictionary<int, int>();
        var phase2 = new SortedDictionary<int, int>();
        var phase3 = new SortedDictionary<int, int>();
        var powerByRound = new SortedDictionary<int, List<double>>();
        var maxGroupByRound = new SortedDictionary<int, long>();
        var firstConflict = new SortedDictionary<int, int>();
        int noConflict = 0;
        var endRounds = new SortedDictionary<int, int>();
        long totalTurns = 0;
        long totalRounds = 0;
        var roundMs = new List<double>();
        var matchMs = new List<double>();

        foreach (MatchLog log in logs)
        {
            int? conflictRound = null;
            var lastOfRound = new Dictionary<int, TurnSnapshot>();
            foreach (TurnSnapshot turn in log.Turns)
            {
                if (turn.DeployLimit > 0)
                {
                    SortedDictionary<int, int> bucket = turn.MajorRound <= 3 ? phase1 : turn.MajorRound <= 6 ? phase2 : phase3;
                    bucket[turn.DeployLimit] = bucket.TryGetValue(turn.DeployLimit, out int n) ? n + 1 : 1;
                }

                if (conflictRound is null && turn.Captures.Count > 0)
                {
                    conflictRound = turn.MajorRound;
                }

                lastOfRound[turn.MajorRound] = turn;
            }

            foreach ((int round, TurnSnapshot snapshot) in lastOfRound)
            {
                if (!powerByRound.TryGetValue(round, out List<double>? list))
                {
                    powerByRound[round] = list = [];
                }

                foreach (PlayerEntry p in snapshot.PlayersState.Where(p => p.Status == "Active"))
                {
                    list.Add(p.Total);
                    long maxGroup = p.Groups.Count == 0 ? 0 : p.Groups.Max(g => g.Power);
                    maxGroupByRound[round] = Math.Max(maxGroupByRound.TryGetValue(round, out long m) ? m : 0, maxGroup);
                }
            }

            if (conflictRound is { } c)
            {
                firstConflict[c] = firstConflict.TryGetValue(c, out int n) ? n + 1 : 1;
            }
            else
            {
                noConflict++;
            }

            LogResult r = log.Result!;
            if (r.Converged)
            {
                endRounds[r.MajorRound] = endRounds.TryGetValue(r.MajorRound, out int n) ? n + 1 : 1;
            }

            totalTurns += r.TurnCount;
            totalRounds += r.MajorRound;
            if (r.MajorRoundMs is { } ms)
            {
                roundMs.AddRange(ms.Select(v => (double)v));
            }

            if (r.TotalMs is { } total)
            {
                matchMs.Add(total);
            }
        }

        List<(int, double, long, int)> curve = [.. powerByRound.Select(kv =>
            (kv.Key, Statistics.Mean(kv.Value), maxGroupByRound.TryGetValue(kv.Key, out long m) ? m : 0, kv.Value.Count))];
        double opening = Statistics.Mean(powerByRound.Where(kv => kv.Key <= 2).SelectMany(kv => kv.Value));
        double mid = Statistics.Mean(powerByRound.Where(kv => kv.Key is >= 4 and <= 6).SelectMany(kv => kv.Value));
        double conflictMean = Statistics.Mean(firstConflict.SelectMany(kv => Enumerable.Repeat((double)kv.Key, kv.Value)));
        double endMean = Statistics.Mean(endRounds.SelectMany(kv => Enumerable.Repeat((double)kv.Key, kv.Value)));
        LeaderSection leader = Leader(logs, options);

        return new TargetsSection(
            phase1, phase2, phase3,
            Statistics.Assess(Median(phase1), 3, 3),
            Statistics.Assess(Median(phase2), 3, 5),
            Statistics.Assess(Median(phase3), 5, 8),
            curve,
            Statistics.Assess(opening, 1, 99),
            Statistics.Assess(mid, 20, 150),
            firstConflict, noConflict,
            Statistics.Assess(conflictMean, 4, 5, " 大回合"),
            Statistics.Unmeasurable(0, 3, " 分钟"),
            totalRounds == 0 ? double.NaN : (double)totalTurns / totalRounds,
            Statistics.Mean(roundMs),
            endRounds,
            Statistics.Assess(endMean, 7, 10, " 大回合"),
            Statistics.Unmeasurable(20, 30, " 分钟"),
            Statistics.Mean(matchMs),
            Statistics.Assess(leader.AnyOfGroupWins.IsEmpty ? double.NaN : leader.AnyOfGroupWins.Value * 100, 0, 50, "%"));
    }

    private static double Median(SortedDictionary<int, int> histogram) =>
        Statistics.Median(histogram.SelectMany(kv => Enumerable.Repeat((double)kv.Key, kv.Value)));

    // ---------- 领先者（§16.6 / §17.1，裁决 3） ----------

    /// <summary>第 3 大回合结束时势力名次为 1 的玩家（并列则多人）。没有第 3 大回合结束事件的局返回空。</summary>
    internal static List<int> LeadersAtRound3(MatchLog log)
    {
        LogEvent? e = log.Events.FirstOrDefault(ev => ev.Type == LogEventType.MajorRoundEnded && ev.MajorRound == 3);
        if (e?.Values is null)
        {
            return [];
        }

        return [.. log.Header.Players.Where(p => e.Values.TryGetValue($"P{p}.Rank", out long rank) && rank == 1)];
    }

    private static LeaderSection Leader(List<MatchLog> logs, AnalysisOptions options)
    {
        int samples = 0;
        int tied = 0;
        int anyWins = 0;
        int specificSamples = 0;
        int specificWins = 0;
        int nonLeaderSamples = 0;
        int nonLeaderWins = 0;
        foreach (MatchLog log in logs)
        {
            List<int> leaders = LeadersAtRound3(log);
            if (leaders.Count == 0)
            {
                continue;
            }

            samples++;
            if (leaders.Count > 1)
            {
                tied++;
            }

            List<int> winners = log.Result!.Winners;
            if (leaders.Any(winners.Contains))
            {
                anyWins++;
            }

            foreach (int leader in leaders)
            {
                specificSamples++;
                if (winners.Contains(leader))
                {
                    specificWins++;
                }
            }

            foreach (int p in log.Header.Players.Where(p => !leaders.Contains(p)))
            {
                nonLeaderSamples++;
                if (winners.Contains(p))
                {
                    nonLeaderWins++;
                }
            }
        }

        int players = logs.Count == 0 ? 0 : logs[0].Header.Players.Count;
        return new LeaderSection(
            samples, tied,
            Statistics.Wilson(anyWins, samples),
            Statistics.Wilson(specificWins, specificSamples),
            Statistics.Wilson(nonLeaderWins, nonLeaderSamples),
            players == 0 ? double.NaN : 1.0 / players,
            samples >= options.RequiredMatches);
    }

    // ---------- §17.2 先手滚雪球 ----------

    private static SnowballSection Snowball(List<MatchLog> logs)
    {
        var byPosition = new SortedDictionary<int, (int Improved, int Same, int Worse)>();
        int firstSamples = 0;
        int firstStays = 0;
        var finalByRound3 = new SortedDictionary<int, List<double>>();
        foreach (MatchLog log in logs)
        {
            List<LogEvent> ends = [.. log.Events.Where(e => e.Type == LogEventType.MajorRoundEnded && e.Values is not null).OrderBy(e => e.MajorRound)];
            for (int i = 0; i + 1 < ends.Count; i++)
            {
                Dictionary<string, long> now = ends[i].Values!;
                Dictionary<string, long> next = ends[i + 1].Values!;
                foreach (int p in log.Header.Players)
                {
                    if (!now.TryGetValue($"P{p}.Next", out long position) || !now.TryGetValue($"P{p}.Rank", out long rank)
                        || !next.TryGetValue($"P{p}.Rank", out long nextRank))
                    {
                        continue;
                    }

                    (int improved, int same, int worse) = byPosition.TryGetValue((int)position, out var t) ? t : (0, 0, 0);
                    if (nextRank < rank)
                    {
                        improved++;
                    }
                    else if (nextRank == rank)
                    {
                        same++;
                    }
                    else
                    {
                        worse++;
                    }

                    byPosition[(int)position] = (improved, same, worse);
                    if (position == 0)
                    {
                        firstSamples++;
                        if (next.TryGetValue($"P{p}.Next", out long nextPosition) && nextPosition == 0)
                        {
                            firstStays++;
                        }
                    }
                }
            }

            LogEvent? third = ends.FirstOrDefault(e => e.MajorRound == 3);
            if (third is not null)
            {
                foreach (int p in log.Header.Players)
                {
                    if (third.Values!.TryGetValue($"P{p}.Next", out long position))
                    {
                        StandingEntry? standing = log.Result!.Standings.FirstOrDefault(s => s.Player == p);
                        if (standing is not null)
                        {
                            if (!finalByRound3.TryGetValue((int)position, out List<double>? list))
                            {
                                finalByRound3[(int)position] = list = [];
                            }

                            list.Add(standing.Rank);
                        }
                    }
                }
            }
        }

        return new SnowballSection(
            [.. byPosition.Select(kv => (kv.Key, kv.Value.Improved, kv.Value.Same, kv.Value.Worse))],
            Statistics.Wilson(firstStays, firstSamples),
            [.. finalByRound3.Select(kv => (kv.Key, Statistics.Mean(kv.Value), kv.Value.Count))]);
    }

    // ---------- §17.3 棋子与信物 ----------

    private static SelectionSection Selection(List<MatchLog> logs)
    {
        var offered = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var picked = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var pickerSamples = new SortedDictionary<string, (int Wins, int Samples)>(StringComparer.Ordinal);
        var relicCount = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var relicRevealed = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var relicControlledTurns = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var controllerSamples = new SortedDictionary<string, (int Wins, int Samples)>(StringComparer.Ordinal);
        foreach (string type in Enum.GetNames<Core.Board.PieceType>())
        {
            offered[type] = 0;
            picked[type] = 0;
            pickerSamples[type] = (0, 0);
        }

        foreach (string type in Enum.GetNames<RelicType>())
        {
            relicCount[type] = 0;
            relicRevealed[type] = 0;
            relicControlledTurns[type] = 0;
            controllerSamples[type] = (0, 0);
        }

        foreach (MatchLog log in logs)
        {
            List<int> winners = log.Result!.Winners;
            var pickedBy = new Dictionary<int, HashSet<string>>();
            foreach (LogEvent e in log.Events.Where(e => e.Type == LogEventType.Recruit && e.Player is not null))
            {
                foreach (string type in Field(e.Detail, "candidates"))
                {
                    offered[type] = offered.TryGetValue(type, out int n) ? n + 1 : 1;
                }

                foreach (string type in Field(e.Detail, "picks"))
                {
                    picked[type] = picked.TryGetValue(type, out int n) ? n + 1 : 1;
                    if (!pickedBy.TryGetValue(e.Player!.Value, out HashSet<string>? set))
                    {
                        pickedBy[e.Player.Value] = set = [];
                    }

                    set.Add(type);
                }
            }

            foreach ((int player, HashSet<string> types) in pickedBy)
            {
                foreach (string type in types)
                {
                    (int wins, int samples) = pickerSamples.TryGetValue(type, out var t) ? t : (0, 0);
                    pickerSamples[type] = (wins + (winners.Contains(player) ? 1 : 0), samples + 1);
                }
            }

            var typeOf = log.Header.Relics.ToDictionary(r => r.Coord, r => r.Type, StringComparer.Ordinal);
            foreach (RelicEntry relic in log.Header.Relics)
            {
                relicCount[relic.Type] = relicCount.TryGetValue(relic.Type, out int n) ? n + 1 : 1;
            }

            foreach (RelicRevealEntry reveal in log.Result.RelicReveals.Where(r => r.RevealedInMajorRound is not null))
            {
                string type = typeOf[reveal.Coord];
                relicRevealed[type] = relicRevealed.TryGetValue(type, out int n) ? n + 1 : 1;
            }

            var controlledBy = new Dictionary<int, HashSet<string>>();
            foreach (TurnSnapshot turn in log.Turns)
            {
                foreach (RelicStateEntry state in turn.Relics.Where(r => r.Control == nameof(RelicControlKind.Controlled) && r.Holder is not null))
                {
                    string type = typeOf[state.Coord];
                    relicControlledTurns[type] = relicControlledTurns.TryGetValue(type, out int n) ? n + 1 : 1;
                    if (!controlledBy.TryGetValue(state.Holder!.Value, out HashSet<string>? set))
                    {
                        controlledBy[state.Holder.Value] = set = [];
                    }

                    set.Add(type);
                }
            }

            foreach ((int player, HashSet<string> types) in controlledBy)
            {
                foreach (string type in types)
                {
                    (int wins, int samples) = controllerSamples.TryGetValue(type, out var t) ? t : (0, 0);
                    controllerSamples[type] = (wins + (winners.Contains(player) ? 1 : 0), samples + 1);
                }
            }
        }

        List<PieceStat> pieces = [.. offered.Keys.Select(type => new PieceStat(
            type, offered[type], picked[type],
            Statistics.Wilson(picked[type], offered[type]),
            Statistics.Wilson(pickerSamples[type].Wins, pickerSamples[type].Samples)))];
        List<RelicStat> relics = [.. relicCount.Keys.Select(type => new RelicStat(
            type, relicCount[type], relicRevealed[type], relicControlledTurns[type],
            relicCount[type] == 0 ? double.NaN : (double)relicControlledTurns[type] / relicCount[type],
            Statistics.Wilson(controllerSamples[type].Wins, controllerSamples[type].Samples)))];
        return new SelectionSection(pieces, relics);
    }

    /// <summary>从 <c>key=a,b key2=</c> 形式的文本取某字段的列表。</summary>
    internal static IEnumerable<string> Field(string detail, string key)
    {
        foreach (string token in detail.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.StartsWith(key + "=", StringComparison.Ordinal))
            {
                return token[(key.Length + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries);
            }
        }

        return [];
    }

    // ---------- §17.4 高倍率棋串 ----------

    private static MultiplierSection Multiplier(List<MatchLog> logs)
    {
        var distribution = new SortedDictionary<int, int>();
        var effective = new SortedDictionary<int, int>();
        var rounds = new List<double>();
        var powers = new List<double>();
        long max = 0;
        int destroyed = 0;
        int peaks = 0;
        foreach (PeakEntry peak in logs.Select(l => l.Result!).Where(r => r.Peak is not null).Select(r => r.Peak!))
        {
            peaks++;
            distribution[peak.MultiplierCount] = distribution.TryGetValue(peak.MultiplierCount, out int n) ? n + 1 : 1;
            effective[peak.EffectiveMultiplierCount] = effective.TryGetValue(peak.EffectiveMultiplierCount, out int e) ? e + 1 : 1;
            rounds.Add(peak.MajorRound);
            powers.Add(peak.Power);
            max = Math.Max(max, peak.Power);
        }

        foreach (LogResult r in logs.Select(l => l.Result!).Where(r => r.PeakDestroyed == true))
        {
            destroyed++;
        }

        return new MultiplierSection(peaks, distribution, effective, Statistics.Mean(rounds), Statistics.Mean(powers), max, Statistics.Wilson(destroyed, peaks));
    }

    // ---------- 各棋子势力占比（multiplier-rebalance 裁决 3） ----------

    private static PieceShareSection PieceShares(List<MatchLog> logs)
    {
        PieceType[] types = Enum.GetValues<PieceType>();
        var stones = types.ToDictionary(t => t, _ => 0L);
        var power = types.ToDictionary(t => t, _ => 0L);
        int matches = 0;
        int skipped = 0;
        foreach (MatchLog log in logs)
        {
            if (log.Turns.Count == 0)
            {
                skipped++;
                continue;
            }

            List<GroupEntry> groups = [.. log.Turns[^1].PlayersState
                .Where(p => p.Status == nameof(PlayerStatus.Active))
                .SelectMany(p => p.Groups)];
            if (groups.Any(g => g.PieceCounts is null))
            {
                skipped++;
                continue;
            }

            matches++;
            foreach (GroupEntry g in groups)
            {
                long amplified = new Siege.Core.Scoring.Multiplier(g.EffectiveMultiplierCount).Apply(g.Base) - g.Base;
                foreach (PieceType type in types)
                {
                    int count = g.PieceCounts!.TryGetValue(type.ToString(), out int n) ? n : 0;
                    if (count == 0)
                    {
                        continue;
                    }

                    stones[type] += count;
                    power[type] += (long)PieceEffects.BasePower(type) * count + type switch
                    {
                        PieceType.Line => g.LineBonus,
                        PieceType.Synergy => g.SynergyBonus,
                        PieceType.Multiplier => amplified,
                        _ => 0,
                    };
                }
            }
        }

        long totalStones = stones.Values.Sum();
        long totalPower = power.Values.Sum();
        List<PieceShare> pieces = [.. types.Select(t => new PieceShare(
            t.ToString(),
            stones[t],
            totalStones == 0 ? double.NaN : (double)stones[t] / totalStones,
            power[t],
            totalPower == 0 ? double.NaN : (double)power[t] / totalPower,
            stones[t] == 0 ? double.NaN : (double)power[t] / stones[t]))];
        return new PieceShareSection(matches, skipped, totalStones, totalPower, pieces);
    }

    // ---------- §17.5 出生区（裁决 8：收敛 / 未收敛分组） ----------

    private static BirthZoneSection BirthZones(List<MatchLog> logs)
    {
        int zones = logs.Count == 0 ? 0 : logs.Max(l => l.Header.Zones.Count == 0 ? 0 : l.Header.Zones.Max() + 1);
        double baseline = zones == 0 ? double.NaN : 1.0 / Math.Max(zones, logs[0].Header.Players.Count);
        List<MatchLog> converged = [.. logs.Where(l => l.Header.RelicsConverged)];
        List<MatchLog> notConverged = [.. logs.Where(l => !l.Header.RelicsConverged)];
        return new BirthZoneSection(
            baseline,
            ZoneStats(logs, zones, baseline),
            ZoneStats(converged, zones, baseline),
            ZoneStats(notConverged, zones, baseline),
            converged.Count,
            notConverged.Count);
    }

    private static List<ZoneStat> ZoneStats(List<MatchLog> logs, int zones, double baseline)
    {
        int[] wins = new int[zones];
        int[] samples = new int[zones];
        foreach (MatchLog log in logs)
        {
            for (int p = 0; p < log.Header.Zones.Count; p++)
            {
                int zone = log.Header.Zones[p];
                if (zone < 0 || zone >= zones)
                {
                    continue;
                }

                samples[zone]++;
                if (log.Result!.Winners.Contains(log.Header.Players[p]))
                {
                    wins[zone]++;
                }
            }
        }

        return [.. Enumerable.Range(0, zones).Select(z =>
        {
            Proportion rate = Statistics.Wilson(wins[z], samples[z]);
            return new ZoneStat(z, rate, rate.Excludes(baseline));
        })];
    }

    // ---------- §17.6 成长轴顺序 ----------

    /// <summary>某玩家在一局中四条成长轴的首次获取大回合（未获取为 <c>null</c>）：供给、部署、槽位、倍率。</summary>
    internal static int?[] AxisAcquisitionRounds(MatchLog log, int player)
    {
        int?[] rounds = new int?[4];
        foreach (TurnSnapshot turn in log.Turns)
        {
            if (turn.Player == player && turn.DeployLimit > 0)
            {
                if (rounds[0] is null && (turn.ShowCount > EffectSnapshot.BaseRevealCount || turn.FreePickCount > EffectSnapshot.BaseFreePickCount))
                {
                    rounds[0] = turn.MajorRound;
                }

                if (rounds[1] is null && turn.DeployLimit > EffectSnapshot.BaseDeployLimitFor(turn.MajorRound))
                {
                    rounds[1] = turn.MajorRound;
                }

                if (rounds[2] is null && turn.TypeSlots > EffectSnapshot.BaseTypeSlots)
                {
                    rounds[2] = turn.MajorRound;
                }
            }

            if (rounds[3] is null)
            {
                PlayerEntry? entry = turn.PlayersState.FirstOrDefault(p => p.Player == player);
                if (entry is not null && entry.Groups.Any(g => g.MultiplierCount > 0))
                {
                    rounds[3] = turn.MajorRound;
                }
            }
        }

        return rounds;
    }

    /// <summary>获取顺序文本：按大回合先后排列，同轮用 <c>=</c> 连接，未获取的轴不出现；一条都没有为 <c>无</c>。</summary>
    internal static string AxisSequence(int?[] rounds)
    {
        var groups = rounds
            .Select((r, i) => (Round: r, Axis: AxisNames[i]))
            .Where(t => t.Round is not null)
            .GroupBy(t => t.Round!.Value)
            .OrderBy(g => g.Key)
            .Select(g => string.Join("=", g.Select(t => t.Axis)));
        string text = string.Join(">", groups);
        return text.Length == 0 ? "无" : text;
    }

    private static GrowthAxisSection GrowthAxes(List<MatchLog> logs)
    {
        var sequences = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var first = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int samples = 0;
        foreach (MatchLog log in logs)
        {
            foreach (int winner in log.Result!.Winners)
            {
                samples++;
                int?[] rounds = AxisAcquisitionRounds(log, winner);
                string sequence = AxisSequence(rounds);
                sequences[sequence] = sequences.TryGetValue(sequence, out int n) ? n + 1 : 1;
                string head = sequence.Split('>')[0];
                first[head] = first.TryGetValue(head, out int m) ? m + 1 : 1;
            }
        }

        KeyValuePair<string, int> top = sequences.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).FirstOrDefault();
        double share = samples == 0 ? double.NaN : (double)top.Value / samples;
        return new GrowthAxisSection(samples, sequences, first, samples == 0 ? null : top.Key, share);
    }

    // ---------- §17.7 最小落子拖延 ----------

    private static StallingSection Stalling(List<MatchLog> logs)
    {
        int signal = 0;
        int total = 0;
        foreach (MatchLog log in logs)
        {
            for (int i = 0; i < log.Turns.Count; i++)
            {
                total++;
                TurnSnapshot turn = log.Turns[i];
                if (turn.Placements.Count != 1 || i == 0)
                {
                    continue;
                }

                long before = log.Turns[i - 1].PlayersState.First(p => p.Player == turn.Player).Total;
                long after = turn.PlayersState.First(p => p.Player == turn.Player).Total;
                if (before == after)
                {
                    signal++;
                }
            }
        }

        return new StallingSection(signal, total, Statistics.Wilson(signal, total));
    }

    // ---------- AI 决策质量旁证（design.md Risks / implement 8.7） ----------

    private static AiQualitySection AiQuality(List<MatchLog> logs)
    {
        int settled = 0;
        int captures = 0;
        int suicides = 0;
        int rejected = 0;
        int passed = 0;
        int turns = 0;
        foreach (MatchLog log in logs)
        {
            foreach (TurnSnapshot turn in log.Turns)
            {
                turns++;
                if (turn.Passed)
                {
                    passed++;
                }
                else
                {
                    settled++;
                    captures += turn.Captures.Count;
                }
            }

            foreach (LogEvent e in log.Events.Where(e => e.Type == LogEventType.Rejected))
            {
                rejected++;
                if (e.FailureKind == nameof(Core.Batch.BatchFailureKind.Suicide))
                {
                    suicides++;
                }
            }
        }

        Proportion suicideRate = Statistics.Wilson(suicides, settled + rejected);
        Proportion passRate = Statistics.Wilson(passed, turns);
        bool unreliable = (!suicideRate.IsEmpty && suicideRate.Value >= SuicideUnreliableThreshold)
            || (!passRate.IsEmpty && passRate.Value >= PassUnreliableThreshold);
        string verdict = unreliable
            ? "警告：AI 决策质量旁证显示近乎随机（自杀手尝试率或 Pass 率超过阈值），本报告的数值结论不可信，必须先提升 AI。"
            : "AI 决策质量旁证未触发近乎随机警告。";
        return new AiQualitySection(
            settled, captures, settled == 0 ? double.NaN : (double)captures / settled,
            suicides, suicideRate, passRate, unreliable, verdict);
    }
}
