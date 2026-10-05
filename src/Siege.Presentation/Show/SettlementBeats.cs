using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Relics;
using Siege.Core.Scoring;
using Siege.Presentation.Layers;
using Siege.Presentation.Preview;
using Siege.Presentation.Style;
using Siege.Presentation.Text;

namespace Siege.Presentation.Show;

/// <summary>
/// 落子节拍里的一枚棋子：坐标、类型、归属（取自结算后公开快照的盘面）。
/// 军势算式不在这里（tiered-number-show D3）：由军势揭示节拍 <see cref="PowerRevealBeat"/> 逐步给出。
/// </summary>
public readonly record struct PlacedPiece(Coord Coord, PieceType Type, PlayerId Owner)
{
    /// <summary>飘字文案：只有棋子类型名（<see cref="Labels.Piece"/>）。</summary>
    public string CalloutText => Labels.Piece(Type);
}

/// <summary>提子节拍里的一枚被提棋子：坐标与原归属（原归属与类型都取自结算前公开快照的盘面；类型只为按原样画出淡出中的棋子）。</summary>
public readonly record struct CapturedPiece(Coord Coord, PlayerId Owner, PieceType Type);

/// <summary>
/// 某玩家的一条势力读数：总势力、竞争名次（已弃赛 / 已出局为 <c>null</c>）、领地分与棋串军势（settlement-show-callouts：分项到账）。
/// 由公开快照的势力明细投影而来；<see cref="Total"/> 原样取快照的总势力，不由分项相加得出。
/// </summary>
public readonly record struct PowerReading(PlayerId Player, BigInteger Total, int? Rank, BigInteger Territory, BigInteger GroupScore);

/// <summary>某玩家的参赛状态读数（横幅节拍用：状态变为出局的玩家各一条）。</summary>
public readonly record struct StatusReading(PlayerId Player, PlayerStatus Status);

/// <summary>信物揭示节拍里的一格：坐标与信物名（经 <see cref="Labels.RelicContent"/>；只存文案，不存信物对象）。</summary>
public readonly record struct RevealedRelic(Coord Coord, string Name);

/// <summary>势力重算节拍播放中某玩家所处的段（settlement-show-callouts D2）。</summary>
public enum PowerStage
{
    /// <summary>节拍尚未开始：显示旧值。</summary>
    Pending,

    /// <summary>领地段：从旧值滚到"旧值 + 领地增量"。</summary>
    Territory,

    /// <summary>军势段：再滚到新值。</summary>
    Group,

    /// <summary>定格：显示新值。</summary>
    Hold,
}

/// <summary>势力重算节拍某一时刻的显示：段、段内进度（0..1000‰）、显示值、本段增量文案（定格 / 未开始为 <c>null</c>）。</summary>
public readonly record struct PowerStageDisplay(PowerStage Stage, int StagePermille, BigInteger Value, string? StageText);

/// <summary>
/// 军势揭示条目里的一步（tiered-number-show D2）：这一步新出现的文案（"5" / "+2" / "×1.5" / "= 10"）、到这一步为止的累计文案
/// （"5" / "5+2" / "(5+2)×1.5" / "(5+2)×1.5 = 10"）、这一步的数值档位与基准时长（毫秒；整拍超上限时按比例压缩，见 <see cref="PowerRevealBeat.StepStartMs"/>）。
/// </summary>
public sealed record RevealStep(string Text, string RunningText, int Tier, int DurationMs);

/// <summary>
/// 军势揭示节拍的一个条目：一条含本次落子的棋串。落点与常驻标注同一格（<see cref="GroupPowerLabels.AnchorOf"/>），
/// 军势与算式各项原样取自结算后快照的棋串军势明细，不重算。
/// </summary>
public sealed record RevealEntry(Coord Coord, PlayerId Owner, BigInteger Power, ImmutableArray<RevealStep> Steps)
{
    /// <summary>末步的档位 = 该棋串军势的数值档位：决定末步时长、结果停留、亮环圈数与是否轻震。</summary>
    public int FinalTier => Steps[^1].Tier;

