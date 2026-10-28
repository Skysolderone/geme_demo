using Siege.Core.Match;
using Siege.Presentation.Text;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.Battlefield;

/// <summary>
/// cycle-stop tasks 2.3（裁决 S3 / S4）：表现层的大回合上限提示——图形版提示条与终端大回合开始行共用 <see cref="Labels"/> 的同一份条件与文案，
/// 图形版 / 终端只接线（<c>src/godot/scripts/MatchSession.Rebuild</c>、<c>Hud</c> 提示标签、<c>BoardRenderer.Status</c>）。结算原因文案一并在此守门。
/// </summary>
/// <remarks>变异 M-RC9：<c>RoundCapNotice</c> 的窗口 <c>&gt; 5</c> 改 <c>&gt;= 5</c> → 「临近上限才提示」红；M-RC10：上限 0 也提示 → 同条红。</remarks>
public class 大回合上限提示条Tests
{
    [Fact]
    public void 临近上限才提示()
    {
        // S3：上限 − 当前大回合 ≤ 5 时每大回合给一句"第 X 大回合结束时按势力结算"，更早不提示；上限 0（不启用）永不提示；到达上限那一大回合也提示。
        Assert.Equal(5, Labels.RoundCapNoticeWindow);
        Assert.Null(Labels.RoundCapNotice(100, 1));
        Assert.Null(Labels.RoundCapNotice(100, 94));
        Assert.Equal("第 100 大回合结束时按势力结算（还剩 5 个大回合）", Labels.RoundCapNotice(100, 95));
        Assert.Equal("第 100 大回合结束时按势力结算（还剩 1 个大回合）", Labels.RoundCapNotice(100, 99));
        Assert.Equal("第 100 大回合结束时按势力结算（本大回合即上限）", Labels.RoundCapNotice(100, 100));
        Assert.Null(Labels.RoundCapNotice(0, 300));
        Assert.Null(Labels.RoundCapNotice(0, 0));

        // 终端大回合开始行的上限段。
        Assert.Equal("上限 100（还剩 97）", Labels.RoundCapStatus(100, 3));
        Assert.Equal("上限 60（还剩 0）", Labels.RoundCapStatus(60, 60));
        Assert.Equal("上限 不启用", Labels.RoundCapStatus(0, 7));
    }

    [Fact]
    public void 提示读公开视图的上限()
    {
        // 图形版与终端都只读公开快照里的 RoundCap / MajorRound（不读 MatchFlow 内部）：按缺省 100 建局第 95 大回合起提示，上限 0 的局不提示。
        MatchFlow match = Started([0, 1, 3, 2]).AtRound(95, [P0, P1, P2, P3]);
        MatchPublicView view = match.Publish();
        Assert.Equal((100, 95), (view.RoundCap, view.MajorRound));
        Assert.Equal("第 100 大回合结束时按势力结算（还剩 5 个大回合）", Labels.RoundCapNotice(view.RoundCap, view.MajorRound));
        Assert.Null(Labels.RoundCapNotice(Started([0, 1, 3, 2], options: Options with { RoundCap = 0 }).AtRound(95).Publish().RoundCap, 95));
    }

    [Fact]
    public void 结算原因文案()
    {
        // battlefield「战场结算」：结算原因记为 整轮 Pass / 填满 / 减员 / 上限 之一；终端结算行与报告用同一份文案。
        Assert.Equal(["整轮 Pass", "填满", "减员", "上限"], Enum.GetValues<SettlementReason>().Select(Labels.Settlement));
        Assert.Equal(nameof(SettlementReason.RoundCap), Enum.GetNames<SettlementReason>()[^1]);   // 新值追加在末尾，既有序号不变
    }
}
