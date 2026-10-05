using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Presentation.Style;
using Siege.Presentation.Text;

namespace Siege.Presentation.Layers;

/// <summary>棋串军势常驻标注的一条：标在哪一格、归谁、写什么、第几档（tiered-number-show D6：该棋串军势的数值档位，引擎层按它取字号）。</summary>
public sealed record GroupPowerLabel(Coord Coord, PlayerId Owner, string Text, int Tier);

/// <summary>
/// 棋串军势常驻标注（tactical-layers「棋串军势常驻标注」）：默认棋盘上每条棋串标一处军势。数值取势力层内容（与势力层同一份），
/// 这里只决定"标在哪一格、写什么、第几档"；引擎层照着画，不算军势、不算重心也不取档。
/// </summary>
public static class GroupPowerLabels
{
    /// <summary>
    /// 每条棋串一条标注：落在 <see cref="AnchorOf"/> 给出的那枚棋子上；文案是军势的缩写（<see cref="Labels.CompactPower"/>，与势力栏同一规则）；
    /// 档位取军势的数值档位（<see cref="NumberTier.Of"/>，与军势揭示、势力栏同一张表）。
    /// </summary>
    public static ImmutableArray<GroupPowerLabel> Of(PowerLayerContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return
        [
            .. content.Groups.Where(g => !g.Stones.IsEmpty)
                .Select(g => new GroupPowerLabel(AnchorOf(g.Stones), g.Owner, Labels.CompactPower(g.Power.Power), NumberTier.Of(g.Power.Power))),
        ];
    }

    /// <summary>
    /// 一条棋串的标注落点：离棋串重心最近的那枚棋子，并列取坐标序最小的。距离用整数比较：把坐标乘以枚数再与坐标和相减，避免浮点重心。
    /// 落点算法<b>只此一份</b>：常驻标注与军势揭示条目（tiered-number-show D2）共用，同一条棋串两处标在同一格。
    /// </summary>
    public static Coord AnchorOf(ImmutableArray<Coord> stones)
    {
        if (stones.IsDefaultOrEmpty)
        {
            throw new ArgumentException("棋串至少有一枚棋子。", nameof(stones));
        }

        long n = stones.Length;
        long sumX = stones.Sum(s => (long)s.X), sumY = stones.Sum(s => (long)s.Y);
        return stones
            .OrderBy(s => (((n * s.X) - sumX) * ((n * s.X) - sumX)) + (((n * s.Y) - sumY) * ((n * s.Y) - sumY)))
            .ThenBy(s => s)
            .First();
    }
}
