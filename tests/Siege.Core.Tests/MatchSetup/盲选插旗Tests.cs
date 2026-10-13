using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Core.Relics;

namespace Siege.Core.Tests.MatchSetup;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/match-setup —— Requirement: 匿名同时插旗、原型插旗替代路径（链接模式的盲选，实现裁决 B2）。
/// 旧模式的插旗行为由既有 <c>匿名同时插旗Tests</c> / <c>原型插旗替代路径Tests</c> 原样守住。
/// </summary>
/// <remarks>
/// 变异验证（段 B 前半实跑）：
/// M-B3 <c>GameSeed.FlagPick</c> 改为 <c>"setup"</c> → 「AI均匀盲选可复现」「时限到时未选者由flag-pick均匀指定」红 2（钉住子流名）；
/// M-B6 <c>MatchFlow.PlantPrototype</c> 链接分支里对每个与人工同区的 AI 多抽一次 setup 子流（把选区与首回合顺序耦合）→ 「选区不扰动其他随机」红 1。
/// </remarks>
public class 盲选插旗Tests
{
    [Fact]
    public void 旗帜互不可见()
    {
        // Scenario：玩家 A 锁定棋盘 1，玩家 B 尚未选择 → B 看不到棋盘 1 上有旗。
        MatchFlow match = LinkModeFixtures.Create();
        match.Flags.Plant(LinkModeFixtures.P0, 0);

        SeatFlagView b = match.Flags.ViewFor(LinkModeFixtures.P1);
        Assert.Null(b.OwnZone);
        Assert.Empty(b.SameBoard);
        Assert.Equal(5, b.ZoneCount);
        // 公开视图不暴露任何旗数。
        Assert.Empty(match.Flags.PublicView().FlagCountByZone);
        Assert.Equal(0, match.Flags.ViewFor(LinkModeFixtures.P0).OwnZone);

        // 反面：旧模式公开视图照旧给出每区旗数。
        MatchFlow legacy = MatchFixtures.Create();
        legacy.Flags.Plant(MatchFixtures.P0, 0);
        Assert.Equal(1, legacy.Flags.PublicView().FlagsAt(0));
    }

    [Fact]
    public void 选中即锁定()
    {
        // Scenario：玩家选中棋盘 1 后尝试改选棋盘 3 → 系统拒绝，锁定结果仍为棋盘 1。
        MatchFlow match = LinkModeFixtures.Create();
        match.Flags.Plant(LinkModeFixtures.P0, 0);
        SiegeRuleException ex = Assert.Throws<SiegeRuleException>(() => match.Flags.Plant(LinkModeFixtures.P0, 2));
        Assert.Contains("选中即锁定", ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, match.Flags.FlagOf(LinkModeFixtures.P0));

        // 反面：旧模式锁定前仍可更换。
        MatchFlow legacy = MatchFixtures.Create();
        legacy.Flags.Plant(MatchFixtures.P0, 0);
        legacy.Flags.Plant(MatchFixtures.P0, 2);
        Assert.Equal(2, legacy.Flags.FlagOf(MatchFixtures.P0));
    }

    [Fact]
    public void 多人同区()
    {
        // Scenario：A 与 B 都锁定棋盘 2 → 接受；锁定后 A 与 B 各自看到对方在棋盘 2 上，其他玩家看不到棋盘 2 上有谁。
        MatchFlow match = LinkModeFixtures.Started([1, 1, 3, 4]);
        Assert.Equal(MatchPhase.InProgress, match.Phase);

        Assert.Equal([LinkModeFixtures.P0, LinkModeFixtures.P1], match.Flags.ViewFor(LinkModeFixtures.P0).SameBoard);
        Assert.Equal([LinkModeFixtures.P0, LinkModeFixtures.P1], match.Flags.ViewFor(LinkModeFixtures.P1).SameBoard);
        Assert.Equal([LinkModeFixtures.P2], match.Flags.ViewFor(LinkModeFixtures.P2).SameBoard);
        Assert.Empty(match.Flags.PublicView().FlagCountByZone);
        Assert.Equal(2, match.LockedFlagCounts[1]);
    }

    [Fact]
    public void 时限到时未选者由flag_pick均匀指定()
    {
        // 正式插旗：只有 P0 选了棋盘 3，时限到 → 其余三人取 flag-pick 子流里各自那一次（按编号升序每人一次），不看 P0 的旗。
        MatchFlow match = LinkModeFixtures.Create();
        match.Flags.Plant(LinkModeFixtures.P0, 3);
        match.LockFlags();

        int[] expected = ExpectedPicks(MatchFixtures.Seed, 4, 5);
        Assert.Equal(3, match.StateOf(LinkModeFixtures.P0).BirthZone);
        for (int i = 1; i < 4; i++)
        {
            Assert.Equal(expected[i], match.StateOf(LinkModeFixtures.All[i]).BirthZone);
        }
    }

