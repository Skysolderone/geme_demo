using Siege.Core.Batch;
using Siege.Core.Board;

namespace Siege.Core.Tests.CaptureResolution;

/// <summary>规格：capture-resolution —— Requirement: 盘面同形禁则</summary>
public class 盘面同形禁则Tests
{
    [Fact]
    public void 循环提子被禁止()
    {
        // 变异验证：Rehearse 删掉第 6 步 → 本测试红 1（「非盘面差异不豁免同形」「同形返回历史序号」等连带红）。
        GameBoard board = BatchFixtures.KoBoard(holderA: TestMaps.P1, holderB: TestMaps.P0);
        SettlementDriver driver = BatchFixtures.Driver(board);

        Assert.True(BatchFixtures.TakeKo(driver, TestMaps.P0, 'A', PieceType.Basic).Confirmed);   // S1
        // 提回去得到的是初始盘面：初始盘面不是提交，不入集合
        Assert.True(BatchFixtures.TakeKo(driver, TestMaps.P1, 'A', PieceType.Basic).Confirmed);   // S2
        string s2 = board.Serialize();

        SettlementOutcome outcome = BatchFixtures.TakeKo(driver, TestMaps.P0, 'A', PieceType.Basic);

        Assert.False(outcome.Confirmed);
        Assert.Equal(BatchFailureKind.Superko, outcome.Failure!.Kind);
        Assert.Equal(1, outcome.Failure.DuplicateOfSequence);
        Assert.Equal(s2, board.Serialize());
        Assert.Equal(2, driver.History.Count);
    }

    [Fact]
    public void 换类型不构成同形()
    {
        // 变异验证：BoardHistory.FindDuplicate 比对前把盘面串里的类型码统一替换成 'B' → 本测试红 1。
        GameBoard board = BatchFixtures.KoBoard(holderA: TestMaps.P1, holderB: TestMaps.P0);
        SettlementDriver driver = BatchFixtures.Driver(board);
        Assert.True(BatchFixtures.TakeKo(driver, TestMaps.P0, 'A', PieceType.Basic).Confirmed);
        Assert.True(BatchFixtures.TakeKo(driver, TestMaps.P1, 'A', PieceType.Basic).Confirmed);
        Assert.False(BatchFixtures.TakeKo(driver, TestMaps.P0, 'A', PieceType.Basic).Confirmed);

        SettlementOutcome outcome = BatchFixtures.TakeKo(driver, TestMaps.P0, 'A', PieceType.Fortress);

        Assert.True(outcome.Confirmed, outcome.Failure?.Message);
        Assert.Equal(3, outcome.CaptureRecord!.Sequence);
        Assert.Equal(new Occupant(TestMaps.P0, PieceType.Fortress), board[TestMaps.At("D4")].Occupant);
    }

    [Fact]
    public void 非盘面差异不豁免同形()
    {
        // 逐格一致，但当前玩家的手牌、部署上限与合法范围都完全不同 → 仍同形。
        // 变异验证：BoardHistory.Record 把 context 信息拼进盘面串（或比对时附带库存）→ 本测试红 1。
        GameBoard board = BatchFixtures.KoBoard(holderA: TestMaps.P1, holderB: TestMaps.P0);
        SettlementDriver driver = BatchFixtures.Driver(board);
        Assert.True(driver.Confirm(
            BatchFixtures.Context(board, TestMaps.P0, limit: 3, stock: BatchFixtures.Stock((PieceType.Basic, 1))),
            [BatchFixtures.P("D4")]).Confirmed);
        Assert.True(BatchFixtures.TakeKo(driver, TestMaps.P1, 'A', PieceType.Basic).Confirmed);

        BatchContext different = BatchFixtures.Context(
            board, TestMaps.P0, limit: 9,
            stock: BatchFixtures.Stock((PieceType.Basic, 7), (PieceType.Fortress, 2), (PieceType.Synergy, 1)),
            range: new HashSet<Coord> { TestMaps.At("D4"), TestMaps.At("A1") });
        SettlementOutcome outcome = driver.Confirm(different, [BatchFixtures.P("D4")]);

        Assert.False(outcome.Confirmed);
        Assert.Equal(BatchFailureKind.Superko, outcome.Failure!.Kind);
        Assert.Equal(1, outcome.Failure.DuplicateOfSequence);
    }

