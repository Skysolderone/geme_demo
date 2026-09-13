using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.CoverageTerritory;

/// <summary>规格：coverage-territory —— Requirement: 棋子向四邻接相邻格提供覆盖</summary>
public class 棋子向四邻接相邻格提供覆盖Tests
{
    [Fact]
    public void 覆盖范围()
    {
        // 设计文档 §7.1：中央棋子 F6 → 覆盖 E6/G6/F5/F7，斜向 E5 等不覆盖。
        // 变异验证 M21：CoverageMap.Compute 在 Neighbors 之外再加 (x±1, y±1) 四格 → 红 19，含本测试与「棋盘外沿不传播覆盖」。
        GameBoard board = TestMaps.Blank().Place("F6", TestMaps.P0);

        CoverageMap coverage = CoverageMap.Compute(board);

        foreach (string covered in new[] { "E6", "G6", "F5", "F7" })
        {
            Assert.Equal(new CellCoverage(1, TestMaps.P0), coverage.CoverageOf(TestMaps.At(covered)));
        }

        foreach (string diagonal in new[] { "E5", "E7", "G5", "G7" })
        {
            Assert.Equal(CellCoverage.None, coverage.CoverageOf(TestMaps.At(diagonal)));
        }

        // 棋子不覆盖自己所在格；两格以外不受影响
        Assert.Equal(CellCoverage.None, coverage.CoverageOf(TestMaps.At("F6")));
        Assert.Equal(CellCoverage.None, coverage.CoverageOf(TestMaps.At("F8")));
        Assert.Equal(CellCoverage.None, coverage.CoverageOf(TestMaps.At("D6")));
    }

    [Fact]
    public void 障碍不传播覆盖()
    {
        // 障碍格不记录任何覆盖，且覆盖不越过障碍到达更远的格。
        // 变异验证 M15：Compute 去掉 Terrain.Obstacle 的 continue → 红 2（本测试、「唯一覆盖查询与空格归属交叉一致」）。
        GameBoard board = TestMaps.Blank(size: 11, "G6").Place("F6", TestMaps.P0);

        CoverageMap coverage = CoverageMap.Compute(board);

        Assert.Equal(CellCoverage.None, coverage.CoverageOf(TestMaps.At("G6")));
        Assert.Equal(new CellOwnership(OwnershipKind.Obstacle, null), coverage.OwnershipOf(TestMaps.At("G6")));
        Assert.Equal(CellCoverage.None, coverage.CoverageOf(TestMaps.At("H6")));
        Assert.Equal(new CellCoverage(1, TestMaps.P0), coverage.CoverageOf(TestMaps.At("E6")));
    }

    [Fact]
    public void 棋盘外沿不传播覆盖()
    {
        // 角格 A1 的棋子只覆盖 B1 与 A2；越界方向与障碍语义相同。
        GameBoard board = TestMaps.Blank(size: 5).Place("A1", TestMaps.P0);

        CoverageMap coverage = CoverageMap.Compute(board);

        Assert.Equal(new CellCoverage(1, TestMaps.P0), coverage.CoverageOf(TestMaps.At("B1")));
        Assert.Equal(new CellCoverage(1, TestMaps.P0), coverage.CoverageOf(TestMaps.At("A2")));
        Assert.Equal(CellCoverage.None, coverage.CoverageOf(TestMaps.At("B2")));
    }

    [Fact]
    public void 覆盖随盘面实时重算()
    {
        // 覆盖关系在每次盘面变化后重算，不保留历史覆盖状态：棋子移除后其覆盖立即消失。
        GameBoard board = TestMaps.Blank().Place("F6", TestMaps.P0);
        Assert.Equal(TestMaps.P0, CoverageMap.Compute(board).UniqueCoverer(TestMaps.At("E6")));

        board.Clear(TestMaps.At("F6"));

        Assert.Equal(CellCoverage.None, CoverageMap.Compute(board).CoverageOf(TestMaps.At("E6")));
    }
}
