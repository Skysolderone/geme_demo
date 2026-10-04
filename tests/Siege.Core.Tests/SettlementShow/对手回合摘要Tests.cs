using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Presentation.Show;
using static Siege.Core.Tests.SettlementShow.ShowFixtures;

namespace Siege.Core.Tests.SettlementShow;

/// <summary>
/// 规格：settlement-show —— Requirement: 对手回合摘要（follow-opponent）。算例即规格四个场景的文案；P1 是蓝方。
/// 每个用例的势力节拍里都另放一条 P2 的变化（数值不同）：摘要只取行动者那一条（M4：取第一条而不按行动者筛 → 红）。
/// </summary>
public class 对手回合摘要Tests
{
    private static PlacedPiece Placed(string cell, PieceType type) => new(Coord.Parse(cell), type, P1);

    private static CapturedPiece Captured(string cell) => new(Coord.Parse(cell), P2, PieceType.Basic);

    private static readonly PowerChange Other = new(P2, 9, 4, 1, 2, TerritoryDelta: -1, GroupDelta: -4);

    [Fact]
    public void 落子并提子的摘要()
    {
        ImmutableArray<SettlementBeat> beats =
        [
            new PlacementBeat([Placed("C3", PieceType.Fortress), Placed("C4", PieceType.Basic), Placed("C5", PieceType.Basic)]),
            new CaptureBeat([Captured("D3"), Captured("D4")]),
            new PowerBeat([Other, new PowerChange(P1, 10, 18, 2, 1, TerritoryDelta: 3, GroupDelta: 5)]),
        ];
        Assert.Equal("蓝方：落 3 子（堡垒子、普通子 ×2） · 提 2 子 · 势力 +8（领地 +3、军势 +5）", TurnSummary.Of(P1, beats));
    }

    [Fact]
    public void Pass的摘要()
    {
        // 行动者不在势力节拍里（势力无变化）：不写势力一段；别人的变化不算他的。
        Assert.Equal("蓝方：Pass", TurnSummary.Of(P1, [new PowerBeat([Other])]));
        Assert.Equal("蓝方：Pass", TurnSummary.Of(P1, []));
    }

    [Fact]
    public void Pass但势力有变()
    {
        ImmutableArray<SettlementBeat> beats = [new PowerBeat([Other, new PowerChange(P1, 10, 8, 1, 1, TerritoryDelta: -2, GroupDelta: 0)])];
        Assert.Equal("蓝方：Pass · 势力 −2（领地 −2、军势 ±0）", TurnSummary.Of(P1, beats));
    }

    [Fact]
    public void 无提子不写提子()
    {
        ImmutableArray<SettlementBeat> beats =
        [
            new PlacementBeat([Placed("C3", PieceType.Basic)]),
            new PowerBeat([new PowerChange(P1, 3, 4, 1, 1, TerritoryDelta: 0, GroupDelta: 1), Other]),
        ];
        Assert.Equal("蓝方：落 1 子（普通子） · 势力 +1（领地 ±0、军势 +1）", TurnSummary.Of(P1, beats));
    }
}
