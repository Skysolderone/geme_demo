using Siege.Core.Board;
using Siege.Core.Recruit;

namespace Siege.Core.Tests.HandManagement;

/// <summary>规格：hand-management —— Requirement: 手牌为空的可查询状态</summary>
public class 手牌为空的可查询状态Tests
{
    [Fact]
    public void 手牌清空()
    {
        // 设计文档 §12.1：玩家把全部手牌部署到盘面 → "手牌是否为空"返回是；部署前为否。
        // 变异验证 M-H21：IsHandEmpty 改为 `Hand.Count == 1` → 红 1（本测试）。
        HandLedger ledger = HandFixtures.Ledger();
        Assert.False(ledger.IsHandEmpty(HandFixtures.P0));
        PlayerHandAccess access = HandFixtures.Begin(ledger, HandFixtures.P0);
        access.EnterRecruit();

        ledger.DeductHand(HandFixtures.P0, HandFixtures.Deployed((PieceType.Basic, 5)));

        Assert.True(ledger.IsHandEmpty(HandFixtures.P0));
        Assert.True(ledger.PublicView(HandFixtures.P0).IsEmpty);
        Assert.True(access.PrivateView().IsEmpty);
        Assert.Equal(0, access.PrivateView().OccupiedSlots);
        Assert.False(ledger.IsHandEmpty(HandFixtures.P1));
        Assert.Throws<SiegeRuleException>(() => ledger.IsHandEmpty(new PlayerId(7)));
    }
}
