using System.Collections.Immutable;

namespace Siege.Core.Board;

/// <summary>一条地图校验失败。</summary>
public readonly record struct MapValidationFailure(
    string Code,
    string Message,
    ImmutableArray<Coord> Coords)
{
    public override string ToString() =>
        Coords.IsDefaultOrEmpty
            ? $"[{Code}] {Message}"
            : $"[{Code}] {Message}（{string.Join(", ", Coords.Select(c => c.ToNotation()))}）";
}

/// <summary>规则第 1 条的一项距离均衡目标：目标名、目标格、各出生区（按编号）到最近目标格的沿气边距离，<c>null</c> 为不可达或无目标。</summary>
public readonly record struct BirthZoneDistance(string Name, ImmutableArray<Coord> Targets, ImmutableArray<int?> Distances);

/// <summary>地图校验结果。</summary>
public sealed class MapValidationResult
{
    internal MapValidationResult(ImmutableArray<MapValidationFailure> failures) => Failures = failures;

    public ImmutableArray<MapValidationFailure> Failures { get; }

    public bool IsValid => Failures.IsEmpty;

    public override string ToString() =>
        IsValid ? "地图校验通过。" : string.Join(Environment.NewLine, Failures);
}

/// <summary>地图校验失败时抛出。</summary>
public sealed class MapValidationException : Exception
{
    public MapValidationException(MapValidationResult result)
        : base($"地图校验未通过：{Environment.NewLine}{result}") => Result = result;

    public MapValidationResult Result { get; }
}

/// <summary>
/// 地图静态校验。把必死口袋、距离失衡、出生区被崖壁封死这类问题挡在加载期——
/// 漏到对局中会表现为"某玩家莫名其妙被迫 Pass"，极难归因。
/// 距离与连通区一律沿气边（<see cref="Adjacency.LibertyNeighbors"/>）：崖壁、栅栏、未架桥深水挡住的路不算路。
/// "障碍占外接区域 25%–35%"在 terrain-model 取消：深水与崖壁同样在压缩空间，只数障碍已无意义，密度由可落子格区间把控。
/// </summary>
/// <remarks>规格：openspec/changes/terrain-model/specs/map-definition —— Requirement: 地图静态校验规则</remarks>
public static class MapValidator
{
    /// <summary>
    /// 人数适配预算：可落子格区间、信物格区间、出生区可落子格区间、据点数区间（scoring-sites：2 人 6–8 / 3 人 9–11 / 4 人 10–14，2 / 3 人为估值）。
    /// 出生区区间为 <c>null</c> 表示规格未给该人数的区间（2 / 3 人图尚未定稿），此项不校验；
    /// 与之无关的"容得下 9 枚基础部署"下界对所有人数一律生效。
    /// </summary>
    private static readonly ImmutableDictionary<int, Budget> Budgets =
        new Dictionary<int, Budget>
        {
            [2] = new(50, 65, 7, 9, null, 6, 8),
            [3] = new(75, 90, 10, 12, null, 9, 11),
            [4] = new(95, 110, 13, 15, (12, 14), 10, 14),
        }.ToImmutableDictionary();

    /// <summary>一档人数的规模预算。</summary>
    private readonly record struct Budget(
        int MinPlayable,
        int MaxPlayable,
        int MinRelics,
        int MaxRelics,
        (int Min, int Max)? BirthZoneCells,
        int MinSites,
        int MaxSites);

    /// <summary>前三大回合单玩家最多 9 枚基础部署，出生区必须容得下。</summary>
    private const int ProtectionPhaseDeployments = 9;

    public static MapValidationResult Validate(MapData map)
    {
        ArgumentNullException.ThrowIfNull(map);
        ImmutableArray<MapValidationFailure>.Builder f = ImmutableArray.CreateBuilder<MapValidationFailure>();

        ValidateStructure(map, f);
        ValidateTerrainBounds(map, f);
        ValidatePlayableCount(map, f);
        ValidateBudgets(map, f);
        ValidateBirthZones(map, f);
        ValidateRelicCells(map, f);
        ValidateSites(map, f);
        ValidateLandmarks(map, f);
        ValidateTolerance(map, f);
        ValidatePockets(map, f);
        ValidateBirthZoneConnectivity(map, f);
        ValidateDistanceBalance(map, f);

        return new MapValidationResult(f.ToImmutable());
    }

