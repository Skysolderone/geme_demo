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
        MatchFlow match = MatchFixtures.Started();
        PlayerId[] order = [.. match.ActionOrder];
        string[] zoneCell = ["B2", "H2", "B8", "H8"];
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
        Assert.Equal(MatchFixtures.All.Order(), match.ActionOrder.Order());
    }

    [Fact]
    public void 出局者不再获得小回合()
    {
        // 设计文档 §12.1：某玩家在第 6 大回合出局 → 第 7 大回合的行动序列中不包含该玩家。
        // 变异验证 M-T6：EndMajorRound 用 _players 而不是参赛者生成下一轮 → 红 1（本测试）。
        MatchFlow match = MatchFixtures.Started().AtRound(6, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3]);
        match.Debug.SeedHand(MatchFixtures.P2);
        Assert.True(match.Hands.IsHandEmpty(MatchFixtures.P2));

        match.PlayTurn("E5");
        Assert.Equal(PlayerStatus.Eliminated, match.StateOf(MatchFixtures.P2).Status);
        Assert.Equal(6, match.StateOf(MatchFixtures.P2).EliminatedInMajorRound);

        match.PlayTurn("A5");   // P1
        Assert.Equal(MatchFixtures.P3, match.CurrentPlayer);   // P2 被跳过
        match.PlayTurn("J5");   // P3
        Assert.Equal(7, match.MajorRound);
        Assert.DoesNotContain(MatchFixtures.P2, match.ActionOrder);
        Assert.Equal(3, match.ActionOrder.Length);
        Assert.Equal(0, match.Events.Count(e => e.Kind == FlowEventKind.TurnStarted && e.Player == MatchFixtures.P2));
    }

    [Fact]
    public void 无固定轮数()
    {
        // 设计文档 §11 / §18.2：对局进行到第 15 大回合且终局条件均未满足 → 继续。
        // 变异验证 M-T7：EndMajorRound 在 completed >= 15 时 Finish → 红 1（本测试）。
        // round-cap：标准局上限 15 起，第 15 大回合结束即达上限终局；"无固定轮数"改由上限 0（不设上限）表达（§18.2：上限是兜底，0 保留原行为）。
        MatchFlow match = MatchFixtures.Started(options: MatchOptions.Immediate with { MaxMajorRounds = 0 }).AtRound(15, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3]);
        match.PlayTurn("B2");
        match.PlayTurn("H2");
        match.PlayTurn("B8");
        match.PlayTurn("H8");
        Assert.Equal(16, match.MajorRound);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Null(match.Result);
    }
}
