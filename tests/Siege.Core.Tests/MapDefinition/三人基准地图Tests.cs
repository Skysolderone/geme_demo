using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>
/// 规格：openspec/changes/small-maps/specs/map-definition —— Requirement: 3 人基准地图（tasks 2.1）。
/// 范式同 <see cref="两人基准地图Tests"/>：规模、镜像对称、三区公平三个 Scenario 各钉一条，另钉地形要素、静态校验、出生区容量、内置表登记与磁盘文件的规范写法。
/// </summary>
public class 三人基准地图Tests
{
    private static MapData Map() => ThreePlayerBaseMap.Create();

    [Fact]
    public void 三人图规模()
    {
        // 规格 Scenario「3 人图规模」：可落子格 75–90，出生区 3 个且各 12–14 格、全部 h=2，信物格 10–12 个且每个出生区各 2 个。
        // 校验器的 3 人预算表不带单区格数区间（只有 4 人档带），12–14 由本测试守。
        MapData map = Map();
        Assert.Equal(ThreePlayerBaseMap.Id, map.Id);
        Assert.Equal(3, map.MaxPlayers);
        Assert.Equal(MapProfile.Standard, map.Profile);
        Assert.InRange(map.Width, 1, 12);
        Assert.InRange(map.Height, 1, 12);
        Assert.InRange(map.PlayableCount, 75, 90);

        Assert.Equal(3, map.BirthZones.Length);
        foreach (ImmutableHashSet<Coord> zone in map.BirthZones)
        {
            Assert.All(zone, c => Assert.True(map.IsPlayable(c), $"出生区格 {c} 不可落子。"));
            Assert.InRange(zone.Count, 12, 14);
            Assert.All(zone, c => Assert.Equal(2, map.HeightAt(c)));
        }

        for (int i = 0; i < map.BirthZones.Length; i++)
        {
            Assert.Equal(2, map.RelicCells.Count(kv => map.BirthZoneOf(kv.Key) == i));
            Assert.All(map.RelicCells.Where(kv => map.BirthZoneOf(kv.Key) == i), kv => Assert.Equal(new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth), kv.Value));
        }