    /// <summary>
    /// 结构性前置校验。这些问题若不先报出来，后面的规则只会报出症状
    /// （"可落子格为 0"），把根因藏在一堆派生失败里。
    /// </summary>
    private static void ValidateStructure(MapData map, ImmutableArray<MapValidationFailure>.Builder f)
    {
        if (map.Width <= 0 || map.Height <= 0)
        {
            f.Add(new MapValidationFailure(
                "MAP_DIMENSION_INVALID",
                $"地图外接尺寸必须为正，实际为 {map.Width}×{map.Height}。",
                ImmutableArray<Coord>.Empty));
        }

        if (string.IsNullOrWhiteSpace(map.Id))
        {
            f.Add(new MapValidationFailure(
                "MAP_ID_MISSING",
                "地图必须有标识：对局日志按地图标识归档（设计文档 §17）。",
                ImmutableArray<Coord>.Empty));
        }

        // 越界障碍不参与任何校验，等于被静默丢弃——占比、口袋、距离全都算不到它。
        ImmutableArray<Coord> outside = map.Obstacles.Where(c => !map.Contains(c)).Order().ToImmutableArray();
        if (!outside.IsEmpty)
        {
            f.Add(new MapValidationFailure(
                "OBSTACLE_OUT_OF_BOUNDS", "障碍格越界，不会参与任何校验。", outside));
        }
    }

    /// <summary>
    /// 地形坐标必须在盘内：高度 / 地表 / 桥 / 栅栏两端。越界的地形项不参与任何查询，等于被静默丢弃。
    /// "桥必须在深水格上、栅栏必须在相邻格之间"（规则第 6 条）已由 <see cref="TerrainData"/> 构造期强制，
    /// 到不了这里（terrain-model 裁决 A-1），此处不重复。
    /// </summary>
    private static void ValidateTerrainBounds(MapData map, ImmutableArray<MapValidationFailure>.Builder f)
    {
        TerrainData t = map.TerrainData;
        ImmutableArray<Coord> outside = t.Heights.Keys
            .Concat(t.Surfaces.Keys)
            .Concat(t.Bridges)
            .Concat(t.Fences.SelectMany(fence => new[] { fence.A, fence.B }))
            .Where(c => !map.Contains(c))
            .Distinct()
            .Order()
            .ToImmutableArray();
        if (!outside.IsEmpty)
        {
            f.Add(new MapValidationFailure(
                "TERRAIN_OUT_OF_BOUNDS", "地形数据（高度 / 地表 / 桥 / 栅栏端点）含越界格，不会参与任何查询。", outside));
        }
    }

    /// <summary>
    /// 校验规则第 5 条：可落子格总数必须落在该人数的预算区间内（4 人 95–110，terrain-model 裁决 D18）。
    /// 单列成一步而不是混在信物 / 出生区预算里——改图时越界是最容易发生、又最难在对局中归因的一类错误
    /// （表现为"密度不对、冲突时点漂移"，而不是任何一条规则报错）。denser-map 裁决 5。
    /// </summary>
    private static void ValidatePlayableCount(MapData map, ImmutableArray<MapValidationFailure>.Builder f)
    {
        if (!Budgets.TryGetValue(map.MaxPlayers, out Budget budget))
        {
            return;
        }

        int playable = map.PlayableCount;
        if (playable < budget.MinPlayable || playable > budget.MaxPlayable)
        {
            f.Add(new MapValidationFailure(
                "PLAYABLE_COUNT_OUT_OF_RANGE",
                $"{map.MaxPlayers} 人地图的可落子格为 {playable}，超出 {budget.MinPlayable}–{budget.MaxPlayable} 区间。",
                ImmutableArray<Coord>.Empty));
        }
    }

