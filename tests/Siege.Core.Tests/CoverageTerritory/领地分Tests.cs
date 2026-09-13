using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.CoverageTerritory;

/// <summary>规格：coverage-territory —— Requirement: 领地分</summary>
public class 领地分Tests
{
    [Fact]
    public void 孤立棋子的势力上限()
    {
        // 设计文档 §10.1：无竞争的孤立普通子 → 军势 1 + 4 个相邻独占空格各 1 = 恰好 5。
        // 变异验证 M14：ExclusiveCellsOf 把 Occupied 格也计入 → 红 17，含本测试（领地 5、总势力 6）；
        //           M20：Compute 的 Total 漏掉 exclusive.Length → 红 14，含本测试。
        GameBoard board = TestMaps.Blank().Place("F6", TestMaps.P0);

        PlayerPower p0 = PowerCalculator.Compute(board).Of(TestMaps.P0);

        Assert.Equal(5, p0.Total);
        Assert.Equal(4, p0.TerritoryScore);
        Assert.Equal(["F5", "E6", "G6", "F7"], p0.ExclusiveCells.Notations());
        GroupPower group = Assert.Single(p0.Groups);
        Assert.Equal(1, group.Power);
        Assert.Equal(1, group.BaseTotal);
    }

    [Fact]
    public void 棋子格不重复计分()
    {
        // 棋子所在格只通过基础军势计分，MUST NOT 出现在独占空格集合中。多子棋串同样如此。
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("C4", TestMaps.P0).Place("D4", TestMaps.P0, PieceType.Fortress).Place("D5", TestMaps.P0);

        PlayerPower p0 = PowerCalculator.Compute(board).Of(TestMaps.P0);

        Coord[] stones = [TestMaps.At("C4"), TestMaps.At("D4"), TestMaps.At("D5")];
        Assert.Empty(p0.ExclusiveCells.Intersect(stones));
        Assert.Equal(["C3", "D3", "B4", "E4", "C5", "E5", "D6"], p0.ExclusiveCells.Notations());
        Assert.Equal(7 + 6, p0.Total);
    }

    [Fact]
    public void 争议与中立不产生领地分()
    {
        // 争议格、中立格不给任何玩家领地分。P0 的 D4 与 P1 的 F4 共同覆盖 E4 → 双方领地各 3。
        GameBoard board = TestMaps.Blank(size: 7).Place("D4", TestMaps.P0).Place("F4", TestMaps.P1);

        PowerSnapshot snapshot = PowerCalculator.Compute(board);

        Assert.Equal(3, snapshot.Of(TestMaps.P0).TerritoryScore);
        Assert.Equal(3, snapshot.Of(TestMaps.P1).TerritoryScore);
        Assert.Equal(OwnershipKind.Contested, snapshot.Coverage.OwnershipOf(TestMaps.At("E4")).Kind);
    }
}
