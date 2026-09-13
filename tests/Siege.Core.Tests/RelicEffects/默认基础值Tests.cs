using Siege.Core.Board;
using Siege.Core.Relics;

namespace Siege.Core.Tests.RelicEffectsSpec;

/// <summary>规格：relic-effects —— Requirement: 默认基础值</summary>
public class 默认基础值Tests
{
    [Fact]
    public void 无信物时的快照()
    {
        // 设计文档 §5.2–§5.4：展示数 5、免费选取数 3、手牌类型槽 5、部署上限 3；先手修正 0。
        // 变异验证 M-E12：BaseDeployLimit 改 4 → 红 9，含本测试。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Command()));
        board.Place("A1", TestMaps.P0);
        ledger.Settle(board, 1);

        EffectSnapshot snapshot = ledger.SnapshotFor(TestMaps.P0, board, 0, 1);

        Assert.Equal((5, 3, 5, 3), (snapshot.RevealCount, snapshot.FreePickCount, snapshot.TypeSlots, snapshot.DeployLimit));
        Assert.Empty(snapshot.EmblemCounts);
        Assert.Equal(0, snapshot.OverflowTypeCount);
        Assert.Equal(4, snapshot.EmblemWeightNumerator(PieceType.Basic));
        Assert.Equal(0, ledger.ReadInitiativeBonuses(board)[TestMaps.P0]);
        Assert.Equal(snapshot, EffectSnapshot.Defaults(TestMaps.P0, 1, 0));
    }
}
