using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Preview;
using Siege.Core.Relics;
using Siege.Core.Scoring;
using Siege.Presentation.Preview;
using Siege.Presentation.Text;
using Siege.Presentation.Visibility;

namespace Siege.Presentation.Layers;

/// <summary>气层的危险等级（tactical-ui 裁决 2）。</summary>
public enum DangerLevel
{
    Safe,

    /// <summary>危险：气 ≤ 危险阈值。</summary>
    Danger,

    /// <summary>紧急：气 ≤ 紧急阈值。</summary>
    Urgent,

    /// <summary>无气：只会出现在自杀手预演里。</summary>
    NoLiberty,
}

/// <summary>危险棋串阈值（tactical-ui 裁决 2）：初值气 ≤ 2 危险、气 = 1 紧急，可配置，跑局后校准。这是显示阈值，不是规则。</summary>
public sealed record LibertyThresholds
{
    public LibertyThresholds(int danger = 2, int urgent = 1)
    {
        if (urgent < 1 || danger < urgent)
        {
            throw new ArgumentOutOfRangeException(nameof(danger), $"阈值须满足 1 ≤ 紧急({urgent}) ≤ 危险({danger})。");
        }

        Danger = danger;
        Urgent = urgent;
    }

    /// <summary>默认阈值。</summary>
    public static readonly LibertyThresholds Default = new();

    public int Danger { get; }

    public int Urgent { get; }

    /// <summary>按气数分级。</summary>
    public DangerLevel Classify(int liberties) =>
        liberties <= 0 ? DangerLevel.NoLiberty
        : liberties <= Urgent ? DangerLevel.Urgent
        : liberties <= Danger ? DangerLevel.Danger
        : DangerLevel.Safe;
}

/// <summary>信息层内容的基类。Godot 层按具体类型分派渲染。</summary>
public abstract record LayerContent(TacticalLayer Layer);

// ---------- 盘面层 · 归属读法 ----------

/// <summary>领地层四态（设计文档 §7.1 / §7.2），取自 Core 覆盖表的归属结果，不重算。</summary>
public enum TerritoryState
{
    Occupied,
    Exclusive,
    Contested,
    Neutral,
}

/// <summary>盘面层归属读法的一格（障碍格不列出）。<see cref="Owner"/> 只在占据与独占时非空。</summary>
public sealed record TerritoryCellView(Coord Coord, TerritoryState State, PlayerId? Owner);

/// <summary>盘面层的归属读法。集合来自 Core 覆盖表（按覆盖关系导出）；与棋串读法的差集见 <see cref="Diff"/>。</summary>
public sealed record TerritoryLayerContent(ImmutableArray<TerritoryCellView> Cells, BoardReadingDiff Diff) : LayerContent(TacticalLayer.Board);

// ---------- 盘面层 · 两种读法的差集 ----------

/// <summary>
/// 两种读法点亮的空格不一致时的地形原因（tactical-layers「差集可由地形解释」，design D-G）。
/// 被覆盖但不是气：<see cref="Cliff"/>（居高临下）、<see cref="Fence"/>（栅栏挡气不挡覆盖）、<see cref="AcrossWater"/>（隔一格深水覆盖对岸）；
/// 是气但未被覆盖：<see cref="Forest"/>（林地不接收覆盖）。
/// </summary>
public enum TerrainReason
{
    Cliff,
    Fence,
    AcrossWater,
    Forest,
}

/// <summary>差集中的一格及其全部地形原因（去重、按枚举序）。同一格可能同时有多个来源，各给各的原因。</summary>
public sealed record ReadingDiffCell(Coord Coord, ImmutableArray<TerrainReason> Reasons);

/// <summary>
/// 归属读法与棋串读法点亮的空格之差（design D-G）：
/// <see cref="CoveredNotLiberty"/> 是被覆盖（独占 / 争议）但不是任何棋串的气的空格；<see cref="LibertyNotCovered"/> 是某棋串的气但无人覆盖的空格。
/// 平地上两者都为空。原因只由地形数据查表得出（覆盖来源是否相邻、栅栏、高度、地表），本层不算邻接。
/// </summary>
public sealed record BoardReadingDiff(ImmutableArray<ReadingDiffCell> CoveredNotLiberty, ImmutableArray<ReadingDiffCell> LibertyNotCovered)
{
    /// <summary>两种读法点亮同一批空格。</summary>
    public static readonly BoardReadingDiff Empty = new([], []);

