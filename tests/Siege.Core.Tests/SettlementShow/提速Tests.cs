using System.Collections.Immutable;
using Siege.Presentation.Show;
using static Siege.Core.Tests.SettlementShow.ShowFixtures;

namespace Siege.Core.Tests.SettlementShow;

/// <summary>
/// 规格：settlement-show —— Requirement: 提速（turn-settlement-show 2.2）。
/// </summary>
/// <remarks>
/// 变异验证 M-S2「提速倍率改 2」——<c>ShowTimeline.SpeedUpFactor = 4</c> 改为 <c>2</c> → 红 2（本类 按住提速：总时间 1000 ≠ 500；松开恢复：进度 200 ≠ 400）。还原后 30/30 绿。
/// </remarks>
public class 提速Tests
{
    /// <summary>以固定步长推进到播完，返回（播完顺序的节拍文本，所用总时间 ms）。</summary>
    private static (List<string> Passed, int ElapsedMs) Run(ShowTimeline timeline, int stepMs, bool fast)
    {
        var passed = new List<string>();
        int elapsed = 0;
        while (!timeline.IsFinished)
        {
            elapsed += stepMs;
            passed.AddRange(timeline.Advance(stepMs, fast).Select(Text));
            Assert.True(elapsed <= 100_000, "时间线没有播完");
        }

        return (passed, elapsed);
    }

    [Fact]
    public void 按住提速()
    {
        // 同一序列提速与不提速经过的节拍相同（顺序、内容、数量），提速所需总时间为四分之一（2000 ms → 500 ms）。
        ImmutableArray<SettlementBeat> beats = CaptureExampleBeats();
        (List<string> normal, int normalMs) = Run(new ShowTimeline(beats, ShowDuration.Normal), 50, fast: false);
        (List<string> fast, int fastMs) = Run(new ShowTimeline(beats, ShowDuration.Normal), 50, fast: true);
        Assert.Equal(normal, fast);
        Assert.Equal(3, fast.Count);
        Assert.Equal(2000, normalMs);
        Assert.Equal(500, fastMs);
        Assert.Equal(ShowTimeline.SpeedUpFactor, normalMs / fastMs);
    }

    [Fact]
    public void 松开恢复()
    {
        // 按住 50 ms（计 200 ms）后松开再推进 100 ms：从当前进度起按正常速度继续——落子节拍（500 ms）进度 (200 + 100) / 500 = 600‰。
        var timeline = new ShowTimeline(CaptureExampleBeats(), ShowDuration.Normal);
        Assert.Empty(timeline.Advance(50, fast: true));
        Assert.Equal(400, timeline.ProgressPermille);
        Assert.Empty(timeline.Advance(100));
        Assert.Equal(600, timeline.ProgressPermille);
        Assert.IsType<PlacementBeat>(timeline.Current);
    }

    [Fact]
    public void 提速不省略节拍()
    {
        // 提速下一次推进跨过全部节拍：逐个报告，无一省略；时间线没有"跳到结尾"的入口。
        var timeline = new ShowTimeline(CaptureExampleBeats(), ShowDuration.Normal);
        ImmutableArray<SettlementBeat> passed = timeline.Advance(10_000, fast: true);
        Assert.Equal(Text(CaptureExampleBeats()), Text(passed));
        Assert.True(timeline.IsFinished);
    }
}
