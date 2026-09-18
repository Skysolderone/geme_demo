using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.PieceEffects;

/// <summary>规格：piece-effects —— Requirement: 五种原型棋子的基础军势（artisan-terrain-edit 段 A：类型增至六种）</summary>
public class 六种原型棋子的基础军势Tests
{
    [Theory]
    [InlineData(PieceType.Basic, 1)]
    [InlineData(PieceType.Fortress, 4)]
    [InlineData(PieceType.Line, 1)]
    [InlineData(PieceType.Multiplier, 1)]
    [InlineData(PieceType.Synergy, 1)]
    [InlineData(PieceType.Artisan, 1)]
    public void 各类型基础军势(PieceType type, int expected)
    {
        // 设计文档 §9.2 表 + artisan-terrain-edit 裁决 T-1：普通子 1、堡垒子 4、连珠子 1、倍增子 1、协同子 1、匠人 1
        Assert.Equal(expected, Siege.Core.Scoring.PieceEffects.BasePower(type));
    }

    [Fact]
    public void 军势表穷举六种类型()
    {
        // 守门（tasks 1.1）：军势表必须对每一种 PieceType 都有条目——漏掉任何一种，BasePower 会抛 ArgumentOutOfRangeException。
        // 断言不照抄实现表，而是用"结构性事实"表达：恰有六种类型；只有堡垒子是 4，其余五种都是 1。
        // 变异验证见测试报告 M-A1（BasePower 去掉 Artisan 分支 → 本测试抛异常红）、M-A2（Artisan => 4 → "只有堡垒子是 4"红）。
        PieceType[] all = Enum.GetValues<PieceType>();
        Assert.Equal(6, all.Length);
        Assert.Contains(PieceType.Artisan, all);
        int[] powers = [.. all.Select(Siege.Core.Scoring.PieceEffects.BasePower)];
        Assert.Equal([PieceType.Fortress], all.Where(t => Siege.Core.Scoring.PieceEffects.BasePower(t) == 4));
        Assert.Equal(5, powers.Count(p => p == 1));
        Assert.Equal(9, powers.Sum());
    }

    [Fact]
    public void 匠人按1计()
    {
        // 规格 piece-effects「匠人按 1 计」：匠人×2 + 堡垒子×1 → 基础军势 2×1 + 4 = 6。
        GameBoard board = TestMaps.Blank(size: 9)
            .Place("B2", TestMaps.P0, PieceType.Artisan)
            .Place("C2", TestMaps.P0, PieceType.Artisan)
            .Place("D2", TestMaps.P0, PieceType.Fortress);

        GroupPower group = Assert.Single(PowerCalculator.Compute(board).Of(TestMaps.P0).Groups);

        Assert.Equal(3, group.Stones.Length);
        Assert.Equal(6, group.BaseTotal);
        Assert.Equal(6L, group.Power);
    }

    [Fact]
    public void 匠人落子后无持续效果()
    {
        // 规格 piece-effects「匠人落子后无持续效果」：留在盘面上的匠人只贡献 1 点基础军势——
        // 没有位置加值、没有倍率、也没有额外的气或免死。逐项与同形状的普通子棋串对照。
        GameBoard artisans = TestMaps.Blank(size: 9)
            .Place("B2", TestMaps.P0, PieceType.Artisan).Place("C2", TestMaps.P0, PieceType.Artisan).Place("D2", TestMaps.P0, PieceType.Artisan);
        GameBoard basics = TestMaps.Blank(size: 9)
            .Place("B2", TestMaps.P0, PieceType.Basic).Place("C2", TestMaps.P0, PieceType.Basic).Place("D2", TestMaps.P0, PieceType.Basic);

        GroupPower a = Assert.Single(PowerCalculator.Compute(artisans).Of(TestMaps.P0).Groups);
        GroupPower b = Assert.Single(PowerCalculator.Compute(basics).Of(TestMaps.P0).Groups);
        Assert.Equal((b.BaseTotal, b.LineBonus, b.SynergyBonus, b.MultiplierCount, b.HighGroundBonus, b.Power), (a.BaseTotal, a.LineBonus, a.SynergyBonus, a.MultiplierCount, a.HighGroundBonus, a.Power));
        Assert.Equal(3, a.BaseTotal);
        Assert.Equal(basics.LibertiesOf(basics.GroupAt(Coord.Parse("C2"))!).Length, artisans.LibertiesOf(artisans.GroupAt(Coord.Parse("C2"))!).Length);
    }

