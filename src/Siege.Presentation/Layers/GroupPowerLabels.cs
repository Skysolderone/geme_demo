using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Presentation.Style;
using Siege.Presentation.Text;

namespace Siege.Presentation.Layers;

/// <summary>常驻标注放在锚格的哪个角（board-labels D4）。引擎层按它取偏移，不自己判断。</summary>
public enum LabelCorner
{
    /// <summary>右前角（默认）。</summary>
    FrontRight,

    /// <summary>左前角：锚格在最右一列时用，避开右边的行数字。</summary>
    FrontLeft,
}

/// <summary>
/// 棋串军势常驻标注的一条：标在哪一格、归谁、写什么、第几档（tiered-number-show D6：该棋串军势的数值档位，引擎层按它取字号）、
/// 放在锚格的哪个角（board-labels D4）。
/// </summary>
public sealed record GroupPowerLabel(Coord Coord, PlayerId Owner, string Text, int Tier, LabelCorner Corner);

/// <summary>
/// 棋串军势常驻标注（tactical-layers「棋串军势常驻标注」）：默认棋盘上每条棋串标一处军势。数值取势力层内容（与势力层同一份），
/// 这里只决定"标在哪一格、写什么、第几档"；引擎层照着画，不算军势、不算重心也不取档。
/// </summary>
public static class GroupPowerLabels
{
    /// <summary>
    /// 每条棋串一条标注：落在 <see cref="AnchorOf"/> 给出的那枚棋子上；文案是军势的缩写（<see cref="Labels.CompactPower"/>，与势力栏同一规则）；
    /// 档位取军势的数值档位（<see cref="NumberTier.Of"/>，与军势揭示、势力栏同一张表）；
    /// 角位按 <see cref="CornerOf"/>（锚格在最右一列时放左前角，避开右边的行数字，board-labels D4）。
    /// </summary>
    /// <param name="content">势力层内容。</param>
    /// <param name="boardWidth">棋盘宽度（列数）：判断锚格是否在最右一列。</param>
    public static ImmutableArray<GroupPowerLabel> Of(PowerLayerContent content, int boardWidth)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(boardWidth);
        return
        [
            .. content.Groups.Where(g => !g.Stones.IsEmpty)
                .Select(g =>
                {
                    Coord anchor = AnchorOf(g.Stones);
                    return new GroupPowerLabel(anchor, g.Owner, Labels.CompactPower(g.Power.Power), NumberTier.Of(g.Power.Power), CornerOf(anchor, boardWidth));
                }),
        ];
    }

    /// <summary>标注的角位：锚格在最右一列（<c>x == 宽 − 1</c>）时左前角，否则右前角（board-labels D4）。</summary>
    public static LabelCorner CornerOf(Coord anchor, int boardWidth) =>
        anchor.X == boardWidth - 1 ? LabelCorner.FrontLeft : LabelCorner.FrontRight;

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
