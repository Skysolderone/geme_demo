using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Board;

namespace Siege.Presentation.Show;

/// <summary>时间线的时长模式（design.md D8）。</summary>
public enum ShowDuration
{
    /// <summary>正常：按各节拍基准时长播放。</summary>
    Normal,

    /// <summary>零时长：无人值守运行——创建即播完、遮罩为空、不占任何推进。</summary>
    Zero,
}

/// <summary>势力值的整数插值（design.md D7）：千分比进度、任意精度整数、先乘后除向零取整；进度满时恰为新值。</summary>
public static class PowerInterpolation
{
    /// <summary>千分比的满值。</summary>
    public const int FullPermille = 1000;

    /// <summary>显示值 = 旧值 + (新值 − 旧值) × 进度 / 1000；<paramref name="permille"/> 夹到 0..1000，为 1000 时直接取新值。</summary>
    public static BigInteger Lerp(BigInteger oldValue, BigInteger newValue, int permille)
    {
        int p = Math.Clamp(permille, 0, FullPermille);
        if (p == FullPermille)
        {
            return newValue;
        }

        if (p == 0)
        {
            return oldValue;
        }

        // BigInteger 除法向零取整：上升时不超过新值、下降时不低于新值，中间值恒在两端之间。
        return oldValue + ((newValue - oldValue) * p / FullPermille);
    }
}

/// <summary>
/// 某玩家在演出遮罩里的势力显示：当前显示值、对应的变化条目、节拍内进度（0..1000‰）、是否正在滚动（势力重算节拍进行中），
/// 以及分段到账的段、段内进度与本段增量文案（settlement-show-callouts D2；图形侧按段内进度算数字弹跳的缩放，这里不给浮点）。
/// </summary>
public sealed record PowerDisplay(BigInteger Value, PowerChange Change, int ProgressPermille, bool Rolling, PowerStage Stage, int StagePermille, string? StageText);

/// <summary>飘字的来源。</summary>
public enum CalloutKind
{
    /// <summary>落子：类型名 + 军势算式。</summary>
    Placement,

    /// <summary>提子："提"。</summary>
    Capture,

    /// <summary>信物揭示：信物名。</summary>
    Relic,
}

/// <summary>一条活跃飘字（design.md D4）：所在格、文案、来源、年龄（0..1000‰，满即淡出完毕、不再列出）。</summary>
public sealed record Callout(Coord Coord, string Text, CalloutKind Kind, int AgePermille)
{
    /// <summary>飘字寿命（毫秒）：出现后上浮并在此时间内淡出，可跨到下一节拍。</summary>
    public const int LifetimeMs = 1400;
}

/// <summary>当前显示的横幅（settlement-show-callouts D3）：文案、在本节拍里的序号与总数、本条内进度（0..1000‰）。</summary>
public sealed record BannerDisplay(string Text, int Index, int Count, int ProgressPermille);

