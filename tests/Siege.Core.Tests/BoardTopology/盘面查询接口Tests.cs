using Siege.Core.Board;

namespace Siege.Core.Tests.BoardTopology;

/// <summary>规格：board-topology —— Requirement: 盘面查询接口</summary>
public class 盘面查询接口Tests
{
    [Fact]
    public void 棋盘填满查询()
    {
        // 设计文档 §12.3 终局条件 3
        GameBoard board = TestMaps.Blank(size: 3, "B2");
        Assert.True(board.HasPlayableEmptyCell());

        foreach (Coord c in board.AllCoords().Where(c => board[c].IsPlayableEmpty))
        {
            board.Place(c, TestMaps.P0, PieceType.Basic);
        }

        Assert.False(board.HasPlayableEmptyCell());
    }

    [Fact]
    public void 查询不产生副作用()
    {
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("D4", TestMaps.P0)
            .Place("E5", TestMaps.P1);
        string before = board.Serialize();

        for (int i = 0; i < 1000; i++)
        {
            foreach (Coord c in board.AllCoords())
            {
                _ = board[c];
                _ = board.Neighbors(c);
                Group? g = board.GroupAt(c);
                if (g is not null)
                {
                    _ = board.LibertiesOf(g);
                }
            }

            _ = board.GroupsOf(TestMaps.P0);
            _ = board.HasPlayableEmptyCell();
        }

        Assert.Equal(before, board.Serialize());
    }

    [Fact]
    public void 枚举玩家棋串()
    {
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("B2", TestMaps.P0)
            .Place("B3", TestMaps.P0)
            .Place("F6", TestMaps.P0)
            .Place("D4", TestMaps.P1);

        var groups = board.GroupsOf(TestMaps.P0);

        Assert.Equal(2, groups.Length);
        Assert.Equal(["B2", "B3"], groups[0].Stones.Notations());
        Assert.Equal(["F6"], groups[1].Stones.Notations());
        Assert.Single(board.GroupsOf(TestMaps.P1));
    }

    [Fact]
    public void 障碍不计入可落子空格()
    {
        GameBoard board = TestMaps.Blank(size: 2, "A1", "B1", "A2");

        Assert.True(board.HasPlayableEmptyCell());
        board.Place(TestMaps.At("B2"), TestMaps.P0, PieceType.Basic);
        Assert.False(board.HasPlayableEmptyCell());
    }
}
