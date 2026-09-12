using Siege.Core.Board;

namespace Siege.Core.Tests.BoardTopology;

/// <summary>规格：board-topology —— Requirement: 四邻接是唯一邻接语义</summary>
public class 四邻接Tests
{
    [Fact]
    public void 斜向不相邻()
    {
        // 同一玩家的两枚棋子位于 D4 与 E5，二者之间无其他己方棋子
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("D4", TestMaps.P0)
            .Place("E5", TestMaps.P0);

        Group a = board.GroupAt(TestMaps.At("D4"))!;
        Group b = board.GroupAt(TestMaps.At("E5"))!;

        Assert.Equal(1, a.Size);
        Assert.Equal(1, b.Size);
        Assert.DoesNotContain(TestMaps.At("E5"), a.Stones);
    }

    [Fact]
    public void 边角格邻居数量()
    {
        GameBoard board = TestMaps.Blank(size: 11);

        Assert.Equal(["B1", "A2"], board.Neighbors(TestMaps.At("A1")).Notations());
    }

    [Fact]
    public void 邻居不含斜向()
    {
        GameBoard board = TestMaps.Blank(size: 11);

        Assert.Equal(["F5", "E6", "G6", "F7"], board.Neighbors(TestMaps.At("F6")).Notations());
    }

    [Fact]
    public void 邻接实现唯一()
    {
        // GameBoard 必须委托给 Adjacency，而不是自带一份遍历
        GameBoard board = TestMaps.Blank(size: 11);

        foreach (Coord c in board.AllCoords())
        {
            Assert.Equal(Adjacency.Neighbors(board.Width, board.Height, c), board.Neighbors(c));
        }
    }
}
