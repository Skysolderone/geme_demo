using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Relics;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.InitiativeOrder;

/// <summary>规格：initiative-order —— Requirement: 排名的作用范围</summary>
public class 排名的作用范围Tests
{
    [Fact]
    public void 排名不累计()
    {
        // 设计文档 §10.2 / §11.1：连续 5 个大回合排名第 1 → 不获得任何累计分数或额外资源；势力只评价当前盘面。
        // 变异验证 M-I9：EndMajorRound 给名次第 1 的玩家 Power += 1 写回明细 → 红 1（本测试：五轮 Power 不再恒等）。
        MatchFlow match = MatchFixtures.Started().AtRound(5, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3])
            .Stones(MatchFixtures.P0, "B5", "E5", "H5", "E2", "E8");
        string[][] moves = [["A1", "B1", "C1", "A2", "B2"], ["J1", "H1", "G1", "J2", "H2"], ["A9", "B9", "C9", "A8", "B8"], ["J9", "H9", "G9", "J8", "H8"]];

        for (int round = 0; round < 5; round++)
        {
            match.PassTurn();   // P0 什么都不做，凭既有盘面稳居第 1
            for (int p = 1; p < 4; p++)
            {
                match.PlayTurn(moves[p][round]);
            }
        }

        Assert.Equal(5, match.InitiativeReports.Count);
        Assert.All(match.InitiativeReports, r => Assert.Equal(1, r.Of(MatchFixtures.P0).Rank));
        Assert.Single(match.InitiativeReports.Select(r => r.Of(MatchFixtures.P0).Power).Distinct());
        Assert.Equal(match.Scoreboard.Latest!.Of(MatchFixtures.P0).Total, match.InitiativeReports[^1].Of(MatchFixtures.P0).Power);

        // 落后补偿也不累计：P0 连续 5 个大回合第 1，从未获得补偿（补偿开关已删除，过渡期补偿恒开，段 D 删除补偿本体）

        // 结构上不存在累计分：玩家状态与先手明细类型里没有任何 Score / Accum / Point 字段
        foreach (Type t in new[] { typeof(PlayerFlowState), typeof(InitiativeEntry), typeof(InitiativeReport) })
        {
            foreach (System.Reflection.PropertyInfo p in t.GetProperties())
            {
                Assert.DoesNotContain("Score", p.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Accum", p.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Point", p.Name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void 落后补偿不累计()
    {
        // 规格 Scenario「落后补偿不累计」（catch-up-recruit 裁决 2）：连续 5 个小回合以最后一名开始 → 每个小回合各获得一次当回合的补偿，
        // 补偿不叠加、不结转：5 次快照逐次都是 展示 6 / 选取 4，MUST NOT 递增成 7 / 8…，本回合未用完的选取数也不结转到下一回合。
        // 势力独立复算（四邻接）：P0 A1-D1 → 4 + 5 = 9；P3 J9 → 1 + 2 = 3，名次 4（最后一名）。
        // 变异验证：补偿若被累加 / 结转（例如按玩家保留一个计数器再加到快照上），第 2 个小回合起就会变成 7 / 5，本测试红。
        // 与 M-CU1b（阈值比较改 ≥）、M-CU5（去掉 `rank > 1`）互补：那两条改的是"谁拿"，这条钉的是"拿几次"。
        MatchFlow match = MatchFixtures.Started()
            .AtRound(5, [MatchFixtures.P3, MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2])
            .Stones(MatchFixtures.P0, "A1", "B1", "C1", "D1")
            .Stones(MatchFixtures.P1, "G1", "H1", "J1")
            .Stones(MatchFixtures.P2, "A9", "B9")
            .Stones(MatchFixtures.P3, "J9");

        var seen = new List<string>();
        for (int round = 0; round < 5; round++)
        {
            match.Debug.SetOrder(MatchFixtures.P3, MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2);
            Assert.Equal(MatchFixtures.P3, match.CurrentPlayer);
            Assert.Equal(4, match.Scoreboard.Latest!.RankOf(MatchFixtures.P3));
            match.BeginTurn();
            EffectSnapshot snapshot = match.CurrentSnapshot!;
            seen.Add($"{snapshot.RevealCount}/{snapshot.FreePickCount}/{snapshot.CatchUp.RevealBonus}{snapshot.CatchUp.PickBonus}");
            match.EnterRecruit();
            match.CurrentHand().Pick(0);   // 每回合只用掉 4 个选取名额中的 1 个，剩余名额不结转
            match.EnterDeploy();
            Assert.True(match.Confirm().Confirmed);   // P3 本回合 Pass，盘面不变，下一回合仍是最后一名

            // 第 1 名落 1 枚打断连续 Pass（4 连 Pass 会终局），其余两人 Pass
            match.BeginTurn();
            Assert.Equal(MatchFixtures.P0, match.CurrentPlayer);
            match.EnterRecruit();
            Siege.Core.Batch.StagedBatch batch = match.EnterDeploy();
            Coord cell = batch.Context.LegalRange.Where(c => batch.Board[c].IsPlayableEmpty).Order().First();
            Assert.Null(batch.Stage(cell, PieceType.Basic));
            Assert.True(match.Confirm().Confirmed);
            match.PassTurn();
            match.PassTurn();
        }

        Assert.Equal(["6/4/11", "6/4/11", "6/4/11", "6/4/11", "6/4/11"], seen);
    }
}
