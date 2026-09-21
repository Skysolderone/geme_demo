using System.Collections.Immutable;
using Siege.Core.Determinism;

namespace Siege.Core.Board.Maps;

/// <summary>平台的外接方块：西南角（0 基列 / 行索引）与边长。</summary>
public readonly record struct PlatformRect(int X, int Y, int Side)
{
    /// <summary>最东一列的索引。</summary>
    public int X1 => X + Side - 1;

    /// <summary>最北一行的索引。</summary>
    public int Y1 => Y + Side - 1;

    /// <summary>外接面积。</summary>
    public int Area => Side * Side;

    /// <summary>该格是否落在方块内。</summary>
    public bool Contains(int x, int y) => x >= X && x <= X1 && y >= Y && y <= Y1;

    /// <summary>
    /// 与另一方块之间隔了几格：两个方向上间隔的较大者（投影重叠的方向记为负）。
    /// ≥ g 即"把其中一个向四周各扩 g 格仍不相交"——对角相邻也算在内。
    /// </summary>
    public int GapTo(PlatformRect other)
    {
        int dx = Math.Max(X - other.X1 - 1, other.X - X1 - 1);
        int dy = Math.Max(Y - other.Y1 - 1, other.Y - Y1 - 1);
        return Math.Max(dx, dy);
    }
}

/// <summary>一次生成的结果：地图、实际采用的尝试序号（只供诊断，不进标识）、各平台的外接方块（下标 = 出生区下标，按边长从大到小）。</summary>
public sealed record GeneratedMap(MapData Map, int Attempt, ImmutableArray<PlatformRect> Platforms);

/// <summary>尝试次数耗尽仍未得到通过校验的地图。消息带最后一次尝试的失败原因。</summary>
public sealed class MapGenerationException : Exception
{
    public MapGenerationException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// 边疆档 4 人地图的确定性生成器（map-generator D1–D4）：<c>(地图种子, 参数) → MapData</c>。
/// 同一种子与参数在任何机器上得到逐字节相同的地图；全程整数运算，随机只来自 <see cref="MapRandom"/>，不读时钟与环境，也拿不到对局种子。
/// </summary>
/// <remarks>
/// <para><b>校验闭环</b>：第 k 次尝试（k 从 0 起）的随机源只由（地图种子, k）决定；每次尝试依次过三关——构造（<see cref="FrontierMapLayout"/>）、
/// 边疆档静态校验（<see cref="MapValidator"/>，与内置图同一套）、布局规则自检（<see cref="CheckLayout"/>：校验器不查、但规格要求的布局特征）。
/// 任一关不过即作废、k + 1；到上限抛 <see cref="MapGenerationException"/>，MUST NOT 返回未通过校验的地图。</para>
/// <para><b>出生区编号</b>按边长从大到小：1 号最大，最后两个最小且紧邻中央广场（"小平台靠中央"）。</para>
/// <para>规格：openspec/changes/map-generator/specs/map-generation</para>
/// </remarks>
public static class FrontierMapGenerator
{
    /// <summary>列数（坐标记法的上限）。</summary>
    public const int Columns = FrontierMapLayout.W;

    /// <summary>行数。</summary>
    public const int Rows = FrontierMapLayout.H;

    /// <summary>尝试上限的缺省值。</summary>
    public const int DefaultMaxAttempts = 64;

    /// <summary>按完整标识（<c>gen:&lt;种子&gt;[:p&lt;N&gt;]</c>）生成。</summary>
    public static MapData Generate(string mapId)
    {
        (ulong seed, MapGenParameters parameters) = GeneratedMapId.Parse(mapId);
        return Generate(seed, parameters);
    }

    /// <summary>生成地图。<see cref="MapData.Id"/> 为规范化标识。</summary>
    public static MapData Generate(ulong mapSeed, MapGenParameters? parameters = null, int maxAttempts = DefaultMaxAttempts) =>
        GenerateDetailed(mapSeed, parameters, maxAttempts).Map;

    /// <summary>生成地图，并给出实际采用的尝试序号与各平台的外接方块。</summary>
    public static GeneratedMap GenerateDetailed(ulong mapSeed, MapGenParameters? parameters = null, int maxAttempts = DefaultMaxAttempts) =>
        GenerateDetailed(mapSeed, parameters, maxAttempts, forcedFailures: 0);