    private static void ValidateBudgets(MapData map, ImmutableArray<MapValidationFailure>.Builder f)
    {
        if (!Budgets.TryGetValue(map.MaxPlayers, out Budget budget))
        {
            f.Add(new MapValidationFailure(
                "UNSUPPORTED_PLAYER_COUNT",
                $"不支持的人数 {map.MaxPlayers}：只提供 2 / 3 / 4 人的预算表。",
                ImmutableArray<Coord>.Empty));
            return;
        }

        int relics = map.RelicCells.Count;
        if (relics < budget.MinRelics || relics > budget.MaxRelics)
        {
            // 规格的 Scenario 要求报出方向（"信物格少于 7"），不只是"超出区间"。
            string direction = relics < budget.MinRelics
                ? $"少于下限 {budget.MinRelics}"
                : $"多于上限 {budget.MaxRelics}";
            f.Add(new MapValidationFailure(
                "RELIC_COUNT_OUT_OF_RANGE",
                $"{map.MaxPlayers} 人地图的信物格为 {relics}，{direction}，超出 {budget.MinRelics}–{budget.MaxRelics} 区间。",
                ImmutableArray<Coord>.Empty));
        }

        if (map.BirthZones.Length != map.MaxPlayers)
        {
            f.Add(new MapValidationFailure(
                "BIRTH_ZONE_COUNT_MISMATCH",
                $"出生区数量 {map.BirthZones.Length} 必须等于该地图支持的最大人数 {map.MaxPlayers}。",
                ImmutableArray<Coord>.Empty));
        }
    }

    private static void ValidateBirthZones(MapData map, ImmutableArray<MapValidationFailure>.Builder f)
    {
        for (int i = 0; i < map.BirthZones.Length; i++)
        {
            ImmutableHashSet<Coord> zone = map.BirthZones[i];
            ImmutableArray<Coord> outside = zone.Where(c => !map.Contains(c)).Order().ToImmutableArray();
            if (!outside.IsEmpty)
            {
                f.Add(new MapValidationFailure(
                    "BIRTH_ZONE_OUT_OF_BOUNDS", $"{BirthZoneLabel.Of(i)} 含越界格。", outside));
            }

            int playable = zone.Count(map.IsPlayable);
            if (playable < ProtectionPhaseDeployments)
            {
                f.Add(new MapValidationFailure(
                    "BIRTH_ZONE_TOO_SMALL",
                    $"{BirthZoneLabel.Of(i)} 只有 {playable} 个可落子格，容不下前三大回合最多 {ProtectionPhaseDeployments} 枚基础部署。",
                    ImmutableArray<Coord>.Empty));
            }

            // 出生区内可以有障碍或深水，但不得把该区压到区间之外（denser-map：4 人图每区 12–14 格）。
            if (Budgets.TryGetValue(map.MaxPlayers, out Budget b)
                && b.BirthZoneCells is { } range
                && (playable < range.Min || playable > range.Max))
            {
                f.Add(new MapValidationFailure(
                    "BIRTH_ZONE_SIZE_OUT_OF_RANGE",
                    $"{BirthZoneLabel.Of(i)} 有 {playable} 个可落子格，超出 {map.MaxPlayers} 人地图的 {range.Min}–{range.Max} 区间。",
                    ImmutableArray<Coord>.Empty));
            }

            for (int j = i + 1; j < map.BirthZones.Length; j++)
            {
                ImmutableArray<Coord> overlap = zone.Intersect(map.BirthZones[j]).Order().ToImmutableArray();
                if (!overlap.IsEmpty)
                {
                    f.Add(new MapValidationFailure(
                        "BIRTH_ZONE_OVERLAP", $"{BirthZoneLabel.Of(i)} 与 {BirthZoneLabel.Number(j)} 重叠。", overlap));
                }
            }
        }
    }

    private static void ValidateRelicCells(MapData map, ImmutableArray<MapValidationFailure>.Builder f)
    {
        foreach ((Coord c, RelicCellSpec spec) in map.RelicCells.OrderBy(kv => kv.Key))
        {
            if (map.TerrainAt(c) != Terrain.Playable)
            {
                f.Add(new MapValidationFailure(
                    "RELIC_ON_NON_PLAYABLE", "信物格必须位于可落子格上。", [c]));
                continue;
            }

            int? zone = map.BirthZoneOf(c);
            bool inBirthZone = zone is not null;

            if (inBirthZone && spec.Zone != RelicZone.BirthZone)
            {
                f.Add(new MapValidationFailure(
                    "RELIC_ZONE_MISMATCH",
                    $"位于{BirthZoneLabel.Of(zone!.Value)} 的信物格被标注为 {spec.Zone}。", [c]));
            }
            else if (!inBirthZone && spec.Zone != RelicZone.Contested)
            {
                f.Add(new MapValidationFailure(
                    "RELIC_ZONE_MISMATCH",
                    $"位于公共区的信物格被标注为 {spec.Zone}。", [c]));
            }

            // 出生区不生成高阶信物；公共区的预算档位必须严格高于出生区。
            if (spec.Zone == RelicZone.BirthZone && spec.Budget != BudgetTier.Birth)
            {
                f.Add(new MapValidationFailure(
                    "RELIC_BUDGET_MISMATCH",
                    $"出生区信物格的预算档位必须是 {BudgetTier.Birth}，实际为 {spec.Budget}。", [c]));
            }

            if (spec.Zone == RelicZone.Contested && spec.Budget <= BudgetTier.Birth)
            {
                f.Add(new MapValidationFailure(
                    "RELIC_BUDGET_MISMATCH",
                    $"公共争夺区信物格的预算档位必须严格高于出生区，实际为 {spec.Budget}。", [c]));
            }
        }
    }

