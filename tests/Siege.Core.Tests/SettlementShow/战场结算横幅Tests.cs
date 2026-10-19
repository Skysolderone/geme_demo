using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Presentation.Show;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.SettlementShow;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/settlement-show —— Requirement: 战场结算横幅（tasks 4.5）。
/// 节拍 / 提示条由 <see cref="LinkShowBeats.Settlements"/> 从结算前后全局公开的结算记录纯函数生成。
/// </summary>
/// <remarks>变异验证（段 D-c 实跑）：M-F6 <c>Settlements</c> 在对局终局时也出战场横幅 → 「决赛不重复」红 1；M-F7 他场结算也出横幅（不出提示条）→ 「他场结算提示」「决赛不重复」红 2。</remarks>
public class 战场结算横幅Tests
{
    /// <summary>种子 9、开局 [0,0,3,4]：第 1 大回合 P0、P1 都 Pass → 棋盘 1 结算，P0 胜（种子兜底顺序）、P1 落败第 2 名。</summary>
    private static (ImmutableArray<BattlefieldSettlementRecord> Before, ImmutableArray<BattlefieldSettlementRecord> After, MatchFlow Match) FirstSettlement()
    {
        MatchFlow match = Started([0, 0, 3, 4], seed: new GameSeed(9));
        ImmutableArray<BattlefieldSettlementRecord> before = [.. match.Settlements];
        match.PassRound();
        BattlefieldSettlementRecord settled = Assert.Single(match.Settlements);
        Assert.Equal((0, P0), (settled.Battlefield, settled.Winner));
        return (before, [.. match.Settlements], match);
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 落败横幅()
    {
        // Scenario「落败横幅」：本机 P1 所在的棋盘 1 结算、势力次高 → "战场落败（第 2 名）"（随后由出局视图接手）。
        (var before, var after, _) = FirstSettlement();

        (ImmutableArray<SettlementBeat> beats, ImmutableArray<string> notices) = LinkShowBeats.Settlements(before, after, P1, matchEnded: false);

        var banner = Assert.IsType<BannerBeat>(Assert.Single(beats));
        Assert.Equal(["战场落败（第 2 名）"], banner.Banners);
        Assert.Empty(notices);

        // 胜者看到的是"战场胜出"。
        (beats, notices) = LinkShowBeats.Settlements(before, after, P0, matchEnded: false);
        Assert.Equal([LinkShowBeats.BattlefieldWonText], Assert.IsType<BannerBeat>(Assert.Single(beats)).Banners);
        Assert.Empty(notices);
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 他场结算提示()
    {
        // Scenario「他场结算提示」：P2 在棋盘 4 → 提示条"战场 出生区 1 已结算，胜者 红方(P0)"，没有节拍（不打断操作）。
        (var before, var after, _) = FirstSettlement();

        (ImmutableArray<SettlementBeat> beats, ImmutableArray<string> notices) = LinkShowBeats.Settlements(before, after, P2, matchEnded: false);

        Assert.Empty(beats);
        Assert.Equal(["战场 出生区 1 已结算，胜者 红方(P0)"], notices);
        Assert.Equal(LinkShowBeats.SettledElsewhereText(0, P0), notices[0]);
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 决赛不重复()
    {
        // Scenario「决赛不重复」：决赛战场结算即终局 → 不出战场横幅、不出提示条，只有既有的终局横幅（由 SettlementBeats.Banners 的"对局结束"给出）。
        MatchFlow match = Started([0, 0, 0, 0]);
        ImmutableArray<BattlefieldSettlementRecord> before = [.. match.Settlements];
        match.PassRound();
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Single(match.Settlements);

        foreach (PlayerId me in All)
        {
            (ImmutableArray<SettlementBeat> beats, ImmutableArray<string> notices) = LinkShowBeats.Settlements(before, [.. match.Settlements], me, matchEnded: true);
            Assert.Empty(beats);
            Assert.Empty(notices);
        }

        // 反面：同一记录在对局未终局时会出横幅。
        Assert.NotEmpty(LinkShowBeats.Settlements(before, [.. match.Settlements], P0, matchEnded: false).Beats);

        // 本机早已出局（不在决赛战场的在局名单里）时，终局的决赛结算同样不出提示条——终局面板已列出全部名次。
        var final = new BattlefieldSettlementRecord(1, 20, 0, SettlementReason.AllPassed, P2,
            [new SettlementEntry(P2, 1, 100, 0, 0, 10), new SettlementEntry(P3, 2, 50, 0, 0, 8)], 3);
        Assert.Empty(LinkShowBeats.Settlements([], [final], P0, matchEnded: true).Notices);
        Assert.Equal([LinkShowBeats.SettledElsewhereText(0, P2)], LinkShowBeats.Settlements([], [final], P0, matchEnded: false).Notices);
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 落败横幅排在出局横幅之前_展开节拍在最后()
    {
        // 规格「落败横幅」：先"战场落败（第 N 名）"再出局视图——既有结算节拍末尾的出局 / 终局横幅与战场横幅并成一条，战场横幅在前；展开节拍接在最后。
        (var before, var after, _) = FirstSettlement();
        (ImmutableArray<SettlementBeat> settled, _) = LinkShowBeats.Settlements(before, after, P1, matchEnded: false);
        ImmutableArray<SettlementBeat> existing = [new PowerBeat([]), new BannerBeat([BannerBeat.EliminatedText(P1)])];
        ImmutableArray<SettlementBeat> expansion = [new BannerBeat(["棋盘展开：与 X 交战"])];

        ImmutableArray<SettlementBeat> spliced = LinkShowBeats.Splice(existing, settled, expansion);

        Assert.Equal(3, spliced.Length);
        Assert.IsType<PowerBeat>(spliced[0]);
        Assert.Equal(["战场落败（第 2 名）", BannerBeat.EliminatedText(P1)], Assert.IsType<BannerBeat>(spliced[1]).Banners);
        Assert.Same(expansion[0], spliced[2]);

        // 既有序列没有横幅时战场横幅自成一条接在末尾；没有战场结算时原样拼接。
        Assert.Equal(["战场落败（第 2 名）"], Assert.IsType<BannerBeat>(LinkShowBeats.Splice([new PowerBeat([])], settled, []).Last()).Banners);
        Assert.Equal(existing.Length + expansion.Length, LinkShowBeats.Splice(existing, [], expansion).Length);
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 没有新结算就没有节拍与提示()
    {
        (var before, var after, _) = FirstSettlement();
        (ImmutableArray<SettlementBeat> beats, ImmutableArray<string> notices) = LinkShowBeats.Settlements(after, after, P1, matchEnded: false);
        Assert.Empty(beats);
        Assert.Empty(notices);
        Assert.Empty(LinkShowBeats.Settlements(before, before, P1, matchEnded: false).Beats);
    }
}
