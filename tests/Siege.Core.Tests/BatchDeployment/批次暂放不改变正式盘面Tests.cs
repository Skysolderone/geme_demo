using Siege.Core.Batch;
using Siege.Core.Board;

namespace Siege.Core.Tests.BatchDeployment;

/// <summary>规格：batch-deployment —— Requirement: 批次暂放不改变正式盘面</summary>
public class 批次暂放不改变正式盘面Tests
{
    [Fact]
    public void 暂放不影响盘面()
    {
        // 变异验证：把 StagedBatch.TryApply 里的校验换成 Board.Place(...) 真落子 → 本测试红 1（Serialize 不再相等）。
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("D4", TestMaps.P0)
            .Place("E4", TestMaps.P1, PieceType.Fortress);
        string before = board.Serialize();
        string groupsBefore = string.Join("|", board.AllGroups().Select(g => g.ToString()));

        var batch = new StagedBatch(board, BatchFixtures.Context(board, TestMaps.P0));
        Assert.Null(batch.Stage(TestMaps.At("C3"), PieceType.Basic));
        Assert.Null(batch.Stage(TestMaps.At("F6"), PieceType.Line));
        Assert.Null(batch.Stage(TestMaps.At("D5"), PieceType.Multiplier));
        Assert.Equal(3, batch.Count);

        // 正式盘面逐字节不变；棋串与气也不变——其他玩家看到的盘面就是这份
        Assert.Equal(before, board.Serialize());
        Assert.Equal(groupsBefore, string.Join("|", board.AllGroups().Select(g => g.ToString())));
        Assert.Null(board[TestMaps.At("C3")].Occupant);
        Assert.Null(board[TestMaps.At("F6")].Occupant);
        Assert.Null(board[TestMaps.At("D5")].Occupant);
    }

    [Fact]
    public void 自由撤销与换位()
    {
        // 设计文档 §5.4：暂放 E5 → 移到 F5 → 换成另一类型，暂放数量始终为 1，手牌库存不变。
        // 变异验证：Move 改成"先 Stage 新格再 Unstage 旧格"（中途 Count 变 2 且受上限约束）→ 用上限 1 的本测试红 1。
        GameBoard board = TestMaps.Blank(size: 7);
        IReadOnlyDictionary<PieceType, int> stock = BatchFixtures.Stock((PieceType.Basic, 1), (PieceType.Line, 1));
        var batch = new StagedBatch(board, BatchFixtures.Context(board, TestMaps.P0, limit: 1, stock: stock));

        Assert.Null(batch.Stage(TestMaps.At("E5"), PieceType.Basic));
        Assert.Equal(1, batch.Count);

        Assert.Null(batch.Move(TestMaps.At("E5"), TestMaps.At("F5")));
        Assert.Equal(1, batch.Count);
        Assert.Equal(BatchFixtures.P("F5"), batch.Placements[0]);

        Assert.Null(batch.Replace(TestMaps.At("F5"), PieceType.Line));
        Assert.Equal(1, batch.Count);
        Assert.Equal(BatchFixtures.P("F5", PieceType.Line), batch.Placements[0]);

        // 次数不限：再换回去、撤销、重新暂放
        Assert.Null(batch.Replace(TestMaps.At("F5"), PieceType.Basic));
        Assert.True(batch.Unstage(TestMaps.At("F5")));
        Assert.Equal(0, batch.Count);
        Assert.False(batch.Unstage(TestMaps.At("F5")));
        Assert.Null(batch.Stage(TestMaps.At("E5"), PieceType.Line));
        Assert.Equal(1, batch.Count);

        // 手牌库存从未被扣减
        Assert.Equal(1, stock[PieceType.Basic]);
        Assert.Equal(1, stock[PieceType.Line]);
        Assert.Equal(TestMaps.Blank(size: 7).Serialize(), board.Serialize());
    }
}