/// <summary>
/// 演出遮罩（design.md D6）：由时间线当前状态推出的"画面中间态"，纯显示层、不回写任何视图模型。
/// </summary>
/// <param name="Hidden">落子节拍尚未播到的棋子：绘制时隐藏。</param>
/// <param name="Appearing">正在落下的棋子及其出现进度（0..1000‰，不含 0）。</param>
/// <param name="StillShown">提子节拍尚未播完的被提棋子：仍按原归属绘制。</param>
/// <param name="CaptureFadePermille">提子节拍内的淡出进度（0..1000‰）；提子节拍未开始为 0。</param>
/// <param name="Power">势力重算节拍尚未播完的玩家：当前应显示的势力值（节拍前为旧值，节拍中按段插值）。</param>
/// <param name="Callouts">活跃飘字（落子 / 提子 / 信物揭示），按出现顺序。</param>
/// <param name="CaptureSummary">提子节拍进行中的合计文案"提 N 子"；其余时候 <c>null</c>。</param>
/// <param name="RelicFlash">信物揭示节拍进行中闪光的信物格及其进度（0..1000‰）。</param>
/// <param name="Banner">横幅节拍进行中当前显示的横幅；其余时候 <c>null</c>。</param>
public sealed record ShowMask(
    ImmutableHashSet<Coord> Hidden,
    ImmutableDictionary<Coord, int> Appearing,
    ImmutableArray<CapturedPiece> StillShown,
    int CaptureFadePermille,
    ImmutableDictionary<PlayerId, PowerDisplay> Power,
    ImmutableArray<Callout> Callouts,
    string? CaptureSummary,
    ImmutableDictionary<Coord, int> RelicFlash,
    BannerDisplay? Banner)
{
    /// <summary>空遮罩：画面即终态。</summary>
    public static readonly ShowMask Empty = new(
        [], ImmutableDictionary<Coord, int>.Empty, [], 0, ImmutableDictionary<PlayerId, PowerDisplay>.Empty,
        [], null, ImmutableDictionary<Coord, int>.Empty, null);

    /// <summary>遮罩是否为空（没有任何中间态）。</summary>
    public bool IsEmpty =>
        Hidden.IsEmpty && Appearing.IsEmpty && StillShown.IsEmpty && CaptureFadePermille == 0 && Power.IsEmpty
        && Callouts.IsEmpty && CaptureSummary is null && RelicFlash.IsEmpty && Banner is null;
}

/// <summary>
/// 演出时间线（settlement-show「演出按节拍依次播放」「提速」，design.md D2 / D4 / D5 / D8）：按传入的时间增量推进节拍，
/// 给出当前节拍、节拍内进度与是否播完。不读时钟——只接受调用方传入的整数毫秒增量，可用固定增量做确定性测试。
/// </summary>
/// <remarks>
/// <para>没有"直接到结尾"的入口（D4）：每个节拍至少作为 <see cref="Current"/> 出现一次，或在跨过时由 <see cref="Advance"/> 报告。</para>
/// <para>空序列（如势力不变的 Pass）在正常模式下保留 <see cref="EmptyHoldMs"/> 的短停顿（D5），零时长模式下同样不占推进。</para>
/// <para>飘字按演出内的绝对时刻计年龄（<see cref="_startOfCurrentMs"/> + 节拍内已过毫秒），可跨节拍存活；演出播完即遮罩为空，最后一拍里未淡完的飘字随之消失。</para>
/// </remarks>
public sealed class ShowTimeline
{
    /// <summary>提速倍率（D4）。</summary>
    public const int SpeedUpFactor = 4;

    /// <summary>空序列的短停顿（毫秒，D5）。</summary>
    public const int EmptyHoldMs = 300;

    private int _index;
    private int _elapsedMs;
    private int _startOfCurrentMs;

    /// <summary>创建时间线。<paramref name="duration"/> 为零时长即创建就播完。</summary>
    public ShowTimeline(ImmutableArray<SettlementBeat> beats, ShowDuration duration)
    {
        Beats = beats.IsDefault ? [] : beats;
        Duration = duration;
        IsFinished = duration == ShowDuration.Zero;
    }

    /// <summary>一条已播完的空时间线（对局开始、演出结束或被弃赛终止后的静止状态）。</summary>
    public static ShowTimeline Finished { get; } = new([], ShowDuration.Zero);

    /// <summary>节拍序列（只读）。</summary>
    public ImmutableArray<SettlementBeat> Beats { get; }

    /// <summary>时长模式。</summary>
    public ShowDuration Duration { get; }

    /// <summary>是否播完。</summary>
    public bool IsFinished { get; private set; }

    /// <summary>当前节拍；播完或空序列停顿中为 <c>null</c>。</summary>
    public SettlementBeat? Current => !IsFinished && _index < Beats.Length ? Beats[_index] : null;

    /// <summary>当前节拍（或空序列停顿）内的进度，0..1000‰；播完为 1000。</summary>
    public int ProgressPermille
    {
        get
        {
            if (IsFinished)
            {
                return PowerInterpolation.FullPermille;
            }

            int total = CurrentDurationMs;
            return total <= 0 ? PowerInterpolation.FullPermille : Math.Min(PowerInterpolation.FullPermille, _elapsedMs * PowerInterpolation.FullPermille / total);
        }
    }