    /// <summary>是否无差集。</summary>
    public bool IsEmpty => CoveredNotLiberty.IsEmpty && LibertyNotCovered.IsEmpty;
}

// ---------- 盘面层 · 棋串读法 ----------

/// <summary>盘面层棋串读法的一条棋串：轮廓（棋子集合）、全部气位与危险等级。</summary>
public sealed record LibertyGroupView(PlayerId Owner, ImmutableArray<Coord> Stones, ImmutableArray<Coord> Liberties, int LibertyCount, DangerLevel Level);

/// <summary>盘面层的棋串读法。集合来自 Core 气快照（按气边导出）。两个内容 record 不合并——它们携带的字段本就不同（merge-board-layer D5）。</summary>
public sealed record LibertyLayerContent(ImmutableArray<LibertyGroupView> Groups, LibertyThresholds Thresholds, BoardReadingDiff Diff) : LayerContent(TacticalLayer.Board);

// ---------- 势力层 ----------

/// <summary>一个计入领地分的独占空格及其归属玩家。</summary>
public sealed record TerritoryContributionView(Coord Coord, PlayerId Owner);

/// <summary>势力层的一条棋串分数与倍率热区等级（= 生效倍率指数，0 表示无倍率）。</summary>
public sealed record GroupScoreView(PlayerId Owner, ImmutableArray<Coord> Stones, GroupPowerView Power, int HeatLevel);

/// <summary>势力层的玩家汇总。</summary>
public sealed record PlayerPowerRowView(PlayerId Player, PlayerStatus Status, long Total, int TerritoryScore, int? Rank, string? StatusText);

/// <summary>势力层。</summary>
public sealed record PowerLayerContent(
    ImmutableArray<TerritoryContributionView> TerritoryCells,
    ImmutableArray<GroupScoreView> Groups,
    ImmutableArray<PlayerPowerRowView> Players) : LayerContent(TacticalLayer.Power);

// ---------- 信物层 ----------

/// <summary>
/// 信物层的一格：分区与档位（地图公开信息，只在本层出现）、揭示状态、内容（仅已揭示）、控制与失效原因。
/// 未揭示时 <see cref="Content"/> 必为 <c>null</c>，文案为「未知信物」。
/// </summary>
public sealed record RelicCellView(
    Coord Coord,
    RelicZone Zone,
    BudgetTier Budget,
    string ZoneText,
    bool IsRevealed,
    RelicContent? Content,
    string ContentText,
    RelicControlKind Control,
    PlayerId? Holder,
    string ControlText,
    string? IneffectiveReason);

/// <summary>信物层。</summary>
public sealed record RelicLayerContent(ImmutableArray<RelicCellView> Relics) : LayerContent(TacticalLayer.Relics);

// ---------- 顺序层 ----------

/// <summary>顺序层的一行：势力名次、势力、先手修正、先手值、预测位置（1 起）与公式解释。</summary>
public sealed record OrderRowView(PlayerId Player, int Rank, long Power, int Bonus, int Value, int PredictedPosition, string Explanation);

/// <summary>顺序层（tactical-ui D7）：本轮顺序 + 若此刻结束本大回合的先手值明细与下一轮顺序预测。</summary>
public sealed record OrderLayerContent(
    int MajorRound,
    ImmutableArray<PlayerId> CurrentOrder,
    ImmutableArray<OrderRowView> Rows,
    ImmutableArray<PlayerId> PredictedNextOrder) : LayerContent(TacticalLayer.Order);

