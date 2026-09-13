using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.CoverageTerritory;

/// <summary>规格：coverage-territory —— Requirement: 占据优先于覆盖</summary>
public class 占据优先于覆盖Tests
{
    [Fact]
    public void 敌方覆盖不夺格()
    {
        // 设计文档 §7.1：A 的棋子在 D4，B 的三枚棋子覆盖该格 → 仍由 A 直接控制，不是争议格。
        // 变异验证 M7：CoverageMap.Resolve 在 Occupant 分支前插入"唯一敌方覆盖 → Exclusive" → 红 3（本测试、交叉一致、弃赛者遗留棋子可被围杀）。
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("D4", TestMaps.P0)
            .Place("C4", TestMaps.P1).Place("E4", TestMaps.P1).Place("D3", TestMaps.P1);

        CoverageMap coverage = CoverageMap.Compute(board);
        CellOwnership d4 = coverage.OwnershipOf(TestMaps.At("D4"));

        Assert.Equal(new CellOwnership(OwnershipKind.Occupied, TestMaps.P0), d4);
        Assert.True(d4.IsControlledBy(TestMaps.P0));
        Assert.False(d4.IsControlledBy(TestMaps.P1));
        // 覆盖关系本身照常记录（P1 是 D4 的唯一覆盖者），只是不改变归属
        Assert.Equal(TestMaps.P1, coverage.UniqueCoverer(TestMaps.At("D4")));
        Assert.DoesNotContain(TestMaps.At("D4"), coverage.ExclusiveCellsOf(TestMaps.P1));
    }

    [Fact]
    public void 围杀后归属重算()
    {
        // A 位于 D4 的棋串被 B 围杀移除 → D4 变为空格，按当前覆盖关系判为 B 的独占。
        // 围杀走正式结算驱动器，重算读的是提子后的盘面。
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("D4", TestMaps.P0)
            .Place("C4", TestMaps.P1).Place("E4", TestMaps.P1).Place("D3", TestMaps.P1);
        SettlementDriver driver = BatchFixtures.Driver(board);

        Assert.True(driver.Confirm(BatchFixtures.Context(board, TestMaps.P1), [BatchFixtures.P("D5")]).Confirmed);

        Assert.Null(board[TestMaps.At("D4")].Occupant);
        PowerSnapshot snapshot = PowerCalculator.Compute(board);
        Assert.Equal(new CellOwnership(OwnershipKind.Exclusive, TestMaps.P1), snapshot.Coverage.OwnershipOf(TestMaps.At("D4")));
        Assert.Contains(TestMaps.At("D4"), snapshot.Of(TestMaps.P1).ExclusiveCells);
        Assert.DoesNotContain(snapshot.Players, p => p.Player == TestMaps.P0);
    }
}
