using Siege.Core.Batch;
using Siege.Core.Board;

namespace Siege.Core.Tests.BatchDeployment;

/// <summary>规格：batch-deployment —— Requirement: 非法批次必须给出可定位的原因</summary>
public class 非法批次必须给出可定位的原因Tests
{
    [Fact]
    public void 自杀手返回可高亮信息()
    {
        // P1 围出 C4/D4/E4 三格口袋，P0 已有 E4（仅剩 D4 一口气）。批次落 D4、C4 后三子合成一条无气棋串：
        // 返回的坐标必须是整条棋串（C4、D4、E4）——既不是只有最后一枚，也不是只有本批落点（E4 不是落点）。
        // 变异验证：Suicide 的 Coords 只放第一枚落点 → 本测试红 1；
        // trellis-check 复核：Coords 只放本批落点（去掉既有子）→ 旧用例（死串 = 落点）抓不住，改成本用例后红 1。
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("B4", TestMaps.P1).Place("C3", TestMaps.P1).Place("C5", TestMaps.P1)
            .Place("D3", TestMaps.P1).Place("D5", TestMaps.P1)
            .Place("E3", TestMaps.P1).Place("E5", TestMaps.P1).Place("F4", TestMaps.P1)
            .Place("E4", TestMaps.P0);
        Assert.Equal(["D4"], board.LibertiesOf(board.GroupAt(TestMaps.At("E4"))!).Notations());
        SettlementDriver driver = BatchFixtures.Driver(board);

        SettlementOutcome outcome = driver.Confirm(
            BatchFixtures.Context(board, TestMaps.P0), [BatchFixtures.P("D4"), BatchFixtures.P("C4")]);

        Assert.False(outcome.Confirmed);
        BatchFailure failure = outcome.Failure!;
        Assert.Equal(BatchFailureKind.Suicide, failure.Kind);
        Assert.Equal(["C4", "D4", "E4"], failure.Coords.Notations());
        Assert.Contains("自杀手", failure.Message);
        Assert.Null(failure.DuplicateOfSequence);
    }

    [Fact]
    public void 同形返回历史序号()
    {
        // 设计文档 §6.2：结算后盘面与第 12 次提交相同 → 拒绝并返回序号 12。
        // 变异验证：Superko 的 DuplicateOfSequence 固定填 History.Count → 本测试红 1（期望 12，得到 13）。
        GameBoard board = BatchFixtures.KoBoard(holderA: TestMaps.P1, holderB: TestMaps.P1);
        SettlementDriver driver = BatchFixtures.Driver(board);
        KoScript.Play(driver, upTo: 12);
        Assert.True(BatchFixtures.TakeKo(driver, TestMaps.P0, 'A', PieceType.Fortress).Confirmed);
        Assert.Equal(13, driver.History.Count);

        SettlementOutcome outcome = BatchFixtures.TakeKo(driver, TestMaps.P1, 'A', PieceType.Basic);

        Assert.False(outcome.Confirmed);
        BatchFailure failure = outcome.Failure!;
        Assert.Equal(BatchFailureKind.Superko, failure.Kind);
        Assert.Equal(12, failure.DuplicateOfSequence);
        Assert.Contains("第 12 次提交", failure.Message);
        Assert.Equal(["C4"], failure.Coords.Notations());
    }

    [Fact]
    public void 失败类别_落点不可落子()
    {
        // 变异验证（trellis-check）：ValidateShape 删掉 `cell.Terrain == Terrain.Obstacle` 判断 → 本测试红 1。
        GameBoard board = TestMaps.Blank(size: 7, "C3");
        BatchContext context = BatchFixtures.Context(board, TestMaps.P0);

        BatchFailure? obstacle = BatchRehearsal.ValidateShape(board, context, [BatchFixtures.P("C3")]);
        Assert.Equal(BatchFailureKind.Unplayable, obstacle!.Kind);
        Assert.Equal(["C3"], obstacle.Coords.Notations());
        Assert.Contains("不可落子", obstacle.Message);

        // 越界方向与障碍格封堵语义相同
        BatchFailure? outside = BatchRehearsal.ValidateShape(board, context, [new Placement(new Coord(7, 0), PieceType.Basic)]);
        Assert.Equal(BatchFailureKind.Unplayable, outside!.Kind);
    }

    [Fact]
    public void 失败类别_落点已被占据()
    {
        GameBoard board = TestMaps.Blank(size: 7).Place("D4", TestMaps.P1);
        BatchFailure? failure = BatchRehearsal.ValidateShape(board, BatchFixtures.Context(board, TestMaps.P0), [BatchFixtures.P("D4")]);
        Assert.Equal(BatchFailureKind.Occupied, failure!.Kind);
        Assert.Equal(["D4"], failure.Coords.Notations());

        // 己方棋子占据同样属于"已被占据"
        board.Place(TestMaps.At("E4"), TestMaps.P0, PieceType.Basic);
        Assert.Equal(BatchFailureKind.Occupied,
            BatchRehearsal.ValidateShape(board, BatchFixtures.Context(board, TestMaps.P0), [BatchFixtures.P("E4")])!.Kind);
    }