    /// <summary>正常速度下从头到尾所需的总时长（毫秒）。零时长模式为 0。</summary>
    public int TotalDurationMs => Duration == ShowDuration.Zero ? 0 : Beats.IsEmpty ? EmptyHoldMs : Beats.Sum(b => b.DurationMs);

    private int CurrentDurationMs => Beats.IsEmpty ? EmptyHoldMs : Beats[_index].DurationMs;

    /// <summary>
    /// 推进 <paramref name="deltaMs"/> 毫秒；<paramref name="fast"/> 为真即按住提速，增量按 <see cref="SpeedUpFactor"/> 倍计。
    /// 返回本次推进中<b>播完</b>的全部节拍（按顺序）——单帧跨过多个节拍时逐个报告，图形侧据此至少应用其终态（D4）。
    /// </summary>
    public ImmutableArray<SettlementBeat> Advance(int deltaMs, bool fast = false)
    {
        if (IsFinished || deltaMs <= 0)
        {
            return [];
        }

        int remaining = fast ? deltaMs * SpeedUpFactor : deltaMs;
        ImmutableArray<SettlementBeat>.Builder passed = ImmutableArray.CreateBuilder<SettlementBeat>();
        while (remaining > 0 && !IsFinished)
        {
            int left = CurrentDurationMs - _elapsedMs;
            if (remaining < left)
            {
                _elapsedMs += remaining;
                remaining = 0;
                break;
            }

            remaining -= left;
            if (!Beats.IsEmpty)
            {
                passed.Add(Beats[_index]);
            }

            _startOfCurrentMs += CurrentDurationMs;
            _index++;
            _elapsedMs = 0;
            if (_index >= Math.Max(Beats.Length, 1))
            {
                IsFinished = true;
            }
        }

        return passed.ToImmutable();
    }

