using Siege.Core.Match;
using Siege.Sim.Logging;

namespace Siege.Sim.Analysis;

// 本文件属于离线分析输出层：允许浮点（裁决 14）。只读日志对象，不引用任何对局运行时对象。

/// <summary>
/// 链接模式统计（board-link-mode tasks 3.5 / 规格 match-telemetry「对局日志的记录内容」：同区率、首轮配对分布、各阶段大回合数、退化链接率、等待时长；
/// 另按负责人要求列终局原因分布、出局原因计数、通道长度与决赛战场可落子格数（裁决 R3 / R4 的复核项）、过早 Pass 落败次数与 AI 小回合耗时）。
/// 只统计首部 <see cref="LogHeader.Mode"/> 为链接模式的纳入局；没有这样的局时整段不出现（旧日志的报告逐字节不变）。
/// </summary>
/// <param name="Matches">纳入的链接模式局数（含截断局）。</param>
/// <param name="Truncated">其中被小回合数截断的局数。</param>
/// <param name="SharedMatches">同区局数：锁定时有出生棋盘的旗数 ≥ 2（首部 <see cref="LogHeader.LockedFlagCounts"/>）。</param>
/// <param name="Layouts">开局布局分布：锁定旗数降序拼成 <c>1+1+1+1</c> / <c>2+1+1</c> / <c>2+2</c> / <c>3+1</c> / <c>4</c>。</param>
/// <param name="FirstRoundPairings">首轮配对分布：规范次序的配对（面向人的棋盘编号 1–5）+ 等待者；没有首轮配对的局记"无"。</param>
/// <param name="BuildRounds">构筑期大回合数的均值（= min(3, 最后一个大回合)）。</param>
/// <param name="FirstFightRounds">首轮交战大回合数的均值：第 4 大回合起到决赛开始前（或到局终）。</param>
/// <param name="FinalRounds">决赛大回合数的均值，只计到达决赛的局（<see cref="FinalMatches"/>）。</param>
/// <param name="FinalMatches">到达决赛（有决赛链接事件）的局数。</param>
/// <param name="Links">链接总次数。</param>
/// <param name="DegradedLinks">其中退化为直通道的次数。</param>
/// <param name="WaitingMean">每名玩家等待大回合数的均值（全部玩家，取最后一个快照）。</param>
/// <param name="WaitingMeanOfWaiters">等待过（&gt; 0）的玩家的均值。</param>
/// <param name="WaitingMax">等待大回合数的最大值。</param>
/// <param name="Waiters">等待过的玩家人次。</param>
/// <param name="EndReasons">终局原因分布（含 <c>turn_limit</c>）。</param>
/// <param name="BattlefieldDefeats">战场落败出局人次。</param>
/// <param name="PowerZeroEliminations">势力归零出局人次。</param>
/// <param name="CorridorLengths">全部新增通道的长度分布。</param>
/// <param name="FinalBattlefieldCells">决赛战场（决赛链接后合并战场）的可落子格数，每局一个样本。</param>
/// <param name="EndPlayableCells">每局最后一个快照的全图可落子格（<c>Life.PlayableCells</c>，链接后的分母口径）的均值。</param>
/// <param name="AllPassedDefeats">在"整轮 Pass"结算中落败的人次。</param>
/// <param name="PrematurePassDefeats">过早 Pass 落败：在整轮 Pass 结算中落败，且结算那一大回合本人的小回合是 Pass、Pass 后公开手牌类型仍非空（手里还有子）。手里有子不等于有合法可落的格（未逐格核对禁入格与部署上限），故为上界。</param>
/// <param name="TurnMedianMs">单个小回合耗时（含 AI 决策）的中位数，毫秒。</param>
/// <param name="TurnMaxMs">单个小回合耗时的最大值，毫秒。</param>
/// <param name="TotalMatchMs">全部纳入局的整局耗时之和，毫秒。</param>
public sealed record LinkModeSection(
    int Matches,
    int Truncated,
    int SharedMatches,
    SortedDictionary<string, int> Layouts,
    SortedDictionary<string, int> FirstRoundPairings,
    double BuildRounds,
    double FirstFightRounds,
    double FinalRounds,
    int FinalMatches,
    int Links,
    int DegradedLinks,
    double WaitingMean,
    double WaitingMeanOfWaiters,
    int WaitingMax,
    int Waiters,
    SortedDictionary<string, int> EndReasons,
    int BattlefieldDefeats,
    int PowerZeroEliminations,
    SortedDictionary<int, int> CorridorLengths,
    SortedDictionary<int, int> FinalBattlefieldCells,
    double EndPlayableCells,
    int AllPassedDefeats,
    int PrematurePassDefeats,
    double TurnMedianMs,
    long TurnMaxMs,
    long TotalMatchMs)
{
    public double SharedShare => Matches == 0 ? double.NaN : (double)SharedMatches / Matches;

    public double DegradedShare => Links == 0 ? double.NaN : (double)DegradedLinks / Links;
}

