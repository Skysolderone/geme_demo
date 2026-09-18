using System.Text.Json;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Scoring;
using Siege.Sim.Analysis;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.MatchTelemetry;

/// <summary>
/// multiplier-rebalance（implement.md 2.2，裁决 3）：Sim 快照棋串明细记各棋子类型计数；平衡报告"各棋子势力占比"（proposal 表格口径）。
/// 本段是报告层统计，没有 openspec Scenario；方法名按 implement.md 条目命名。
/// </summary>
public class 各棋子势力占比Tests
{
    [Fact]
    public void 快照棋串明细往返保留各棋子类型计数()
    {
        // 六种计数非零且互不相同（artisan-terrain-edit 段 A：类型增至六种）：写入 / 读回任何一处把两种类型写反或漏写，都能从值上看出来。
        // 旧日志（无 PieceCounts 字段）解析为 null（未知），MUST NOT 回填成 0。
        // 变异验证 M-MR8：GroupEntry.PieceCounts 的 get 恒 null、init 丢弃传入值 → 红 3（本类 3 个测试全红）。
        var counts = new Dictionary<string, int> { ["Basic"] = 1, ["Fortress"] = 2, ["Line"] = 3, ["Multiplier"] = 4, ["Synergy"] = 5, ["Artisan"] = 6 };
        GroupEntry group = new() { Stones = ["A1"], Base = 20, LineBonus = 6, SynergyBonus = 8, MultiplierCount = 4, EffectiveMultiplierCount = 3, Power = 81, PieceCounts = counts };
        MatchLog log = SimFixtures.Synthetic(
            21,
            [SimFixtures.Turn(1, 1, 0, [81, 0, 0, 0], ["A1:Basic"], groupsOfPlayer: [group])],
            [],
            SimFixtures.ResultOf(1, [0]));

        MatchLog restored = MatchLog.Parse(log.DeterministicText());
        Dictionary<string, int>? read = Assert.Single(restored.Turns[0].PlayersState[0].Groups).PieceCounts;

        Assert.NotNull(read);
        Assert.Equal(
            ["Artisan=6", "Basic=1", "Fortress=2", "Line=3", "Multiplier=4", "Synergy=5"],
            read.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));

