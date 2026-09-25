using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>
/// 规格：openspec/changes/small-maps/specs/map-definition —— Requirement: 2 人基准地图（tasks 1.1）。
/// 范式同 <see cref="四人基准地图Tests"/>：规模、旋转对称、地形要素各钉一条，另钉静态校验、出生区容量、内置表登记与磁盘文件的规范写法。
/// </summary>
public class 两人基准地图Tests
{
    private static MapData Map() => TwoPlayerBaseMap.Create();

    [Fact]
    public void 两人图规模()
    {
        // 规格 Scenario「2 人图规模」：可落子格 50–65，出生区 2 个且各 12–14 格、全部 h=2，信物格 7–9 个且每个出生区各 2 个。
        // 校验器的 2 人预算表不带单区格数区间（只有 4 人档带），12–14 由本测试守。
        MapData map = Map();
        Assert.Equal(TwoPlayerBaseMap.Id, map.Id);
        Assert.Equal(2, map.MaxPlayers);
        Assert.Equal(MapProfile.Standard, map.Profile);
        Assert.InRange(map.Width, 1, 10);
        Assert.InRange(map.Height, 1, 10);
        Assert.InRange(map.PlayableCount, 50, 65);

        Assert.Equal(2, map.BirthZones.Length);
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

        // 公共争夺区 3–5（C2 轨道只能是 2 或 1）；中心格是唯一的 1 元轨道，放高档信物并兼作中央入口。
        int contested = map.RelicCells.Count(kv => kv.Value.Zone == RelicZone.Contested);
        Assert.InRange(contested, 3, 5);
        Assert.All(map.RelicCells.Where(kv => kv.Value.Zone == RelicZone.Contested), kv => Assert.Null(map.BirthZoneOf(kv.Key)));
        Assert.InRange(map.RelicCells.Count, 7, 9);
        Assert.Equal(new RelicCellSpec(RelicZone.Contested, BudgetTier.High), map.RelicCells[map.CentralEntrance]);
    }

    [Fact]
    public void 两人图旋转对称()
    {
        // 规格 Scenario「2 人图旋转对称」：绕中心旋转 180° 后高度、地表、障碍、桥、栅栏、出生区（编号互换）与信物格逐格一致。
        // 逐格比对只在 MapSymmetry 一处实现（C4 / C2 共用）；下面再用测试内独立的旋转算式复核出生区互换与中心不动，两边不共用代码。
        // 变异（small-maps 段 A，过滤跑三类）：M-S1 JSON 里 G1 高度 0→1（一格破坏 C2）→ 红 1（本测试）；
        // M-S2 Rotate180 改成左右镜像 (w−1−x, y) → 红 1（本测试）；M-S3 C2 出生区不互换（zoneShift: 0）→ 红 1（本测试）。
        MapData map = Map();
        Assert.Empty(MapSymmetry.Rotation180Defects(map));
        Assert.True(MapSymmetry.IsC2Symmetric(map));

        Coord Image(Coord c) => new(map.Width - 1 - c.X, map.Height - 1 - c.Y);
        Assert.True(map.BirthZones[0].Select(Image).ToHashSet().SetEquals(map.BirthZones[1]), "出生区 1 的像不是出生区 2。");
        Assert.Equal(map.CentralEntrance, Image(map.CentralEntrance));
        Assert.True(map.RelicCells.Keys.Select(Image).ToHashSet().SetEquals(map.RelicCells.Keys), "信物格集合不是 180° 不变。");

        // 反面：C4 的基准图同样 C2 对称（C4 ⇒ C2），且 C2 检查对非对称的 4 人旧图确实报缺陷——防"检查恒空"。
        Assert.True(MapSymmetry.IsC2Symmetric(FourPlayerBaseMap.Create()));
        Assert.NotEmpty(MapSymmetry.Rotation180Defects(map with { CentralEntrance = new Coord(0, 0) }));
    }

