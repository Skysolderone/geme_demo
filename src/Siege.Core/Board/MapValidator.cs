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
/// 地图静态校验。把必死口袋、距离失衡、障碍占比这类问题挡在加载期——
/// 漏到对局中会表现为"某玩家莫名其妙被迫 Pass"，极难归因。
/// </summary>
/// <remarks>规格：openspec/changes/add-board-core/specs/map-definition —— Requirement: 地图静态校验规则</remarks>
public static class MapValidator
{
    // 用整数百分比比较，避免浮点进入内核。规范：.trellis/spec/core/determinism.md
    private const int MinObstaclePercent = 8;
    private const int MaxObstaclePercent = 12;

    /// <summary>人数适配预算：可落子格区间、出生区数、信物格区间。</summary>
    private static readonly ImmutableDictionary<int, (int MinPlayable, int MaxPlayable, int MinRelics, int MaxRelics)> Budgets =
        new Dictionary<int, (int, int, int, int)>
        {
            [2] = (50, 65, 7, 9),
            [3] = (75, 90, 10, 12),
            [4] = (100, 115, 13, 15),
        }.ToImmutableDictionary();

    /// <summary>前三大回合单玩家最多 9 枚基础部署，出生区必须容得下。</summary>
    private const int ProtectionPhaseDeployments = 9;

