using System.Numerics;
using Siege.Core.Board;
using Siege.Core.Relics;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.CoverageTerritory;

/// <summary>规格：coverage-territory —— Requirement: 空格归属三态</summary>
public class 空格归属三态Tests
{
    [Fact]
    public void 独占()
    {
        // restore-go-core-rules coverage-territory 规格：某空格只被 A 覆盖 → A 独占，向 A 计 1 点领地分。
        // 段 A 重算：原期望总势力 1（scoring-sites：独占空格不计分）→ 5 = 军势 1 + 领地 4。
        // 变异验证 M-A4（段 A）：PowerCalculator 的 Total 漏掉领地分 → 红，含本测试（1）。
        GameBoard board = TestMaps.Blank(size: 7).Place("D4", TestMaps.P0);

        CoverageMap coverage = CoverageMap.Compute(board);

        Assert.Equal(new CellOwnership(OwnershipKind.Exclusive, TestMaps.P0), coverage.OwnershipOf(TestMaps.At("C4")));
        Assert.Equal(["D3", "C4", "E4", "D5"], coverage.ExclusiveCellsOf(TestMaps.P0).Notations());
        PlayerPower p0 = PowerCalculator.Compute(board).Of(TestMaps.P0);
        Assert.Equal(["D3", "C4", "E4", "D5"], p0.ExclusiveCells.Notations());
        Assert.Equal((4, (BigInteger)5), (p0.TerritoryScore, p0.Total));
    }

    [Fact]
    public void 棋子格不在独占集合中()
    {
        // 由原 领地分Tests「棋子格不重复计分」迁入：棋子所在格 MUST NOT 出现在独占空格集合中（盘面层归属读法仍用它），多子棋串同样如此。
        // 段 A 重算：原期望总势力 6（scoring-sites：独占空格不计分）→ 13 = 领地 7 + 军势 6（普通 1 + 堡垒 4 + 普通 1）；三枚棋子所在格不计领地。
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("C4", TestMaps.P0).Place("D4", TestMaps.P0, PieceType.Fortress).Place("D5", TestMaps.P0);

        PlayerPower p0 = PowerCalculator.Compute(board).Of(TestMaps.P0);

        Coord[] stones = [TestMaps.At("C4"), TestMaps.At("D4"), TestMaps.At("D5")];
        Assert.Empty(p0.ExclusiveCells.Intersect(stones));
        Assert.Equal(["C3", "D3", "B4", "E4", "C5", "E5", "D6"], p0.ExclusiveCells.Notations());
        Assert.Equal((7, (BigInteger)13), (p0.TerritoryScore, p0.Total));
    }

    [Fact]
    public void 争议()
    {
        // D5 同时被 P0（D4）与 P1（D6）覆盖 → 争议，不向任何玩家计分：每人总势力 = 军势 1 + 其余三个独占邻格 3 = 4（把 D5 计入会得 5）。
        // 变异验证 M-AC7（段 A check 实跑，= implement.md 变异表的 M-A3）：CoverageMap.ExclusiveCellsOf 把争议格也算作独占 → 红 18，含本测试。
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
        Assert.Equal((3, (BigInteger)4), (snapshot.Of(TestMaps.P0).TerritoryScore, snapshot.Of(TestMaps.P0).Total));
        Assert.Equal((3, (BigInteger)4), (snapshot.Of(TestMaps.P1).TerritoryScore, snapshot.Of(TestMaps.P1).Total));
    }

