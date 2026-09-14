using Siege.Core.Board;
using Siege.Core.Match;

namespace Siege.Core.Tests.MatchSetup;

/// <summary>规格：match-setup —— Requirement: 匿名同时插旗</summary>
public class 匿名同时插旗Tests
{
    [Fact]
    public void 身份匿名()
    {
        // 设计文档 §4.1：旗帜只显示位置，不显示玩家身份。
        // 变异验证 M-S2：FlagPublicView 增加 Owners 字段 → 红 1（本测试的反射断言）。
        MatchFlow match = MatchFixtures.Create();
        match.Flags.Plant(MatchFixtures.P0, 1);

        FlagPublicView view = match.Flags.PublicView();
        Assert.Equal(1, view.FlagsAt(1));
        Assert.Equal(0, view.FlagsAt(0));
        Assert.False(view.IsLocked);

        foreach (System.Reflection.PropertyInfo p in typeof(FlagPublicView).GetProperties())
        {
            Assert.DoesNotContain("Player", p.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Owner", p.Name, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(typeof(PlayerId), p.PropertyType);
        }
    }

    [Fact]
    public void 时限可配置()
    {
        // 裁决记录 1：默认 15 秒；单机 / AI 路径配置为立即，对局状态与正式插旗结果等价。
        // 变异验证 M-S3：MatchOptions.Immediate 也用 15 秒 → 红 1（本测试）。
        MatchFlow online = MatchFixtures.Create(options: MatchOptions.Default);
        MatchFlow local = MatchFixtures.Create(options: MatchOptions.Immediate);
        Assert.Equal(TimeSpan.FromSeconds(15), online.Flags.TimeLimit);
        Assert.False(online.Flags.IsImmediate);
        Assert.True(local.Flags.IsImmediate);

        foreach (MatchFlow m in new[] { online, local })
        {
            m.Flags.Plant(MatchFixtures.P0, 2);
            m.Flags.Plant(MatchFixtures.P1, 0);
            m.Flags.Plant(MatchFixtures.P2, 3);
            m.Flags.Plant(MatchFixtures.P3, 1);
            m.LockFlags();
        }

        Assert.Equal(online.PlayerStates, local.PlayerStates);
        Assert.Equal(online.ActionOrder, local.ActionOrder);
        Assert.Equal(MatchPhase.InProgress, local.Phase);
    }

    [Fact]
    public void 锁定前可更换()
    {
        // 设计文档 §4.1 算例：A 先选区 1 再改区 3 → 锁定为区 3。
        // 变异验证 M-S4：Plant 对已插旗玩家忽略第二次 → 红 1（本测试）。
        MatchFlow match = MatchFixtures.Create();
        match.Flags.Plant(MatchFixtures.P0, 1);
        match.Flags.Plant(MatchFixtures.P0, 3);
        Assert.Equal(1, match.Flags.PublicView().FlagsAt(3));
        Assert.Equal(0, match.Flags.PublicView().FlagsAt(1));

        match.LockFlags();
        Assert.Equal(3, match.StateOf(MatchFixtures.P0).BirthZone);
        Assert.Throws<SiegeRuleException>(() => match.Flags.Plant(MatchFixtures.P0, 0));
    }

    [Fact]
    public void 多人同区()
    {
        // 设计文档 §4.1 / §4.2：A 与 B 都锁定出生区 2 → 接受，保护期内共享合法落子范围。
        // 变异验证 M-S5：Plant 拒绝已被占用的出生区 → 红 1（本测试）。
        MatchFlow match = MatchFixtures.Create(options: MatchOptions.Immediate);
        match.Flags.Plant(MatchFixtures.P0, 2);
        match.Flags.Plant(MatchFixtures.P1, 2);
        match.Flags.Plant(MatchFixtures.P2, 0);
        match.Flags.Plant(MatchFixtures.P3, 1);
        match.LockFlags();

        Assert.Equal(2, match.StateOf(MatchFixtures.P0).BirthZone);
        Assert.Equal(2, match.StateOf(MatchFixtures.P1).BirthZone);
        Assert.Equal(match.Map.BirthZones[2], match.LegalRangeFor(MatchFixtures.P0));
        Assert.Equal(match.LegalRangeFor(MatchFixtures.P0), match.LegalRangeFor(MatchFixtures.P1));
    }
}
