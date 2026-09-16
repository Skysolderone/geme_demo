using Siege.Core.Board;

namespace Siege.Core.Tests.TerrainSpec;

/// <summary>规格：terrain —— Requirement: 边属性</summary>
public class 边属性Tests
{
    [Fact]
    public void 栅栏两侧均可落子()
    {
        // F6 与 G6 之间有栅栏，两格均为空草地 → 两格都是合法落点
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(fences: [("F6", "G6")]));

        Assert.True(board.Map.HasFence(TestMaps.At("F6"), TestMaps.At("G6")));
        Assert.True(board[TestMaps.At("F6")].IsPlayableEmpty);
        Assert.True(board[TestMaps.At("G6")].IsPlayableEmpty);
        board.Place("F6", TestMaps.P0).Place("G6", TestMaps.P1);
        Assert.Equal(TestMaps.P0, board[TestMaps.At("F6")].Occupant!.Value.Owner);
        Assert.Equal(TestMaps.P1, board[TestMaps.At("G6")].Occupant!.Value.Owner);
    }

    [Fact]
    public void 栅栏只能标在相邻格之间()
    {
        // F6 与 H6 不相邻 → 构造被拒并指出这对坐标
        var ex = Assert.Throws<ArgumentException>(() => TestMaps.Terrain(fences: [("F6", "H6")]));

        Assert.Contains("F6", ex.Message, StringComparison.Ordinal);
        Assert.Contains("H6", ex.Message, StringComparison.Ordinal);
        // 斜向也不是相邻
        Assert.Throws<ArgumentException>(() => TestMaps.Terrain(fences: [("F6", "G7")]));
    }

    [Fact]
    public void 栅栏边不分方向()
    {
        var fence = new FenceEdge(TestMaps.At("G6"), TestMaps.At("F6"));

        Assert.Equal(new FenceEdge(TestMaps.At("F6"), TestMaps.At("G6")), fence);
        Assert.Equal("F6-G6", fence.ToString());
        Assert.Throws<ArgumentException>(() => new FenceEdge(TestMaps.At("F6"), TestMaps.At("F6")));

        TerrainData terrain = TestMaps.Terrain(fences: [("G6", "F6")]);
        Assert.True(terrain.HasFence(TestMaps.At("F6"), TestMaps.At("G6")));
        Assert.True(terrain.HasFence(TestMaps.At("G6"), TestMaps.At("F6")));
        Assert.False(terrain.HasFence(TestMaps.At("F6"), TestMaps.At("F6")));
    }
}
