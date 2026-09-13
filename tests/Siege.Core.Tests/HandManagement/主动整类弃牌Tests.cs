using Siege.Core.Board;
using Siege.Core.Recruit;

namespace Siege.Core.Tests.HandManagement;

/// <summary>规格：hand-management —— Requirement: 主动整类弃牌</summary>
public class 主动整类弃牌Tests
{
    [Fact]
    public void 整类弃牌腾槽()
    {
        // 设计文档 §5.2：持 5 种、槽位已满，弃掉全部 3 枚协同子 → 协同子从手牌移除、空出 1 槽、不返还任何资源（手牌总数减 3，其余不变）。
        // 变异验证 M-H5：Discard 只把 Carried 减 1 → 红 4（本测试 + 拒绝部分弃牌 + 超限解除后继续 + Pass 不撤销本轮的整类弃牌）。
        HandLedger ledger = HandFixtures.Ledger();
        ledger.Debug.SeedHand(HandFixtures.P0, (PieceType.Basic, 5), (PieceType.Fortress, 2), (PieceType.Line, 1), (PieceType.Multiplier, 1), (PieceType.Synergy, 3));
        PlayerHandAccess access = HandFixtures.Begin(ledger, HandFixtures.P0);
        Assert.Equal(5, access.PrivateView().OccupiedSlots);

        access.Discard(PieceType.Synergy);

        HandPrivateView hand = access.PrivateView();
        Assert.Equal(0, hand.CountOf(PieceType.Synergy));
        Assert.DoesNotContain(PieceType.Synergy, hand.Types);
        Assert.Equal(4, hand.OccupiedSlots);
        Assert.Equal(9, hand.TotalCount);
        Assert.Equal(5, hand.CountOf(PieceType.Basic));
        Assert.DoesNotContain(PieceType.Synergy, ledger.PublicView(HandFixtures.P0).Types);
    }

    [Fact]
    public void 拒绝部分弃牌()
    {
        // 设计文档 §5.2：弃牌以整类为单位。持 4 枚堡垒子、尝试只弃 2 枚 → 拒绝并说明弃牌必须整类进行，手牌不变。
        // 变异验证 M-H6：Discard(type, count) 的 `count != held` 改 `count > held` → 红 1（本测试）。
        HandLedger ledger = HandFixtures.Ledger();
        ledger.Debug.SeedHand(HandFixtures.P0, (PieceType.Basic, 5), (PieceType.Fortress, 4));
        PlayerHandAccess access = HandFixtures.Begin(ledger, HandFixtures.P0);

        var ex = Assert.Throws<SiegeRuleException>(() => access.Discard(PieceType.Fortress, 2));

        Assert.Contains("弃牌必须整类进行", ex.Message);
        Assert.Equal(4, access.PrivateView().CountOf(PieceType.Fortress));
        // 数量恰为全部持有量时等价于整类弃牌
        access.Discard(PieceType.Fortress, 4);
        Assert.Equal(0, access.PrivateView().CountOf(PieceType.Fortress));
        // 未持有的类型无从弃牌
        Assert.Throws<SiegeRuleException>(() => access.Discard(PieceType.Line));
    }

    [Fact]
    public void 只允许在整理手牌阶段弃牌()
    {
        // 裁决记录 3：征募与部署阶段不可弃牌，避免玩家看到候选后再回头弃牌腾槽。
        // 变异验证 M-H7：Discard 的阶段检查改为 `Phase == Idle` 时才拒 → 红 1（本测试）。
        HandLedger ledger = HandFixtures.Ledger();
        ledger.Debug.SeedHand(HandFixtures.P0, (PieceType.Basic, 5), (PieceType.Fortress, 1));
        PlayerHandAccess access = ledger.AccessFor(HandFixtures.P0);
        Assert.Throws<SiegeRuleException>(() => access.Discard(PieceType.Fortress));

        HandFixtures.Begin(ledger, HandFixtures.P0);
        access.EnterRecruit();
        var ex = Assert.Throws<SiegeRuleException>(() => access.Discard(PieceType.Fortress));
        Assert.Contains("整理手牌阶段", ex.Message);

        ledger.OnPass(HandFixtures.P0);
        Assert.Throws<SiegeRuleException>(() => access.Discard(PieceType.Fortress));
        Assert.Equal(1, access.PrivateView().CountOf(PieceType.Fortress));
    }
}
