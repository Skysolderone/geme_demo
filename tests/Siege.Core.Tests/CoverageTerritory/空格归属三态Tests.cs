using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.CoverageTerritory;

/// <summary>规格：coverage-territory —— Requirement: 空格归属三态</summary>
public class 空格归属三态Tests
{
    [Fact]
    public void 独占()
    {
        // 规格 Scenario（scoring-sites 改写）：某空格只被 A 覆盖 → A 独占，但不向 A 计入任何分数。
        // 「不计分」一句由原 领地分Tests（REMOVED Requirement，整文件删除）的「孤立棋子的势力上限」改写并入：旧期望总势力 5（军势 1 + 领地 4）→ 新期望 1。
        // 变异验证 M-S5（段 A2）：PowerCalculator 的 Total 加回 exclusive.Length → 红，含本测试。
        GameBoard board = TestMaps.Blank(size: 7).Place("D4", TestMaps.P0);

        CoverageMap coverage = CoverageMap.Compute(board);

        Assert.Equal(new CellOwnership(OwnershipKind.Exclusive, TestMaps.P0), coverage.OwnershipOf(TestMaps.At("C4")));
        Assert.Equal(["D3", "C4", "E4", "D5"], coverage.ExclusiveCellsOf(TestMaps.P0).Notations());
        PlayerPower p0 = PowerCalculator.Compute(board).Of(TestMaps.P0);
        Assert.Equal(["D3", "C4", "E4", "D5"], p0.ExclusiveCells.Notations());
        Assert.Equal(1, p0.Total);
    }

    [Fact]
    public void 棋子格不在独占集合中()
    {
        // 由原 领地分Tests「棋子格不重复计分」迁入：棋子所在格 MUST NOT 出现在独占空格集合中（盘面层归属读法仍用它），多子棋串同样如此。
        // scoring-sites 2.7 改写：旧期望总势力 7 + 6（领地 7 + 军势 6）→ 新期望 6（独占空格不计分）。
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("C4", TestMaps.P0).Place("D4", TestMaps.P0, PieceType.Fortress).Place("D5", TestMaps.P0);

        PlayerPower p0 = PowerCalculator.Compute(board).Of(TestMaps.P0);

        Coord[] stones = [TestMaps.At("C4"), TestMaps.At("D4"), TestMaps.At("D5")];
        Assert.Empty(p0.ExclusiveCells.Intersect(stones));
        Assert.Equal(["C3", "D3", "B4", "E4", "C5", "E5", "D6"], p0.ExclusiveCells.Notations());
        Assert.Equal(6, p0.Total);
    }

    [Fact]
    public void 争议()
    {
        // D5 同时被 P0（D4）与 P1（D6）覆盖 → 争议，双方都不得分。
        // 变异验证 M18：Compute 的 SoleCoverer 改为 set.First()（不判 Count == 1）→ 红 3（本测试、「多人覆盖」、交叉一致）。
        GameBoard board = TestMaps.Blank(size: 7).Place("D4", TestMaps.P0).Place("D6", TestMaps.P1);

        PowerSnapshot snapshot = PowerCalculator.Compute(board);
        CoverageMap coverage = snapshot.Coverage;

        Assert.Equal(new CellOwnership(OwnershipKind.Contested, null), coverage.OwnershipOf(TestMaps.At("D5")));
        Assert.Equal(new CellCoverage(2, null), coverage.CoverageOf(TestMaps.At("D5")));
        Assert.DoesNotContain(TestMaps.At("D5"), snapshot.Of(TestMaps.P0).ExclusiveCells);
        Assert.DoesNotContain(TestMaps.At("D5"), snapshot.Of(TestMaps.P1).ExclusiveCells);
        Assert.Equal(3, snapshot.Of(TestMaps.P0).ExclusiveCells.Length);
        Assert.Equal(3, snapshot.Of(TestMaps.P1).ExclusiveCells.Length);
    }

    [Fact]
    public void 覆盖数量不影响独占()
    {
        // D4 被 P0 的 4 枚棋子（C4/E4/D3/D5）同时覆盖，且无其他玩家 → 仍只是一个独占格。
        // 4 枚棋子的独占格：D4 + 外圈 B4/C3/C5/F4/E3/E5/D2/D6 = 9（外圈中 C3/E3/C5/E5 各被两枚棋子覆盖，同样只计一次）。
        // 变异验证 M5：Compute 的 HashSet<PlayerId> 改为 List<PlayerId>（CovererCount = 覆盖棋子数）→ 红 6，含本测试（D4 判为争议）。
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("C4", TestMaps.P0).Place("E4", TestMaps.P0).Place("D3", TestMaps.P0).Place("D5", TestMaps.P0);

        PowerSnapshot snapshot = PowerCalculator.Compute(board);

        Assert.Equal(new CellCoverage(1, TestMaps.P0), snapshot.Coverage.CoverageOf(TestMaps.At("D4")));
        Assert.Equal(new CellOwnership(OwnershipKind.Exclusive, TestMaps.P0), snapshot.Coverage.OwnershipOf(TestMaps.At("D4")));
        PlayerPower p0 = snapshot.Of(TestMaps.P0);
        Assert.Single(p0.ExclusiveCells, TestMaps.At("D4"));
        Assert.Equal(9, p0.ExclusiveCells.Length);
        Assert.Equal(p0.ExclusiveCells.Length, p0.ExclusiveCells.Distinct().Count());
    }

    [Fact]
    public void 中立()
    {
        GameBoard board = TestMaps.Blank(size: 7).Place("D4", TestMaps.P0);

        CoverageMap coverage = CoverageMap.Compute(board);

        Assert.Equal(new CellOwnership(OwnershipKind.Neutral, null), coverage.OwnershipOf(TestMaps.At("A1")));
        Assert.Equal(new CellOwnership(OwnershipKind.Neutral, null), coverage.OwnershipOf(TestMaps.At("D6")));
    }

    [Fact]
    public void 障碍不属于任何玩家()
    {
        // 障碍格 C4 紧贴 P0 的 D4：不出现在任何玩家的归属集合中，也不计分。
        // 变异验证 M22：Resolve 去掉 Obstacle 分支 → 红 3（本测试、「障碍不传播覆盖」、交叉一致）。
        GameBoard board = TestMaps.Blank(size: 7, "C4").Place("D4", TestMaps.P0);

        PowerSnapshot snapshot = PowerCalculator.Compute(board);

        Assert.Equal(OwnershipKind.Obstacle, snapshot.Coverage.OwnershipOf(TestMaps.At("C4")).Kind);
        Assert.Equal(["D3", "E4", "D5"], snapshot.Of(TestMaps.P0).ExclusiveCells.Notations());
        Assert.Equal(3, snapshot.Of(TestMaps.P0).ExclusiveCells.Length);
    }
}
