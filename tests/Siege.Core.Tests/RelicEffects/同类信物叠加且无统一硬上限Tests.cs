using Siege.Core.Board;
using Siege.Core.Relics;

namespace Siege.Core.Tests.RelicEffectsSpec;

/// <summary>规格：relic-effects —— Requirement: 同类信物叠加且无统一硬上限</summary>
public class 同类信物叠加且无统一硬上限Tests
{
    [Fact]
    public void 军令叠加()
    {
        // 2 枚普通军令 + 1 枚 +2 军令 → 3 + 1 + 1 + 2 = 7。
        // 变异验证 M-E4：BuildSnapshot 对 Command 用 `deploy = Base + Magnitude`（覆盖而非累加）→ 红 2，含本测试。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(
            ("B2", RelicFixtures.Command()), ("E7", RelicFixtures.Command()), ("H8", RelicFixtures.Command(2)));
        board.Place("B2", TestMaps.P0).Place("E7", TestMaps.P0).Place("H8", TestMaps.P0);
        ledger.Settle(board, 1);

        Assert.Equal(7, ledger.SnapshotFor(TestMaps.P0, board, 0, 1).DeployLimit);
    }

    [Fact]
    public void 无硬上限()
    {
        // 3 枚 +2 军令 → 3 + 6 = 9，系统接受，不截断；同类叠加对探勘 / 征召 / 兵站同样成立。
        // 变异验证 M-E5：BuildSnapshot 末尾加 `deploy = Math.Min(deploy, 8)` → 红 1（本测试）。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(
            ("B2", RelicFixtures.Command(2)), ("E7", RelicFixtures.Command(2)), ("H8", RelicFixtures.Command(2)),
            ("B8", RelicFixtures.Prospecting(2)), ("H2", RelicFixtures.Prospecting(2)), ("E4", RelicFixtures.Prospecting(2)));
        foreach (string cell in new[] { "B2", "E7", "H8", "B8", "H2", "E4" })
        {
            board.Place(cell, TestMaps.P0);
        }

        ledger.Settle(board, 1);
        EffectSnapshot snapshot = ledger.SnapshotFor(TestMaps.P0, board, 0, 1);

        Assert.Equal(9, snapshot.DeployLimit);
        Assert.Equal(11, snapshot.RevealCount);
        Assert.Equal(new DeployLimitPeak(9, 1, TestMaps.P0), ledger.DeployLimitPeak);
    }
}
