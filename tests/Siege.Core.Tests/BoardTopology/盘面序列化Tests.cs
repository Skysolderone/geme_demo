using Siege.Core.Board;

namespace Siege.Core.Tests.BoardTopology;

/// <summary>规格：board-topology —— Requirement: 盘面状态可序列化且确定性</summary>
public class 盘面序列化Tests
{
    [Fact]
    public void 同位置不同类型不等价()
    {
        // 设计文档 §6.2：同一玩家在同一位置用不同类型棋子形成的盘面不视为同形
        GameBoard basic = TestMaps.Blank(size: 5).Place("C3", TestMaps.P0, PieceType.Basic);
        GameBoard fortress = TestMaps.Blank(size: 5).Place("C3", TestMaps.P0, PieceType.Fortress);

        Assert.NotEqual(basic.Serialize(), fortress.Serialize());
    }

    [Fact]
    public void 非盘面信息不影响序列化()
    {
        // 两份盘面的格子占用完全一致；手牌与信物控制不属于序列化内容，因此结果必须相等。
        // 这里用"地图的信物标注不同但占用相同"来代表非盘面差异。
        GameBoard plain = TestMaps.Blank(size: 11).Place("D4", TestMaps.P0, PieceType.Line);
        GameBoard withRelics = GameBoard.LoadUnvalidated(Siege.Core.Board.Maps.FourPlayerBaseMap.Create());
        withRelics.Place(TestMaps.At("D4"), TestMaps.P0, PieceType.Line);

        Assert.Equal(plain.Serialize(), withRelics.Serialize());
    }

    [Fact]
    public void 重复序列化逐字节一致()
    {
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("B2", TestMaps.P0, PieceType.Synergy)
            .Place("F6", TestMaps.P1, PieceType.Multiplier);

        string first = board.Serialize();
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(first, board.Serialize());
        }
    }

    [Fact]
    public void 落子顺序不影响序列化()
    {
        GameBoard a = TestMaps.Blank(size: 7)
            .Place("B2", TestMaps.P0)
            .Place("F6", TestMaps.P1)
            .Place("D4", TestMaps.P0);
        GameBoard b = TestMaps.Blank(size: 7)
            .Place("D4", TestMaps.P0)
            .Place("F6", TestMaps.P1)
            .Place("B2", TestMaps.P0);

        Assert.Equal(a.Serialize(), b.Serialize());
    }

    [Fact]
    public void 不同玩家不等价()
    {
        GameBoard a = TestMaps.Blank(size: 5).Place("C3", TestMaps.P0);
        GameBoard b = TestMaps.Blank(size: 5).Place("C3", TestMaps.P1);

        Assert.NotEqual(a.Serialize(), b.Serialize());
    }
}
