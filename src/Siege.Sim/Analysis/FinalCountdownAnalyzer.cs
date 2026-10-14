using System.Numerics;
using Siege.Core.Match;
using Siege.Sim.Logging;

namespace Siege.Sim.Analysis;

// 本文件属于离线分析输出层：只读日志对象，不引用任何对局运行时对象。势力差用 BigInteger，不经浮点。

/// <summary>
/// 决赛倒计时统计（final-countdown 2.1，match-telemetry「决赛倒计时的记录」）：K 的分布、启动了倒计时的局与起点 R 的分布、进入倒计时的战场数、
/// 到期结算次数（与全部战场结算按原因的分布对照）、到期结算的大回合分布与到期时势力差（胜者 − 第二名）分布。等待者决赛胜率、等待大回合与决赛开局势力差
/// 沿用「等待者评估」一段，这里不重复。只统计首部为链接模式的有结果纳入局；没有这样的局时整段不出现。
/// </summary>
/// <param name="Matches">纳入的链接模式局数（含截断局）。</param>
/// <param name="Ks">K 的分布（首部缺项的旧日志按 0 计）。</param>
/// <param name="MissingK">首部缺 K 的旧日志局数（按 0 读，单列以免与显式 K = 0 混淆）。</param>
/// <param name="Started">启动了倒计时（出现过 <c>FinalCountdown</c> 事件）的局数。</param>
/// <param name="Starts">倒计时起点 R 的分布（每局一个样本）。</param>
/// <param name="CountdownFields">进入倒计时的战场总数（<c>FinalCountdown</c> 事件数）。</param>
/// <param name="SettlementReasons">全部战场结算按原因的分布。</param>
/// <param name="ExpiredRounds">到期结算所在大回合的分布。</param>
/// <param name="ExpiredGaps">到期时势力差（胜者 − 第二名，按结算记录），升序。</param>
/// <param name="Expired">到期结算明细（种子 / 大回合 / 战场 / 胜者 / 势力差）。</param>
public sealed record FinalCountdownSection(
    int Matches,
    SortedDictionary<int, int> Ks,
    int MissingK,
    int Started,
    SortedDictionary<int, int> Starts,
    int CountdownFields,
    SortedDictionary<string, int> SettlementReasons,
    SortedDictionary<int, int> ExpiredRounds,
    IReadOnlyList<BigInteger> ExpiredGaps,
    IReadOnlyList<ExpiredSettlementEntry> Expired)
{
    /// <summary>到期结算次数。</summary>
    public int ExpiredCount => ExpiredGaps.Count;
}

/// <summary>一次到期结算：种子（十六进制）、大回合、战场（出生棋盘编号 0 起）、胜者、期限、到期时势力差（胜者 − 第二名）。</summary>
public sealed record ExpiredSettlementEntry(string Seed, int MajorRound, int Battlefield, int Winner, int? Deadline, BigInteger Gap);

/// <summary>决赛倒计时统计的唯一实现（见 <see cref="FinalCountdownSection"/>）。</summary>
public static class FinalCountdownAnalyzer
{
    /// <summary>统计纳入局中的链接模式局；没有链接模式局、或其中没有一局 K &gt; 0 时返回 <c>null</c>（K = 0 与旧日志的报告逐字节不变）。</summary>
    public static FinalCountdownSection? Analyze(IReadOnlyList<MatchLog> included)
    {
        ArgumentNullException.ThrowIfNull(included);
        List<MatchLog> logs = [.. included.Where(l => l.IsLinkMode && l.Result is not null)];
        if (!logs.Any(l => l.FinalCountdownRounds > 0))
        {
            return null;
        }

        var ks = new SortedDictionary<int, int>();
        var starts = new SortedDictionary<int, int>();
        var reasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var expiredRounds = new SortedDictionary<int, int>();
        var gaps = new List<BigInteger>();
        var expired = new List<ExpiredSettlementEntry>();
        int missingK = 0, started = 0, fields = 0;
        foreach (MatchLog log in logs)
        {
            Add(ks, log.FinalCountdownRounds);
            missingK += log.Header.Config.FinalCountdownRounds is null ? 1 : 0;

            List<FinalCountdownLogEntry> countdowns = [.. log.Events.Where(e => e.Countdown is not null).OrderBy(e => e.Seq).Select(e => e.Countdown!)];
            fields += countdowns.Count;
            if (countdowns.Count > 0)
            {
                started++;
                Add(starts, countdowns[0].Start);
            }

            foreach (SettlementLogEntry s in log.Events.Where(e => e.Settlement is not null).OrderBy(e => e.Seq).Select(e => e.Settlement!))
            {
                Add(reasons, s.Reason);
                if (s.Reason != nameof(SettlementReason.Expired))
                {
                    continue;
                }

                Add(expiredRounds, s.MajorRound);
                BigInteger winner = s.Entries.First(x => x.Player == s.Winner).Power;
                BigInteger second = s.Entries.Where(x => x.Player != s.Winner).Select(x => x.Power).DefaultIfEmpty(winner).Max();
                gaps.Add(winner - second);
                expired.Add(new ExpiredSettlementEntry(log.Header.Seed, s.MajorRound, s.Battlefield, s.Winner, s.Deadline, winner - second));
            }
        }

        gaps.Sort();
        return new FinalCountdownSection(logs.Count, ks, missingK, started, starts, fields, reasons, expiredRounds, gaps, expired);
    }

    private static void Add<TKey>(SortedDictionary<TKey, int> histogram, TKey key)
        where TKey : notnull =>
        histogram[key] = histogram.GetValueOrDefault(key) + 1;
}
