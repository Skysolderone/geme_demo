using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Scoring;
using Siege.Presentation.Preview;
using Siege.Presentation.Show;
using Siege.Presentation.Style;

namespace Siege.Core.Tests.SettlementShow;

public class 分档揭示移植Tests
{
    [Fact]
    public void 揭示步骤沿用棋串明细的倍增与阵型因子()
    {
        var group = new GroupPower(new PlayerId(1), [Coord.Parse("C3")], BaseTotal: 5,
            LineBonus: 2, SynergyBonus: 0, HighGroundBonus: 0, BannerBonus: 0,
            ChainBonus: 0, SentryBonus: 0, BoundaryBonus: 0, MultiplierCount: 1,
            Power: 23) { FormationTier = 2 };

        RevealEntry entry = RevealEntry.From(group);
        Assert.Equal(3, entry.FinalTier);
        Assert.Equal(["5", "+2", "×1.5", "×2.25", "= 23"], entry.Steps.Select(s => s.Text));
        Assert.Equal(GroupPowerView.From(group).ShortFormulaText, entry.Steps[^1].RunningText);
    }

    [Fact]
    public void 五档末步持续显示亮环和轻震且收尾不截断()
    {
        var entry = new RevealEntry(Coord.Parse("C3"), new PlayerId(1), 70,
            [new RevealStep("70", "70", 5, NumberTierStyle.For(5).FinalStepMs)]);
        var beat = new PowerRevealBeat([entry]);
        var timeline = new ShowTimeline([beat], ShowDuration.Normal);

        ShowMask initial = timeline.Mask();
        Assert.Equal(5, Assert.Single(initial.Reveals).StepTier);
        Assert.Equal(2, Assert.Single(initial.Rings).Count);
        Assert.Equal(0, initial.ShakePermille);

        timeline.Advance(beat.DurationMs);
        Assert.Null(timeline.Current);
        Assert.False(timeline.IsFinished);
        Assert.Single(timeline.Mask().Reveals);
        Assert.True(timeline.TailMs > 0);

        timeline.Advance(timeline.TailMs);
        Assert.True(timeline.IsFinished);
        Assert.True(timeline.Mask().IsEmpty);
    }
}