        // 公共争夺区 4–6；中央入口放高档信物（同 v5 / 2 人图）。
        int contested = map.RelicCells.Count(kv => kv.Value.Zone == RelicZone.Contested);
        Assert.InRange(contested, 4, 6);
        Assert.All(map.RelicCells.Where(kv => kv.Value.Zone == RelicZone.Contested), kv => Assert.Null(map.BirthZoneOf(kv.Key)));
        Assert.InRange(map.RelicCells.Count, 10, 12);
        Assert.Equal(new RelicCellSpec(RelicZone.Contested, BudgetTier.High), map.RelicCells[map.CentralEntrance]);
    }

    [Fact]
    public void 三人图镜像对称()
    {
        // 规格 Scenario「3 人图镜像对称」：沿竖直中轴左右镜像后高度、地表、障碍、桥、栅栏、出生区（跨中轴者不变、另两个互换）与信物格逐格一致。
        // 逐格比对只在 MapSymmetry 一处实现（C4 / C2 / 镜像共用）；下面再用测试内独立的镜像算式复核出生区的对应关系与中轴，两边不共用代码。
        // 变异（small-maps 段 B，过滤跑地图 / 选图 / 入口七类 88 条）：M-T1 JSON 里 A9 林地 → 草地（一格破坏镜像）→ 红 1（本测试）；
        // M-T2 MirrorVertical 改成绕中心 180° → 红 1（本测试）；M-T3 镜像的出生区像改成恒等（2、3 不互换）→ 红 1（本测试）。
        MapData map = Map();
        Assert.Empty(MapSymmetry.MirrorDefects(map));
        Assert.True(MapSymmetry.IsMirrorSymmetric(map));

        // 宽为奇数，中轴列存在。
        Assert.Equal(1, map.Width % 2);
        int axis = (map.Width - 1) / 2;
        Coord Image(Coord c) => new(map.Width - 1 - c.X, c.Y);
        Assert.True(map.BirthZones[0].Select(Image).ToHashSet().SetEquals(map.BirthZones[0]), "出生区 1 的像不是它自己。");
        Assert.True(map.BirthZones[1].Select(Image).ToHashSet().SetEquals(map.BirthZones[2]), "出生区 2 的像不是出生区 3。");
        Assert.Contains(map.BirthZones[0], c => c.X == axis);
        Assert.DoesNotContain(map.BirthZones[1], c => c.X == axis);
        Assert.Equal(axis, map.CentralEntrance.X);
        Assert.True(map.RelicCells.Keys.Select(Image).ToHashSet().SetEquals(map.RelicCells.Keys), "信物格集合不是镜像不变。");

        // 布局（design D1）：跨中轴的出生区在上方，另两个在下方。
        Assert.True(map.BirthZones[0].Min(c => c.Y) > map.BirthZones[1].Max(c => c.Y), "跨中轴的出生区应整体位于另两个之上。");

        // 反面：C4 的 4 人基准图不是这个口径下的镜像（出生区 1 的像不是自身），中央入口离开中轴也报缺陷——防"检查恒空"。
        Assert.False(MapSymmetry.IsMirrorSymmetric(FourPlayerBaseMap.Create()));
        Assert.NotEmpty(MapSymmetry.MirrorDefects(map with { CentralEntrance = new Coord(0, map.CentralEntrance.Y) }));
    }

    [Fact]
    public void 三人图三区公平()
    {
        // 规格 Scenario「3 人图三区公平」：三个出生区到最近公共信物、中央入口与主要咽喉的沿气边距离，每一项两两差值都不超过 1。
        // 距离用测试内独立的多源 BFS 算（只借用气边的唯一实现 Adjacency.LibertyNeighbors），再与校验器的距离表逐项对照。
        // 变异 M-T4（small-maps 段 B）：JSON 里中央入口 F5 → F6（沿中轴上移一格，高档信物随之互换，镜像不破）→ 上区到中央入口 4 → 3、下两区 4 → 5，
        // 极差 2 → 红 5：本测试、静态校验、出生区容量（建局时校验失败）与两条 3 人图入口测试；「三人图镜像对称」保持绿。
        MapData map = Map();
        Assert.Equal(1, map.DistanceTolerance);
        Assert.Null(map.ToleranceRelaxReason);

        Coord[] publicRelics = [.. map.RelicCells.Where(kv => kv.Value.Zone == RelicZone.Contested).Select(kv => kv.Key)];
        Coord[] chokes = [.. map.ChokePoints];
        Assert.True(publicRelics.Length >= 4, "样本口径：公共信物不足 4 个。");
        Assert.NotEmpty(chokes);
        (string Name, Coord[] Targets)[] metrics =
        [
            ("最近公共信物", publicRelics),
            ("中央入口", [map.CentralEntrance]),
            ("最近咽喉", chokes),
        ];

        Dictionary<Coord, int>[] fromZone = [.. map.BirthZones.Select(z => Bfs(map, z))];
        ImmutableArray<BirthZoneDistance> table = MapValidator.DistanceTable(map);
        Assert.Equal(metrics.Length, table.Length);
        for (int m = 0; m < metrics.Length; m++)
        {
            int[] ds = [.. fromZone.Select(d => metrics[m].Targets.Where(d.ContainsKey).Select(t => d[t]).DefaultIfEmpty(-1).Min())];
            Assert.Equal(3, ds.Length);
            Assert.All(ds, d => Assert.True(d > 0, $"{metrics[m].Name}：有出生区到不了。"));
            Assert.True(ds.Max() - ds.Min() <= 1, $"{metrics[m].Name}：三区距离 {string.Join("/", ds)} 两两差值超过 1。");

            Assert.Equal(metrics[m].Name, table[m].Name);
            Assert.Equal(ds.Select(d => (int?)d), table[m].Distances);
        }
    }

    [Fact]
    public void 三人图地形要素齐全()
    {
        // 规格 Requirement：三种高度、一格宽深水、预置桥、栅栏与林地各至少出现一次，且不含四种新地表。
        MapData map = Map();
        Coord[] playable = [.. map.AllCoords().Where(map.IsPlayable)];
        Assert.Equal([0, 1, 2], playable.Select(map.HeightAt).Distinct().Order());
        Assert.Contains(map.AllCoords(), map.TerrainData.IsUnbridgedDeepWater);
        Assert.NotEmpty(map.TerrainData.Bridges);
        Assert.NotEmpty(map.TerrainData.Fences);
        Assert.Contains(playable, c => map.SurfaceAt(c) == Surface.Forest);
        Assert.DoesNotContain(map.AllCoords(), c => map.SurfaceAt(c) is Surface.Desert or Surface.Marsh or Surface.Crag or Surface.Shallows);

        // "一格宽"同 4 人 / 2 人图的可观察定义（terrain 规格 E4）：某可落子格 s 的几何邻居 t 是未架桥深水，且 s 的覆盖落到对岸 u = 2t − s。
        bool oneWide = false;
        foreach (Coord s in playable)
        {
            foreach (Coord t in Adjacency.Neighbors(map.Width, map.Height, s))
            {
                int ux = (2 * t.X) - s.X;
                int uy = (2 * t.Y) - s.Y;
                if (map.TerrainData.IsUnbridgedDeepWater(t)
                    && ux >= 0 && uy >= 0 && ux < map.Width && uy < map.Height
                    && Adjacency.CoverageTargets(map, s).Contains(new Coord(ux, uy)))
                {
                    oneWide = true;
                }
            }
        }

        Assert.True(oneWide, "地图上没有任何一段可隔岸覆盖的一格宽深水。");

        // design D2：地貌语言沿用 v5——中央入口是 h=0 低地，桥即咽喉；三座桥各对一个出生区。
        Assert.Equal(0, map.HeightAt(map.CentralEntrance));
        Assert.All(map.TerrainData.Bridges, b => Assert.Contains(b, map.ChokePoints));
        Assert.Equal(3, map.TerrainData.Bridges.Count);
    }

    [Fact]
    public void 通过静态校验与人数预算()
    {
        // tasks 2.2：地图静态校验（含距离容差与必死口袋）与人数适配预算全部通过，且能建局。
        MapData map = Map();
        MapValidationResult result = MapValidator.Validate(map);
        Assert.True(result.IsValid, result.ToString());
        GameBoard.Load(map);
    }

    [Fact]
    public void 出生区容量()
    {
        // 规格 Requirement：每个出生区 MUST 能容纳单名玩家前三大回合最多 9 枚基础部署而不出现无合法落点。三区都试。
        MapData map = Map();
        for (int z = 0; z < map.BirthZones.Length; z++)
        {
            GameBoard board = GameBoard.Load(map);
            ImmutableHashSet<Coord> zone = map.BirthZones[z];
            for (int stone = 0; stone < 9; stone++)
            {
                Coord? free = zone.Where(c => board[c].IsPlayableEmpty).Order().Cast<Coord?>().FirstOrDefault();
                Assert.True(free is not null, $"出生区 {z + 1} 第 {stone + 1} 枚部署时已无合法空格。");
                board.Place(free!.Value, TestMaps.P0, PieceType.Basic);
            }

            Assert.Equal(9, board.GroupsOf(TestMaps.P0).Sum(g => g.Size));
            Assert.Contains(zone, c => board[c].IsPlayableEmpty);
        }
    }

    [Fact]
    public void 登记在内置地图表_显示名与顺序()
    {
        // tasks 2.2：内置地图表登记（含显示名），顺序按规格「开局选图界面」的列举：标准图、2 人图、3 人图、手工边疆图。
        MapData map = Map();
        BuiltinMapInfo info = Assert.Single(MapCatalog.BuiltinMaps, m => m.Id == ThreePlayerBaseMap.Id);
        Assert.Equal($"三人图 {map.Width}×{map.Height}", info.Title);
        Assert.Equal("三人图 11×11", info.Title);
        Assert.Equal(2, MapCatalog.BuiltinIds.ToList().IndexOf(ThreePlayerBaseMap.Id));
        Assert.Equal(MapFile.ToJson(map), MapFile.ToJson(MapCatalog.Resolve(ThreePlayerBaseMap.Id)));
    }

    [Fact]
    public void 磁盘上的地图文件是规范写法_与内置图逐项一致()
    {
        // 权威数据是手工编写的 maps/siege-3p-base-v1.json（嵌入资源）。它必须是 MapFile 的规范写法：
        // `Siege.Sim map --map siege-3p-base-v1` 会按内置图重新导出这个文件，非规范写法会被悄悄改写。
        string path = Path.Combine(FrontierFixtures.RepoRoot(), "maps", ThreePlayerBaseMap.Id + ".json");
        string disk = File.ReadAllText(path).ReplaceLineEndings("\n");
        MapData onDisk = MapFile.FromJson(disk);

        Assert.Equal(MapFile.ToJson(onDisk).ReplaceLineEndings("\n"), disk);
        Assert.Equal(MapFile.ToJson(Map()), MapFile.ToJson(onDisk));
        Assert.True(disk.Split('\n').Length > 40, "地图文件过短。");
    }

    /// <summary>测试内独立的多源 BFS：从出生区全部格出发、沿气边，得到每个可达格的最短落子距离。</summary>
    private static Dictionary<Coord, int> Bfs(MapData map, IEnumerable<Coord> sources)
    {
        var dist = new Dictionary<Coord, int>();
        var queue = new Queue<Coord>();
        foreach (Coord s in sources)
        {
            dist[s] = 0;
            queue.Enqueue(s);
        }

        while (queue.Count > 0)
        {
            Coord c = queue.Dequeue();
            foreach (Coord n in Adjacency.LibertyNeighbors(map, c))
            {
                if (dist.TryAdd(n, dist[c] + 1))
                {
                    queue.Enqueue(n);
                }
            }
        }

        return dist;
    }
}
