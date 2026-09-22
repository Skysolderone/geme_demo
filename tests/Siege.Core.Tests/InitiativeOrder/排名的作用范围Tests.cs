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
    public void 落后不获补偿()
    {
        // 规格 Scenario「落后不获补偿」（restore-go-core-rules 裁决 #7）：某玩家连续 5 个小回合以最后一名开始 →
        // 其征募展示数、免费选取数与其他结构参数不因名次发生任何变化。
        // 势力独立复算（四邻接）：P0 A1-D1 → 4 + 5 = 9；P1 G1 H1 J1 → 3 + 4 = 7；P2 A9 B9 → 2 + 3 = 5；P3 J9 → 1 + 2 = 3，名次 4（最后一名）。
        // 每个大回合同时记下第 1 名 P0 的快照（二者都不控制任何信物）：两份逐项相同，且等于默认值 + 该大回合的分阶段基础部署上限
        // （第 5、6 大回合 4，第 7–9 大回合 5）。
        // 旧 → 新：旧名 `落后补偿不累计`，期望 5 次 "6/4/11"（展示 6 / 选取 4 / 两档补偿各 1）→ 5 次 P3 = P0 = "5/3/5/{4,4,5,5,5}"，依据裁决 #7 删除落后补偿。
        MatchFlow match = MatchFixtures.Started()
            .AtRound(5, [MatchFixtures.P3, MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2])
            .Stones(MatchFixtures.P0, "A1", "B1", "C1", "D1")
            .Stones(MatchFixtures.P1, "G1", "H1", "J1")
            .Stones(MatchFixtures.P2, "A9", "B9")
            .Stones(MatchFixtures.P3, "J9");

        var last = new List<string>();
        var first = new List<string>();
        for (int round = 0; round < 5; round++)
        {
            match.Debug.SetOrder(MatchFixtures.P3, MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2);
            Assert.Equal(MatchFixtures.P3, match.CurrentPlayer);
            Assert.Equal(4, match.Scoreboard.Latest!.RankOf(MatchFixtures.P3));
            match.BeginTurn();
            last.Add(Params(match.CurrentSnapshot!));
            match.EnterRecruit();
            match.CurrentHand().Pick(0);
            match.EnterDeploy();
            Assert.True(match.Confirm().Confirmed);   // P3 本回合 Pass，盘面不变，下一回合仍是最后一名

            // 第 1 名落 1 枚打断连续 Pass（4 连 Pass 会终局），其余两人 Pass
            Assert.Equal(1, match.Scoreboard.Latest!.RankOf(MatchFixtures.P0));
            match.BeginTurn();
            Assert.Equal(MatchFixtures.P0, match.CurrentPlayer);
            first.Add(Params(match.CurrentSnapshot!));
            match.EnterRecruit();
            Siege.Core.Batch.StagedBatch batch = match.EnterDeploy();
            Coord cell = batch.Context.LegalRange.Where(c => batch.Board[c].IsPlayableEmpty).Order().First();
            Assert.Null(batch.Stage(cell, PieceType.Basic));
            Assert.True(match.Confirm().Confirmed);
            match.PassTurn();
            match.PassTurn();
        }

        Assert.Equal(["5:5/3/5/4", "6:5/3/5/4", "7:5/3/5/5", "8:5/3/5/5", "9:5/3/5/5"], last);
        Assert.Equal(first, last);

        static string Params(EffectSnapshot s) =>
            $"{s.MajorRound}:{s.RevealCount}/{s.FreePickCount}/{s.TypeSlots}/{s.DeployLimit}{(s.EmblemCounts.IsEmpty ? string.Empty : "/徽记")}";
    }
}
