using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Presentation.Show;
using static Siege.Core.Tests.SettlementShow.ShowFixtures;

namespace Siege.Core.Tests.SettlementShow;

/// <summary>
/// 规格：settlement-show —— Requirement: 演出按节拍依次播放（turn-settlement-show 2.1、2.3、2.5、2.7、3.5）。
/// 时间线 <see cref="ShowTimeline"/> 不读时钟，用固定增量逐步推进断言节拍切换时刻。
/// </summary>
public class 演出按节拍依次播放Tests
{
    /// <summary>1.2 算例的时间线：落子 2 枚（500 ms）→ 提子（600 ms）→ 势力（900 ms），共 2000 ms。</summary>
    private static ShowTimeline Example() => new(CaptureExampleBeats(), ShowDuration.Normal);

    [Fact]
    public void 节拍切换时刻()
    {
        ShowTimeline timeline = Example();
        Assert.Equal(2000, timeline.TotalDurationMs);
        var switches = new List<string>();
        for (int t = 100; t <= 2100; t += 100)
        {
            foreach (SettlementBeat passed in timeline.Advance(100))
            {
                switches.Add($"{t}:{Text(passed)}");
            }
        }

        // 落子在 500 ms 播完、提子在 1100 ms 播完、势力在 2000 ms 播完。
        Assert.Equal(
            ["500:落子[C3:Basic:P1,C4:Basic:P1]", "1100:提子[D3:P2]", "2000:势力[P1:5->9(+4):2->1,P2:6->4(−2):1->2]"],
            switches);
        Assert.True(timeline.IsFinished);

        // 切换前后当前节拍：499 ms 仍是落子、500 ms 起是提子（进度 0）。
        ShowTimeline again = Example();
        again.Advance(499);
        Assert.IsType<PlacementBeat>(again.Current);
        Assert.Equal(998, again.ProgressPermille);
        again.Advance(1);
        Assert.IsType<CaptureBeat>(again.Current);
        Assert.Equal(0, again.ProgressPermille);
    }

    [Fact]
    public void 落子逐枚出现()
    {
        // 5 枚：整拍 1000 ms，第 i 枚在 i×200 ms 起出现、200 ms 出现完毕；最后一枚在整拍结束之前已开始出现。
        var pieces = Enumerable.Range(1, 5).Select(i => new PlacedPiece(Coord.Parse($"A{i}"), PieceType.Basic, P1)).ToImmutableArray();
        var timeline = new ShowTimeline([new PlacementBeat(pieces)], ShowDuration.Normal);
        Assert.Equal("隐藏[A1,A2,A3,A4,A5] 出现[] 仍显示[] 淡出0 势力[]", Text(timeline.Mask()));

        timeline.Advance(100);
        Assert.Equal("隐藏[A2,A3,A4,A5] 出现[A1:500] 仍显示[] 淡出0 势力[]", Text(timeline.Mask()));

        timeline.Advance(150);   // 250 ms：A1 已完全出现，A2 出现到一半
        Assert.Equal("隐藏[A3,A4,A5] 出现[A2:250] 仍显示[] 淡出0 势力[]", Text(timeline.Mask()));

        timeline.Advance(650);   // 900 ms：最后一枚出现中
        Assert.Equal("隐藏[] 出现[A5:500] 仍显示[] 淡出0 势力[]", Text(timeline.Mask()));

        Assert.Single(timeline.Advance(100));
        Assert.True(timeline.IsFinished);
    }