    /// <summary>
    /// 校验规则第 8 条：每个据点位于可落子格、标注合法档位、不与信物格重合；据点总数落在人数区间。
    /// 据点与信物完全分离（裁决 D10）：同一格两种价值会让"控制一格"的收益翻倍，且表现层地标互相遮挡。
    /// </summary>
    private static void ValidateSites(MapData map, ImmutableArray<MapValidationFailure>.Builder f)
    {
        foreach ((Coord c, SiteTier tier) in map.Sites.OrderBy(kv => kv.Key))
        {
            if (!Enum.IsDefined(tier))
            {
                f.Add(new MapValidationFailure(
                    "SITE_TIER_MISSING", $"据点必须标注档位（营帐 / 篝火 / 石碑），实际为 {(int)tier}。", [c]));
            }

            if (map.TerrainAt(c) != Terrain.Playable)
            {
                f.Add(new MapValidationFailure(
                    "SITE_ON_NON_PLAYABLE", "据点必须位于可落子格上。", [c]));
            }

            if (map.RelicCells.ContainsKey(c))
            {
                f.Add(new MapValidationFailure(
                    "SITE_ON_RELIC_CELL", "据点不得与信物格重合。", [c]));
            }
        }

        if (!Budgets.TryGetValue(map.MaxPlayers, out Budget budget))
        {
            return;
        }

        int sites = map.Sites.Count;
        if (sites < budget.MinSites || sites > budget.MaxSites)
        {
            string direction = sites < budget.MinSites
                ? $"少于下限 {budget.MinSites}"
                : $"多于上限 {budget.MaxSites}";
            f.Add(new MapValidationFailure(
                "SITE_COUNT_OUT_OF_RANGE",
                $"{map.MaxPlayers} 人地图的据点为 {sites} 个，{direction}，超出 {budget.MinSites}–{budget.MaxSites} 区间。",
                ImmutableArray<Coord>.Empty));
        }
    }

    private static void ValidateLandmarks(MapData map, ImmutableArray<MapValidationFailure>.Builder f)
    {
        if (map.TerrainAt(map.CentralEntrance) != Terrain.Playable)
        {
            f.Add(new MapValidationFailure(
                "CENTRAL_ENTRANCE_NOT_PLAYABLE", "中央入口必须位于可落子格上。", [map.CentralEntrance]));
        }

        ImmutableArray<Coord> badChokes = map.ChokePoints
            .Where(c => map.TerrainAt(c) != Terrain.Playable).Order().ToImmutableArray();
        if (!badChokes.IsEmpty)
        {
            f.Add(new MapValidationFailure(
                "CHOKE_NOT_PLAYABLE", "标注的咽喉格必须位于可落子格上。", badChokes));
        }

        if (map.ChokePoints.IsEmpty)
        {
            f.Add(new MapValidationFailure(
                "CHOKE_NOT_ANNOTATED",
                "地图必须显式标注主要咽喉格——不做自动识别，自动识别的错误会静默污染距离均衡校验。",
                ImmutableArray<Coord>.Empty));
        }
    }

    private static void ValidateTolerance(MapData map, ImmutableArray<MapValidationFailure>.Builder f)
    {
        const int defaultTolerance = 1;
        if (map.DistanceTolerance < 0)
        {
            f.Add(new MapValidationFailure(
                "TOLERANCE_NEGATIVE", "距离容差不得为负。", ImmutableArray<Coord>.Empty));
        }

        if (map.DistanceTolerance > defaultTolerance && string.IsNullOrWhiteSpace(map.ToleranceRelaxReason))
        {
            f.Add(new MapValidationFailure(
                "TOLERANCE_RELAX_WITHOUT_REASON",
                $"距离容差放宽到 {map.DistanceTolerance}（默认 {defaultTolerance}）必须同时写明理由。",
                ImmutableArray<Coord>.Empty));
        }

        foreach (Coord c in map.PocketExemptions.Order())
        {
            if (!map.PocketExemptionReasons.ContainsKey(c))
            {
                f.Add(new MapValidationFailure(
                    "POCKET_EXEMPTION_WITHOUT_REASON", "每条必死口袋豁免都必须写明理由。", [c]));
            }
        }
    }

