using System.Numerics;
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
        // 设计文档 §10.1：普通子×3、堡垒子×1、倍增子×2，无位置加值 → 基础 9、倍率 2.25、军势 ⌊(9 + 0) × 2.25⌋ = 20（新旧公式同值，未改期望）
        // 变异验证 M29：Multiplier.Apply 改为先除后乘（value / 2^n × 3^n）→ 红 15，含本测试（⌊9/4⌋×9 = 18）。growth-pass-1 重跑（封顶 4）：红 41，含本测试。
        GameBoard board = TestMaps.Blank(size: 9).PlaceStandardGroup(TestMaps.P0, row: 2);

        GroupPower group = Assert.Single(PowerCalculator.Compute(board).Of(TestMaps.P0).Groups);

        Assert.Equal(9, group.BaseTotal);
        Assert.Equal(0, group.PositionBonus);
        Assert.Equal("2.25", group.Multiplier.ToString());
        Assert.Equal(20, group.Power);
    }

    [Fact]
    public void 位置加值被倍率放大()
    {
        // restore-go-core-rules power-score 规格：基础 6、位置加值 5、1 枚倍增子 → ⌊(6 + 5) × 1.5⌋ = 16，而非 ⌊6 × 1.5⌋ + 5 = 14。
        // 段 A 重算：原「含位置加值的计算」期望 14（multiplier-rebalance：加值在倍率之外）→ 16 = ⌊11 × 3 / 2⌋。
        // 位置加值来源（连珠 L×(L−1)、协同 ×2）在盘面上总是偶数，规格的"加值 5"只能在公式层验证；公式是计算层唯一的组装实现。
        // 变异验证 M-A1（段 A）：GroupPowerOf 改为 Apply(baseTotal) + positionBonus（加值挪到乘法之外）→ 红，含本测试（14）。
        Assert.Equal(16, PowerCalculator.GroupPowerOf(baseTotal: 6, positionBonus: 5, multiplierCount: 1));
        Assert.NotEqual(14, PowerCalculator.GroupPowerOf(baseTotal: 6, positionBonus: 5, multiplierCount: 1));
    }

    [Fact]
    public void 连珠加值被倍率放大()
    {
        // restore-go-core-rules power-score 规格：4 枚连珠子连成横线 + 2 枚倍增子，无协同子、无高地 → 基础 6、连珠加值 4 × 3 = 12、
        // 军势 ⌊18 × 2.25⌋ = ⌊40.5⌋ = 40。段 A 重算：原「位置加值不被倍率放大」期望 25（⌊6 × 2.25⌋ + 12）→ 40 = ⌊18 × 9 / 4⌋。
        // 盘面上的整条链路（棋串 → 明细 → 预演文案）都要表达"加值在倍率之内"。
        // 变异验证 M-A1（加值挪到乘法之外）→ 红，含本测试（25）。
        GroupPower group = Assert.Single(PowerCalculator.Compute(Row("LLLLMM")).Of(TestMaps.P0).Groups);

        Assert.Equal(6, group.BaseTotal);
        Assert.Equal((12, 0, 0), (group.LineBonus, group.SynergyBonus, group.HighGroundBonus));
        Assert.Equal(2, group.MultiplierCount);
        Assert.Equal("2.25", group.Multiplier.ToString());
        Assert.Equal(40, group.Power);
        Assert.NotEqual(25, group.Power);
        Assert.Equal("（基础 6 + 位置加值 12（连珠 12 / 协同 0 / 高地 0））× 2.25 = 40", GroupPowerView.From(group).FormulaText);
    }

    [Fact]
    public void 高地加值被倍率放大()
    {
        // restore-go-core-rules power-score 规格：h=2 的棋串含普通子×2、倍增子×2，其中 3 枚各自压制到一枚更低处的敌子
        // → 基础 4、高地 3，军势 ⌊7 × 2.25⌋ = ⌊15.75⌋ = 15。段 A 重算：原「高地加值不被倍率放大」期望 12（⌊4 × 2.25⌋ + 3）→ 15 = ⌊7 × 9 / 4⌋。
        // 变异验证 M-A1（加值挪到乘法之外）→ 红，含本测试（12）。
        TerrainData terrain = TestMaps.Terrain(heights: [("B7", 2), ("C7", 2), ("D7", 2), ("E7", 2)]);
        GameBoard board = TestMaps.Blank(terrain)
            .Place("B7", TestMaps.P0).Place("C7", TestMaps.P0)
            .Place("D7", TestMaps.P0, PieceType.Multiplier).Place("E7", TestMaps.P0, PieceType.Multiplier)
            .Place("B6", TestMaps.P1).Place("C6", TestMaps.P1).Place("E6", TestMaps.P1);

        GroupPower group = PowerCalculator.Compute(board).GroupContaining(TestMaps.P0, "B7");

        Assert.Equal((4, 3, 2), (group.BaseTotal, group.HighGroundBonus, group.MultiplierCount));
        Assert.Equal((4 + 3) * 9 / 4, group.Power);
        Assert.Equal(15, group.Power);
    }

    [Fact]
    public void 逐棋串取整()
    {
        // 两条棋串未取整军势各 16.5（基础 11 × 1.5）→ 分别取整 16 + 16 = 32，MUST NOT 先求和 33 再取整。
        // 每条：堡垒子×1 + 普通子×6 + 倍增子×1 = 基础 11。
        // 变异验证 M1：Compute 改为把各串 (基础+加值)×3^n/2^n 通分累加后再做一次整数除法 → 红 1（本测试，33）。全套测试里只有它锁住取整位置。
        // 段 A 重算：总势力原期望 32（独占空格不计分）→ 32 + 独占空格 36 = 68。
        // 独占空格手数：每条棋串占第 r 行 B–J 共 8 格，上下两行各 8 格 + 左右两端 A、K 各 1 格 = 18；两条（第 2、8 行）相隔 5 行、无重叠 → 36。
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
        Assert.All(p0.Groups, g => Assert.Equal((11, 1, (BigInteger)16), (g.BaseTotal, g.MultiplierCount, g.Power)));
        Assert.Equal(32, p0.GroupPowerSum());
        Assert.Equal(36, p0.ExclusiveCells.Length);
        Assert.Equal(68, p0.Total);
    }

    [Fact]
    public void 倍率不封顶()
    {
        // restore-go-core-rules power-score 规格：普通子×4、倍增子×4，无位置加值 → 基础 8、倍率 1.5^4 = 81/16、军势 ⌊8 × 81 / 16⌋ = ⌊40.5⌋ = 40。
        // 段 A 重算：原「第4枚倍增子只加基础军势」期望 27（封顶 3：⌊8 × 27 / 8⌋）→ 40；分子 / 分母 27 / 8 → 81 / 16。
        // 变异验证 M-A2（段 A）：Multiplier 的分子 / 分母指数改为 Math.Min(Count, 3)（把指数夹到 3）→ 红 8，含本测试（27）。check 阶段独立重跑，红数与 implement.md 记录一致。
        GroupPower group = Assert.Single(PowerCalculator.Compute(Row("BBBBMMMM")).Of(TestMaps.P0).Groups);

        Assert.Equal(8, group.BaseTotal);
        Assert.Equal(0, group.PositionBonus);
        Assert.Equal(4, group.MultiplierCount);
        Assert.Equal((BigInteger)81, group.Multiplier.Numerator);
        Assert.Equal((BigInteger)16, group.Multiplier.Denominator);
        Assert.Equal("5.0625", group.Multiplier.ToString());
        Assert.Equal(40, group.Power);
        Assert.NotEqual(27, group.Power);
    }

    [Fact]
    public void 大指数精确不溢出()
    {
        // restore-go-core-rules power-score 规格：60 枚倍增子、无其他棋子与位置加值 → ⌊60 × 3^60 / 2^60⌋ 的精确整数值。
        // 期望字面值由仓库外独立计算（python：60*3**60//2**60 = 2206108123015）；中间值 60 × 3^60 ≈ 2.5 × 10^30 已超出 64 位。
        // 盘面：15×15 空图第 2–5 行各 15 枚倍增子，上下相邻连成一条 60 子棋串。
        // 变异验证 M-A2（指数夹到 3）→ 红，含本测试（⌊60 × 27 / 8⌋ = 202）。
        GameBoard board = TestMaps.Blank(size: 15);
        for (int y = 2; y <= 5; y++)
        {
            for (int x = 0; x < 15; x++)
            {
                board.Place(new Coord(x, y), TestMaps.P0, PieceType.Multiplier);
            }
        }

        GroupPower group = Assert.Single(PowerCalculator.Compute(board).Of(TestMaps.P0).Groups);

        Assert.Equal((60, 0, 60), (group.BaseTotal, group.PositionBonus, group.MultiplierCount));
        Assert.Equal(BigInteger.Parse("2206108123015"), group.Power);

        // n 的上界是地图可落子格数（边疆档 411 格）：3^411 远超 Int128（约 1.7 × 10^38），结果 75 位十进制，MUST NOT 溢出、饱和或损失精度。
        // 首尾各 12 位由仓库外独立计算（python：411*3**411//2**411）。
        // 变异验证 M-AC3（段 A check 实跑）：Multiplier.Apply 的结果夹到 long.MaxValue（饱和）→ 红 2：本测试 + 倍增子的棋串倍率.大数值精确不抛出。
        // 注：本条原标 M-A3，与 implement.md 变异表里的 M-A3（ExclusiveCellsOf 含争议格，红 18）重号；check 阶段实跑的一律改用 M-AC 系列编号。
        string huge = PowerCalculator.GroupPowerOf(baseTotal: 411, positionBonus: 0, multiplierCount: 411).ToString();
        Assert.Equal(75, huge.Length);
        Assert.StartsWith("971290841628", huge, StringComparison.Ordinal);
        Assert.EndsWith("911226360845", huge, StringComparison.Ordinal);
    }

    [Fact]
    public void 倍率显示为精确值()
    {
        // restore-go-core-rules power-score 规格：含 3 枚倍增子的棋串 → 倍率显示精确十进制 3.375。
        // 沿用原「恰好第3枚倍增子」的盘面（普通子×4、倍增子×3）：基础 7、军势 ⌊7 × 27 / 8⌋ = ⌊23.625⌋ = 23（新旧公式同值，未改期望）。
        // 另钉 8 枚倍增子：显示 15^8 / 10^8 = 25.62890625、军势 ⌊8 × 6561 / 256⌋ = ⌊205.03…⌋ = 205
        //（段 A 重算：原「倍率显示表达封顶」期望 3.375 / 27 → 25.62890625 / 205）。
        // 变异验证 M-A2（指数夹到 3）→ 红，含本测试（8 枚仍显示 3.375）。
        GroupPower three = Assert.Single(PowerCalculator.Compute(Row("BBBBMMM")).Of(TestMaps.P0).Groups);
        GroupPower eight = Assert.Single(PowerCalculator.Compute(Row("MMMMMMMM")).Of(TestMaps.P0).Groups);

        Assert.Equal((7, 0, 3), (three.BaseTotal, three.PositionBonus, three.MultiplierCount));
        Assert.Equal("3.375", three.Multiplier.ToString());
        Assert.Equal(23, three.Power);
        Assert.Equal("25.62890625", eight.Multiplier.ToString());
        Assert.Equal(205, eight.Power);
    }

    [Fact]
    public void 倍增子仍是棋串成员()
    {
        // piece-effects「倍增子的棋串倍率」正文：每一枚倍增子同时是棋串成员——计入基础军势、共享气、参与协同的类型计数。
        // 倍增子×7 + 协同子×1 → 协同加值 2（其他类型只有倍增子 1 种）、8 枚全部计入基础军势；气数与同位置 8 枚普通子的棋串逐格一致。
        // 段 A 重算：原「封顶不影响其他效果」期望 29（⌊8 × 27 / 8⌋ + 2）→ ⌊(8 + 2) × 3^7 / 2^7⌋ = ⌊21870 / 128⌋ = 170。
        GameBoard mixed = Row("MMMMMMMS");
        GameBoard plain = Row("BBBBBBBB");

        GroupPower group = Assert.Single(PowerCalculator.Compute(mixed).Of(TestMaps.P0).Groups);
        Siege.Core.Board.Group mixedGroup = Assert.Single(mixed.GroupsOf(TestMaps.P0));
        Siege.Core.Board.Group plainGroup = Assert.Single(plain.GroupsOf(TestMaps.P0));

        Assert.Equal(8, group.Stones.Length);
        Assert.Equal(8, group.BaseTotal);
        Assert.Equal((0, 2), (group.LineBonus, group.SynergyBonus));
        Assert.Equal(7, group.MultiplierCount);
        Assert.Equal(170, group.Power);
        Assert.Equal(plainGroup.Stones.Notations(), mixedGroup.Stones.Notations());
        Assert.Equal(plain.LibertiesOf(plainGroup).Notations(), mixed.LibertiesOf(mixedGroup).Notations());
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
        // 段 A 重算（不封顶）：n = 4..8 五行由封顶 3 的 2^(n−3) × 27（54 / 108 / 216 / 432 / 864）改为 3^n（81 / 243 / 729 / 2187 / 6561）；n = 0..3 原值不变。
        // 变异验证 M4：Apply 改为 (long)Math.Floor(value * Math.Pow(1.5, Count) - 1e-9) → 红（本 Theory 各行、「计分路径不含浮点」等）。
        Assert.Equal(expected, new Multiplier(count).Apply(value));
    }

    [Theory]
    [InlineData(1, 3, 4)]      // 4.5
    [InlineData(2, 5, 11)]     // 11.25
    [InlineData(3, 9, 30)]     // 30.375
    [InlineData(4, 17, 86)]    // 86.0625  = 81 + 81/16
    [InlineData(5, 33, 250)]   // 250.59…  = 243 + 243/32
    [InlineData(6, 65, 740)]   // 740.39…  = 729 + 729/64
    [InlineData(7, 129, 2204)] // 2204.08… = 2187 + 2187/128
    [InlineData(8, 257, 6586)] // 6586.62… = 6561 + 6561/256
    public void 倍率取整边界_非整数向下取整(int count, int value, long expected)
    {
        // (2^n + 1) × 1.5^n = 3^n + 3^n / 2^n 一定不是整数，向下取整不得四舍五入。
        // 段 A 重算（不封顶）：n = 4..8 五行由封顶 3 的 ⌊(2^n + 1) × 27 / 8⌋（57 / 111 / 219 / 435 / 867）改为 3^n + ⌊3^n / 2^n⌋（86 / 250 / 740 / 2204 / 6586）；n = 1..3 原值不变。
        // 变异验证 M17：Apply 改为 (scaled + Denominator/2) / Denominator（四舍五入）→ 红，含本 Theory 与「逐棋串取整」。
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
        // 段 F 6.4：改为递归扫描（此前 Directory.GetFiles 不递归，Scoring 下新增子目录会被静默漏扫），并钉文件数下界。
        // 变异验证 M-F2（段 F 实跑）：新建 Scoring/Probe/FloatProbe.cs（内含一个 double 常量）→ 红 2（本测试 + `UI层不含规则计算Tests.内核与表现层不出现浮点`，后者本来就递归扫全 Core）；改前本测试用不递归的 GetFiles，扫不到子目录。
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "siege.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        string scoring = Path.Combine(dir.FullName, "src", "Siege.Core", "Scoring");
        string[] files = Directory.GetFiles(scoring, "*.cs", SearchOption.AllDirectories);
        Assert.True(files.Length >= 8, $"Scoring 下只扫到 {files.Length} 个源文件，口径可疑");   // 段 F 实测 8 个（含 PowerNotation / BigIntegerJsonConverter）
        // 不列 Single：LINQ 的 .Single() 会误伤；System.Single 由 float 关键字与 Math.* 一并挡住。
        var forbidden = new Regex(@"\b(double|float|decimal|Double|Decimal|Math\.(Floor|Ceiling|Round|Pow))\b");

        foreach (string file in files)
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                Assert.False(forbidden.IsMatch(lines[i]), $"{Path.GetRelativePath(scoring, file)}:{i + 1} 出现浮点：{lines[i].Trim()}");
            }
        }
    }

    [Fact]
    public void 新来源一并被倍率放大()
    {
        // more-pieces-relics 规格 power-score「新来源一并被倍率放大」：信物格上的旗手子、铁链子、倍增子各 1 枚连成，旗手子没有其他相邻信物格 →
        // 基础 3，旗手 3、铁链 3 − 1 = 2，棋串军势 ⌊(3 + 3 + 2) × 1.5⌋ = 12。
        GameBoard board = TestMaps.WithRelicCells(["C5"])
            .Place("C5", TestMaps.P0, PieceType.Bannerman).Place("D5", TestMaps.P0, PieceType.Chain).Place("E5", TestMaps.P0, PieceType.Multiplier);

        GroupPower group = Assert.Single(PowerCalculator.Compute(board).Of(TestMaps.P0).Groups);

        Assert.Equal((3, 3, 2, 1), (group.BaseTotal, group.BannerBonus, group.ChainBonus, group.MultiplierCount));
        Assert.Equal(5, group.PositionBonus);
        Assert.Equal(12, group.Power);
    }
}
