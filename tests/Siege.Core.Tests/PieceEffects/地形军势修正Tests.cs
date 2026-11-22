using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.PieceEffects;

/// <summary>规格：piece-effects —— Requirement: 地形军势修正（terrain-bonus 裁决 3–7；数值为未校准初值，取自规格表）</summary>
public class 地形军势修正Tests
{
    private static GroupPower GroupAt(GameBoard board, PlayerId owner, string cell) =>
        PowerCalculator.Compute(board).GroupContaining(owner, cell);

    [Theory]
    [InlineData(Surface.Grass, 1)]
    [InlineData(Surface.Road, 1)]
    [InlineData(Surface.Crag, 1)]
    [InlineData(Surface.Forest, 2)]
    [InlineData(Surface.Desert, 2)]
    [InlineData(Surface.Shallows, 2)]
    [InlineData(Surface.Marsh, 3)]
    [InlineData(Surface.Rock, 3)]
    [InlineData(Surface.DeepWater, 0)]
    public void 地表修正逐枚计入基础军势(Surface surface, int expected)
    {
        // 规格表：普通子基础 1 + 地表修正（草地 / 土路 / 岩台 0，林地 / 荒漠 / 浅滩 +1，沼泽 / 岩石 +2，未架桥深水 −1）
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("F6", surface)])).Place("F6", TestMaps.P0);

        Assert.Equal(expected, Siege.Core.Scoring.PieceEffects.BaseTotal(board, board.GroupAt(TestMaps.At("F6"))!));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    public void 高度修正逐枚计入基础军势(int height, int expected)
    {
        GameBoard board = TestMaps.Blank(height == 0 ? TerrainData.Flat : TestMaps.Terrain(heights: [("F6", height)])).Place("F6", TestMaps.P0);

        Assert.Equal(expected, Siege.Core.Scoring.PieceEffects.BaseTotal(board, board.GroupAt(TestMaps.At("F6"))!));
    }

    [Fact]
    public void 桥格不吃深水修正()
    {
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("F6", Surface.DeepWater)], bridges: ["F6"])).Place("F6", TestMaps.P0);

        Assert.Equal(1, Siege.Core.Scoring.PieceEffects.BaseTotal(board, board.GroupAt(TestMaps.At("F6"))!));
    }

    [Fact]
    public void 地表与高度修正叠加()
    {
        // 堡垒子基础 4 + 沼泽 +2 + 高台 +2 = 8
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(heights: [("F6", 2)], surfaces: [("F6", Surface.Marsh)]))
            .Place("F6", TestMaps.P0, PieceType.Fortress);

        Assert.Equal(8, Siege.Core.Scoring.PieceEffects.BaseTotal(board, board.GroupAt(TestMaps.At("F6"))!));
    }

    [Fact]
    public void 地形修正被倍率放大()
    {
        // 倍增子在沼泽 F6（1 + 2 = 3）、普通子在草地 G6（1）：⌊(3 + 1) × 1.5⌋ = 6
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("F6", Surface.Marsh)]))
            .Place("F6", TestMaps.P0, PieceType.Multiplier).Place("G6", TestMaps.P0);

        Assert.Equal(6, GroupAt(board, TestMaps.P0, "F6").Power);
    }

    [Fact]
    public void 高度修正与高地压制加值并存()
    {
        // A 在 h=2 的 F7 压制 h=0 的 B（F6）：基础 1 + 高度 +2 = 3，高地压制加值另计 1 → 军势 4
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(heights: [("F7", 2)])).Place("F7", TestMaps.P0).Place("F6", TestMaps.P1);

        GroupPower a = GroupAt(board, TestMaps.P0, "F7");
        Assert.Equal(1, a.HighGroundBonus);
        Assert.Equal(4, a.Power);
    }
}
