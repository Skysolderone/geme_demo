using Siege.Core.Match;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.EliminationEndgame;

/// <summary>规格：elimination-endgame —— Requirement: 保护期暂停出局检查</summary>
public class 保护期暂停出局检查Tests
{
    [Fact]
    public void 保护期内被清空不出局()
    {
        // 设计文档 §4.2：第 2 大回合某玩家全部棋子被围杀且手牌为空 → 不出局，下一小回合正常进入征募阶段。
        // 变异验证 M-E1：CheckEliminations 忽略 Protection → 红 3（本测试 + 先手行动不误伤后手 + 逐玩家解除）。
        MatchFlow match = MatchFixtures.Started(zones: [0, 0, 2, 3]).AtRound(2, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3])
            .Stones(MatchFixtures.P1, "A1");
        match.Debug.SeedHand(MatchFixtures.P1);

        match.PlayTurn("B1", "A2");   // P0 围杀 A1
        Assert.Equal(0, match.StoneCount(MatchFixtures.P1));
        Assert.True(match.Hands.IsHandEmpty(MatchFixtures.P1));
        Assert.Equal(PlayerStatus.Active, match.StateOf(MatchFixtures.P1).Status);
        Assert.True(match.StateOf(MatchFixtures.P1).HasOpeningProtection);

        Assert.Equal(MatchFixtures.P1, match.CurrentPlayer);
        match.BeginTurn();
        Assert.NotNull(match.EnterRecruit());
        Assert.Equal(TurnStage.Recruit, match.Stage);
    }
}