    /// <summary>由一条棋串军势明细投影：落点取常驻标注的落点，步骤见 <see cref="StepsOf"/>。</summary>
    public static RevealEntry From(GroupPower group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return new RevealEntry(GroupPowerLabels.AnchorOf(group.Stones), group.Owner, group.Power, StepsOf(GroupPowerView.From(group)));
    }

    /// <summary>
    /// 把军势算式拆成依次出现的步骤，只列非零项，口径与势力层短算式（<see cref="GroupPowerView.ShortFormulaText"/>）相同：
    /// 基础军势 →（加值非零）"+加值" →（有倍增子）"×倍率" →（有加值或倍率）"= 军势"。既无加值也无倍率的棋串只有一步，这一步就是结果（design.md A3）。
    /// 末步的累计文案与短算式逐字相同（守门：军势揭示节拍Tests.末步累计文案等于短算式）。
    /// 档位：末步取军势的数值档位 T；共 n 步时第 i 步（0 起）= max(1, T − (n − 1 − i))，即往前每步降一档、降到一档为止（A2：不保证严格递增）。
    /// 时长：每步 <see cref="PowerRevealBeat.StepMs"/>，末步取样式表的末步时长。
    /// </summary>
    public static ImmutableArray<RevealStep> StepsOf(GroupPowerView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var texts = new List<(string Text, string Running)>(4);
        string running = $"{view.BaseTotal}";
        texts.Add((running, running));
        if (view.PositionBonus != 0)
        {
            running = $"{running}+{view.PositionBonus}";
            texts.Add(($"+{view.PositionBonus}", running));
        }

        if (view.MultiplierCount != 0)
        {
            running = view.PositionBonus != 0 ? $"({running})×{view.MultiplierText}" : $"{running}×{view.MultiplierText}";
            texts.Add(($"×{view.MultiplierText}", running));
        }

        if (texts.Count > 1)
        {
            running = $"{running} = {view.Power}";
            texts.Add(($"= {view.Power}", running));
        }

        int finalTier = NumberTier.Of(view.Power);
        int last = texts.Count - 1;
        ImmutableArray<RevealStep>.Builder steps = ImmutableArray.CreateBuilder<RevealStep>(texts.Count);
        for (int i = 0; i <= last; i++)
        {
            int tier = Math.Max(NumberTier.Lowest, finalTier - (last - i));
            steps.Add(new RevealStep(texts[i].Text, texts[i].Running, tier, i == last ? NumberTierStyle.For(finalTier).FinalStepMs : PowerRevealBeat.StepMs));
        }

        return steps.MoveToImmutable();
    }
}

/// <summary>
/// 势力重算节拍的一条：旧值、新值、旧名次、新名次，以及领地分增量与棋串军势增量（两者之和恒等于总增量，settlement-show「势力重算节拍与数值变化显示」）。
/// 名次为 <c>null</c> 表示该玩家没有势力名次（已弃赛 / 已出局，design.md A6），此时不显示名次变动提示。
/// </summary>
public sealed record PowerChange(PlayerId Player, BigInteger OldValue, BigInteger NewValue, int? OldRank, int? NewRank, BigInteger TerritoryDelta, BigInteger GroupDelta)
{
    /// <summary>只给总值的条目：分项按"领地 0、军势 = 总增量"（只关心总值滚动的场合，如插值算例）。</summary>
    public PowerChange(PlayerId Player, BigInteger OldValue, BigInteger NewValue, int? OldRank, int? NewRank)
        : this(Player, OldValue, NewValue, OldRank, NewRank, BigInteger.Zero, NewValue - OldValue)
    {
    }

    /// <summary>带符号的增量（新 − 旧）。</summary>
    public BigInteger Delta => NewValue - OldValue;

    /// <summary>增量文案："+8" / "−6"（负号用 U+2212）；无变化时 "±0"。</summary>
    public string DeltaText => Signed(Delta);

    /// <summary>领地段文案："领地 +3"。</summary>
    public string TerritoryText => $"领地 {Signed(TerritoryDelta)}";

    /// <summary>军势段文案："军势 +6"。</summary>
    public string GroupText => $"军势 {Signed(GroupDelta)}";