    /// <summary>
    /// 必死口袋：出生区内被障碍、崖壁、深水、栅栏或边界围成的沿气边连通空区，面积小于
    /// <see cref="MapData.MinTwoEyeArea"/> 即判失败，除非有显式豁免。
    /// 这是保守近似——不做完整死活判定，宁可误报由设计师确认，也不放过对局中卡死的风险。
    /// </summary>
    private static void ValidatePockets(MapData map, ImmutableArray<MapValidationFailure>.Builder f)
    {
        var visited = new HashSet<Coord>();
        foreach (Coord start in map.AllCoords())
        {
            if (visited.Contains(start) || !map.IsPlayable(start))
            {
                continue;
            }

            ImmutableArray<Coord> component = FloodFill(map, start, visited);
            bool touchesBirthZone = component.Any(c => map.BirthZoneOf(c) is not null);
            if (!touchesBirthZone || component.Length >= map.MinTwoEyeArea)
            {
                continue;
            }

            if (component.Any(map.PocketExemptions.Contains))
            {
                continue;
            }

            f.Add(new MapValidationFailure(
                "DEAD_POCKET",
                $"出生区内存在面积 {component.Length} 的必死口袋，小于形成两眼所需的 {map.MinTwoEyeArea} 格。",
                component));
        }
    }

    /// <summary>
    /// 校验规则第 7 条：每个出生区至少有一条沿气边到中央入口的通路。高台出生区若四周全是崖壁 / 深水 / 栅栏，
    /// 对局根本无法开始。与第 1 条的"中央入口不可达"是同一根因的两条规则，各自报出（后者带的是距离口径）。
    /// </summary>
    private static void ValidateBirthZoneConnectivity(MapData map, ImmutableArray<MapValidationFailure>.Builder f)
    {
        for (int z = 0; z < map.BirthZones.Length; z++)
        {
            Dictionary<Coord, int> dist = MultiSourceDistances(map, map.BirthZones[z]);
            if (dist.ContainsKey(map.CentralEntrance))
            {
                continue;
            }

            f.Add(new MapValidationFailure(
                "BIRTH_ZONE_ISOLATED",
                $"{BirthZoneLabel.Of(z)} 没有任何一条沿气边到中央入口 {map.CentralEntrance.ToNotation()} 的通路：它的边缘全是崖壁、深水、栅栏或障碍。",
                map.BirthZones[z].Where(map.IsPlayable).Order().ToImmutableArray()));
        }
    }

    /// <summary>
    /// 各出生区到每个距离均衡目标的最短落子距离（沿气边），规则第 1 条的唯一口径。
    /// 目标依次为：最近公共信物、中央入口、最近咽喉、最近篝火、最近石碑（后两项 scoring-sites R-5；营帐在区内，不纳入）。
    /// 距离为 <c>null</c> 表示该出生区到不了任一目标格；目标集合为空时（如无据点的旧图）整项距离全为 <c>null</c>。
    /// 校验器与 <c>Siege.Sim map</c> 的距离表共用这一份计算。
    /// </summary>
    public static ImmutableArray<BirthZoneDistance> DistanceTable(MapData map)
    {
        ArgumentNullException.ThrowIfNull(map);

        ImmutableArray<Coord> publicRelics = map.RelicCells
            .Where(kv => kv.Value.Zone == RelicZone.Contested)
            .Select(kv => kv.Key).Order().ToImmutableArray();

        var metrics = new (string Name, ImmutableArray<Coord> Targets)[]
        {
            ("最近公共信物", publicRelics),
            ("中央入口", [map.CentralEntrance]),
            ("最近咽喉", map.ChokePoints.Order().ToImmutableArray()),
            ("最近篝火", SitesOf(map, SiteTier.Campfire)),
            ("最近石碑", SitesOf(map, SiteTier.Stele)),
        };

        Dictionary<Coord, int>[] zoneDistances = map.BirthZones.IsDefault
            ? []
            : [.. map.BirthZones.Select(z => MultiSourceDistances(map, z))];

        ImmutableArray<BirthZoneDistance>.Builder table = ImmutableArray.CreateBuilder<BirthZoneDistance>(metrics.Length);
        foreach ((string name, ImmutableArray<Coord> targets) in metrics)
        {
            ImmutableArray<int?>.Builder ds = ImmutableArray.CreateBuilder<int?>(zoneDistances.Length);
            foreach (Dictionary<Coord, int> dist in zoneDistances)
            {
                int? best = null;
                foreach (Coord t in targets)
                {
                    if (dist.TryGetValue(t, out int d) && (best is null || d < best))
                    {
                        best = d;
                    }
                }

                ds.Add(best);
            }

            table.Add(new BirthZoneDistance(name, targets, ds.ToImmutable()));
        }

        return table.ToImmutable();
    }

