using Siege.Core.Board;
using Siege.Core.Relics;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.RelicEffectsSpec;

/// <summary>规格：relic-effects —— Requirement: 先锋的独立读取时机</summary>
public class 先锋的独立读取时机Tests
{
    [Fact]
    public void 先锋在回合结束时读取()
    {
        // 设计文档 §8.1：P0 在本大回合最后一个小回合占领先锋 E7 → 本大回合结束读取时即计入先手修正 +1；控制 +2 先锋与另一枚普通先锋的 P1 为 3。
        // 变异验证 M-E8：ReadInitiativeBonuses 读上次结算缓存而不重算 → 红 1（本测试，P0 占领后未结算即读取）；
        // M-E9：SumVanguard 忽略 Magnitude → 红 1（本测试）。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(
            ("E7", RelicFixtures.Vanguard()), ("B2", RelicFixtures.Vanguard(2)), ("H8", RelicFixtures.Vanguard()));
        board.Place("B2", TestMaps.P1).Place("H7", TestMaps.P1);
        ledger.Settle(board, 1);
        IReadOnlyDictionary<PlayerId, PlayerStatus> roster = RelicFixtures.AllActive(TestMaps.P0, TestMaps.P1);
        Assert.Equal(new Dictionary<PlayerId, int> { [TestMaps.P0] = 0, [TestMaps.P1] = 3 }, ledger.ReadInitiativeBonuses(board, roster));

        // 最后一个小回合：P0 占领 E7
        board.Place("E7", TestMaps.P0);

        Assert.Equal(new Dictionary<PlayerId, int> { [TestMaps.P0] = 1, [TestMaps.P1] = 3 }, ledger.ReadInitiativeBonuses(board, roster));
    }

    [Fact]
    public void 先锋不进小回合快照()
    {
        // 快照结构不含先手修正字段；控制先锋的玩家小回合快照与默认值完全一致。
        // 变异验证 M-E10：BuildSnapshot 的 Vanguard 分支改为 `deploy += Magnitude`（先锋混进快照）→ 红 2，含本测试；
        // M-E11：给 EffectSnapshot 加 `InitiativeBonus` 属性 → 红 1（本测试）。
        foreach (System.Reflection.PropertyInfo p in typeof(EffectSnapshot).GetProperties())
        {
            Assert.DoesNotContain("Initiative", p.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Vanguard", p.Name, StringComparison.OrdinalIgnoreCase);
        }

        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Vanguard(2)));
        board.Place("E7", TestMaps.P0);
        ledger.Settle(board, 1);

        Assert.Equal(EffectSnapshot.Defaults(TestMaps.P0, 1, 0), ledger.SnapshotFor(TestMaps.P0, board, 0, 1));
        Assert.Equal(2, ledger.ReadInitiativeBonuses(board)[TestMaps.P0]);
    }
}
