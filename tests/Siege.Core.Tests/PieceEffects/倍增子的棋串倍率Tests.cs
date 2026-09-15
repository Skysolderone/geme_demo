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
    [InlineData(4, "5.0625")]
    [InlineData(5, "5.0625")] // growth-pass-1 改写：封顶 4，原 7.59375
    public void 倍率的精确十进制表示(int count, string expected)
    {
        Assert.Equal(expected, new Multiplier(count).ToString());
    }

    [Fact]
    public void 倍率不作用于领地()
    {
        // 10 个独占空格 + 一条倍率 2.25 的棋串（§10.1 标准算例，军势 20）→ 领地按原值 10 计入，总势力 30。
        // 标准算例棋串 B2–G2 天然有 14 个相邻空格，用障碍 A2/H2/B1/C1 削到 10。
        // 变异验证 M2：PowerCalculator.Compute 的 Total 把 exclusive.Length 也乘上该玩家最高倍率 → 红 9，含本测试（22+20=42）。
        GameBoard board = TestMaps.Blank(size: 9, "A2", "H2", "B1", "C1").PlaceStandardGroup(TestMaps.P0, row: 2);

        PlayerPower p0 = PowerCalculator.Compute(board).Of(TestMaps.P0);

        Assert.Equal(10, p0.TerritoryScore);
        GroupPower group = Assert.Single(p0.Groups);
        Assert.Equal("2.25", group.Multiplier.ToString());
        Assert.Equal(20, group.Power);
        Assert.Equal(30, p0.Total);
    }

    [Fact]
    public void 倍率指数封顶为4()
    {
        // growth-pass-1 piece-effects 规格（cap-multiplier 的 5 调整为 4）：n ≥ 4 时倍率恒为 81/16 = 5.0625。
        // 12 枚倍增子 → 数量 12、生效指数 4、分子 81、分母 16，⌊12 × 81 / 16⌋ = ⌊60.75⌋ = 60（封顶 5 时为 91，无上限版为 1556）。
        // piece-effects Scenario「倍率指数封顶为 4」：12 枚倍增子全部计入基础军势（基础 12）。
        // growth-pass-1 改写：原「倍率指数封顶为5」断言 MaxExponent 5、243/32、91。
        // 变异验证 M-GP5（growth-pass-1）：Multiplier.MaxExponent 改回 5 → 红 18，含本测试（243/32、91）；M-M2（去掉 Math.Min，growth-pass-1 重跑）→ 红 16，含本测试（1556）；M-GP6（ToString 用原始 Count）→ 红 5，含本测试。
        GameBoard board = TestMaps.Blank(size: 15);
        for (int x = 1; x <= 12; x++)
        {
            board.Place(new Coord(x, 2), TestMaps.P0, PieceType.Multiplier);
        }

        GroupPower group = Assert.Single(PowerCalculator.Compute(board).Of(TestMaps.P0).Groups);

        Assert.Equal(12, group.MultiplierCount);
        Assert.Equal(12, group.BaseTotal);
        Assert.Equal(Multiplier.MaxExponent, group.EffectiveMultiplierCount);
        Assert.Equal(4, Multiplier.MaxExponent);
        Assert.Equal((Int128)81, group.Multiplier.Numerator);
        Assert.Equal((Int128)16, group.Multiplier.Denominator);
        Assert.Equal("5.0625", group.Multiplier.ToString());
        Assert.Equal(60, group.Power);
    }

    [Fact]
    public void 超出整数范围时响亮失败()
    {
        // 封顶后 value × 3^4 = value × 81 不可能溢出 Int128，但结果仍可能装不进 long：以 long.MaxValue 为基础军势时 MUST 抛 OverflowException，不得静默回绕（checked 作防御保留）。
        // 原"n = 81 时 3^81 超出 Int128"的溢出路径已因封顶消失：n = 60 / 81 现在都按 81/16 算，⌊10^6 × 81 / 16⌋ = 5062500、⌊1 × 81 / 16⌋ = 5
        //（growth-pass-1 改写：封顶 5 时为 7593750 / 7）。
        // 变异验证 C1（cap-multiplier 复跑）：Apply 改为 unchecked 的 long 乘法 → 红 1（本测试：回绕成错误值、且不再抛 OverflowException）。
        Assert.Equal(5_062_500L, new Multiplier(60).Apply(1_000_000));
        Assert.Equal(5L, new Multiplier(81).Apply(1));
        Assert.Equal(long.MaxValue, new Multiplier(0).Apply(long.MaxValue));
        Assert.Throws<OverflowException>(() => new Multiplier(1).Apply(long.MaxValue));
        Assert.Throws<OverflowException>(() => new Multiplier(5).Apply(long.MaxValue));
        Assert.Throws<OverflowException>(() => new Multiplier(60).Apply(long.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Multiplier(-1));
    }
}