    /// <summary>由当前状态推出演出遮罩（D6）。播完即 <see cref="ShowMask.Empty"/>。</summary>
    public ShowMask Mask()
    {
        if (IsFinished || Beats.IsEmpty)
        {
            return ShowMask.Empty;
        }

        ImmutableHashSet<Coord>.Builder hidden = ImmutableHashSet.CreateBuilder<Coord>();
        ImmutableDictionary<Coord, int>.Builder appearing = ImmutableDictionary.CreateBuilder<Coord, int>();
        ImmutableArray<CapturedPiece> stillShown = [];
        int fade = 0;
        ImmutableDictionary<PlayerId, PowerDisplay>.Builder power = ImmutableDictionary.CreateBuilder<PlayerId, PowerDisplay>();
        ImmutableArray<Callout>.Builder callouts = ImmutableArray.CreateBuilder<Callout>();
        string? captureSummary = null;
        ImmutableDictionary<Coord, int>.Builder relicFlash = ImmutableDictionary.CreateBuilder<Coord, int>();
        BannerDisplay? banner = null;
        int progress = ProgressPermille;
        int nowMs = _startOfCurrentMs + _elapsedMs;

        // 已播过与当前的节拍：飘字按各自出现时刻计年龄（D4，可跨节拍）。
        int startMs = 0;
        for (int i = 0; i <= _index && i < Beats.Length; i++)
        {
            switch (Beats[i])
            {
                case PlacementBeat placement:
                    for (int k = 0; k < placement.Pieces.Length; k++)
                    {
                        AddCallout(callouts, placement.Pieces[k].Coord, placement.Pieces[k].CalloutText, CalloutKind.Placement, nowMs - startMs - placement.AppearAtMs(k));
                    }

                    break;
                case CaptureBeat capture:
                    foreach (CapturedPiece piece in capture.Pieces)
                    {
                        AddCallout(callouts, piece.Coord, CaptureBeat.CalloutText, CalloutKind.Capture, nowMs - startMs);
                    }

                    break;
                case RelicRevealBeat reveal:
                    foreach (RevealedRelic relic in reveal.Relics)
                    {
                        AddCallout(callouts, relic.Coord, relic.Name, CalloutKind.Relic, nowMs - startMs);
                    }

                    break;
                default:
                    break;
            }

            startMs += Beats[i].DurationMs;
        }

        // 当前与未播到的节拍：画面中间态。
        for (int i = _index; i < Beats.Length; i++)
        {
            bool current = i == _index;
            switch (Beats[i])
            {
                case PlacementBeat placement:
                    MaskPlacement(placement, current ? progress : 0, hidden, appearing);
                    break;
                case CaptureBeat capture:
                    stillShown = capture.Pieces;
                    fade = current ? progress : 0;
                    captureSummary = current ? capture.SummaryText : null;
                    break;
                case RelicRevealBeat reveal:
                    if (current)
                    {
                        foreach (RevealedRelic relic in reveal.Relics)
                        {
                            relicFlash[relic.Coord] = progress;
                        }
                    }

                    break;
                case PowerBeat beat:
                    foreach (PowerChange change in beat.Changes)
                    {
                        int p = current ? progress : 0;
                        PowerStageDisplay stage = change.DisplayAt(current ? _elapsedMs : -1);
                        power[change.Player] = new PowerDisplay(stage.Value, change, p, current, stage.Stage, stage.StagePermille, stage.StageText);
                    }

                    break;
                case BannerBeat banners:
                    if (current && !banners.Banners.IsEmpty)
                    {
                        int index = Math.Min(banners.Banners.Length - 1, _elapsedMs / BannerBeat.PerBannerMs);
                        int within = Math.Min(PowerInterpolation.FullPermille, (_elapsedMs - (index * BannerBeat.PerBannerMs)) * PowerInterpolation.FullPermille / BannerBeat.PerBannerMs);
                        banner = new BannerDisplay(banners.Banners[index], index, banners.Banners.Length, within);
                    }

                    break;
                default:
                    break;
            }
        }

        return new ShowMask(hidden.ToImmutable(), appearing.ToImmutable(), stillShown, fade, power.ToImmutable(), callouts.ToImmutable(), captureSummary, relicFlash.ToImmutable(), banner);
    }

    /// <summary>年龄在 0..寿命 之内的飘字才活跃（尚未出现或已淡完的不列）。</summary>
    private static void AddCallout(ImmutableArray<Callout>.Builder callouts, Coord coord, string text, CalloutKind kind, int ageMs)
    {
        if (ageMs < 0 || ageMs >= Callout.LifetimeMs)
        {
            return;
        }

        callouts.Add(new Callout(coord, text, kind, ageMs * PowerInterpolation.FullPermille / Callout.LifetimeMs));
    }

    /// <summary>
    /// 落子节拍的中间态：第 i 枚（0 起）在节拍进度 i/n 处开始出现、到 (i+1)/n 处出现完毕；压缩节拍（本机玩家确认闪动）全部棋子同步出现。
    /// 节拍未开始（进度 0）时全部隐藏。
    /// </summary>
    private static void MaskPlacement(PlacementBeat beat, int progress, ImmutableHashSet<Coord>.Builder hidden, ImmutableDictionary<Coord, int>.Builder appearing)
    {
        int n = beat.Pieces.Length;
        for (int i = 0; i < n; i++)
        {
            int local;
            if (beat.Compressed)
            {
                local = progress;
            }
            else
            {
                // 第 i 枚的出现进度 = (整拍进度 − i/n) × n，夹到 0..1000。
                local = Math.Clamp((progress * n) - (i * PowerInterpolation.FullPermille), 0, PowerInterpolation.FullPermille);
            }

            if (local <= 0)
            {
                hidden.Add(beat.Pieces[i].Coord);
            }
            else if (local < PowerInterpolation.FullPermille)
            {
                appearing[beat.Pieces[i].Coord] = local;
            }
        }
    }
}