    [Fact]
    public void 中间态不入集合()
    {
        // 暂放过程中经过与 S1 相同的预览态（预演判同形），改类型后确认 → 合法，且集合只新增最终盘面。
        // 变异验证：Rehearse 在第 6 步之后 history.Record(projected.Serialize()) → 本测试红 1。
        GameBoard board = BatchFixtures.KoBoard(holderA: TestMaps.P1, holderB: TestMaps.P0);
        SettlementDriver driver = BatchFixtures.Driver(board);
        Assert.True(BatchFixtures.TakeKo(driver, TestMaps.P0, 'A', PieceType.Basic).Confirmed);
        string s1 = board.Serialize();
        Assert.True(BatchFixtures.TakeKo(driver, TestMaps.P1, 'A', PieceType.Basic).Confirmed);

        var batch = new StagedBatch(board, BatchFixtures.Context(board, TestMaps.P0));
        Assert.Null(batch.Stage(TestMaps.At("D4"), PieceType.Basic));
        RehearsalResult preview = driver.Rehearse(batch.Context, batch.Placements);
        Assert.Equal(BatchFailureKind.Superko, preview.Failure!.Kind);
        Assert.Equal(s1, preview.ProjectedBoard!.Serialize());
        Assert.Equal(2, driver.History.Count);

        Assert.Null(batch.Replace(TestMaps.At("D4"), PieceType.Line));
        SettlementOutcome outcome = driver.Confirm(batch);

        Assert.True(outcome.Confirmed, outcome.Failure?.Message);
        Assert.Equal(3, driver.History.Count);
        Assert.Equal(board.Serialize(), driver.History.Entries[2].Board);
        Assert.Equal(1, driver.History.Entries.Count(e => e.Board == s1));
    }

    [Fact]
    public void 哈希碰撞不误判()
    {
        // implement.md 3.1：哈希只做预筛，命中后必须按内容再比对。用恒定哈希让所有条目碰撞。
        // 变异验证：FindDuplicate 命中哈希桶后直接返回第一条的序号 → 本测试红 1。
        var colliding = new BoardHistory(_ => 42);
        Assert.Equal(1, colliding.Record("00/--"));
        Assert.Equal(2, colliding.Record("0B/--"));
        Assert.Equal(3, colliding.Record("--/0B"));

        Assert.Null(colliding.FindDuplicate("0F/--"));
        Assert.Equal(2, colliding.FindDuplicate("0B/--"));
        Assert.Equal(3, colliding.FindDuplicate("--/0B"));

        // 走完整预演同样不误判：碰撞哈希下，只有真正相同的盘面才触发同形
        GameBoard board = BatchFixtures.KoBoard(holderA: TestMaps.P1, holderB: TestMaps.P0);
        SettlementDriver driver = new(board, new BoardHistory(_ => 0), new RecordingHooks());
        Assert.True(BatchFixtures.TakeKo(driver, TestMaps.P0, 'A', PieceType.Basic).Confirmed);
        Assert.True(BatchFixtures.TakeKo(driver, TestMaps.P1, 'A', PieceType.Basic).Confirmed);
        Assert.True(BatchFixtures.TakeKo(driver, TestMaps.P0, 'A', PieceType.Fortress).Confirmed);
        Assert.Equal(BatchFailureKind.Superko, BatchFixtures.TakeKo(driver, TestMaps.P1, 'A', PieceType.Basic).Failure?.Kind);
    }
}
