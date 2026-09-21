using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.PowerScore;

/// <summary>规格：power-score —— Requirement: 总势力</summary>
public class 总势力Tests
{
    [Fact]
    public void 领地与棋串相加()
    {
        // restore-go-core-rules power-score 规格：12 个独占空格，棋串军势 20 与 7 → 12 + 20 + 7 = 39。
        // 第 2 行：§10.1 标准算例棋串（20）；第 6 行：堡垒子 + 普通子×3（7）。两串天然相邻空格 14 + 10，用障碍削到 12。
        // 段 A 重算：原「独占空格不计分」期望 27（scoring-sites：独占空格只展示）→ 39 = 12 + 20 + 7（盘面不变）。
        // 变异验证 M-A4（段 A）：Compute 的 Total 漏掉领地分（只累加棋串军势）→ 红，含本测试（27）。
        GameBoard board = TestMaps.Blank(size: 11, "A2", "A6", "B1", "C1", "D1", "E1", "F1", "G1", "B7", "C7", "D7", "E7")
            .PlaceStandardGroup(TestMaps.P0, row: 2)
            .Place("B6", TestMaps.P0, PieceType.Fortress).Place("C6", TestMaps.P0).Place("D6", TestMaps.P0).Place("E6", TestMaps.P0);

        PlayerPower p0 = PowerCalculator.Compute(board).Of(TestMaps.P0);

        Assert.Equal(12, p0.ExclusiveCells.Length);
        Assert.Equal(12, p0.TerritoryScore);
        Assert.Equal(new BigInteger[] { 20, 7 }, p0.Groups.Select(g => g.Power));
        Assert.Equal(39, p0.Total);
    }

    [Fact]
    public void 争议格不计分()
    {
        // restore-go-core-rules power-score 规格：某空格同时被 A 与 B 覆盖 → 不向任何玩家计分。
        // P0 在 D4、P1 在 F4：E4 同时被两人覆盖（争议）；各自另有 3 个独占邻格 → 每人总势力 = 军势 1 + 领地 3 = 4，而非 5。
        // 变异验证 M-AC7（段 A check 实跑，与 implement.md 变异表的 M-A3 是同一条）：CoverageMap.ExclusiveCellsOf 把争议格也算作独占 → 红 18，含本测试（5）。
        GameBoard board = TestMaps.Blank(size: 9).Place("D4", TestMaps.P0).Place("F4", TestMaps.P1);

        PowerSnapshot snapshot = PowerCalculator.Compute(board);

        Assert.Equal(OwnershipKind.Contested, snapshot.Coverage.OwnershipOf(TestMaps.At("E4")).Kind);
        Assert.Equal(["D3", "C4", "D5"], snapshot.Of(TestMaps.P0).ExclusiveCells.Notations());
        Assert.Equal(["F3", "G4", "F5"], snapshot.Of(TestMaps.P1).ExclusiveCells.Notations());
        Assert.Equal((3, (BigInteger)4), (snapshot.Of(TestMaps.P0).TerritoryScore, snapshot.Of(TestMaps.P0).Total));
        Assert.Equal((3, (BigInteger)4), (snapshot.Of(TestMaps.P1).TerritoryScore, snapshot.Of(TestMaps.P1).Total));
    }

    [Fact]
    public void 孤立棋子的势力()
    {
        // restore-go-core-rules power-score 规格：平地中央无竞争的孤立普通子，四邻为空的可落子格 → 5 点势力（棋子军势 1 + 四个独占空格各 1）。
        // 段 A 重算：原期望 1（scoring-sites：独占空格不计分）→ 5 = 1 + 4。
        GameBoard board = TestMaps.Blank().Place("F6", TestMaps.P0);

        PlayerPower p0 = PowerCalculator.Compute(board).Of(TestMaps.P0);

        Assert.Equal(["F5", "E6", "G6", "F7"], p0.ExclusiveCells.Notations());
        GroupPower group = Assert.Single(p0.Groups);
        Assert.Equal((1, (BigInteger)1), (group.BaseTotal, group.Power));
        Assert.Equal(4, p0.TerritoryScore);
        Assert.Equal(5, p0.Total);
    }

