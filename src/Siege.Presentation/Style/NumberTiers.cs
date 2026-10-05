using System.Collections.Immutable;
using System.Numerics;

namespace Siege.Presentation.Style;

/// <summary>
/// 数值档位（settlement-show「数值档位」，tiered-number-show design.md D1）：把任意精度整数按<b>绝对值</b>映射到一至五档。
/// 军势揭示、势力栏到账与棋串军势常驻标注共用这<b>一个</b>入口；引擎层不取档，只读呈现层给出的档位。
/// 档位只影响呈现，不参与任何规则计算、比较或排序。
/// </summary>
/// <remarks>阈值是初值（2026-10-05 的 10 局实测分布），数值规则调整后需重定；改阈值只改 <see cref="Thresholds"/> 一处。</remarks>
public static class NumberTier
{
    /// <summary>各档的下界（升序）：绝对值每达到一个下界升一档。只做整数比较，不经浮点。</summary>
    private static readonly ImmutableArray<int> Thresholds = [4, 8, 16, 32];

    /// <summary>最低档（一档）。</summary>
    public const int Lowest = 1;

    /// <summary>最高档（五档）。</summary>
    public static int Highest => Lowest + Thresholds.Length;

    /// <summary>取档：绝对值小于 4 为一档，4–7 二档，8–15 三档，16–31 四档，32 及以上五档。</summary>
    public static int Of(BigInteger value)
    {
        BigInteger magnitude = BigInteger.Abs(value);
        int tier = Lowest;
        foreach (int threshold in Thresholds)
        {
            if (magnitude >= threshold)
            {
                tier++;
            }
        }

        return tier;
    }
}

/// <summary>
/// 某一档的呈现参数（tiered-number-show design.md D1 的分档样式表）。全部整数，不进浮点；各列互相独立，逐档单调不减。
/// 一档的字号、结果停留、势力栏放大与常驻标注字号等于分档之前的值。
/// </summary>
/// <param name="Tier">档位（1–5）。</param>
/// <param name="RevealFontSize">军势揭示条目的字号。</param>
/// <param name="RevealOutlineSize">军势揭示条目的描边宽度。</param>
/// <param name="PopPermille">每一步出现时的弹出幅度（‰，1000 = 原大小）：步首放大到这个幅度，步内回落。</param>
/// <param name="ResultHoldMs">结果自末步开始起的停留时长（毫秒）。</param>
/// <param name="FinalStepMs">末步时长（毫秒）：四、五档定格更久（design.md A1）。</param>
/// <param name="RingCount">末步时格上向外扩散的亮环圈数；0 = 没有。</param>
/// <param name="ShakeMs">末步时镜头轻震的时长（毫秒）；0 = 不震。</param>
/// <param name="RankScalePercent">势力栏分段到账的段首放大幅度（%，100 = 原大小）。</param>
/// <param name="GroupLabelFontSize">棋串军势常驻标注的字号。</param>
/// <param name="PitchSemitones">揭示 / 领地到账 / 军势到账音效的升调半音数。</param>
/// <param name="RevealColor">军势揭示条目的颜色：白 → 淡金 → 金 → 橙 → 红。</param>
public sealed record NumberTierStyle(
    int Tier,
    int RevealFontSize,
    int RevealOutlineSize,
    int PopPermille,
    int ResultHoldMs,
    int FinalStepMs,
    int RingCount,
    int ShakeMs,
    int RankScalePercent,
    int GroupLabelFontSize,
    int PitchSemitones,
    Rgba RevealColor)
{
    /// <summary>常驻标注在一档（分档之前）的字号与描边：其余各档的描边按字号同比例放大。</summary>
    private const int GroupLabelBaseFontSize = 88;

    private const int GroupLabelBaseOutlineSize = 20;

    /// <summary>五档样式表，下标 = 档位 − 1。</summary>
    public static readonly ImmutableArray<NumberTierStyle> All =
    [
        new(1, RevealFontSize: 72, RevealOutlineSize: 10, PopPermille: 1100, ResultHoldMs: 1400, FinalStepMs: 220, RingCount: 0, ShakeMs: 0, RankScalePercent: 130, GroupLabelFontSize: 88, PitchSemitones: 0, RevealColor: new Rgba(244, 240, 230)),
        new(2, RevealFontSize: 84, RevealOutlineSize: 12, PopPermille: 1200, ResultHoldMs: 1400, FinalStepMs: 220, RingCount: 0, ShakeMs: 0, RankScalePercent: 145, GroupLabelFontSize: 96, PitchSemitones: 2, RevealColor: new Rgba(244, 226, 164)),
        new(3, RevealFontSize: 100, RevealOutlineSize: 14, PopPermille: 1300, ResultHoldMs: 1600, FinalStepMs: 220, RingCount: 0, ShakeMs: 0, RankScalePercent: 160, GroupLabelFontSize: 106, PitchSemitones: 4, RevealColor: new Rgba(240, 196, 72)),
        new(4, RevealFontSize: 124, RevealOutlineSize: 18, PopPermille: 1450, ResultHoldMs: 1900, FinalStepMs: 440, RingCount: 1, ShakeMs: 0, RankScalePercent: 180, GroupLabelFontSize: 118, PitchSemitones: 7, RevealColor: new Rgba(244, 146, 48)),
        new(5, RevealFontSize: 156, RevealOutlineSize: 22, PopPermille: 1600, ResultHoldMs: 2200, FinalStepMs: 660, RingCount: 2, ShakeMs: 250, RankScalePercent: 200, GroupLabelFontSize: 132, PitchSemitones: 12, RevealColor: new Rgba(236, 72, 52)),
    ];

    /// <summary>常驻标注的描边宽度：按字号与一档同比例（整数除法），一档即分档之前的 20。</summary>
    public int GroupLabelOutlineSize => GroupLabelFontSize * GroupLabelBaseOutlineSize / GroupLabelBaseFontSize;

    /// <summary>某一档的样式；档位超出 1–5 抛出。</summary>
    public static NumberTierStyle For(int tier) =>
        tier >= NumberTier.Lowest && tier <= All.Length
            ? All[tier - NumberTier.Lowest]
            : throw new ArgumentOutOfRangeException(nameof(tier), tier, $"数值档位只有 {NumberTier.Lowest}–{All.Length} 档。");
}
