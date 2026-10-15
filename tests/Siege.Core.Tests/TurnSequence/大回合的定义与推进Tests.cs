using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.TurnSequence;

/// <summary>规格：turn-sequence —— Requirement: 大回合的定义与推进</summary>
public class 大回合的定义与推进Tests
{
    [Fact]
    public void 大回合完成()
    {
        // 设计文档 §11：4 名参赛玩家依次完成各自的小回合 → 当前大回合结束，生成下一大回合的行动顺序。
        // 变异验证 M-T5：EnsureCurrentActive 在越过末位时不调用 EndMajorRound 而是回绕 → 红 1（本测试）。
        MatchFlow match = LinkModeFixtures.PlainStarted();
        PlayerId[] order = [.. match.ActionOrder];
        string[] zoneCell = ["D4", "K4", "D10", "K10"];
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(order[i], match.CurrentPlayer);
            Assert.Equal(1, match.MajorRound);
            match.PlayTurn(zoneCell[order[i].Value]);
        }

        Assert.Equal(2, match.MajorRound);
        Assert.Single(match.InitiativeReports);
        Assert.Equal(1, match.CountEvents(FlowEventKind.MajorRoundEnded));
        Assert.Equal(match.InitiativeReports[0].NextOrder, match.ActionOrder);
        Assert.Equal(TestMaps.All.Order(), match.ActionOrder.Order());
    }

    [Fact]
    public void 出局者不再获得小回合()
    {
        // 设计文档 §12.1：某玩家在第 6 大回合出局 → 第 7 大回合的行动序列中不包含该玩家。
        // 变异验证 M-T6：EndMajorRound 用 _players 而不是参赛者生成下一轮 → 红 1（本测试）。
        // 段 C 改摆法：出局判据改为"曾建立正势力且势力归零"——P2 先有 C3（标记置位），被 P0 的 C4 提光（原为 P2 盘面与手牌皆空、P0 落 E5）。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(6, [TestMaps.P0, TestMaps.P1, TestMaps.P2, TestMaps.P3])
            .Stones(TestMaps.P2, "C3")
            .Stones(TestMaps.P0, "D3");

        match.PlayTurn("C4");
        Assert.Equal(PlayerStatus.Eliminated, match.StateOf(TestMaps.P2).Status);
        Assert.Equal(6, match.StateOf(TestMaps.P2).EliminatedInMajorRound);

        match.PlayTurn("C7");   // P1
        Assert.Equal(TestMaps.P3, match.CurrentPlayer);   // P2 被跳过
        match.PlayTurn("K7");   // P3
        Assert.Equal(7, match.MajorRound);
        Assert.DoesNotContain(TestMaps.P2, match.ActionOrder);
        Assert.Equal(3, match.ActionOrder.Length);
        Assert.Equal(0, match.Events.Count(e => e.Kind == FlowEventKind.TurnStarted && e.Player == TestMaps.P2));
    }

    [Fact]
    public void 无固定轮数()
    {
        // 设计文档 §11 / §18.2：对局进行到第 15 大回合且终局条件均未满足 → 继续。
        // 变异验证 M-T7：EndMajorRound 在 completed >= 15 时 Finish → 红 1（本测试）。
        // round-cap：标准局上限 15 起，第 15 大回合结束即达上限终局；"无固定轮数"改由上限 0（不设上限）表达（§18.2：上限是兜底，0 保留原行为）。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(15, [TestMaps.P0, TestMaps.P1, TestMaps.P2, TestMaps.P3]);
        match.PlayTurn("D4");
        match.PlayTurn("K4");
        match.PlayTurn("D10");
        match.PlayTurn("K10");
        Assert.Equal(16, match.MajorRound);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Null(match.Result);
    }
}