    [Fact]
    public void 失败类别_违反合法落子范围()
    {
        // 保护期：合法范围锁定为出生区（这里用 A1–B2 四格代替）
        // 变异验证：ValidateShape 删掉 LegalRange 判断 → 本测试红 1。
        GameBoard board = TestMaps.Blank(size: 7);
        var birthZone = new HashSet<Coord> { TestMaps.At("A1"), TestMaps.At("A2"), TestMaps.At("B1"), TestMaps.At("B2") };
        BatchContext context = BatchFixtures.Context(board, TestMaps.P0, range: birthZone);

        Assert.Null(BatchRehearsal.ValidateShape(board, context, [BatchFixtures.P("B2")]));
        BatchFailure? failure = BatchRehearsal.ValidateShape(board, context, [BatchFixtures.P("B2"), BatchFixtures.P("C3")]);
        Assert.Equal(BatchFailureKind.OutOfLegalRange, failure!.Kind);
        Assert.Equal(["C3"], failure.Coords.Notations());
        Assert.Contains("合法落子范围", failure.Message);
    }

    [Fact]
    public void 失败类别_超出部署上限()
    {
        // 变异验证（trellis-check）：DeployLimitExceeded 的 Coords 改成全部落点（不 Skip(limit)）→ 本测试与「超出部署上限」红 2。
        GameBoard board = TestMaps.Blank(size: 7);
        BatchFailure? failure = BatchRehearsal.ValidateShape(
            board, BatchFixtures.Context(board, TestMaps.P0, limit: 2),
            [BatchFixtures.P("A1"), BatchFixtures.P("B1"), BatchFixtures.P("C1"), BatchFixtures.P("D1")]);
        Assert.Equal(BatchFailureKind.DeployLimitExceeded, failure!.Kind);
        // 高亮超出上限的那几枚
        Assert.Equal(["C1", "D1"], failure.Coords.Notations());
    }

    [Fact]
    public void 失败类别_手牌库存不足()
    {
        // 变异验证（trellis-check）：InsufficientStock 的 Coords 只放该类型第一枚 → 本测试与「超出手牌库存」红 2。
        GameBoard board = TestMaps.Blank(size: 7);
        BatchFailure? failure = BatchRehearsal.ValidateShape(
            board, BatchFixtures.Context(board, TestMaps.P0, stock: BatchFixtures.Stock((PieceType.Basic, 1))),
            [BatchFixtures.P("A1"), BatchFixtures.P("B1")]);
        Assert.Equal(BatchFailureKind.InsufficientStock, failure!.Kind);
        Assert.Equal(PieceType.Basic, failure.StockType);
        Assert.Equal(["A1", "B1"], failure.Coords.Notations());
    }

    [Fact]
    public void 失败类别_批次内重复落点()
    {
        // 规格要求的七类之外的第八类：同一批次两枚棋子指向同一格。不能并入"已被占据"——那条文案专指盘面占据。
        // 变异验证（trellis-check）：ValidateShape 删掉 `seen.Add` 判断 → 本测试红 1。
        GameBoard board = TestMaps.Blank(size: 7);
        BatchFailure? failure = BatchRehearsal.ValidateShape(
            board, BatchFixtures.Context(board, TestMaps.P0), [BatchFixtures.P("A1"), BatchFixtures.P("A1", PieceType.Line)]);
        Assert.Equal(BatchFailureKind.DuplicateInBatch, failure!.Kind);
        Assert.Equal(["A1"], failure.Coords.Notations());
    }

    [Fact]
    public void 七类失败各有独立类别()
    {
        // 枚举本身必须至少包含规格列出的七类，且互不相同
        BatchFailureKind[] required =
        [
            BatchFailureKind.Unplayable, BatchFailureKind.Occupied, BatchFailureKind.OutOfLegalRange,
            BatchFailureKind.DeployLimitExceeded, BatchFailureKind.InsufficientStock,
            BatchFailureKind.Suicide, BatchFailureKind.Superko,
        ];
        Assert.Equal(7, required.Distinct().Count());
        Assert.All(required, kind => Assert.True(Enum.IsDefined(kind)));
    }

    [Fact]
    public void 预占腾空格的拒绝文案为该格当前已被占据()
    {
        // implement.md 6.3：文案级断言。UI 必须把拒绝原因说成"该格当前已被占据"，而不是含糊的"非法落点"。
        // 变异验证：Occupied 的文案改成"非法落点" → 本测试红 1。
        GameBoard board = TestMaps.Blank()
            .Place("G7", TestMaps.P1)
            .Place("F7", TestMaps.P0).Place("G6", TestMaps.P0).Place("G8", TestMaps.P0);
        var batch = new StagedBatch(board, BatchFixtures.Context(board, TestMaps.P0));
        Assert.Null(batch.Stage(TestMaps.At("H7"), PieceType.Basic));

        BatchFailure? failure = batch.Stage(TestMaps.At("G7"), PieceType.Basic);

        Assert.Equal("该格当前已被占据：G7。", failure!.Message);
        Assert.DoesNotContain("非法落点", failure.Message);
        Assert.DoesNotContain("不可落子", failure.Message);
    }
}
