using Siege.Core.Batch;
using Siege.Core.Board;

namespace Siege.Core.Tests.BatchDeployment;

/// <summary>规格：batch-deployment —— Requirement: 批次内落点的多样性</summary>
public class 批次内落点的多样性Tests
{
    [Fact]
    public void 混合类型分散落子()
    {
        // 变异验证：在 ValidateShape 加"每枚落点必须与批内其他落点四邻接"的检查 → 本测试红 1。
        GameBoard board = TestMaps.Blank();
        var batch = new StagedBatch(board, BatchFixtures.Context(board, TestMaps.P0));

        Assert.Null(batch.Stage(TestMaps.At("C3"), PieceType.Fortress));
        Assert.Null(batch.Stage(TestMaps.At("J10"), PieceType.Line));

        Assert.Equal(2, batch.Count);
        Assert.Equal([BatchFixtures.P("C3", PieceType.Fortress), BatchFixtures.P("J10", PieceType.Line)], batch.Placements);

        // 确认同样接受不相连的混合批次
        SettlementOutcome outcome = BatchFixtures.Driver(board).Confirm(batch);
        Assert.True(outcome.Confirmed);
        Assert.Equal(new Occupant(TestMaps.P0, PieceType.Fortress), board[TestMaps.At("C3")].Occupant);
        Assert.Equal(new Occupant(TestMaps.P0, PieceType.Line), board[TestMaps.At("J10")].Occupant);
    }

    [Fact]
    public void 禁止预占将被提空的格()
    {
        // 设计文档 §5.4：G7 上的敌串预计会被本批次提走，仍然不能把棋子暂放到 G7。
        // 变异验证：ValidateShape 删掉 `cell.Occupant is not null` 判断 → Place 抛 SiegeRuleException，本测试红 1
        //（「非法批次必须给出可定位的原因」的文案用例同时红）。
        GameBoard board = TestMaps.Blank()
            .Place("G7", TestMaps.P1)
            .Place("F7", TestMaps.P0)
            .Place("G6", TestMaps.P0)
            .Place("G8", TestMaps.P0);
        SettlementDriver driver = BatchFixtures.Driver(board);
        var batch = new StagedBatch(board, BatchFixtures.Context(board, TestMaps.P0));
        Assert.Null(batch.Stage(TestMaps.At("H7"), PieceType.Basic));

        // 前提成立：这批确实会把 G7 提走
        RehearsalResult rehearsal = driver.Rehearse(batch.Context, batch.Placements);
        Assert.True(rehearsal.IsLegal);
        Assert.Equal(["G7"], rehearsal.Captures.Select(s => s.Coord).Notations());

        BatchFailure? failure = batch.Stage(TestMaps.At("G7"), PieceType.Basic);

        Assert.NotNull(failure);
        Assert.Equal(BatchFailureKind.Occupied, failure.Kind);
        Assert.Equal(["G7"], failure.Coords.Notations());
        Assert.StartsWith("该格当前已被占据", failure.Message, StringComparison.Ordinal);
        Assert.Equal(1, batch.Count);
    }
}