    /// <summary>名次是否变动。两端都有名次且不同才算；任一端无名次不提示（A6）。</summary>
    public bool RankChanged => OldRank is not null && NewRank is not null && OldRank != NewRank;

    /// <summary>名次变动提示："名次 2 → 1"；没有变动（或没有名次）为 <c>null</c>。</summary>
    public string? RankText => RankChanged ? $"名次 {OldRank} → {NewRank}" : null;

    /// <summary>领地段时长（毫秒）：增量为 0 则跳过。</summary>
    public int TerritoryMs => TerritoryDelta.IsZero ? 0 : PowerBeat.SegmentMs;

    /// <summary>军势段时长（毫秒）：增量为 0 则跳过。</summary>
    public int GroupMs => GroupDelta.IsZero ? 0 : PowerBeat.SegmentMs;

    /// <summary>领地增量的数值档位（tiered-number-show D5）：领地段的段首放大幅度与到账音的音高按它取样式表。</summary>
    public int TerritoryTier => NumberTier.Of(TerritoryDelta);

    /// <summary>军势增量的数值档位。</summary>
    public int GroupTier => NumberTier.Of(GroupDelta);

    /// <summary>总增量的数值档位。</summary>
    public int TotalTier => NumberTier.Of(Delta);

    /// <summary>某一段显示的增量所在的档：领地段取领地增量、军势段取军势增量，其余（未开始 / 定格）取总增量。引擎层只读它，不自己取档。</summary>
    public int TierOf(PowerStage stage) => stage switch
    {
        PowerStage.Territory => TerritoryTier,
        PowerStage.Group => GroupTier,
        _ => TotalTier,
    };

    /// <summary>
    /// 节拍内已过 <paramref name="elapsedMs"/> 毫秒时的显示（design.md D2）：领地段 → 军势段 → 定格，分项为 0 的段跳过、时长让给定格；
    /// 显示值按 <see cref="PowerInterpolation.Lerp"/> 整数插值，段末恰为该段目标值。负的已过时间视为节拍未开始（<see cref="PowerStage.Pending"/>，旧值）。
    /// </summary>
    public PowerStageDisplay DisplayAt(int elapsedMs)
    {
        if (elapsedMs < 0)
        {
            return new PowerStageDisplay(PowerStage.Pending, 0, OldValue, null);
        }

        int territoryMs = TerritoryMs;
        int groupMs = GroupMs;
        BigInteger afterTerritory = OldValue + TerritoryDelta;
        if (elapsedMs < territoryMs)
        {
            int p = elapsedMs * PowerInterpolation.FullPermille / territoryMs;
            return new PowerStageDisplay(PowerStage.Territory, p, PowerInterpolation.Lerp(OldValue, afterTerritory, p), TerritoryText);
        }

        elapsedMs -= territoryMs;
        if (elapsedMs < groupMs)
        {
            int p = elapsedMs * PowerInterpolation.FullPermille / groupMs;
            return new PowerStageDisplay(PowerStage.Group, p, PowerInterpolation.Lerp(afterTerritory, NewValue, p), GroupText);
        }

        elapsedMs -= groupMs;
        int holdMs = PowerBeat.Ms - territoryMs - groupMs;
        int hold = holdMs <= 0 ? PowerInterpolation.FullPermille : Math.Min(PowerInterpolation.FullPermille, elapsedMs * PowerInterpolation.FullPermille / holdMs);
        return new PowerStageDisplay(PowerStage.Hold, hold, NewValue, null);
    }

    private static string Signed(BigInteger value) => value.Sign switch
    {
        > 0 => "+" + value.ToString(),
        < 0 => "−" + BigInteger.Negate(value).ToString(),
        _ => "±0",
    };
}

/// <summary>
/// 结算演出的一个节拍（design.md D3）。封闭集合：落子 → 提子 → 信物揭示 → 军势揭示 → 势力重算 → 横幅
/// （settlement-show-callouts D3 插入信物揭示与横幅，tiered-number-show D2 插入军势揭示，都不改已有节拍的时长）。
/// 时长为整数毫秒——表现层不用浮点（determinism.md）。
/// </summary>
public abstract record SettlementBeat
{
    /// <summary>基准时长（毫秒）。</summary>
    public abstract int DurationMs { get; }
}

