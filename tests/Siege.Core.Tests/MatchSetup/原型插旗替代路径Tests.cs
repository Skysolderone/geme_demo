using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
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

        MatchFlow restored = MatchFlow.RestoreUnvalidated(match.Board.BaseMap, match.Relics.Generation, match.Serialize());
        Assert.Equal(new int?[] { 2, 2, 0, 3 }, restored.PlayerStates.Select(s => s.BirthZone));
        Assert.Equal(new int?[] { 2, 2, 0, 3 }, MatchFixtures.All.Select(restored.Flags.FlagOf));
        Assert.True(restored.Flags.IsLocked);
        Assert.Equal(match.ActionOrder, restored.ActionOrder);
        Assert.Equal(match.LegalRangeFor(MatchFixtures.P1), restored.LegalRangeFor(MatchFixtures.P1));
    }

    // ---------- frontier-map D4 / 裁决 10：AI 选区收拢为一份逻辑 ----------
    // 下面带「黄金值」的期望全部取自引入 PrototypeZoneAssignment 之前的代码（提交 a572877）的实际运行结果：
    // 批量侧 MatchSession.Create（P<i> → 区 i % 区数）与终端版 PlayCommand（顺排跳过人选）的输出，抓取后写成字面量。
    // 它们不是用现在的实现算出来的——否则就是"测试抄实现"。

    [Fact]
    public void 区数等于人数时保持现状()
    {
        // 规格 Scenario：4 个出生区的地图上，本机玩家选 2 号区，其余三名 AI 按编号顺序占用 1、3、4 号区。
        // 变异 M-A13：Sequential 里去掉"取到人选的区就跳过" → 红（AI 会与人同区）。
        MatchFlow match = MatchFlow.Create(FourPlayerBaseMap.Create(), new GameSeed(42), MatchFixtures.All, MatchOptions.Immediate);

        var choices = match.PlantPrototype((MatchFixtures.P1, 1));

        Assert.Equal(new[] { (MatchFixtures.P0, 0), (MatchFixtures.P1, 1), (MatchFixtures.P2, 2), (MatchFixtures.P3, 3) }, choices.ToArray());
        Assert.Equal(new int?[] { 0, 1, 2, 3 }, match.PlayerStates.Select(s => s.BirthZone));
        Assert.Equal(new[] { 1, 3, 4 }, choices.Where(c => c.Player != MatchFixtures.P1).Select(c => BirthZoneLabel.Number(c.Zone)));
        Assert.Equal(MatchPhase.InProgress, match.Phase);
    }

    [Theory]
    // 黄金值（改动前终端版转录的"出生区锁定"行，区号已换回 0 起）：
    [InlineData(42UL, 4, 1, 0, new[] { 0, 1, 2, 3 })]   // 人坐 1 号位选 1 号区
    [InlineData(42UL, 4, 2, 0, new[] { 1, 0, 2, 3 })]   // 人坐 2 号位选 1 号区：玩家 1 被挤到 2 号区
    [InlineData(7UL, 4, 4, 2, new[] { 0, 1, 3, 2 })]    // 人坐末位选 3 号区：玩家 3 跳过它取 4 号区
    [InlineData(7UL, 4, 1, 3, new[] { 3, 0, 1, 2 })]    // 人选最后一个区：其余从头顺排（手算：next 从 0 起，永远碰不到 3）
    [InlineData(7UL, 3, 2, 3, new[] { 0, 3, 1 })]       // v4 上 3 人局：区数 4 > 参赛 3 人，仍是顺排（判据是地图人数上限，不是参赛人数）
    [InlineData(9UL, 2, 1, 1, new[] { 1, 0 })]          // v4 上 2 人局
    public void 标准图上人工选区后的顺排与改动前逐项相同(ulong seed, int players, int seat, int zone, int[] expected)
    {
        // 变异 M-A21：判据由"区数 > 地图人数上限"改成"区数 > 参赛人数" → v4 上的 2 / 3 人局变成种子选区，本测试与批量侧那条共红 5。
        PlayerId[] ids = [.. Enumerable.Range(0, players).Select(i => new PlayerId(i))];
        MatchFlow match = MatchFlow.Create(FourPlayerBaseMap.Create(), new GameSeed(seed), ids, MatchOptions.Immediate);

        match.PlantPrototype((ids[seat - 1], zone));

        Assert.Equal(expected.Select(z => (int?)z), match.PlayerStates.Select(s => s.BirthZone));
    }

    [Theory]
    // 黄金值（改动前批量侧 MatchSession.Create 的实际结果）：无人工选择时 P<i> → 区 i；首回合顺序只取决于种子与人数。
    [InlineData(1UL, 4, new[] { 0, 3, 2, 1 })]
    [InlineData(42UL, 4, new[] { 3, 0, 2, 1 })]
    [InlineData(20260919UL, 4, new[] { 1, 2, 3, 0 })]
    [InlineData(1UL, 3, new[] { 1, 2, 0 })]
    [InlineData(20260919UL, 3, new[] { 0, 2, 1 })]
    [InlineData(20260919UL, 2, new[] { 1, 0 })]
    public void 批量侧顺排与首回合顺序和改动前逐项相同(ulong seed, int players, int[] firstOrder)
    {
        Siege.Sim.Running.MatchSession session = Siege.Sim.Running.MatchSession.Create(SimFixtures.Config(players: players), seed);

        Assert.Equal(Enumerable.Range(0, players).Select(i => (int?)i), session.Match.PlayerStates.Select(s => s.BirthZone));
        Assert.Equal(firstOrder, session.Match.ActionOrder.Select(p => p.Value));
    }

    [Fact]
    public void 选区不扰动其他随机()
    {
        // 规格 Scenario：4 个出生区的标准地图上用某种子开局 → 信物内容、首回合顺序与引入本规则之前逐项相同。
        // 黄金值：改动前 v4、种子 42、4 人的信物分布（RelicPlacement.ToString 逐格）与首回合顺序 P3 > P0 > P2 > P1。
        // 变异 M-A14：PlantPrototype 里多消费一次 MatchFlow 自己持有的 setup 子流（模拟选区借用 setup 实例）→ 首回合顺序变，本测试、上一条与 6 平台那条共红 8。
        string[] golden =
        [
            "B2 BirthZone/Birth SchoolEmblemx1(Basic)", "M2 BirthZone/Birth SchoolEmblemx1(Basic)", "C3 BirthZone/Birth SchoolEmblemx1(Artisan)",
            "L3 BirthZone/Birth SchoolEmblemx1(Fortress)", "G5 Contested/Standard SchoolEmblemx1(Multiplier)", "E7 Contested/Standard SchoolEmblemx1(Artisan)",
            "G7 Contested/High SchoolEmblemx2(Basic)", "J7 Contested/Standard Conscription+1", "G9 Contested/Standard SchoolEmblemx1(Basic)",
            "C11 BirthZone/Birth SchoolEmblemx1(Basic)", "L11 BirthZone/Birth SchoolEmblemx1(Artisan)", "B12 BirthZone/Birth SchoolEmblemx1(Basic)",
            "M12 BirthZone/Birth SchoolEmblemx1(Fortress)",
        ];
        MatchFlow match = MatchFlow.Create(FourPlayerBaseMap.Create(), new GameSeed(42), MatchFixtures.All, MatchOptions.Immediate);

        match.PlantPrototype();

        Assert.Equal(golden, match.Relics.Generation.Placements.Select(p => p.ToString()));
        Assert.Equal(new[] { 3, 0, 2, 1 }, match.ActionOrder.Select(p => p.Value));
    }

    [Fact]
    public void 区数多于人数时种子选区()
    {
        // 规格 Scenario：6 个平台的地图上用同一对局种子、同一人工选择重复开局两次 → 三名 AI 两次锁定的平台完全相同，互不重复，也不与人工选择重复。
        // 变异 M-A15：Seeded 抽中后不从空闲表里移除 → 出现重复，红。
        MapData map = FrontierFixtures.Map();
        var seen = new HashSet<string>();
        for (ulong seed = 1; seed <= 40; seed++)
        {
            int[] first = Zones(map, seed, manualZone: 4);
            int[] second = Zones(map, seed, manualZone: 4);

            Assert.Equal(first, second);
            Assert.Equal(4, first[1]);                          // 人（P1）选的 5 号台原样保留
            Assert.Equal(4, first.Distinct().Count());          // 互不同区，也不与人重复
            Assert.All(first, z => Assert.InRange(z, 0, 5));
            seen.Add(string.Join(",", first));
        }

        // 由种子驱动而不是写死：40 颗种子至少抽出 10 种不同的组合（5×4×3 = 60 种等概率），且每个平台都有 AI 选到过——
        // "AI 永远挤在前几个编号"正是本规则要消灭的现状。
        Assert.True(seen.Count >= 10, $"40 颗种子只抽出 {seen.Count} 种组合。");
        Assert.Equal(new[] { 0, 1, 2, 3, 5 }, seen.SelectMany(s => s.Split(',').Select(int.Parse)).Where(z => z != 4).Distinct().Order());
    }

    [Fact]
    public void 种子选区不扰动信物与首回合顺序()
    {
        // 6 平台图上的自包含强测：同种子下，AI 由种子选区 vs 全部手工指定成另一组区，信物分布与首回合顺序逐项相同——
        // 选区子流（zone-pick）独立命名，消费多少次都不影响 relic-gen 与 setup。
        MapData map = FrontierFixtures.Map();
        var seed = new GameSeed(20260919);
        MatchFlow seeded = MatchFlow.Create(map, seed, MatchFixtures.All, MatchOptions.Immediate);
        MatchFlow manual = MatchFlow.Create(map, seed, MatchFixtures.All, MatchOptions.Immediate);

        seeded.PlantPrototype();
        manual.PlantSequentially([(MatchFixtures.P0, 5), (MatchFixtures.P1, 5), (MatchFixtures.P2, 0), (MatchFixtures.P3, 0)]);

        Assert.NotEqual(manual.PlayerStates.Select(s => s.BirthZone), seeded.PlayerStates.Select(s => s.BirthZone));   // 样本口径：两局的选区确实不同
        Assert.Equal(16, seeded.Relics.Generation.Placements.Length);
        Assert.Equal(manual.Relics.Generation.Placements.Select(p => p.ToString()), seeded.Relics.Generation.Placements.Select(p => p.ToString()));
        Assert.Equal(manual.ActionOrder, seeded.ActionOrder);
        Assert.NotEqual(GameSeed.ZonePick, GameSeed.Setup);
        Assert.NotEqual(GameSeed.ZonePick, GameSeed.RelicGeneration);
        Assert.NotEqual(GameSeed.ZonePick, GameSeed.Recruit);
    }

    [Fact]
    public void 种子选区的子流名与消费方式是可复现契约()
    {
        // 同一种子在将来的版本里必须抽出同一组平台：钉住子流名 "zone-pick" 与"按玩家编号顺序、每名 AI 在升序空闲表里取一次等概率整数"。
        // 这里用测试内的独立复算（直接从种子派生同名子流），不调用被测的选区函数。
        // 变异 M-A22：Seeded 改从 GameSeed.Setup 同名子流派生 → 本测试红（行为上不扰动 setup 实例，其他测试抓不到）。
        Assert.Equal("zone-pick", GameSeed.ZonePick);
        MapData map = FrontierFixtures.Map();
        int differs = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            RandomStream stream = new GameSeed(seed).Stream("zone-pick");
            List<int> free = [0, 1, 2, 3, 5];   // 人（P1）占 4
            var expected = new List<int>();
            foreach (int player in new[] { 0, 1, 2, 3 })
            {
                if (player == 1)
                {
                    expected.Add(4);
                    continue;
                }

                int index = stream.NextInt(free.Count);
                expected.Add(free[index]);
                free.RemoveAt(index);
            }

            Assert.Equal(expected, Zones(map, seed, manualZone: 4));
            differs += expected.SequenceEqual([0, 4, 1, 2]) ? 0 : 1;
        }

        Assert.True(differs >= 15, "样本口径：抽取结果几乎都等于顺排，测不出是否真的由子流驱动。");
    }

    [Fact]
    public void 中立平台()
    {
        // 规格 Scenario：6 个平台上 4 名玩家锁定 4 个不同平台 → 其余 2 个平台不属于任何玩家的保护期范围。
        MapData map = FrontierFixtures.Map();
        MatchFlow match = MatchFlow.Create(map, new GameSeed(7), MatchFixtures.All, MatchOptions.Immediate);

        var choices = match.PlantPrototype((MatchFixtures.P0, 2));

        int[] taken = [.. choices.Select(c => c.Zone)];
        int[] neutral = [.. Enumerable.Range(0, 6).Except(taken)];
        Assert.Equal(2, neutral.Length);
        foreach ((PlayerId player, int zone) in choices)
        {
            var range = match.LegalRangeFor(player);
            Assert.Equal(map.BirthZones[zone].Order(), range.Order());                          // 保护期内只能落在自己的平台
            Assert.All(neutral, n => Assert.Empty(map.BirthZones[n].Intersect(range)));         // 中立平台不在任何人的保护期范围内
        }

        // Scenario 的后半句（检查阶段补）：保护期结束后，中立平台与其他空格一样可被任何人落子——也包括别人的出生平台。
        match.AtRound(MatchFlow.BuildProtectionRounds + 1);
        foreach ((PlayerId player, _) in choices)
        {
            var range = match.LegalRangeFor(player).ToHashSet();
            Assert.All(neutral, n => Assert.Subset(range, map.BirthZones[n].ToHashSet()));
            Assert.Equal(map.PlayableCount, range.Count);
        }
    }

    [Fact]
    public void 三个入口的选区都走内核的唯一实现()
    {
        // tasks 2.3：批量 MatchSession、终端 PlayCommand、图形 MatchSession.ChooseZone 共用一份选区逻辑。
        // src/godot 不在 siege.sln 里，对行为测试完全隐身（testing.md）——把图形版改回手写顺排，6 区图上 AI 又会挤在前几个编号，
        // 而没有任何行为测试会红；只能做源码文本扫描。判据：入口层（Siege.Sim 全部 + src/godot/scripts 全部）不得直接调
        // PlantSequentially(、不得自己派生选区子流；三个调用点确实调了 PlantPrototype(。
        // 变异 M-C5：src/godot/scripts/MatchSession.cs 的 ChooseZone 改回 Match.PlantSequentially(…) → 本测试红 1（检查阶段补，此前 0 红）。
        string root = FrontierFixtures.RepoRoot();
        string[] entryFiles =
        [
            .. Directory.EnumerateFiles(Path.Combine(root, "src", "Siege.Sim"), "*.cs", SearchOption.AllDirectories),
            .. Directory.EnumerateFiles(Path.Combine(root, "src", "godot", "scripts"), "*.cs", SearchOption.AllDirectories),
        ];
        entryFiles = [.. entryFiles.Where(p =>
            !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))];
        Assert.True(entryFiles.Length >= 20, $"样本口径：只扫到 {entryFiles.Length} 个入口层源文件。");

        string[] forbidden = ["PlantSequentially(", "PrototypeZoneAssignment", "GameSeed.ZonePick", "\"zone-pick\""];
        string[] offenders = [.. entryFiles
            .Where(p => forbidden.Any(token => File.ReadAllText(p).Contains(token, StringComparison.Ordinal)))
            .Select(p => Path.GetRelativePath(root, p))];
        Assert.True(offenders.Length == 0, "入口层绕过了 MatchFlow.PlantPrototype：" + string.Join("；", offenders));

        foreach (string[] caller in new[]
        {
            new[] { "src", "Siege.Sim", "Running", "MatchSession.cs" },
            ["src", "Siege.Sim", "Play", "PlayCommand.cs"],
            ["src", "godot", "scripts", "MatchSession.cs"],
        })
        {
            Assert.Contains("PlantPrototype(", File.ReadAllText(Path.Combine([root, .. caller])), StringComparison.Ordinal);
        }

        // 反面：被禁的记号在它的归属处确实命中（扫描不是因为拼错了记号才干净）。
        string flow = File.ReadAllText(Path.Combine(root, "src", "Siege.Core", "Match", "MatchFlow.cs"));
        Assert.Contains("PlantSequentially(", flow, StringComparison.Ordinal);
        Assert.Contains("PrototypeZoneAssignment.Assign(", flow, StringComparison.Ordinal);
        Assert.Contains("GameSeed.ZonePick", File.ReadAllText(Path.Combine(root, "src", "Siege.Core", "Match", "PrototypeZoneAssignment.cs")), StringComparison.Ordinal);
    }

    private static int[] Zones(MapData map, ulong seed, int manualZone)
    {
        MatchFlow match = MatchFlow.Create(map, new GameSeed(seed), MatchFixtures.All, MatchOptions.Immediate);
        match.PlantPrototype((MatchFixtures.P1, manualZone));
        return [.. match.PlayerStates.Select(s => s.BirthZone!.Value)];
    }
}
