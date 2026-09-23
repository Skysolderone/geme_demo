using System.Collections.Immutable;
using System.Numerics;
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

/// <summary>
/// 盘面层归属读法 / 势力层独占格的一格（障碍格不列出）。<see cref="Owner"/> 只在占据与独占时非空。
/// <see cref="Scored"/> 只在势力层有意义：独占但不计领地分的格（荒漠，terrain-surfaces）为 <c>false</c>，取自 Core 势力明细的 <see cref="PlayerPower.ScoredCells"/>；
/// 盘面层归属读法只读归属，恒为 <c>true</c>。
/// </summary>
public sealed record TerritoryCellView(Coord Coord, TerritoryState State, PlayerId? Owner, bool Scored = true);

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

/// <summary>
/// 盘面层棋串读法的一条棋串：轮廓（棋子集合）、全部气位、危险等级与<b>栅栏侧</b>。
/// </summary>
/// <param name="FenceSides">
/// 该棋串被栅栏堵住的那些侧（tactical-layers「棋串读法」：栅栏侧 MUST 与空侧可区分，玩家据此看出气边被堵在哪里）：
/// 地形里恰有一端落在 <paramref name="Stones"/> 上的边。同一条棋串的两枚子之间不可能有栅栏——立栅当场把棋串切成两条，
/// 所以"恰一端在串上"就是这条串全部的栅栏侧。本字段只做数据过滤（读 <see cref="TerrainData.Fences"/>），不算邻接、不判气。
/// 预置栅栏与本局立起的栅栏在这里<b>不区分</b>（information-visibility「改造结果公开」）。
/// </param>
/// <param name="Life">
/// 该棋串的活形状态（life-shape D6）：原样取自公开视图 <see cref="MatchPublicView.LifeShape"/>（与盘面同一时刻的那一份全量分析），本层不重算。
/// </param>
public sealed record LibertyGroupView(
    PlayerId Owner,
    ImmutableArray<Coord> Stones,
    ImmutableArray<Coord> Liberties,
    int LibertyCount,
    DangerLevel Level,
    ImmutableArray<FenceEdge> FenceSides,
    LifeState Life)
{
    /// <summary>
    /// 读法标记：已确定活形优先于危险——活形受活棋禁入与破坏活形保护、非所有者提不走，按气数标"危险"会误导（两眼活形往往只有两口气）。
    /// </summary>
    public GroupMark Mark => Life == LifeState.Alive ? GroupMark.Alive : Level == DangerLevel.Safe ? GroupMark.Normal : GroupMark.Danger;

    /// <summary>标记文案：已活为「已活」，危险为「危险」，普通为空串。</summary>
    public string MarkText => Labels.GroupMark(Mark);
}

/// <summary>盘面层的棋串读法。集合来自 Core 气快照（按气边导出）。两个内容 record 不合并——它们携带的字段本就不同（merge-board-layer D5）。</summary>
public sealed record LibertyLayerContent(ImmutableArray<LibertyGroupView> Groups, LibertyThresholds Thresholds, BoardReadingDiff Diff) : LayerContent(TacticalLayer.Board);

/// <summary>棋串读法里一条棋串的标记（tactical-layers「活形与禁入格的标示」）。外观形状见 <see cref="Style.GroupMarks.ShapeOf"/>。</summary>
public enum GroupMark
{
    /// <summary>普通棋串。</summary>
    Normal,

    /// <summary>危险棋串：气数 ≤ 危险阈值（含紧急）。</summary>
    Danger,

    /// <summary>已确定活形棋串。</summary>
    Alive,
}

// ---------- 势力层 ----------

/// <summary>势力层的一条棋串分数与倍率热区等级（= min(倍增子数量, <see cref="MaxHeatLevel"/>)，0 表示无倍率）。
/// 热区等级只是显示档位（柱高 / 着色），到 <see cref="MaxHeatLevel"/> 为止不再加高；它不是倍率封顶，军势与倍率文字始终取精确值。</summary>
public sealed record GroupScoreView(PlayerId Owner, ImmutableArray<Coord> Stones, GroupPowerView Power, int HeatLevel)
{
    /// <summary>热区显示档位上限。</summary>
    public const int MaxHeatLevel = 3;
}

