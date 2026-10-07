using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.TerrainSpec;

/// <summary>
/// 规格：terrain —— Requirement: 地形规则的当前来源（retire-legacy-maps 段 D 收尾补）。
/// 所有可加载的地图都由互不连通的棋盘组成：高度 / 深水 / 桥 / 林地 / 土路 / 四种新地表在当前任何地图上都不产生，规则保留待用；
/// 栅栏仍有来源（立栅在棋盘图上有合法目标）。
/// </summary>
public class 地形规则的当前来源Tests
{
    /// <summary>三张内置棋盘图 + 2 / 3 / 4 人各三颗种子的 <c>board:</c> 生成图。</summary>
    private static IEnumerable<string> BoardMapIds() =>
        MapCatalog.BuiltinIds.Concat(
            from players in new[] { 2, 3, 4 }
            from seed in new[] { 1, 2, 12345 }
            select $"board:{seed}:p{players}");

    [Fact]
    public void 棋盘图上不产生不可达地形()
    {
        // 读的是开局地图的地形数据本身（Heights / Surfaces / Bridges / Fences 四张表），逐格再经 HeightAt / SurfaceAt 读一遍。
        // 变异 D-T1（段 D 实跑）：BoardMapGenerator 给生成的地图写一格林地（左下角场景格 A1，经 TerrainData）→ 本测试红 1。
        string[] ids = [.. BoardMapIds()];
        Assert.True(ids.Length >= 12, $"样本口径：只有 {ids.Length} 张图");
        Assert.Equal(3, MapCatalog.BuiltinIds.Count());
        int cells = 0;
        foreach (string id in ids)
        {
            MapData map = MapCatalog.Resolve(id);
            Assert.NotEmpty(map.Boards);   // 前提：确是棋盘组成的图
            TerrainData t = map.TerrainData;
            Assert.True(t.Heights.Values.All(h => h == 0), $"{id}：有高度不为 0 的格");
            Assert.True(t.Surfaces.Values.All(s => s == Surface.Grass), $"{id}：有非草地地表 {string.Join(",", t.Surfaces.Values.Where(s => s != Surface.Grass).Distinct())}");
            Assert.Empty(t.Bridges);
            Assert.Empty(t.Fences);
            foreach (Coord c in map.AllCoords())
            {
                Assert.Equal(0, map.HeightAt(c));
                Assert.Equal(Surface.Grass, map.SurfaceAt(c));
                Assert.False(t.IsUnbridgedDeepWater(c), $"{id}：{c.ToNotation()} 是深水");
                cells++;
            }
        }

        Assert.True(cells > 10_000, $"样本口径：只扫了 {cells} 格");
    }

    [Fact]
    public void 地形规则在合成盘面上照常生效()
    {
        // 规则保留待用：不经校验构造的合成盘面上，h=0 孤子 F6 上方 F7 是 h=2 的空格 → F7 不计入气（与 气边Tests.崖壁切断气 同一判定，这里钉"删图之后仍在"）。
        // 反面：同一盘面去掉高度 → F7 计入气。
        // 变异 D-T3（段 D 实跑）：Adjacency.LibertyNeighbors 的崖壁判据 `< CliffDrop` 改成 `<= CliffDrop` → 本测试红 1。
        GameBoard cliff = TestMaps.Blank(TestMaps.Terrain(heights: [("F7", 2)])).Place("F6", TestMaps.P0);
        Assert.Equal(["F5", "E6", "G6"], cliff.LibertiesOf(cliff.GroupAt(TestMaps.At("F6"))!).Notations());

        GameBoard flat = TestMaps.Blank().Place("F6", TestMaps.P0);
        Assert.Contains(TestMaps.At("F7"), flat.LibertiesOf(flat.GroupAt(TestMaps.At("F6"))!));
    }

    [Fact]
    public void 棋盘图上立栅有合法目标()
    {
        // 在 4 人内置棋盘图上：出生棋盘 1 内四邻全是棋盘格的落点 c，匠人的合法改造目标（工坊开 / 关都查）含立栅、不含任何搭桥与烧林；
        // 贴着场景格的落点另有"棋盘格—场景格"的边。立栅之后：栅栏挡气（两枚己方子不再连成一串、邻格不再是气）而不挡覆盖。
        // 变异 D-T4（段 D 实跑）：TerrainEditRules.LegalTargets 的边目标只收两端都可落子的边 → 本测试红 1（场景格边断言）。
        // 变异 D-T5（段 D 实跑）：Adjacency.LibertyNeighbors 去掉栅栏判断 → 本测试红 1。
        MapData map = MapCatalog.Resolve(SimFixtures.Board4);
        var zone = map.BirthZones[0].ToHashSet();
        Coord inner = zone.Order().First(c => Adjacency.Neighbors(map.Width, map.Height, c).Count(zone.Contains) == 4);
        Coord rim = zone.Order().First(c => Adjacency.Neighbors(map.Width, map.Height, c).Any(n => !map.IsPlayable(n)));

        foreach (bool workshop in new[] { false, true })
        {
            var targets = TerrainEditRules.LegalTargets(map, inner, workshop);
            Assert.Contains(targets, e => e.Kind == TerrainEditKind.Fence);
            Assert.DoesNotContain(targets, e => e.Kind is TerrainEditKind.Bridge or TerrainEditKind.Burn);
            Assert.DoesNotContain(TerrainEditRules.LegalTargets(map, rim, workshop), e => e.Kind is TerrainEditKind.Bridge or TerrainEditKind.Burn);
        }

        Assert.Contains(TerrainEditRules.LegalTargets(map, rim), e => e.Kind == TerrainEditKind.Fence && (!map.IsPlayable(e.Edge.A) || !map.IsPlayable(e.Edge.B)));

        Coord right = Adjacency.Neighbors(map.Width, map.Height, inner).Order().First();
        TerrainEdit fence = TerrainEdit.Fence(inner, right);
        Assert.True(TerrainEditRules.IsLegal(map, inner, fence));

        GameBoard board = GameBoard.Load(map);
        board.Place(inner.ToNotation(), TestMaps.P0).Place(right.ToNotation(), TestMaps.P0);
        Assert.Equal(2, board.GroupAt(inner)!.Size);   // 反面：立栅之前连成一串

        board.ApplyTerrainEdits([fence]);
        Assert.True(board.Map.HasFence(inner, right));
        Assert.Equal(1, board.GroupAt(inner)!.Size);
        Assert.Equal(1, board.GroupAt(right)!.Size);
        Assert.DoesNotContain(right, board.LibertyNeighbors(inner));
        Assert.Contains(right, board.CoverageTargets(inner));   // 栅栏挡气不挡覆盖
    }
}