    [Fact]
    public void 匠人不免死()
    {
        // 规格 piece-effects：匠人与其他类型遵守完全相同的有气 / 无气 / 围杀规则，类型效果 MUST NOT 提供免死。
        // 与「堡垒子不免死」同一局面，只把被围的两枚换成匠人。
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("D4", TestMaps.P1, PieceType.Artisan).Place("D5", TestMaps.P1, PieceType.Artisan)
            .Place("C4", TestMaps.P0).Place("E4", TestMaps.P0).Place("C5", TestMaps.P0).Place("E5", TestMaps.P0).Place("D3", TestMaps.P0);
        Assert.Equal(2, PowerCalculator.Compute(board).Of(TestMaps.P1).GroupPowerSum());
        SettlementDriver driver = BatchFixtures.Driver(board);

        SettlementOutcome outcome = driver.Confirm(BatchFixtures.Context(board, TestMaps.P0), [BatchFixtures.P("D6")]);

        Assert.True(outcome.Confirmed);
        Assert.Equal(2, outcome.CaptureRecord!.Captured.Length);
        Assert.Empty(board.GroupsOf(TestMaps.P1));
    }

    [Fact]
    public void 堡垒子不免死()
    {
        // 纯堡垒子棋串 D4-D5 失去全部气 → 与普通子一样被整体移除；类型效果不提供额外气、免死或复活。
        // 走正式结算驱动器：棋盘层与批次层对类型一视同仁，计分层随后看到的就是空格。
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("D4", TestMaps.P1, PieceType.Fortress).Place("D5", TestMaps.P1, PieceType.Fortress)
            .Place("C4", TestMaps.P0).Place("E4", TestMaps.P0).Place("C5", TestMaps.P0).Place("E5", TestMaps.P0).Place("D3", TestMaps.P0);
        Assert.Equal(8, PowerCalculator.Compute(board).Of(TestMaps.P1).GroupPowerSum());
        SettlementDriver driver = BatchFixtures.Driver(board);

        SettlementOutcome outcome = driver.Confirm(BatchFixtures.Context(board, TestMaps.P0), [BatchFixtures.P("D6")]);

        Assert.True(outcome.Confirmed);
        Assert.Equal(2, outcome.CaptureRecord!.Captured.Length);
        Assert.Empty(board.GroupsOf(TestMaps.P1));
        PowerSnapshot after = PowerCalculator.Compute(board, ScoringFixtures.Roster((TestMaps.P0, PlayerStatus.Active), (TestMaps.P1, PlayerStatus.Active)), SiteValues.Standard);
        Assert.Equal(0, after.Of(TestMaps.P1).Total);
        Assert.Empty(after.Of(TestMaps.P1).Groups);
    }

    [Fact]
    public void 基础军势求和()
    {
        // 设计文档 §10.1：普通子×3、堡垒子×1、倍增子×2 → 基础军势 3×1 + 4 + 2×1 = 9
        // 变异验证 M23：BasePower 的 Fortress 改为 1 → 红 17，含本测试（基础 6）与「各类型基础军势」。
        GameBoard board = TestMaps.Blank(size: 9).PlaceStandardGroup(TestMaps.P0, row: 2);

        GroupPower group = Assert.Single(PowerCalculator.Compute(board).Of(TestMaps.P0).Groups);

        Assert.Equal(9, group.BaseTotal);
        Assert.Equal(6, group.Stones.Length);
    }
}
