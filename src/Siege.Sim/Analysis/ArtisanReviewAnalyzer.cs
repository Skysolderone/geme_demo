using Siege.Core.Board;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Sim.Analysis;

// 本文件属于离线分析输出层：允许浮点（裁决 14）。

/// <summary>一次改造诊断及其所属玩家锁定的出生棋盘主题（首部棋盘表；没有为 <c>null</c>）。</summary>
public sealed record ArtisanReviewItem(ArtisanReviewEntry Entry, BoardTheme? Theme);

/// <summary>
/// AI 改造诊断（ai-terrain-review 1.2，规格 match-telemetry「AI 改造与主题诊断」第 1 条）的汇总：每次 AI 匠人可改造的时刻（手里有匠人、部署上限 &gt; 0 的 AI 部署），
/// 按日志回放到该时刻、用其自身 AI 配置复盘三类改造。
/// </summary>
/// <param name="Logs">回放的链接模式局数（有结果、有小回合的纳入局）。</param>
/// <param name="SkippedLogs">未能复盘的局：原因 → 局数。</param>
/// <param name="Items">逐次明细（按种子、小回合序号）。</param>
public sealed record ArtisanReviewSection(int Logs, SortedDictionary<string, int> SkippedLogs, IReadOnlyList<ArtisanReviewItem> Items);

/// <summary>AI 改造诊断的唯一实现（见 <see cref="ArtisanReviewSection"/>）。</summary>
public static class ArtisanReviewAnalyzer
{
    /// <summary>
    /// 对纳入局中的每局链接模式日志按日志回放诊断（<see cref="ArtisanReplay.Review"/>）。没有可回放的链接模式局时返回 <c>null</c>（报告整段不出现）。
    /// 回放要重跑对局并在每个匠人时刻多做一遍完整枚举，耗时约为跑局的一到两倍，所以只由 <c>analyze --review-artisan</c> 开启（<see cref="AnalysisOptions.ReviewArtisan"/>）。
    /// </summary>
    public static ArtisanReviewSection? Review(IReadOnlyList<MatchLog> included)
    {
        ArgumentNullException.ThrowIfNull(included);
        List<MatchLog> logs = [.. included.Where(l => l.IsLinkMode && l.Result is not null && l.Turns.Count > 0)];
        if (logs.Count == 0)
        {
            return null;
        }

        var skipped = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var items = new List<ArtisanReviewItem>();
        foreach (MatchLog log in logs.OrderBy(l => l.Seed))
        {
            ArtisanReplayResult result = ArtisanReplay.Review(log);
            if (result.Skipped is { } reason)
            {
                skipped[reason] = skipped.GetValueOrDefault(reason) + 1;
                continue;
            }

            List<BoardEntry> boards = log.Header.Boards ?? [];
            foreach (ArtisanReviewEntry entry in result.Entries)
            {
                int player = entry.Review.Player.Value;
                int zone = player < log.Header.Zones.Count ? log.Header.Zones[player] : -1;
                items.Add(new ArtisanReviewItem(entry, zone >= 0 && zone < boards.Count ? boards[zone].Theme : null));
            }
        }

        return new ArtisanReviewSection(logs.Count, skipped, items);
    }
}
