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
        // 变异验证 M29：Multiplier.Apply 改为先除后乘（value / 2^n × 3^n）→ 红 15，含本测试（⌊9/4⌋×9 = 18）。growth-pass-1 重跑（封顶 4）：红 41，含本测试。
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

    [Fact]
    public void 恰好第4枚倍增子()
    {
        // growth-pass-1 power-score 规格：普通子×4、倍增子×4，无位置加值 → 基础 8、生效倍率指数 4、倍率 5.0625、军势 ⌊8 × 81 / 16⌋ = ⌊40.5⌋ = 40
        // growth-pass-1 改写：原「恰好第5枚倍增子」（BBBBMMMMM → 基础 9、7.59375、68），封顶 5 → 4 后按规格改为 4 枚。
        // 变异验证 M-M2：Multiplier.Exponent 去掉 Math.Min（=> Count）→ 本测试仍绿（n=4 恰在顶上），封顶由「第5枚倍增子只加基础军势」等抓。
        GroupPower group = Assert.Single(PowerCalculator.Compute(Row("BBBBMMMM")).Of(TestMaps.P0).Groups);

        Assert.Equal(8, group.BaseTotal);
        Assert.Equal(0, group.PositionBonus);
        Assert.Equal((4, 4), (group.MultiplierCount, group.EffectiveMultiplierCount));
        Assert.Equal("5.0625", group.Multiplier.ToString());
        Assert.Equal(40, group.Power);
    }

    [Fact]
    public void 第5枚倍增子只加基础军势()
    {
        // growth-pass-1 power-score 规格：普通子×4、倍增子×5 → 数量 5、生效指数仍 4、基础 9、军势 ⌊9 × 81 / 16⌋ = ⌊45.56⌋ = 45，而非 ⌊9 × 1.5^5⌋ = 68
        // growth-pass-1 改写：原「第6枚倍增子只加基础军势」（BBBBMMMMMM → (6,5)、243/32、75、≠113），封顶 5 → 4 后按规格改为第 5 枚。
        // 变异验证 M-GP5（Multiplier.MaxExponent 改回 5）→ 全套红 18，含本测试（(5,5)、243/32、68）、取整边界两个 Theory 的 n=5..8 各行、Sim 回填与报告双列。
        // 变异验证 M-M2（growth-pass-1 重跑）：Multiplier.Exponent 去掉 Math.Min（=> Count）→ 红 16，含本测试（68）；「恰好第4枚倍增子」仍绿（n=4 恰在顶上）。
        GroupPower group = Assert.Single(PowerCalculator.Compute(Row("BBBBMMMMM")).Of(TestMaps.P0).Groups);

        Assert.Equal(9, group.BaseTotal);
        Assert.Equal((5, 4), (group.MultiplierCount, group.EffectiveMultiplierCount));
        Assert.Equal((Int128)81, group.Multiplier.Numerator);
        Assert.Equal((Int128)16, group.Multiplier.Denominator);
        Assert.Equal(45, group.Power);
        Assert.NotEqual(68, group.Power);
    }

    [Fact]
    public void 封顶不影响其他效果()
    {
        // growth-pass-1 power-score 规格：倍增子×7 + 协同子×1 → 协同加值 2（其他类型只有倍增子 1 种）、8 枚全部计入基础军势与共享气，只有倍率指数取 4。
        // 军势 ⌊(8 + 2) × 81 / 16⌋ = ⌊50.625⌋ = 50（growth-pass-1 改写：封顶 5 时为 (7,5)、75）。气数与同位置 8 枚普通子的棋串逐格一致——封顶不得让第 5 枚起的倍增子脱离棋串。
        // 变异验证 M-M2：Exponent 去掉 Math.Min → 红 16，含本测试（⌊10 × 2187 / 128⌋ = 170）；M-M4（生效指数退化为原始数量）→ 红 6，含本测试（growth-pass-1 重跑）。
        GameBoard mixed = Row("MMMMMMMS");
        GameBoard plain = Row("BBBBBBBB");

        GroupPower group = Assert.Single(PowerCalculator.Compute(mixed).Of(TestMaps.P0).Groups);
        Siege.Core.Board.Group mixedGroup = Assert.Single(mixed.GroupsOf(TestMaps.P0));
        Siege.Core.Board.Group plainGroup = Assert.Single(plain.GroupsOf(TestMaps.P0));

        Assert.Equal(8, group.Stones.Length);
        Assert.Equal(8, group.BaseTotal);
        Assert.Equal((0, 2), (group.LineBonus, group.SynergyBonus));
        Assert.Equal((7, 4), (group.MultiplierCount, group.EffectiveMultiplierCount));
        Assert.Equal(50, group.Power);
        Assert.Equal(plainGroup.Stones.Notations(), mixedGroup.Stones.Notations());
        Assert.Equal(plain.LibertiesOf(plainGroup).Notations(), mixed.LibertiesOf(mixedGroup).Notations());
    }

    [Fact]
    public void 倍率显示表达封顶()
    {
        // growth-pass-1 power-score 规格：含 8 枚倍增子的棋串 → 倍率显示 5.0625，并能同时得知数量 8、生效指数 4。军势 ⌊8 × 81 / 16⌋ = 40。
        // growth-pass-1 改写：封顶 5 时为显示 7.59375、生效 5、军势 60。
        // 变异验证 M-GP6（= cap-multiplier 的 M-M3，growth-pass-1 重跑）：Multiplier.ToString 改用原始 Count 生成 → 红 5（本测试 25.62890625、「明细区分原始与生效倍率」、
        //「遥测峰值保留原始数量并可得生效指数」、「倍率指数封顶为4」、「倍率的精确十进制表示」n=5 行）。
        GroupPower group = Assert.Single(PowerCalculator.Compute(Row("MMMMMMMM")).Of(TestMaps.P0).Groups);

        Assert.Equal("5.0625", group.Multiplier.ToString());
        Assert.Equal(8, group.MultiplierCount);
        Assert.Equal(4, group.EffectiveMultiplierCount);
        Assert.Equal(40, group.Power);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 2, 3)]
    [InlineData(2, 4, 9)]
    [InlineData(3, 8, 27)]
    [InlineData(4, 16, 81)]
    [InlineData(5, 32, 162)]
    [InlineData(6, 64, 324)]
    [InlineData(7, 128, 648)]
    [InlineData(8, 256, 1296)]
    public void 倍率取整边界_恰为整数的值必须取到该整数(int count, int value, long expected)
    {
        // 强制回归（.trellis/spec/core/testing.md）：2^n × 1.5^min(n,4) 恰好落在整数上，各阶都不得因浮点误差少 1。
        // cap-multiplier：n ≥ 5 时倍率恒为 243/32，n = 6/7/8 三行期望值由 3^n 改为 2^(n−5) × 243（原 729 / 2187 / 6561）。
        // growth-pass-1：封顶 5 → 4，n ≥ 4 时倍率恒为 81/16，n = 5/6/7/8 四行改为 2^(n−4) × 81（原 243 / 486 / 972 / 1944）；n = 0..4 原值不变。
        // 变异验证 M4：Apply 改为 (long)Math.Floor(value * Math.Pow(1.5, Count) - 1e-9) → 红 21（本 Theory 各行、「计分路径不含浮点」等）。growth-pass-1 重跑：红 58（本 Theory 各行、非整数 Theory n=5..8、「计分路径不含浮点」等）。
        Assert.Equal(expected, new Multiplier(count).Apply(value));
    }

    [Theory]
    [InlineData(1, 3, 4)]      // 4.5
    [InlineData(2, 5, 11)]     // 11.25
    [InlineData(3, 9, 30)]     // 30.375
    [InlineData(4, 17, 86)]    // 86.0625
    [InlineData(5, 33, 167)]   // 167.0625（封顶：33 × 81 / 16）
    [InlineData(6, 65, 329)]   // 329.0625（封顶：65 × 81 / 16）
    [InlineData(7, 129, 653)]  // 653.0625（封顶：129 × 81 / 16）
    [InlineData(8, 257, 1301)] // 1301.0625（封顶：257 × 81 / 16）
    public void 倍率取整边界_非整数向下取整(int count, int value, long expected)
    {
        // 2^n + 1 乘以 1.5^min(n,4) 一定不是整数，向下取整不得四舍五入。
        // cap-multiplier：n = 6/7/8 三行期望值改为封顶版（原 740 / 2204 / 6586）。
        // growth-pass-1：封顶 4，n = 5/6/7/8 四行改为 ⌊(2^n + 1) × 81 / 16⌋（原 250 / 493 / 979 / 1951）；n = 0..4 原值不变。
        // 变异验证 M17：Apply 改为 (scaled + Denominator/2) / Denominator（四舍五入）→ 红 9，含本 Theory 与「逐棋串取整」。growth-pass-1 重跑：红 25，含本 Theory n=1 行与「逐棋串取整」。
        Assert.Equal(expected, new Multiplier(count).Apply(value));
    }

    /// <summary>第 2 行从 x=1 起横放一串 P0 棋子：B 普通、M 倍增、S 协同。</summary>
    private static GameBoard Row(string types)
    {
        GameBoard board = TestMaps.Blank(size: 11);
        for (int i = 0; i < types.Length; i++)
        {
            PieceType type = types[i] switch
            {
                'B' => PieceType.Basic,
                'M' => PieceType.Multiplier,
                'S' => PieceType.Synergy,
                _ => throw new ArgumentOutOfRangeException(nameof(types), types, "只支持 B/M/S。"),
            };
            board.Place(new Coord(i + 1, 2), TestMaps.P0, type);
        }

        return board;
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
