using System.Numerics;
using Siege.Presentation.Show;
using Siege.Presentation.Style;
using Siege.Presentation.Text;

namespace Siege.Presentation.Hud;

/// <summary>地图上势力飘字的段（power-map）。</summary>
public enum PowerBurstPhase
{
    /// <summary>领地段：飘字从 +0 滚到 +领地增量。</summary>
    Territory,

    /// <summary>军势段：再滚到 +总增量。</summary>
    Group,

    /// <summary>收束：总增量定格，亮环、火花爆开，高档位镜头轻震。</summary>
    Finale,

    /// <summary>停留展示。</summary>
    Hold,

    /// <summary>（只本机玩家）一道光从落子处飞向个人卡片，地图上的飘字淡出。</summary>
    Fly,

    /// <summary>（只本机玩家）个人卡片原位从旧值滚到新值，外圈发光、冒火花。</summary>
    Soar,

    /// <summary>（其他玩家）飘字淡出。</summary>
    Fade,

    /// <summary>播完。</summary>
    Done,
}

/// <summary>
/// 势力飘字某一时刻的画面。
/// </summary>
/// <param name="Phase">所处的段。</param>
/// <param name="PhasePermille">段内进度（0..1000‰）。</param>
/// <param name="ShownDelta">地图飘字此刻显示的增量（从 0 起滚，段末恰为该段目标值）。</param>
/// <param name="ShownDeltaText"><see cref="ShownDelta"/> 的文案："+984"（大数缩写）。</param>
/// <param name="StageText">领地段 / 军势段的本段说明（"领地 +12"）；其余段为 <c>null</c>。</param>
/// <param name="StageTier">本段增量的数值档位（领地段取领地增量、军势段取军势增量、其余取总增量）。</param>
/// <param name="CardValue">个人卡片此刻应显示的势力：光飞到之前为旧值，飙升段内滚动，之后为新值。只对本机玩家有意义。</param>
/// <param name="CardValueText"><see cref="CardValue"/> 的缩写。</param>
public readonly record struct PowerBurstFrame(
    PowerBurstPhase Phase,
    int PhasePermille,
    BigInteger ShownDelta,
    string ShownDeltaText,
    string? StageText,
    int StageTier,
    BigInteger CardValue,
    string CardValueText);

/// <summary>
/// 结算后地图上的势力飘字时间线（power-map，纯函数：给已过毫秒数出画面）。每名玩家的结算都在其落子处播放，所有人都看得到；
/// 本机玩家自己的那一手在定格后再飞入个人卡片并让卡片原位飙升。只播上涨；分项为零的段跳过；数字段内缓出插值，全程整数。
/// </summary>
public sealed class PowerBurst
{
    /// <summary>领地段 / 军势段各自的时长。</summary>
    public const int SegmentMs = 550;

    /// <summary>收束。</summary>
    public const int FinaleMs = 450;

    /// <summary>停留。</summary>
    public const int HoldMs = 350;

    /// <summary>飞入卡片（只本机）。</summary>
    public const int FlyMs = 450;

    /// <summary>卡片飙升（只本机）。</summary>
    public const int SoarMs = 800;

    /// <summary>淡出（其他玩家）。</summary>
    public const int FadeMs = 350;

    private readonly int _territoryMs;
    private readonly int _groupMs;

    /// <param name="change">这一手结算里行动玩家自己的势力变化（取自结算节拍，领地 + 军势 = 总增量）。</param>
    /// <param name="forViewer">行动玩家是不是本机玩家：是则定格后飞入个人卡片、卡片飙升；否则原地淡出。</param>
    public PowerBurst(PowerChange change, bool forViewer)
    {
        Change = change ?? throw new ArgumentNullException(nameof(change));
        ForViewer = forViewer;
        _territoryMs = change.TerritoryDelta.IsZero ? 0 : SegmentMs;
        _groupMs = change.GroupDelta.IsZero ? 0 : SegmentMs;
    }

    public PowerChange Change { get; }

    public bool ForViewer { get; }

    /// <summary>总时长。</summary>
    public int DurationMs => _territoryMs + _groupMs + FinaleMs + HoldMs + (ForViewer ? FlyMs + SoarMs : FadeMs);

