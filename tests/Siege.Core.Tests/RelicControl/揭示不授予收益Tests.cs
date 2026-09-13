using Siege.Core.Board;
using Siege.Core.Relics;

namespace Siege.Core.Tests.RelicControlSpec;

/// <summary>规格：relic-control —— Requirement: 揭示不授予收益</summary>
public class 揭示不授予收益Tests
{
    [Fact]
    public void 揭示者未获收益()
    {
        // P0（E6）与 P1（E8）同时首次覆盖军令 E7：内容被揭示，但 E7 争议、无人取得控制 → P0 的部署上限仍为 3。
        // 再让 P0 单独揭示另一枚军令 C3（P0 在 C2），随后 P1 落 C4 形成争议——揭示者 P0 同样不得收益。
        // 变异验证 M-C5：BuildSnapshot 改为「已揭示即计入」（`relic.IsRevealed` 代替 GrantsEffectTo）→ 红 6，含本测试。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Command()), ("C3", RelicFixtures.Command(2)));
        board.Place("E6", TestMaps.P0).Place("E8", TestMaps.P1);
        ledger.Settle(board, 1);
        Assert.True(ledger.IsRevealed(TestMaps.At("E7")));
        Assert.Equal(3, ledger.SnapshotFor(TestMaps.P0, board, 0, 1).DeployLimit);

        board.Place("C2", TestMaps.P0);
        ledger.Settle(board, 1);
        Assert.True(ledger.IsRevealed(TestMaps.At("C3")));
        Assert.Equal(5, ledger.SnapshotFor(TestMaps.P0, board, 0, 1).DeployLimit);

        board.Place("C4", TestMaps.P1);
        ledger.Settle(board, 2);
        Assert.Equal(RelicControl.Contested, ledger.ControlOf(TestMaps.At("C3")));
        Assert.Equal(3, ledger.SnapshotFor(TestMaps.P0, board, 0, 2).DeployLimit);
        Assert.Equal(3, ledger.SnapshotFor(TestMaps.P1, board, 0, 2).DeployLimit);
    }

    [Fact]
    public void 失去控制即失效()
    {
        // P0 在第 5 大回合控制兵站 E7，第 6 大回合失去控制（棋子被清、无人覆盖）→ 第 6 大回合快照的手牌类型槽回到 5。
        // 变异验证 M-C6：RecalculateCore 只在 Control 为 Uncontrolled 时才写入（保留旧控制者）→ 红 4，含本测试。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Depot()));
        board.Place("E6", TestMaps.P0);
        ledger.Settle(board, 5);
        Assert.Equal(6, ledger.SnapshotFor(TestMaps.P0, board, 0, 5).TypeSlots);

        board.Clear(TestMaps.At("E6"));
        ledger.Settle(board, 6);

        Assert.Equal(5, ledger.SnapshotFor(TestMaps.P0, board, 0, 6).TypeSlots);
        Assert.True(ledger.IsRevealed(TestMaps.At("E7")));
    }
}
