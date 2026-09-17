using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.PieceEffects;

/// <summary>规格：piece-effects —— Requirement: 倍增子的棋串倍率</summary>
public class 倍增子的棋串倍率Tests
{
    [Fact]
    public void 两枚倍增子()
    {
        // 2 枚倍增子 → 倍率 1.5^2 = 2.25 = 9/4
        // 变异验证 M27：Multiplier.Numerator 改为 BigInteger.Pow(3, Count + 1) → 红 28，含本测试。growth-pass-1 重跑（Pow(3, Exponent + 1)）：红 69，含本测试。
        GameBoard board = TestMaps.Blank(size: 9)
            .Place("B2", TestMaps.P0, PieceType.Multiplier).Place("C2", TestMaps.P0, PieceType.Multiplier);

        GroupPower group = Assert.Single(PowerCalculator.Compute(board).Of(TestMaps.P0).Groups);

        Assert.Equal(2, group.MultiplierCount);
        Assert.Equal((Int128)9, group.Multiplier.Numerator);
        Assert.Equal((Int128)4, group.Multiplier.Denominator);
        Assert.Equal("2.25", group.Multiplier.ToString());
        Assert.Equal(4, group.Power); // ⌊2 × 2.25⌋ = ⌊4.5⌋
    }

    [Theory]
    [InlineData(0, "1")]
    [InlineData(1, "1.5")]
    [InlineData(3, "3.375")]
    [InlineData(4, "3.375")] // multiplier-rebalance 改写：封顶 3，原 5.0625
    [InlineData(5, "3.375")] // multiplier-rebalance 改写：封顶 3，原 5.0625（growth-pass-1 之前 7.59375）
    public void 倍率的精确十进制表示(int count, string expected)
    {
        Assert.Equal(expected, new Multiplier(count).ToString());
    }

    [Fact]
    public void 倍率不作用于据点分()
    {
        // 规格 Scenario（scoring-sites，取代原「倍率不作用于领地」）：控制一个 45 分的石碑 + 一条倍率 2.25 的棋串（§10.1 标准算例，军势 20）
        // → 45 按原值计入，总势力 65，MUST NOT 为 ⌊45 × 2.25⌋ + 20 = 121。
        // scoring-sites 2.7 改写：旧期望 领地 10 + 20 = 30 → 新期望 石碑 45 + 20 = 65。石碑 H2 紧贴 G2，只被 P0 覆盖。
        // 变异验证 M-S6（段 A2）：Compute 把 siteScore 乘上该玩家任一棋串的倍率 → 红，含本测试。
        GameBoard board = TestMaps.Blank(size: 9).WithSites(("H2", SiteTier.Stele)).PlaceStandardGroup(TestMaps.P0, row: 2);

        PlayerPower p0 = PowerCalculator.Compute(board).Of(TestMaps.P0);

        Assert.Equal(new SiteHolding(TestMaps.At("H2"), SiteTier.Stele, 45, SiteControlKind.UniqueCoverage), Assert.Single(p0.Sites));
        GroupPower group = Assert.Single(p0.Groups);
        Assert.Equal("2.25", group.Multiplier.ToString());
        Assert.Equal(20, group.Power);
        Assert.Equal(65, p0.Total);
    }

    [Fact]
    public void 倍率不作用于位置加值()
    {
        // multiplier-rebalance piece-effects 规格：一条倍率 2.25 的棋串有 12 点连珠位置加值 → 12 点按原值计入棋串军势，MUST NOT 乘以 2.25。
        // 纵向摆放（与「位置加值不被倍率放大」的横线互补，连珠走纵向扫描分支）：连珠子 B2–B5 成竖线（加值 4 × 3 = 12）+ 倍增子 C5、D5。
        // 对照组把两枚倍增子换成普通子（基础同为 6、加值同为 12、倍率 1）→ 军势 18。两条的差 = 倍率只作用在基础 6 上放大出的 ⌊6 × 9 / 4⌋ − 6 = 7，
        // 而加值部分两条都恰好是 12：军势 − 连珠加值 = 13（倍增串）与 6（对照串）。
        // 变异验证 M-MR1（加值进倍率）→ 红 5，含本测试（倍增串 40、差 22）。
        GameBoard multiplied = VerticalLine(PieceType.Multiplier);
        GameBoard plain = VerticalLine(PieceType.Basic);

        GroupPower group = Assert.Single(PowerCalculator.Compute(multiplied).Of(TestMaps.P0).Groups);
        GroupPower control = Assert.Single(PowerCalculator.Compute(plain).Of(TestMaps.P0).Groups);

        Assert.Equal("2.25", group.Multiplier.ToString());
        Assert.Equal((6, 12, 0), (group.BaseTotal, group.LineBonus, group.SynergyBonus));
        Assert.Equal((6, 12, 0, 0), (control.BaseTotal, control.LineBonus, control.SynergyBonus, control.MultiplierCount));
        Assert.Equal(25, group.Power);
        Assert.Equal(18, control.Power);
        Assert.Equal(13, group.Power - group.LineBonus);
        Assert.Equal(7, group.Power - control.Power);
    }

