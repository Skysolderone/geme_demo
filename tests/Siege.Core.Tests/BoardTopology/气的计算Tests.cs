using Siege.Core.Board;

namespace Siege.Core.Tests.BoardTopology;

/// <summary>规格：board-topology —— Requirement: 气的计算</summary>
public class 气的计算Tests
{
    [Fact]
    public void 孤立棋子的气()
    {
        // 设计文档 §10.1：棋盘中央一枚孤立棋子四周均为空 → 气数 4
        GameBoard board = TestMaps.Blank(size: 11).Place("F6", TestMaps.P0);

        Assert.Equal(4, board.LibertiesOf(board.GroupAt(TestMaps.At("F6"))!).Length);
    }

    [Fact]
    public void 障碍减少气()
    {
        // 上方为障碍，其余三面为空 → 气数 3
        GameBoard board = TestMaps.Blank(size: 11, "F7").Place("F6", TestMaps.P0);

        Assert.Equal(["F5", "E6", "G6"], board.LibertiesOf(board.GroupAt(TestMaps.At("F6"))!).Notations());
    }

    [Fact]
    public void 共享气去重()
    {
        // 两枚相邻棋子成串，某空格同时与两枚相邻 → 只计一次
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("D4", TestMaps.P0)
            .Place("D5", TestMaps.P0);

        var liberties = board.LibertiesOf(board.GroupAt(TestMaps.At("D4"))!);

        Assert.Equal(liberties.Length, liberties.Distinct().Count());
        Assert.Equal(["D3", "C4", "E4", "C5", "E5", "D6"], liberties.Notations());
    }

    [Fact]
    public void 无气判定()
    {
        // 四周全被敌方棋子、障碍或棋盘外沿封堵
        GameBoard board = TestMaps.Blank(size: 5, "A2")
            .Place("A1", TestMaps.P0)
            .Place("B1", TestMaps.P1);

        Group trapped = board.GroupAt(TestMaps.At("A1"))!;

        Assert.Empty(board.LibertiesOf(trapped));
        Assert.True(board.IsCaptured(trapped));
    }

    [Fact]
    public void 气随盘面实时重算()
    {
        GameBoard board = TestMaps.Blank(size: 7).Place("D4", TestMaps.P0);
        Assert.Equal(4, board.LibertiesOf(board.GroupAt(TestMaps.At("D4"))!).Length);

        board.Place(TestMaps.At("D5"), TestMaps.P1, PieceType.Basic);
        Assert.Equal(3, board.LibertiesOf(board.GroupAt(TestMaps.At("D4"))!).Length);

        board.Clear(TestMaps.At("D5"));
        Assert.Equal(4, board.LibertiesOf(board.GroupAt(TestMaps.At("D4"))!).Length);
    }
}