    /// <summary>
    /// 测试入口：前 <paramref name="forcedFailures"/> 次尝试不构造、直接按"未通过"处理，其余与公开入口完全相同
    /// （第 k 次尝试的随机源只由种子与 k 决定，跳过前几次不影响后面的图）。闭环的规格测试靠它，不依赖"样本里恰好有重试过的种子"。
    /// </summary>
    internal static GeneratedMap GenerateDetailed(ulong mapSeed, MapGenParameters? parameters, int maxAttempts, int forcedFailures)
    {
        parameters ??= MapGenParameters.Default;
        parameters.EnsureValid();
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), maxAttempts, "尝试上限至少为 1。");
        }

        string id = GeneratedMapId.Format(mapSeed, parameters);
        string lastReason = string.Empty;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (attempt < forcedFailures)
            {
                lastReason = $"第 {attempt} 次尝试构造作废：测试入口指定该次尝试不通过。";
                continue;
            }

            RandomStream rng = MapRandom.ForAttempt(mapSeed, attempt);
            var layout = new FrontierMapLayout(rng, parameters.PlatformCount);
            if (!layout.TryBuild(out string reason))
            {
                lastReason = $"第 {attempt} 次尝试构造作废：{reason}";
                continue;
            }

            MapData map = ToMapData(id, layout);
            MapValidationResult validation = MapValidator.Validate(map);
            if (!validation.IsValid)
            {
                lastReason = $"第 {attempt} 次尝试未通过边疆档静态校验：{string.Join("；", validation.Failures)}";
                continue;
            }

            ImmutableArray<PlatformRect> platforms = [.. layout.Platforms];
            if (CheckLayout(map, platforms) is { } broken)
            {
                lastReason = $"第 {attempt} 次尝试不满足布局规则：{broken}";
                continue;
            }

            return new GeneratedMap(map, attempt, platforms);
        }

        throw new MapGenerationException(
            $"地图 {id} 在 {maxAttempts} 次尝试内没有得到通过校验的地图。最后一次的失败原因——{lastReason}");
    }

    /// <summary>
    /// 布局规则自检：静态校验不查、但规格「布局规则」要求的特征。返回第一条不满足的说明；全部满足为 <c>null</c>。
    /// </summary>
    private static string? CheckLayout(MapData map, ImmutableArray<PlatformRect> platforms)
    {
        for (int i = 0; i < platforms.Length; i++)
        {
            int playable = map.BirthZones[i].Count(map.IsPlayable);
            if (playable * 5 < platforms[i].Area * 4)
            {
                return $"{BirthZoneLabel.Of(i)} 的可落子格 {playable} 不足外接面积 {platforms[i].Area} 的 80%。";
            }
        }

        if (CheckBridges(map) is { } badBridges)
        {
            return badBridges;
        }

        if (map.TerrainData.Fences.IsEmpty || !map.TerrainData.Surfaces.Values.Contains(Surface.Forest))
        {
            return "缺栅栏或林地。";
        }

        int tents = map.Sites.Values.Count(t => t == SiteTier.Tent);
        int fires = map.Sites.Values.Count(t => t == SiteTier.Campfire);
        int steles = map.Sites.Values.Count(t => t == SiteTier.Stele);
        int publicRelics = map.RelicCells.Values.Count(r => r.Zone == RelicZone.Contested);
        if (tents != platforms.Length || fires != platforms.Length || steles != 4 || publicRelics != 7)
        {
            return $"布点数量不对：营帐 {tents}、篝火 {fires}、石碑 {steles}、公共信物 {publicRelics}。";
        }

        // 小平台靠中央：最小的两个平台（编号最后两个）到中央入口的沿气边距离，不大于任何一个边长最大的平台的距离。
        // 距离直接取校验器的距离表，不另写一份最短路。
        ImmutableArray<int?> toEntrance = MapValidator.DistanceTable(map)[1].Distances;
        int n = platforms.Length;
        int smallFar = Math.Max(toEntrance[n - 1] ?? int.MaxValue, toEntrance[n - 2] ?? int.MaxValue);
        int largeNear = int.MaxValue;
        for (int i = 0; i < n; i++)
        {
            if (platforms[i].Side == platforms[0].Side)
            {
                largeNear = Math.Min(largeNear, toEntrance[i] ?? int.MaxValue);
            }
        }

        return smallFar <= largeNear
            ? null
            : $"最小的两个平台到中央入口的距离 {smallFar} 大于最大平台的距离 {largeNear}。";
    }

    /// <summary>
    /// 桥：一座桥是一行或一列上连续的桥格（河垂直穿过走廊，桥面必是直的；只做坐标算术，不遍历邻居、不遍历散列容器）。
    /// 至少 2 座；每座不超过 <see cref="FrontierMapLayout.MaxBridgeSpan"/> 格——桥长就是走廊宽；
    /// 桥格总数不超过 <see cref="FrontierMapLayout.MaxBridgeCells"/>。
    /// </summary>
    private static string? CheckBridges(MapData map)
    {
        bool Bridge(int x, int y) => x >= 0 && y >= 0 && x < map.Width && y < map.Height && map.HasBridge(new Coord(x, y));

        int bridges = 0;
        int cells = 0;
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                if (!Bridge(x, y))
                {
                    continue;
                }

                cells++;
                bool alongRow = Bridge(x - 1, y) || Bridge(x + 1, y);
                bool alongColumn = Bridge(x, y - 1) || Bridge(x, y + 1);
                if (alongRow && alongColumn)
                {
                    return $"桥在 {new Coord(x, y)} 拐了弯：两座桥连在了一起，或河不是垂直穿过走廊。";
                }

                if (Bridge(x - 1, y) || Bridge(x, y - 1))
                {
                    continue;       // 不是这座桥的起点（最西 / 最南的一格）
                }

                bridges++;
                int span = 1;
                while (Bridge(x + span, y) || Bridge(x, y + span))
                {
                    span++;
                }

                if (span > FrontierMapLayout.MaxBridgeSpan)
                {
                    return $"有一座桥长 {span} 格，超过走廊宽度上限 {FrontierMapLayout.MaxBridgeSpan}。";
                }
            }
        }

        if (bridges < 2)
        {
            return $"桥只有 {bridges} 座，少于 2 座。";
        }

        return cells > FrontierMapLayout.MaxBridgeCells ? $"桥格共 {cells} 个，超过上限 {FrontierMapLayout.MaxBridgeCells}。" : null;
    }

    /// <summary>把工作态网格灌成不可变地图数据。写法与手工边疆图一致：平台内的岩石不进出生区、不标高度。</summary>
    private static MapData ToMapData(string id, FrontierMapLayout layout)
    {
        var obstacles = ImmutableHashSet.CreateBuilder<Coord>();
        var heights = ImmutableDictionary.CreateBuilder<Coord, int>();
        var surfaces = ImmutableDictionary.CreateBuilder<Coord, Surface>();
        var bridges = ImmutableHashSet.CreateBuilder<Coord>();
        var chokes = ImmutableHashSet.CreateBuilder<Coord>();
        var relics = ImmutableDictionary.CreateBuilder<Coord, RelicCellSpec>();
        var sites = ImmutableDictionary.CreateBuilder<Coord, SiteTier>();
        var zones = new ImmutableHashSet<Coord>.Builder[layout.Platforms.Length];
        for (int i = 0; i < zones.Length; i++)
        {
            zones[i] = ImmutableHashSet.CreateBuilder<Coord>();
        }

        for (int y = 0; y < Rows; y++)
        {
            for (int x = 0; x < Columns; x++)
            {
                var c = new Coord(x, y);
                switch (layout.Cells[x, y])
                {
                    case FrontierMapLayout.Cell.Rock:
                    case FrontierMapLayout.Cell.PlatformRock:
                        obstacles.Add(c);
                        break;
                    case FrontierMapLayout.Cell.Water:
                    case FrontierMapLayout.Cell.River:
                        surfaces[c] = Surface.DeepWater;
                        break;
                    case FrontierMapLayout.Cell.Bridge:
                        surfaces[c] = Surface.DeepWater;
                        bridges.Add(c);
                        chokes.Add(c);
                        break;
                    case FrontierMapLayout.Cell.Platform:
                        heights[c] = 2;
                        zones[layout.Zone[x, y]].Add(c);
                        break;
                    case FrontierMapLayout.Cell.Ramp:
                        heights[c] = 1;
                        chokes.Add(c);
                        break;
                    case FrontierMapLayout.Cell.Ground:
                        if (layout.Forest[x, y])
                        {
                            surfaces[c] = Surface.Forest;
                        }

                        break;
                    default:
                        throw new InvalidOperationException($"生成器在 {c} 留下了未填充的格子。");
                }

                if (layout.Sites[x, y] is { } tier)
                {
                    sites[c] = tier;
                }

                if (layout.Relics[x, y] is { } spec)
                {
                    relics[c] = spec;
                }
            }
        }

        var fences = ImmutableHashSet.CreateBuilder<FenceEdge>();
        foreach ((FrontierMapLayout.P a, FrontierMapLayout.P b) in layout.Fences)
        {
            fences.Add(new FenceEdge(new Coord(a.X, a.Y), new Coord(b.X, b.Y)));
        }

        return new MapData
        {
            Id = id,
            Width = Columns,
            Height = Rows,
            MaxPlayers = 4,
            Profile = MapProfile.Frontier,
            Obstacles = obstacles.ToImmutable(),
            TerrainData = new TerrainData(heights.ToImmutable(), surfaces.ToImmutable(), bridges.ToImmutable(), fences.ToImmutable()),
            BirthZones = [.. zones.Select(z => z.ToImmutable())],
            RelicCells = relics.ToImmutable(),
            Sites = sites.ToImmutable(),
            ChokePoints = chokes.ToImmutable(),
            CentralEntrance = new Coord(layout.Entrance.X, layout.Entrance.Y),
        };
    }
}