    [Fact]
    public void 倍率指数封顶为3()
    {
        // multiplier-rebalance piece-effects 规格（growth-pass-1 的 4 调整为 3）：n ≥ 3 时倍率恒为 27/8 = 3.375，与含 3 枚倍增子时相同。
        // 12 枚倍增子 → 数量 12、生效指数 3、分子 27、分母 8，⌊12 × 27 / 8⌋ = ⌊40.5⌋ = 40（封顶 4 时为 60，封顶 5 时为 91，无上限版为 1556）。
        // piece-effects Scenario「倍率指数封顶为 3」：12 枚倍增子全部计入基础军势（基础 12）。
        // multiplier-rebalance 改写：原「倍率指数封顶为4」断言 MaxExponent 4、81/16、5.0625、60。
        // 变异验证 M-MR2（Multiplier.MaxExponent 改回 4）→ 红 23，含本测试（81/16、60）；M-MR4（ToString 用原始 Count）→ 红 7，含本测试。
        GameBoard board = TestMaps.Blank(size: 15);
        for (int x = 1; x <= 12; x++)
        {
            board.Place(new Coord(x, 2), TestMaps.P0, PieceType.Multiplier);
        }

        GameBoard three = TestMaps.Blank(size: 15);
        for (int x = 1; x <= 3; x++)
        {
            three.Place(new Coord(x, 2), TestMaps.P0, PieceType.Multiplier);
        }

        GroupPower group = Assert.Single(PowerCalculator.Compute(board).Of(TestMaps.P0).Groups);
        GroupPower threeGroup = Assert.Single(PowerCalculator.Compute(three).Of(TestMaps.P0).Groups);

        Assert.Equal(12, group.MultiplierCount);
        Assert.Equal(12, group.BaseTotal);
        Assert.Equal(Multiplier.MaxExponent, group.EffectiveMultiplierCount);
        Assert.Equal(3, Multiplier.MaxExponent);
        Assert.Equal((Int128)27, group.Multiplier.Numerator);
        Assert.Equal((Int128)8, group.Multiplier.Denominator);
        Assert.Equal("3.375", group.Multiplier.ToString());
        Assert.Equal(40, group.Power);
        Assert.Equal("3.375", threeGroup.Multiplier.ToString());
        Assert.Equal((threeGroup.Multiplier.Numerator, threeGroup.Multiplier.Denominator), (group.Multiplier.Numerator, group.Multiplier.Denominator));
        Assert.Equal(10, threeGroup.Power); // ⌊3 × 27 / 8⌋ = ⌊10.125⌋
    }

    [Fact]
    public void 超出整数范围时响亮失败()
    {
        // 封顶后 value × 3^3 = value × 27 不可能溢出 Int128，但结果仍可能装不进 long：以 long.MaxValue 为基础军势时 MUST 抛 OverflowException，不得静默回绕（checked 作防御保留）。
        // 原"n = 81 时 3^81 超出 Int128"的溢出路径已因封顶消失：n = 60 / 81 现在都按 27/8 算，⌊10^6 × 27 / 8⌋ = 3375000、⌊1 × 27 / 8⌋ = 3
        //（multiplier-rebalance 改写：封顶 4 时为 5062500 / 5；封顶 5 时为 7593750 / 7）。
        // 变异验证 C1（cap-multiplier 复跑）：Apply 改为 unchecked 的 long 乘法 → 红 1（本测试：回绕成错误值、且不再抛 OverflowException）。
        Assert.Equal(3_375_000L, new Multiplier(60).Apply(1_000_000));
        Assert.Equal(3L, new Multiplier(81).Apply(1));
        Assert.Equal(long.MaxValue, new Multiplier(0).Apply(long.MaxValue));
        Assert.Throws<OverflowException>(() => new Multiplier(1).Apply(long.MaxValue));
        Assert.Throws<OverflowException>(() => new Multiplier(5).Apply(long.MaxValue));
        Assert.Throws<OverflowException>(() => new Multiplier(60).Apply(long.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Multiplier(-1));
    }

    /// <summary>连珠子 (1,2)–(1,5) 成竖线，末端 (2,5)、(3,5) 两枚 <paramref name="tail"/>。</summary>
    private static GameBoard VerticalLine(PieceType tail)
    {
        GameBoard board = TestMaps.Blank(size: 11);
        for (int y = 2; y <= 5; y++)
        {
            board.Place(new Coord(1, y), TestMaps.P0, PieceType.Line);
        }

        board.Place(new Coord(2, 5), TestMaps.P0, tail);
        board.Place(new Coord(3, 5), TestMaps.P0, tail);
        return board;
    }
}
