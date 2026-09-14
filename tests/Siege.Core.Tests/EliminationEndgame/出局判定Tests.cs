using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.EliminationEndgame;

/// <summary>规格：elimination-endgame —— Requirement: 出局判定</summary>
public class 出局判定Tests
{
    [Fact]
    public void 两个条件都满足才出局()
    {
        // 设计文档 §12.1：已解除保护、盘面无棋子但手牌仍有 2 枚 → 不出局。
        // 变异验证 M-E5：CheckEliminationOf 改为 `||` → 红 1（本测试）。
        MatchFlow match = MatchFixtures.Started().AtRound(5, [MatchFixtures.P1, MatchFixtures.P0, MatchFixtures.P2, MatchFixtures.P3]);
        match.Debug.SeedHand(MatchFixtures.P0, (PieceType.Basic, 2));
        Assert.Equal(0, match.StoneCount(MatchFixtures.P0));

        match.PlayTurn("E5");   // P1 结算 → 检查
        Assert.Equal(PlayerStatus.Active, match.StateOf(MatchFixtures.P0).Status);
    }

    [Fact]
    public void 出局立即生效()
    {
        // 设计文档 §12.1：已解除保护的玩家在一次结算后盘面与手牌同时为空 → 立即出局，后续大回合的行动序列不再包含他。
        // 变异验证 M-E6：Eliminate 只记事件不改 Status → 红 1（本测试）。
        MatchFlow match = MatchFixtures.Started().AtRound(5, [MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3, MatchFixtures.P0]);
        match.Debug.SeedHand(MatchFixtures.P0);

        match.PlayTurn("E5");   // P1
        Assert.Equal(PlayerStatus.Eliminated, match.StateOf(MatchFixtures.P0).Status);
        Assert.Equal(5, match.StateOf(MatchFixtures.P0).EliminatedInMajorRound);
        Assert.Equal(1, match.CountEvents(FlowEventKind.PlayerEliminated));

        match.PlayTurn("B2");   // P2
        match.PlayTurn("H8");   // P3 → 本大回合结束（P0 被跳过）
        Assert.Equal(6, match.MajorRound);
        Assert.DoesNotContain(MatchFixtures.P0, match.ActionOrder);
        Assert.Equal(0, match.Events.Count(e => e.Kind == FlowEventKind.TurnStarted && e.Player == MatchFixtures.P0));
    }

    [Fact]
    public void Pass后也检查()
    {
        // 设计文档 §12.1：Pass 后盘面与手牌同时为空且已解除保护 → 出局检查在该次 Pass 完成后执行。
        // 变异验证 M-E7：OnCheckEndConditions 在 IsPass 时跳过 CheckEliminations → 红 1（本测试）。
        MatchFlow match = MatchFixtures.Started().AtRound(5, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3]);
        match.Debug.SeedHand(MatchFixtures.P0);

        match.PassTurn();
        Assert.Equal(PlayerStatus.Eliminated, match.StateOf(MatchFixtures.P0).Status);
        int passIndex = match.Events.ToList().FindIndex(e => e.Kind == FlowEventKind.TurnEnded && e.Player == MatchFixtures.P0);
        int elimIndex = match.Events.ToList().FindIndex(e => e.Kind == FlowEventKind.PlayerEliminated);
        Assert.True(elimIndex >= 0 && elimIndex < passIndex, "出局应在 Pass 结算内、小回合结束前判定");
        Assert.Equal(MatchFixtures.P1, match.CurrentPlayer);
    }
}