    [Fact]
    public void 领地分不参与倍率()
    {
        // restore-go-core-rules power-score 规格：10 个独占空格 + 一条倍率 2.25、基础 4、无位置加值的棋串 → 10 + ⌊4 × 2.25⌋ = 19，10 点领地分 MUST NOT 被乘以 2.25。
        // 盘面：9×9 空图第 2 行 B–E 为普通子×2 + 倍增子×2；相邻空格 = 上 4 + 下 4 + 两端 A2 / F2 = 10。
        // 变异验证 M-AC5（段 A check 实跑）：Compute 的 Total 把领地分也乘上该玩家首条棋串的倍率 → 红 15，含本测试（⌊10 × 2.25⌋ + 9 = 31）。
        GameBoard board = TestMaps.Blank(size: 9)
            .Place("B2", TestMaps.P0).Place("C2", TestMaps.P0)
            .Place("D2", TestMaps.P0, PieceType.Multiplier).Place("E2", TestMaps.P0, PieceType.Multiplier);

        PlayerPower p0 = PowerCalculator.Compute(board).Of(TestMaps.P0);
        GroupPower group = Assert.Single(p0.Groups);

        Assert.Equal((4, 0, "2.25"), (group.BaseTotal, group.PositionBonus, group.Multiplier.ToString()));
        Assert.Equal(9, group.Power);
        Assert.Equal(10, p0.TerritoryScore);
        Assert.Equal(19, p0.Total);
    }

