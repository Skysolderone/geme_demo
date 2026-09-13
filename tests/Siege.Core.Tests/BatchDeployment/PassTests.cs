using Siege.Core.Batch;
using Siege.Core.Board;

namespace Siege.Core.Tests.BatchDeployment;

/// <summary>规格：batch-deployment —— Requirement: Pass</summary>
public class PassTests
{
    [Fact]
    public void 确认0落子()
    {
        // Pass 仍要走第 5、6 步：power-score 规格「Pass 也触发更新」+ 设计文档 §12.1"每次合法批次结算或 Pass 完成后"检查出局。
        // 不走第 1 步（无手牌可扣）与第 4 步（盘面未变，不可能有信物格新进入覆盖）。
        // 变异验证：Confirm 的 Pass 分支删掉 OnCheckEndConditions 调用 → 本测试红 1；
        // 删掉 OnPass 调用 → 本测试红 1；Pass 分支也调用 History.Record → 本测试红 1（trellis-check 复核：红 1）；
        // 删掉 OnRecalculatePower → 本测试红 1；把 OnRecalculatePower 挪到 OnCheckEndConditions 之后 → 本测试红 1。
        GameBoard board = TestMaps.Blank(size: 7).Place("D4", TestMaps.P1);
        var hooks = new RecordingHooks { Board = board };
        SettlementDriver driver = BatchFixtures.Driver(board, hooks);
        string before = board.Serialize();
        var batch = new StagedBatch(board, BatchFixtures.Context(board, BatchFixtures.P2));

        SettlementOutcome outcome = driver.Confirm(batch);

        Assert.True(outcome.Confirmed);
        Assert.True(outcome.IsPass);
        Assert.Null(outcome.CaptureRecord);
        // Pass 事件携带当前玩家标识，且与正常结算共用同一个势力重算回调与终局检查回调，顺序 5 → 6
        Assert.Equal(["OnPass", "OnRecalculatePower", "OnCheckEndConditions"], hooks.Steps);
        Assert.Equal([BatchFixtures.P2], hooks.Passes);
        Assert.Equal(2, hooks.Contexts.Count);
        Assert.Same(hooks.Contexts[0], hooks.Contexts[1]);
        SettlementContext end = hooks.Contexts[1];
        Assert.True(end.IsPass);
        Assert.Equal(BatchFixtures.P2, end.Player);
        Assert.Null(end.Sequence);
        Assert.Empty(end.Placements);
        Assert.Empty(end.Captures);
        // 不扣手牌、不改盘面、不记入已提交盘面集合
        Assert.Empty(hooks.Deductions);
        Assert.Equal(before, board.Serialize());
        Assert.Equal(0, driver.History.Count);
    }

    [Fact]
    public void 落子1枚即保留征募()
    {
        // 设计文档 §5.5：本轮征募 3 枚只部署 1 枚，另外 2 枚留在手牌——本层只发出"扣 1 枚"的请求，绝不发 Pass 事件。
        // 变异验证：DeductHand 传整份库存而非实际部署数 → 本测试红 1。
        GameBoard board = TestMaps.Blank(size: 7);
        var hooks = new RecordingHooks { Board = board };
        SettlementDriver driver = BatchFixtures.Driver(board, hooks);
        IReadOnlyDictionary<PieceType, int> stock = BatchFixtures.Stock((PieceType.Basic, 3));

        SettlementOutcome outcome = driver.Confirm(
            BatchFixtures.Context(board, TestMaps.P0, stock: stock), [BatchFixtures.P("D4")]);

        Assert.True(outcome.Confirmed);
        Assert.False(outcome.IsPass);
        Assert.Empty(hooks.Passes);
        (PlayerId player, IReadOnlyDictionary<PieceType, int> deployed) = Assert.Single(hooks.Deductions);
        Assert.Equal(TestMaps.P0, player);
        Assert.Equal(new Dictionary<PieceType, int> { [PieceType.Basic] = 1 }, deployed);
        Assert.Equal(3, stock[PieceType.Basic]);
    }
}
