using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Core.Relics;

namespace Siege.Core.Tests.MatchSetup;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/match-setup —— Requirement: 匿名同时插旗、原型插旗替代路径（链接模式的盲选，实现裁决 B2）。
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
        Assert.Equal(10, b.ZoneCount);   // map-expand-25：link:1:4 浮空岛 10 块平台（原 5 块）
        // 公开视图不暴露任何旗数（retire-legacy-rules 第 3 组：旧模式的每区旗数字段删除，改为断言公开视图只有"是否已锁定"一项）。
        Assert.Equal([nameof(FlagPublicView.IsLocked)], typeof(FlagPublicView).GetProperties().Select(p => p.Name));
        Assert.Equal(new FlagPublicView(false), match.Flags.PublicView());
        Assert.Equal(0, match.Flags.ViewFor(LinkModeFixtures.P0).OwnZone);
        // retire-legacy-rules 1.7（清单"删反面段"）：原有的旧模式反面段（公开视图照旧给出每区旗数）随旧模式退役删除。
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
        // retire-legacy-rules 1.7（清单"删反面段"）：原有的旧模式反面段（锁定前仍可更换）随旧模式退役删除。
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
        Assert.Equal(new FlagPublicView(true), match.Flags.PublicView());   // 只有"已锁定"，没有旗数（retire-legacy-rules 第 3 组：原断言旗数字段为空）
        Assert.Equal(2, match.LockedFlagCounts[1]);
    }

    [Fact]
    public void 时限到时未选者由flag_pick均匀指定()
    {
        // 正式插旗：只有 P0 选了棋盘 3，时限到 → 其余三人取 flag-pick 子流里各自那一次（按编号升序每人一次），不看 P0 的旗。
        MatchFlow match = LinkModeFixtures.Create();
        match.Flags.Plant(LinkModeFixtures.P0, 3);
        match.LockFlags();

        int[] expected = ExpectedPicks(TestMaps.Seed, 4, LinkModeFixtures.Map.Boards.Length);   // map-expand-25：在全部平台（link:1:4 为 10 块）里均匀抽
        Assert.Equal(3, match.StateOf(LinkModeFixtures.P0).BirthZone);
        for (int i = 1; i < 4; i++)
        {
            Assert.Equal(expected[i], match.StateOf(LinkModeFixtures.All[i]).BirthZone);
        }
    }

    [Fact]
    public void 时限可配置()
    {
        // 裁决记录 1：默认 15 秒；单机 / AI 路径配置为立即，对局状态与正式插旗结果等价。
        // retire-legacy-rules 1.5：由「匿名同时插旗Tests」迁来（原文件整删）。载体由 9×9 旧模式夹具换为 link:1（公开建局恒为链接模式、盲选），断言不变。
        // 变异验证 M-S3：MatchOptions.Immediate 也用 15 秒 → 红 1（本测试）。
        MatchFlow online = LinkModeFixtures.Create(options: MatchOptions.Default);
        MatchFlow local = LinkModeFixtures.Create(options: MatchOptions.Immediate);
        Assert.Equal(TimeSpan.FromSeconds(15), online.Flags.TimeLimit);
        Assert.False(online.Flags.IsImmediate);
        Assert.True(local.Flags.IsImmediate);

        foreach (MatchFlow m in new[] { online, local })
        {
            m.Flags.Plant(TestMaps.P0, 2);
            m.Flags.Plant(TestMaps.P1, 0);
            m.Flags.Plant(TestMaps.P2, 3);
            m.Flags.Plant(TestMaps.P3, 1);
            m.LockFlags();
        }

        Assert.Equal(online.PlayerStates, local.PlayerStates);
        Assert.Equal(online.ActionOrder, local.ActionOrder);
        Assert.Equal(MatchPhase.InProgress, local.Phase);

        // S-14：锁定事件文本对人显示出生区 1–4（内部索引 2 / 0 / 3 / 1 → 3 / 1 / 4 / 2）。
        // 变异验证 M-D3：MatchFlow 锁定事件改回直接写内部索引 → 红 1（本断言）。
        FlowEvent locked = Assert.Single(local.Events, e => e.Kind == FlowEventKind.FlagsLocked);
        Assert.StartsWith("出生区 P0:3 P1:1 P2:4 P3:2；", locked.Detail, StringComparison.Ordinal);
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
        debug.PassTurn();
    }

    [Fact]
    public void AI均匀盲选可复现()
    {
        // Scenario：本机玩家选棋盘 2，其余三名 AI 自动插旗，同一对局种子重复开局两次 → 三名 AI 两次锁定的棋盘完全相同。
        // 钉住子流：AI 的结果等于 flag-pick 子流（字面量子流名）按编号升序每人一次的抽取。
        int[] once = PlantWithHuman(TestMaps.Seed, 1);
        int[] twice = PlantWithHuman(TestMaps.Seed, 1);
        Assert.Equal(once, twice);
        Assert.Equal(1, once[0]);
        int[] expected = ExpectedPicks(TestMaps.Seed, 4, LinkModeFixtures.Map.Boards.Length);
        Assert.Equal(expected[1..], once[1..]);

        // 均匀盲选：多个种子下 AI 会与人同区、彼此同区，每块棋盘都会被选到（不看已插的旗、不避让）。map-expand-25：link:1:4 为 10 块平台。
        var zones = new HashSet<int>();
        bool sharedWithHuman = false;
        for (ulong s = 1; s <= 40; s++)
        {
            // 只取选区的唯一实现（不建局，减轻并行负载）；建局路径与它同源由上面两条断言钉住。
            int[] picks = [.. PrototypeZoneAssignment.AssignBlind(LinkModeFixtures.Map, new GameSeed(s), LinkModeFixtures.All, (LinkModeFixtures.P0, 1)).Select(c => c.Zone)];
            zones.UnionWith(picks[1..]);
            sharedWithHuman |= picks[1..].Contains(1);
        }

        Assert.Equal(10, zones.Count);
        Assert.True(sharedWithHuman);
    }

    [Fact]
    public void 选区不扰动其他随机()
    {
        // Scenario：用某种子开局并改变本机玩家的人工选择 → 信物内容与首回合顺序逐项不变（AI 的选择也不变）。
        // 人工选择取遍全部棋盘：必有某次与某名 AI 同区，"同区时多消费一次别的子流"这类耦合才会被触发。
        // map-expand-25 段 A3：浮空岛不再放静态信物格（relic-redesign R29），本条测旧信物系统，载体改为 LinkModeFixtures.RelicMap（link:1:4 补上段 A2 口径信物格的手写夹具）。
        MatchFlow a = LinkModeFixtures.CreateWithRelics();
        a.PlantPrototype((LinkModeFixtures.P0, 0));
        // 裁决 J2：每块出生棋盘 2 枚出生区档。map-expand-25 D5：link:1:4 浮空岛 10 块平台 20 枚 + 低地 6 枚公共区档 = 26（重钉，原 13）。
        // 段 A3：生成图不再放信物格，26 枚来自 RelicMap 夹具（每块平台核心区对角 2 枚 + 低地 6 枚）。
        Assert.Equal(26, a.Relics.Coords.Count());
        for (int zone = 1; zone < a.Map.Boards.Length; zone++)
        {
            MatchFlow b = LinkModeFixtures.CreateWithRelics();
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