    [Fact]
    public void 领地计分直接取空格归属结果()
    {
        // design D2 / coverage-territory「空格归属三态」：领地计分 MUST 直接使用空格归属判定的结果，MUST NOT 另行统计覆盖——全仓库只有一处覆盖统计。
        // ① 行为：多人混战盘面（含争议格、林地、障碍）上，每名玩家的领地分 = 覆盖表里"独占且归该玩家"的格数（逐格读 OwnershipOf，不经 ExclusiveCellsOf）。
        // ② 源码：PowerCalculator 只经 ExclusiveCellsOf 取领地，不得自己遍历覆盖目标 / 邻格 / 覆盖者；全仓库调用 CoverageTargets( 的文件只在允许名单内
        //    （唯一实现 Adjacency、转发 GameBoard、覆盖表 CoverageMap、高地加值 PieceEffects）。src/godot 不在 sln 里，同样纳入文本扫描（testing.md）。
        // 变异验证 M-AC2（段 A check 实跑）：ComputeCore 的 Total 改为自己遍历 board.AllCoords() + coverage.OwnershipOf 统计独占格
        //（行为完全不变，exclusive 仍供明细）→ 全套只红本测试 1 条，红在 ② 的违禁词 OwnershipOf(。
        // 已知缺口（check 记录、未修，留给段 F 6.3）：把这份重复统计搬到 Scoring 下另一个文件、再由本类调用 → 0 红。
        // ② 只扫 PowerCalculator.cs 的违禁词 + 仓库级 CoverageTargets( 名单，挡不住 testing.md 说的"在别处照抄一份算式"。
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("C5", Surface.Forest)]), size: 9, "G6")
            .Place("C4", TestMaps.P0).Place("D4", TestMaps.P0, PieceType.Multiplier).Place("F4", TestMaps.P1).Place("F6", TestMaps.P1)
            .Place("D6", ScoringFixtures.P2);
        PowerSnapshot snapshot = PowerCalculator.Compute(board);
        Assert.Contains(board.AllCoords(), c => snapshot.Coverage.OwnershipOf(c).Kind == OwnershipKind.Contested);
        foreach (PlayerPower player in snapshot.Players)
        {
            int owned = board.AllCoords().Count(c => snapshot.Coverage.OwnershipOf(c) == new CellOwnership(OwnershipKind.Exclusive, player.Player));
            Assert.True(owned > 0);
            Assert.Equal(owned, player.TerritoryScore);
            Assert.Equal(owned + player.GroupPowerSum(), player.Total);
        }

        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "siege.sln")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        string calculator = File.ReadAllText(Path.Combine(root.FullName, "src", "Siege.Core", "Scoring", "PowerCalculator.cs"));
        Assert.Contains("coverage.ExclusiveCellsOf(", calculator);
        foreach (string forbidden in new[] { "CoverageTargets(", "Neighbors(", "CoverageOf(", "SourcesOf(", "UniqueCoverer(", "OwnershipOf(" })
        {
            Assert.DoesNotContain(forbidden, calculator);
        }

        string[] allowed = ["Adjacency.cs", "GameBoard.cs", "CoverageMap.cs", "PieceEffects.cs"];
        string[] sources = [.. new[] { "Siege.Core", "Siege.Sim", "Siege.Presentation", Path.Combine("godot", "scripts") }
            .SelectMany(dir => Directory.GetFiles(Path.Combine(root.FullName, "src", dir), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))];
        Assert.True(sources.Length >= 100, $"扫描到的源文件只有 {sources.Length} 个，口径可疑");   // 段 A 实测 141 个
        Assert.Contains(sources, f => f.Contains("godot", StringComparison.Ordinal));
        string[] callers = [.. sources.Where(f => File.ReadAllText(f).Contains("CoverageTargets(")).Select(f => Path.GetFileName(f)!).Order(StringComparer.Ordinal)];
        Assert.Contains("CoverageMap.cs", callers);   // 反面：判据在唯一实现所在文件里确实命中
        Assert.Equal(allowed.Order(StringComparer.Ordinal), callers);
    }

    [Fact]
    public void 势力不可消耗()
    {
        // 系统 MUST NOT 提供任何以势力值为代价的兑换：计分层的公开类型没有可写的势力字段，也没有任何消耗类接口。
        // 变异验证 M30：给 PlayerPower 加 `public PlayerPower Spend(long cost) => this with { Total = Total - cost }` → 红 1（本测试）。
        Type[] types = [typeof(PlayerPower), typeof(GroupPower), typeof(PowerSnapshot), typeof(RankGroup), typeof(PowerScoreboard)];
        string[] verbs = ["Spend", "Consume", "Deduct", "Pay", "Exchange", "Redeem", "Subtract", "Add", "Set"];

        foreach (Type type in types)
        {
            foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                MethodInfo? setter = prop.GetSetMethod(nonPublic: false);
                bool isInitOnly = setter?.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)) == true;
                Assert.True(setter is null || isInitOnly, $"{type.Name}.{prop.Name} 有公开 setter，势力可被外部改写");
            }

            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                Assert.DoesNotContain(verbs, v => method.Name.StartsWith(v, StringComparison.Ordinal));
            }
        }
    }

    [Fact]
    public void 势力不累计()
    {
        // 规格 Scenario：势力高位后棋串被摧毁 → 当前势力只反映当前盘面，不保留历史高位。
        // 规格算例 80 → 12。段 A 重算（盘面不变）：原期望 64 → 8（scoring-sites：独占空格不计分）→ 80 → 12。
        // 80 = 军势 64（堡垒子×4 + 倍增子×3：⌊19 × 27 / 8⌋ = ⌊64.125⌋）+ 独占 16（第 2 行 B–H 七子：上 7 + 下 7 + 两端 A2 / J2）；
        // 12 = 军势 8（剩 B2 / C2 两枚堡垒）+ 独占 4（B1 / C1 / A2 / D2；B3 / C3 同时被 P1 的 B4 / C4 覆盖，是争议格）。
        // 变异验证 M31：PlayerPower 加 `public long PeakTotal => Total;` → 红 1（本测试）；M20/M14 改动 Total 计算也让本测试红。
        GameBoard board = TestMaps.Blank(size: 11);
        foreach (char col in "BCDE")
        {
            board.Place($"{col}2", TestMaps.P0, PieceType.Fortress);
        }

        foreach (char col in "FGH")
        {
            board.Place($"{col}2", TestMaps.P0, PieceType.Multiplier);
        }

        var scoreboard = new PowerScoreboard();
        IReadOnlyDictionary<PlayerId, PlayerStatus> roster = ScoringFixtures.Roster((TestMaps.P0, PlayerStatus.Active), (TestMaps.P1, PlayerStatus.Active));
        Assert.Equal(80, scoreboard.Recalculate(board, roster, 5).Of(TestMaps.P0).Total);

        board.RemoveStones(new[] { "D2", "E2", "F2", "G2", "H2" }.Select(TestMaps.At));
        board.Place("B4", TestMaps.P1).Place("C4", TestMaps.P1);

        PlayerPower now = scoreboard.Recalculate(board, roster, 6).Of(TestMaps.P0);
        Assert.Equal((4, (BigInteger)12), (now.TerritoryScore, now.Total));
        Assert.Equal(12, scoreboard.Latest!.Of(TestMaps.P0).Total);
        string[] historyWords = ["History", "Accumulated", "Cumulative", "Previous", "Peak", "Max"];
        Assert.DoesNotContain(
            typeof(PlayerPower).GetProperties().Select(p => p.Name),
            name => historyWords.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase)));
    }
}
