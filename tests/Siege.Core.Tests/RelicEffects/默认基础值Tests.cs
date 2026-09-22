using Siege.Core.Board;
using Siege.Core.Relics;

namespace Siege.Core.Tests.RelicEffectsSpec;

/// <summary>规格：relic-effects —— Requirement: 默认基础值</summary>
public class 默认基础值Tests
{
    [Fact]
    public void 无信物时的快照()
    {
        // 规格 Scenario「无信物时的快照」：玩家不控制任何信物，其第 2 大回合的小回合开始 → 展示数 5、免费选取数 3、手牌类型槽 5、
        // 部署上限 3（第 1–3 大回合的分阶段基础值）；先手修正 0。restore-go-core-rules 裁决 #7：默认值不再附带任何名次前提。
        // 变异验证 M-E12（growth-pass-1 前）：BaseDeployLimit 改 4 → 红 9，含本测试。常量已由 BaseDeployLimitFor 取代，现行记录见 M-GP2。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Command()));
        board.Place("A1", TestMaps.P0);
        ledger.Settle(board, 2);

        EffectSnapshot snapshot = ledger.SnapshotFor(TestMaps.P0, board, 0, 2);

        Assert.Equal((5, 3, 5, 3), (snapshot.RevealCount, snapshot.FreePickCount, snapshot.TypeSlots, snapshot.DeployLimit));
        Assert.Empty(snapshot.EmblemCounts);
        Assert.Equal(0, snapshot.OverflowTypeCount);
        Assert.Equal(4, snapshot.EmblemWeightNumerator(PieceType.Basic));
        Assert.Equal(0, ledger.ReadInitiativeBonuses(board)[TestMaps.P0]);
        Assert.Equal(snapshot, EffectSnapshot.Defaults(TestMaps.P0, 2, 0));
    }
}
