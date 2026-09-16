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
}
