using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Relics;
using Siege.Presentation.Show;
using Siege.Presentation.Text;
using static Siege.Core.Tests.SettlementShow.ShowFixtures;

namespace Siege.Core.Tests.SettlementShow;

/// <summary>
/// 规格：settlement-show —— Requirement: 信物揭示节拍（settlement-show-callouts 1.1）。
/// 节拍位置：提子（若有）之后、势力之前；内容为本次结算 <c>IsRevealed</c> 由假变真的信物格与信物名（经 <see cref="Labels.RelicContent"/>）。
/// </summary>
public class 信物揭示节拍Tests
{
    private static readonly GameBoard Before = Stones(("D3", P2), ("E5", P2));
    private static readonly GameBoard After = Stones(("C3", P1), ("C4", P1), ("E5", P2));

    [Fact]
    public void 新揭示的信物()
    {
        // F6 与 H2 首次进入覆盖而被揭示；G1 结算前就已揭示，不入节拍。条目按坐标字典序（Coord.CompareTo 行优先：H2 在 F6 之前，与落子 / 提子节拍一致）。
        SettlementSide before = Side(Before, Reading(P1, 5, 2), Reading(P2, 6, 1)) with
        {
            Relics = [Relic("F6", RelicType.Command, revealed: false), Relic("H2", RelicType.Depot, revealed: false), Relic("G1", RelicType.Vanguard, revealed: true)],
        };
        SettlementSide after = Side(After, Reading(P1, 9, 1), Reading(P2, 4, 2)) with
        {
            Relics = [Relic("F6", RelicType.Command, revealed: true), Relic("H2", RelicType.Depot, revealed: true), Relic("G1", RelicType.Vanguard, revealed: true)],
        };

        ImmutableArray<SettlementBeat> beats = SettlementBeats.Generate(before, after, Order("C3", "C4"));
        Assert.Equal(
            "落子[C3:Basic:P1,C4:Basic:P1]\n提子[D3:P2]\n信物[H2:兵站 +1,F6:军令 +1]\n势力[P1:5->9(+4):2->1,P2:6->4(−2):1->2]",
            Text(beats));
        RelicRevealBeat reveal = Assert.IsType<RelicRevealBeat>(beats[2]);
        Assert.Equal(RelicRevealBeat.Ms, reveal.DurationMs);
        Assert.Equal(600, RelicRevealBeat.Ms);
        Assert.Equal(["H2", "F6"], reveal.Relics.Select(r => r.Coord.ToNotation()));

        // 播放：该格闪光并弹出信物名（进度 500‰ 时闪光 500、飘字年龄 500）。
        var timeline = new ShowTimeline([reveal, beats[3]], ShowDuration.Normal);
        timeline.Advance(300);
        Assert.Equal("飘字[H2:Relic:兵站 +1:500,F6:Relic:军令 +1:500] 合计[] 闪光[H2:500,F6:500] 横幅[]", CalloutText(timeline.Mask()));
        timeline.Advance(300);
        Assert.IsType<PowerBeat>(timeline.Current);
        Assert.Empty(timeline.Mask().RelicFlash);
    }

    [Fact]
    public void 无新揭示()
    {
        // 结算前后揭示状态相同（含一直已揭示的 G1）→ 不含信物揭示节拍；同样的信物列表两次生成得到相同序列。
        ImmutableArray<RelicPublicState> relics = [Relic("F6", RelicType.Command, revealed: false), Relic("G1", RelicType.Vanguard, revealed: true)];
        SettlementSide before = Side(Before, Reading(P1, 5, 2), Reading(P2, 6, 1)) with { Relics = relics };
        SettlementSide after = Side(After, Reading(P1, 9, 1), Reading(P2, 4, 2)) with { Relics = relics };

        ImmutableArray<SettlementBeat> beats = SettlementBeats.Generate(before, after, Order("C3", "C4"));
        Assert.DoesNotContain(beats, b => b is RelicRevealBeat);
        Assert.Equal(3, beats.Length);
        Assert.Equal(Text(beats), Text(SettlementBeats.Generate(before, after, Order("C3", "C4"))));
    }
}