/// <summary>链接模式统计的唯一实现（见 <see cref="LinkModeSection"/>）。</summary>
public static class LinkModeAnalyzer
{
    /// <summary>构筑期大回合数（battlefield「阶段推进」：第 1–3 大回合）。</summary>
    public const int BuildRounds = 3;

    /// <summary>统计纳入局中的链接模式局；一局都没有时返回 <c>null</c>。</summary>
    public static LinkModeSection? Analyze(IReadOnlyList<MatchLog> included)
    {
        ArgumentNullException.ThrowIfNull(included);
        List<MatchLog> logs = [.. included.Where(l => l.IsLinkMode && l.Result is not null)];
        if (logs.Count == 0)
        {
            return null;
        }

        var layouts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var pairings = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var endReasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var corridors = new SortedDictionary<int, int>();
        var finalCells = new SortedDictionary<int, int>();
        var build = new List<double>();
        var firstFight = new List<double>();
        var final = new List<double>();
        var waiting = new List<double>();
        var turnMs = new List<double>();
        var endPlayable = new List<double>();
        int shared = 0, links = 0, degraded = 0, defeats = 0, powerZero = 0, allPassedDefeats = 0, premature = 0, truncated = 0;
        long turnMax = 0, totalMs = 0;

        foreach (MatchLog log in logs)
        {
            LogResult result = log.Result!;
            truncated += result.Truncated ? 1 : 0;
            Add(endReasons, result.Reason);
            totalMs += result.TotalMs ?? 0;

            List<int> flags = log.Header.LockedFlagCounts ?? [];
            shared += flags.Any(f => f >= 2) ? 1 : 0;
            Add(layouts, string.Join("+", flags.Where(f => f > 0).OrderDescending()));

            List<PairingLogEntry> events = [.. log.Events.Where(e => e.Pairing is not null).OrderBy(e => e.Seq).Select(e => e.Pairing!)];
            PairingLogEntry? firstRound = events.FirstOrDefault(p => p.Kind == nameof(PairingKind.FirstRound));
            PairingLogEntry? finalPairing = events.FirstOrDefault(p => p.Kind == nameof(PairingKind.Final));
            Add(pairings, firstRound is null ? "无" : PairingText(firstRound));

            // 阶段：构筑 = 第 1–3 大回合；首轮交战 = 第 4 大回合起到决赛开始前；决赛 = 决赛链接事件记的大回合（链接后的第一个大回合）到局终。
            int last = result.MajorRound;
            build.Add(Math.Min(BuildRounds, last));
            firstFight.Add(Math.Max(0, Math.Min(last, (finalPairing?.MajorRound ?? last + 1) - 1) - BuildRounds));
            if (finalPairing is not null)
            {
                final.Add(Math.Max(0, last - finalPairing.MajorRound + 1));
            }

            foreach (LinkLogEntry link in log.Events.Where(e => e.Link is not null).Select(e => e.Link!))
            {
                links++;
                degraded += link.Degraded ? 1 : 0;
                foreach (int length in link.CorridorLengths)
                {
                    Add(corridors, length);
                }

                if (finalPairing is not null && link.PairingSequence == finalPairing.Sequence)
                {
                    Add(finalCells, link.BattlefieldCells);
                }
            }

            if (log.Turns.Count > 0)
            {
                TurnSnapshot lastTurn = log.Turns[^1];
                waiting.AddRange(lastTurn.PlayersState.Select(p => (double)(p.WaitingRounds ?? 0)));
                if (lastTurn.Life is { } life)
                {
                    endPlayable.Add(life.PlayableCells);
                }
            }

            foreach (LogEvent e in log.Events.Where(e => e.Type == LogEventType.PlayerEliminated))
            {
                defeats += e.Reason == nameof(EliminationReason.BattlefieldDefeat) ? 1 : 0;
                powerZero += e.Reason == nameof(EliminationReason.PowerZero) ? 1 : 0;
            }

            (int allPassed, int early) = PassDefeats(log);
            allPassedDefeats += allPassed;
            premature += early;

            foreach (TurnSnapshot turn in log.Turns.Where(t => t.ElapsedMs is not null))
            {
                turnMs.Add(turn.ElapsedMs!.Value);
                turnMax = Math.Max(turnMax, turn.ElapsedMs.Value);
            }
        }

        double[] waiters = [.. waiting.Where(w => w > 0)];
        return new LinkModeSection(
            logs.Count, truncated, shared, layouts, pairings,
            Statistics.Mean(build), Statistics.Mean(firstFight), Statistics.Mean(final), final.Count,
            links, degraded,
            Statistics.Mean(waiting), Statistics.Mean(waiters), waiting.Count == 0 ? 0 : (int)waiting.Max(), waiters.Length,
            endReasons, defeats, powerZero, corridors, finalCells, Statistics.Mean(endPlayable),
            allPassedDefeats, premature, Statistics.Median(turnMs), turnMax, totalMs);
    }

