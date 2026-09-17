using System.Text.RegularExpressions;
using Siege.Core.Board;
using Siege.Core.Scoring;
using Siege.Presentation.Preview;

namespace Siege.Core.Tests.PowerScore;

/// <summary>规格：power-score —— Requirement: 棋串军势公式</summary>
public class 棋串军势公式Tests
{
    [Fact]
    public void 设计文档标准算例()
    {
        // 设计文档 §10.1：普通子×3、堡垒子×1、倍增子×2，无位置加值 → 基础 9、倍率 2.25、军势 ⌊9 × 2.25⌋ + 0 = 20（multiplier-rebalance：无位置加值，结果不变）
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
        // multiplier-rebalance power-score 规格：基础 6、位置加值 5、1 枚倍增子 → ⌊6 × 1.5⌋ + 5 = 9 + 5 = 14，而非 ⌊(6 + 5) × 1.5⌋ = 16
        // multiplier-rebalance 改写：原期望 16（旧公式加值进倍率）。
        // 位置加值来源（连珠 L×(L−1)、协同 ×2）在盘面上总是偶数，规格的"加值 5"只能在公式层验证；公式是计算层唯一的组装实现。
        // 变异验证 M-MR1：GroupPowerOf 改回 Apply(baseTotal + positionBonus)（加值重新放进倍率）→ 红 5，含本测试（16）。
        // 变异验证 M-MR3：取整放到加值之后（⌊(基础 × 3^e + 加值 × 2^e) / 2^e⌋）→ 红 0：加值是整数，与 ⌊基础 × 倍率⌋ + 加值 恒等（design.md D2），属等价变异，不为它造测试。
        Assert.Equal(14, PowerCalculator.GroupPowerOf(baseTotal: 6, positionBonus: 5, multiplierCount: 1));
        Assert.NotEqual(16, PowerCalculator.GroupPowerOf(baseTotal: 6, positionBonus: 5, multiplierCount: 1));
    }

    [Fact]
    public void 位置加值不被倍率放大()
    {
        // multiplier-rebalance power-score 规格：4 枚连珠子连成横线 + 2 枚倍增子，无协同子 → 基础 6、连珠加值 4 × 3 = 12、
        // 军势 ⌊6 × 2.25⌋ + 12 = 13 + 12 = 25，而非 ⌊18 × 2.25⌋ = 40。
        // 盘面上的整条链路（棋串 → 明细 → 预演文案）都要表达"加值在倍率之后"。
        // 变异验证 M-MR1（加值重新放进倍率）→ 红 5，含本测试（40）；M-MR10（预演文案恢复"（基础 + 加值）× 倍率"）→ 红 2，含本测试。
        GroupPower group = Assert.Single(PowerCalculator.Compute(Row("LLLLMM")).Of(TestMaps.P0).Groups);

        Assert.Equal(6, group.BaseTotal);
        Assert.Equal((12, 0), (group.LineBonus, group.SynergyBonus));
        Assert.Equal((2, 2), (group.MultiplierCount, group.EffectiveMultiplierCount));
        Assert.Equal("2.25", group.Multiplier.ToString());
        Assert.Equal(25, group.Power);
        Assert.NotEqual(40, group.Power);
        Assert.Equal("基础 6 × 2.25 + 位置加值 12（连珠 12 / 协同 0 / 高地 0） = 25", GroupPowerView.From(group).FormulaText);
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
        Assert.Equal(32, p0.Total); // scoring-sites 2.7 改写：旧期望 32 + 领地分 → 32（无据点，独占空格不计分）
    }

    [Fact]
    public void 恰好第3枚倍增子()
    {
        // multiplier-rebalance power-score 规格：普通子×4、倍增子×3，无位置加值 → 基础 7、生效倍率指数 3、倍率 3.375、军势 ⌊7 × 27 / 8⌋ = ⌊23.625⌋ = 23
        // multiplier-rebalance 改写：原「恰好第4枚倍增子」（BBBBMMMM → 基础 8、(4,4)、5.0625、40），封顶 4 → 3 后按规格改为 3 枚。
        // 变异验证 M-MR2（Multiplier.MaxExponent 改回 4）→ 本测试仍绿（n=3 在封顶 3 / 4 下同值），封顶由「第4枚倍增子只加基础军势」等抓。
        GroupPower group = Assert.Single(PowerCalculator.Compute(Row("BBBBMMM")).Of(TestMaps.P0).Groups);

        Assert.Equal(7, group.BaseTotal);
        Assert.Equal(0, group.PositionBonus);
        Assert.Equal((3, 3), (group.MultiplierCount, group.EffectiveMultiplierCount));
        Assert.Equal("3.375", group.Multiplier.ToString());
        Assert.Equal(23, group.Power);
    }

