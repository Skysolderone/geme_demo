using System.Collections.Immutable;
using Siege.Core.Batch;
using Siege.Core.Board;

namespace Siege.Core.Tests.CaptureResolution;

/// <summary>规格：capture-resolution —— Requirement: 正式结算顺序</summary>
public class 正式结算顺序Tests
{
    private static readonly string[] ExpectedSteps = ["DeductHand", "OnRevealRelics", "OnRecalculatePower", "OnCheckEndConditions"];

    [Fact]
    public void 提子先于信物揭示()
    {
        // E7 是信物格；P1 的 E5 原本是唯一贴近它的棋子。批次落 E6 既提走 E5 又让己方进入 E7 的邻域。
        // 第 4 步回调看到的盘面必须已经完成提子（E5 空、E6 为 P0）。
        // 变异验证：Confirm 把 OnRevealRelics 调用移到 RemoveStones 之前 → 本测试红 1（「结算的原子性」同时红）。
        MapData map = TestMaps.Synthetic(
            size: 9, maxPlayers: 4,
            relics: [KeyValuePair.Create(TestMaps.At("E7"), new RelicCellSpec(RelicZone.Contested, BudgetTier.High))]);
        GameBoard board = GameBoard.LoadUnvalidated(map)
            .Place("E5", TestMaps.P1)
            .Place("D5", TestMaps.P0).Place("F5", TestMaps.P0).Place("E4", TestMaps.P0);
        Assert.True(board[TestMaps.At("E7")].IsRelicCell);
        var hooks = new RecordingHooks { Board = board };
        SettlementDriver driver = BatchFixtures.Driver(board, hooks);

        SettlementOutcome outcome = driver.Confirm(BatchFixtures.Context(board, TestMaps.P0), [BatchFixtures.P("E6")]);

        Assert.True(outcome.Confirmed);
        (string step, string snapshot) = hooks.Calls[1];
        Assert.Equal("OnRevealRelics", step);
        Assert.Equal(board.Serialize(), snapshot);
        SettlementContext reveal = hooks.Contexts[0];
        Assert.Null(reveal.Board[TestMaps.At("E5")].Occupant);
        Assert.Equal(new Occupant(TestMaps.P0, PieceType.Basic), reveal.Board[TestMaps.At("E6")].Occupant);
        Assert.Equal([new CapturedStone(TestMaps.At("E5"), TestMaps.P1, PieceType.Basic)], reveal.Captures);
    }

    [Fact]
    public void 势力重算后才检查终局()
    {
        // 一次结算把 P1 的棋子全部清空：第 5 步先完成重算，第 6 步才做出局与终局检查。
        // 变异验证：Confirm 交换 OnRecalculatePower 与 OnCheckEndConditions → 本测试红 1（「结算驱动器步骤顺序」同时红）。
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("D4", TestMaps.P1)
            .Place("C4", TestMaps.P0).Place("E4", TestMaps.P0).Place("D3", TestMaps.P0);
        var hooks = new RecordingHooks { Board = board };
        SettlementDriver driver = BatchFixtures.Driver(board, hooks);

        Assert.True(driver.Confirm(BatchFixtures.Context(board, TestMaps.P0), [BatchFixtures.P("D5")]).Confirmed);

        Assert.Empty(board.GroupsOf(TestMaps.P1));
        int recalc = Array.IndexOf(hooks.Steps, "OnRecalculatePower");
        int end = Array.IndexOf(hooks.Steps, "OnCheckEndConditions");
        Assert.True(recalc >= 0 && end > recalc, string.Join(",", hooks.Steps));
        // 终局检查看到的就是重算时的那个盘面
        Assert.Equal(hooks.Calls[recalc].Board, hooks.Calls[end].Board);
        Assert.Same(hooks.Contexts[recalc - 1], hooks.Contexts[end - 1]);
    }

