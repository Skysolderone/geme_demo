using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Relics;

namespace Siege.Core.Tests.TurnSequence;

/// <summary>规格：turn-sequence —— Requirement: 构筑保护期的落子限制</summary>
public class 构筑保护期的落子限制Tests
{
    [Fact]
    public void 保护期内越区落子被拒()
    {
        // 设计文档 §4.2：第 2 大回合在自己出生区之外暂放 → 拒绝并说明违反当前合法落子范围。
        // 变异验证 M-T8：LegalRangeFor 恒返回全图 → 红 2（本测试 + 范围随大回合切换）。
        MatchFlow match = MatchFixtures.Started().AtRound(2, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3]);
        match.BeginTurn();
        match.EnterRecruit();
        StagedBatch batch = match.EnterDeploy();

        BatchFailure? failure = batch.Stage(TestMaps.At("E5"), PieceType.Basic);
        Assert.NotNull(failure);
        Assert.Equal(BatchFailureKind.OutOfLegalRange, failure.Kind);
        Assert.Contains("合法落子范围", failure.Message);
        Assert.Null(batch.Stage(TestMaps.At("B2"), PieceType.Basic));
    }

    [Fact]
    public void 保护期内规则照常()
    {
        // 设计文档 §4.2：第 3 大回合两名共享出生区的玩家互相围杀 → 提子、覆盖重算、信物揭示与势力更新全部正常执行。
        // 变异验证 M-T9：OnRevealRelics 在保护期内跳过揭示 → 红 1（本测试）。
        MatchFlow match = MatchFixtures.Started(zones: [0, 0, 2, 3], relics: [("B2", RelicFixtures.Depot())])
            .AtRound(3, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3])
            .Stones(MatchFixtures.P1, "A1");
        Assert.False(match.Relics.IsRevealed(TestMaps.At("B2")));

        match.PlayTurn("B1", "A2");

        Assert.Null(match.Board[TestMaps.At("A1")].Occupant);                       // 提子
        Assert.True(match.Relics.IsRevealed(TestMaps.At("B2")));                     // 信物揭示
        Assert.Equal(RelicControlKind.Controlled, match.Relics.ControlOf(TestMaps.At("B2")).Kind);
        Assert.Contains(TestMaps.At("A1"), match.Scoreboard.Latest!.Of(MatchFixtures.P0).ExclusiveCells);   // 覆盖重算
        Assert.Equal(0, match.Scoreboard.Latest.Of(MatchFixtures.P1).Total);                                // 势力更新
        Assert.True(match.Scoreboard.Latest.Of(MatchFixtures.P0).Total > 0);
    }
}