    /// <summary>总增量的数值档位：决定收束的特效强度。</summary>
    public int Tier => Change.TotalTier;

    /// <summary>名次变化（"名次 3 → 1"）；不变为 <c>null</c>。</summary>
    public string? RankText => Change.RankText;

    /// <summary>这一手值不值得播：只播上涨。</summary>
    public static bool Wants(PowerChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return change.Delta.Sign > 0;
    }

    /// <summary>已过 <paramref name="elapsedMs"/> 毫秒时的画面；负数视为 0，超出总时长为 <see cref="PowerBurstPhase.Done"/>。</summary>
    public PowerBurstFrame At(int elapsedMs)
    {
        int t = Math.Max(0, elapsedMs);
        BigInteger territory = Change.TerritoryDelta;
        BigInteger total = Change.Delta;

        if (t < _territoryMs)
        {
            BigInteger shown = PowerInterpolation.Lerp(BigInteger.Zero, territory, EaseOut(Permille(t, _territoryMs)));
            return Frame(PowerBurstPhase.Territory, t, _territoryMs, shown, Change.TerritoryText, Change.TerritoryTier, Change.OldValue);
        }

        t -= _territoryMs;
        if (t < _groupMs)
        {
            BigInteger shown = PowerInterpolation.Lerp(territory, total, EaseOut(Permille(t, _groupMs)));
            return Frame(PowerBurstPhase.Group, t, _groupMs, shown, Change.GroupText, Change.GroupTier, Change.OldValue);
        }

        t -= _groupMs;
        (PowerBurstPhase Phase, int Ms)[] tail = ForViewer
            ? [(PowerBurstPhase.Finale, FinaleMs), (PowerBurstPhase.Hold, HoldMs), (PowerBurstPhase.Fly, FlyMs), (PowerBurstPhase.Soar, SoarMs)]
            : [(PowerBurstPhase.Finale, FinaleMs), (PowerBurstPhase.Hold, HoldMs), (PowerBurstPhase.Fade, FadeMs)];
        foreach ((PowerBurstPhase phase, int ms) in tail)
        {
            if (t < ms)
            {
                BigInteger card = phase == PowerBurstPhase.Soar
                    ? PowerInterpolation.Lerp(Change.OldValue, Change.NewValue, EaseOut(Permille(t, ms)))
                    : Change.OldValue;
                return Frame(phase, t, ms, total, null, Change.TotalTier, card);
            }

            t -= ms;
        }

        return Frame(PowerBurstPhase.Done, 1, 1, total, null, Change.TotalTier, Change.NewValue);
    }

    private static PowerBurstFrame Frame(PowerBurstPhase phase, int t, int ms, BigInteger shown, string? stageText, int tier, BigInteger card) =>
        new(phase, Math.Min(PowerInterpolation.FullPermille, Permille(t, ms)), shown, $"+{Labels.CompactPower(shown)}", stageText, tier, card, Labels.CompactPower(card));

    private static int Permille(int t, int ms) => t * PowerInterpolation.FullPermille / ms;

    /// <summary>缓出（二次）：p' = 1 − (1 − p)²，千分比整数。</summary>
    public static int EaseOut(int permille)
    {
        int rest = PowerInterpolation.FullPermille - Math.Clamp(permille, 0, PowerInterpolation.FullPermille);
        return PowerInterpolation.FullPermille - (rest * rest / PowerInterpolation.FullPermille);
    }

    /// <summary>收束段的镜头轻震强度（‰ 进度，供引擎层折算位移）：四、五档才震，时长取样式表；不震为 <c>null</c>。</summary>
    public int? ShakePermilleAt(PowerBurstFrame frame)
    {
        int shakeMs = NumberTierStyle.For(Tier).ShakeMs;
        if (frame.Phase != PowerBurstPhase.Finale || Tier < 4)
        {
            return null;
        }

        int ms = Math.Max(shakeMs, 180);
        int elapsed = frame.PhasePermille * FinaleMs / PowerInterpolation.FullPermille;
        return elapsed < ms ? elapsed * PowerInterpolation.FullPermille / ms : null;
    }
}
