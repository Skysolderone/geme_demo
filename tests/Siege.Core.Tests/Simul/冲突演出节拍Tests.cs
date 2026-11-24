using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Simul;
using Siege.Presentation.Show;
using Siege.Presentation.Text;
using static Siege.Core.Tests.Simul.SimulFixtures;

namespace Siege.Core.Tests.Simul;

/// <summary>openspec/specs/simultaneous-round —— Requirement: 冲突演出节拍（Q15；A3 裁决 D3）。</summary>
/// <remarks>变异记录（A3-S1 实跑）：M24 冲突横幅恒为第一格 → 红 1（时间线逐格显示横幅与飘字）；M25 冲突节拍排到落子之后 → 红 2。</remarks>
public sealed class 冲突演出节拍Tests
{
    /// <summary>两格并列冲突（E5 第 5 行、C7 第 7 行），返回回合开始与结算后的投影快照和报告。</summary>
    private static (MatchPublicView Before, MatchPublicView After, SimulRoundReport Report) TwoConflicts()
    {
        SimulFlow flow = Started(2, order: [P0, P1]);
        MatchPublicView before = flow.PublishFor(P0).View;
        flow.Submit(P0, "E5", "C7");
        flow.Submit(P1, "E5", "C7");
        SimulRoundReport report = flow.ResolveRound();
        return (before, flow.PublishFor(P0).View, report);
    }

    [Fact]
    public void 冲突横幅先于落子且按报告次序()
    {
        (MatchPublicView before, MatchPublicView after, SimulRoundReport report) = TwoConflicts();
        var beats = SimulBeats.Generate(before, after, report);

        ConflictBeat conflicts = Assert.IsType<ConflictBeat>(beats[0]);
        Assert.Equal(["E5", "C7"], conflicts.Conflicts.Select(c => c.Coord.ToNotation()));
        Assert.Equal(report.Conflicts.Select(Labels.SimulConflictBanner), conflicts.Conflicts.Select(c => c.BannerText));
        Assert.Equal(2 * ConflictBeat.PerConflictMs, conflicts.DurationMs);

        // 其后与现有节拍生成逐拍相同（落子在前）。
        var settlement = SettlementBeats.Generate(before, after, record: null);
        Assert.Equal(settlement.Select(b => (b.GetType(), b.DurationMs)), beats.Skip(1).Select(b => (b.GetType(), b.DurationMs)));
        PlacementBeat placed = Assert.IsType<PlacementBeat>(beats[1]);
        Assert.Equal(["E5", "C7"], placed.Pieces.Select(p => p.Coord.ToNotation()));   // 没有结算记录：按坐标字典序
    }

    [Fact]
    public void 没有冲突时与现有节拍相同()
    {
        SimulFlow flow = Started(2);
        MatchPublicView before = flow.PublishFor(P0).View;
        flow.Submit(P0, "D4");
        flow.Submit(P1, "J9");
        SimulRoundReport report = flow.ResolveRound();
        MatchPublicView after = flow.PublishFor(P0).View;

        var beats = SimulBeats.Generate(before, after, report);
        Assert.DoesNotContain(beats, b => b is ConflictBeat);
        Assert.Equal(SettlementBeats.Generate(before, after, record: null).Select(b => (b.GetType(), b.DurationMs)), beats.Select(b => (b.GetType(), b.DurationMs)));
    }

    [Fact]
    public void 冲突文字与飘字转录战力与骰点()
    {
        // E1：P0 3 : P1 2，P0 胜，不掷骰。
        SimulFlow e1 = 同格冲突Tests.E1();
        SimulConflict plain = Assert.Single(e1.ResolveRound().Conflicts);
        Assert.Equal($"E5 冲突：{Labels.Player(P0)} 3 : {Labels.Player(P1)} 2，{Labels.Player(P0)} 胜", Labels.SimulConflictBanner(plain));
        Assert.StartsWith("3 : 2 ", Labels.SimulConflictCallout(plain), StringComparison.Ordinal);

        // E4：2 : 2 : 1，掷骰 3 : 3 → 5 : 2，P0 胜。
        SimulFlow e4 = Started(3, f =>
        {
            f.Debug.Stones(P0, "D5");
            f.Debug.Stones(P1, "E4");
        }, order: [P0, P1, P2], seed: 同格冲突Tests.DiceSeed);
        e4.Submit(P0, "E5");
        e4.Submit(P1, "E5");
        e4.Submit(P2, "E5");
        SimulConflict diced = Assert.Single(e4.ResolveRound().Conflicts);
        Assert.Equal(
            $"E5 冲突：{Labels.Player(P0)} 2 : {Labels.Player(P1)} 2 : {Labels.Player(P2)} 1，掷骰 3 : 3 → 5 : 2，{Labels.Player(P0)} 胜",
            Labels.SimulConflictBanner(diced));
        Assert.StartsWith("掷骰 5 : 2 ", Labels.SimulConflictCallout(diced), StringComparison.Ordinal);

        foreach (string text in new[] { Labels.SimulConflictBanner(plain), Labels.SimulConflictCallout(plain), Labels.SimulConflictBanner(diced) })
        {
            Assert.DoesNotContain("棋盘", text, StringComparison.Ordinal);
            Assert.DoesNotContain("棋子", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void 时间线逐格显示横幅与飘字()
    {
        (MatchPublicView before, MatchPublicView after, SimulRoundReport report) = TwoConflicts();
        var beats = SimulBeats.Generate(before, after, report);
        var timeline = new ShowTimeline(beats, ShowDuration.Normal);
        ConflictBeat conflicts = (ConflictBeat)beats[0];

        ShowMask first = timeline.Mask();
        Assert.Equal(conflicts.Conflicts[0].BannerText, first.Banner!.Text);
        Assert.Equal((0, 2), (first.Banner.Index, first.Banner.Count));
        Callout e5 = Assert.Single(first.Callouts);
        Assert.Equal((Coord.Parse("E5"), CalloutKind.Conflict, conflicts.Conflicts[0].CalloutText), (e5.Coord, e5.Kind, e5.Text));

        timeline.Advance(ConflictBeat.PerConflictMs);
        ShowMask second = timeline.Mask();
        Assert.Equal(conflicts.Conflicts[1].BannerText, second.Banner!.Text);
        Assert.Equal(1, second.Banner.Index);
        Assert.Contains(second.Callouts, c => c.Coord == Coord.Parse("C7") && c.Kind == CalloutKind.Conflict);
        Assert.Contains(second.Callouts, c => c.Coord == Coord.Parse("E5") && c.Kind == CalloutKind.Conflict);   // 飘字可跨格存活

        Assert.Single(timeline.Advance(ConflictBeat.PerConflictMs));   // 冲突节拍播完，进入落子节拍
        Assert.IsType<PlacementBeat>(timeline.Current);
        Assert.True(timeline.TotalDurationMs >= beats.Sum(b => b.DurationMs));
    }
}