    private static ImmutableArray<Coord> SitesOf(MapData map, SiteTier tier) =>
        map.Sites.Where(kv => kv.Value == tier).Select(kv => kv.Key).Order().ToImmutableArray();

    /// <summary>
    /// 距离均衡：各出生区到最近公共信物格、中央入口、主要咽喉、最近篝火与最近石碑的最短落子距离
    /// （沿气边——崖壁、栅栏、未架桥深水挡住的路不算路），两两差值不得超过容差。
    /// </summary>
    private static void ValidateDistanceBalance(MapData map, ImmutableArray<MapValidationFailure>.Builder f)
    {
        if (map.BirthZones.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (BirthZoneDistance metric in DistanceTable(map))
        {
            if (metric.Targets.IsDefaultOrEmpty)
            {
                continue;
            }

            bool complete = true;
            for (int z = 0; z < metric.Distances.Length; z++)
            {
                if (metric.Distances[z] is null)
                {
                    f.Add(new MapValidationFailure(
                        "LANDMARK_UNREACHABLE",
                        $"{BirthZoneLabel.Of(z)} 无法到达{metric.Name}。", metric.Targets));
                    complete = false;
                }
            }

            if (!complete)
            {
                continue;
            }

            int[] ds = [.. metric.Distances.Select(d => d!.Value)];
            int min = ds.Min();
            int max = ds.Max();
            if (max - min > map.DistanceTolerance)
            {
                string detail = string.Join("，", ds.Select((d, z) => $"{BirthZoneLabel.Of(z)} = {d}"));
                f.Add(new MapValidationFailure(
                    "DISTANCE_IMBALANCE",
                    $"各出生区到{metric.Name}的最短落子距离失衡（{detail}），"
                    + $"极差 {max - min} 超出容差 {map.DistanceTolerance}。",
                    ImmutableArray<Coord>.Empty));
            }
        }
    }

    private static ImmutableArray<Coord> FloodFill(MapData map, Coord start, HashSet<Coord> visited)
    {
        var component = new List<Coord>();
        var queue = new Queue<Coord>();
        queue.Enqueue(start);
        visited.Add(start);

        while (queue.Count > 0)
        {
            Coord current = queue.Dequeue();
            component.Add(current);
            // 连通区沿气边（terrain-model D-E）：崖壁、栅栏、深水隔开的两片各成一区。平地上与几何邻居等价。
            foreach (Coord n in Adjacency.LibertyNeighbors(map, current))
            {
                if (!visited.Contains(n))
                {
                    visited.Add(n);
                    queue.Enqueue(n);
                }
            }
        }

        return component.Order().ToImmutableArray();
    }

    private static Dictionary<Coord, int> MultiSourceDistances(MapData map, IEnumerable<Coord> sources)
    {
        var dist = new Dictionary<Coord, int>();
        var queue = new Queue<Coord>();
        foreach (Coord s in sources.Order())
        {
            if (!map.IsPlayable(s))
            {
                continue;
            }

            dist[s] = 0;
            queue.Enqueue(s);
        }

        while (queue.Count > 0)
        {
            Coord current = queue.Dequeue();
            // 最短落子距离沿气边（terrain-model D-E）：崖壁挡住的路不算路。平地上与几何邻居等价。
            foreach (Coord n in Adjacency.LibertyNeighbors(map, current))
            {
                if (!dist.ContainsKey(n))
                {
                    dist[n] = dist[current] + 1;
                    queue.Enqueue(n);
                }
            }
        }

        return dist;
    }

}
