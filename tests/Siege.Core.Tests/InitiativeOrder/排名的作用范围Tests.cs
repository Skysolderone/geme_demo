using Siege.Core.Match;

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
}
