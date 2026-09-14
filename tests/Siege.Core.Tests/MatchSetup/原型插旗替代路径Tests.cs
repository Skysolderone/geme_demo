using Siege.Core.Board;
using Siege.Core.Match;

namespace Siege.Core.Tests.MatchSetup;

/// <summary>规格：match-setup —— Requirement: 原型插旗替代路径</summary>
public class 原型插旗替代路径Tests
{
    [Fact]
    public void 调试依次插旗()
    {
        // 设计文档 §4.1：原型可由 AI 或调试界面依次完成插旗，产出与正式插旗完全相同的对局状态（implement 1.4：逐字段一致）。
        // 变异验证 M-S8：PlantSequentially 不经 LockAll 而是另起一套顺序生成 → 红 1（本测试的存档逐字节比较）。
        MatchFlow simultaneous = MatchFixtures.Create(options: MatchOptions.Immediate);
        simultaneous.Flags.Plant(MatchFixtures.P2, 1);
        simultaneous.Flags.Plant(MatchFixtures.P0, 3);
        simultaneous.Flags.Plant(MatchFixtures.P3, 1);
        simultaneous.Flags.Plant(MatchFixtures.P1, 0);
        simultaneous.LockFlags();

        MatchFlow sequential = MatchFixtures.Create(options: MatchOptions.Immediate);
        sequential.PlantSequentially([(MatchFixtures.P0, 3), (MatchFixtures.P1, 0), (MatchFixtures.P2, 1), (MatchFixtures.P3, 1)]);

        Assert.Equal(MatchPhase.InProgress, sequential.Phase);
        Assert.Equal(1, sequential.MajorRound);
        Assert.Equal(simultaneous.Serialize(), sequential.Serialize());

        // 可正常开始第一大回合
        sequential.BeginTurn();
        Assert.Equal(TurnStage.OrganizeHand, sequential.Stage);
    }

    [Fact]
    public void 出生区锁定结果可持久化()
    {
        // 插旗完成后保存对局 → 每名玩家锁定的出生区编号被完整保存并可恢复。
        // 变异验证 M-S9：Serialize 不写 BirthZone → 红 1（本测试）。
        MatchFlow match = MatchFixtures.Create(options: MatchOptions.Immediate);
        match.PlantSequentially([(MatchFixtures.P0, 2), (MatchFixtures.P1, 2), (MatchFixtures.P2, 0), (MatchFixtures.P3, 3)]);

        MatchFlow restored = MatchFlow.RestoreUnvalidated(match.Map, match.Relics.Generation, match.Serialize());
        Assert.Equal(new int?[] { 2, 2, 0, 3 }, restored.PlayerStates.Select(s => s.BirthZone));
        Assert.Equal(new int?[] { 2, 2, 0, 3 }, MatchFixtures.All.Select(restored.Flags.FlagOf));
        Assert.True(restored.Flags.IsLocked);
        Assert.Equal(match.ActionOrder, restored.ActionOrder);
        Assert.Equal(match.LegalRangeFor(MatchFixtures.P1), restored.LegalRangeFor(MatchFixtures.P1));
    }
}
