using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Presentation.Show;
using Siege.Presentation.Visibility;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.SettlementShow;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/settlement-show —— Requirement: 棋盘展开节拍（tasks 4.5，裁决 D5）。
/// 节拍由 <see cref="LinkShowBeats.Expansion"/> 从本机结算前后两份按座位过滤的默认棋盘纯函数生成；时间线与遮罩沿用 <see cref="ShowTimeline"/>。
/// </summary>
/// <remarks>
/// 变异验证（段 D-c 实跑）：M-F4 <c>Expansion</c> 不区分对方出生棋盘与公共棋盘（全部进升起节拍）→ 「首轮链接演出_四个节拍依次」「遮罩_升起前藏在地面之下_对方格先隐藏后渐显_相机目标带进度」「无人值守零时长」红 3；
/// M-F5 <c>Expansion</c> 对"本机战场没有新增格"也出横幅 → 「他人链接不演出」红 1。
/// </remarks>
public class 棋盘展开节拍Tests
{
    /// <summary>四人四区、脚本配对 (1,2)(4,5)：第 3 大回合结束链接。返回链接前后 P0 的默认棋盘与新同战场玩家。</summary>
    private static (DefaultBoardView Before, DefaultBoardView After, ImmutableArray<PlayerId> Opponents, MatchFlow Match) LinkedForP0()
    {
        MatchFlow match = Started([0, 1, 3, 4], new ScriptedPairingSource()).PassRounds(2);
        DefaultBoardView before = match.World(P0).Board();
        ImmutableArray<PlayerId> matesBefore = match.World(P0).SameBattlefieldPlayers;
        match.PassRound();
        Assert.Equal(2, match.Links.Count);
        DefaultBoardView after = match.World(P0).Board();
        ImmutableArray<PlayerId> opponents = [.. match.World(P0).SameBattlefieldPlayers.Where(p => !matesBefore.Contains(p))];
        return (before, after, opponents, match);
    }

    [Fact]
    public void 首轮链接演出_四个节拍依次()
    {
        // Scenario「首轮链接演出」：公共棋盘与通道升起 → 相机拉远到合并后战场 → 对方盘面显现 → 横幅"棋盘展开：与 X 交战"；四拍合计约 2.5 s。
        (DefaultBoardView before, DefaultBoardView after, ImmutableArray<PlayerId> opponents, _) = LinkedForP0();
        Assert.Equal([P1], opponents);

        ImmutableArray<SettlementBeat> beats = LinkShowBeats.Expansion(before, after, opponents);

        Assert.Equal(4, beats.Length);
        var rise = Assert.IsType<BoardRiseBeat>(beats[0]);
        var pull = Assert.IsType<CameraPullBeat>(beats[1]);
        var reveal = Assert.IsType<OpponentRevealBeat>(beats[2]);
        var banner = Assert.IsType<BannerBeat>(beats[3]);

        HashSet<Coord> had = [.. before.Cells.Select(c => c.Coord)];
        HashSet<Coord> opponentBoard = [.. Map.BirthZones[1]];
        Assert.NotEmpty(rise.Cells);
        Assert.All(rise.Cells, c => Assert.False(had.Contains(c) || opponentBoard.Contains(c)));   // 升起的是新长出的公共棋盘与通道
        Assert.Equal(opponentBoard.Order(), reveal.Cells.Order());                                  // 显现的是对方那块出生棋盘
        Assert.Equal(rise.Cells.Length + reveal.Cells.Length + had.Count, after.Cells.Length);     // 新增格一个不漏
        CoordRect expected = CoordRect.Of(after.Cells.Select(c => c.Coord))!.Value;
        Assert.Equal(expected, pull.Target);                                                       // 相机目标 = 合并后战场包围盒
        Assert.Equal(["棋盘展开：与 蓝方(P1) 交战"], banner.Banners);
        Assert.Equal(LinkShowBeats.ExpansionText(opponents), banner.Banners[0]);

        int total = beats.Sum(b => b.DurationMs);
        Assert.Equal(LinkShowBeats.ExpansionTotalMs, total);
        Assert.InRange(total, 2300, 2800);   // 裁决 D5：约 2.5 s
        Assert.Equal(total, new ShowTimeline(beats, ShowDuration.Normal).TotalDurationMs);
    }

