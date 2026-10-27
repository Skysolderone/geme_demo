using Siege.Core.Board;
using Siege.Core.PieceRelics;
using static Siege.Core.Tests.PieceRelics.PieceRelicFixtures;

namespace Siege.Core.Tests.PieceRelics;

/// <summary>
/// relic-redesign RQ1 / AC1（盘面层面）：信物层每格至多 1 件；落子到地图信物格即挂在新子上；持物棋子被提后信物留在原格；同一枚棋子不能挂第 2 件。
/// design.md D1：拾取 / 掉落不需要额外操作——<see cref="GameBoard.Place"/> 与 <see cref="GameBoard.RemoveStones"/> 都不碰信物层。
/// 变异验证（加法段实跑）：M8 AttachRelic 不查同格已有信物 → 红 3（本类两条 + 序列化的重复格用例）。
/// </summary>
public class 信物挂在棋子上Tests
{
    [Fact]
    public void 落子到地图信物格即挂在新子上()
    {
        GameBoard board = TestMaps.Blank(size: 9).Attach("E5", Map(0, RelicKind.WarDrum));
        Assert.Null(board[TestMaps.At("E5")].Occupant);

        board.Place("E5", TestMaps.P0);

        Assert.Equal(Map(0, RelicKind.WarDrum), board.RelicAt(TestMaps.At("E5")));
        Assert.Equal(TestMaps.P0, board[TestMaps.At("E5")].Occupant!.Value.Owner);

        // 挂上即生效：战鼓让持物棋子基础军势 1 → 4。
        Assert.Equal(4, OnlyGroup(board, TestMaps.P0).Power);
    }

    [Fact]
    public void 持物棋子被提后信物留在原格成为地图信物()
    {
        GameBoard board = TestMaps.Blank(size: 9).Place("E5", TestMaps.P0).Attach("E5", Carried(3, RelicKind.Banner, TestMaps.P0));

        board.RemoveStones([TestMaps.At("E5")]);

        Assert.Null(board[TestMaps.At("E5")].Occupant);
        Assert.Equal(Carried(3, RelicKind.Banner, TestMaps.P0), board.RelicAt(TestMaps.At("E5")));

        // 另一名玩家落到这一格即拾取（不需要额外操作）。
        board.Place("E5", TestMaps.P1);
        Assert.Equal(3, board.RelicAt(TestMaps.At("E5"))!.Value.Id);
        Assert.Equal(TestMaps.P1, board[TestMaps.At("E5")].Occupant!.Value.Owner);
    }

    [Fact]
    public void 清除棋子同样不碰信物层()
    {
        GameBoard board = TestMaps.Blank(size: 9).Place("C3", TestMaps.P0).Attach("C3", Map(1, RelicKind.Command));

        board.Clear(TestMaps.At("C3"));

        Assert.Equal(Map(1, RelicKind.Command), board.RelicAt(TestMaps.At("C3")));
    }

    [Fact]
    public void 同一棋子不能挂第二件()
    {
        GameBoard board = TestMaps.Blank(size: 9).Place("E5", TestMaps.P0).Attach("E5", Map(0, RelicKind.WarDrum));
        string before = board.Serialize();

        Assert.Throws<SiegeRuleException>(() => board.AttachRelic(TestMaps.At("E5"), Carried(1, RelicKind.Command, TestMaps.P0)));

        Assert.Equal(Map(0, RelicKind.WarDrum), board.RelicAt(TestMaps.At("E5")));
        Assert.Equal(before, board.Serialize());
    }

    [Fact]
    public void 地图信物格上也不能再放第二件()
    {
        GameBoard board = TestMaps.Blank(size: 9).Attach("E5", Map(0, RelicKind.WarDrum));

        Assert.Throws<SiegeRuleException>(() => board.AttachRelic(TestMaps.At("E5"), Map(1, RelicKind.Banner)));
    }

    [Fact]
    public void 编号在盘面上必须唯一()
    {
        GameBoard board = TestMaps.Blank(size: 9).Attach("E5", Map(7, RelicKind.WarDrum));

        Assert.Throws<SiegeRuleException>(() => board.AttachRelic(TestMaps.At("A1"), Map(7, RelicKind.Command)));
        Assert.Null(board.RelicAt(TestMaps.At("A1")));
    }

    [Fact]
    public void 不可落子格不能放信物()
    {
        GameBoard board = TestMaps.Blank(9, "E5");

        Assert.Throws<SiegeRuleException>(() => board.AttachRelic(TestMaps.At("E5"), Map(0, RelicKind.WarDrum)));
        Assert.False(board.HasRelics);
    }

    [Fact]
    public void 盘面上的信物按坐标字典序列出()
    {
        GameBoard board = TestMaps.Blank(size: 9)
            .Attach("E5", Map(2, RelicKind.Watchtower))
            .Attach("B1", Map(0, RelicKind.WarDrum))
            .Attach("A5", Map(1, RelicKind.Command));

        Assert.Equal(["B1", "A5", "E5"], board.RelicsOnBoard().Select(r => r.Cell).Notations());
        Assert.True(board.HasRelics);
    }

    [Fact]
    public void 副本复制信物层且之后互不可见()
    {
        GameBoard board = TestMaps.Blank(size: 9).Place("E5", TestMaps.P0).Attach("E5", Map(0, RelicKind.WarDrum));

        GameBoard copy = board.Clone();
        copy.AttachRelic(TestMaps.At("A1"), Map(1, RelicKind.Banner));
        board.AttachRelic(TestMaps.At("J9"), Map(2, RelicKind.Command));

        Assert.Equal(Map(0, RelicKind.WarDrum), copy.RelicAt(TestMaps.At("E5")));
        Assert.Null(board.RelicAt(TestMaps.At("A1")));
        Assert.Null(copy.RelicAt(TestMaps.At("J9")));

        // 预演副本看得到挂件效果：副本上的战鼓同样计分。
        Assert.Equal(4, OnlyGroup(copy, TestMaps.P0).Power);
    }

    [Fact]
    public void 空信物层的副本仍为空()
    {
        GameBoard board = TestMaps.Blank(size: 9).Place("E5", TestMaps.P0);

        GameBoard copy = board.Clone();

        Assert.False(copy.HasRelics);
        Assert.Empty(copy.RelicsOnBoard());
        Assert.Equal(board.Serialize(), copy.Serialize());
    }
}
