using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.TerrainSpec;

/// <summary>规格：terrain —— Requirement: 格属性</summary>
public class 格属性Tests
{
    [Fact]
    public void 深水拒绝落子()
    {
        // G6 为未架桥的深水 → 任何写入被拒并报"目标格不可落子"
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("G6", Surface.DeepWater)]));

        Assert.False(board.Map.IsPlayable(TestMaps.At("G6")));
        Assert.Equal(Siege.Core.Board.Terrain.Obstacle, board[TestMaps.At("G6")].Terrain);
        var ex = Assert.Throws<SiegeRuleException>(() => board.Place(TestMaps.At("G6"), TestMaps.P0, PieceType.Basic));
        Assert.Contains("不可落子", ex.Message, StringComparison.Ordinal);
        Assert.Contains("G6", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 桥格可落子()
    {
        // G6 深水架桥 → 落子成功，气与覆盖按普通格：G6 上的孤子有 4 气、向四邻覆盖
        // 变异验证 M-A4：MapData.IsPlayable 忽略 HasBridge（桥格仍当深水）→ 本测试红。
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("G6", Surface.DeepWater)], bridges: ["G6"]));

        Assert.True(board.Map.IsPlayable(TestMaps.At("G6")));
        board.Place("G6", TestMaps.P0);

        Group group = board.GroupAt(TestMaps.At("G6"))!;
        Assert.Equal(["G5", "F6", "H6", "G7"], board.LibertiesOf(group).Notations());
        CoverageMap coverage = CoverageMap.Compute(board);
        Assert.All(new[] { "G5", "F6", "H6", "G7" }, c => Assert.Equal(TestMaps.P0, coverage.UniqueCoverer(TestMaps.At(c))));
    }

    [Fact]
    public void 桥只能架在深水上()
    {
        // 桥标在草地 F6 → 构造被拒并指出坐标
        var ex = Assert.Throws<ArgumentException>(() => TestMaps.Terrain(bridges: ["F6"]));

        Assert.Contains("F6", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 高度只有三档()
    {
        var ex = Assert.Throws<ArgumentException>(() => TestMaps.Terrain(heights: [("F6", 3)]));

        Assert.Contains("F6", ex.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => TestMaps.Terrain(heights: [("F6", -1)]));
    }

    [Fact]
    public void 缺省为平地草地无桥无栅()
    {
        GameBoard board = TestMaps.Blank(size: 7);
        MapData map = board.Map;

        Assert.Same(TerrainData.Flat, map.TerrainData);
        Assert.All(map.AllCoords(), c =>
        {
            Assert.Equal(0, map.HeightAt(c));
            Assert.Equal(Surface.Grass, map.SurfaceAt(c));
            Assert.False(map.HasBridge(c));
        });
        Assert.Equal(49, map.PlayableCount);
    }

    [Fact]
    public void 林地是普通可落子格()
    {
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("G6", Surface.Forest)]));

        Assert.True(board.Map.IsPlayable(TestMaps.At("G6")));
        board.Place("G6", TestMaps.P0);
        Assert.Equal(4, board.LibertiesOf(board.GroupAt(TestMaps.At("G6"))!).Length);
    }

    [Fact]
    public void 深水不计入可落子格数()
    {
        GameBoard board = TestMaps.Blank(
            TestMaps.Terrain(surfaces: [("G6", Surface.DeepWater), ("H6", Surface.DeepWater)], bridges: ["H6"]),
            size: 7,
            "A1");

        // 49 − 1 障碍 − 1 未架桥深水；桥格 H6 仍计入
        Assert.Equal(47, board.Map.PlayableCount);
    }


    // ---------- terrain-surfaces 段 0：四种新地表只是"可落子格"，不动气边 ----------

    [Theory]
    [InlineData(Surface.Desert)]
    [InlineData(Surface.Marsh)]
    [InlineData(Surface.Crag)]
    [InlineData(Surface.Shallows)]
    public void 新地表可落子(Surface surface)
    {
        // 规格 terrain-surfaces · terrain「格属性」Scenario「新地表可落子」：四种新地表在可落子性上与草地相同。
        // 变异验证 M-S0a（实跑）：MapData.IsPlayable 把 Shallows 当不可落子 → 本测试与「新地表不改变气边」各红 1。
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("G6", surface)]));

        Assert.True(board.Map.IsPlayable(TestMaps.At("G6")));
        Assert.Equal(Siege.Core.Board.Terrain.Playable, board[TestMaps.At("G6")].Terrain);
        board.Place("G6", TestMaps.P0);
        Assert.Equal(TestMaps.P0, board[TestMaps.At("G6")].Occupant?.Owner);
        Assert.Equal(121, board.Map.PlayableCount);
    }

    [Theory]
    [InlineData(Surface.Desert)]
    [InlineData(Surface.Marsh)]
    [InlineData(Surface.Crag)]
    [InlineData(Surface.Shallows)]
    public void 新地表不改变气边(Surface surface)
    {
        // 规格 Scenario「新地表不改变气边」：同高、无栅栏的草地 F6 与新地表 G6 之间始终有气边，两格上的同色子同属一串。
        // 算例取拐角串 F6-G6-G7（testing.md：直线相邻测不出去重），G6 的四个气边邻格都要在 LibertyNeighbors 里。
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("G6", surface)]));
        board.Place("F6", TestMaps.P0);
        board.Place("G6", TestMaps.P0);
        board.Place("G7", TestMaps.P0);

        Group group = board.GroupAt(TestMaps.At("F6"))!;
        Assert.Equal(group.Stones, board.GroupAt(TestMaps.At("G6"))!.Stones);
        Assert.Equal(group.Stones, board.GroupAt(TestMaps.At("G7"))!.Stones);
        Assert.Equal(3, group.Stones.Length);
        Assert.Equal(new[] { "F6", "G5", "G7", "H6" }, Adjacency.LibertyNeighbors(board.Map, TestMaps.At("G6")).Notations().Order());
    }

    [Fact]
    public void 新地表与高度独立()
    {
        // 规格 Scenario「新地表与高度独立」：h=0 的岩台、h=2 的浅滩加载成功，高度按 0 与 2 参与判定。
        TerrainData terrain = TestMaps.Terrain(
            heights: [("H6", 2)],
            surfaces: [("F6", Surface.Crag), ("H6", Surface.Shallows)]);
        MapData map = TestMaps.Blank(terrain).Map;

        Assert.Equal(0, map.HeightAt(TestMaps.At("F6")));
        Assert.Equal(Surface.Crag, map.SurfaceAt(TestMaps.At("F6")));
        Assert.Equal(2, map.HeightAt(TestMaps.At("H6")));
        Assert.Equal(Surface.Shallows, map.SurfaceAt(TestMaps.At("H6")));

        // h=2 的浅滩与 h=0 的 G6 之间是崖壁：高度照常参与气边判定，与地表无关
        Assert.DoesNotContain(TestMaps.At("G6"), Adjacency.LibertyNeighbors(map, TestMaps.At("H6")));
    }
}