    [Fact]
    public void 两人图地形要素齐全()
    {
        // 规格 Scenario「2 人图地形要素齐全」：三种高度、一格宽深水、预置桥、栅栏与林地各至少出现一次，且不含四种新地表。
        MapData map = Map();
        Coord[] playable = [.. map.AllCoords().Where(map.IsPlayable)];
        Assert.Equal([0, 1, 2], playable.Select(map.HeightAt).Distinct().Order());
        Assert.Contains(map.AllCoords(), map.TerrainData.IsUnbridgedDeepWater);
        Assert.NotEmpty(map.TerrainData.Bridges);
        Assert.NotEmpty(map.TerrainData.Fences);
        Assert.Contains(playable, c => map.SurfaceAt(c) == Surface.Forest);
        Assert.DoesNotContain(map.AllCoords(), c => map.SurfaceAt(c) is Surface.Desert or Surface.Marsh or Surface.Crag or Surface.Shallows);

        // "一格宽"同 4 人图的可观察定义（terrain 规格 E4）：某可落子格 s 的几何邻居 t 是未架桥深水，且 s 的覆盖落到对岸 u = 2t − s。
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

        // design D2：地貌语言沿用 v5——中央入口是 h=0 低地，桥头（咽喉）在中央低地一侧。
        Assert.Equal(0, map.HeightAt(map.CentralEntrance));
        Assert.All(map.TerrainData.Bridges, b => Assert.Contains(b, map.ChokePoints));
    }

    [Fact]
    public void 通过静态校验与人数预算_三项距离两区相等()
    {
        // tasks 1.2：地图静态校验与人数适配预算全部通过；C2 对称下两区到三类目标的距离应完全相等（容差 1 之内且极差 0）。
        MapData map = Map();
        MapValidationResult result = MapValidator.Validate(map);
        Assert.True(result.IsValid, result.ToString());
        GameBoard.Load(map);

        ImmutableArray<BirthZoneDistance> table = MapValidator.DistanceTable(map);
        Assert.Equal(3, table.Length);
        Assert.All(table, m =>
        {
            Assert.Equal(2, m.Distances.Length);
            Assert.NotNull(m.Distances[0]);
            Assert.Equal(m.Distances[0], m.Distances[1]);
        });
    }

    [Fact]
    public void 出生区容量()
    {
        // 规格 Requirement：每个出生区 MUST 能容纳单名玩家前三大回合最多 9 枚基础部署而不出现无合法落点。两区都试。
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
        // tasks 1.2：内置地图表登记（含显示名），顺序按规格「开局选图界面」的列举：标准图、2 人图、……、手工边疆图。
        Assert.Contains(TwoPlayerBaseMap.Id, MapCatalog.BuiltinIds);
        BuiltinMapInfo info = Assert.Single(MapCatalog.BuiltinMaps, m => m.Id == TwoPlayerBaseMap.Id);
        Assert.Equal("双人图 9×9", info.Title);
        Assert.Equal(1, MapCatalog.BuiltinIds.ToList().IndexOf(TwoPlayerBaseMap.Id));
        Assert.Equal(MapCatalog.DefaultId, MapCatalog.BuiltinIds[0]);
        Assert.Equal(MapFile.ToJson(Map()), MapFile.ToJson(MapCatalog.Resolve(TwoPlayerBaseMap.Id)));
    }

    [Fact]
    public void 磁盘上的地图文件是规范写法_与内置图逐项一致()
    {
        // 权威数据是手工编写的 maps/siege-2p-base-v1.json（嵌入资源）。它必须是 MapFile 的规范写法：
        // `Siege.Sim map --map siege-2p-base-v1` 会按内置图重新导出这个文件，非规范写法会被悄悄改写。
        string path = Path.Combine(FrontierFixtures.RepoRoot(), "maps", TwoPlayerBaseMap.Id + ".json");
        string disk = File.ReadAllText(path).ReplaceLineEndings("\n");
        MapData onDisk = MapFile.FromJson(disk);

        Assert.Equal(MapFile.ToJson(onDisk).ReplaceLineEndings("\n"), disk);
        Assert.Equal(MapFile.ToJson(Map()), MapFile.ToJson(onDisk));
        Assert.True(disk.Split('\n').Length > 40, "地图文件过短。");
    }
}