    [Fact]
    public void 提子节拍开始前被提棋子仍在()
    {
        // 落子节拍进行中：本批棋子尚未播到的隐藏、D3 仍在"仍需显示"集合里、记分显示仍为旧值。
        ShowTimeline timeline = Example();
        Assert.Equal("隐藏[C3,C4] 出现[] 仍显示[D3] 淡出0 势力[P1:5:0:旧值,P2:6:0:旧值]", Text(timeline.Mask()));

        timeline.Advance(125);   // 第 1 枚出现一半
        ShowMask mask = timeline.Mask();
        Assert.Equal("隐藏[C4] 出现[C3:500] 仍显示[D3] 淡出0 势力[P1:5:0:旧值,P2:6:0:旧值]", Text(mask));
        Assert.Contains(mask.StillShown, p => p.Coord == Coord.Parse("D3") && p.Owner == P2);

        // 提子节拍进行中：被提棋子仍在列表里、按进度淡出；势力仍是旧值。
        timeline.Advance(375 + 300);
        Assert.Equal("隐藏[] 出现[] 仍显示[D3] 淡出500 势力[P1:5:0:旧值,P2:6:0:旧值]", Text(timeline.Mask()));

        // 势力节拍进行中：被提棋子已不在，势力滚动到中间值（settlement-show-callouts D2 分段到账：算例只有总值 → 单段 0.35 秒，175 ms 处为一半）。
        timeline.Advance(300 + 175);
        Assert.Equal("隐藏[] 出现[] 仍显示[] 淡出0 势力[P1:7:194:滚动,P2:5:194:滚动]", Text(timeline.Mask()));
    }

    [Fact]
    public void 演出结束后画面与快照一致()
    {
        // 最后一个节拍播完：遮罩为空——棋盘与记分显示即结算后公开快照（图形侧不再叠加任何中间态）。
        ShowTimeline timeline = Example();
        timeline.Advance(2000);
        Assert.True(timeline.IsFinished);
        Assert.Null(timeline.Current);
        Assert.Equal(1000, timeline.ProgressPermille);
        Assert.True(timeline.Mask().IsEmpty);
        Assert.Empty(timeline.Advance(100));
    }

    [Fact]
    public void 单次推进跨过多个节拍时逐个报告()
    {
        // 一次推进 10 秒：三个节拍按序全部报告，状态为播完。
        ShowTimeline timeline = Example();
        ImmutableArray<SettlementBeat> passed = timeline.Advance(10_000);
        Assert.Equal(Text(CaptureExampleBeats()), Text(passed));
        Assert.True(timeline.IsFinished);

        // 跨过部分：一次推进 1200 ms 报告前两个，当前为势力节拍、进度 (1200 − 1100) / 900。
        ShowTimeline partial = Example();
        Assert.Equal(2, partial.Advance(1200).Length);
        Assert.IsType<PowerBeat>(partial.Current);
        Assert.Equal(111, partial.ProgressPermille);
    }

    [Fact]
    public void 空序列的短停顿()
    {
        // 势力不变的 Pass：节拍序列为空，正常模式下保留 0.3 秒停顿（design.md D5），期间遮罩为空、没有当前节拍。
        var timeline = new ShowTimeline([], ShowDuration.Normal);
        Assert.False(timeline.IsFinished);
        Assert.Null(timeline.Current);
        Assert.Equal(300, timeline.TotalDurationMs);
        Assert.True(timeline.Mask().IsEmpty);

        Assert.Empty(timeline.Advance(299));
        Assert.False(timeline.IsFinished);
        Assert.Empty(timeline.Advance(1));   // 没有节拍可报告
        Assert.True(timeline.IsFinished);
    }

    [Fact]
    public void 本机玩家确认压缩为一次闪动()
    {
        // design.md A2：本机玩家自己的结算，落子节拍压缩为 0.25 秒的整体确认闪动——全部棋子同步出现，提子与势力节拍照常。
        (GameBoard before, GameBoard after, ImmutableArray<PowerReading> pb, ImmutableArray<PowerReading> pa) = CaptureExample();
        ImmutableArray<SettlementBeat> beats = SettlementBeats.Generate(before, after, pb, pa, Order("C3", "C4"), compressPlacement: true);
        PlacementBeat placement = Assert.IsType<PlacementBeat>(beats[0]);
        Assert.True(placement.Compressed);
        Assert.Equal(250, placement.DurationMs);
        Assert.Equal(600, beats[1].DurationMs);
        Assert.Equal(900, beats[2].DurationMs);

        var timeline = new ShowTimeline(beats, ShowDuration.Normal);
        timeline.Advance(125);
        Assert.Equal("隐藏[] 出现[C3:500,C4:500] 仍显示[D3] 淡出0 势力[P1:5:0:旧值,P2:6:0:旧值]", Text(timeline.Mask()));
        Assert.Single(timeline.Advance(125));
        Assert.IsType<CaptureBeat>(timeline.Current);
    }
}