/// <summary>
/// 势力层的玩家汇总。<see cref="TerritoryScore"/> 是领地分、<see cref="GroupScore"/> 是全部棋串军势之和（总势力 = 领地分 + 棋串军势，两项都取自 Core 势力明细）。
/// <see cref="CompactText"/> 给概览栏（≥ 10^6 缩写，<see cref="PowerNotation.Compact"/>），<see cref="ExactText"/> 给明细（精确值）。
/// </summary>
public sealed record PlayerPowerRowView(PlayerId Player, PlayerStatus Status, BigInteger Total, int TerritoryScore, BigInteger GroupScore, int? Rank, string? StatusText)
{
    /// <summary>概览文案，如「势力 1.23M（领地 12 + 棋串 1.23M）」。</summary>
    public string CompactText => Labels.PowerBreakdown(Total, TerritoryScore, GroupScore, compact: true);

    /// <summary>明细文案（精确值）。</summary>
    public string ExactText => Labels.PowerBreakdown(Total, TerritoryScore, GroupScore, compact: false);
}

/// <summary>
/// 势力层（tactical-layers「五种战术信息层」）：独占格着色（<see cref="Territory"/>）+ 领地分与棋串分（<see cref="Players"/>）+ 棋串分数（位置加值拆连珠 / 协同 / 高地）与倍率热区（<see cref="Groups"/>）。
/// <see cref="Territory"/> 只含独占空格（每格带独占者），直接投影势力明细的 <see cref="PlayerPower.ExclusiveCells"/>——即计分所用的那一份空格归属结果；
/// 争议格与中立格不在其中，因而 MUST NOT 被显示为任何玩家的得分（与独占格可区分）。
/// </summary>
public sealed record PowerLayerContent(
    ImmutableArray<GroupScoreView> Groups,
    ImmutableArray<PlayerPowerRowView> Players,
    ImmutableArray<TerritoryCellView> Territory) : LayerContent(TacticalLayer.Power);

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
public sealed record OrderRowView(PlayerId Player, int Rank, BigInteger Power, int Bonus, int Value, int PredictedPosition, string Explanation);

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
        ImmutableArray<FenceEdge> fences = [.. world.View.Board.Map.TerrainData.Fences];

        // 活形状态只读公开视图里那一份全量分析（与盘面同一时刻，PublicWorld.From 已钉住两份快照同源），本层不调 LifeShapeReport.Analyze。
        LifeShapeReport life = world.View.LifeShape;
        return new LibertyLayerContent(
            [
                .. world.Supplement.Liberties.Select(g =>
                {
                    var stones = g.Stones.ToHashSet();
                    return new LibertyGroupView(g.Owner, g.Stones, g.Liberties, g.Count, thresholds.Classify(g.Count),
                        [.. fences.Where(f => stones.Contains(f.A) || stones.Contains(f.B)).OrderBy(f => f.A).ThenBy(f => f.B)],
                        life.GroupLifeAt(g.Stones[0])?.Life
                            ?? throw new InvalidOperationException($"{g.Stones[0].ToNotation()} 在气快照里是棋子，活形报告里却是空格：两份快照不同源。"));
                }),
            ],
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

        if (map.HeightAt(source.Stone) - map.HeightAt(target) >= TerrainData.CliffDrop)
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
            // 插旗阶段尚无势力快照：棋串、玩家汇总与独占格都为空。
            return new PowerLayerContent([], [], []);
        }

        return new PowerLayerContent(
            [.. power.Players.SelectMany(p => p.Groups).Select(g => new GroupScoreView(g.Owner, g.Stones, GroupPowerView.From(g), Math.Min(g.MultiplierCount, GroupScoreView.MaxHeatLevel)))],
            [.. power.Players.Select(p => new PlayerPowerRowView(p.Player, p.Status, p.Total, p.TerritoryScore, p.GroupScore, power.RankOf(p.Player), Labels.Status(p.Status)))],
            [.. power.Players.SelectMany(p => p.ExclusiveCells.Select(c => new TerritoryCellView(c, TerritoryState.Exclusive, p.Player, p.ScoredCells.Contains(c)))).OrderBy(c => c.Coord)]);
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