/// <summary>
/// 落子节拍：按放置顺序逐枚出现，每枚 0.25 秒、整拍上限 1.0 秒（超过 4 枚压缩每枚间隔）。
/// <paramref name="Compressed"/> 为真即本机玩家自己的确认（design.md A2）：棋子在确认前已作暂放显示，整拍压成一次 0.25 秒的确认闪动，全部飘字同时出现。
/// </summary>
public sealed record PlacementBeat(ImmutableArray<PlacedPiece> Pieces, bool Compressed = false) : SettlementBeat
{
    /// <summary>每枚棋子的基准时长（毫秒）。</summary>
    public const int PerPieceMs = 250;

    /// <summary>整拍上限（毫秒）。</summary>
    public const int MaxMs = 1000;

    /// <inheritdoc/>
    public override int DurationMs => Compressed ? PerPieceMs : Math.Min(PerPieceMs * Pieces.Length, MaxMs);

    /// <summary>第 <paramref name="index"/> 枚（0 起）相对节拍开始的出现时刻（毫秒）：压缩节拍全部为 0，否则按 i/n 均分整拍。</summary>
    public int AppearAtMs(int index) => Compressed || Pieces.Length == 0 ? 0 : DurationMs * index / Pieces.Length;
}

/// <summary>提子节拍：全部被提棋子同时消失，0.6 秒，与数量无关（设计文档 §6.3 提子"同时"，A1）。每枚飘字"提"，合计"提 N 子"。</summary>
public sealed record CaptureBeat(ImmutableArray<CapturedPiece> Pieces) : SettlementBeat
{
    /// <summary>基准时长（毫秒）。</summary>
    public const int Ms = 600;

    /// <summary>每枚被提棋子的飘字。</summary>
    public const string CalloutText = "提";

    /// <inheritdoc/>
    public override int DurationMs => Ms;

    /// <summary>节拍合计文案："提 N 子"。</summary>
    public string SummaryText => $"提 {Pieces.Length} 子";
}

/// <summary>信物揭示节拍（settlement-show-callouts D3）：本次结算新揭示的信物格闪光并弹出信物名，0.6 秒，与数量无关。</summary>
public sealed record RelicRevealBeat(ImmutableArray<RevealedRelic> Relics) : SettlementBeat
{
    /// <summary>基准时长（毫秒）。</summary>
    public const int Ms = 600;

    /// <inheritdoc/>
    public override int DurationMs => Ms;
}

/// <summary>
/// 军势揭示节拍（settlement-show「军势揭示节拍」，tiered-number-show D2）：结算后含本次落子的棋串每条一个条目，按军势从小到大逐条依次播放，
/// 每条把算式一步步长成结果（前一条目的末步结束后下一条目才开始）。排在信物揭示之后、势力重算之前：算式对应提子完成后的棋串，数字紧接着流到势力栏。
/// 每步 0.22 秒，末步四档 0.44 秒、五档 0.66 秒；各步时长之和超过 1.6 秒时整拍取 1.6 秒，各步的开始时刻按比例压缩（整数运算，A7：高档定格一并压短）。
/// 本机玩家自己的结算同样完整播放——只有落子节拍压缩。
/// </summary>
public sealed record PowerRevealBeat(ImmutableArray<RevealEntry> Entries) : SettlementBeat
{
    /// <summary>每一步的基准时长（毫秒）；末步按档位取 <see cref="NumberTierStyle.FinalStepMs"/>。</summary>
    public const int StepMs = 220;

    /// <summary>整拍上限（毫秒）。</summary>
    public const int MaxMs = 1600;

    /// <summary>未压缩的各步时长之和（毫秒）。</summary>
    public int RawDurationMs => Entries.Sum(e => e.Steps.Sum(s => s.DurationMs));

    /// <inheritdoc/>
    public override int DurationMs => Math.Min(RawDurationMs, MaxMs);

