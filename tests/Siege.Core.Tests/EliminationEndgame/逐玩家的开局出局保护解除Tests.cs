using Siege.Core.Match;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.EliminationEndgame;

/// <summary>规格：elimination-endgame —— Requirement: 逐玩家的开局出局保护解除</summary>
public class 逐玩家的开局出局保护解除Tests
{
    [Fact]
    public void 先手行动不误伤后手()
    {
        // 设计文档 §12.1：第 4 大回合先手玩家的批次清空了尚未行动的 C 的全部棋子且 C 手牌为空 → C 不出局，仍获得自己的小回合。
        // 这是 ROADMAP 三大陷阱之一的强制回归。
        // 变异验证 M-E2：BeginTurn / EndMajorRound 在 MajorRound >= 4 时全体解除保护 → 红 2（本测试 + 逐玩家解除）。
        MatchFlow match = MatchFixtures.Started().AtRound(4, [MatchFixtures.P0, MatchFixtures.P2, MatchFixtures.P1, MatchFixtures.P3])
            .Stones(MatchFixtures.P2, "E5")
            .Stones(MatchFixtures.P0, "E4", "D5", "F5");
        match.Debug.SeedHand(MatchFixtures.P2);

        match.PlayTurn("E6");   // P0 提掉 E5
        Assert.Equal(0, match.StoneCount(MatchFixtures.P2));
        Assert.True(match.Hands.IsHandEmpty(MatchFixtures.P2));
        Assert.Equal(PlayerStatus.Active, match.StateOf(MatchFixtures.P2).Status);
        Assert.True(match.StateOf(MatchFixtures.P2).HasOpeningProtection);

        Assert.Equal(MatchFixtures.P2, match.CurrentPlayer);
        match.BeginTurn();
        Assert.Equal(1, match.Events.Count(e => e.Kind == FlowEventKind.TurnStarted && e.Player == MatchFixtures.P2));
    }

    [Fact]
    public void 逐玩家解除()
    {
        // 设计文档 §12.1：第 4 大回合 A 已完成小回合、C 尚未行动 → A 的保护已解除，C 的仍然有效。
        // 变异验证 M-E3：CompleteTurn 解除保护时顺带解除全体 → 红 1（本测试）。
        MatchFlow match = MatchFixtures.Started().AtRound(4, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3]);
        Assert.All(MatchFixtures.All, p => Assert.True(match.StateOf(p).HasOpeningProtection));

        match.PlayTurn("E5");   // A = P0
        Assert.False(match.StateOf(MatchFixtures.P0).HasOpeningProtection);
        Assert.True(match.StateOf(MatchFixtures.P2).HasOpeningProtection);
        Assert.True(match.StateOf(MatchFixtures.P1).HasOpeningProtection);
        Assert.Equal(1, match.Events.Count(e => e.Kind == FlowEventKind.ProtectionLifted));
    }

    [Fact]
    public void 解除后即时检查()
    {
        // 设计文档 §12.1：A 已解除保护，此后某次结算使 A 的盘面与手牌同时为空 → A 在该次结算的出局检查中立即出局。
        // 变异验证 M-E4：OnCheckEndConditions 不调用 CheckEliminations（只在回合完成时查自己）→ 红 1（本测试）。
        MatchFlow match = MatchFixtures.Started().AtRound(5, [MatchFixtures.P1, MatchFixtures.P0, MatchFixtures.P2, MatchFixtures.P3])
            .Stones(MatchFixtures.P0, "A1")
            .Stones(MatchFixtures.P1, "B1");
        match.Debug.SeedHand(MatchFixtures.P0);
        Assert.False(match.StateOf(MatchFixtures.P0).HasOpeningProtection);

        match.PlayTurn("A2");   // P1 提掉 A1
        Assert.Equal(PlayerStatus.Eliminated, match.StateOf(MatchFixtures.P0).Status);
        Assert.Equal(1, match.StateOf(MatchFixtures.P0).EliminationOrder);
        Assert.Equal(MatchFixtures.P2, match.CurrentPlayer);
    }
}
