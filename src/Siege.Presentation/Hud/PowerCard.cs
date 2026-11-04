using System.Numerics;
using Siege.Core.Board;
using Siege.Presentation.Layers;
using Siege.Presentation.Preview;
using Siege.Presentation.Show;
using Siege.Presentation.Style;
using Siege.Presentation.Text;

namespace Siege.Presentation.Hud;

/// <summary>个人势力卡片上"本手预计"的形态。</summary>
public enum PowerForecastKind
{
    /// <summary>没有暂放：不显示预估。</summary>
    None,

    /// <summary>增量不超过 <see cref="PowerCard.RangeThreshold"/>：给确切的带符号增量。</summary>
    Exact,

    /// <summary>增量超过 <see cref="PowerCard.RangeThreshold"/>：不给确切数，只给落子后总势力的区间，悬念留给确认后的数值构筑。</summary>
    Range,

    /// <summary>暂放批次不合法：预演没有可信的势力值。</summary>
    Invalid,
}

/// <summary>
/// 个人势力卡片（左上回合信息下方，只显示本机玩家自己）。全部文字由 <see cref="PowerCard.Build"/> 给出，引擎层只摆放与取色。
/// </summary>
/// <param name="Player">本机玩家（引擎层据此取徽记与阵营色）。</param>
/// <param name="TitleText">卡片标题："红方 · 第 3 名"；没有名次为 "红方 · —"。</param>
/// <param name="Value">卡片大字显示的势力：演出中取滚动中的显示值，否则取总势力。</param>
/// <param name="ValueText"><see cref="Value"/> 的缩写（与排名栏同一份缩写规则）。</param>
/// <param name="BreakdownText">"领地 12 · 军势 3"；演出中改为本段增量（与排名栏同步）。</param>
/// <param name="StatusText">出局 / 弃赛的状态文字；没有为 <c>null</c>。</param>
/// <param name="LiveDeltaText">演出中正在到账的增量（"领地 +3" / "军势 −2" / 定格时的总增量）；不在演出里为 <c>null</c>。</param>
/// <param name="LiveDeltaNegative"><see cref="LiveDeltaText"/> 是否为负（引擎层取警示色）。</param>
/// <param name="Forecast">本手预计的形态。</param>
/// <param name="ForecastText">本手预计第一行："本手预计 +36" / "本手预计 +100 以上" / 不合法说明；<see cref="PowerForecastKind.None"/> 时为 <c>null</c>。</param>
/// <param name="ForecastRangeText">区间形态的第二行："预计到达 1100 – 1500"；其余形态为 <c>null</c>。</param>
/// <param name="ForecastRankText">预计名次变化："名次 第 3 → 第 1"；名次不变或没有预估为 <c>null</c>。</param>
/// <param name="ForecastNegative">预估增量为负（引擎层取警示色）。</param>
/// <param name="ForecastTier">预估增量的数值档位（1–5，引擎层据此取色）；没有预估为最低档。</param>
public sealed record PowerCardView(
    PlayerId Player,
    string TitleText,
    BigInteger Value,
    string ValueText,
    string BreakdownText,
    string? StatusText,
    string? LiveDeltaText,
    bool LiveDeltaNegative,
    PowerForecastKind Forecast,
    string? ForecastText,
    string? ForecastRangeText,
    string? ForecastRankText,
    bool ForecastNegative,
    int ForecastTier);

/// <summary>个人势力卡片的构建与"本手预计"的区间规则。只读视图模型，不做任何规则计算。</summary>
public static class PowerCard
{
    /// <summary>本手增量超过这个数（严格大于）才改给区间。</summary>
    public const int RangeThreshold = 100;