/// <summary>
/// 五种信息层内容的构建入口。只读 <see cref="PublicWorld"/>——结构上拿不到任何私有信息；
/// 全部数值取自 Core 发布的快照（覆盖表、势力明细、气快照、信物公开状态、先手值明细），不做任何规则计算。
/// </summary>
public static class TacticalLayers
{
    /// <summary>
    /// 某层当前该显示的内容。<paramref name="reading"/> 只对 <see cref="TacticalLayer.Board"/> 有意义，
    /// 其余层忽略它——读法是盘面层内部的维度，不参与层之间的互斥（merge-board-layer D5）。
    /// </summary>
    public static LayerContent Build(
        PublicWorld world,
        TacticalLayer layer,
        BoardReading reading = BoardReading.Ownership,
        LibertyThresholds? thresholds = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        return layer switch
        {
            TacticalLayer.Board when reading == BoardReading.Ownership => Territory(world),
            TacticalLayer.Board => Liberties(world, thresholds ?? LibertyThresholds.Default),
            TacticalLayer.Power => Power(world),
            TacticalLayer.Relics => Relics(world),
            TacticalLayer.Order => Order(world),
            _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, "未知信息层。"),
        };
    }

    public static TerritoryLayerContent Territory(PublicWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (world.View.Power is not { } power)
        {
            return new TerritoryLayerContent([], BoardReadingDiff.Empty);
        }

        ImmutableArray<TerritoryCellView>.Builder cells = ImmutableArray.CreateBuilder<TerritoryCellView>();
        foreach (Coord c in world.View.Board.AllCoords())
        {
            CellOwnership ownership = power.Coverage.OwnershipOf(c);
            TerritoryState? state = ownership.Kind switch
            {
                OwnershipKind.Obstacle => null,
                OwnershipKind.Occupied => TerritoryState.Occupied,
                OwnershipKind.Exclusive => TerritoryState.Exclusive,
                OwnershipKind.Contested => TerritoryState.Contested,
                OwnershipKind.Neutral => TerritoryState.Neutral,
                _ => throw new ArgumentOutOfRangeException(nameof(world), ownership.Kind, "未知归属。"),
            };
            if (state is { } s)
            {
                cells.Add(new TerritoryCellView(c, s, ownership.Owner));
            }
        }

        return new TerritoryLayerContent(cells.ToImmutable(), ReadingDiff(world));
    }

    public static LibertyLayerContent Liberties(PublicWorld world, LibertyThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(thresholds);
        return new LibertyLayerContent(
            [.. world.Supplement.Liberties.Select(g => new LibertyGroupView(g.Owner, g.Stones, g.Liberties, g.Count, thresholds.Classify(g.Count)))],
            thresholds,
            ReadingDiff(world));
    }

    /// <summary>
    /// 两种读法的差集及其地形原因。被覆盖的空格取覆盖表的独占 / 争议格，气取气快照里全部棋串的气；
    /// 每个差集格的原因由 Core 给出的覆盖来源（<see cref="CoverageMap.SourcesOf"/>，含"是否相邻"一位）与地图数据查表得出：
    /// 来源不相邻 → 隔岸；相邻且有栅栏 → 栅栏；相邻且来源比目标高 2 → 崖壁；是气而无人覆盖 → 林地。
    /// 四条都不命中说明 Core 的覆盖 / 气边关系与规格不一致，直接抛出而不是静默吞掉。
    /// </summary>
    public static BoardReadingDiff ReadingDiff(PublicWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (world.View.Power is not { } power)
        {
            return BoardReadingDiff.Empty;
        }

        MapData map = world.View.Board.Map;
        HashSet<Coord> liberties = [.. world.Supplement.Liberties.SelectMany(g => g.Liberties)];
        HashSet<Coord> covered = [.. world.View.Board.AllCoords().Where(c => power.Coverage.OwnershipOf(c).Kind is OwnershipKind.Exclusive or OwnershipKind.Contested)];

        ImmutableArray<ReadingDiffCell> coveredNotLiberty =
        [
            .. covered.Except(liberties).OrderBy(c => c).Select(c =>
            {
                ImmutableArray<TerrainReason> reasons = [.. power.Coverage.SourcesOf(c).Select(s => ReasonFor(map, s, c)).Distinct().OrderBy(r => r)];
                return reasons.IsEmpty
                    ? throw new InvalidOperationException($"{c.ToNotation()} 被覆盖但不是气，却没有任何覆盖来源。")
                    : new ReadingDiffCell(c, reasons);
            }),
        ];

        ImmutableArray<ReadingDiffCell> libertyNotCovered =
        [
            .. liberties.Except(covered).OrderBy(c => c).Select(c => map.SurfaceAt(c) == Surface.Forest
                ? new ReadingDiffCell(c, [TerrainReason.Forest])
                : throw new InvalidOperationException($"{c.ToNotation()} 是气但未被覆盖，且不是林地：气边与覆盖关系不一致。")),
        ];

        return coveredNotLiberty.IsEmpty && libertyNotCovered.IsEmpty ? BoardReadingDiff.Empty : new BoardReadingDiff(coveredNotLiberty, libertyNotCovered);
    }

    private static TerrainReason ReasonFor(MapData map, CoverageSource source, Coord target)
    {
        if (!source.Adjacent)
        {
            return TerrainReason.AcrossWater;
        }

        if (map.HasFence(source.Stone, target))
        {
            return TerrainReason.Fence;
        }

        if (map.HeightAt(source.Stone) - map.HeightAt(target) >= 2)
        {
            return TerrainReason.Cliff;
        }

        throw new InvalidOperationException($"{source.Stone.ToNotation()} 覆盖相邻的 {target.ToNotation()} 却不构成气边，且既无栅栏也非崖壁：气边与覆盖关系不一致。");
    }

    public static PowerLayerContent Power(PublicWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (world.View.Power is not { } power)
        {
            return new PowerLayerContent([], [], []);
        }

        return new PowerLayerContent(
            [.. power.Players.SelectMany(p => p.ExclusiveCells.Select(c => new TerritoryContributionView(c, p.Player))).OrderBy(t => t.Coord)],
            [.. power.Players.SelectMany(p => p.Groups).Select(g => new GroupScoreView(g.Owner, g.Stones, GroupPowerView.From(g), g.EffectiveMultiplierCount))],
            [.. power.Players.Select(p => new PlayerPowerRowView(p.Player, p.Status, p.Total, p.TerritoryScore, power.RankOf(p.Player), Labels.Status(p.Status)))]);
    }

    public static RelicLayerContent Relics(PublicWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var statusOf = world.View.Players.ToDictionary(p => p.Player, p => p.Status);
        return new RelicLayerContent([.. world.View.Relics.Select(r => RelicCell(r, statusOf))]);
    }

    private static RelicCellView RelicCell(RelicPublicState r, IReadOnlyDictionary<PlayerId, PlayerStatus> statusOf)
    {
        RelicContent? content = r.IsRevealed ? r.Content : null;
        string contentText = content is { } c ? Labels.RelicContent(c) : "未知信物";
        (string control, string? reason) = r.Control.Kind switch
        {
            RelicControlKind.Uncontrolled => ("无人控制", "无人控制：不向任何玩家提供效果"),
            RelicControlKind.Contested => ("争议", "争议：多名玩家同时覆盖，不向任何玩家提供效果"),
            RelicControlKind.Controlled => ($"由 {Labels.Player(r.Control.Holder!.Value)} 控制", (string?)null),
            RelicControlKind.Blocked => BlockedText(r.Control.Holder!.Value, statusOf),
            _ => throw new ArgumentOutOfRangeException(nameof(r), r.Control.Kind, "未知控制状态。"),
        };

        return new RelicCellView(r.Coord, r.Spec.Zone, r.Spec.Budget, $"{Labels.Zone(r.Spec.Zone)} · {Labels.Budget(r.Spec.Budget)}",
            r.IsRevealed, content, contentText, r.Control.Kind, r.Control.Holder, control, reason);
    }

    private static (string Control, string? Reason) BlockedText(PlayerId holder, IReadOnlyDictionary<PlayerId, PlayerStatus> statusOf)
    {
        string state = statusOf.TryGetValue(holder, out PlayerStatus status) && status == PlayerStatus.Eliminated ? "已出局" : "已弃赛";
        return ($"由{state}玩家 {Labels.Player(holder)} 控制（封锁）", $"封锁：控制者{state}，不向任何玩家提供效果");
    }

    public static OrderLayerContent Order(PublicWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (world.Supplement.NextOrderForecast is not { } forecast)
        {
            return new OrderLayerContent(world.View.MajorRound, world.View.ActionOrder, [], []);
        }

        return new OrderLayerContent(
            world.View.MajorRound,
            world.View.ActionOrder,
            [.. forecast.Entries.Select((e, i) => OrderRow(e, i, forecast.ActiveCount))],
            forecast.NextOrder);
    }

    private static OrderRowView OrderRow(InitiativeEntry e, int index, int activeCount) =>
        new(e.Player, e.Rank, e.Power, e.Bonus, e.Value, index + 1,
            $"先手值 {e.Value} =（参赛 {activeCount} − 势力名次 {e.Rank}）+ 先手修正 {e.Bonus}；预测第 {index + 1} 位");
}
