using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Board;
using Siege.Presentation.Style;

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
    /// <summary>落子：类型名（军势算式由军势揭示条目 <see cref="RevealDisplay"/> 给出，tiered-number-show D3）。</summary>
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
/// 一条正在显示的军势揭示条目（tiered-number-show D4）：自其第一步开始起持续显示，结果自末步开始起按末步档位停留（<see cref="NumberTierStyle.ResultHoldMs"/>），
/// 可跨到后续节拍；尚未轮到的条目不列，结果停留满即不再列。全部是整数——弹出缩放、上浮与淡出由引擎层按进度折算。
/// </summary>
/// <param name="Coord">落点（与该棋串常驻标注同一格）。</param>
/// <param name="Owner">棋串所有者。</param>
/// <param name="RunningText">到最新一步为止的累计文案（"5+2"）；到末步即完整短算式。</param>
/// <param name="StepText">最新一步新出现的文案（"+2"）。</param>
/// <param name="StepTier">最新一步的档位：字号、颜色、描边、弹出幅度按它取样式表。</param>
/// <param name="StepPermille">最新一步内的进度（0..1000‰；该步结束后保持 1000）。</param>
/// <param name="AtFinal">是否已到末步（结果已出）。</param>
/// <param name="FinalTier">末步档位（= 该棋串军势的数值档位）。</param>
/// <param name="ResultAgePermille">结果年龄（0..1000‰，自末步开始按结果停留时长计）；未到末步为 0。</param>
public sealed record RevealDisplay(
    Coord Coord,
    PlayerId Owner,
    string RunningText,
    string StepText,
    int StepTier,
    int StepPermille,
    bool AtFinal,
    int FinalTier,
    int ResultAgePermille);

