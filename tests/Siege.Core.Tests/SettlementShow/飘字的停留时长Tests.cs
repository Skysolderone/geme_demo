using Siege.Core.Board;
using Siege.Presentation.Show;
using static Siege.Core.Tests.SettlementShow.ShowFixtures;

namespace Siege.Core.Tests.SettlementShow;

/// <summary>
/// 规格：settlement-show —— Requirement: 飘字的停留时长（follow-opponent D6：0.6 秒 → 1.4 秒）。
/// 算例：1 枚落子（节拍 250 ms）+ 提子节拍（600 ms）+ 势力节拍（900 ms），全长 1750 ms。M6：寿命改回 600 → 第一个用例红。
/// </summary>
public class 飘字的停留时长Tests
{
    private static ShowTimeline Timeline() => new(
        [
            new PlacementBeat([new PlacedPiece(Coord.Parse("C3"), PieceType.Basic, P1)]),
            new CaptureBeat([new CapturedPiece(Coord.Parse("D3"), P2, PieceType.Basic)]),
            new PowerBeat([new PowerChange(P1, 3, 5, 1, 1)]),
        ],
        ShowDuration.Normal);

    [Fact]
    public void 落子飘字跨到势力节拍()
    {
        ShowTimeline timeline = Timeline();
        timeline.Advance(1000);
        Assert.IsType<PowerBeat>(timeline.Current);
        Callout placed = Assert.Single(timeline.Mask().Callouts, c => c.Kind == CalloutKind.Placement);
        Assert.Equal("C3", placed.Coord.ToNotation());
        Assert.Equal(1000 * 1000 / Callout.LifetimeMs, placed.AgePermille);
    }

    [Fact]
    public void 超过寿命即消失()
    {
        ShowTimeline timeline = Timeline();
        timeline.Advance(1400);
        Assert.False(timeline.IsFinished);
        Assert.DoesNotContain(timeline.Mask().Callouts, c => c.Kind == CalloutKind.Placement);

        // 提子飘字 250 ms 才出现，此刻年龄 1150 ms，还在。
        Assert.Contains(timeline.Mask().Callouts, c => c.Kind == CalloutKind.Capture);
    }

    [Fact]
    public void 演出结束飘字全部消失()
    {
        ShowTimeline timeline = Timeline();
        timeline.Advance(1750);
        Assert.True(timeline.IsFinished);
        Assert.Empty(timeline.Mask().Callouts);
    }
}
