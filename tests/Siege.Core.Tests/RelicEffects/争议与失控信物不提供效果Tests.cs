using Siege.Core.Board;
using Siege.Core.Relics;

namespace Siege.Core.Tests.RelicEffectsSpec;

/// <summary>规格：relic-effects —— Requirement: 争议与失控信物不提供效果</summary>
public class 争议与失控信物不提供效果Tests
{
    [Fact]
    public void 争议信物不生效()
    {
        // 探勘 E7 同时被 P0（D7）与 P1（F7）覆盖 → 两人快照展示数均为 5；P0 另控一枚探勘 B2 → P0 为 6 而非 7。
        // 变异验证 M-C8（争议给编号最小者）→ 本测试红。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Prospecting()), ("B2", RelicFixtures.Prospecting()));
        board.Place("D7", TestMaps.P0).Place("F7", TestMaps.P1).Place("B2", TestMaps.P0);
        ledger.Settle(board, 1);

        Assert.Equal(6, ledger.SnapshotFor(TestMaps.P0, board, 0, 1).RevealCount);
        Assert.Equal(5, ledger.SnapshotFor(TestMaps.P1, board, 0, 1).RevealCount);
    }

    [Fact]
    public void 效果不残留()
    {
        // 连续三个大回合：控制 → 失去 → 夺回同一枚军令 E7 → 部署上限依次为 4 / 3 / 4。
        // 变异验证 M-C6（保留旧控制者）→ 本测试红（第二轮仍为 4）。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Command()));
        var limits = new List<int>();

        board.Place("E6", TestMaps.P0);
        ledger.Settle(board, 1);
        limits.Add(ledger.SnapshotFor(TestMaps.P0, board, 0, 1).DeployLimit);

        board.Place("E8", TestMaps.P1);
        ledger.Settle(board, 2);
        limits.Add(ledger.SnapshotFor(TestMaps.P0, board, 0, 2).DeployLimit);

        board.Clear(TestMaps.At("E8"));
        ledger.Settle(board, 3);
        limits.Add(ledger.SnapshotFor(TestMaps.P0, board, 0, 3).DeployLimit);

        Assert.Equal([4, 3, 4], limits);
        Assert.Equal(new DeployLimitPeak(4, 1, TestMaps.P0), ledger.DeployLimitPeak);
    }
}
