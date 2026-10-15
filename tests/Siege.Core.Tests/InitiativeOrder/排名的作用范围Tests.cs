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
        // retire-legacy-rules 1.5：载体为平坦 8×8 链接档图（四人同锁棋盘 0；每个大回合都有人落子 → 本战场不结算）。P1 / P2 / P3 的五手仍下在东南 / 西北 / 东北三个角
        // （四角各贴棋盘 0 的角），原 9×9 上 P0 的十字五子在 8×8 上会与角上的棋串共享邻格（势力随之变化，"恒等"就不再是本测试要守的东西），
        // 改为棋盘 0 中部五个与三角棋串曼哈顿距离 ≥ 3、彼此距离 ≥ 3 的格。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5, [TestMaps.P0, TestMaps.P1, TestMaps.P2, TestMaps.P3])
            .Stones(TestMaps.P0, "C3", "E4", "G5", "C6", "E7");
        string[][] moves = [[], ["K3", "J3", "H3", "K4", "J4"], ["C10", "D10", "E10", "C9", "D9"], ["K10", "J10", "H10", "K9", "J9"]];

        for (int round = 0; round < 5; round++)
        {
            match.PassTurn();   // P0 什么都不做，凭既有盘面稳居第 1
            for (int p = 1; p < 4; p++)
            {
                match.PlayTurn(moves[p][round]);
            }
        }

        Assert.Equal(5, match.InitiativeReports.Count);
        Assert.All(match.InitiativeReports, r => Assert.Equal(1, r.Of(TestMaps.P0).Rank));
        Assert.Single(match.InitiativeReports.Select(r => r.Of(TestMaps.P0).Power).Distinct());
        Assert.Equal(match.Scoreboard.Latest!.Of(TestMaps.P0).Total, match.InitiativeReports[^1].Of(TestMaps.P0).Power);

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
        // 势力独立复算（四邻接）：P0 C3-F3 → 4 + 4 = 8（G3 夹在 P0 与 P1 之间为争议格）；P1 H3 J3 K3 → 3 + 3 = 6；P2 C10 D10 → 2 + 3 = 5；P3 K10 → 1 + 2 = 3，名次 4（最后一名）。
        // （retire-legacy-rules 1.5：载体为平坦 8×8 链接档图、四人同锁棋盘 0，原 9×9 的四角各贴棋盘 0 的角；原 9×9 复算为 9 / 7 / 5 / 3，名次不变。）
        // 每个大回合同时记下第 1 名 P0 的快照（二者都不控制任何信物）：两份逐项相同，且等于默认值 + 该大回合的分阶段基础部署上限
        // （第 5、6 大回合 4，第 7–9 大回合 5）。
        // 旧 → 新：旧名 `落后补偿不累计`，期望 5 次 "6/4/11"（展示 6 / 选取 4 / 两档补偿各 1）→ 5 次 P3 = P0 = "5/3/5/{4,4,5,5,5}"，依据裁决 #7 删除落后补偿。
        MatchFlow match = LinkModeFixtures.PlainStarted()
            .AtRound(5, [TestMaps.P3, TestMaps.P0, TestMaps.P1, TestMaps.P2])
            .Stones(TestMaps.P0, "C3", "D3", "E3", "F3")
            .Stones(TestMaps.P1, "H3", "J3", "K3")
            .Stones(TestMaps.P2, "C10", "D10")
            .Stones(TestMaps.P3, "K10");

        Assert.Equal([8, 6, 5, 3], TestMaps.All.Select(p => (int)match.Scoreboard.Latest!.Of(p).Total));   // 上面的独立复算
        var last = new List<string>();
        var first = new List<string>();
        for (int round = 0; round < 5; round++)
        {
            match.Debug.SetOrder(TestMaps.P3, TestMaps.P0, TestMaps.P1, TestMaps.P2);
            Assert.Equal(TestMaps.P3, match.CurrentPlayer);
            Assert.Equal(4, match.Scoreboard.Latest!.RankOf(TestMaps.P3));
            match.BeginTurn();
            last.Add(Params(match.CurrentSnapshot!));
            match.EnterRecruit();
            match.CurrentHand().Pick(0);
            match.EnterDeploy();
            Assert.True(match.Confirm().Confirmed);   // P3 本回合 Pass，盘面不变，下一回合仍是最后一名

            // 第 1 名落 1 枚打断连续 Pass（4 连 Pass 会终局），其余两人 Pass
            Assert.Equal(1, match.Scoreboard.Latest!.RankOf(TestMaps.P0));
            match.BeginTurn();
            Assert.Equal(TestMaps.P0, match.CurrentPlayer);
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
