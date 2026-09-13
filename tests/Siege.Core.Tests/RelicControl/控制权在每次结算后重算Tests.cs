using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Relics;

namespace Siege.Core.Tests.RelicControlSpec;

/// <summary>规格：relic-control —— Requirement: 控制权在每次结算后重算</summary>
public class 控制权在每次结算后重算Tests
{
    [Fact]
    public void 围杀导致信物易主()
    {
        // P0 占据信物格 E7；P1 已围三面（D7、F7、E8），落 E6 提走 E7 的 P0 棋子。提子后 E7 为空、四邻全是 P1 → 唯一覆盖 → 控制者变为 P1。
        // 走真实结算驱动器，第 5 步回调重算。
        // 变异验证 M-C6（保留旧控制者）→ 本测试红。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Command()));
        board.Place("E7", TestMaps.P0).Place("D7", TestMaps.P1).Place("F7", TestMaps.P1).Place("E8", TestMaps.P1);
        ledger.Settle(board, 1);
        Assert.Equal(new RelicControl(RelicControlKind.Controlled, TestMaps.P0), ledger.ControlOf(TestMaps.At("E7")));
        var driver = new SettlementDriver(board, new BoardHistory(), new RelicHooks(ledger) { MajorRound = 2 });

        SettlementOutcome outcome = driver.Confirm(BatchFixtures.Context(board, TestMaps.P1), [BatchFixtures.P("E6")]);

        Assert.True(outcome.Confirmed);
        Assert.Null(board[TestMaps.At("E7")].Occupant);
        Assert.Equal(new RelicControl(RelicControlKind.Controlled, TestMaps.P1), ledger.ControlOf(TestMaps.At("E7")));
        Assert.Equal(4, ledger.SnapshotFor(TestMaps.P1, board, 0, 2).DeployLimit);
        Assert.Equal(3, ledger.SnapshotFor(TestMaps.P0, board, 0, 2).DeployLimit);
    }

    [Fact]
    public void 控制权不保留历史()
    {
        // 第 3 大回合 P0 控制 E7；此后棋子被清、第 4–7 大回合无人覆盖 → 第 7 大回合为无人控制，公开状态不残留 P0。
        // 变异验证 M-C6（保留旧控制者）→ 本测试红。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Prospecting()));
        board.Place("E6", TestMaps.P0);
        ledger.Settle(board, 3);
        Assert.Equal(new RelicControl(RelicControlKind.Controlled, TestMaps.P0), ledger.ControlOf(TestMaps.At("E7")));

        board.Clear(TestMaps.At("E6"));
        for (int round = 4; round <= 7; round++)
        {
            ledger.Settle(board, round);
        }

        RelicPublicState state = ledger.PublicStateOf(TestMaps.At("E7"));
        Assert.Equal(RelicControl.Uncontrolled, state.Control);
        Assert.Null(state.Control.Holder);
        Assert.True(state.IsRevealed);
        Assert.Equal(5, ledger.SnapshotFor(TestMaps.P0, board, 0, 7).RevealCount);
    }
}
