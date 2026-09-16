using Siege.Core.Board;

namespace Siege.Core.Tests.BoardTopology;

/// <summary>规格：board-topology —— Requirement: 棋串构成</summary>
public class 棋串构成Tests
{
    [Fact]
    public void 跨类型合并棋串()
    {
        // 设计文档 §9.2：不同类型的己方棋子相邻时合并为同一棋串并共享气
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("C3", TestMaps.P0, PieceType.Basic)
            .Place("C4", TestMaps.P0, PieceType.Fortress)
            .Place("C5", TestMaps.P0, PieceType.Multiplier);

        Group group = board.GroupAt(TestMaps.At("C4"))!;

        Assert.Equal(3, group.Size);
        Assert.Equal(["C3", "C4", "C5"], group.Stones.Notations());
    }

    [Fact]
    public void 敌我相邻不合并()
    {
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("D4", TestMaps.P0)
            .Place("E4", TestMaps.P1);

        Group mine = board.GroupAt(TestMaps.At("D4"))!;
        Group theirs = board.GroupAt(TestMaps.At("E4"))!;

        Assert.Equal(1, mine.Size);
        Assert.Equal(1, theirs.Size);
        Assert.Equal(TestMaps.P0, mine.Owner);
        Assert.Equal(TestMaps.P1, theirs.Owner);

        // 互相占据对方原本可用的气
        Assert.DoesNotContain(TestMaps.At("E4"), board.LibertiesOf(mine));
    }

    [Fact]
    public void 空格与障碍不属于任何棋串()
    {
        GameBoard board = TestMaps.Blank(size: 5, "C3");

        Assert.Null(board.GroupAt(TestMaps.At("C3")));
        Assert.Null(board.GroupAt(TestMaps.At("B2")));
    }

    [Fact]
    public void 崖壁两侧不成串()
    {
        // 同一玩家的两枚棋子位于 h=0 的 F6 与 h=2 的 F7 → 分属两个棋串
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(heights: [("F7", 2)]))
            .Place("F6", TestMaps.P0)
            .Place("F7", TestMaps.P0);

        Group low = board.GroupAt(TestMaps.At("F6"))!;
        Group high = board.GroupAt(TestMaps.At("F7"))!;

        Assert.Equal(1, low.Size);
        Assert.Equal(1, high.Size);
        Assert.Equal(2, board.GroupsOf(TestMaps.P0).Length);
    }

    [Fact]
    public void 棋串坐标按字典序稳定()
    {
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("C4", TestMaps.P0)
            .Place("B4", TestMaps.P0)
            .Place("D4", TestMaps.P0);

        Assert.Equal(["B4", "C4", "D4"], board.GroupAt(TestMaps.At("D4"))!.Stones.Notations());
    }
}
