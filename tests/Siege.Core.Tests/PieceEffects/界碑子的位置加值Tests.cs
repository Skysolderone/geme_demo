using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.PieceEffects;

/// <summary>规格：more-pieces-relics piece-effects —— Requirement: 界碑子的位置加值</summary>
/// <remarks>
/// 气边邻格中每个按空格归属三态为所有者<b>独占</b>的空格 +1。独占判定复用 <see cref="CoverageMap"/>（不另算覆盖、不看地表），
/// 荒漠独占格计入（design.md D2 / 裁决 ⑦），争议格与中立格（含空林地）不计。
/// </remarks>
public class 界碑子的位置加值Tests
{
    [Fact]
    public void 平地孤立界碑()
    {
        // 规格 Scenario：孤立界碑子位于平地中央，四周为空草地、附近无敌子 → 4 点，该玩家从此局部获得 1 + 4 + 4 = 9 点势力。
        GameBoard board = TestMaps.Blank().Place("F6", TestMaps.P0, PieceType.Boundary);

        PlayerPower player = PowerCalculator.Compute(board).Of(TestMaps.P0);

        GroupPower group = Assert.Single(player.Groups);
        Assert.Equal(4, group.BoundaryBonus);
        Assert.Equal(4, player.TerritoryScore);
        Assert.Equal(9, player.Total);
    }

    [Fact]
    public void 争议格不计()
    {
        // 规格 Scenario：A 的界碑子四个气边邻格都是空格，其中一格同时被玩家 B 覆盖 → 3。
        // B 的棋子放在 H6：覆盖 G6（F6 的气边邻格），与 F6 本身不相邻。
        GameBoard board = TestMaps.Blank().Place("F6", TestMaps.P0, PieceType.Boundary).Place("H6", TestMaps.P1);

        PowerSnapshot snapshot = PowerCalculator.Compute(board);

        Assert.Equal(OwnershipKind.Contested, snapshot.Coverage.OwnershipOf(TestMaps.At("G6")).Kind);   // 前提：G6 为争议格
        Assert.Equal(3, snapshot.GroupContaining(TestMaps.P0, "F6").BoundaryBonus);
    }

    [Fact]
    public void 独占荒漠格计入()
    {
        // 规格 Scenario：A 的界碑子位于草地，四个气边邻格都是 A 独占的空格，其中两格是荒漠 → 4 点，而 A 从这四格只获得 2 点领地分。
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("E6", Surface.Desert), ("G6", Surface.Desert)]))
            .Place("F6", TestMaps.P0, PieceType.Boundary);

        PlayerPower player = PowerCalculator.Compute(board).Of(TestMaps.P0);

        Assert.Equal(4, player.ExclusiveCells.Length);   // 前提：四格都被 A 独占（荒漠照常可独占）
        Assert.Equal(4, Assert.Single(player.Groups).BoundaryBonus);
        Assert.Equal(2, player.TerritoryScore);
    }

    [Fact]
    public void 空林地不计()
    {
        // 规格 Scenario：A 的界碑子的一个气边邻格是空林地格 → 该格为中立，不计入界碑加值。
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("G6", Surface.Forest)])).Place("F6", TestMaps.P0, PieceType.Boundary);

        PowerSnapshot snapshot = PowerCalculator.Compute(board);

        Assert.Contains(TestMaps.At("G6"), board.LibertyNeighbors(TestMaps.At("F6")));   // 前提：林地是气边邻格
        Assert.Equal(OwnershipKind.Neutral, snapshot.Coverage.OwnershipOf(TestMaps.At("G6")).Kind);   // 前提：空林地恒为中立
        Assert.Equal(3, snapshot.GroupContaining(TestMaps.P0, "F6").BoundaryBonus);
    }
}
