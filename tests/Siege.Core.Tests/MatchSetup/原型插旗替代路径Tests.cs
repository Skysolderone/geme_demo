using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Sim.Config;

namespace Siege.Core.Tests.MatchSetup;

/// <summary>规格：match-setup —— Requirement: 原型插旗替代路径</summary>
public class 原型插旗替代路径Tests
{
    /// <summary>
    /// 冒险概率写死为 0（flag-contest 1.3）：下面凡钉"引入冒险概率之前"黄金值 / 互不同区的测试都用它，不跟随缺省 p = 15——
    /// 规格保证 p = 0 时锁定结果与此前逐项相同，缺省值另由冒险概率的 Scenario 覆盖（testing.md「依赖 AI 实际怎么走的断言要把权重写死」同理）。
    /// </summary>
    private static readonly MatchOptions NoRisk = MatchOptions.Immediate with { FlagRisk = 0 };

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
        // 规格 Scenario（flag-contest 改写）：4 个出生区的地图上、冒险概率为 0，本机玩家选 2 号区，其余三名 AI 按编号顺序占用 1、3、4 号区。
        // 变异 M-A13：Sequential 里去掉"取到人选的区就跳过" → 红（AI 会与人同区）。
        MatchFlow match = MatchFlow.Create(FourPlayerBaseMap.Create(), new GameSeed(42), MatchFixtures.All, NoRisk);

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
        MatchFlow match = MatchFlow.Create(FourPlayerBaseMap.Create(), new GameSeed(seed), ids, NoRisk);

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
        Siege.Sim.Running.MatchSession session = Siege.Sim.Running.MatchSession.Create(SimFixtures.Config(players: players) with { FlagRisk = 0 }, seed);

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
        MatchFlow match = MatchFlow.Create(FourPlayerBaseMap.Create(), new GameSeed(42), MatchFixtures.All, NoRisk);

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
        MatchFlow seeded = MatchFlow.Create(map, seed, MatchFixtures.All, NoRisk);
        MatchFlow manual = MatchFlow.Create(map, seed, MatchFixtures.All, NoRisk);

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
        MatchFlow match = MatchFlow.Create(map, new GameSeed(7), MatchFixtures.All, NoRisk);

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

