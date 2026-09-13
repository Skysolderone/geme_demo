using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.PieceEffects;

/// <summary>规格：piece-effects —— Requirement: 协同子的位置加值</summary>
public class 协同子的位置加值Tests
{
    [Fact]
    public void 单枚协同子()
    {
        // 协同子×1、普通子×2、堡垒子×1 → 除协同子外类型数 2 → 2×2 = 4
        // 变异验证 M8：SynergyBonus 把协同子自身也计入 otherTypes → 红 7，含本测试（6）与「纯协同子棋串」。
        GameBoard board = TestMaps.Blank(size: 9)
            .Place("B2", TestMaps.P0, PieceType.Synergy)
            .Place("C2", TestMaps.P0, PieceType.Basic).Place("D2", TestMaps.P0, PieceType.Basic)
            .Place("E2", TestMaps.P0, PieceType.Fortress);

        GroupPower group = Assert.Single(PowerCalculator.Compute(board).Of(TestMaps.P0).Groups);

        Assert.Equal(4, group.SynergyBonus);
        Assert.Equal(0, group.LineBonus);
        Assert.Equal(1 + 2 + 4 + 4, group.Power);
    }

    [Fact]
    public void 多枚协同子()
    {
        // 协同子×3、普通子×1、堡垒子×1、连珠子×1 → 类型数 3 → 3×3×2 = 18
        // 变异验证 M26：SynergyBonus 去掉 `* synergyCount`（每串只计一次）→ 红 2（本测试 6、「位置加值可溯源」）。
        GameBoard board = TestMaps.Blank(size: 9)
            .Place("B2", TestMaps.P0, PieceType.Synergy).Place("C2", TestMaps.P0, PieceType.Synergy).Place("D2", TestMaps.P0, PieceType.Synergy)
            .Place("E2", TestMaps.P0, PieceType.Basic).Place("F2", TestMaps.P0, PieceType.Fortress).Place("G2", TestMaps.P0, PieceType.Line);

        GroupPower group = Assert.Single(PowerCalculator.Compute(board).Of(TestMaps.P0).Groups);

        Assert.Equal(18, group.SynergyBonus);
        Assert.Equal(0, group.LineBonus);
        Assert.Equal(3 + 1 + 4 + 1 + 18, group.Power);
    }

    [Fact]
    public void 纯协同子棋串()
    {
        // 裁决记录 2：只含协同子 → 其他类型数 0 → 0 点位置加值
        GameBoard board = TestMaps.Blank(size: 9)
            .Place("B2", TestMaps.P0, PieceType.Synergy).Place("C2", TestMaps.P0, PieceType.Synergy);

        GroupPower group = Assert.Single(PowerCalculator.Compute(board).Of(TestMaps.P0).Groups);

        Assert.Equal(0, group.SynergyBonus);
        Assert.Equal(2, group.Power);
    }

    [Fact]
    public void 只看同一棋串()
    {
        // 裁决记录 D5：其他类型数只看同一棋串。协同子 B2 与普通子 D2 不相连 → 0；B2 与 B3 堡垒相连 → 2。
        GameBoard board = TestMaps.Blank(size: 9)
            .Place("B2", TestMaps.P0, PieceType.Synergy).Place("D2", TestMaps.P0, PieceType.Basic)
            .Place("G7", TestMaps.P0, PieceType.Synergy).Place("G8", TestMaps.P0, PieceType.Fortress);

        PowerSnapshot snapshot = PowerCalculator.Compute(board);

        Assert.Equal(0, snapshot.GroupContaining(TestMaps.P0, "B2").SynergyBonus);
        Assert.Equal(2, snapshot.GroupContaining(TestMaps.P0, "G7").SynergyBonus);
    }
}
