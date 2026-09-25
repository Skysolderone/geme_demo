using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.PieceEffects;

/// <summary>规格：more-pieces-relics piece-effects —— Requirement: 铁链子的位置加值</summary>
/// <remarks>每枚铁链子为所在棋串提供"棋串棋子数 − 1"，棋子数不论类型；棋串取自唯一的棋串判定（沿气边）。</remarks>
public class 铁链子的位置加值Tests
{
    [Fact]
    public void 单子铁链不加分()
    {
        // 规格 Scenario：一枚铁链子独自构成一条棋串 → 1 − 1 = 0。
        GameBoard board = TestMaps.Blank().Place("F6", TestMaps.P0, PieceType.Chain);

        GroupPower group = PowerCalculator.Compute(board).GroupContaining(TestMaps.P0, "F6");

        Assert.Equal(0, group.ChainBonus);
        Assert.Equal(1, group.Power);
    }

    [Fact]
    public void 五子棋串含一枚铁链子()
    {
        // 规格 Scenario：普通子×3、堡垒子×1、铁链子×1 → 铁链加值 5 − 1 = 4，棋串军势 1×3 + 4 + 1 + 4 = 12。
        GameBoard board = TestMaps.Blank()
            .Place("C5", TestMaps.P0).Place("D5", TestMaps.P0).Place("E5", TestMaps.P0)
            .Place("F5", TestMaps.P0, PieceType.Fortress).Place("G5", TestMaps.P0, PieceType.Chain);

        GroupPower group = Assert.Single(PowerCalculator.Compute(board).Of(TestMaps.P0).Groups);

        Assert.Equal(5, group.Stones.Length);
        Assert.Equal(4, group.ChainBonus);
        Assert.Equal(12, group.Power);
    }

    [Fact]
    public void 多枚铁链子各自计算()
    {
        // 规格 Scenario：一条 5 子棋串含铁链子×2 → 2 × (5 − 1) = 8。
        GameBoard board = TestMaps.Blank()
            .Place("C5", TestMaps.P0, PieceType.Chain).Place("D5", TestMaps.P0).Place("E5", TestMaps.P0)
            .Place("F5", TestMaps.P0).Place("G5", TestMaps.P0, PieceType.Chain);

        GroupPower group = Assert.Single(PowerCalculator.Compute(board).Of(TestMaps.P0).Groups);

        Assert.Equal(8, group.ChainBonus);
        Assert.Equal(5 + 8, group.Power);
    }

    [Fact]
    public void 棋串分裂后重算()
    {
        // 规格 Scenario：一条 6 子棋串含 1 枚铁链子，分裂为两条各 3 子的棋串，铁链子落在其中一条 → 该铁链子提供 3 − 1 = 2，另一条不含铁链加值。
        // 提子只会整串移除（无气即整串提走），一条己方棋串被一分为二只能来自地形改造切断气边；这里用立栅 E5–F5 切开，
        // 盘面变化后势力全量重算（boundaries.md「全量重算，不做增量」），与分裂的成因无关。
        GameBoard board = TestMaps.Blank()
            .Place("C5", TestMaps.P0, PieceType.Chain).Place("D5", TestMaps.P0).Place("E5", TestMaps.P0)
            .Place("F5", TestMaps.P0).Place("G5", TestMaps.P0).Place("H5", TestMaps.P0);
        Assert.Equal(5, Assert.Single(PowerCalculator.Compute(board).Of(TestMaps.P0).Groups).ChainBonus);   // 分裂前 6 − 1

        board.ApplyTerrainEdits([TerrainEdit.Fence(TestMaps.At("E5"), TestMaps.At("F5"))]);
        PlayerPower after = PowerCalculator.Compute(board).Of(TestMaps.P0);

        Assert.Equal(2, after.Groups.Length);
        Assert.Equal(2, after.GroupContaining("C5").ChainBonus);
        Assert.Equal(0, after.GroupContaining("H5").ChainBonus);
    }
}

file static class 铁链子测试扩展
{
    internal static GroupPower GroupContaining(this PlayerPower player, string notation) =>
        player.Groups.Single(g => g.Stones.Contains(TestMaps.At(notation)));
}
