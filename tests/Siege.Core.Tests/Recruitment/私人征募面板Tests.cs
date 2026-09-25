using System.Reflection;
using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Core.Recruit;

namespace Siege.Core.Tests.Recruitment;

/// <summary>规格：recruitment —— Requirement: 私人征募面板</summary>
public class 私人征募面板Tests
{
    [Fact]
    public void 默认面板()
    {
        // 设计文档 §5.3：默认展示 5、最多免费选取 3。选取数是上限（裁决记录 1）：第 4 次选取被拒，但少选、不选都合法。
        // 变异验证 M-R9：EnterRecruit 循环写成 `i <= RevealCount` → 红 36（几乎全部依赖面板长度 / 随机序列的用例）；
        // M-R10：RejectReason 的 `Picked.Count >= FreePickCount` 改 `>` → 红 2（本测试 + 探勘与征召生效）。
        HandLedger ledger = HandFixtures.Ledger();
        PlayerHandAccess access = HandFixtures.Begin(ledger, HandFixtures.P0);
        RecruitPanelView panel = access.EnterRecruit();

        Assert.Equal(5, panel.ShowCount);
        Assert.Equal(3, panel.FreePickCount);
        Assert.Equal(3, panel.PicksRemaining);
        Assert.All(panel.Candidates, c => Assert.True(c.IsSelectable));

        access.Pick(0);
        access.Pick(1);
        access.Pick(2);
        Assert.Equal(0, access.Panel().PicksRemaining);
        Assert.All(access.Panel().Candidates.Where(c => !c.IsPicked), c => Assert.Contains("免费选取数已用完", c.Reason));
        var ex = Assert.Throws<SiegeRuleException>(() => access.Pick(3));
        Assert.Contains("免费选取数已用完", ex.Message);
        Assert.Equal(3, access.PrivateView().PendingGained);
    }

    [Fact]
    public void 探勘与征召生效()
    {
        // 设计文档 §8.1：快照展示 7、选取 4 → 面板 7 枚、最多选 4 枚。
        HandLedger ledger = HandFixtures.Ledger();
        PlayerHandAccess access = HandFixtures.Begin(ledger, HandFixtures.P0, reveal: 7, freePick: 4);
        RecruitPanelView panel = access.EnterRecruit();

        Assert.Equal(7, panel.ShowCount);
        Assert.Equal(4, panel.FreePickCount);
        for (int i = 0; i < 4; i++)
        {
            access.Pick(i);
        }

        Assert.Throws<SiegeRuleException>(() => access.Pick(4));
        Assert.Equal(4, access.PrivateView().PendingGained);
    }

    [Fact]
    public void 最后一名没有补偿()
    {
        // 规格 Scenario「最后一名没有补偿」（restore-go-core-rules 裁决 #7）：4 人局中势力名次第 4 的玩家不控制任何信物，进入征募阶段
        // → 面板展示 5 枚候选，最多可免费选取 3 枚，与第 1 名相同。走真实 MatchFlow（名次来自势力榜），不走 HandFixtures 的直给快照。
        // 势力独立复算（四邻接）：P0 A1-D1 → 4 + 5 = 9；P1 G1 H1 J1 → 3 + 4 = 7；P2 A9 B9 → 2 + 3 = 5；P3 J9 → 1 + 2 = 3。
        // 先红：旧实现下 P3 面板为 展示 6 / 选取 4 → 本测试红。
        MatchFlow match = MatchFixtures.Started()
            .AtRound(5, [MatchFixtures.P3, MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2])
            .Stones(MatchFixtures.P0, "A1", "B1", "C1", "D1")
            .Stones(MatchFixtures.P1, "G1", "H1", "J1")
            .Stones(MatchFixtures.P2, "A9", "B9")
            .Stones(MatchFixtures.P3, "J9");
        Assert.Equal(4, match.Scoreboard.Latest!.RankOf(MatchFixtures.P3));
        Assert.Empty(match.Relics.PublicStates().Where(s => s.Control.GrantsEffectTo(MatchFixtures.P3)));

        match.BeginTurn();
        Assert.Equal(MatchFixtures.P3, match.CurrentPlayer);
        RecruitPanelView lastPanel = match.EnterRecruit();
        Assert.Equal((5, 3, 3), (lastPanel.ShowCount, lastPanel.FreePickCount, lastPanel.PicksRemaining));
        match.EnterDeploy();
        Assert.True(match.Confirm().Confirmed);

        Assert.Equal(1, match.Scoreboard.Latest!.RankOf(MatchFixtures.P0));
        match.BeginTurn();
        Assert.Equal(MatchFixtures.P0, match.CurrentPlayer);
        RecruitPanelView firstPanel = match.EnterRecruit();
        Assert.Equal((firstPanel.ShowCount, firstPanel.FreePickCount), (lastPanel.ShowCount, lastPanel.FreePickCount));
    }

