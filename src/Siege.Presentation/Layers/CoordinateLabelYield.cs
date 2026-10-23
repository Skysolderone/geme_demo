using System.Collections.Immutable;
using Siege.Presentation.Show;

namespace Siege.Presentation.Layers;

/// <summary>
/// 坐标标注的让位结果（board-labels D1）：远边列字母、左边行数字、右边行数字各一张表，下标 → 显现进度（‰）。
/// <b>不在表里的标注一律完全显示（1000‰）</b>；近边列字母永不让位，所以没有它的表。三张表都为空即全部完全显示。
/// </summary>
/// <param name="FarColumns">远边（<c>y = 高 − 1</c> 一侧）列字母：列下标 x → 显现进度。</param>
/// <param name="LeftRows">左边（<c>x = 0</c> 一侧）行数字：行下标 y → 显现进度。</param>
/// <param name="RightRows">右边（<c>x = 宽 − 1</c> 一侧）行数字：行下标 y → 显现进度。</param>
public sealed record CoordinateYieldView(
    ImmutableDictionary<int, int> FarColumns,
    ImmutableDictionary<int, int> LeftRows,
    ImmutableDictionary<int, int> RightRows)
{
    /// <summary>没有任何让位：全部完全显示。</summary>
    public static readonly CoordinateYieldView None = new(
        ImmutableDictionary<int, int>.Empty, ImmutableDictionary<int, int>.Empty, ImmutableDictionary<int, int>.Empty);

    /// <summary>是否没有任何让位。</summary>
    public bool IsEmpty => FarColumns.IsEmpty && LeftRows.IsEmpty && RightRows.IsEmpty;

    /// <summary>远边第 <paramref name="x"/> 列字母的显现进度（不在表里为 1000）。</summary>
    public int FarColumn(int x) => FarColumns.TryGetValue(x, out int p) ? p : CoordinateLabelYield.FullPermille;

    /// <summary>左边第 <paramref name="y"/> 行数字的显现进度（不在表里为 1000）。</summary>
    public int LeftRow(int y) => LeftRows.TryGetValue(y, out int p) ? p : CoordinateLabelYield.FullPermille;

    /// <summary>右边第 <paramref name="y"/> 行数字的显现进度（不在表里为 1000）。</summary>
    public int RightRow(int y) => RightRows.TryGetValue(y, out int p) ? p : CoordinateLabelYield.FullPermille;
}

/// <summary>
/// 坐标标注让位（visual-style-baseline「盘面标注的避让与对比」，board-labels D1）：结算演出期间，军势揭示条目从棋子头顶向屏幕上方（远边）升起，
/// 可能压到远边的列字母与左右两边的行数字；这里按演出遮罩里的揭示条目与棋盘尺寸纯函数地给出哪些标注让位、此刻显现多少。
/// 引擎层只按结果画透明度，不判断重叠。整数运算，不读时钟。
/// </summary>
/// <remarks>让位范围按格子估算（不按屏幕像素），常量是初值，按截图调时只改这里。</remarks>
public static class CoordinateLabelYield
{
    /// <summary>完全显示（‰）。</summary>
    public const int FullPermille = 1000;

    /// <summary>锚格离远边在这么多行之内（<c>y ≥ 高 − FarRowReach</c>）时，远边列字母让位。</summary>
    public const int FarRowReach = 3;

    /// <summary>远边让位的列：锚格所在列左右各这么多列（<c>|x − 锚格 x| ≤ ColumnSpan</c>）。</summary>
    public const int ColumnSpan = 2;

    /// <summary>锚格离左 / 右边在这么多列之内（左 <c>x &lt; SideColumnReach</c>，右 <c>x ≥ 宽 − SideColumnReach</c>）时，那一边的行数字让位。</summary>
    public const int SideColumnReach = 2;

    /// <summary>左右让位的行：锚格所在行及其上方（远边方向）这么多行（<c>锚格 y ≤ 行 ≤ 锚格 y + RowSpan</c>）。</summary>
    public const int RowSpan = 3;

    /// <summary>条目出现后淡出所用的毫秒数。</summary>
    public const int FadeMs = 150;

    /// <summary>让位到底时的显现进度（‰）：不完全消失，坐标位置仍隐约可辨。</summary>
    public const int YieldPermille = 150;

    /// <summary>
    /// 由演出遮罩推出坐标标注的让位结果。每个揭示条目按其锚格让出所在范围内的标注，显现进度按 <see cref="PermilleAt"/>（条目已显示的毫秒数）；
    /// 多个条目让同一个标注时取最小值。没有揭示条目（含演出结束、零时长模式）时三张表都为空。
    /// </summary>
    public static CoordinateYieldView Of(ShowMask mask, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (mask.Reveals.IsEmpty)
        {
            return CoordinateYieldView.None;
        }

        ImmutableDictionary<int, int>.Builder far = ImmutableDictionary.CreateBuilder<int, int>();
        ImmutableDictionary<int, int>.Builder left = ImmutableDictionary.CreateBuilder<int, int>();
        ImmutableDictionary<int, int>.Builder right = ImmutableDictionary.CreateBuilder<int, int>();
        foreach (RevealDisplay reveal in mask.Reveals)
        {
            int permille = PermilleAt(reveal.ShownMs);
            int x = reveal.Coord.X, y = reveal.Coord.Y;
            if (y >= height - FarRowReach)
            {
                for (int column = Math.Max(0, x - ColumnSpan); column <= Math.Min(width - 1, x + ColumnSpan); column++)
                {
                    Hold(far, column, permille);
                }
            }

            if (x < SideColumnReach)
            {
                HoldRows(left, y, height, permille);
            }

            if (x >= width - SideColumnReach)
            {
                HoldRows(right, y, height, permille);
            }
        }

        return new CoordinateYieldView(far.ToImmutable(), left.ToImmutable(), right.ToImmutable());
    }

    /// <summary>
    /// 条目已显示 <paramref name="shownMs"/> 毫秒时被它让位的标注的显现进度：前 <see cref="FadeMs"/> 内从 1000 线性降到 <see cref="YieldPermille"/>，此后保持。
    /// </summary>
    public static int PermilleAt(int shownMs)
    {
        if (shownMs >= FadeMs)
        {
            return YieldPermille;
        }

        return FullPermille - ((FullPermille - YieldPermille) * Math.Max(0, shownMs) / FadeMs);
    }

    private static void HoldRows(ImmutableDictionary<int, int>.Builder rows, int y, int height, int permille)
    {
        for (int row = y; row <= Math.Min(height - 1, y + RowSpan); row++)
        {
            Hold(rows, row, permille);
        }
    }

    private static void Hold(ImmutableDictionary<int, int>.Builder table, int index, int permille) =>
        table[index] = table.TryGetValue(index, out int held) ? Math.Min(held, permille) : permille;
}
