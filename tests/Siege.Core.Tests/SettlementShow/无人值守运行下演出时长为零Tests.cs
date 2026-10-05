using System.Collections.Immutable;
using Siege.Presentation.Show;
using static Siege.Core.Tests.SettlementShow.ShowFixtures;

namespace Siege.Core.Tests.SettlementShow;

/// <summary>
/// 规格：settlement-show —— Requirement: 无人值守运行下演出时长为零（turn-settlement-show 2.4）。
/// 「自动演示的帧数不变」「定帧截图不变」两条 Scenario 在 Godot 侧用同种子基线比对（tasks 0.1 / 4.1），这里守零时长模式本身。
/// </summary>
/// <remarks>
/// 变异验证 M-S3「零时长模式仍占推进」——<c>ShowTimeline</c> 构造函数里 <c>IsFinished = duration == ShowDuration.Zero</c> 改为 <c>IsFinished = false</c> → 红 1（零时长模式创建即播完）。
/// 零时长模式节拍序列照常生成 不看 IsFinished，按设计不红。还原后 30/30 绿。
/// tiered-number-show 段 A 检查复跑 M-S3（SettlementShow + TacticalLayers + VisualStyleBaseline，基线 240 通过 / 2 跳过）：红 5——上面的"红 1"是首版时的数，
/// 此后各 change 陆续加了零时长守门：本类 零时长模式创建即播完、横幅节拍Tests.无人值守仍零时长、演出音效提示Tests.零时长无提示、
/// 落子与提子节拍的内容Tests.飘字寿命跨节拍、高档冲击环与镜头轻震Tests.零时长下不出现。
/// 同批复跑 M-SC2（<c>Mask()</c> 去掉 <c>IsFinished ||</c>）：红 6（上面五条，加 飘字的停留时长Tests.揭示结果跨到势力节拍）。
/// </remarks>
public class 无人值守运行下演出时长为零Tests
{
    [Fact]
    public void 零时长模式创建即播完()
    {
        // 创建即播完、遮罩为空、总时长 0、不占用任何推进。
        var timeline = new ShowTimeline(CaptureExampleBeats(), ShowDuration.Zero);
        Assert.True(timeline.IsFinished);
        Assert.Null(timeline.Current);
        Assert.Equal(1000, timeline.ProgressPermille);
        Assert.Equal(0, timeline.TotalDurationMs);
        Assert.True(timeline.Mask().IsEmpty);

        Assert.Empty(timeline.Advance(16));
        Assert.True(timeline.IsFinished);
        Assert.True(timeline.Mask().IsEmpty);

        // 空序列在零时长模式下同样没有 0.3 秒停顿。
        var empty = new ShowTimeline([], ShowDuration.Zero);
        Assert.True(empty.IsFinished);
        Assert.Equal(0, empty.TotalDurationMs);
        Assert.True(ShowTimeline.Finished.IsFinished);
    }

    [Fact]
    public void 零时长模式节拍序列照常生成()
    {
        // 节拍序列照常生成（保证该路径被覆盖），只是时间线不播。
        ImmutableArray<SettlementBeat> beats = CaptureExampleBeats();
        var timeline = new ShowTimeline(beats, ShowDuration.Zero);
        Assert.Equal(Text(beats), Text(timeline.Beats));
        Assert.Equal(3, timeline.Beats.Length);
        Assert.Equal(ShowDuration.Zero, timeline.Duration);
    }
}