    /// <summary>
    /// 构建卡片。<paramref name="power"/> 为势力层内容（没有本机玩家那一行时返回 <c>null</c>，如插旗阶段）；
    /// <paramref name="preview"/> 为本机玩家的批次预演（不在部署时为 <c>null</c>）；<paramref name="display"/> 为演出遮罩里本机玩家的势力显示（不在演出里为 <c>null</c>）。
    /// </summary>
    public static PowerCardView? Build(PowerLayerContent power, PlayerId viewer, PreviewPresentation? preview, PowerDisplay? display)
    {
        ArgumentNullException.ThrowIfNull(power);
        if (power.Players.IsDefaultOrEmpty || power.Players.FirstOrDefault(p => p.Player == viewer) is not { } row)
        {
            return null;
        }

        int? rank = display is { Rolling: false } ? display.Change.OldRank : row.Rank;
        BigInteger value = display?.Value ?? row.Total;
        string title = $"{FactionTable.For(viewer).Name} · {(rank is int r ? $"第 {r} 名" : "—")}";

        string? live = null;
        bool liveNegative = false;
        if (display is not null)
        {
            (live, BigInteger shown) = display.Stage switch
            {
                PowerStage.Territory or PowerStage.Group => (display.StageText, display.Stage == PowerStage.Territory ? display.Change.TerritoryDelta : display.Change.GroupDelta),
                PowerStage.Hold => (display.Change.DeltaText, display.Change.Delta),
                _ => ((string?)null, BigInteger.Zero),
            };
            liveNegative = shown.Sign < 0;
        }

        // 演出中拆分取自结算后快照，与滚动中的旧值对不上：节拍开始前与滚动中只给增量，不给拆分。
        string breakdown = display is null
            ? $"领地 {row.TerritoryScore} · 军势 {Labels.CompactPower(row.GroupScore)}"
            : string.Empty;

        (PowerForecastKind kind, string? text, string? range, string? rankText, bool negative, int tier) = Forecast(preview, viewer);
        return new PowerCardView(viewer, title, value, Labels.CompactPower(value), breakdown, row.StatusText, live, liveNegative,
            kind, text, range, rankText, negative, tier);
    }

    private static (PowerForecastKind, string?, string?, string?, bool, int) Forecast(PreviewPresentation? preview, PlayerId viewer)
    {
        if (preview is null || preview.DeployUsed == 0)
        {
            return (PowerForecastKind.None, null, null, null, false, NumberTier.Lowest);
        }

        if (preview.Failure is not null || preview.PowerChanges.FirstOrDefault(c => c.Player == viewer) is not { } change)
        {
            return (PowerForecastKind.Invalid, "批次不合法，暂无预估", null, null, false, NumberTier.Lowest);
        }

        string? rankText = change.RankChanged && change.RankBefore is int before && change.RankAfter is int after
            ? $"名次 第 {before} → 第 {after}"
            : null;
        int tier = NumberTier.Of(change.Delta);
        if (change.Delta > RangeThreshold && change.After.Sign > 0)
        {
            (BigInteger low, BigInteger high) = RangeOf(change.After);
            return (PowerForecastKind.Range, $"本手预计 +{RangeThreshold} 以上",
                $"预计到达 {Labels.CompactPower(low)} – {Labels.CompactPower(high)}", rankText, false, tier);
        }

        return (PowerForecastKind.Exact, $"本手预计 {Signed(change.Delta)}", null, rankText, change.Delta.Sign < 0, tier);
    }

    /// <summary>
    /// 落子后总势力 <paramref name="after"/>（正数）的预计区间：下沿 = after × 0.9 向下取到两位有效数字，上沿 = after × 1.1 向上取到两位有效数字。
    /// 恒有 下沿 ≤ after ≤ 上沿；全程整数。
    /// </summary>
    public static (BigInteger Low, BigInteger High) RangeOf(BigInteger after)
    {
        if (after.Sign <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(after), after, "区间只对正的势力值给出。");
        }

        BigInteger low = TwoDigits(after * 9 / 10, roundUp: false);
        BigInteger high = TwoDigits(((after * 11) + 9) / 10, roundUp: true);
        return (low, high);
    }

    /// <summary>取到两位有效数字（两位以内原样）：向下截断或向上进位。</summary>
    private static BigInteger TwoDigits(BigInteger value, bool roundUp)
    {
        int digits = value.IsZero ? 1 : value.ToString(System.Globalization.CultureInfo.InvariantCulture).Length;
        if (digits <= 2)
        {
            return value;
        }

        BigInteger unit = BigInteger.Pow(10, digits - 2);
        BigInteger floor = value / unit * unit;
        return roundUp && floor != value ? floor + unit : floor;
    }

    private static string Signed(BigInteger value) => value.Sign < 0 ? $"−{BigInteger.Negate(value)}" : $"+{value}";
}
