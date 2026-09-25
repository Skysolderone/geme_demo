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
    [InlineData(PieceType.Bannerman, 1)]
    [InlineData(PieceType.Chain, 1)]
    [InlineData(PieceType.Sentry, 1)]
    [InlineData(PieceType.Boundary, 1)]
    public void 各类型基础军势(PieceType type, int expected)
    {
        // 设计文档 §9.2 表 + artisan-terrain-edit 裁决 T-1：普通子 1、堡垒子 4、连珠子 1、倍增子 1、协同子 1、匠人 1
        Assert.Equal(expected, Siege.Core.Scoring.PieceEffects.BasePower(type));
    }

    [Fact]
    public void 军势表穷举六种类型()
    {
        // 守门（tasks 1.1）：军势表必须对每一种 PieceType 都有条目——漏掉任何一种，BasePower 会抛 ArgumentOutOfRangeException。
        // 断言不照抄实现表，而是用"结构性事实"表达：恰有十种类型；只有堡垒子是 4，其余九种都是 1。
        // 变异验证见测试报告 M-A1（BasePower 去掉 Artisan 分支 → 本测试抛异常红）、M-A2（Artisan => 4 → "只有堡垒子是 4"红）。
        // more-pieces-relics 段 A 改写：枚举末尾追加旗手子 / 铁链子 / 哨兵子 / 界碑子（D9），规格「基础军势」表扩为十种、新四种各 1（裁决 ②）→ 6 / 5 / 9 改为 10 / 9 / 13。
        PieceType[] all = Enum.GetValues<PieceType>();
        Assert.Equal(10, all.Length);
        Assert.Contains(PieceType.Artisan, all);
        Assert.Equal([PieceType.Bannerman, PieceType.Chain, PieceType.Sentry, PieceType.Boundary], all[^4..]);   // D9：只在末尾追加
        int[] powers = [.. all.Select(Siege.Core.Scoring.PieceEffects.BasePower)];
        Assert.Equal([PieceType.Fortress], all.Where(t => Siege.Core.Scoring.PieceEffects.BasePower(t) == 4));
        Assert.Equal(9, powers.Count(p => p == 1));
        Assert.Equal(13, powers.Sum());
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
        PowerSnapshot after = PowerCalculator.Compute(board, ScoringFixtures.Roster((TestMaps.P0, PlayerStatus.Active), (TestMaps.P1, PlayerStatus.Active)));
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

    [Fact]
    public void 四种新棋子按1计()
    {
        // more-pieces-relics 规格 piece-effects「四种新棋子按 1 计」：旗手子×1、铁链子×1、哨兵子×1、界碑子×1、普通子×1 → 基础军势 5 × 1 = 5。
        GameBoard board = TestMaps.Blank()
            .Place("B2", TestMaps.P0, PieceType.Bannerman).Place("C2", TestMaps.P0, PieceType.Chain).Place("D2", TestMaps.P0, PieceType.Sentry)
            .Place("E2", TestMaps.P0, PieceType.Boundary).Place("F2", TestMaps.P0, PieceType.Basic);

        GroupPower group = Assert.Single(PowerCalculator.Compute(board).Of(TestMaps.P0).Groups);

        Assert.Equal(5, group.Stones.Length);
        Assert.Equal(5, group.BaseTotal);
    }

    [Fact]
    public void 新棋子不免死()
    {
        // more-pieces-relics 规格 piece-effects「新棋子不免死」：铁链子×3 与哨兵子×1 组成的棋串失去全部气 → 与普通子棋串一样整体移除，位置加值随之消失。
        // P1 的 D4-D5-D6（铁链）+ D7（哨兵）竖成一串，P0 围住除 D8 外的全部气，本批落 D8 提子。
        GameBoard board = TestMaps.Blank(size: 9)
            .Place("D4", TestMaps.P1, PieceType.Chain).Place("D5", TestMaps.P1, PieceType.Chain).Place("D6", TestMaps.P1, PieceType.Chain)
            .Place("D7", TestMaps.P1, PieceType.Sentry)
            .Place("D3", TestMaps.P0)
            .Place("C4", TestMaps.P0).Place("C5", TestMaps.P0).Place("C6", TestMaps.P0).Place("C7", TestMaps.P0)
            .Place("E4", TestMaps.P0).Place("E5", TestMaps.P0).Place("E6", TestMaps.P0).Place("E7", TestMaps.P0);
        GroupPower before = Assert.Single(PowerCalculator.Compute(board).Of(TestMaps.P1).Groups);
        Assert.Equal(3 * (4 - 1), before.ChainBonus);   // 前提：提子前铁链加值在
        SettlementDriver driver = BatchFixtures.Driver(board);

        SettlementOutcome outcome = driver.Confirm(BatchFixtures.Context(board, TestMaps.P0), [BatchFixtures.P("D8")]);

        Assert.True(outcome.Confirmed);
        Assert.Equal(4, outcome.CaptureRecord!.Captured.Length);
        Assert.Empty(board.GroupsOf(TestMaps.P1));
        PowerSnapshot after = PowerCalculator.Compute(board, ScoringFixtures.Roster((TestMaps.P0, PlayerStatus.Active), (TestMaps.P1, PlayerStatus.Active)));
        Assert.Equal(0, after.Of(TestMaps.P1).Total);
        Assert.Empty(after.Of(TestMaps.P1).Groups);
    }
}
