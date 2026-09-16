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
        // 某空格同时与棋串的两枚棋子相邻 → 只计一次。
        // 注意：四邻接下两枚"直线相邻"的棋子不可能共用邻格（它们之间那格正是棋子本身），
        // 所以必须用拐角棋串 D4-D5-E5 才真的触发去重：E4 同时邻接 D4 与 E5。
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("D4", TestMaps.P0)
            .Place("D5", TestMaps.P0)
            .Place("E5", TestMaps.P0);

        Group group = board.GroupAt(TestMaps.At("D4"))!;
        var liberties = board.LibertiesOf(group);

        Assert.Equal(3, group.Size);
        Assert.Equal(liberties.Length, liberties.Distinct().Count());

        // E4 邻接 D4 与 E5 两枚棋子，在气集合里只出现一次
        Assert.Single(liberties, c => c == TestMaps.At("E4"));
        // 顺序必须是坐标字典序（先行后列），不是"邻居遍历的插入顺序"——
        // 这两者在这个拐角棋串上恰好不同（F5 与 D6 会换位），所以这个断言钉得住排序。
        // 规范：.trellis/spec/core/determinism.md —— 并列必须确定性打破
        Assert.Equal(["D3", "C4", "E4", "C5", "F5", "D6", "E6"], liberties.Notations());
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

    [Fact]
    public void 靠崖壁的棋串()
    {
        // h=0 的棋串 F6 三面（F5/E6/G6）被敌子围住，第四面 F7 是 h=2 的空格 → 无气
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(heights: [("F7", 2)]))
            .Place("F6", TestMaps.P0)
            .Place("F5", TestMaps.P1)
            .Place("E6", TestMaps.P1)
            .Place("G6", TestMaps.P1);

        Group trapped = board.GroupAt(TestMaps.At("F6"))!;

        Assert.True(board[TestMaps.At("F7")].IsPlayableEmpty);
        Assert.Empty(board.LibertiesOf(trapped));
        Assert.True(board.IsCaptured(trapped));
    }
}