    /// <summary>首轮配对的文本：规范次序的配对（棋盘编号 1–5），等待者另记。</summary>
    internal static string PairingText(PairingLogEntry pairing) =>
        string.Join(" ", pairing.Pairs.Select(p => $"{p[0] + 1}-{p[1] + 1}")) + (pairing.Waiter is { } w ? $" 等{w + 1}" : string.Empty);

    /// <summary>
    /// 一局里"整轮 Pass"结算的落败人次，以及其中的过早 Pass 落败人次：结算那一大回合（结算事件所在小回合的快照大回合）里，
    /// 该落败者自己的小回合是 Pass，且 Pass 后快照里本人的公开手牌类型仍非空。"仍有可落的空格"由结算原因保证（整轮 Pass 而非填满）。
    /// </summary>
    internal static (int AllPassed, int Premature) PassDefeats(MatchLog log)
    {
        int allPassed = 0, premature = 0;
        foreach (LogEvent e in log.Events.Where(e => e.Settlement is { Reason: nameof(SettlementReason.AllPassed) }))
        {
            int round = e.Settlement!.MajorRound;
            foreach (SettlementRankEntry loser in e.Settlement.Entries.Where(x => x.Player != e.Settlement.Winner))
            {
                allPassed++;
                TurnSnapshot? own = log.Turns.LastOrDefault(t => t.MajorRound == round && t.Player == loser.Player);
                if (own is { Passed: true } && own.PlayersState.FirstOrDefault(p => p.Player == loser.Player) is { HandTypes.Count: > 0 })
                {
                    premature++;
                }
            }
        }

        return (allPassed, premature);
    }

    private static void Add<TKey>(SortedDictionary<TKey, int> histogram, TKey key)
        where TKey : notnull =>
        histogram[key] = histogram.GetValueOrDefault(key) + 1;
}
