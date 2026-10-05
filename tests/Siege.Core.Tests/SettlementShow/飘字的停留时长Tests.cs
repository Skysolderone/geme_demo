using Siege.Core.Board;
using Siege.Presentation.Show;
using static Siege.Core.Tests.SettlementShow.ShowFixtures;

namespace Siege.Core.Tests.SettlementShow;

/// <summary>
/// 规格：settlement-show —— Requirement: 飘字的停留时长（follow-opponent D6：0.6 秒 → 1.4 秒）。
/// 算例：1 枚落子（节拍 250 ms）+ 提子节拍（600 ms）+ 势力节拍（900 ms），全长 1750 ms。M6：寿命改回 600 → 第一个用例红。
/// tiered-number-show 1.3 增两条：军势揭示条目的结果自末步开始按末步档位停留（1.4 / 1.4 / 1.6 / 1.9 / 2.2 秒），可跨后续节拍。
/// </summary>
/// <remarks>
/// 变异验证（tiered-number-show 段 A）。每条变异由脚本做：二进制读入原文并另存备份 → 断言锚点恰命中 1 次 → 改写 → 跑 SettlementShow + TacticalLayers 两个命名空间（基线 215 通过 / 2 跳过）→ finally 里重新写回原文（mtime 随之刷新）并逐字节比对；全部还原后确认跑 0 红。
/// M-H1「结果停留不分档」——<c>ShowTimeline.AddReveals</c> 里结果停留满的判定恒取 <c>Callout.LifetimeMs</c>（1400）→ 红 1（高档停留更久）。
/// M-H2「揭示条目不跨节拍」——<c>ShowTimeline.Mask</c> 里军势揭示只在当前节拍（<c>i == _index</c>）时才列条目 → 红 5（揭示结果跨到势力节拍、高档停留更久、揭示条目逐步推进、
/// 高档冲击环与镜头轻震Tests 的 低档没有 / 同一时刻至多一个轻震）。
/// M-H3「尚未轮到的条目也列」——<c>sinceBeatMs &lt; beat.StepStartMs(e, 0)</c> 改为 <c>sinceBeatMs &lt; 0</c> → 红 3（高档停留更久、高档冲击环与镜头轻震Tests.同一时刻至多一个轻震、军势揭示节拍Tests.压缩后遮罩与音效按压缩后的时刻）。
/// M6（飘字寿命改回 600）复跑：红 7（本类 落子飘字跨到势力节拍 / 超过寿命即消失 等）。
/// </remarks>
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

    [Fact]
    public void 揭示结果跨到势力节拍()
    {
        // 军势揭示节拍只有一个一步条目（军势 3，一档，220 ms），其后是势力重算节拍（900 ms）：揭示节拍开始后 800 ms 已在势力节拍里，该条目的结果仍在显示。
        var timeline = new ShowTimeline(
            [new PowerRevealBeat([RevealEntry.From(Group(P1, 3, 0, 0, 3, "C3"))]), new PowerBeat([new PowerChange(P1, 3, 5, 1, 1)])],
            ShowDuration.Normal);
        Assert.Equal("C3:3:3:1:0:末步:1:0", RevealText(timeline.Mask()));   // 一步条目：第一步就是结果

        timeline.Advance(110);
        Assert.Equal("C3:3:3:1:500:末步:1:78", RevealText(timeline.Mask()));   // 步内进度 110 / 220；结果年龄 110 / 1400

        timeline.Advance(690);
        Assert.IsType<PowerBeat>(timeline.Current);
        RevealDisplay shown = Assert.Single(timeline.Mask().Reveals);
        Assert.Equal(("C3", P1, "3", true, 1), (shown.Coord.ToNotation(), shown.Owner, shown.RunningText, shown.AtFinal, shown.FinalTier));
        Assert.Equal((1000, 800 * 1000 / 1400), (shown.StepPermille, shown.ResultAgePermille));

        // 演出结束（1120 ms）时揭示条目随之消失——结果停留（1400 ms）被截断（design.md A1）。
        timeline.Advance(320);
        Assert.True(timeline.IsFinished);
        Assert.Empty(timeline.Mask().Reveals);
    }

    [Fact]
    public void 高档停留更久()
    {
        // 两个一步条目：军势 3（一档，0 ms 开始、220 ms）与军势 70（五档 ≥ 64，220 ms 开始、660 ms）；其后势力（900 ms）与两条横幅（1600 ms），演出全长 3380 ms，不提前结束。
        // 一档的结果自末步开始 1400 ms 后不再显示；五档的结果到 2200 ms 才不再显示。
        var timeline = new ShowTimeline(
            [
                new PowerRevealBeat([RevealEntry.From(Group(P1, 3, 0, 0, 3, "C3")), RevealEntry.From(Group(P1, 70, 0, 0, 70, "G7"))]),
                new PowerBeat([new PowerChange(P1, 3, 5, 1, 1)]),
                new BannerBeat([BannerBeat.EliminatedText(P2), BannerBeat.MatchEndedText]),
            ],
            ShowDuration.Normal);
        Assert.Equal(3380, timeline.TotalDurationMs);

        // 尚未轮到的条目不列。
        timeline.Advance(219);
        Assert.Equal(["C3"], timeline.Mask().Reveals.Select(r => r.Coord.ToNotation()));
        timeline.Advance(1);
        Assert.Equal("C3:3:3:1:1000:末步:1:157,G7:70:70:5:0:末步:5:0", RevealText(timeline.Mask()));

        timeline.Advance(1399 - 220);
        Assert.Equal(["C3", "G7"], timeline.Mask().Reveals.Select(r => r.Coord.ToNotation()));
        timeline.Advance(1);   // 1400 ms：一档的结果停留满
        Assert.Equal(["G7"], timeline.Mask().Reveals.Select(r => r.Coord.ToNotation()));

        timeline.Advance(220 + 2199 - 1400);   // 五档末步开始后 2199 ms
        Assert.Equal("G7:70:70:5:1000:末步:5:999", RevealText(timeline.Mask()));
        timeline.Advance(1);   // 2200 ms：五档的结果停留满
        Assert.False(timeline.IsFinished);
        Assert.Empty(timeline.Mask().Reveals);

        // 二、三、四档的停留：1400 / 1600 / 1900 ms（一步条目，军势 10 / 20 / 40，阈值 8 / 16 / 32 / 64）。
        foreach ((int power, int holdMs) in new[] { (10, 1400), (20, 1600), (40, 1900) })
        {
            var one = new ShowTimeline(
                [
                    new PowerRevealBeat([RevealEntry.From(Group(P1, power, 0, 0, power, "C3"))]),
                    new PowerBeat([new PowerChange(P1, 3, 5, 1, 1)]),
                    new BannerBeat([BannerBeat.EliminatedText(P2), BannerBeat.MatchEndedText]),
                ],
                ShowDuration.Normal);
            one.Advance(holdMs - 1);
            Assert.Single(one.Mask().Reveals);
            one.Advance(1);
            Assert.Empty(one.Mask().Reveals);
        }
    }

    [Fact]
    public void 揭示条目逐步推进()
    {
        // 四步条目（军势 18 = ⌊(10+2)×1.5⌋，三档：档位一、一、二、三）：自第一步开始起持续显示，逐步给出累计文案、最新一步与步内进度；未到末步时结果年龄为 0。
        var timeline = new ShowTimeline(
            [new PowerRevealBeat([RevealEntry.From(Group(P1, 10, 2, 1, 18, "C3"))]), new PowerBeat([new PowerChange(P1, 3, 5, 1, 1)])],
            ShowDuration.Normal);
        Assert.Equal("C3:10:10:1:0:未到:3:0", RevealText(timeline.Mask()));
        timeline.Advance(330);
        Assert.Equal("C3:10+2:+2:1:500:未到:3:0", RevealText(timeline.Mask()));
        timeline.Advance(220);
        Assert.Equal("C3:(10+2)×1.5:×1.5:2:500:未到:3:0", RevealText(timeline.Mask()));
        timeline.Advance(220);
        Assert.Equal("C3:(10+2)×1.5 = 18:= 18:3:500:末步:3:68", RevealText(timeline.Mask()));   // 末步开始后 110 ms：110 / 1600
        timeline.Advance(330);
        Assert.IsType<PowerBeat>(timeline.Current);
        Assert.Equal("C3:(10+2)×1.5 = 18:= 18:3:1000:末步:3:275", RevealText(timeline.Mask()));   // 440 / 1600
    }
}
