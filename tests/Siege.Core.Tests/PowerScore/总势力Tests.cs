using System.Reflection;
using System.Runtime.CompilerServices;
using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.PowerScore;

/// <summary>规格：power-score —— Requirement: 总势力</summary>
public class 总势力Tests
{
    [Fact]
    public void 据点与棋串相加()
    {
        // 规格 Scenario（scoring-sites，取代原「领地与棋串相加」）：控制 1 个营帐与 1 个石碑（标准局 5 / 15 / 45），棋串军势 20 与 7 → 5 + 45 + 20 + 7 = 77。
        // 第 2 行：§10.1 标准算例棋串（20），营帐 H2 紧贴 G2；第 6 行：堡垒子 + 普通子×3（7），石碑 F6 紧贴 E6。
        // 变异验证 M20：Compute 的 Total 改为只累加 groupTotal（漏掉据点分）→ 红，含本测试（27）。
        GameBoard board = TestMaps.Blank(size: 11)
            .WithSites(("H2", SiteTier.Tent), ("F6", SiteTier.Stele))
            .PlaceStandardGroup(TestMaps.P0, row: 2)
            .Place("B6", TestMaps.P0, PieceType.Fortress).Place("C6", TestMaps.P0).Place("D6", TestMaps.P0).Place("E6", TestMaps.P0);

        PlayerPower p0 = PowerCalculator.Compute(board).Of(TestMaps.P0);

        Assert.Equal(50, p0.SiteScore);
        Assert.Equal(new long[] { 20, 7 }, p0.Groups.Select(g => g.Power));
        Assert.Equal(77, p0.Total);
    }

    [Fact]
    public void 独占空格不计分()
    {
        // 规格 Scenario：12 个独占空格、不控制任何据点、棋串军势合计 27 → 总势力 27。
        // 盘面沿用原「领地与棋串相加」（旧期望 12 + 20 + 7 = 39 → 新期望 27）：两串天然相邻空格 14 + 10，用障碍削到 12。
        // 变异验证 M-S5（段 A2）：Total 加回 exclusive.Length → 红，含本测试（39）。
        GameBoard board = TestMaps.Blank(size: 11, "A2", "A6", "B1", "C1", "D1", "E1", "F1", "G1", "B7", "C7", "D7", "E7")
            .PlaceStandardGroup(TestMaps.P0, row: 2)
            .Place("B6", TestMaps.P0, PieceType.Fortress).Place("C6", TestMaps.P0).Place("D6", TestMaps.P0).Place("E6", TestMaps.P0);

        PlayerPower p0 = PowerCalculator.Compute(board).Of(TestMaps.P0);

        Assert.Equal(12, p0.ExclusiveCells.Length);
        Assert.Empty(p0.Sites);
        Assert.Equal(new long[] { 20, 7 }, p0.Groups.Select(g => g.Power));
        Assert.Equal(27, p0.Total);
    }

    [Fact]
    public void 孤立棋子的势力()
    {
        // 规格 Scenario（由原 领地分Tests「孤立棋子的势力上限」改写迁入）：平地中央无竞争的孤立普通子，四邻为空的非据点可落子格 → 势力 1，四个独占空格不计分。
        // 旧期望 5（军势 1 + 领地 4）→ 新期望 1。
        GameBoard board = TestMaps.Blank().Place("F6", TestMaps.P0);

        PlayerPower p0 = PowerCalculator.Compute(board).Of(TestMaps.P0);

        Assert.Equal(["F5", "E6", "G6", "F7"], p0.ExclusiveCells.Notations());
        GroupPower group = Assert.Single(p0.Groups);
        Assert.Equal((1, 1L), (group.BaseTotal, group.Power));
        Assert.Equal(1, p0.Total);
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
        // scoring-sites 2.7 改写（盘面不变，数值按新公式）：旧期望 80（64 + 16 独占）→ 12（8 + 4 独占）；新期望 64 → 8（独占空格不计分）。
        // 64：堡垒子×4 + 倍增子×3（基础 19 × 3.375 = 64）；摧毁后剩 B2/C2 两枚堡垒（8）。
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
        Assert.Equal(64, scoreboard.Recalculate(board, roster, SiteValues.Standard, 5).Of(TestMaps.P0).Total);

        board.RemoveStones(new[] { "D2", "E2", "F2", "G2", "H2" }.Select(TestMaps.At));
        board.Place("B4", TestMaps.P1).Place("C4", TestMaps.P1);

        PlayerPower now = scoreboard.Recalculate(board, roster, SiteValues.Standard, 6).Of(TestMaps.P0);
        Assert.Equal(8, now.Total);
        Assert.Equal(8, scoreboard.Latest!.Of(TestMaps.P0).Total);
        string[] historyWords = ["History", "Accumulated", "Cumulative", "Previous", "Peak", "Max"];
        Assert.DoesNotContain(
            typeof(PlayerPower).GetProperties().Select(p => p.Name),
            name => historyWords.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase)));
    }
}
