using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Scoring;
using Siege.Presentation.Show;
using static Siege.Core.Tests.SettlementShow.ShowFixtures;

namespace Siege.Core.Tests.SettlementShow;

/// <summary>
/// 规格：settlement-show —— Requirement: 横幅节拍（settlement-show-callouts 1.1）。
/// 位置：势力重算之后；内容：状态变为出局的玩家各一条"X 出局"，对局结果由无变有时一条"对局结束"，按此顺序；弃赛不产生横幅。
/// </summary>
/// <remarks>
/// 变异验证 M-C2「横幅排在势力之前」——<c>SettlementBeats.Generate</c> 里横幅节拍的 <c>Add</c> 挪到势力重算节拍之前 → 红 2（出局横幅、终局横幅）。
/// 还原后逐字节校验、刷新 mtime，44/44 绿。
/// </remarks>
public class 横幅节拍Tests
{
    private static readonly GameBoard Before = Stones(("D3", P2), ("E5", P2));
    private static readonly GameBoard After = Stones(("C3", P1), ("C4", P1), ("E5", P2));

    // 规格里的"玩家 C"= 金方 = PlayerId 2（FactionTable 唯一映射）。
    private static readonly PlayerId Gold = new(2);

    [Fact]
    public void 出局横幅()
    {
        // 本次结算使玩家 C（金方）出局 → 序列末尾（势力之后）含横幅节拍，内容"金方出局"。
        SettlementSide before = Side(Before, Reading(P1, 5, 2), Reading(Gold, 6, 1)) with
        {
            Statuses = [Status(P1, PlayerStatus.Active), Status(Gold, PlayerStatus.Active)],
        };
        SettlementSide after = Side(After, Reading(P1, 9, 1), Reading(Gold, 0, null)) with
        {
            Statuses = [Status(P1, PlayerStatus.Active), Status(Gold, PlayerStatus.Eliminated)],
        };

        ImmutableArray<SettlementBeat> beats = SettlementBeats.Generate(before, after, Order("C3", "C4"));
        Assert.Equal("落子[C3:Basic:P1,C4:Basic:P1]\n提子[D3:P2]\n势力[P1:5->9(+4):2->1,P2:6->0(−6):1->-]\n横幅[金方出局]", Text(beats));
        BannerBeat banner = Assert.IsType<BannerBeat>(beats[^1]);
        Assert.Equal(800, BannerBeat.PerBannerMs);
        Assert.Equal(800, banner.DurationMs);

        // 已经出局的玩家不再重复出横幅。
        ImmutableArray<SettlementBeat> again = SettlementBeats.Generate(after, after with { Power = [Reading(P1, 10, 1), Reading(Gold, 0, null)] }, null);
        Assert.DoesNotContain(again, b => b is BannerBeat);
    }

    [Fact]
    public void 终局横幅()
    {
        // 本次结算触发终局：出局横幅在前、"对局结束"在后，各 0.8 秒依次；播放中遮罩给出当前横幅，播完遮罩为空（终局面板此后才显示）。
        SettlementSide before = Side(Before, Reading(P1, 5, 2), Reading(Gold, 6, 1)) with
        {
            Statuses = [Status(P1, PlayerStatus.Active), Status(Gold, PlayerStatus.Active)],
        };
        SettlementSide after = Side(After, Reading(P1, 9, 1), Reading(Gold, 0, null)) with
        {
            Statuses = [Status(P1, PlayerStatus.Active), Status(Gold, PlayerStatus.Eliminated)],
            HasResult = true,
        };

        ImmutableArray<SettlementBeat> beats = SettlementBeats.Generate(before, after, Order("C3", "C4"));
        BannerBeat banner = Assert.IsType<BannerBeat>(beats[^1]);
        Assert.Equal(["金方出局", "对局结束"], banner.Banners);
        Assert.Equal(1600, banner.DurationMs);

        var timeline = new ShowTimeline([beats[^2], banner], ShowDuration.Normal);
        Assert.Null(timeline.Mask().Banner);   // 势力节拍进行中还没有横幅
        timeline.Advance(900 + 400);
        Assert.Equal("飘字[] 合计[] 闪光[] 横幅[金方出局:0/2:500]", CalloutText(timeline.Mask()));
        timeline.Advance(800);
        Assert.Equal("横幅[对局结束:1/2:500]", CalloutText(timeline.Mask())[^16..]);
        timeline.Advance(400);
        Assert.True(timeline.IsFinished);
        Assert.True(timeline.Mask().IsEmpty);

        // 只触发终局、无人出局：只有"对局结束"一条。
        ImmutableArray<SettlementBeat> endOnly = SettlementBeats.Generate(
            before, after with { Statuses = before.Statuses, Power = [Reading(P1, 9, 1), Reading(Gold, 4, 2)] }, Order("C3", "C4"));
        Assert.Equal(["对局结束"], Assert.IsType<BannerBeat>(endOnly[^1]).Banners);
    }

    [Fact]
    public void 弃赛不产生横幅()
    {
        // 玩家 C 由参赛中变为已弃赛（不是结算效果）：没有横幅节拍；对局结果一直为空同样没有。
        SettlementSide before = Side(Before, Reading(P1, 5, 2), Reading(Gold, 6, 1)) with
        {
            Statuses = [Status(P1, PlayerStatus.Active), Status(Gold, PlayerStatus.Active)],
        };
        SettlementSide after = Side(After, Reading(P1, 9, 1), Reading(Gold, 4, null)) with
        {
            Statuses = [Status(P1, PlayerStatus.Active), Status(Gold, PlayerStatus.Resigned)],
        };

        ImmutableArray<SettlementBeat> beats = SettlementBeats.Generate(before, after, Order("C3", "C4"));
        Assert.DoesNotContain(beats, b => b is BannerBeat);
        Assert.IsType<PowerBeat>(beats[^1]);
    }

    [Fact]
    public void 无人值守仍零时长()
    {
        // 自动演示（零时长）：含出局与终局的序列照常生成，但时间线创建即播完——不出现任何横幅或飘字，遮罩为空，不占帧。
        SettlementSide before = Side(Before, Reading(P1, 5, 2), Reading(Gold, 6, 1)) with
        {
            Statuses = [Status(P1, PlayerStatus.Active), Status(Gold, PlayerStatus.Active)],
        };
        SettlementSide after = Side(After, Reading(P1, 9, 1), Reading(Gold, 0, null)) with
        {
            Statuses = [Status(P1, PlayerStatus.Active), Status(Gold, PlayerStatus.Eliminated)],
            HasResult = true,
        };

        ImmutableArray<SettlementBeat> beats = SettlementBeats.Generate(before, after, Order("C3", "C4"));
        Assert.Contains(beats, b => b is BannerBeat);
        var timeline = new ShowTimeline(beats, ShowDuration.Zero);
        Assert.True(timeline.IsFinished);
        Assert.Equal(0, timeline.TotalDurationMs);
        ShowMask mask = timeline.Mask();
        Assert.True(mask.IsEmpty);
        Assert.Null(mask.Banner);
        Assert.Empty(mask.Callouts);
        Assert.Empty(timeline.Advance(16));
        Assert.True(timeline.Mask().IsEmpty);
    }
}
