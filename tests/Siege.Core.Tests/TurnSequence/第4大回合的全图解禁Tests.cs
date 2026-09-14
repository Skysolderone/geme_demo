using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;

namespace Siege.Core.Tests.TurnSequence;

/// <summary>规格：turn-sequence —— Requirement: 第 4 大回合的全图解禁</summary>
public class 第4大回合的全图解禁Tests
{
    [Fact]
    public void 解禁后全图可落子()
    {
        // 设计文档 §4.2：第 4 大回合在远离自己出生区的公共区暂放 → 接受。
        // 变异验证 M-T10：BuildProtectionRounds 改为 4 → 红 2（本测试 + 范围随大回合切换）。
        MatchFlow match = MatchFixtures.Started().AtRound(4, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3]);
        match.BeginTurn();
        match.EnterRecruit();
        StagedBatch batch = match.EnterDeploy();
        Assert.Null(batch.Stage(TestMaps.At("E5"), PieceType.Basic));
        Assert.Null(batch.Stage(TestMaps.At("H8"), PieceType.Basic));
        Assert.True(match.Confirm().Confirmed);
        Assert.Equal(MatchFixtures.P0, match.Board[TestMaps.At("H8")].Occupant!.Value.Owner);
    }

    [Fact]
    public void 空盘面玩家重新进入()
    {
        // 设计文档 §4.2 / 裁决记录 4：第 4 大回合盘面为空的玩家完成征募后可在全图任意合法空格重新落子，不加任何限制。
        // 变异验证 M-T11：LegalRangeFor 对盘面为空的玩家返回出生区 → 红 1（本测试）。
        MatchFlow match = MatchFixtures.Started().AtRound(4, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3]);
        match.Debug.SeedHand(MatchFixtures.P0);
        Assert.True(match.Hands.IsHandEmpty(MatchFixtures.P0));
        Assert.Equal(0, match.StoneCount(MatchFixtures.P0));

        match.BeginTurn();
        RecruitPanelView panel = match.EnterRecruit();
        PlayerHandAccess hand = match.CurrentHand();
        for (int i = 0; i < panel.FreePickCount; i++)
        {
            hand.Pick(hand.Panel().Candidates.First(c => c.IsSelectable).Index);
        }

        Assert.False(hand.PrivateView().IsEmpty);
        StagedBatch batch = match.EnterDeploy();
        PieceType type = hand.PrivateView().Types.First();
        Assert.Null(batch.Stage(TestMaps.At("H8"), type));   // 敌方出生区深处也允许
        Assert.True(match.Confirm().Confirmed);
        Assert.Equal(1, match.StoneCount(MatchFixtures.P0));
    }
}