    /// <summary>
    /// 第 <paramref name="entry"/> 个条目第 <paramref name="step"/> 步（都从 0 起）相对节拍开始的开始时刻（毫秒）：
    /// 未压缩的开始时刻 × 整拍 ÷ 各步之和（整数除法；未超上限时比例为 1，即未压缩的开始时刻本身）。
    /// </summary>
    public int StepStartMs(int entry, int step) => Scale(RawStartMs(entry, step));

    /// <summary>该步的结束时刻（毫秒）= 下一步的开始时刻；全拍最后一步为整拍时长。</summary>
    public int StepEndMs(int entry, int step) => Scale(RawStartMs(entry, step) + Entries[entry].Steps[step].DurationMs);

    /// <summary>
    /// 节拍内已过 <paramref name="elapsedMs"/> 毫秒时已经开始的各步的档位，按开始顺序（跨条目连续排）。负的已过时间为空；
    /// 传入不小于整拍时长的值即全部步骤。音效提示据此得知一帧里新开始了哪几步（design.md D7）。
    /// </summary>
    public ImmutableArray<int> StepTiersStartedBy(int elapsedMs)
    {
        ImmutableArray<int>.Builder tiers = ImmutableArray.CreateBuilder<int>();
        for (int e = 0; e < Entries.Length; e++)
        {
            for (int k = 0; k < Entries[e].Steps.Length; k++)
            {
                if (StepStartMs(e, k) > elapsedMs)
                {
                    return tiers.ToImmutable();
                }

                tiers.Add(Entries[e].Steps[k].Tier);
            }
        }

        return tiers.ToImmutable();
    }

    private long RawStartMs(int entry, int step)
    {
        long raw = 0;
        for (int e = 0; e < entry; e++)
        {
            raw += Entries[e].Steps.Sum(s => s.DurationMs);
        }

        for (int k = 0; k < step; k++)
        {
            raw += Entries[entry].Steps[k].DurationMs;
        }

        return raw;
    }

    private int Scale(long rawMs)
    {
        long total = RawDurationMs;
        return total <= 0 ? 0 : (int)(rawMs * DurationMs / total);
    }
}

/// <summary>势力重算节拍：每名有变化玩家分段到账（领地 0.35 秒 → 军势 0.35 秒 → 定格），整拍 0.9 秒。</summary>
public sealed record PowerBeat(ImmutableArray<PowerChange> Changes) : SettlementBeat
{
    /// <summary>基准时长（毫秒）。</summary>
    public const int Ms = 900;

    /// <summary>领地段 / 军势段各自的时长（毫秒）；分项为 0 的段跳过，时长让给定格。</summary>
    public const int SegmentMs = 350;

    /// <inheritdoc/>
    public override int DurationMs => Ms;
}

/// <summary>横幅节拍（settlement-show-callouts D3）：出局与终局各一条居中横幅，每条 0.8 秒依次显示；排在势力之后。</summary>
public sealed record BannerBeat(ImmutableArray<string> Banners) : SettlementBeat
{
    /// <summary>每条横幅的时长（毫秒）。</summary>
    public const int PerBannerMs = 800;

    /// <summary>终局横幅文案。</summary>
    public const string MatchEndedText = "对局结束";

    /// <inheritdoc/>
    public override int DurationMs => PerBannerMs * Banners.Length;

    /// <summary>出局横幅文案："金方出局"（阵营名经 <see cref="FactionTable"/>）。</summary>
    public static string EliminatedText(PlayerId player) => $"{FactionTable.For(player).Name}出局";
}

