using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Recruit;

namespace Siege.Core.Tests.Recruitment;

/// <summary>规格：recruitment —— Requirement: 选取受类型槽约束</summary>
public class 选取受类型槽约束Tests
{
    /// <summary>5 槽满：普通 / 堡垒 / 连珠 / 倍增各 1，协同 0 → 面板中的协同子是"第 6 种类型"（这里用 4 种 + 4 槽表达同一结构）。</summary>
    private static (HandLedger Ledger, PlayerHandAccess Access, RecruitPanelView Panel) FullSlots()
    {
        // 找一个面板同时含协同子（新类型）与普通子（已有类型）的种子
        GameSeed seed = HandFixtures.SeedWhere(p => p.Contains(PieceType.Synergy) && p.Contains(PieceType.Basic));
        HandLedger ledger = HandFixtures.Ledger(seed);
        ledger.Debug.SeedHand(HandFixtures.P0, (PieceType.Basic, 5), (PieceType.Fortress, 1), (PieceType.Line, 1), (PieceType.Multiplier, 1));
        PlayerHandAccess access = HandFixtures.Begin(ledger, HandFixtures.P0, slots: 4);
        return (ledger, access, access.EnterRecruit());
    }

    [Fact]
    public void 满槽拒绝新类型()
    {
        // 设计文档 §5.3：新类型在没有空槽时不可选择。裁决记录 2：候选照常展示、标不可选并说明"无可用类型槽"，不隐藏不替换。
        // 变异验证 M-R13：RejectReason 的 `Hand.Count >= TypeSlots` 改 `>` → 红 2（本测试 + 选取新类型后其余新类型随即不可选）；
        // M-R14：EnterRecruit 生成候选时过滤掉不可选类型（隐藏）→ 红 1（本测试：面板长度与不可选标记）；
        // M-H1/H2：RejectReason 改按手牌总数比较槽位（类型槽当容量）→ 红 17。
        (_, PlayerHandAccess access, RecruitPanelView panel) = FullSlots();

        Assert.Equal(5, panel.ShowCount);
        Assert.Equal(4, panel.OccupiedSlots);
        Assert.Equal(4, panel.TypeSlots);
        int synergy = panel.IndicesOf(PieceType.Synergy)[0];
        RecruitCandidateView candidate = panel.Candidates[synergy];
        Assert.Equal(PieceType.Synergy, candidate.Type);
        Assert.False(candidate.IsSelectable);
        Assert.Contains("无可用类型槽", candidate.Reason);

        var ex = Assert.Throws<SiegeRuleException>(() => access.Pick(synergy));
        Assert.Contains("无可用类型槽", ex.Message);
        Assert.Equal(0, access.PrivateView().CountOf(PieceType.Synergy));
        Assert.Equal(4, access.PrivateView().OccupiedSlots);
    }

    [Fact]
    public void 满槽可叠加已有类型()
    {
        // 设计文档 §5.3：已有类型可继续叠加，数量 +1，槽位占用不变。
        // 变异验证 M-R15：RejectReason 去掉 `!Hand.ContainsKey(type) &&`（满槽一律拒）→ 红 2（本测试 + 同类无限叠加）。
        (_, PlayerHandAccess access, RecruitPanelView panel) = FullSlots();
        int basic = panel.IndicesOf(PieceType.Basic)[0];
        Assert.True(panel.Candidates[basic].IsSelectable);

        access.Pick(basic);

        HandPrivateView hand = access.PrivateView();
        Assert.Equal(6, hand.CountOf(PieceType.Basic));
        Assert.Equal(new HandEntry(5, 1), hand.EntryOf(PieceType.Basic));
        Assert.Equal(4, hand.OccupiedSlots);
    }

    [Fact]
    public void 选取新类型后其余新类型随即不可选()
    {
        // 类型槽是种类维度：恰好剩 1 个空槽时，选一枚新类型后面板里其他新类型立即变为不可选（可选性按当前手牌现算）。
        GameSeed seed = HandFixtures.SeedWhere(p => p.Contains(PieceType.Synergy) && p.Contains(PieceType.Multiplier));
        HandLedger ledger = HandFixtures.Ledger(seed);
        ledger.Debug.SeedHand(HandFixtures.P0, (PieceType.Basic, 5), (PieceType.Fortress, 1), (PieceType.Line, 1));
        PlayerHandAccess access = HandFixtures.Begin(ledger, HandFixtures.P0, slots: 4);
        RecruitPanelView panel = access.EnterRecruit();
        Assert.True(panel.Candidates[panel.IndicesOf(PieceType.Synergy)[0]].IsSelectable);

        access.Pick(panel.IndicesOf(PieceType.Multiplier)[0]);

        RecruitCandidateView synergy = access.Panel().Candidates[panel.IndicesOf(PieceType.Synergy)[0]];
        Assert.False(synergy.IsSelectable);
        Assert.Contains("无可用类型槽", synergy.Reason);
    }
}
