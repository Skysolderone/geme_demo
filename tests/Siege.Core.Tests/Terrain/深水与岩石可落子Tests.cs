using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.TerrainSpec;

/// <summary>规格：terrain —— Requirement: 格属性（terrain-bonus：未架桥深水与岩石可落子）</summary>
public class 深水与岩石可落子Tests
{
    [Fact]
    public void 深水可落子()
    {
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("G6", Surface.DeepWater)]));

        Assert.True(board.Map.IsPlayable(TestMaps.At("G6")));
        board.Place("G6", TestMaps.P0);
        Assert.Equal(TestMaps.P0, board[TestMaps.At("G6")].Occupant!.Value.Owner);
    }

    [Fact]
    public void 空深水不是气()
    {
        // F6 孤子，右侧 G6 为空的未架桥深水 → 气只有 F5、E6、F7
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("G6", Surface.DeepWater)])).Place("F6", TestMaps.P0);

        Assert.Equal(["F5", "E6", "F7"], board.LibertiesOf(board.GroupAt(TestMaps.At("F6"))!).Notations());
    }

    [Fact]
    public void 深水上的棋子照常有气与连串()
    {
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("F6", Surface.DeepWater)])).Place("F6", TestMaps.P0).Place("G6", TestMaps.P0);

        Group group = board.GroupAt(TestMaps.At("F6"))!;
        Assert.Equal(2, group.Size);
        Assert.Equal(["F5", "G5", "E6", "H6", "F7", "G7"], board.LibertiesOf(group).Notations());
    }

    [Fact]
    public void 空深水不是覆盖目标且覆盖照旧隔水()
    {
        // F6 的子：G6 为深水 → 不覆盖 G6、改覆盖对岸 H6
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("G6", Surface.DeepWater)])).Place("F6", TestMaps.P0);

        CoverageMap coverage = CoverageMap.Compute(board);
        Assert.Null(coverage.UniqueCoverer(TestMaps.At("G6")));
        Assert.Equal(TestMaps.P0, coverage.UniqueCoverer(TestMaps.At("H6")));
    }

    [Fact]
    public void 岩石可落子且其上的棋子不产生覆盖()
    {
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("F6", Surface.Rock)])).Place("F6", TestMaps.P0);

        Assert.Empty(board.CoverageTargets(TestMaps.At("F6")));
        Assert.Equal(["F5", "E6", "G6", "F7"], board.LibertiesOf(board.GroupAt(TestMaps.At("F6"))!).Notations());
    }

    [Fact]
    public void 陆地口径不含未架桥深水()
    {
        // 地图生成 / 校验 / 插旗 / 遗址只认陆地（实现决策 I7）：未架桥深水不是陆地，桥格是
        MapData map = TestMaps.Blank(TestMaps.Terrain(surfaces: [("F6", Surface.DeepWater), ("G6", Surface.DeepWater)], bridges: ["G6"])).Map;

        Assert.False(map.IsLand(TestMaps.At("F6")));
        Assert.True(map.IsLand(TestMaps.At("G6")));
        Assert.Equal(map.PlayableCount - 1, map.LandCount);
    }
}