    [Fact]
    public void 结算的原子性()
    {
        // 每个回调点采样正式盘面：只允许"结算前"与"结算后"两种状态；
        // 绝不出现"棋子已落下但敌串尚未移除"或"敌串已移除但势力未重算（终局检查早于重算）"。
        // 变异验证：Confirm 在 Place 循环之后、RemoveStones 之前插入 OnRecalculatePower 调用 → 本测试红 1。
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("C3", TestMaps.P1).Place("D3", BatchFixtures.P2)
            .Place("C2", TestMaps.P0).Place("C4", TestMaps.P0).Place("D2", TestMaps.P0).Place("D4", TestMaps.P0);
        var hooks = new RecordingHooks { Board = board };
        SettlementDriver driver = BatchFixtures.Driver(board, hooks);
        string before = board.Serialize();

        Assert.True(driver.Confirm(
            BatchFixtures.Context(board, TestMaps.P0), [BatchFixtures.P("B3"), BatchFixtures.P("E3")]).Confirmed);

        string after = board.Serialize();
        Assert.NotEqual(before, after);
        Assert.Equal(ExpectedSteps, hooks.Steps);
        Assert.Equal(before, hooks.Calls[0].Board);
        foreach ((string step, string snapshot) in hooks.Calls.Skip(1))
        {
            Assert.True(snapshot == after, $"{step} 观察到中间盘面：{snapshot}");
        }

        // 中间态的具体形状：落下 B3/E3 但 C3/D3 未移除——它不曾出现在任何采样中
        GameBoard intermediate = board.Clone();
        intermediate.Place(TestMaps.At("C3"), TestMaps.P1, PieceType.Basic);
        intermediate.Place(TestMaps.At("D3"), BatchFixtures.P2, PieceType.Basic);
        Assert.DoesNotContain(intermediate.Serialize(), hooks.Calls.Select(c => c.Board));
    }

    [Fact]
    public void 结算驱动器步骤顺序()
    {
        // implement.md 4.1 / 4.2：四个回调各恰好一次，顺序 1 → 4 → 5 → 6；调换任意两步即失败。
        // 变异验证：分别交换 (DeductHand, OnRevealRelics)、(OnRevealRelics, OnRecalculatePower)、
        // (OnRecalculatePower, OnCheckEndConditions) → 各红 1；删掉任一回调 → 红 1。
        GameBoard board = TestMaps.Blank(size: 7).Place("D4", TestMaps.P1);
        var hooks = new RecordingHooks { Board = board };
        SettlementDriver driver = BatchFixtures.Driver(board, hooks);
        string before = board.Serialize();

        SettlementOutcome outcome = driver.Confirm(
            BatchFixtures.Context(board, TestMaps.P0), [BatchFixtures.P("A1", PieceType.Fortress), BatchFixtures.P("B1")]);

        Assert.True(outcome.Confirmed);
        Assert.Equal(ExpectedSteps, hooks.Steps);
        // 第 1 步在盘面变更之前：扣手牌请求发出时盘面仍是结算前
        Assert.Equal(before, hooks.Calls[0].Board);
        Assert.Equal(new Dictionary<PieceType, int> { [PieceType.Basic] = 1, [PieceType.Fortress] = 1 }, hooks.Deductions[0].Deployed);
        // 第 4–6 步共享同一份上下文
        Assert.Equal(3, hooks.Contexts.Count);
        Assert.All(hooks.Contexts, c => Assert.Same(hooks.Contexts[0], c));
        Assert.Equal(1, hooks.Contexts[0].Sequence);
        Assert.False(hooks.Contexts[0].IsPass);
        Assert.Same(board, hooks.Contexts[0].Board);
    }

    [Fact]
    public void 提子记录可重建被提棋串并保留落子顺序()
    {
        // implement.md 7.1：被提棋子坐标、所属玩家、类型齐全，足以重建棋串；批次内落子顺序原样保留（裁决记录 3）。
        // 变异验证：CaptureRecord.Placements 改成按坐标排序 → 本测试红 1；Captured 丢掉 Type → 本测试红 1。
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("D4", TestMaps.P1, PieceType.Fortress).Place("D5", TestMaps.P1, PieceType.Line)
            .Place("C4", TestMaps.P0).Place("C5", TestMaps.P0).Place("E4", TestMaps.P0).Place("E5", TestMaps.P0).Place("D3", TestMaps.P0);
        Group victim = board.GroupAt(TestMaps.At("D4"))!;
        ImmutableArray<Occupant?> victimStones = [.. victim.Stones.Select(s => board[s].Occupant)];
        SettlementDriver driver = BatchFixtures.Driver(board);

        // 刻意用非字典序的落子顺序：G7 先、D6 后
        SettlementOutcome outcome = driver.Confirm(
            BatchFixtures.Context(board, TestMaps.P0), [BatchFixtures.P("G7", PieceType.Synergy), BatchFixtures.P("D6")]);

        Assert.True(outcome.Confirmed);
        CaptureRecord record = outcome.CaptureRecord!;
        Assert.Equal(1, record.Sequence);
        Assert.Equal(TestMaps.P0, record.Capturer);
        Assert.Equal([BatchFixtures.P("G7", PieceType.Synergy), BatchFixtures.P("D6")], record.Placements);
        Assert.Equal(victim.Stones, record.Captured.Select(s => s.Coord));
        Assert.Equal(victimStones, record.Captured.Select(s => (Occupant?)new Occupant(s.Owner, s.Type)));
    }
}
