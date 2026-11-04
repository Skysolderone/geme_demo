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

/// <summary>确认落子后卡片数值构筑的段。</summary>
public enum PowerBuildPhase
{
    /// <summary>卡片从左上飞到画面中央、放大。显示旧值。</summary>
    Enter,

    /// <summary>领地段：旧值滚到"旧值 + 领地增量"。</summary>
    Territory,

    /// <summary>军势段：再滚到新值。</summary>
    Group,

    /// <summary>收束：新值定格，爆光、亮环、名次变化。</summary>
    Finale,

    /// <summary>停留展示。</summary>
    Hold,

    /// <summary>缩回左上卡片。</summary>
    Exit,

    /// <summary>播完。</summary>
    Done,
}

/// <summary>
/// 数值构筑某一时刻的画面。
/// </summary>
/// <param name="Phase">所处的段。</param>
/// <param name="PhasePermille">段内进度（0..1000‰）。</param>
/// <param name="Value">此刻显示的势力值（整数插值，段末恰为该段目标值）。</param>
/// <param name="ValueText"><see cref="Value"/> 的缩写。</param>
/// <param name="StageText">领地段 / 军势段的本段增量文案（"领地 +12"）；其余段为 <c>null</c>。</param>
/// <param name="StageTier">本段增量的数值档位（领地段取领地增量、军势段取军势增量、其余取总增量）。</param>
public readonly record struct PowerBuildFrame(PowerBuildPhase Phase, int PhasePermille, BigInteger Value, string ValueText, string? StageText, int StageTier);

/// <summary>
/// 确认落子后个人势力卡片的数值构筑时间线（纯函数：给已过毫秒数出画面）。只在本机玩家自己确认、且势力上涨时播放；
/// 分项为零的段跳过。数字在段内按缓出插值（先快后慢），全程整数。
/// </summary>
public sealed class PowerCardBuild
{
    /// <summary>飞入。</summary>
    public const int EnterMs = 350;

    /// <summary>领地段 / 军势段各自的时长。</summary>
    public const int SegmentMs = 650;

    /// <summary>收束。</summary>
    public const int FinaleMs = 500;

    /// <summary>停留。</summary>
    public const int HoldMs = 600;

    /// <summary>缩回。</summary>
    public const int ExitMs = 400;

    public PowerCardBuild(PowerChange change)
    {
        Change = change ?? throw new ArgumentNullException(nameof(change));
        TerritoryMs = change.TerritoryDelta.IsZero ? 0 : SegmentMs;
        GroupMs = change.GroupDelta.IsZero ? 0 : SegmentMs;
    }

    /// <summary>本机玩家这一手的势力变化（取自结算节拍，领地 + 军势 = 总增量）。</summary>
    public PowerChange Change { get; }

    private int TerritoryMs { get; }

    private int GroupMs { get; }

    /// <summary>总时长。</summary>
    public int DurationMs => EnterMs + TerritoryMs + GroupMs + FinaleMs + HoldMs + ExitMs;

    /// <summary>缩回段的起点：点击跳过时时钟直接跳到这里。</summary>
    public int ExitStartMs => DurationMs - ExitMs;

    /// <summary>总增量文案（"+36"）。</summary>
    public string DeltaText => Change.DeltaText;

    /// <summary>名次变化（"名次 3 → 1"）；不变为 <c>null</c>。</summary>
    public string? RankText => Change.RankText;

    /// <summary>新名次是否更靠前。</summary>
    public bool RankUp => Change.RankChanged && Change.NewRank < Change.OldRank;

    /// <summary>总增量的数值档位：决定收束的特效强度。</summary>
    public int Tier => Change.TotalTier;

    /// <summary>这一手的变化值不值得播构筑：只播上涨。</summary>
    public static bool Wants(PowerChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return change.Delta.Sign > 0;
    }

    /// <summary>已过 <paramref name="elapsedMs"/> 毫秒时的画面；负数视为 0，超出总时长为 <see cref="PowerBuildPhase.Done"/>。</summary>
    public PowerBuildFrame At(int elapsedMs)
    {
        int t = Math.Max(0, elapsedMs);
        BigInteger old = Change.OldValue;
        BigInteger afterTerritory = old + Change.TerritoryDelta;
        BigInteger now = Change.NewValue;

        if (t < EnterMs)
        {
            return Frame(PowerBuildPhase.Enter, t, EnterMs, old, null, Change.TotalTier);
        }

        t -= EnterMs;
        if (t < TerritoryMs)
        {
            int p = Permille(t, TerritoryMs);
            return Frame(PowerBuildPhase.Territory, t, TerritoryMs, PowerInterpolation.Lerp(old, afterTerritory, EaseOut(p)), Change.TerritoryText, Change.TerritoryTier);
        }

        t -= TerritoryMs;
        if (t < GroupMs)
        {
            int p = Permille(t, GroupMs);
            return Frame(PowerBuildPhase.Group, t, GroupMs, PowerInterpolation.Lerp(afterTerritory, now, EaseOut(p)), Change.GroupText, Change.GroupTier);
        }

        t -= GroupMs;
        foreach ((PowerBuildPhase phase, int ms) in new[] { (PowerBuildPhase.Finale, FinaleMs), (PowerBuildPhase.Hold, HoldMs), (PowerBuildPhase.Exit, ExitMs) })
        {
            if (t < ms)
            {
                return Frame(phase, t, ms, now, null, Change.TotalTier);
            }

            t -= ms;
        }

        return Frame(PowerBuildPhase.Done, 1, 1, now, null, Change.TotalTier);
    }

    private static PowerBuildFrame Frame(PowerBuildPhase phase, int t, int ms, BigInteger value, string? stageText, int tier) =>
        new(phase, Math.Min(PowerInterpolation.FullPermille, Permille(t, ms)), value, Labels.CompactPower(value), stageText, tier);

    private static int Permille(int t, int ms) => t * PowerInterpolation.FullPermille / ms;

    /// <summary>缓出（二次）：p' = 1 − (1 − p)²，千分比整数。</summary>
    public static int EaseOut(int permille)
    {
        int rest = PowerInterpolation.FullPermille - Math.Clamp(permille, 0, PowerInterpolation.FullPermille);
        return PowerInterpolation.FullPermille - (rest * rest / PowerInterpolation.FullPermille);
    }
}
