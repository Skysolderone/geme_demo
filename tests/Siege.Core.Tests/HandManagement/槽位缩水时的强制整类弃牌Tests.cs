using Siege.Core.Board;
using Siege.Core.Recruit;

namespace Siege.Core.Tests.HandManagement;

/// <summary>规格：hand-management —— Requirement: 槽位缩水时的强制整类弃牌</summary>
public class 槽位缩水时的强制整类弃牌Tests
{
    private static (HandLedger Ledger, PlayerHandAccess Access) SixTypesFiveSlots()
    {
        HandLedger ledger = HandFixtures.Ledger();
        // 上一回合有兵站（6 槽）时攒下的 6 种类型：五种棋子外加……原型只有五种棋子，故用 5 种 + 槽位 4 表达同一超限结构（持有 = 槽位 + 1）
        ledger.Debug.SeedHand(HandFixtures.P0, (PieceType.Basic, 5), (PieceType.Fortress, 1), (PieceType.Line, 1), (PieceType.Multiplier, 1), (PieceType.Synergy, 2));
        PlayerHandAccess access = HandFixtures.Begin(ledger, HandFixtures.P0, slots: 4);
        return (ledger, access);
    }

    [Fact]
    public void 兵站丢失触发强制弃牌()
    {
        // 设计文档 §5.2 / design.md D7：持有类型数超过本小回合快照槽位 → 征募阶段不可进入（阻断式，不是提示式），随机子流也不被消费。
        // 变异验证 M-H8：EnterRecruit 去掉超限检查（只在 PrivateView 标 Overflow 提示）→ 红 1（本测试）。
        (HandLedger ledger, PlayerHandAccess access) = SixTypesFiveSlots();
        Assert.Equal(1, access.PrivateView().Overflow);

        var ex = Assert.Throws<SiegeRuleException>(() => access.EnterRecruit());

        Assert.Contains("超出", ex.Message);
        Assert.Contains("整类弃牌", ex.Message);
        Assert.Equal(TurnPhase.Organize, ledger.PhaseOf(HandFixtures.P0));
        Assert.Equal(0, ledger.RecruitStreamConsumed);
        Assert.Throws<SiegeRuleException>(() => access.Panel());
        // 超限期间也不能跳过征募直接结算
        Assert.Throws<SiegeRuleException>(() => ledger.OnPass(HandFixtures.P0));
        Assert.Throws<SiegeRuleException>(() => ledger.DeductHand(HandFixtures.P0, HandFixtures.Deployed((PieceType.Basic, 1))));
    }

    [Fact]
    public void 超限解除后继续()
    {
        // 设计文档 §5.2：完成 1 次整类弃牌、类型数降至槽位数 → 超限解除，小回合继续进入征募阶段。
        // check M-C2：EnterRecruit 改用快照携带的陈旧 OverflowTypeCount（弃牌后不更新）而非账本现算 → 红 1（本测试）。这是本层必须自己算超限的守门。
        (HandLedger ledger, PlayerHandAccess access) = SixTypesFiveSlots();

        access.Discard(PieceType.Synergy);
        Assert.Equal(0, access.PrivateView().Overflow);
        RecruitPanelView panel = access.EnterRecruit();

        Assert.Equal(TurnPhase.Recruit, ledger.PhaseOf(HandFixtures.P0));
        Assert.Equal(5, panel.ShowCount);
        Assert.Equal(4, panel.OccupiedSlots);
        Assert.Equal(4, panel.TypeSlots);
    }

    [Fact]
    public void 快照持有类型数与账本不一致视为接线错误()
    {
        // boundaries.md「显式输入的名册：未知玩家必须响亮失败」的同类规则：快照携带的 HeldTypeCount 是手牌层给出的输入，对不上就抛出，不静默采用任何一边。
        // 变异验证 M-H9：BeginTurn 去掉 HeldTypeCount 一致性检查 → 红 1（本测试）。
        HandLedger ledger = HandFixtures.Ledger();
        var wrong = new Relics.EffectSnapshot(HandFixtures.P0, 1, 5, 3, 5, 3, System.Collections.Immutable.ImmutableSortedDictionary<PieceType, int>.Empty, heldTypeCount: 3);

        var ex = Assert.Throws<SiegeRuleException>(() => ledger.BeginTurn(HandFixtures.P0, wrong));
        Assert.Contains("不一致", ex.Message);
        Assert.Throws<SiegeRuleException>(() => ledger.BeginTurn(HandFixtures.P0, HandFixtures.Snapshot(ledger, HandFixtures.P1)));
        Assert.Throws<SiegeRuleException>(() => ledger.BeginTurn(new PlayerId(9), HandFixtures.Snapshot(ledger, HandFixtures.P0)));
        Assert.Equal(TurnPhase.Idle, ledger.PhaseOf(HandFixtures.P0));
    }
}
