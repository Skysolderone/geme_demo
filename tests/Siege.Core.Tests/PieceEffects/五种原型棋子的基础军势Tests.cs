using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.PieceEffects;

/// <summary>规格：piece-effects —— Requirement: 五种原型棋子的基础军势</summary>
public class 五种原型棋子的基础军势Tests
{
    [Theory]
    [InlineData(PieceType.Basic, 1)]
    [InlineData(PieceType.Fortress, 4)]
    [InlineData(PieceType.Line, 1)]
    [InlineData(PieceType.Multiplier, 1)]
    [InlineData(PieceType.Synergy, 1)]
    public void 各类型基础军势(PieceType type, int expected)
    {
        // 设计文档 §9.2 表：普通子 1、堡垒子 4、连珠子 1、倍增子 1、协同子 1
        Assert.Equal(expected, Siege.Core.Scoring.PieceEffects.BasePower(type));
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