    [Fact]
    public void 调试依次插旗()
    {
        // Scenario：在调试界面依次为 4 名玩家指定出生棋盘 → 对局进入与正式插旗结果等价的状态，可正常开始第 1 大回合。
        MatchFlow formal = LinkModeFixtures.Create();
        formal.Flags.Plant(LinkModeFixtures.P0, 2);
        formal.Flags.Plant(LinkModeFixtures.P1, 2);
        formal.Flags.Plant(LinkModeFixtures.P2, 0);
        formal.Flags.Plant(LinkModeFixtures.P3, 4);
        formal.LockFlags();
        MatchFlow debug = LinkModeFixtures.Started([2, 2, 0, 4]);

        Assert.Equal(formal.PlayerStates.Select(s => s.BirthZone), debug.PlayerStates.Select(s => s.BirthZone));
        Assert.Equal(formal.ActionOrder, debug.ActionOrder);
        Assert.Equal(1, debug.MajorRound);
        Assert.Equal(LinkStage.Building, debug.LinkStage);
        debug.PassTurn();
    }

    [Fact]
    public void AI均匀盲选可复现()
    {
        // Scenario：本机玩家选棋盘 2，其余三名 AI 自动插旗，同一对局种子重复开局两次 → 三名 AI 两次锁定的棋盘完全相同。
        // 钉住子流：AI 的结果等于 flag-pick 子流（字面量子流名）按编号升序每人一次的抽取。
        int[] once = PlantWithHuman(MatchFixtures.Seed, 1);
        int[] twice = PlantWithHuman(MatchFixtures.Seed, 1);
        Assert.Equal(once, twice);
        Assert.Equal(1, once[0]);
        int[] expected = ExpectedPicks(MatchFixtures.Seed, 4, 5);
        Assert.Equal(expected[1..], once[1..]);

        // 均匀盲选：多个种子下 AI 会与人同区、彼此同区，五块棋盘都会被选到（不看已插的旗、不避让）。
        var zones = new HashSet<int>();
        bool sharedWithHuman = false;
        for (ulong s = 1; s <= 40; s++)
        {
            // 只取选区的唯一实现（不建局，减轻并行负载）；建局路径与它同源由上面两条断言钉住。
            int[] picks = [.. PrototypeZoneAssignment.AssignBlind(LinkModeFixtures.Map, new GameSeed(s), LinkModeFixtures.All, (LinkModeFixtures.P0, 1)).Select(c => c.Zone)];
            zones.UnionWith(picks[1..]);
            sharedWithHuman |= picks[1..].Contains(1);
        }

        Assert.Equal(5, zones.Count);
        Assert.True(sharedWithHuman);
    }

    [Fact]
    public void 选区不扰动其他随机()
    {
        // Scenario：用某种子开局并改变本机玩家的人工选择 → 信物内容与首回合顺序逐项不变（AI 的选择也不变）。
        // 人工选择取遍 5 块棋盘：必有某次与某名 AI 同区，"同区时多消费一次别的子流"这类耦合才会被触发。
        MatchFlow a = LinkModeFixtures.Create();
        a.PlantPrototype((LinkModeFixtures.P0, 0));
        Assert.Equal(10, a.Relics.Coords.Count());   // 裁决 J2：5 块出生棋盘各 2 枚
        for (int zone = 1; zone < 5; zone++)
        {
            MatchFlow b = LinkModeFixtures.Create();
            b.PlantPrototype((LinkModeFixtures.P0, zone));

            Assert.Equal(RelicText(a), RelicText(b));
            Assert.Equal(a.ActionOrder, b.ActionOrder);
            Assert.Equal(a.PlayerStates.Skip(1).Select(s => s.BirthZone), b.PlayerStates.Skip(1).Select(s => s.BirthZone));
            Assert.Equal(zone, b.StateOf(LinkModeFixtures.P0).BirthZone);
        }
    }

    private static int[] PlantWithHuman(GameSeed seed, int humanZone)
    {
        MatchFlow match = LinkModeFixtures.Create(seed);
        return [.. match.PlantPrototype((LinkModeFixtures.P0, humanZone)).Select(c => c.Zone)];
    }

    private static int[] ExpectedPicks(GameSeed seed, int players, int zones)
    {
        RandomStream stream = seed.Stream("flag-pick");
        return [.. Enumerable.Range(0, players).Select(_ => stream.NextInt(zones))];
    }

    private static string RelicText(MatchFlow match) =>
        string.Join(";", match.Relics.Generation.Placements.Select(p => p.ToString()));
}
