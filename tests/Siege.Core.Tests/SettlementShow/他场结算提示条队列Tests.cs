using Siege.Presentation.Show;

namespace Siege.Core.Tests.SettlementShow;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/settlement-show —— Requirement: 战场结算横幅（他场结算提示条，board-link-mode 段 D-d）。
/// 提示条改为队列显示（<see cref="NoticeQueue"/>）：多条依次、每条停留约 3 s 后自动消失；无人值守零时长入队即空。
/// </summary>
/// <remarks>变异验证（段 D-d 实跑）：M-G1 <c>Advance</c> 到期后不换下一条（把 while 改成只清空）→ 「多条依次显示_每条停留约三秒」「一次推进跨过多条_多余时间顺延」「显示中再入队排到后面_不覆盖」红 3；M-G2 零时长也入队 → 「零时长入队即空」红 1。</remarks>
public class 他场结算提示条队列Tests
{
    [Fact]
    public void 多条依次显示_每条停留约三秒()
    {
        var queue = new NoticeQueue(ShowDuration.Normal);
        Assert.True(queue.IsEmpty);

        queue.Enqueue(["战场 出生区 3 已结算，胜者 蓝方(P1)", "战场 出生区 5 已结算，胜者 金方(P2)"]);
        Assert.Equal("战场 出生区 3 已结算，胜者 蓝方(P1)", queue.Current);
        Assert.Equal(1, queue.PendingCount);

        queue.Advance(NoticeQueue.DwellMs - 1);
        Assert.Equal("战场 出生区 3 已结算，胜者 蓝方(P1)", queue.Current);   // 还差 1 ms

        queue.Advance(1);
        Assert.Equal("战场 出生区 5 已结算，胜者 金方(P2)", queue.Current);   // 第二条接上
        Assert.Equal(0, queue.PendingCount);

        queue.Advance(NoticeQueue.DwellMs);
        Assert.Null(queue.Current);
        Assert.True(queue.IsEmpty);
        Assert.InRange(NoticeQueue.DwellMs, 2500, 3500);   // "约 3 s"
    }

    [Fact]
    public void 一次推进跨过多条_多余时间顺延()
    {
        var queue = new NoticeQueue(ShowDuration.Normal);
        queue.Enqueue(["一", "二", "三"]);
        queue.Advance((2 * NoticeQueue.DwellMs) + 100);
        Assert.Equal("三", queue.Current);
        Assert.Equal(100, queue.ElapsedMs);
        queue.Advance(NoticeQueue.DwellMs - 100);
        Assert.Null(queue.Current);
    }

    [Fact]
    public void 显示中再入队排到后面_不覆盖()
    {
        var queue = new NoticeQueue(ShowDuration.Normal);
        queue.Enqueue(["一"]);
        queue.Advance(1000);
        queue.Enqueue(["二"]);
        Assert.Equal("一", queue.Current);   // 不被最近一条覆盖
        Assert.Equal(1, queue.PendingCount);
        queue.Advance(NoticeQueue.DwellMs - 1000);
        Assert.Equal("二", queue.Current);
    }

    [Fact]
    public void 零时长入队即空()
    {
        // Scenario「无人值守零时长」：提示条不显示、不排队（文案仍由调用方记日志）。
        var queue = new NoticeQueue(ShowDuration.Zero);
        queue.Enqueue(["战场 出生区 3 已结算，胜者 蓝方(P1)"]);
        Assert.Null(queue.Current);
        Assert.True(queue.IsEmpty);
        queue.Advance(10);
        Assert.True(queue.IsEmpty);
    }

    [Fact]
    public void 空文案忽略_负推进拒绝()
    {
        var queue = new NoticeQueue(ShowDuration.Normal);
        queue.Enqueue([string.Empty]);
        Assert.True(queue.IsEmpty);
        Assert.Throws<ArgumentOutOfRangeException>(() => queue.Advance(-1));
    }
}