    [Fact]
    public void 第4枚倍增子只加基础军势()
    {
        // multiplier-rebalance power-score 规格：普通子×4、倍增子×4 → 数量 4、生效指数仍 3、基础 8、军势 ⌊8 × 27 / 8⌋ = 27，而非 ⌊8 × 1.5^4⌋ = 40
        // multiplier-rebalance 改写：原「第5枚倍增子只加基础军势」（BBBBMMMMM → (5,4)、81/16、45、≠68），封顶 4 → 3 后按规格改为第 4 枚。
        // 变异验证 M-MR2（Multiplier.MaxExponent 改回 4）→ 红 23（14 个方法，含取整边界两个 Theory 的 n=4..8 各行、Sim 回填与报告），含本测试（(4,4)、81/16、40）。
        GroupPower group = Assert.Single(PowerCalculator.Compute(Row("BBBBMMMM")).Of(TestMaps.P0).Groups);

        Assert.Equal(8, group.BaseTotal);
        Assert.Equal((4, 3), (group.MultiplierCount, group.EffectiveMultiplierCount));
        Assert.Equal((Int128)27, group.Multiplier.Numerator);
        Assert.Equal((Int128)8, group.Multiplier.Denominator);
        Assert.Equal(27, group.Power);
        Assert.NotEqual(40, group.Power);
    }

    [Fact]
    public void 封顶不影响其他效果()
    {
        // multiplier-rebalance power-score 规格：倍增子×7 + 协同子×1 → 协同加值 2（其他类型只有倍增子 1 种）、8 枚全部计入基础军势与共享气，只有倍率指数取 3。
        // 军势 ⌊8 × 27 / 8⌋ + 2 = 27 + 2 = 29（multiplier-rebalance 改写：原 (7,4)、⌊(8 + 2) × 81 / 16⌋ = 50）。气数与同位置 8 枚普通子的棋串逐格一致——封顶不得让第 4 枚起的倍增子脱离棋串。
        // 变异验证 M-MR2（封顶改回 4）→ 红 23，含本测试（⌊8 × 81 / 16⌋ + 2 = 42）；M-MR1（加值进倍率）→ 红 5，含本测试（⌊10 × 27 / 8⌋ = 33）。
        GameBoard mixed = Row("MMMMMMMS");
        GameBoard plain = Row("BBBBBBBB");

        GroupPower group = Assert.Single(PowerCalculator.Compute(mixed).Of(TestMaps.P0).Groups);
        Siege.Core.Board.Group mixedGroup = Assert.Single(mixed.GroupsOf(TestMaps.P0));
        Siege.Core.Board.Group plainGroup = Assert.Single(plain.GroupsOf(TestMaps.P0));

        Assert.Equal(8, group.Stones.Length);
        Assert.Equal(8, group.BaseTotal);
        Assert.Equal((0, 2), (group.LineBonus, group.SynergyBonus));
        Assert.Equal((7, 3), (group.MultiplierCount, group.EffectiveMultiplierCount));
        Assert.Equal(29, group.Power);
        Assert.Equal(plainGroup.Stones.Notations(), mixedGroup.Stones.Notations());
        Assert.Equal(plain.LibertiesOf(plainGroup).Notations(), mixed.LibertiesOf(mixedGroup).Notations());
    }