/// <summary>
/// 结算的一侧（结算前或结算后）供节拍生成读取的公开数据：盘面、势力读数、逐棋串军势明细（军势揭示的算式）、信物公开状态（揭示）、玩家状态（出局）、有无终局结果。
/// 全部取自公开快照，不含任何私有信息。
/// </summary>
public sealed record SettlementSide(
    GameBoard Board,
    ImmutableArray<PowerReading> Power,
    ImmutableArray<GroupPower> Groups,
    ImmutableArray<RelicPublicState> Relics,
    ImmutableArray<StatusReading> Statuses,
    bool HasResult)
{
    /// <summary>由公开快照投影。</summary>
    public static SettlementSide From(MatchPublicView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new SettlementSide(
            view.Board,
            SettlementBeats.Readings(view.Power),
            view.Power is { } power ? [.. power.Players.SelectMany(p => p.Groups)] : [],
            view.Relics.IsDefault ? [] : view.Relics,
            view.Players.IsDefault ? [] : [.. view.Players.Select(p => new StatusReading(p.Player, p.Status))],
            view.Result is not null);
    }

    /// <summary>只有盘面与势力读数的一侧（棋串、信物、玩家状态为空，无终局结果）。</summary>
    public static SettlementSide Of(GameBoard board, ImmutableArray<PowerReading> power)
    {
        ArgumentNullException.ThrowIfNull(board);
        return new SettlementSide(board, power.IsDefault ? [] : power, [], [], [], false);
    }
}

