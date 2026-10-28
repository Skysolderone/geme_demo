using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Core.PieceRelics;

namespace Siege.Core.Tests.MatchSetup;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/match-setup —— Requirement: 匿名同时插旗、原型插旗替代路径（链接模式的盲选，实现裁决 B2）。
/// </summary>
/// <remarks>
/// 变异验证（段 B 前半实跑）：
/// M-B3 <c>GameSeed.FlagPick</c> 改为 <c>"setup"</c> → 「AI均匀盲选可复现」「时限到时未选者由flag-pick均匀指定」红 2（钉住子流名）；
/// M-B6 <c>MatchFlow.PlantPrototype</c> 链接分支里对每个与人工同区的 AI 多抽一次 setup 子流（把选区与首回合顺序耦合）→ 「选区不扰动其他随机」红 1。
/// seat-gap-25 E4（裁决 S2）：座位互斥——锁定时按插旗先后，撞座位的后插旗者与未插旗者按编号升序、各从 flag-pick 子流在当前空座位里抽一次（期望值用测试内独立算式 <see cref="ExpectedSeats"/>）。
/// 变异 M-S5（seat-gap-25 实跑）：BlindPicks 改为在全部座位里抽（不避开已占座位）→ 「AI均匀盲选可复现」「撞座位按插旗先后判定」「时限到时未选者只落空座位」「调试依次插旗」「选区不扰动其他随机」红 5。
/// 原「多人同区」「AI 均匀盲选会与人同区」的口径作废，改为「撞座位时后插旗者改派到空座位」「未选者只落空座位」；link:1:4 换图后只有 4 个座位（原 10 块平台）。
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
        Assert.Equal(4, b.ZoneCount);   // seat-gap-25：link:1:4 恰 4 个出生座位（map-expand-25 时为 10 块平台，更早为 5 块）
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
    public void 撞座位时后插旗者改派到空座位()
    {
        // seat-gap-25 E4（原 Scenario「多人同区」作废）：P1 先插座位 2、P0 后插同一座位、P2 插座位 4、P3 未插 → 锁定后 P1 保住座位 2；
        // P0（撞座位）与 P3（未插旗）按编号升序各从 flag-pick 子流在当前空座位（座位 1 / 3）里抽一次——四人各占一个座位，同座只剩本人。
        // 插旗时仍接受选别人已选的座位（盲选不泄露信息）。
        MatchFlow match = LinkModeFixtures.Create();
        match.Flags.Plant(LinkModeFixtures.P1, 1);
        match.Flags.Plant(LinkModeFixtures.P0, 1);
        match.Flags.Plant(LinkModeFixtures.P2, 3);
        Assert.Equal(1, match.Flags.FlagOf(LinkModeFixtures.P0));   // 锁定前自己看到的仍是自己插的旗
        match.LockFlags();
        Assert.Equal(MatchPhase.InProgress, match.Phase);

        int[] expected = ExpectedSeats(TestMaps.Seed, 4, [(1, 1), (0, 1), (2, 3)]);
        Assert.Equal(1, expected[1]);
        Assert.Equal(3, expected[2]);
        Assert.Equal(expected, match.PlayerStates.Select(st => st.BirthZone!.Value));
        Assert.Equal(4, expected.Distinct().Count());
        foreach (PlayerId p in LinkModeFixtures.All)
        {
            Assert.Equal([p], match.Flags.ViewFor(p).SameBoard);
            Assert.Equal(match.StateOf(p).BirthZone, match.Flags.ViewFor(p).OwnZone);
        }

        Assert.Equal(new FlagPublicView(true), match.Flags.PublicView());   // 只有"已锁定"，没有旗数（retire-legacy-rules 第 3 组：原断言旗数字段为空）
        Assert.All(match.LockedFlagCounts.Values, c => Assert.Equal(1, c));
    }

    [Fact]
    public void 撞座位按插旗先后判定()
    {
        // seat-gap-25 E4：同一对局种子、同样两人插同一座位，只是先后相反 → 先插者保住座位，后插者被改派；同种子同先后两次结果相同。
        static int[] Run(params (PlayerId Player, int Zone)[] plants)
        {
            MatchFlow match = LinkModeFixtures.Create();
            foreach ((PlayerId player, int zone) in plants)
            {
                match.Flags.Plant(player, zone);
            }

            match.LockFlags();
            return [.. match.PlayerStates.Select(st => st.BirthZone!.Value)];
        }

        int[] p0First = Run((LinkModeFixtures.P0, 2), (LinkModeFixtures.P1, 2));
        int[] p1First = Run((LinkModeFixtures.P1, 2), (LinkModeFixtures.P0, 2));
        Assert.Equal(2, p0First[0]);
        Assert.NotEqual(2, p0First[1]);
        Assert.Equal(2, p1First[1]);
        Assert.NotEqual(2, p1First[0]);
        Assert.Equal(p0First, Run((LinkModeFixtures.P0, 2), (LinkModeFixtures.P1, 2)));
        Assert.Equal(ExpectedSeats(TestMaps.Seed, 4, [(0, 2), (1, 2)]), p0First);
        Assert.Equal(ExpectedSeats(TestMaps.Seed, 4, [(1, 2), (0, 2)]), p1First);
    }

    [Fact]
    public void 时限到时未选者只落空座位()
    {
        // 正式插旗：只有 P0 选了座位 4，时限到 → 其余三人按编号升序各从 flag-pick 子流在当前空座位里抽一次（seat-gap-25 E4：原为在全部平台里各抽一次、可与 P0 同区）。
        MatchFlow match = LinkModeFixtures.Create();
        match.Flags.Plant(LinkModeFixtures.P0, 3);
        match.LockFlags();

        int[] expected = ExpectedSeats(TestMaps.Seed, 4, [(0, 3)]);
        Assert.Equal(3, match.StateOf(LinkModeFixtures.P0).BirthZone);
        Assert.Equal(expected, match.PlayerStates.Select(st => st.BirthZone!.Value));
        Assert.Equal([0, 1, 2, 3], expected.Order());
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
        // seat-gap-25 E4：依次插旗与同时插旗走同一个锁定（含撞座位改派）——P0 / P1 撞座位 3，两条路径改派结果相同。
        MatchFlow formal = LinkModeFixtures.Create();
        formal.Flags.Plant(LinkModeFixtures.P0, 2);
        formal.Flags.Plant(LinkModeFixtures.P1, 2);
        formal.Flags.Plant(LinkModeFixtures.P2, 0);
        formal.Flags.Plant(LinkModeFixtures.P3, 3);
        formal.LockFlags();
        MatchFlow debug = LinkModeFixtures.Create();
        debug.PlantSequentially([(LinkModeFixtures.P0, 2), (LinkModeFixtures.P1, 2), (LinkModeFixtures.P2, 0), (LinkModeFixtures.P3, 3)]);

        Assert.Equal(formal.PlayerStates.Select(s => s.BirthZone), debug.PlayerStates.Select(s => s.BirthZone));
        Assert.Equal(4, debug.PlayerStates.Select(s => s.BirthZone).Distinct().Count());
        Assert.Equal(formal.ActionOrder, debug.ActionOrder);
        Assert.Equal(1, debug.MajorRound);
        debug.PassTurn();
    }

    [Fact]
    public void AI均匀盲选可复现()
    {
        // Scenario：本机玩家选棋盘 2，其余三名 AI 自动插旗，同一对局种子重复开局两次 → 三名 AI 两次锁定的棋盘完全相同。
        // 钉住子流：AI 的结果等于 flag-pick 子流（字面量子流名）按编号升序每人一次、在当前空座位里的抽取（seat-gap-25 E4）。
        int[] once = PlantWithHuman(TestMaps.Seed, 1);
        int[] twice = PlantWithHuman(TestMaps.Seed, 1);
        Assert.Equal(once, twice);
        Assert.Equal(1, once[0]);
        Assert.Equal(ExpectedSeats(TestMaps.Seed, 4, [(0, 1)]), once);

        // 盲选只落空座位：多个种子下 AI 从不与人或彼此同座，人没选的三个座位都会被 AI 坐到（原 map-expand-25 口径"会与人同区、10 块平台都被选到"作废）。
        var zones = new HashSet<int>();
        for (ulong s = 1; s <= 40; s++)
        {
            // 只取选区的唯一实现（不建局，减轻并行负载）；建局路径与它同源由上面两条断言钉住。
            int[] picks = [.. PrototypeZoneAssignment.AssignBlind(LinkModeFixtures.Map, new GameSeed(s), LinkModeFixtures.All, (LinkModeFixtures.P0, 1)).Select(c => c.Zone)];
            Assert.Equal(4, picks.Distinct().Count());
            Assert.Equal(1, picks[0]);
            zones.UnionWith(picks[1..]);
        }

        Assert.Equal([0, 2, 3], zones.Order());
    }

    [Fact]
    public void 选区不扰动其他随机()
    {
        // Scenario：用某种子开局并改变本机玩家的人工选择 → 信物刷新与首回合顺序逐项不变。
        // seat-gap-25 E4：AI 只落空座位，人选哪个座位会改变 AI 落在哪（原断言"AI 的选择也不变"作废），改为断言四人座位互不相同。
        // relic-redesign W1：开局信物生成随旧信物删除，改看开局盘面上第 3 大回合的预告（子流 relic-spawn:4）；原载体 RelicMap 随之删除。
        MatchFlow a = LinkModeFixtures.Create();
        a.PlantPrototype((LinkModeFixtures.P0, 0));
        Assert.Equal(2, RelicSpawner.Forecast(a.Board, new BirthBoardArea(a.Map), a.Seed, 3, a.Players.Length, 0)!.Slots.Length);   // 样本口径：⌈4/2⌉ = 2 件
        for (int zone = 1; zone < a.Map.BirthZones.Length; zone++)
        {
            MatchFlow b = LinkModeFixtures.Create();
            b.PlantPrototype((LinkModeFixtures.P0, zone));

            Assert.Equal(RelicText(a), RelicText(b));
            Assert.Equal(a.ActionOrder, b.ActionOrder);
            Assert.Equal(4, b.PlayerStates.Select(s => s.BirthZone).Distinct().Count());
            Assert.Equal(zone, b.StateOf(LinkModeFixtures.P0).BirthZone);
        }
    }

    private static int[] PlantWithHuman(GameSeed seed, int humanZone)
    {
        MatchFlow match = LinkModeFixtures.Create(seed);
        return [.. match.PlantPrototype((LinkModeFixtures.P0, humanZone)).Select(c => c.Zone)];
    }

    /// <summary>
    /// 锁定后各玩家（P0..P(n−1)）的座位（seat-gap-25 E4，测试内独立算式）：<paramref name="plants"/> 按插旗先后给出（玩家编号, 座位）；先到先得，
    /// 撞座位者与未插旗者合并、按编号升序，各从 flag-pick 子流抽 <c>NextInt(空座位数)</c>，在升序的空座位里取。座位数取 <see cref="LinkModeFixtures.Map"/> 的出生区数。
    /// </summary>
    private static int[] ExpectedSeats(GameSeed seed, int players, (int Player, int Zone)[] plants)
    {
        int seats = LinkModeFixtures.Map.BirthZones.Length;
        int?[] result = new int?[players];
        var pending = new List<int>();
        foreach ((int player, int zone) in plants)
        {
            if (result.Contains(zone))
            {
                pending.Add(player);
            }
            else
            {
                result[player] = zone;
            }
        }

        pending.AddRange(Enumerable.Range(0, players).Where(p => !plants.Any(x => x.Player == p)));
        RandomStream stream = seed.Stream("flag-pick");
        foreach (int player in pending.Order())
        {
            int[] empty = [.. Enumerable.Range(0, seats).Where(z => !result.Contains(z))];
            result[player] = empty[stream.NextInt(empty.Length)];
        }

        return [.. result.Select(z => z!.Value)];
    }

    private static string RelicText(MatchFlow match) =>
        string.Join(";", RelicSpawner.Forecast(match.Board, new BirthBoardArea(match.Map), match.Seed, 3, match.Players.Length, 0)!.Slots);
}