        string[] forbidden = ["PlantSequentially(", "PrototypeZoneAssignment", "GameSeed.ZonePick", "\"zone-pick\"", "GameSeed.FlagRisk"];   // 冒险子流只禁类型常量："flag-risk" 同时是 Siege.Sim 的命令行选项名（--flag-risk），不能按字面量禁
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
        Assert.Contains("GameSeed.FlagRisk", File.ReadAllText(Path.Combine(root, "src", "Siege.Core", "Match", "PrototypeZoneAssignment.cs")), StringComparison.Ordinal);   // flag-contest：冒险子流同样只在唯一实现里派生
    }

    // ---------- flag-contest D1：冒险概率 ----------

    [Fact]
    public void 冒险概率为0时锁定结果不变()
    {
        // 规格 Scenario：冒险概率为 0，用任意种子与任意人工选择开局 → 锁定结果与引入冒险概率之前逐项相同。
        // 期望取自测试内对"引入冒险概率之前"两支规则的独立复算（OldAssign：顺排游标跳过人选 / zone-pick 子流在升序空闲表里等概率抽），不调用被测实现。
        // 覆盖：标准图（v5，区数 = 人数上限）与 6 平台图，2–4 人，无人工选择 + 每个座位 × 每个区。
        int cases = 0;
        foreach (MapData map in new[] { FourPlayerBaseMap.Create(), FrontierFixtures.Map() })
        {
            int zoneCount = map.BirthZones.Length;
            for (ulong seed = 1; seed <= 12; seed++)
            {
                for (int players = 2; players <= 4; players++)
                {
                    PlayerId[] ids = [.. Enumerable.Range(0, players).Select(i => new PlayerId(i))];
                    Assert.Equal(OldAssign(map, seed, players, null, 0), AssignZones(map, seed, ids, 0, null));
                    cases++;
                    for (int seat = 0; seat < players; seat++)
                    {
                        for (int zone = 0; zone < zoneCount; zone++)
                        {
                            Assert.Equal(OldAssign(map, seed, players, seat, zone), AssignZones(map, seed, ids, 0, (ids[seat], zone)));
                            cases++;
                        }
                    }
                }
            }
        }

        Assert.True(cases >= 1000, $"样本口径：只比了 {cases} 种开局。");
    }

    [Fact]
    public void 冒险加入已有人的出生区()
    {
        // 规格 Scenario：冒险概率为 100，本机玩家选 2 号区，其余三名 AI 依次插旗 → 每名 AI 都加入一个此前已有人插旗的出生区，全部玩家锁定在同一个出生区。
        // 人坐 2 号位（P1）：P0 编号在人之前，仍然看得到人的旗——人工选择先于全部 AI 插下。
        MatchOptions always = MatchOptions.Immediate with { FlagRisk = 100 };
        foreach (ulong seed in new ulong[] { 1, 42, 20260925 })
        {
            MatchFlow match = MatchFlow.Create(FourPlayerBaseMap.Create(), new GameSeed(seed), MatchFixtures.All, always);
            var choices = match.PlantPrototype((MatchFixtures.P1, 1));

            Assert.All(choices, c => Assert.Equal(1, c.Zone));
            Assert.Equal(new int?[] { 1, 1, 1, 1 }, match.PlayerStates.Select(s => s.BirthZone));
            Assert.Equal(MatchPhase.InProgress, match.Phase);
        }

        // 它之前没有任何已插旗时直接占用空闲出生区：无人工选择时 P0 按原规则取区（标准图顺排 → 1 号区；6 平台图 → zone-pick 的第一次抽取），其余全部跟进。
        MatchFlow batch = MatchFlow.Create(FourPlayerBaseMap.Create(), new GameSeed(42), MatchFixtures.All, always);
        batch.PlantPrototype();
        Assert.Equal(new int?[] { 0, 0, 0, 0 }, batch.PlayerStates.Select(s => s.BirthZone));

        MapData frontier = FrontierFixtures.Map();
        for (ulong seed = 1; seed <= 6; seed++)
        {
            int first = new GameSeed(seed).Stream("zone-pick").NextInt(6);
            MatchFlow match = MatchFlow.Create(frontier, new GameSeed(seed), MatchFixtures.All, always);
            match.PlantPrototype();
            Assert.Equal(Enumerable.Repeat((int?)first, 4), match.PlayerStates.Select(s => s.BirthZone));
        }
    }

    [Fact]
    public void 冒险抽签可复现且不扰动其他随机()
    {
        // 规格 Scenario：冒险概率为 15，用同一种子与同一人工选择重复开局两次，并与冒险概率为 0 的同种子开局对照
        // → 两次锁定结果完全相同；三次开局的信物内容与首回合顺序逐项相同。
        MatchOptions risky = MatchOptions.Immediate with { FlagRisk = 15 };
        int differs = 0;
        for (ulong seed = 1; seed <= 12; seed++)
        {
            MatchFlow a = MatchFlow.Create(FourPlayerBaseMap.Create(), new GameSeed(seed), MatchFixtures.All, risky);
            MatchFlow b = MatchFlow.Create(FourPlayerBaseMap.Create(), new GameSeed(seed), MatchFixtures.All, risky);
            MatchFlow zero = MatchFlow.Create(FourPlayerBaseMap.Create(), new GameSeed(seed), MatchFixtures.All, NoRisk);
            a.PlantPrototype((MatchFixtures.P1, 1));
            b.PlantPrototype((MatchFixtures.P1, 1));
            zero.PlantPrototype((MatchFixtures.P1, 1));

            Assert.Equal(a.PlayerStates.Select(s => s.BirthZone), b.PlayerStates.Select(s => s.BirthZone));
            string[] relics = [.. zero.Relics.Generation.Placements.Select(p => p.ToString())];
            Assert.Equal(relics, a.Relics.Generation.Placements.Select(p => p.ToString()));
            Assert.Equal(relics, b.Relics.Generation.Placements.Select(p => p.ToString()));
            Assert.Equal(zero.ActionOrder, a.ActionOrder);
            Assert.Equal(zero.ActionOrder, b.ActionOrder);
            differs += a.PlayerStates.Select(s => s.BirthZone).SequenceEqual(zero.PlayerStates.Select(s => s.BirthZone)) ? 0 : 1;
        }

        // 样本口径：至少有一颗种子在 p = 15 下确实冒了险（锁定结果与 p = 0 不同），否则"不扰动"是空证。
        Assert.True(differs >= 1, "12 颗种子在 p = 15 下没有一局冒险，测不出冒险抽签是否扰动其他随机。");
    }

    [Fact]
    public void 冒险抽签的子流名与消费方式是可复现契约()
    {
        // 同一种子、同一人工选择与同一 p 在将来的版本里必须得到同一锁定结果：钉住子流名 "flag-risk" 与消费方式——
        // 按编号依次，只有此前已有旗时才抽一次 [0,100)，小于 p 再在"已有人的出生区（升序去重）"里用同一子流抽一次等概率下标；否则走原规则、不碰冒险子流。
        // 期望用测试内的独立复算（RiskAssign，直接从种子派生同名子流），不调用被测实现。
        Assert.Equal("flag-risk", GameSeed.FlagRisk);
        Assert.Equal(5, new[] { GameSeed.RelicGeneration, GameSeed.Recruit, GameSeed.Setup, GameSeed.ZonePick, GameSeed.FlagRisk }.Distinct().Count());

        int hits = 0, cases = 0;
        foreach (MapData map in new[] { FourPlayerBaseMap.Create(), FrontierFixtures.Map() })
        {
            foreach (int p in new[] { 15, 50 })
            {
                for (ulong seed = 1; seed <= 30; seed++)
                {
                    foreach (int? seat in new int?[] { null, 1, 3 })
                    {
                        int zone = seat is null ? 0 : map.BirthZones.Length - 1;
                        (PlayerId, int)? manual = seat is { } s ? (MatchFixtures.All[s], zone) : null;
                        int[] expected = RiskAssign(map, seed, 4, seat, zone, p);
                        Assert.Equal(expected, AssignZones(map, seed, MatchFixtures.All, p, manual));
                        cases++;
                        hits += expected.SequenceEqual(OldAssign(map, seed, 4, seat, zone)) ? 0 : 1;
                    }
                }
            }
        }

        // 样本口径：冒险确实发生过不少次（否则契约只钉住了"原规则"那一半）。
        Assert.True(hits >= 30, $"{cases} 种开局里只有 {hits} 种冒了险。");
    }

    [Fact]
    public void 冒险概率须在0到100之间()
    {
        // D2：p 是 0–100 的整数百分比；越界在建局时响亮失败，选区入口同样拒绝。
        foreach (int bad in new[] { -1, 101 })
        {
            Assert.ThrowsAny<ArgumentException>(() =>
                MatchFlow.Create(FourPlayerBaseMap.Create(), new GameSeed(1), MatchFixtures.All, MatchOptions.Immediate with { FlagRisk = bad }));
            Assert.ThrowsAny<ArgumentException>(() => PrototypeZoneAssignment.Assign(FourPlayerBaseMap.Create(), new GameSeed(1), MatchFixtures.All, bad));
        }

        Assert.Equal(15, MatchOptions.DefaultFlagRisk);
        Assert.Equal(MatchOptions.DefaultFlagRisk, MatchOptions.Default.FlagRisk);
        Assert.Equal(MatchOptions.DefaultFlagRisk, MatchOptions.Immediate.FlagRisk);
        foreach (int ok in new[] { 0, 100 })
        {
            MatchFlow match = MatchFlow.Create(FourPlayerBaseMap.Create(), new GameSeed(1), MatchFixtures.All, MatchOptions.Immediate with { FlagRisk = ok });
            Assert.Equal(ok, match.Options.FlagRisk);
        }
    }

    [Fact]
    public void 冒险概率进入日志首部()
    {
        // 规格 Scenario：读取任意一局的对局日志首部 → 其中记录了本局生效的冒险概率。
        // 写入路径用非缺省值（37）证伪；缺省配置（未给 p）落成缺省值 15 写进首部与对局本身——不落成就无法与"首部缺该项 = 旧日志 = 0"区分。
        RunConfig set = SimFixtures.Config(turnLimit: 4) with { FlagRisk = 37 };
        Siege.Sim.Logging.MatchLog log = Siege.Sim.Running.BatchRunner.Execute(set, parallelism: 1)[0];
        Assert.Equal(37, log.Header.Config.FlagRisk);
        Assert.Contains("\"FlagRisk\":37,", log.DeterministicText().Split('\n')[0], StringComparison.Ordinal);
        Assert.Equal(37, Siege.Sim.Running.MatchSession.Create(set, 1).Match.Options.FlagRisk);

        RunConfig unset = SimFixtures.Config(turnLimit: 4);
        Assert.Null(unset.FlagRisk);
        Siege.Sim.Logging.MatchLog defaulted = Siege.Sim.Running.BatchRunner.Execute(unset, parallelism: 1)[0];
        Assert.Equal(MatchOptions.DefaultFlagRisk, defaulted.Header.Config.FlagRisk);
        Assert.Equal(MatchOptions.DefaultFlagRisk, Siege.Sim.Running.MatchSession.Create(unset, 1).Match.Options.FlagRisk);
    }

    /// <summary>直接调唯一实现，取各玩家锁定的区号（顺序同 <paramref name="ids"/>）。</summary>
    private static int[] AssignZones(MapData map, ulong seed, PlayerId[] ids, int flagRisk, (PlayerId Player, int Zone)? manual) =>
        [.. PrototypeZoneAssignment.Assign(map, new GameSeed(seed), ids, flagRisk, manual).Select(c => c.Zone)];

    /// <summary>引入冒险概率之前的两支规则（独立复算）：区数 ≤ 人数上限 → 顺排游标、取到人选的区就跳过；否则 zone-pick 子流在升序空闲表里等概率抽取。</summary>
    private static int[] OldAssign(MapData map, ulong seed, int players, int? manualSeat, int manualZone) =>
        RiskAssign(map, seed, players, manualSeat, manualZone, flagRisk: null);

    /// <summary>
    /// 冒险概率规则的独立复算（flag-contest D1）。<paramref name="flagRisk"/> 为 <c>null</c> 即"引入冒险概率之前"：根本不派生冒险子流。
    /// </summary>
    private static int[] RiskAssign(MapData map, ulong seed, int players, int? manualSeat, int manualZone, int? flagRisk)
    {
        int zoneCount = map.BirthZones.Length;
        bool seeded = zoneCount > map.MaxPlayers;
        RandomStream pick = new GameSeed(seed).Stream("zone-pick");
        RandomStream risk = new GameSeed(seed).Stream("flag-risk");
        List<int> free = [.. Enumerable.Range(0, zoneCount).Where(z => manualSeat is null || z != manualZone)];
        var planted = new List<int>();
        if (manualSeat is not null)
        {
            planted.Add(manualZone);
        }

        int cursor = 0;
        var result = new int[players];
        for (int p = 0; p < players; p++)
        {
            if (p == manualSeat)
            {
                result[p] = manualZone;
                continue;
            }

            int[] joinable = [.. planted.Distinct().Order()];
            if (flagRisk is { } threshold && joinable.Length > 0 && risk.NextInt(100) < threshold)
            {
                result[p] = joinable[risk.NextInt(joinable.Length)];
            }
            else if (seeded)
            {
                int index = pick.NextInt(free.Count);
                result[p] = free[index];
                free.RemoveAt(index);
            }
            else
            {
                if (manualSeat is not null && cursor == manualZone)
                {
                    cursor++;
                }

                result[p] = cursor % zoneCount;
                cursor++;
            }

            planted.Add(result[p]);
        }

        return result;
    }

    private static int[] Zones(MapData map, ulong seed, int manualZone)
    {
        MatchFlow match = MatchFlow.Create(map, new GameSeed(seed), MatchFixtures.All, NoRisk);
        match.PlantPrototype((MatchFixtures.P1, manualZone));
        return [.. match.PlayerStates.Select(s => s.BirthZone!.Value)];
    }
}
