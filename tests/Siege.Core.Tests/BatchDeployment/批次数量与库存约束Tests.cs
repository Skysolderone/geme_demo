using Siege.Core.Batch;
using Siege.Core.Board;

namespace Siege.Core.Tests.BatchDeployment;

/// <summary>规格：batch-deployment —— Requirement: 批次数量与库存约束</summary>
public class 批次数量与库存约束Tests
{
    [Fact]
    public void 超出部署上限()
    {
        // 变异验证：ValidateShape 的 `placements.Count > DeployLimit` 改成 `>=`→ 第 3 枚就被拒，本测试红 1；
        // 删掉该判断 → 本测试与「高部署上限无硬顶」红 2。
        GameBoard board = TestMaps.Blank(size: 7);
        var batch = new StagedBatch(board, BatchFixtures.Context(board, TestMaps.P0, limit: 3));
        Assert.Null(batch.Stage(TestMaps.At("A1"), PieceType.Basic));
        Assert.Null(batch.Stage(TestMaps.At("B2"), PieceType.Basic));
        Assert.Null(batch.Stage(TestMaps.At("C3"), PieceType.Basic));

        BatchFailure? failure = batch.Stage(TestMaps.At("D4"), PieceType.Basic);

        Assert.NotNull(failure);
        Assert.Equal(BatchFailureKind.DeployLimitExceeded, failure.Kind);
        Assert.Contains("已达部署上限", failure.Message);
        Assert.Contains("3", failure.Message);
        Assert.Equal(["D4"], failure.Coords.Notations());
        Assert.Equal(3, batch.Count);
    }

    [Fact]
    public void 超出手牌库存()
    {
        // 变异验证：ValidateShape 库存比较 `needed > stock` 改成 `needed > stock + 1` → 本测试红 1。
        GameBoard board = TestMaps.Blank(size: 7);
        IReadOnlyDictionary<PieceType, int> stock = BatchFixtures.Stock((PieceType.Line, 2), (PieceType.Basic, 5));
        var batch = new StagedBatch(board, BatchFixtures.Context(board, TestMaps.P0, limit: 5, stock: stock));
        Assert.Null(batch.Stage(TestMaps.At("A1"), PieceType.Line));
        Assert.Null(batch.Stage(TestMaps.At("B2"), PieceType.Line));

        BatchFailure? failure = batch.Stage(TestMaps.At("C3"), PieceType.Line);

        Assert.NotNull(failure);
        Assert.Equal(BatchFailureKind.InsufficientStock, failure.Kind);
        Assert.Equal(PieceType.Line, failure.StockType);
        Assert.Contains("库存不足", failure.Message);
        Assert.Contains("连珠子", failure.Message);
        Assert.Equal(["A1", "B2", "C3"], failure.Coords.Notations());
        Assert.Equal(2, batch.Count);

        // 库存缺失的类型视为 0
        Assert.Equal(BatchFailureKind.InsufficientStock, batch.Stage(TestMaps.At("C3"), PieceType.Fortress)!.Kind);
        // 其他类型仍可继续暂放
        Assert.Null(batch.Stage(TestMaps.At("C3"), PieceType.Basic));
    }

    [Fact]
    public void 高部署上限无硬顶()
    {
        // 设计文档 §8.1：军令信物把上限提高到 9，不因任何全局上限而拒绝。
        // 变异验证：在 ValidateShape 加 `Math.Min(context.DeployLimit, 3)` 硬顶 → 本测试红 1。
        GameBoard board = TestMaps.Blank(size: 7);
        var batch = new StagedBatch(board, BatchFixtures.Context(board, TestMaps.P0, limit: 9));

        for (int i = 0; i < 9; i++)
        {
            Assert.Null(batch.Stage(new Coord(i % 7, i / 7), PieceType.Basic));
        }

        Assert.Equal(9, batch.Count);
        Assert.Equal(BatchFailureKind.DeployLimitExceeded, batch.Stage(TestMaps.At("D3"), PieceType.Basic)!.Kind);
    }

    [Fact]
    public void 额度不跨回合()
    {
        // 上限 5 只落 2 枚；下一小回合的上限由该回合快照（3）决定，未用的 3 点不结转。
        // 这是契约测试：本层不持有跨回合状态，上限只来自每回合新建的 BatchContext（design.md 接口契约）；
        // 能让它红的变异是"StagedBatch 从静态字段累加上回合剩余额度"，没有对应的实现行可改，故不做变异记录。
        GameBoard board = TestMaps.Blank(size: 7);
        SettlementDriver driver = BatchFixtures.Driver(board);
        var turn1 = new StagedBatch(board, BatchFixtures.Context(board, TestMaps.P0, limit: 5));
        Assert.Null(turn1.Stage(TestMaps.At("A1"), PieceType.Basic));
        Assert.Null(turn1.Stage(TestMaps.At("G7"), PieceType.Basic));
        Assert.True(driver.Confirm(turn1).Confirmed);

        var turn2 = new StagedBatch(board, BatchFixtures.Context(board, TestMaps.P0, limit: 3));
        Assert.Null(turn2.Stage(TestMaps.At("B2"), PieceType.Basic));
        Assert.Null(turn2.Stage(TestMaps.At("C2"), PieceType.Basic));
        Assert.Null(turn2.Stage(TestMaps.At("D2"), PieceType.Basic));

        BatchFailure? failure = turn2.Stage(TestMaps.At("E2"), PieceType.Basic);
        Assert.NotNull(failure);
        Assert.Equal(BatchFailureKind.DeployLimitExceeded, failure.Kind);
        Assert.Contains("最多 3 枚", failure.Message);
    }

    [Fact]
    public void 绕过暂放直接提交超量批次仍被拒绝()
    {
        // 确认路径复用同一份第 1–2 步校验（implement.md 2.2），不信任 UI 层已经拦过。
        // 变异验证：Rehearse 里删掉 ValidateShape 调用 → 本测试红 1。
        GameBoard board = TestMaps.Blank(size: 7);
        SettlementDriver driver = BatchFixtures.Driver(board);
        string before = board.Serialize();

        SettlementOutcome overLimit = driver.Confirm(
            BatchFixtures.Context(board, TestMaps.P0, limit: 3),
            [BatchFixtures.P("A1"), BatchFixtures.P("B1"), BatchFixtures.P("C1"), BatchFixtures.P("D1")]);
        Assert.False(overLimit.Confirmed);
        Assert.Equal(BatchFailureKind.DeployLimitExceeded, overLimit.Failure!.Kind);

        SettlementOutcome overStock = driver.Confirm(
            BatchFixtures.Context(board, TestMaps.P0, limit: 3, stock: BatchFixtures.Stock((PieceType.Fortress, 1))),
            [BatchFixtures.P("A1", PieceType.Fortress), BatchFixtures.P("B1", PieceType.Fortress)]);
        Assert.False(overStock.Confirmed);
        Assert.Equal(BatchFailureKind.InsufficientStock, overStock.Failure!.Kind);

        Assert.Equal(before, board.Serialize());
        Assert.Equal(0, driver.History.Count);
    }
}