/// <summary>
/// 节拍生成（settlement-show「结算节拍序列的生成」，design.md D1 / D2）：把一次结算翻译成有序节拍序列的<b>纯函数</b>。
/// 输入只有结算前后两份公开快照与可得时的结算记录；不读时钟、不消费随机、不修改对局状态、不调用任何规则计算入口。
/// </summary>
/// <remarks>
/// <para>落子与被提棋子只凭两份快照的盘面之差得出：结算前空、结算后有子 = 落子；结算前有子、结算后空 = 被提。
/// 结算记录只用于给落子排序（放置顺序）；没有记录（AI 小回合经运行器推进）时按坐标字典序（<see cref="Coord.CompareTo"/>）。</para>
/// <para>势力与名次变化取两份快照势力明细之差（总势力、竞争名次、领地分、棋串军势）。结算前没有势力快照（本局首次结算前）时旧值按 0、旧名次按无。</para>
/// <para>信物揭示取两份快照信物公开状态之差（<c>IsRevealed</c> 假 → 真）；横幅取玩家状态之差（变为出局）与终局结果由无变有（settlement-show-callouts D1 / D3）。</para>
/// <para>军势揭示取"盘面之差得出的本次落子"与结算后快照的棋串军势明细（tiered-number-show D2）：数值一律原样转录，不重算军势。</para>
/// </remarks>
public static class SettlementBeats
{
    /// <summary>由结算前后两份公开快照（与可得时的结算记录）生成节拍序列。</summary>
    /// <param name="compressPlacement">本机玩家自己的结算：落子节拍压缩为一次整体确认闪动（design.md A2）。</param>
    public static ImmutableArray<SettlementBeat> Generate(MatchPublicView before, MatchPublicView after, CaptureRecord? record, bool compressPlacement = false)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        return Generate(SettlementSide.From(before), SettlementSide.From(after), record?.Placements, compressPlacement);
    }

    /// <summary>同上，输入已拆成盘面与势力读数（便于按设计文档算例直接构造；没有棋串、信物、玩家状态与终局信息）。</summary>
    /// <param name="placementOrder">结算记录里的放置序列（有则按其顺序排落子），<c>null</c> 即没有结算记录。</param>
    public static ImmutableArray<SettlementBeat> Generate(
        GameBoard before,
        GameBoard after,
        IReadOnlyList<PowerReading> powerBefore,
        IReadOnlyList<PowerReading> powerAfter,
        ImmutableArray<Placement>? placementOrder,
        bool compressPlacement = false)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(powerBefore);
        ArgumentNullException.ThrowIfNull(powerAfter);
        return Generate(SettlementSide.Of(before, [.. powerBefore]), SettlementSide.Of(after, [.. powerAfter]), placementOrder, compressPlacement);
    }

    /// <summary>
    /// 由结算前后两侧的公开数据生成节拍序列：落子 → 提子 → 信物揭示 → 军势揭示 → 势力重算 → 横幅，内容为空的节拍省略。
    /// <paramref name="compressPlacement"/> 只压缩落子节拍；军势揭示节拍不随之压缩（tiered-number-show D2）。
    /// </summary>
    public static ImmutableArray<SettlementBeat> Generate(SettlementSide before, SettlementSide after, ImmutableArray<Placement>? placementOrder, bool compressPlacement = false)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        ImmutableArray<SettlementBeat>.Builder beats = ImmutableArray.CreateBuilder<SettlementBeat>(6);

        ImmutableArray<PlacedPiece> placed = PlacedPieces(before.Board, after.Board, placementOrder);
        if (!placed.IsEmpty)
        {
            beats.Add(new PlacementBeat(placed, compressPlacement));
        }

        ImmutableArray<CapturedPiece> captured = CapturedPieces(before.Board, after.Board);
        if (!captured.IsEmpty)
        {
            beats.Add(new CaptureBeat(captured));
        }

        ImmutableArray<RevealedRelic> revealed = RevealedRelics(before.Relics, after.Relics);
        if (!revealed.IsEmpty)
        {
            beats.Add(new RelicRevealBeat(revealed));
        }

        ImmutableArray<RevealEntry> reveals = RevealEntries(placed, after.Groups);
        if (!reveals.IsEmpty)
        {
            beats.Add(new PowerRevealBeat(reveals));
        }

        ImmutableArray<PowerChange> changes = PowerChanges(before.Power, after.Power);
        if (!changes.IsEmpty)
        {
            beats.Add(new PowerBeat(changes));
        }

        ImmutableArray<string> banners = Banners(before.Statuses, after.Statuses, before.HasResult, after.HasResult);
        if (!banners.IsEmpty)
        {
            beats.Add(new BannerBeat(banners));
        }

        return beats.ToImmutable();
    }

    /// <summary>把势力快照投影成读数（玩家升序）。<c>null</c>（尚无势力快照）即空。</summary>
    public static ImmutableArray<PowerReading> Readings(PowerSnapshot? power)
    {
        if (power is null)
        {
            return [];
        }

        return [.. power.Players.OrderBy(p => p.Player.Value).Select(p => new PowerReading(p.Player, p.Total, power.RankOf(p.Player), p.TerritoryScore, p.GroupScore))];
    }

    /// <summary>本次结算新揭示的信物格（坐标字典序）：结算后已揭示且有内容，而结算前没有该格记录或尚未揭示。</summary>
    public static ImmutableArray<RevealedRelic> RevealedRelics(ImmutableArray<RelicPublicState> before, ImmutableArray<RelicPublicState> after)
    {
        var wasRevealed = new HashSet<Coord>();
        foreach (RelicPublicState relic in before.IsDefault ? [] : before)
        {
            if (relic.IsRevealed)
            {
                wasRevealed.Add(relic.Coord);
            }
        }

        var revealed = new SortedDictionary<Coord, RevealedRelic>();
        foreach (RelicPublicState relic in after.IsDefault ? [] : after)
        {
            if (relic.IsRevealed && relic.Content is { } content && !wasRevealed.Contains(relic.Coord))
            {
                revealed[relic.Coord] = new RevealedRelic(relic.Coord, Labels.RelicContent(content));
            }
        }

        return [.. revealed.Values];
    }

    /// <summary>横幅文案：状态变为出局的玩家各一条（玩家升序），再是终局结果由无变有的一条。弃赛不是结算效果，不产生横幅。</summary>
    public static ImmutableArray<string> Banners(ImmutableArray<StatusReading> before, ImmutableArray<StatusReading> after, bool hadResult, bool hasResult)
    {
        var was = new Dictionary<PlayerId, PlayerStatus>();
        foreach (StatusReading reading in before.IsDefault ? [] : before)
        {
            was[reading.Player] = reading.Status;
        }

        ImmutableArray<string>.Builder banners = ImmutableArray.CreateBuilder<string>();
        foreach (StatusReading now in (after.IsDefault ? [] : after).OrderBy(s => s.Player.Value))
        {
            bool wasEliminated = was.TryGetValue(now.Player, out PlayerStatus status) && status == PlayerStatus.Eliminated;
            if (now.Status == PlayerStatus.Eliminated && !wasEliminated)
            {
                banners.Add(BannerBeat.EliminatedText(now.Player));
            }
        }

        if (hasResult && !hadResult)
        {
            banners.Add(BannerBeat.MatchEndedText);
        }

        return banners.ToImmutable();
    }

    /// <summary>
    /// 军势揭示条目（tiered-number-show D2）：结算后含至少一枚本次落子的棋串各一条（同一条棋串落几枚都只揭示一次），按军势升序、同值按落点坐标序。
    /// 只读结算后一侧的棋串军势明细，不重算军势；没有落子或没有棋串明细时为空（节拍省略）。被提子后对手棋串的变化不揭示（A4）。
    /// </summary>
    public static ImmutableArray<RevealEntry> RevealEntries(ImmutableArray<PlacedPiece> placed, ImmutableArray<GroupPower> groups)
    {
        if (placed.IsDefaultOrEmpty || groups.IsDefaultOrEmpty)
        {
            return [];
        }

        var placedAt = new HashSet<Coord>(placed.Select(p => p.Coord));   // 只做成员判断，不遍历
        return
        [
            .. groups.Where(g => !g.Stones.IsDefaultOrEmpty && g.Stones.Any(placedAt.Contains))
                .Select(RevealEntry.From)
                .OrderBy(e => e.Power)
                .ThenBy(e => e.Coord),
        ];
    }

    private static ImmutableArray<PlacedPiece> PlacedPieces(GameBoard before, GameBoard after, ImmutableArray<Placement>? order)
    {
        var byCoord = new SortedDictionary<Coord, PlacedPiece>();
        foreach (Coord coord in after.AllCoords())
        {
            if (after[coord].Occupant is { } now && before[coord].Occupant is null)
            {
                byCoord[coord] = new PlacedPiece(coord, now.Type, now.Owner);
            }
        }

        if (byCoord.Count == 0)
        {
            return [];
        }

        if (order is not { } sequence)
        {
            return [.. byCoord.Values];
        }

        // 有结算记录：按放置顺序；记录里没有而盘面之差有的（理论上不会出现）按字典序补在后面，内容不缺项。
        ImmutableArray<PlacedPiece>.Builder ordered = ImmutableArray.CreateBuilder<PlacedPiece>(byCoord.Count);
        foreach (Placement placement in sequence)
        {
            if (byCoord.Remove(placement.Coord, out PlacedPiece piece))
            {
                ordered.Add(piece);
            }
        }

        ordered.AddRange(byCoord.Values);
        return ordered.ToImmutable();
    }

    private static ImmutableArray<CapturedPiece> CapturedPieces(GameBoard before, GameBoard after)
    {
        var byCoord = new SortedDictionary<Coord, CapturedPiece>();
        foreach (Coord coord in before.AllCoords())
        {
            if (before[coord].Occupant is { } was && after[coord].Occupant is null)
            {
                byCoord[coord] = new CapturedPiece(coord, was.Owner, was.Type);
            }
        }

        return [.. byCoord.Values];
    }

    private static ImmutableArray<PowerChange> PowerChanges(ImmutableArray<PowerReading> before, ImmutableArray<PowerReading> after)
    {
        var old = new Dictionary<PlayerId, PowerReading>();
        foreach (PowerReading reading in before.IsDefault ? [] : before)
        {
            old[reading.Player] = reading;
        }

        var changes = new SortedDictionary<int, PowerChange>();
        foreach (PowerReading now in after.IsDefault ? [] : after)
        {
            // 结算前没有该玩家的读数（本局首次结算前）：旧值、旧领地、旧军势都按 0，旧名次按无。
            PowerReading was = old.TryGetValue(now.Player, out PowerReading found) ? found : new PowerReading(now.Player, BigInteger.Zero, null, BigInteger.Zero, BigInteger.Zero);
            if (was.Total != now.Total || was.Rank != now.Rank)
            {
                changes[now.Player.Value] = new PowerChange(now.Player, was.Total, now.Total, was.Rank, now.Rank, now.Territory - was.Territory, now.GroupScore - was.GroupScore);
            }
        }

        return [.. changes.Values];
    }
}
