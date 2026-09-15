using System.Text.Json;
using Siege.Core.Board;
using Siege.Core.Scoring;
using Siege.Sim.Analysis;
using Siege.Sim.Logging;

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
        // 五种计数非零且互不相同：写入 / 读回任何一处把两种类型写反或漏写，都能从值上看出来。
        // 旧日志（无 PieceCounts 字段）解析为 null（未知），MUST NOT 回填成 0。
        // 变异验证 M-MR8：GroupEntry.PieceCounts 的 get 恒 null、init 丢弃传入值 → 红 3（本类 3 个测试全红）。
        var counts = new Dictionary<string, int> { ["Basic"] = 1, ["Fortress"] = 2, ["Line"] = 3, ["Multiplier"] = 4, ["Synergy"] = 5 };
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
            ["Basic=1", "Fortress=2", "Line=3", "Multiplier=4", "Synergy=5"],
            read.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));

        const string oldGroupLine = "{\"Stones\":[\"C1\"],\"Base\":3,\"LineBonus\":0,\"SynergyBonus\":0,\"MultiplierCount\":3,\"EffectiveMultiplierCount\":3,\"Power\":10}";
        Assert.Null(JsonSerializer.Deserialize<GroupEntry>(oldGroupLine, LogJson.Options)!.PieceCounts);
    }

    [Fact]
    public void 真实跑局快照的类型计数与明细自洽()
    {
        // 写入端从快照盘面逐枚统计。用明细里独立记录的字段反证计数：
        //   Σ计数 = 棋子数；Multiplier 计数 = MultiplierCount；Σ(基础军势 × 计数) = Base（普通 / 堡垒写反会破）；
        //   SynergyBonus = Synergy 计数 × 其他出现类型数 × 2（连珠 / 协同写反会破）；LineBonus > 0 ⇒ Line 计数 ≥ 2。
        // 样本口径下界：必须真的见到同时含普通子与堡垒子、以及有协同加值的棋串，否则上面的反证没有被触发（Easy 小样本里没有连珠成线，连珠 / 协同写反由协同加值等式抓）。
        // 变异验证 M-MR6a：MatchSession.PieceCountsOf 把 Basic / Fortress 的键写反 → 红 1（本测试）；M-MR6b：把 Line / Synergy 写反 → 红 1（本测试）。
        List<GroupEntry> groups = [.. SimFixtures.Sample.Value.SelectMany(l => l.Turns).SelectMany(t => t.PlayersState).SelectMany(p => p.Groups)];
        Assert.NotEmpty(groups);

        int fortressAndBasic = 0;
        int synergyWithBonus = 0;
        foreach (GroupEntry g in groups)
        {
            Dictionary<string, int> c = Assert.IsType<Dictionary<string, int>>(g.PieceCounts);
            Assert.Equal(Enum.GetNames<PieceType>(), c.Keys.ToArray());
            Assert.Equal(g.Stones.Count, c.Values.Sum());
            Assert.Equal(g.MultiplierCount, c["Multiplier"]);
            Assert.Equal(g.Base, c["Basic"] * 1 + c["Fortress"] * 4 + c["Line"] + c["Multiplier"] + c["Synergy"]);
            int otherTypes = new[] { "Basic", "Fortress", "Line", "Multiplier" }.Count(t => c[t] > 0);
            Assert.Equal(g.SynergyBonus, c["Synergy"] * otherTypes * 2);
            if (g.LineBonus > 0)
            {
                Assert.True(c["Line"] >= 2, $"连珠加值 {g.LineBonus} 但连珠子计数 {c["Line"]}");
            }

            fortressAndBasic += c["Fortress"] > 0 && c["Basic"] > 0 ? 1 : 0;
            synergyWithBonus += g.SynergyBonus > 0 ? 1 : 0;
        }

        Assert.True(fortressAndBasic > 0, "样本里没有同时含普通子与堡垒子的棋串");
        Assert.True(synergyWithBonus > 0, "样本里没有协同加值");
    }

    [Fact]
    public void 各棋子势力占比按口径手算()
    {
        // proposal 口径，逐局取终局快照（最后一条小回合快照）中参赛玩家的全部棋串：
        //   P0 串 1：普通×3 + 堡垒×1 + 倍增×2，基础 9、生效 2、军势 20 → 放大部分 ⌊9 × 9/4⌋ − 9 = 11
        //            普通 3、堡垒 4、倍增 2 + 11 = 13
        //   P0 串 2：连珠×4 + 倍增×2，基础 6、连珠加值 12、军势 25 → 放大部分 ⌊6 × 9/4⌋ − 6 = 7
        //            连珠 4 + 12 = 16、倍增 2 + 7 = 9
        //   P1 串  ：倍增×5 + 协同×1，基础 6、协同加值 2、生效 3、军势 22 → 放大部分 ⌊6 × 27/8⌋ − 6 = 14
        //            倍增 5 + 14 = 19、协同 1 + 2 = 3
        //   P2 已弃赛：堡垒×3（军势 12）→ 不计。第 1 小回合快照（非终局）里的棋串 → 不计。
        //   合计：盘面 普通 3 / 堡垒 1 / 连珠 4 / 倍增 9 / 协同 1 = 18 枚；势力 3 / 4 / 16 / 41 / 3 = 67（= 20 + 25 + 22）。
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
                    (1, "Active", [Group(power: 22, @base: 6, multiplier: 5, synergy: 1, synergyBonus: 2)]),
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
        Assert.Equal((18L, 67L), (s.TotalStones, s.TotalPower));
        Assert.Equal(
            ["Basic:3/3", "Fortress:1/4", "Line:4/16", "Multiplier:9/41", "Synergy:1/3"],
            s.Pieces.Select(p => $"{p.Type}:{p.Stones}/{p.Power}"));
        PieceShare m = s.Pieces.Single(p => p.Type == "Multiplier");
        Assert.Equal(9.0 / 18, m.StoneShare, 9);
        Assert.Equal(41.0 / 67, m.PowerShare, 9);
        Assert.Equal(41.0 / 9, m.MeanPerStone, 9);
        Assert.Equal(1.0, s.Pieces.Sum(p => p.PowerShare), 9);

        string text = ReportWriter.Render(report);
        Assert.Contains("各棋子势力占比", text);
        Assert.Contains("- 纳入 1 局，跳过无棋子类型计数的旧日志 / 无快照局 1 局；盘面棋子 18 枚，归因势力 67", text);
        Assert.Contains("- 棋子 Multiplier：盘面 9 枚（50.0%），势力 41（61.2%），每颗平均 4.56", text);
        Assert.Contains("- 棋子 Fortress：盘面 1 枚（5.6%），势力 4（6.0%），每颗平均 4", text);
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

    private static GroupEntry Group(long power, int @base, int basic = 0, int fortress = 0, int line = 0, int multiplier = 0, int synergy = 0, int lineBonus = 0, int synergyBonus = 0) =>
        new()
        {
            Stones = [.. Enumerable.Range(0, basic + fortress + line + multiplier + synergy).Select(i => $"Z{i}")],
            Base = @base,
            LineBonus = lineBonus,
            SynergyBonus = synergyBonus,
            MultiplierCount = multiplier,
            Power = power,
            PieceCounts = new Dictionary<string, int> { ["Basic"] = basic, ["Fortress"] = fortress, ["Line"] = line, ["Multiplier"] = multiplier, ["Synergy"] = synergy },
        };
}
