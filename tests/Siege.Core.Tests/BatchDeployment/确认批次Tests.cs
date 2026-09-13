using Siege.Core.Batch;
using Siege.Core.Board;

namespace Siege.Core.Tests.BatchDeployment;

/// <summary>规格：batch-deployment —— Requirement: 确认批次</summary>
public class 确认批次Tests
{
    [Fact]
    public void 非法批次保留暂放()
    {
        // 变异验证：SettlementDriver.Confirm(StagedBatch) 在拒绝分支也调用 batch.Clear() → 本测试红 1。
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("B4", TestMaps.P1).Place("C3", TestMaps.P1).Place("C5", TestMaps.P1).Place("D4", TestMaps.P1);
        string before = board.Serialize();
        var hooks = new RecordingHooks();
        SettlementDriver driver = BatchFixtures.Driver(board, hooks);
        var batch = new StagedBatch(board, BatchFixtures.Context(board, TestMaps.P0));
        Assert.Null(batch.Stage(TestMaps.At("C4"), PieceType.Basic));
        Assert.Null(batch.Stage(TestMaps.At("A1"), PieceType.Line));

        SettlementOutcome outcome = driver.Confirm(batch);

        Assert.False(outcome.Confirmed);
        Assert.Equal(BatchFailureKind.Suicide, outcome.Failure!.Kind);
        Assert.Equal(["C4"], outcome.Failure.Coords.Notations());
        // 全部暂放保留，可继续调整
        Assert.Equal(2, batch.Count);
        Assert.Equal([BatchFixtures.P("C4"), BatchFixtures.P("A1", PieceType.Line)], batch.Placements);
        Assert.Equal(before, board.Serialize());
        Assert.Empty(hooks.Calls);
        Assert.Equal(0, driver.History.Count);

        // 调整后即可确认
        Assert.True(batch.Unstage(TestMaps.At("C4")));
        Assert.True(driver.Confirm(batch).Confirmed);
        Assert.Equal(0, batch.Count);
    }

    [Fact]
    public void 确认的原子性()
    {
        // 5 枚合法批次：任何回调点看到的盘面要么是结算前，要么是 5 枚全部落下的结算后。
        // 变异验证：Confirm 在第 2 步循环里每放一枚就调用一次 OnRevealRelics → 本测试红 1。
        GameBoard board = TestMaps.Blank(size: 7).Place("A7", TestMaps.P1);
        var hooks = new RecordingHooks { Board = board };
        SettlementDriver driver = BatchFixtures.Driver(board, hooks);
        string before = board.Serialize();
        Placement[] five =
        [
            BatchFixtures.P("B2"), BatchFixtures.P("C3", PieceType.Fortress), BatchFixtures.P("D4", PieceType.Line),
            BatchFixtures.P("E5", PieceType.Multiplier), BatchFixtures.P("F6", PieceType.Synergy),
        ];

        SettlementOutcome outcome = driver.Confirm(BatchFixtures.Context(board, TestMaps.P0, limit: 5), five);

        Assert.True(outcome.Confirmed);
        string after = board.Serialize();
        Assert.NotEqual(before, after);
        foreach (Placement p in five)
        {
            Assert.Equal(new Occupant(TestMaps.P0, p.Type), board[p.Coord].Occupant);
        }

        Assert.NotEmpty(hooks.Calls);
        foreach ((string step, string snapshot) in hooks.Calls)
        {
            Assert.True(snapshot == before || snapshot == after, $"{step} 观察到了部分落子的中间盘面：{snapshot}");
        }
    }

    [Fact]
    public void 确认时重跑完整预演不信任缓存()
    {
        // 裁决记录 1：确认时必须重跑完整预演。这里在预演与确认之间篡改盘面与预演结果，确认必须以重跑结果为准。
        // 变异验证：Confirm 接受调用方传入的 RehearsalResult 并跳过 Rehearse → 本测试红 1。
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("B4", TestMaps.P1).Place("C3", TestMaps.P1).Place("C5", TestMaps.P1);
        SettlementDriver driver = BatchFixtures.Driver(board);
        BatchContext context = BatchFixtures.Context(board, TestMaps.P0);
        Placement[] placements = [BatchFixtures.P("C4")];

        RehearsalResult cached = driver.Rehearse(context, placements);
        Assert.True(cached.IsLegal);

        // 篡改 1：预演副本被外部写坏——正式盘面不受影响
        cached.ProjectedBoard!.Place(TestMaps.At("G7"), TestMaps.P1, PieceType.Fortress);
        // 篡改 2：预演之后盘面变了，C4 现在是自杀手
        board.Place(TestMaps.At("D4"), TestMaps.P1, PieceType.Basic);
        string before = board.Serialize();

        SettlementOutcome outcome = driver.Confirm(context, placements);

        Assert.False(outcome.Confirmed);
        Assert.Equal(BatchFailureKind.Suicide, outcome.Failure!.Kind);
        Assert.Equal(before, board.Serialize());
        Assert.Null(board[TestMaps.At("G7")].Occupant);
    }
}