    [Fact]
    public void 覆盖数量不影响独占()
    {
        // D4 被 P0 的 4 枚棋子（C4/E4/D3/D5）同时覆盖，且无其他玩家 → 仍只是一个独占格。
        // 4 枚棋子的独占格：D4 + 外圈 B4/C3/C5/F4/E3/E5/D2/D6 = 9（外圈中 C3/E3/C5/E5 各被两枚棋子覆盖，同样只计一次）。
        // 变异验证 M5：Compute 的 HashSet<PlayerId> 改为 List<PlayerId>（CovererCount = 覆盖棋子数）→ 红 6，含本测试（D4 判为争议）。
        // 段 A 补：D4 只计 1 分 → 领地分 9、总势力 9 + 四枚互不相连的普通子 4 = 13（D4 被 4 枚棋子覆盖仍只计 1 分）。
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("C4", TestMaps.P0).Place("E4", TestMaps.P0).Place("D3", TestMaps.P0).Place("D5", TestMaps.P0);

        PowerSnapshot snapshot = PowerCalculator.Compute(board);

        Assert.Equal(new CellCoverage(1, TestMaps.P0), snapshot.Coverage.CoverageOf(TestMaps.At("D4")));
        Assert.Equal(new CellOwnership(OwnershipKind.Exclusive, TestMaps.P0), snapshot.Coverage.OwnershipOf(TestMaps.At("D4")));
        PlayerPower p0 = snapshot.Of(TestMaps.P0);
        Assert.Single(p0.ExclusiveCells, TestMaps.At("D4"));
        Assert.Equal(9, p0.ExclusiveCells.Length);
        Assert.Equal(p0.ExclusiveCells.Length, p0.ExclusiveCells.Distinct().Count());
        Assert.Equal((9, (BigInteger)13), (p0.TerritoryScore, p0.Total));
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
    public void 空林地格恒为中立()
    {
        // restore-go-core-rules coverage-territory 规格（裁决 A）：A 的棋子与一个空林地格几何相邻、无其他玩家 → 该林地格中立，不向 A 计分。
        // D4 的四邻中 E4 是林地：独占只剩 D3 / C4 / D5 → 领地 3、总势力 1 + 3 = 4（把林地计入会得 5）。
        // 变异验证 M-AC9（段 A check 实跑）：Adjacency.CoverageTargets 去掉"林地不是覆盖目标"的排除 → 红 13，含本测试（林地格成了 A 的独占格）。
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("E4", Surface.Forest)]), size: 7).Place("D4", TestMaps.P0);

        PowerSnapshot snapshot = PowerCalculator.Compute(board);

        Assert.Equal(new CellOwnership(OwnershipKind.Neutral, null), snapshot.Coverage.OwnershipOf(TestMaps.At("E4")));
        Assert.Equal(CellCoverage.None, snapshot.Coverage.CoverageOf(TestMaps.At("E4")));
        PlayerPower p0 = snapshot.Of(TestMaps.P0);
        Assert.Equal(["D3", "C4", "D5"], p0.ExclusiveCells.Notations());
        Assert.Equal((3, (BigInteger)4), (p0.TerritoryScore, p0.Total));
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
        Assert.Equal((3, (BigInteger)4), (snapshot.Of(TestMaps.P0).TerritoryScore, snapshot.Of(TestMaps.P0).Total));
    }

    // ---------- terrain-surfaces 段 1：荒漠 ----------

    [Fact]
    public void 荒漠独占不计分()
    {
        // 规格 terrain-surfaces · coverage-territory「荒漠独占不计分」：空荒漠 G6 只被 A 覆盖 → 判定为 A 独占（归属照常），但不计领地分。
        // 变异验证 M-S1a（实跑）：把荒漠直接从 ExclusiveCells 里剔掉（等于把它当成不独占）→ 本测试与另两条荒漠测试共红 3。
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("G6", Surface.Desert)])).Place("F6", TestMaps.P0);

        PowerSnapshot snapshot = PowerCalculator.Compute(board);

        Assert.Equal(new CellOwnership(OwnershipKind.Exclusive, TestMaps.P0), snapshot.Coverage.OwnershipOf(TestMaps.At("G6")));
        Assert.Contains(TestMaps.At("G6"), snapshot.Of(TestMaps.P0).ExclusiveCells);
        Assert.DoesNotContain(TestMaps.At("G6"), snapshot.Of(TestMaps.P0).ScoredCells);
        Assert.Equal(3, snapshot.Of(TestMaps.P0).TerritoryScore);
    }

    [Fact]
    public void 荒漠上的信物照常被控制()
    {
        // 规格 Scenario「荒漠上的信物照常被控制」：信物格 E7 是空荒漠、只被 P1（F7）覆盖 → P1 控制，与草地相同。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(TestMaps.Terrain(surfaces: [("E7", Surface.Desert)]), ("E7", RelicFixtures.Conscription()));
        board.Place("F7", TestMaps.P1);

        ledger.Settle(board, 1);

        Assert.Equal(new RelicControl(RelicControlKind.Controlled, TestMaps.P1), ledger.ControlOf(TestMaps.At("E7")));
    }
}
