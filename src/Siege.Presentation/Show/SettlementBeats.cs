using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Relics;
using Siege.Core.Scoring;
using Siege.Presentation.Preview;
using Siege.Presentation.Style;
using Siege.Presentation.Text;

namespace Siege.Presentation.Show;

/// <summary>
/// 落子节拍里的一枚棋子：坐标、类型、归属（取自结算后公开快照的盘面），以及该格所在棋串在结算后快照里的军势短算式（settlement-show-callouts D1、裁决 1）。
/// <paramref name="Formula"/> 为 <c>null</c> 即结算后快照里没有含该格的棋串（理论上不会），飘字只显示类型名。
/// </summary>
public readonly record struct PlacedPiece(Coord Coord, PieceType Type, PlayerId Owner, string? Formula = null)
{
    /// <summary>飘字文案：棋子类型名 + " · " + 军势短算式（<see cref="GroupPowerView.ShortFormulaText"/>，与势力层 <see cref="GroupPowerView.FormulaText"/> 同一处生成、同一份数值）。</summary>
    public string CalloutText => Formula is null ? Labels.Piece(Type) : $"{Labels.Piece(Type)} · {Formula}";
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
/// 结算演出的一个节拍（design.md D3）。封闭集合：落子 → 提子 → 信物揭示 → 势力重算 → 横幅（settlement-show-callouts D3 插入后两种，不改已有节拍）。
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
/// 结算的一侧（结算前或结算后）供节拍生成读取的公开数据：盘面、势力读数、逐棋串军势明细（落子飘字的算式）、信物公开状态（揭示）、玩家状态（出局）、有无终局结果。
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

    /// <summary>由结算前后两侧的公开数据生成节拍序列：落子 → 提子 → 信物揭示 → 势力重算 → 横幅，内容为空的节拍省略。</summary>
    public static ImmutableArray<SettlementBeat> Generate(SettlementSide before, SettlementSide after, ImmutableArray<Placement>? placementOrder, bool compressPlacement = false)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        ImmutableArray<SettlementBeat>.Builder beats = ImmutableArray.CreateBuilder<SettlementBeat>(5);

        ImmutableArray<PlacedPiece> placed = PlacedPieces(before.Board, after.Board, after.Groups, placementOrder);
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

    private static ImmutableArray<PlacedPiece> PlacedPieces(GameBoard before, GameBoard after, ImmutableArray<GroupPower> groups, ImmutableArray<Placement>? order)
    {
        var byCoord = new SortedDictionary<Coord, PlacedPiece>();
        foreach (Coord coord in after.AllCoords())
        {
            if (after[coord].Occupant is { } now && before[coord].Occupant is null)
            {
                byCoord[coord] = new PlacedPiece(coord, now.Type, now.Owner, FormulaAt(groups, coord));
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

    /// <summary>该格所在棋串在结算后快照里的军势短算式：与势力层算式同一处生成（<see cref="GroupPowerView.From"/>，只列非零项）；没有含该格的棋串为 <c>null</c>。</summary>
    private static string? FormulaAt(ImmutableArray<GroupPower> groups, Coord coord)
    {
        if (groups.IsDefault)
        {
            return null;
        }

        foreach (GroupPower group in groups)
        {
            if (group.Stones.Contains(coord))
            {
                return GroupPowerView.From(group).ShortFormulaText;
            }
        }

        return null;
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
