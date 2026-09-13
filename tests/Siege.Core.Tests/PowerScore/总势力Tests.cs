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
        // 12 个独占空格 + 棋串军势 20 与 7 → 12 + 20 + 7 = 39
        // 第 2 行：§10.1 标准算例棋串（20）；第 6 行：堡垒子 + 普通子×3（7）。两串天然相邻空格 14 + 10，用障碍削到 12。
        // 变异验证 M20：Compute 的 Total 改为只累加 groupTotal（漏掉领地）→ 红 14，含本测试（27）。
        GameBoard board = TestMaps.Blank(size: 11, "A2", "A6", "B1", "C1", "D1", "E1", "F1", "G1", "B7", "C7", "D7", "E7")
            .PlaceStandardGroup(TestMaps.P0, row: 2)
            .Place("B6", TestMaps.P0, PieceType.Fortress).Place("C6", TestMaps.P0).Place("D6", TestMaps.P0).Place("E6", TestMaps.P0);

        PlayerPower p0 = PowerCalculator.Compute(board).Of(TestMaps.P0);

        Assert.Equal(12, p0.TerritoryScore);
        Assert.Equal(new long[] { 20, 7 }, p0.Groups.Select(g => g.Power));
        Assert.Equal(39, p0.Total);
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
        // 第 5 大回合势力 80，随后棋串被摧毁至 12 → 当前势力 12，不保留 80。
        // 80：堡垒子×4 + 倍增子×3（基础 19 × 3.375 = 64）+ 16 独占；摧毁后剩 B2/C2 两枚堡垒（8）+ 4 独占（B3/C3 被 P1 争议）。
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
        Assert.Equal(12, now.Total);
        Assert.Equal(12, scoreboard.Latest!.Of(TestMaps.P0).Total);
        string[] historyWords = ["History", "Accumulated", "Cumulative", "Previous", "Peak", "Max"];
        Assert.DoesNotContain(
            typeof(PlayerPower).GetProperties().Select(p => p.Name),
            name => historyWords.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase)));
    }
}