/// <summary>
/// 高档冲击环（settlement-show「高档冲击环与镜头轻震」）：军势揭示条目末步为四档时所在格一圈向外扩散的亮环、五档两圈，自末步开始持续 <see cref="DurationMs"/>。
/// </summary>
/// <param name="Coord">所在格。</param>
/// <param name="Count">圈数（四档 1、五档 2）。</param>
/// <param name="ProgressPermille">进度（0..1000‰，不含 1000：满即不再列）。</param>
public sealed record ImpactRing(Coord Coord, int Count, int ProgressPermille)
{
    /// <summary>亮环自末步开始起的持续时长（毫秒）。</summary>
    public const int DurationMs = 400;
}

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
/// <param name="Reveals">正在显示的军势揭示条目（tiered-number-show D4），按条目顺序（军势从小到大）。</param>
/// <param name="Rings">正在扩散的高档冲击环（四档一圈、五档两圈）。</param>
/// <param name="ShakePermille">镜头轻震的进度（0..1000‰，自五档末步开始 0.25 秒）；不在轻震中为 <c>null</c>。同一时刻至多一个：
/// 两个五档末步的轻震重叠时取后开始的那个。偏移量由引擎层按进度的确定函数算出，不用随机。</param>
/// <param name="RevealStepTiers">军势揭示节拍<b>进行中</b>已经开始的各步的档位，按开始顺序；节拍未开始或已播完为空。只供音效提示数出一帧里新开始了哪几步（D7），引擎层不画它。</param>
/// <param name="GroupLabelPermille">
/// 被揭示棋串的常驻标注显现进度（reveal-label-handoff D1）：落点（与常驻标注同一格）→ 显现进度（0..1000‰，不含 1000）。
/// 军势揭示节拍将要揭示或正在揭示的棋串列在这里：揭示结果停留过半之前为 0（不画），之后随结果的淡出线性淡入；条目消失后不再列出。
/// <b>不在表里的落点一律完全显示</b>。
/// </param>
public sealed record ShowMask(
    ImmutableHashSet<Coord> Hidden,
    ImmutableDictionary<Coord, int> Appearing,
    ImmutableArray<CapturedPiece> StillShown,
    int CaptureFadePermille,
    ImmutableDictionary<PlayerId, PowerDisplay> Power,
    ImmutableArray<Callout> Callouts,
    string? CaptureSummary,
    ImmutableDictionary<Coord, int> RelicFlash,
    BannerDisplay? Banner,
    ImmutableArray<RevealDisplay> Reveals,
    ImmutableArray<ImpactRing> Rings,
    int? ShakePermille,
    ImmutableArray<int> RevealStepTiers,
    ImmutableDictionary<Coord, int> GroupLabelPermille)
{
    /// <summary>空遮罩：画面即终态。</summary>
    public static readonly ShowMask Empty = new(
        [], ImmutableDictionary<Coord, int>.Empty, [], 0, ImmutableDictionary<PlayerId, PowerDisplay>.Empty,
        [], null, ImmutableDictionary<Coord, int>.Empty, null, [], [], null, [], ImmutableDictionary<Coord, int>.Empty);

    /// <summary>遮罩是否为空（没有任何中间态）。</summary>
    public bool IsEmpty =>
        Hidden.IsEmpty && Appearing.IsEmpty && StillShown.IsEmpty && CaptureFadePermille == 0 && Power.IsEmpty
        && Callouts.IsEmpty && CaptureSummary is null && RelicFlash.IsEmpty && Banner is null
        && Reveals.IsEmpty && Rings.IsEmpty && ShakePermille is null && RevealStepTiers.IsEmpty && GroupLabelPermille.IsEmpty;
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
        ImmutableArray<RevealDisplay>.Builder reveals = ImmutableArray.CreateBuilder<RevealDisplay>();
        ImmutableArray<ImpactRing>.Builder rings = ImmutableArray.CreateBuilder<ImpactRing>();
        int? shake = null;
        ImmutableArray<int> revealStepTiers = [];
        ImmutableDictionary<Coord, int>.Builder groupLabels = ImmutableDictionary.CreateBuilder<Coord, int>();
        int progress = ProgressPermille;
        int nowMs = _startOfCurrentMs + _elapsedMs;

        // 已播过与当前的节拍：飘字与军势揭示条目按各自出现时刻计年龄（D4，可跨节拍）。
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
                case PowerRevealBeat reveal:
                    AddReveals(reveal, nowMs - startMs, reveals, rings, groupLabels, ref shake);
                    if (i == _index)
                    {
                        revealStepTiers = reveal.StepTiersStartedBy(_elapsedMs);
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
                case PowerRevealBeat reveal:
                    if (!current)
                    {
                        // 尚未播到军势揭示节拍（落子 / 提子 / 信物节拍期间）：将要揭示的棋串一律先不显示常驻标注——盘面视图已是终态，标注写的就是揭示的结果。
                        foreach (RevealEntry entry in reveal.Entries)
                        {
                            HoldLabel(groupLabels, entry.Coord, 0);
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

        return new ShowMask(
            hidden.ToImmutable(), appearing.ToImmutable(), stillShown, fade, power.ToImmutable(), callouts.ToImmutable(), captureSummary, relicFlash.ToImmutable(), banner,
            reveals.ToImmutable(), rings.ToImmutable(), shake, revealStepTiers, groupLabels.ToImmutable());
    }

    /// <summary>
    /// 军势揭示节拍在其开始后 <paramref name="sinceBeatMs"/> 毫秒时的中间态（tiered-number-show D4）：逐条目给出最新一步与结果年龄，
    /// 末步为四、五档的条目在末步开始后 <see cref="ImpactRing.DurationMs"/> 内带亮环，五档末步开始后 <see cref="NumberTierStyle.ShakeMs"/> 内轻震。
    /// 都按演出内的绝对时刻推出，节拍播完后条目、亮环与轻震照样按各自的时长走完（演出播完即空）。
    /// 同时给出各条目所在棋串的常驻标注显现进度（reveal-label-handoff D1）：尚未轮到、未到末步的条目为 0，到末步后按结果年龄取
    /// <see cref="LabelPermilleAt"/>，结果停留满即不再列出（标注完全显示）。
    /// </summary>
    private static void AddReveals(
        PowerRevealBeat beat,
        int sinceBeatMs,
        ImmutableArray<RevealDisplay>.Builder reveals,
        ImmutableArray<ImpactRing>.Builder rings,
        ImmutableDictionary<Coord, int>.Builder groupLabels,
        ref int? shake)
    {
        for (int e = 0; e < beat.Entries.Length; e++)
        {
            RevealEntry entry = beat.Entries[e];
            if (sinceBeatMs < beat.StepStartMs(e, 0))
            {
                // 条目逐个依次播放：这一条尚未轮到（后面的更晚，同样走到这里）——条目不列，但它的常驻标注先隐藏着。
                HoldLabel(groupLabels, entry.Coord, 0);
                continue;
            }

            int last = entry.Steps.Length - 1;
            int k = last;
            while (k > 0 && sinceBeatMs < beat.StepStartMs(e, k))
            {
                k--;
            }

            RevealStep step = entry.Steps[k];
            int stepStart = beat.StepStartMs(e, k);
            int stepMs = beat.StepEndMs(e, k) - stepStart;
            int within = stepMs <= 0 ? PowerInterpolation.FullPermille : Math.Min(PowerInterpolation.FullPermille, (sinceBeatMs - stepStart) * PowerInterpolation.FullPermille / stepMs);
            bool atFinal = k == last;
            int resultAge = 0;
            if (atFinal)
            {
                NumberTierStyle style = NumberTierStyle.For(entry.FinalTier);
                int sinceFinalMs = sinceBeatMs - stepStart;
                if (style.RingCount > 0 && sinceFinalMs < ImpactRing.DurationMs)
                {
                    rings.Add(new ImpactRing(entry.Coord, style.RingCount, sinceFinalMs * PowerInterpolation.FullPermille / ImpactRing.DurationMs));
                }

                if (style.ShakeMs > 0 && sinceFinalMs < style.ShakeMs)
                {
                    // 同一时刻至多一个：条目按时间先后遍历，后开始的覆盖先开始的。
                    shake = sinceFinalMs * PowerInterpolation.FullPermille / style.ShakeMs;
                }

                if (sinceFinalMs >= style.ResultHoldMs)
                {
                    // 结果停留满：不再列。
                    continue;
                }

                resultAge = sinceFinalMs * PowerInterpolation.FullPermille / style.ResultHoldMs;
            }

            HoldLabel(groupLabels, entry.Coord, LabelPermilleAt(resultAge));

            reveals.Add(new RevealDisplay(entry.Coord, entry.Owner, step.RunningText, step.Text, step.Tier, within, atFinal, entry.FinalTier, resultAge));
        }
    }

    /// <summary>
    /// 常驻标注在揭示结果年龄 <paramref name="resultAgePermille"/>（‰）时的显现进度（reveal-label-handoff D1）：max(0, (年龄 − 500) × 2)——
    /// 结果停留的前半段保持隐藏，后半段（结果淡出时）线性淡入。未到末步的条目结果年龄为 0，即隐藏。
    /// </summary>
    private static int LabelPermilleAt(int resultAgePermille) => Math.Max(0, (resultAgePermille - (PowerInterpolation.FullPermille / 2)) * 2);

    /// <summary>把一个落点记进显现进度表；同一落点被列多次时取较小的那个（只要还有一条没揭示完，就按最隐藏的算）。</summary>
    private static void HoldLabel(ImmutableDictionary<Coord, int>.Builder groupLabels, Coord coord, int permille) =>
        groupLabels[coord] = groupLabels.TryGetValue(coord, out int held) ? Math.Min(held, permille) : permille;

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