        const string oldGroupLine = "{\"Stones\":[\"C1\"],\"Base\":3,\"LineBonus\":0,\"SynergyBonus\":0,\"MultiplierCount\":3,\"EffectiveMultiplierCount\":3,\"Power\":10}";
        Assert.Null(JsonSerializer.Deserialize<GroupEntry>(oldGroupLine, LogJson.Options)!.PieceCounts);
    }

    [Fact]
    public void 真实跑局快照的类型计数与明细自洽()
    {
        // 写入端从快照盘面逐枚统计。用明细里独立记录的字段反证计数：
        //   Σ计数 = 棋子数；Multiplier 计数 = MultiplierCount；Σ(基础军势 × 计数) = Base（普通 / 堡垒写反会破）；
        //   SynergyBonus = Synergy 计数 × 其他出现类型数 × 2（连珠 / 协同写反会破；匠人算"其他类型"之一）；LineBonus > 0 ⇒ Line 计数 ≥ 2。
        // 样本口径下界：必须真的见到同时含普通子与堡垒子、有协同加值、含匠人、以及匠人与协同子同串的棋串，否则上面的反证没有被触发
        //（Easy 小样本里没有连珠成线，连珠 / 协同写反由协同加值等式抓）。
        // 匠人的样本来源（artisan-terrain-edit 段 A 检查）：共用的 Easy 样本里**一枚匠人都不会落盘**——Easy 的征募前瞻分只看基础军势，
        // 匠人 1 分在平手里永远排在枚举末位，实测 4 局 × 12 大回合 2718 条棋串含匠人 0 条。所以光把匠人加进上面两条算式是惰性的
        // （检查阶段变异 M-C1a / M-C1b 实证：去掉匠人项全绿）。这里另起一份 Standard 难度的小样本（2 局 × 3 大回合，约 1 秒）把匠人真正放上盘面。
        // 变异验证 M-MR6a：MatchSession.PieceCountsOf 把 Basic / Fortress 的键写反 → 红 1（本测试）；M-MR6b：把 Line / Synergy 写反 → 红 1（本测试）；
        // M-C1a（去掉 Base 等式的匠人项）、M-C1b（otherTypes 去掉匠人）→ 各红 1（本测试）。
        List<MatchLog> withArtisan = BatchRunner.Execute(
            SimFixtures.Config(count: 2, seedStart: 11, maxRounds: 3, difficulty: AiDifficulty.Standard), parallelism: 1);
        List<GroupEntry> groups =
            [.. SimFixtures.Sample.Value.Concat(withArtisan).SelectMany(l => l.Turns).SelectMany(t => t.PlayersState).SelectMany(p => p.Groups)];
        Assert.NotEmpty(groups);

        int fortressAndBasic = 0;
        int synergyWithBonus = 0;
        int withArtisanGroups = 0;
        int artisanWithSynergy = 0;
        foreach (GroupEntry g in groups)
        {
            Dictionary<string, int> c = Assert.IsType<Dictionary<string, int>>(g.PieceCounts);
            Assert.Equal(Enum.GetNames<PieceType>(), c.Keys.ToArray());
            Assert.Equal(g.Stones.Count, c.Values.Sum());
            Assert.Equal(g.MultiplierCount, c["Multiplier"]);
            Assert.Equal(g.Base, c["Basic"] * 1 + c["Fortress"] * 4 + c["Line"] + c["Multiplier"] + c["Synergy"] + c["Artisan"]);
            int otherTypes = new[] { "Basic", "Fortress", "Line", "Multiplier", "Artisan" }.Count(t => c[t] > 0);
            Assert.Equal(g.SynergyBonus, c["Synergy"] * otherTypes * 2);
            if (g.LineBonus > 0)
            {
                Assert.True(c["Line"] >= 2, $"连珠加值 {g.LineBonus} 但连珠子计数 {c["Line"]}");
            }

            fortressAndBasic += c["Fortress"] > 0 && c["Basic"] > 0 ? 1 : 0;
            synergyWithBonus += g.SynergyBonus > 0 ? 1 : 0;
            withArtisanGroups += c["Artisan"] > 0 ? 1 : 0;
            artisanWithSynergy += c["Artisan"] > 0 && c["Synergy"] > 0 ? 1 : 0;
        }

        Assert.True(fortressAndBasic > 0, "样本里没有同时含普通子与堡垒子的棋串");
        Assert.True(synergyWithBonus > 0, "样本里没有协同加值");
        Assert.True(withArtisanGroups > 0, "样本里没有含匠人的棋串 —— Base 等式的匠人项没有被触发");
        Assert.True(artisanWithSynergy > 0, "样本里没有匠人与协同子同串的棋串 —— otherTypes 里的匠人项没有被触发");
    }

    [Fact]
    public void 各棋子势力占比按口径手算()
    {
        // proposal 口径，逐局取终局快照（最后一条小回合快照）中参赛玩家的全部棋串：
        //   P0 串 1：普通×3 + 堡垒×1 + 倍增×2，基础 9、生效 2、军势 20 → 放大部分 ⌊9 × 9/4⌋ − 9 = 11
        //            普通 3、堡垒 4、倍增 2 + 11 = 13
        //   P0 串 2：连珠×4 + 倍增×2，基础 6、连珠加值 12、军势 25 → 放大部分 ⌊6 × 9/4⌋ − 6 = 7
        //            连珠 4 + 12 = 16、倍增 2 + 7 = 9
        //   P1 串  ：倍增×5 + 协同×1 + 匠人×1，基础 7、协同加值 1 × 2 × 2 = 4、生效 3、军势 ⌊7 × 27/8⌋ + 4 = 23 + 4 = 27 → 放大部分 23 − 7 = 16
        //            倍增 5 + 16 = 21、协同 1 + 4 = 5、匠人 1 + 0 = 1（匠人只贡献基础军势 1，无任何位置加值：piece-effects「匠人落子后无持续效果」）
        //   P2 已弃赛：堡垒×3（军势 12）→ 不计。第 1 小回合快照（非终局）里的棋串 → 不计。
        //   合计：盘面 普通 3 / 堡垒 1 / 连珠 4 / 倍增 9 / 协同 1 / 匠人 1 = 19 枚；势力 3 / 4 / 16 / 43 / 5 / 1 = 72（= 20 + 25 + 27）。
        // 被排除的样本（testing.md：统计口径测试必须放一个被排除的样本）：旧日志一局，终局快照里有一条棋串缺类型计数 → 整局跳过，
        // 它的 100 点军势与 5 枚棋子 MUST NOT 进任何分子分母。
        // 变异验证 M-MR7：PieceShares 在 skipped 分支里仍把该局棋串军势累加进分母 → 红 1（本测试）；M-MR9：终局快照改取 Turns[0] → 红 1（本测试）；M-MR2（封顶改回 4）→ 红 23，含本测试。
        MatchLog counted = SimFixtures.Synthetic(
            31,
            [
                TurnWith(1, (0, "Active", [Group(power: 5, @base: 5, basic: 5)])),
                TurnWith(
                    2,
                    (0, "Active", [
                        Group(power: 20, @base: 9, basic: 3, fortress: 1, multiplier: 2),
                        Group(power: 25, @base: 6, line: 4, multiplier: 2, lineBonus: 12),
                    ]),
                    (1, "Active", [Group(power: 27, @base: 7, multiplier: 5, synergy: 1, artisan: 1, synergyBonus: 4)]),
                    (2, "Resigned", [Group(power: 12, @base: 12, fortress: 3)])),
            ],
            [],
            SimFixtures.ResultOf(1, [0]));
        MatchLog old = SimFixtures.Synthetic(
            32,
            [
                TurnWith(
                    1,
                    (0, "Active", [Group(power: 3, @base: 3, basic: 3)]),
                    (1, "Active", [new GroupEntry { Stones = ["A1", "A2", "A3", "A4", "A5"], Base = 5, MultiplierCount = 5, Power = 100 }])),
            ],
            [],
            SimFixtures.ResultOf(1, [1]));

        BalanceReport report = BalanceAnalyzer.Analyze([counted, old]);
        PieceShareSection s = report.PieceShares;

        Assert.Equal(2, report.Included);
        Assert.Equal((1, 1), (s.Matches, s.Skipped));
        Assert.Equal((19L, 72L), (s.TotalStones, s.TotalPower));
        Assert.Equal(
            ["Basic:3/3", "Fortress:1/4", "Line:4/16", "Multiplier:9/43", "Synergy:1/5", "Artisan:1/1"],
            s.Pieces.Select(p => $"{p.Type}:{p.Stones}/{p.Power}"));
        PieceShare m = s.Pieces.Single(p => p.Type == "Multiplier");
        Assert.Equal(9.0 / 19, m.StoneShare, 9);
        Assert.Equal(43.0 / 72, m.PowerShare, 9);
        Assert.Equal(43.0 / 9, m.MeanPerStone, 9);
        Assert.Equal(1.0, s.Pieces.Sum(p => p.PowerShare), 9);

        string text = ReportWriter.Render(report);
        Assert.Contains("各棋子势力占比", text);
        Assert.Contains("- 纳入 1 局，跳过无棋子类型计数的旧日志 / 无快照局 1 局；盘面棋子 19 枚，归因势力 72", text);
        Assert.Contains("- 棋子 Multiplier：盘面 9 枚（47.4%），势力 43（59.7%），每颗平均 4.78", text);
        Assert.Contains("- 棋子 Fortress：盘面 1 枚（5.3%），势力 4（5.6%），每颗平均 4", text);
        Assert.Contains("- 棋子 Artisan：盘面 1 枚（5.3%），势力 1（1.4%），每颗平均 1", text);
    }

    [Fact]
    public void 含连珠线协同倍增的真实棋串写快照读回并按口径归因()
    {
        // check 补：真实跑局 Easy 样本里没有连珠成线，连珠计数的写入路径未被覆盖；这里用真实盘面走 MatchSession.PieceCountsOf → 日志往返 → 分析器。
        // artisan-terrain-edit 段 A 检查再补一枚匠人 H2：Easy 样本里匠人同样一枚都不落盘（见上一条测试的注释），
        // 匠人计数的写入路径与"协同子把匠人算作其他类型"的位置加值口径都由这条真实盘面测试钉住，且期望值非 0（testing.md：期望值是 0 的遥测断言抓不到写入端漏写）。
        // 盘面：连珠 B2-C2-D2（线长 3，加值 3 × 2 = 6）+ 协同 E2（其他类型 {连珠, 倍增, 匠人} 3 种 → 6）+ 倍增 F2、G2（生效 2，倍率 9/4）+ 匠人 H2（基础 1、无任何加值）。
        // 手算：基础 3 + 1 + 2 + 1 = 7，军势 ⌊7 × 9 / 4⌋ + 6 + 6 = 15 + 12 = 27；放大部分 15 − 7 = 8。
        //   连珠 3 + 6 = 9、协同 1 + 6 = 7、倍增 2 + 8 = 10、匠人 1 + 0 = 1；合计 27 = 棋串军势（归因不多不少）。
        // 变异验证 C-MR2（check）：占比口径里倍增子放大部分不减基础（amplified = Apply(Base)）→ 倍增 15 ≠ 9，本测试红；
        // C-MR3：连珠加值分给倍增子（Multiplier 分支加 LineBonus、Line 分支不加）→ 本测试红；
        // M-C4（artisan-terrain-edit 检查）：PieceCountsOf 把匠人并进普通子键 → 本测试红。
        GameBoard board = TestMaps.Blank(size: 9)
            .Place("B2", TestMaps.P0, PieceType.Line).Place("C2", TestMaps.P0, PieceType.Line).Place("D2", TestMaps.P0, PieceType.Line)
            .Place("E2", TestMaps.P0, PieceType.Synergy)
            .Place("F2", TestMaps.P0, PieceType.Multiplier).Place("G2", TestMaps.P0, PieceType.Multiplier)
            .Place("H2", TestMaps.P0, PieceType.Artisan);
        GroupPower g = Assert.Single(PowerCalculator.Compute(board).Of(TestMaps.P0).Groups);
        Assert.Equal((7, 6, 6, 2, 27L), (g.BaseTotal, g.LineBonus, g.SynergyBonus, g.MultiplierCount, g.Power));

        GroupEntry written = new()
        {
            Stones = [.. g.Stones.Select(s => s.ToNotation())],
            Base = g.BaseTotal,
            LineBonus = g.LineBonus,
            SynergyBonus = g.SynergyBonus,
            MultiplierCount = g.MultiplierCount,
            EffectiveMultiplierCount = g.EffectiveMultiplierCount,
            Power = g.Power,
            PieceCounts = Siege.Sim.Running.MatchSession.PieceCountsOf(board, g),
        };
        MatchLog log = SimFixtures.Synthetic(
            41,
            [SimFixtures.Turn(1, 1, 0, [27, 0, 0, 0], ["H2:Artisan"], groupsOfPlayer: [written])],
            [],
            SimFixtures.ResultOf(1, [0]));

        MatchLog restored = MatchLog.Parse(log.DeterministicText());
        Dictionary<string, int>? read = Assert.Single(restored.Turns[0].PlayersState[0].Groups).PieceCounts;
        Assert.NotNull(read);
        Assert.Equal(
            ["Artisan=1", "Basic=0", "Fortress=0", "Line=3", "Multiplier=2", "Synergy=1"],
            read.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));

        BalanceReport report = BalanceAnalyzer.Analyze([restored]);
        PieceShareSection s = report.PieceShares;
        Assert.Equal((1, 0), (s.Matches, s.Skipped));
        Assert.Equal((7L, 27L), (s.TotalStones, s.TotalPower));
        Assert.Equal(
            ["Basic:0/0", "Fortress:0/0", "Line:3/9", "Multiplier:2/10", "Synergy:1/7", "Artisan:1/1"],
            s.Pieces.Select(p => $"{p.Type}:{p.Stones}/{p.Power}"));
        string rendered = ReportWriter.Render(report);
        Assert.Contains("- 棋子 Synergy：盘面 1 枚（14.3%），势力 7（25.9%），每颗平均 7", rendered);
        Assert.Contains("- 棋子 Artisan：盘面 1 枚（14.3%），势力 1（3.7%），每颗平均 1", rendered);
    }

    private static TurnSnapshot TurnWith(int turn, params (int Player, string Status, GroupEntry[] Groups)[] players) =>
        SimFixtures.Turn(turn, 1, 0, [0, 0, 0, 0], ["A1:Basic"]) with
        {
            PlayersState = [.. Enumerable.Range(0, 4).Select(i => new PlayerEntry
            {
                Player = i,
                Status = players.FirstOrDefault(p => p.Player == i).Status ?? "Active",
                Groups = [.. players.FirstOrDefault(p => p.Player == i).Groups ?? []],
            })],
        };

    private static GroupEntry Group(long power, int @base, int basic = 0, int fortress = 0, int line = 0, int multiplier = 0, int synergy = 0, int artisan = 0, int lineBonus = 0, int synergyBonus = 0) =>
        new()
        {
            Stones = [.. Enumerable.Range(0, basic + fortress + line + multiplier + synergy + artisan).Select(i => $"Z{i}")],
            Base = @base,
            LineBonus = lineBonus,
            SynergyBonus = synergyBonus,
            MultiplierCount = multiplier,
            Power = power,
            PieceCounts = new Dictionary<string, int> { ["Basic"] = basic, ["Fortress"] = fortress, ["Line"] = line, ["Multiplier"] = multiplier, ["Synergy"] = synergy, ["Artisan"] = artisan },
        };
}
