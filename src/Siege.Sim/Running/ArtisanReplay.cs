using System.Collections.Immutable;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Sim.Logging;

namespace Siege.Sim.Running;

/// <summary>
/// 一次 AI 匠人可改造时刻的诊断（ai-terrain-review 1.2）：手里有匠人、部署上限 &gt; 0 的 AI 部署。
/// </summary>
/// <param name="Seed">对局种子（日志首部）。</param>
/// <param name="Turn">小回合序号（从 1 起）。</param>
/// <param name="MajorRound">大回合。</param>
/// <param name="Review">按该 AI 自身配置在部署前复盘的改造诊断（<see cref="HeuristicTurnController.ReviewArtisanEdits"/>）。</param>
/// <param name="Choice">AI 实际确认的批次里的匠人改造类别（取自原日志该小回合的快照，见 <see cref="ArtisanReplay.ChoiceOf"/>）。</param>
public sealed record ArtisanReviewEntry(string Seed, int Turn, int MajorRound, ArtisanEditReview Review, ImmutableArray<ArtisanChoice> Choice);

/// <summary>一局的改造诊断：逐次明细；<paramref name="Skipped"/> 非空即整局未能复盘（原因同 <see cref="DefeatReplay"/>），此时明细为空。</summary>
public sealed record ArtisanReplayResult(string Seed, IReadOnlyList<ArtisanReviewEntry> Entries, string? Skipped);

/// <summary>
/// AI 改造诊断的回放驱动（ai-terrain-review 1.2，design D2"分析时回放"）：经 <see cref="LogReplay"/> 按日志回放到最后一个小回合，
/// 每次 AI 部署前若其手里有匠人且部署上限 &gt; 0，就在<b>影子批次</b>上调用改造诊断；实际选择取自原日志同一小回合的快照。
/// 诊断不碰正式暂放批次、不消费任何随机流，回放与原局逐小回合核对，分歧整局记为未能复盘。
/// </summary>
/// <remarks>本文件不出现浮点（跑局层惯例）。</remarks>
public static class ArtisanReplay
{
    /// <summary>回放一局并诊断全部匠人可改造时刻。日志没有小回合时不回放（明细为空）。</summary>
    public static ArtisanReplayResult Review(MatchLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        LogHeader header = log.Header;
        if (log.Turns.Count == 0)
        {
            return new ArtisanReplayResult(header.Seed, [], null);
        }

        var entries = new List<ArtisanReviewEntry>();
        Dictionary<int, TurnSnapshot> recorded = log.Turns.GroupBy(t => t.Turn).ToDictionary(g => g.Key, g => g.Last());
        string? failure = LogReplay.Run(log, (session, batch) =>
        {
            BatchContext context = batch.Context;
            if (context.DeployLimit <= 0 || context.Stock.GetValueOrDefault(TerrainEditRules.EditorType) <= 0
                || session.AiOf(context.Player) is not { } ai)
            {
                return null;
            }

            int turn = session.TurnCount + 1;
            if (!recorded.TryGetValue(turn, out TurnSnapshot? snapshot) || snapshot.Player != context.Player.Value)
            {
                return $"第 {turn} 小回合在原日志里找不到 {context.Player} 的快照";
            }

            // 影子批次：同一盘面与批次上下文，预演走同一个零副作用入口（与 DefeatReplay 相同），正式暂放批次一概不碰。
            MatchFlow match = session.Match;
            var shadow = new StagedBatch(batch.Board, context);
            ArtisanEditReview review = ai.ReviewArtisanEdits(shadow, () => BatchRehearsal.Rehearse(shadow.Board, shadow.Context, shadow.Placements, match.History));
            entries.Add(new ArtisanReviewEntry(header.Seed, turn, match.MajorRound, review, ChoiceOf(snapshot)));
            return null;
        });

        return failure is null ? new ArtisanReplayResult(header.Seed, entries, null) : new ArtisanReplayResult(header.Seed, [], failure);
    }

    /// <summary>
    /// 实际选择取自原日志该小回合的快照（已确认的批次，回放逐小回合核对过与之相同）：Pass / 无落子为 Pass；没有匠人为未落匠人；
    /// 否则每枚匠人按快照改造表里以它为落点的改造记类别，没有即不改造，按 <see cref="ArtisanChoice"/> 枚举序排列。
    /// </summary>
    internal static ImmutableArray<ArtisanChoice> ChoiceOf(TurnSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Passed || snapshot.Placements.Count == 0)
        {
            return [ArtisanChoice.Pass];
        }

        string suffix = ":" + nameof(PieceType.Artisan);
        List<string> artisans = [.. snapshot.Placements.Where(p => p.EndsWith(suffix, StringComparison.Ordinal)).Select(p => p[..^suffix.Length])];
        if (artisans.Count == 0)
        {
            return [ArtisanChoice.NoArtisan];
        }

        List<TerrainEditEntry> edits = snapshot.Edits ?? [];
        return [.. artisans
            .Select(cell => edits.FirstOrDefault(e => string.Equals(e.Artisan, cell, StringComparison.Ordinal)) is { } edit
                ? Enum.Parse<ArtisanChoice>(edit.Action)
                : ArtisanChoice.ArtisanNoEdit)
            .Order()];
    }
}
