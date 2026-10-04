using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Presentation.Text;

namespace Siege.Presentation.Layers;

/// <summary>棋串军势常驻标注的一条：标在哪一格、归谁、写什么。</summary>
public sealed record GroupPowerLabel(Coord Coord, PlayerId Owner, string Text);

/// <summary>
/// 棋串军势常驻标注（tactical-layers「棋串军势常驻标注」）：默认棋盘上每条棋串标一处军势。数值取势力层内容（与势力层同一份），
/// 这里只决定"标在哪一格、写什么"；引擎层照着画，不算军势也不算重心。
/// </summary>
public static class GroupPowerLabels
{
    /// <summary>
    /// 每条棋串一条标注：落在离棋串重心最近的那枚棋子上，并列取坐标序最小的；文案是军势的缩写（<see cref="Labels.CompactPower"/>，与势力栏同一规则）。
    /// 距离用整数比较：把坐标乘以枚数再与坐标和相减，避免浮点重心。
    /// </summary>
    public static ImmutableArray<GroupPowerLabel> Of(PowerLayerContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return [.. content.Groups.Where(g => !g.Stones.IsEmpty).Select(g => new GroupPowerLabel(Anchor(g.Stones), g.Owner, Labels.CompactPower(g.Power.Power)))];
    }

    private static Coord Anchor(ImmutableArray<Coord> stones)
    {
        long n = stones.Length;
        long sumX = stones.Sum(s => (long)s.X), sumY = stones.Sum(s => (long)s.Y);
        return stones
            .OrderBy(s => (((n * s.X) - sumX) * ((n * s.X) - sumX)) + (((n * s.Y) - sumY) * ((n * s.Y) - sumY)))
            .ThenBy(s => s)
            .First();
    }
}