    public static MapValidationResult Validate(MapData map)
    {
        ArgumentNullException.ThrowIfNull(map);
        ImmutableArray<MapValidationFailure>.Builder f = ImmutableArray.CreateBuilder<MapValidationFailure>();

        ValidateStructure(map, f);
        ValidateBudgets(map, f);
        ValidateObstacleRatio(map, f);
        ValidateBirthZones(map, f);
        ValidateRelicCells(map, f);
        ValidateLandmarks(map, f);
        ValidateTolerance(map, f);
        ValidatePockets(map, f);
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

    private static void ValidateBudgets(MapData map, ImmutableArray<MapValidationFailure>.Builder f)
    {
        if (!Budgets.TryGetValue(map.MaxPlayers, out var budget))
        {
            f.Add(new MapValidationFailure(
                "UNSUPPORTED_PLAYER_COUNT",
                $"不支持的人数 {map.MaxPlayers}：只提供 2 / 3 / 4 人的预算表。",
                ImmutableArray<Coord>.Empty));
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

    private static void ValidateObstacleRatio(MapData map, ImmutableArray<MapValidationFailure>.Builder f)
    {
        // 尺寸非正时 Width*Height 可能算出正数（-3×-3=9），占比会变成无意义的比较。
        if (map.Width <= 0 || map.Height <= 0)
        {
            return;
        }

        int area = map.Width * map.Height;

        int obstacles = map.AllCoords().Count(c => map.Obstacles.Contains(c));
        if (obstacles * 100 < area * MinObstaclePercent)
        {
            f.Add(new MapValidationFailure(
                "OBSTACLE_RATIO_TOO_LOW",
                $"障碍格 {obstacles} 个，占外接区域 {FormatPercent(obstacles, area)}，低于 {MinObstaclePercent}%。",
                ImmutableArray<Coord>.Empty));
        }
        else if (obstacles * 100 > area * MaxObstaclePercent)
        {
            f.Add(new MapValidationFailure(
                "OBSTACLE_RATIO_TOO_HIGH",
                $"障碍格 {obstacles} 个，占外接区域 {FormatPercent(obstacles, area)}，高于 {MaxObstaclePercent}%。",
                ImmutableArray<Coord>.Empty));
        }
    }

    /// <summary>把占比格式化成一位小数的百分比，全程整数运算。</summary>
    private static string FormatPercent(int part, int whole)
    {
        int tenths = (part * 1000) / whole;
        return $"{tenths / 10}.{tenths % 10}%";
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
                    "BIRTH_ZONE_OUT_OF_BOUNDS", $"出生区 {i} 含越界格。", outside));
            }

            int playable = zone.Count(c => map.TerrainAt(c) == Terrain.Playable);
            if (playable < ProtectionPhaseDeployments)
            {
                f.Add(new MapValidationFailure(
                    "BIRTH_ZONE_TOO_SMALL",
                    $"出生区 {i} 只有 {playable} 个可落子格，容不下前三大回合最多 {ProtectionPhaseDeployments} 枚基础部署。",
                    ImmutableArray<Coord>.Empty));
            }

            for (int j = i + 1; j < map.BirthZones.Length; j++)
            {
                ImmutableArray<Coord> overlap = zone.Intersect(map.BirthZones[j]).Order().ToImmutableArray();
                if (!overlap.IsEmpty)
                {
                    f.Add(new MapValidationFailure(
                        "BIRTH_ZONE_OVERLAP", $"出生区 {i} 与 {j} 重叠。", overlap));
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
                    $"位于出生区 {zone} 的信物格被标注为 {spec.Zone}。", [c]));
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
    /// 必死口袋：出生区内被障碍或边界围成的连通空区，面积小于
    /// <see cref="MapData.MinTwoEyeArea"/> 即判失败，除非有显式豁免。
    /// 这是保守近似——不做完整死活判定，宁可误报由设计师确认，也不放过对局中卡死的风险。
    /// </summary>
    private static void ValidatePockets(MapData map, ImmutableArray<MapValidationFailure>.Builder f)
    {
        var visited = new HashSet<Coord>();
        foreach (Coord start in map.AllCoords())
        {
            if (visited.Contains(start) || map.TerrainAt(start) != Terrain.Playable)
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
    /// 距离均衡：各出生区到最近公共信物格、中央入口与主要咽喉的最短落子距离
    /// （四邻接、绕开障碍），两两差值不得超过容差。
    /// </summary>
    private static void ValidateDistanceBalance(MapData map, ImmutableArray<MapValidationFailure>.Builder f)
    {
        if (map.BirthZones.IsDefaultOrEmpty)
        {
            return;
        }

        ImmutableArray<Coord> publicRelics = map.RelicCells
            .Where(kv => kv.Value.Zone == RelicZone.Contested)
            .Select(kv => kv.Key).Order().ToImmutableArray();

        var metrics = new (string Name, ImmutableArray<Coord> Targets)[]
        {
            ("最近公共信物", publicRelics),
            ("中央入口", [map.CentralEntrance]),
            ("最近咽喉", map.ChokePoints.Order().ToImmutableArray()),
        };

        var distances = new int[metrics.Length][];
        for (int m = 0; m < metrics.Length; m++)
        {
            distances[m] = new int[map.BirthZones.Length];
        }

        for (int z = 0; z < map.BirthZones.Length; z++)
        {
            Dictionary<Coord, int> dist = MultiSourceDistances(map, map.BirthZones[z]);
            for (int m = 0; m < metrics.Length; m++)
            {
                (string name, ImmutableArray<Coord> targets) = metrics[m];
                if (targets.IsDefaultOrEmpty)
                {
                    distances[m][z] = -1;
                    continue;
                }

                int best = int.MaxValue;
                foreach (Coord t in targets)
                {
                    if (dist.TryGetValue(t, out int d) && d < best)
                    {
                        best = d;
                    }
                }

                if (best == int.MaxValue)
                {
                    f.Add(new MapValidationFailure(
                        "LANDMARK_UNREACHABLE",
                        $"出生区 {z} 无法到达{name}。", targets));
                    distances[m][z] = -1;
                }
                else
                {
                    distances[m][z] = best;
                }
            }
        }

        for (int m = 0; m < metrics.Length; m++)
        {
            int[] ds = distances[m];
            if (ds.Any(d => d < 0))
            {
                continue;
            }

            int min = ds.Min();
            int max = ds.Max();
            if (max - min > map.DistanceTolerance)
            {
                string detail = string.Join("，", ds.Select((d, z) => $"出生区 {z} = {d}"));
                f.Add(new MapValidationFailure(
                    "DISTANCE_IMBALANCE",
                    $"各出生区到{metrics[m].Name}的最短落子距离失衡（{detail}），"
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
            foreach (Coord n in Adjacency.Neighbors(map.Width, map.Height, current))
            {
                if (!visited.Contains(n) && map.TerrainAt(n) == Terrain.Playable)
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
            if (map.TerrainAt(s) != Terrain.Playable)
            {
                continue;
            }

            dist[s] = 0;
            queue.Enqueue(s);
        }

        while (queue.Count > 0)
        {
            Coord current = queue.Dequeue();
            foreach (Coord n in Adjacency.Neighbors(map.Width, map.Height, current))
            {
                if (map.TerrainAt(n) == Terrain.Playable && !dist.ContainsKey(n))
                {
                    dist[n] = dist[current] + 1;
                    queue.Enqueue(n);
                }
            }
        }

        return dist;
    }

}
