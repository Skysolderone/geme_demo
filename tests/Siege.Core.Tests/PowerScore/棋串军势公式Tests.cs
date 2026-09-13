using System.Text.RegularExpressions;
using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.PowerScore;

/// <summary>规格：power-score —— Requirement: 棋串军势公式</summary>
public class 棋串军势公式Tests
{
    [Fact]
    public void 设计文档标准算例()
    {
        // 设计文档 §10.1：普通子×3、堡垒子×1、倍增子×2，无位置加值 → 基础 9、倍率 2.25、军势 ⌊9 × 2.25⌋ = 20
        // 变异验证 M29：Multiplier.Apply 改为先除后乘（value / 2^n × 3^n）→ 红 15，含本测试（⌊9/4⌋×9 = 18）。
        GameBoard board = TestMaps.Blank(size: 9).PlaceStandardGroup(TestMaps.P0, row: 2);

        GroupPower group = Assert.Single(PowerCalculator.Compute(board).Of(TestMaps.P0).Groups);

        Assert.Equal(9, group.BaseTotal);
        Assert.Equal(0, group.PositionBonus);
        Assert.Equal("2.25", group.Multiplier.ToString());
        Assert.Equal(20, group.Power);
    }

    [Fact]
    public void 含位置加值的计算()
    {
        // 基础 6、位置加值 5、1 枚倍增子 → ⌊(6 + 5) × 1.5⌋ = ⌊16.5⌋ = 16
        // 位置加值来源（连珠 L×(L−1)、协同 ×2）在盘面上总是偶数，规格的"加值 5"只能在公式层验证；公式是计算层唯一的取整实现。
        // 变异验证 M16：GroupPowerOf 改为 Apply(baseTotal) + positionBonus（加值不进倍率）→ 红 1（本测试，9+5 = 14）。
        Assert.Equal(16, PowerCalculator.GroupPowerOf(baseTotal: 6, positionBonus: 5, multiplierCount: 1));
    }

    [Fact]
    public void 逐棋串取整()
    {
        // 两条棋串未取整军势各 16.5（基础 11 × 1.5）→ 分别取整 16 + 16 = 32，MUST NOT 先求和 33 再取整。
        // 每条：堡垒子×1 + 普通子×6 + 倍增子×1 = 基础 11。
        // 变异验证 M1：Compute 改为把各串 (基础+加值)×3^n/2^n 通分累加后再做一次整数除法 → 红 1（本测试，33）。全套测试里只有它锁住取整位置。
        GameBoard board = TestMaps.Blank(size: 11);
        foreach (int row in new[] { 2, 8 })
        {
            board.Place($"B{row}", TestMaps.P0, PieceType.Fortress).Place($"J{row}", TestMaps.P0, PieceType.Multiplier);
            foreach (char col in "CDEFGH")
            {
                board.Place($"{col}{row}", TestMaps.P0, PieceType.Basic);
            }
        }

        PlayerPower p0 = PowerCalculator.Compute(board).Of(TestMaps.P0);

        Assert.Equal(2, p0.Groups.Length);
        Assert.All(p0.Groups, g => Assert.Equal((11, 1, 16L), (g.BaseTotal, g.MultiplierCount, g.Power)));
        Assert.Equal(32, p0.GroupPowerSum());
        Assert.Equal(32 + p0.TerritoryScore, p0.Total);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 2, 3)]
    [InlineData(2, 4, 9)]
    [InlineData(3, 8, 27)]
    [InlineData(4, 16, 81)]
    [InlineData(5, 32, 243)]
    [InlineData(6, 64, 729)]
    [InlineData(7, 128, 2187)]
    [InlineData(8, 256, 6561)]
    public void 倍率取整边界_恰为整数的值必须取到该整数(int count, int value, long expected)
    {
        // 强制回归（.trellis/spec/core/testing.md）：2^n × 1.5^n = 3^n 恰好落在整数上，各阶都不得因浮点误差少 1。
        // 变异验证 M4：Apply 改为 (long)Math.Floor(value * Math.Pow(1.5, Count) - 1e-9) → 红 21（本 Theory 各行、「计分路径不含浮点」等）。
        Assert.Equal(expected, new Multiplier(count).Apply(value));
    }

    [Theory]
    [InlineData(1, 3, 4)]      // 4.5
    [InlineData(2, 5, 11)]     // 11.25
    [InlineData(3, 9, 30)]     // 30.375
    [InlineData(4, 17, 86)]    // 86.0625
    [InlineData(5, 33, 250)]   // 250.59375
    [InlineData(6, 65, 740)]   // 740.390625
    [InlineData(7, 129, 2204)] // 2204.0859375
    [InlineData(8, 257, 6586)] // 6586.62890625
    public void 倍率取整边界_非整数向下取整(int count, int value, long expected)
    {
        // 2^n + 1 乘以 1.5^n 一定不是整数，向下取整不得四舍五入。
        // 变异验证 M17：Apply 改为 (scaled + Denominator/2) / Denominator（四舍五入）→ 红 9，含本 Theory 与「逐棋串取整」。
        Assert.Equal(expected, new Multiplier(count).Apply(value));
    }

    [Fact]
    public void 计分路径不含浮点()
    {
        // .trellis/spec/core/determinism.md：禁止 double / float / decimal 出现在任何计分、倍率、取整路径上。
        // 扫描 Scoring 目录全部源码；Math.Floor / Math.Pow 也一并禁止——它们只会配合浮点出现。
        // 变异验证 M4：Multiplier.Apply 改用 Math.Floor / Math.Pow → 本测试红（与取整边界 Theory 同时红）。
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "siege.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        string scoring = Path.Combine(dir.FullName, "src", "Siege.Core", "Scoring");
        string[] files = Directory.GetFiles(scoring, "*.cs");
        Assert.NotEmpty(files);
        // 不列 Single：LINQ 的 .Single() 会误伤；System.Single 由 float 关键字与 Math.* 一并挡住。
        var forbidden = new Regex(@"\b(double|float|decimal|Double|Decimal|Math\.(Floor|Ceiling|Round|Pow))\b");

        foreach (string file in files)
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                Assert.False(forbidden.IsMatch(lines[i]), $"{Path.GetFileName(file)}:{i + 1} 出现浮点：{lines[i].Trim()}");
            }
        }
    }
}
