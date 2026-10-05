using Siege.Core.Board;

namespace Siege.Presentation.Style;

/// <summary>
/// 盘面标注的颜色与样式下限（visual-style-baseline「盘面标注的避让与对比」，board-labels D2 / D3）：坐标标注两色、棋串军势常驻标注的配色与描边下限、
/// 军势揭示算式行的颜色、字号与描边。全部整数，不进浮点；引擎层只把 <see cref="Rgba"/> 翻成引擎颜色、把整数当字号与描边，不再调用提亮 / 压暗之类的换算。
/// </summary>
public static class BoardLabelStyle
{
    /// <summary>坐标标注的字色（原 <c>Visuals.CoordinateLabel</c>，值不变）。</summary>
    public static readonly Rgba CoordinateLabel = new(206, 202, 190);

    /// <summary>坐标标注的描边色（原 <c>Visuals.CoordinateLabelOutline</c>，值不变）：压住浅色字在亮地砖上的反差不足。常驻标注的描边也用它。</summary>
    public static readonly Rgba CoordinateLabelOutline = new(18, 19, 23);

    /// <summary>常驻标注字色：阵营主色向白色提亮的比例（‰）。四个阵营在这个比例下字与描边的亮度差都已不小于 <see cref="GroupLabelMinLumaGap"/>，不需要逐阵营上调。</summary>
    public const int GroupLabelLightenPermille = 600;

    /// <summary>常驻标注字色与描边色的最小亮度差（<see cref="Rgba.Luma"/>）。</summary>
    public const int GroupLabelMinLumaGap = 120;

    /// <summary>常驻标注描边宽度的下限。</summary>
    public const int GroupLabelMinOutline = 12;

    /// <summary>算式行字号：取结果字号的这个百分比（原 <c>BoardView.RevealFormulaPercent</c>），但不小于 <see cref="RevealFormulaMinFontSize"/>。</summary>
    public const int RevealFormulaPercent = 60;

    /// <summary>算式行字号的下限（与一档结果同字号）。</summary>
    public const int RevealFormulaMinFontSize = 72;

    /// <summary>算式行描边宽度的下限。</summary>
    public const int RevealFormulaMinOutline = 10;

    private const int Full = 1000;

    /// <summary>
    /// 某玩家常驻标注的字色与描边色：描边 = 坐标标注描边色；字色 = 阵营主色各通道向 255 按 <see cref="GroupLabelLightenPermille"/> 提亮（整数除法），透明度不变。
    /// </summary>
    public static (Rgba Text, Rgba Outline) GroupLabelColors(PlayerId owner)
    {
        Rgba primary = FactionTable.For(owner).Primary;
        return (Lighten(primary, GroupLabelLightenPermille), CoordinateLabelOutline);
    }

    /// <summary>常驻标注的描边宽度：<c>max(</c><see cref="GroupLabelMinOutline"/><c>, 该档按字号比例的描边)</c>。</summary>
    public static int GroupLabelOutlineOf(int tier) => GroupLabelOutlineFor(NumberTierStyle.For(tier).GroupLabelOutlineSize);

    /// <summary>
    /// 常驻标注描边的下限规则本身：<c>max(</c><see cref="GroupLabelMinOutline"/><c>, 按字号比例的描边)</c>。
    /// 现有五档的比例值都高于下限，下限只有在样式表改小时才起作用；单列出来是为了能用假设的比例值直接钉住这条规则。
    /// </summary>
    public static int GroupLabelOutlineFor(int proportionalOutline) => Math.Max(GroupLabelMinOutline, proportionalOutline);

    /// <summary>算式行的颜色：与结果相同（该步档位的揭示色），不向白色混色。</summary>
    public static Rgba RevealFormulaColorOf(int tier) => NumberTierStyle.For(tier).RevealColor;

    /// <summary>算式行字号：<c>max(</c><see cref="RevealFormulaMinFontSize"/><c>, 结果字号 × </c><see cref="RevealFormulaPercent"/><c> ÷ 100)</c>。</summary>
    public static int RevealFormulaFontSizeOf(int tier) =>
        Math.Max(RevealFormulaMinFontSize, NumberTierStyle.For(tier).RevealFontSize * RevealFormulaPercent / 100);

    /// <summary>算式行描边：<c>max(</c><see cref="RevealFormulaMinOutline"/><c>, 结果描边 × 算式行字号 ÷ 结果字号)</c>。</summary>
    public static int RevealFormulaOutlineOf(int tier)
    {
        NumberTierStyle style = NumberTierStyle.For(tier);
        return RevealFormulaOutlineFor(style.RevealOutlineSize, RevealFormulaFontSizeOf(tier), style.RevealFontSize);
    }

    /// <summary>
    /// 算式行描边的规则本身：<c>max(</c><see cref="RevealFormulaMinOutline"/><c>, 结果描边 × 算式行字号 ÷ 结果字号)</c>（整数除法）。
    /// 现有五档按比例都不低于下限，下限只有在样式表改小时才起作用；单列出来是为了能用假设的样式值直接钉住这条规则。
    /// </summary>
    public static int RevealFormulaOutlineFor(int resultOutline, int formulaFontSize, int resultFontSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(resultFontSize);
        return Math.Max(RevealFormulaMinOutline, resultOutline * formulaFontSize / resultFontSize);
    }

    private static Rgba Lighten(Rgba color, int permille) => new(
        (byte)(color.R + ((255 - color.R) * permille / Full)),
        (byte)(color.G + ((255 - color.G) * permille / Full)),
        (byte)(color.B + ((255 - color.B) * permille / Full)),
        color.A);
}
