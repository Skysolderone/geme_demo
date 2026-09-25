using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.PieceEffects;

/// <summary>规格：more-pieces-relics piece-effects —— Requirement: 哨兵子的位置加值</summary>
/// <remarks>
/// 气边邻格上每个非己方棋子（含弃赛者遗留）+2；气边只经 <see cref="GameBoard.LibertyNeighbors"/>，不用几何四邻或覆盖。
/// "不计弃赛者"的变异在计分层写不出来：<see cref="PieceEffects"/> 只拿盘面、没有名册（弃赛者的遗留棋子在盘面上与其他棋子无从区分），
/// 本类「弃赛者遗留棋子计入」以带 Resigned 名册的完整势力计算钉住行为。
/// </remarks>
public class 哨兵子的位置加值Tests
{
    [Fact]
    public void 两枚相邻敌子()
    {
        // 规格 Scenario：A 的哨兵子位于 F6，B 的棋子位于 G6，C 的棋子位于 F7，两格都与 F6 有气边 → 2 × 2 = 4。
        GameBoard board = TestMaps.Blank()
            .Place("F6", TestMaps.P0, PieceType.Sentry).Place("G6", TestMaps.P1).Place("F7", ScoringFixtures.P2);

        GroupPower group = PowerCalculator.Compute(board).GroupContaining(TestMaps.P0, "F6");

        Assert.Equal(4, group.SentryBonus);
        Assert.Equal(1 + 4, group.Power);
    }

    [Fact]
    public void 栅栏隔开的敌子不计()
    {
        // 规格 Scenario：A 的哨兵子位于 F6，B 的棋子位于 E6，E6–F6 之间有栅栏 → E6 不计入。
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(fences: [("E6", "F6")]))
            .Place("F6", TestMaps.P0, PieceType.Sentry).Place("E6", TestMaps.P1);

        Assert.DoesNotContain(TestMaps.At("E6"), board.LibertyNeighbors(TestMaps.At("F6")));   // 前提：几何相邻、无气边
        Assert.Equal(0, PowerCalculator.Compute(board).GroupContaining(TestMaps.P0, "F6").SentryBonus);
    }

    [Fact]
    public void 己方棋子不计()
    {
        // 规格 Scenario：A 的哨兵子四周经气边相邻的格上只有 A 自己的棋子 → 0。
        GameBoard board = TestMaps.Blank()
            .Place("F6", TestMaps.P0, PieceType.Sentry)
            .Place("E6", TestMaps.P0).Place("G6", TestMaps.P0).Place("F5", TestMaps.P0).Place("F7", TestMaps.P0);

        GroupPower group = Assert.Single(PowerCalculator.Compute(board).Of(TestMaps.P0).Groups);

        Assert.Equal(5, group.Stones.Length);
        Assert.Equal(0, group.SentryBonus);
        Assert.Equal(5, group.Power);
    }

    [Fact]
    public void 弃赛者遗留棋子计入()
    {
        // 规格 Scenario：A 的哨兵子经气边相邻一枚已弃赛玩家 D 遗留的棋子 → 因此提供 2 点。
        GameBoard board = TestMaps.Blank().Place("F6", TestMaps.P0, PieceType.Sentry).Place("G6", ScoringFixtures.P3);

        PowerSnapshot snapshot = PowerCalculator.Compute(board, ScoringFixtures.Roster(
            (TestMaps.P0, PlayerStatus.Active), (ScoringFixtures.P3, PlayerStatus.Resigned)));

        Assert.Equal(PlayerStatus.Resigned, snapshot.Of(ScoringFixtures.P3).Status);   // 前提：D 已弃赛
        Assert.Equal(2, snapshot.GroupContaining(TestMaps.P0, "F6").SentryBonus);
    }

    [Fact]
    public void 高差2的敌子不计()
    {
        // 规格 Scenario：A 的哨兵子位于 h=2 的 F7，B 的棋子位于几何相邻、h=0 的 F6，两格之间没有气边 → 不因 F6 获得哨兵加值（高地压制另行判定）。
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(heights: [("F7", 2)]))
            .Place("F7", TestMaps.P0, PieceType.Sentry).Place("F6", TestMaps.P1);

        Assert.DoesNotContain(TestMaps.At("F6"), board.LibertyNeighbors(TestMaps.At("F7")));   // 前提：崖壁切断气边
        GroupPower group = PowerCalculator.Compute(board).GroupContaining(TestMaps.P0, "F7");
        Assert.Equal(0, group.SentryBonus);
        Assert.Equal(1, group.HighGroundBonus);   // 高地压制照常：覆盖目标上有更低处的敌子
    }
}
