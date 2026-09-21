using System.Numerics;
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
        Assert.Equal((BigInteger)9, group.Multiplier.Numerator);
        Assert.Equal((BigInteger)4, group.Multiplier.Denominator);
        Assert.Equal("2.25", group.Multiplier.ToString());
        Assert.Equal(4, group.Power); // ⌊2 × 2.25⌋ = ⌊4.5⌋
    }

    [Theory]
    [InlineData(0, "1")]
    [InlineData(1, "1.5")]
    [InlineData(3, "3.375")]
    [InlineData(4, "5.0625")]  // 段 A 重算（不封顶）：原 3.375（封顶 3）→ 15^4 / 10^4 = 50625 / 10000
    [InlineData(5, "7.59375")] // 段 A 重算（不封顶）：原 3.375（封顶 3）→ 15^5 / 10^5 = 759375 / 100000
    public void 倍率的精确十进制表示(int count, string expected)
    {
        Assert.Equal(expected, new Multiplier(count).ToString());
    }

    [Fact]
    public void 倍率不作用于领地分()
    {
        // restore-go-core-rules piece-effects 规格：10 个独占空格 + 一条倍率 2.25 的棋串（§10.1 标准算例，军势 20）→ 领地按原值 10 计入，总势力 30，
        // MUST NOT 为 ⌊10 × 2.25⌋ + 20 = 42。盘面取自 territory-power 时期的同名测试：标准算例棋串 B2–G2 天然有 14 个相邻空格，用障碍 A2 / H2 / B1 / C1 削到 10。
        // 段 A 改写：原「倍率不作用于据点分」（石碑 45 + 20 = 65）——据点分自本段起不计入总势力，按规格换回领地分算例。
        // 变异验证 M-AC5（段 A check 实跑）：Compute 的 Total 把领地分也乘上该玩家首条棋串的倍率 → 红 15，含本测试（42）。
        GameBoard board = TestMaps.Blank(size: 9, "A2", "H2", "B1", "C1").PlaceStandardGroup(TestMaps.P0, row: 2);

        PlayerPower p0 = PowerCalculator.Compute(board).Of(TestMaps.P0);

        Assert.Equal(10, p0.TerritoryScore);
        GroupPower group = Assert.Single(p0.Groups);
        Assert.Equal("2.25", group.Multiplier.ToString());
        Assert.Equal(20, group.Power);
        Assert.Equal(30, p0.Total);
        Assert.NotEqual(42, p0.Total);
    }

    [Fact]
    public void 倍率作用于位置加值()
    {
        // restore-go-core-rules piece-effects 规格：一条倍率 2.25 的棋串基础 6、有 12 点连珠位置加值 → ⌊(6 + 12) × 2.25⌋ = ⌊40.5⌋ = 40。
        // 纵向摆放（与 power-score「连珠加值被倍率放大」的横线互补，连珠走纵向扫描分支）：连珠子竖线 4 枚（加值 4 × 3 = 12）+ 末端两枚倍增子。
        // 对照组把两枚倍增子换成普通子（基础同为 6、加值同为 12、倍率 1）→ 军势 18；两条之差 = 40 − 18 = 22。
        // 段 A 重算：原「倍率不作用于位置加值」期望 倍增串 25（⌊6 × 9 / 4⌋ + 12）、差 7、军势 − 连珠加值 13 → 40、22、28；对照串 18 不变。
        // 变异验证 M-A1（加值挪到乘法之外）→ 红，含本测试（25）。
        GameBoard multiplied = VerticalLine(PieceType.Multiplier);
        GameBoard plain = VerticalLine(PieceType.Basic);

        GroupPower group = Assert.Single(PowerCalculator.Compute(multiplied).Of(TestMaps.P0).Groups);
        GroupPower control = Assert.Single(PowerCalculator.Compute(plain).Of(TestMaps.P0).Groups);

        Assert.Equal("2.25", group.Multiplier.ToString());
        Assert.Equal((6, 12, 0), (group.BaseTotal, group.LineBonus, group.SynergyBonus));
        Assert.Equal((6, 12, 0, 0), (control.BaseTotal, control.LineBonus, control.SynergyBonus, control.MultiplierCount));
        Assert.Equal(40, group.Power);
        Assert.Equal(18, control.Power);
        Assert.Equal(22, group.Power - control.Power);
    }

    [Fact]
    public void 倍率指数不封顶()
    {
        // restore-go-core-rules piece-effects 规格：12 枚倍增子、无其他棋子与位置加值 → 倍率 1.5^12，军势 ⌊12 × 3^12 / 2^12⌋ = ⌊12 × 531441 / 4096⌋ = 1556。
        // 段 A 重算：原「倍率指数封顶为3」期望 分子 27 / 分母 8 / 显示 3.375 / 军势 40 → 531441 / 4096 / 129.746337890625 / 1556；
        // 3 枚对照串 ⌊3 × 27 / 8⌋ = 10 不变，但不再与 12 枚同倍率。
        // 变异验证 M-A2（指数夹到 3）→ 红，含本测试（40）。
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
        Assert.Equal((BigInteger)531441, group.Multiplier.Numerator);
        Assert.Equal((BigInteger)4096, group.Multiplier.Denominator);
        Assert.Equal("129.746337890625", group.Multiplier.ToString());
        Assert.Equal(1556, group.Power);
        Assert.Equal("3.375", threeGroup.Multiplier.ToString());
        Assert.NotEqual(threeGroup.Multiplier.Numerator, group.Multiplier.Numerator);
        Assert.Equal(10, threeGroup.Power); // ⌊3 × 27 / 8⌋ = ⌊10.125⌋
    }

    [Fact]
    public void 大数值精确不抛出()
    {
        // design D1：倍率分子、棋串军势 MUST 用不会溢出的整数表示——再大的输入也给出精确值，不抛 OverflowException、不回绕、不饱和。
        // 段 A 改写：原「超出整数范围时响亮失败」断言 long 装不下时抛 OverflowException（封顶 + long 口径）；改为任意精度后该失败路径不存在。
        // 期望字面值由仓库外独立计算（python）：⌊10^6 × 3^60 / 2^60⌋、⌊3^81 / 2^81⌋、⌊(2^63 − 1) × 3 / 2⌋、⌊(2^63 − 1) × 3^60 / 2^60⌋。
        // 变异验证 M-AC3（段 A check 实跑）：Multiplier.Apply 的结果夹到 long.MaxValue（饱和）→ 红 2，含本测试（原标 M-A3，与 implement.md 的 M-A3 重号）。
        Assert.Equal(BigInteger.Parse("36768468716933021"), new Multiplier(60).Apply(1_000_000));
        Assert.Equal(BigInteger.Parse("183396897083556"), new Multiplier(81).Apply(1));
        Assert.Equal((BigInteger)long.MaxValue, new Multiplier(0).Apply(long.MaxValue));
        Assert.Equal(BigInteger.Parse("13835058055282163710"), new Multiplier(1).Apply(long.MaxValue));
        Assert.Equal(BigInteger.Parse("339129266201729628077586996891"), new Multiplier(60).Apply(long.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Multiplier(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Multiplier(1).Apply(-1));
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