    [Fact]
    public void 倍率显示表达封顶()
    {
        // multiplier-rebalance power-score 规格：含 8 枚倍增子的棋串 → 倍率显示 3.375，并能同时得知数量 8、生效指数 3。军势 ⌊8 × 27 / 8⌋ = 27。
        // multiplier-rebalance 改写：封顶 4 时为显示 5.0625、生效 4、军势 40。
        // 变异验证 M-MR4（Multiplier.ToString 改用原始 Count 生成）→ 红 7（6 个方法），含本测试（25.62890625）。
        GroupPower group = Assert.Single(PowerCalculator.Compute(Row("MMMMMMMM")).Of(TestMaps.P0).Groups);

        Assert.Equal("3.375", group.Multiplier.ToString());
        Assert.Equal(8, group.MultiplierCount);
        Assert.Equal(3, group.EffectiveMultiplierCount);
        Assert.Equal(27, group.Power);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 2, 3)]
    [InlineData(2, 4, 9)]
    [InlineData(3, 8, 27)]
    [InlineData(4, 16, 54)]
    [InlineData(5, 32, 108)]
    [InlineData(6, 64, 216)]
    [InlineData(7, 128, 432)]
    [InlineData(8, 256, 864)]
    public void 倍率取整边界_恰为整数的值必须取到该整数(int count, int value, long expected)
    {
        // 强制回归（.trellis/spec/core/testing.md）：2^n × 1.5^min(n,3) 恰好落在整数上，各阶都不得因浮点误差少 1。
        // cap-multiplier：n ≥ 5 时倍率恒为 243/32，n = 6/7/8 三行期望值由 3^n 改为 2^(n−5) × 243（原 729 / 2187 / 6561）。
        // growth-pass-1：封顶 5 → 4，n ≥ 4 时倍率恒为 81/16，n = 5/6/7/8 四行改为 2^(n−4) × 81（原 243 / 486 / 972 / 1944）。
        // multiplier-rebalance：封顶 4 → 3，n ≥ 3 时倍率恒为 27/8，n = 4/5/6/7/8 五行改为 2^(n−3) × 27（原 81 / 162 / 324 / 648 / 1296）；n = 0..3 原值不变。
        // 变异验证 M4：Apply 改为 (long)Math.Floor(value * Math.Pow(1.5, Count) - 1e-9) → 红 21（本 Theory 各行、「计分路径不含浮点」等）。growth-pass-1 重跑：红 58。
        Assert.Equal(expected, new Multiplier(count).Apply(value));
    }

    [Theory]
    [InlineData(1, 3, 4)]      // 4.5
    [InlineData(2, 5, 11)]     // 11.25
    [InlineData(3, 9, 30)]     // 30.375
    [InlineData(4, 17, 57)]    // 57.375（封顶：17 × 27 / 8）
    [InlineData(5, 33, 111)]   // 111.375（封顶：33 × 27 / 8）
    [InlineData(6, 65, 219)]   // 219.375（封顶：65 × 27 / 8）
    [InlineData(7, 129, 435)]  // 435.375（封顶：129 × 27 / 8）
    [InlineData(8, 257, 867)]  // 867.375（封顶：257 × 27 / 8）
    public void 倍率取整边界_非整数向下取整(int count, int value, long expected)
    {
        // 2^n + 1 乘以 1.5^min(n,3) 一定不是整数，向下取整不得四舍五入。
        // cap-multiplier：n = 6/7/8 三行期望值改为封顶版（原 740 / 2204 / 6586）。
        // growth-pass-1：封顶 4，n = 5/6/7/8 四行改为 ⌊(2^n + 1) × 81 / 16⌋（原 250 / 493 / 979 / 1951）。
        // multiplier-rebalance：封顶 3，n = 4/5/6/7/8 五行改为 ⌊(2^n + 1) × 27 / 8⌋（原 86 / 167 / 329 / 653 / 1301）；n = 1..3 原值不变。
        // 变异验证 M17：Apply 改为 (scaled + Denominator/2) / Denominator（四舍五入）→ 红 9，含本 Theory 与「逐棋串取整」。growth-pass-1 重跑：红 25。
        Assert.Equal(expected, new Multiplier(count).Apply(value));
    }

    /// <summary>第 2 行从 x=1 起横放一串 P0 棋子：B 普通、L 连珠、M 倍增、S 协同。</summary>
    private static GameBoard Row(string types)
    {
        GameBoard board = TestMaps.Blank(size: 11);
        for (int i = 0; i < types.Length; i++)
        {
            PieceType type = types[i] switch
            {
                'B' => PieceType.Basic,
                'L' => PieceType.Line,
                'M' => PieceType.Multiplier,
                'S' => PieceType.Synergy,
                _ => throw new ArgumentOutOfRangeException(nameof(types), types, "只支持 B/L/M/S。"),
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

    [Fact]
    public void 高地加值不被倍率放大()
    {
        // 规格 Scenario（scoring-sites）：h=2 的棋串含普通子×2、倍增子×2，其中 3 枚各自压制到一枚更低处的敌子
        // → 基础 4、高地 3，军势 ⌊4 × 2.25⌋ + 3 = 12，而非 ⌊7 × 2.25⌋ = 15。
        // 变异验证 M-S12（段 A2）：Evaluate 把高地加值并进 baseTotal 传给 GroupPowerOf → 红，含本测试（15）。
        TerrainData terrain = TestMaps.Terrain(heights: [("B7", 2), ("C7", 2), ("D7", 2), ("E7", 2)]);
        GameBoard board = TestMaps.Blank(terrain)
            .Place("B7", TestMaps.P0).Place("C7", TestMaps.P0)
            .Place("D7", TestMaps.P0, PieceType.Multiplier).Place("E7", TestMaps.P0, PieceType.Multiplier)
            .Place("B6", TestMaps.P1).Place("C6", TestMaps.P1).Place("E6", TestMaps.P1);

        GroupPower group = PowerCalculator.Compute(board).GroupContaining(TestMaps.P0, "B7");

        Assert.Equal((4, 3, 2), (group.BaseTotal, group.HighGroundBonus, group.MultiplierCount));
        Assert.Equal((4 * 9 / 4) + 3, group.Power);
        Assert.Equal(12, group.Power);
    }
}