    [Fact]
    public void 他人链接不演出()
    {
        // Scenario「他人链接不演出」：P0、P1 同在棋盘 1（不进首轮配对），P2、P3 的棋盘 4 + 5 链接 → P0 的战场没有新增格，空序列。
        MatchFlow match = Started([0, 0, 3, 4], new ScriptedPairingSource()).PassRounds(2);
        DefaultBoardView before = match.World(P0).Board();
        match.PassRound();
        Assert.Single(match.Links);
        DefaultBoardView after = match.World(P0).Board();

        Assert.Empty(LinkShowBeats.Expansion(before, after, []));
        Assert.Equal(before.Cells.Length, after.Cells.Length);
        // 反面：P2 的战场确实长出了格。
        Assert.True(match.World(P2).Board().Cells.Length > Map.BirthZones[3].Count);
    }

    [Fact]
    public void 遮罩_升起前藏在地面之下_对方格先隐藏后渐显_相机目标带进度()
    {
        (DefaultBoardView before, DefaultBoardView after, ImmutableArray<PlayerId> opponents, _) = LinkedForP0();
        ImmutableArray<SettlementBeat> beats = LinkShowBeats.Expansion(before, after, opponents);
        var rise = (BoardRiseBeat)beats[0];
        var pull = (CameraPullBeat)beats[1];
        var reveal = (OpponentRevealBeat)beats[2];
        var timeline = new ShowTimeline(beats, ShowDuration.Normal);

        // 0 ms：升起格进度 0（仍在地面之下）、对方格全部隐藏、相机未动。
        ShowMask mask = timeline.Mask();
        Assert.All(rise.Cells, c => Assert.Equal(0, mask.Rising[c]));
        Assert.All(reveal.Cells, c => Assert.Contains(c, mask.Hidden));
        Assert.Null(mask.CameraPull);
        Assert.False(mask.IsEmpty);

        // 升起过半。
        timeline.Advance(BoardRiseBeat.Ms / 2);
        mask = timeline.Mask();
        Assert.All(rise.Cells, c => Assert.InRange(mask.Rising[c], 400, 600));
        Assert.All(reveal.Cells, c => Assert.Contains(c, mask.Hidden));

        // 进入相机节拍：升起已到位（不再列入）、相机目标带进度、对方格仍隐藏。
        timeline.Advance(BoardRiseBeat.Ms / 2 + CameraPullBeat.Ms / 2);
        mask = timeline.Mask();
        Assert.Empty(mask.Rising);
        Assert.NotNull(mask.CameraPull);
        Assert.Equal(pull.Target, mask.CameraPull!.Value.Target);
        Assert.InRange(mask.CameraPull.Value.Permille, 400, 600);
        Assert.All(reveal.Cells, c => Assert.Contains(c, mask.Hidden));

        // 显现过半：对方格按进度出现。
        timeline.Advance(CameraPullBeat.Ms / 2 + OpponentRevealBeat.Ms / 2);
        mask = timeline.Mask();
        Assert.Null(mask.CameraPull);
        Assert.All(reveal.Cells, c => Assert.InRange(mask.Appearing[c], 400, 600));
        Assert.DoesNotContain(reveal.Cells[0], mask.Hidden);

        // 横幅。
        timeline.Advance(OpponentRevealBeat.Ms / 2 + BannerBeat.PerBannerMs / 2);
        mask = timeline.Mask();
        Assert.Equal(LinkShowBeats.ExpansionText(opponents), mask.Banner!.Text);
        Assert.Empty(mask.Hidden);
        Assert.Empty(mask.Appearing);

        timeline.Advance(BannerBeat.PerBannerMs);
        Assert.True(timeline.IsFinished);
        Assert.True(timeline.Mask().IsEmpty);
    }

    [Fact]
    public void 无人值守零时长()
    {
        // Scenario「无人值守零时长」：节拍照常生成，时间线零时长创建即播完、遮罩为空。
        (DefaultBoardView before, DefaultBoardView after, ImmutableArray<PlayerId> opponents, _) = LinkedForP0();
        ImmutableArray<SettlementBeat> beats = LinkShowBeats.Expansion(before, after, opponents);
        Assert.Equal(4, beats.Length);
        var timeline = new ShowTimeline(beats, ShowDuration.Zero);
        Assert.True(timeline.IsFinished);
        Assert.Equal(0, timeline.TotalDurationMs);
        Assert.True(timeline.Mask().IsEmpty);
    }

    [Fact]
    public void 提速照旧()
    {
        (DefaultBoardView before, DefaultBoardView after, ImmutableArray<PlayerId> opponents, _) = LinkedForP0();
        var timeline = new ShowTimeline(LinkShowBeats.Expansion(before, after, opponents), ShowDuration.Normal);
        timeline.Advance(LinkShowBeats.ExpansionTotalMs / ShowTimeline.SpeedUpFactor, fast: true);
        Assert.True(timeline.IsFinished);
    }
}