    [Fact]
    public void 选取数是上限不强制取满()
    {
        // 裁决记录 1：玩家可以为保留类型槽而少拿或不拿，随后正常进入部署（这里以 Pass 结算）。
        // 契约测试：没有"强制取满"的实现行可改，故不做变异记录。
        HandLedger ledger = HandFixtures.Ledger();
        PlayerHandAccess access = HandFixtures.Begin(ledger, HandFixtures.P0);
        access.EnterRecruit();
        access.Pick(0);

        ledger.OnPass(HandFixtures.P0);
        ledger.EndTurn(HandFixtures.P0);
        Assert.Equal(1, ledger.Records[0].RecruitedCount);
        Assert.Equal(TurnPhase.Idle, ledger.PhaseOf(HandFixtures.P0));
    }

    [Fact]
    public void 无刷新()
    {
        // 设计文档 §5.3：无金币、无付费刷新、无基础免费刷新。断言征募层的全部公开类型上不存在任何刷新 / 金币入口，
        // 且同一小回合内多次读取面板得到同一组候选（没有隐式重抽）。
        // 变异验证 M-R11：PlayerHandAccess 加一个 public void Refresh() → 红 1（本测试）。
        IEnumerable<MemberInfo> members = typeof(HandLedger).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(HandLedger).Namespace)
            .SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly));
        foreach (MemberInfo member in members)
        {
            foreach (string token in new[] { "refresh", "reroll", "gold", "coin", "刷新", "金币" })
            {
                Assert.False(member.Name.Contains(token, StringComparison.OrdinalIgnoreCase), $"{member.DeclaringType!.Name}.{member.Name} 疑似刷新/金币入口");
            }
        }

        HandLedger ledger = HandFixtures.Ledger();
        PlayerHandAccess access = HandFixtures.Begin(ledger, HandFixtures.P0);
        RecruitPanelView first = access.EnterRecruit();
        Assert.Equal(first.CandidateTypes, access.Panel().CandidateTypes);
        Assert.Equal(first.CandidateTypes, access.Panel().CandidateTypes);
        Assert.Throws<SiegeRuleException>(() => access.EnterRecruit());
    }

    [Fact]
    public void 未选候选消失()
    {
        // 设计文档 §5.3：展示 5 选 3 → 未选的 2 枚不进手牌，也不出现在下一小回合的面板中（下一面板是 recruit 子流的下 5 次抽取）。
        // 变异验证 M-R12：EnterRecruit 沿用上一次的候选（跨回合结转）→ 红 4（本测试 + 征募记录完整 + 两个分布用例）。
        GameSeed seed = HandFixtures.Seed;
        // more-pieces-relics 段 A：期望序列用六档字面量表独立抽取，依赖征募序列 → 写死内容集 v1（v1 棋池与引入新棋子之前相同）。
        HandLedger ledger = HandFixtures.Ledger(ContentSet.V1, seed);
        PlayerHandAccess access = HandFixtures.Begin(ledger, HandFixtures.P0);
        RecruitPanelView first = access.EnterRecruit();
        access.Pick(0);
        access.Pick(1);
        access.Pick(2);
        ledger.DeductHand(HandFixtures.P0, HandFixtures.Deployed((PieceType.Basic, 1)));
        ledger.EndTurn(HandFixtures.P0);

        HandPrivateView after = access.PrivateView();
        Assert.Equal(5 - 1 + 3, after.TotalCount);
        Assert.Throws<SiegeRuleException>(() => access.Panel());

        RandomStream expected = seed.Stream(GameSeed.Recruit);
        int[] table = [160, 80, 72, 48, 40, 40];
        for (int i = 0; i < 5; i++)
        {
            expected.WeightedPick(table);
        }

        PieceType[] nextDirect = [.. Enumerable.Range(0, 5).Select(_ => RecruitWeights.Order[expected.WeightedPick(table)])];
        access = HandFixtures.Begin(ledger, HandFixtures.P0, round: 2);
        RecruitPanelView second = access.EnterRecruit();
        Assert.Equal(nextDirect, second.CandidateTypes);
        Assert.NotSame(first, second);
    }
}
