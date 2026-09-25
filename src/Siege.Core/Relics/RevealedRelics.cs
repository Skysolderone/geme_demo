using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Relics;

/// <summary>
/// "已揭示的公开信物内容"（坐标 → 类型）的<b>唯一</b>投影（more-pieces-relics D3）：只取公开状态里已揭示且带内容的信物。
/// 势力计算的"已知信物内容"输入里，预演（<c>BatchPreviewBuilder</c>）、AI 评价（<c>BatchEvaluator</c>）与终端预演都传它——
/// 批次开始前的公开状态里没有本批将首次揭示的信物，于是"将揭示的计分信物不计入预演 / AI 评价"不需要另写判断。
/// 正式结算传真实内容（<see cref="RelicLedger.TrueContents"/>）；已结算盘面上受控信物必已揭示，两者结果相同。
/// </summary>
public static class RevealedRelics
{
    /// <summary>已揭示信物的公开内容，按坐标排序。未揭示的一律不在其中（即使调用方手里的状态意外带了内容，也以揭示标记为准）。</summary>
    public static ImmutableSortedDictionary<Coord, RelicType> Of(IEnumerable<RelicPublicState> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        return states
            .Where(s => s.IsRevealed && s.Content is not null)
            .ToImmutableSortedDictionary(s => s.Coord, s => s.Content!.Value.Type);
    }
}
