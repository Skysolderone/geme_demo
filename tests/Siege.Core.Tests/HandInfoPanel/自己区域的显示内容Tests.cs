using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Presentation.Hand;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.HandInfoPanel;

/// <summary>规格：hand-info-panel —— Requirement: 自己区域的显示内容</summary>
public class 自己区域的显示内容Tests
{
    [Fact]
    public void 自己看得到准确数量()
    {
        // 设计文档 §14.3 算例：持普通子×6、堡垒子×1 → 自己区域显示两种类型及准确数量 6 与 1。
        // 变异验证 M-O1：HandInfoPanelView.OwnArea 的 Count 改为 `kv.Value.Gained` → 本测试红 1。
        MatchFlow match = AiFixtures.Round5();
        match.Debug.SeedHand(P0, (PieceType.Basic, 6), (PieceType.Fortress, 1));

        OwnHandAreaView own = match.World(P0).HandPanel().Own;

        Assert.Equal([(PieceType.Basic, 6), (PieceType.Fortress, 1)], own.Rows.Select(r => (r.Type, r.Count)));
        Assert.Equal(["普通子 ×6", "堡垒子 ×1"], own.Rows.Select(r => r.Text));
    }

    [Fact]
    public void 显示本轮新征募未提交数量()
    {
        // 设计文档 §14.3 算例：本轮新征募普通子×2 且尚未确认批次 → 标示普通子中有 2 枚为本轮新征募。
        // 从固定起点顺序搜索种子，取第一个"首个面板至少两枚普通子候选"的局（搜索确定）。
        // 变异验证 M-O2：OwnArea 的 PendingGained 改读 `kv.Value.Carried` → 本测试红 1。
        MatchFlow match = FindMatchWithTwoBasicCandidates(out RecruitPanelView panel);
        PlayerHandAccess hand = match.CurrentHand();
        foreach (int index in panel.Candidates.Where(c => c.Type == PieceType.Basic).Take(2).Select(c => c.Index))
        {
            hand.Pick(index);
        }

        match.EnterDeploy();
        OwnHandAreaView own = match.World(P0).HandPanel().Own;

        OwnHandRowView basic = Assert.Single(own.Rows, r => r.Type == PieceType.Basic);
        Assert.Equal((6, 2), (basic.Count, basic.PendingGained));
        Assert.Equal("普通子 ×6（其中 2 枚为本轮新征募）", basic.Text);
        Assert.Equal(2, own.PendingGained);
    }

    [Fact]
    public void 显示槽位占用()
    {
        // 设计文档 §14.3 算例：持 3 种类型、槽位上限 5 → 已占 3、余 2。回合外取公开参数，回合内取本回合快照，两处都钉住。
        // 变异验证 M-O3：OwnArea 的 FreeSlots 改为 `s - 1` → 本测试红 1。
        MatchFlow match = AiFixtures.Round5();
        match.Debug.SeedHand(P0, (PieceType.Basic, 2), (PieceType.Fortress, 1), (PieceType.Line, 1));

        OwnHandAreaView idle = match.World(P0).HandPanel().Own;
        Assert.Equal((3, 5, 2), (idle.OccupiedSlots, idle.TypeSlots!.Value, idle.FreeSlots!.Value));

        match.BeginTurn();
        OwnHandAreaView inTurn = match.World(P0).HandPanel().Own;
        Assert.Equal(5, match.Hands.AccessFor(P0).PrivateView().TypeSlots);
        Assert.Equal((3, 5, 2), (inTurn.OccupiedSlots, inTurn.TypeSlots!.Value, inTurn.FreeSlots!.Value));
    }

    private static MatchFlow FindMatchWithTwoBasicCandidates(out RecruitPanelView panel)
    {
        for (ulong v = 1; v < 200; v++)
        {
            MatchFlow match = MatchFixtures.Started(new GameSeed(v)).AtRound(5);
            match.Debug.SeedHand(P0, (PieceType.Basic, 4));
            match.BeginTurn();
            panel = match.EnterRecruit();
            if (panel.Candidates.Count(c => c.Type == PieceType.Basic && c.IsSelectable) >= 2)
            {
                return match;
            }
        }

        throw new InvalidOperationException("前 200 个种子里找不到首个面板含两枚普通子的局。");
    }
}
