using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.SiteControlSpec;

/// <summary>规格：site-control —— Requirement: 据点分计入势力（scoring-sites 2.4 / 2.5）</summary>
public class 据点分计入势力Tests
{
    private static readonly PlayerId P0 = MatchFixtures.P0;
    private static readonly PlayerId P1 = MatchFixtures.P1;
    private static readonly PlayerId P2 = MatchFixtures.P2;
    private static readonly PlayerId P3 = MatchFixtures.P3;

    [Fact]
    public void 失去控制立即掉分()
    {
        // 规格 Scenario：A 唯一覆盖一个石碑得 45 分，随后 B 落子使该石碑变为争议 → 该次结算后 A 的势力立即减少 45。
        MatchFlow match = SiteFixtures.Started(null, ("E5", SiteTier.Stele)).AtRound(5, [P1, P0, P2, P3]).Stones(P0, "E4");
        Assert.Equal((45L, 46L), (match.Scoreboard.Latest!.Of(P0).SiteScore, match.Scoreboard.Latest.Of(P0).Total));

        match.PlayTurn("E6");   // B 落子

        PowerSnapshot after = match.Scoreboard.Latest!;
        Assert.Equal(SiteControlKind.Contested, after.StateAt("E5").Kind);
        Assert.Equal((0L, 1L), (after.Of(P0).SiteScore, after.Of(P0).Total));
        Assert.Equal(0, after.Of(P1).SiteScore);
    }

    [Fact]
    public void 占据者被围杀()
    {
        // 规格 Scenario：占据某篝火的 A 的棋串被 B 围杀移除，且提子后该格只被 B 覆盖 → 同一次结算内该篝火改由 B 以唯一覆盖控制，A 失去 15、B 获得 15。
        // 变异验证 M-S10（段 A2）：MatchFlow.OnRecalculatePower 不重算（跳过 Scoreboard.Recalculate）→ 红，含本测试。
        MatchFlow match = SiteFixtures.Started(null, ("E5", SiteTier.Campfire)).AtRound(5, [P1, P0, P2, P3])
            .Stones(P0, "E5").Stones(P1, "D5", "F5", "E6");
        Assert.Equal(new SiteState(Coord.Parse("E5"), SiteTier.Campfire, SiteControlKind.Occupied, P0), match.Scoreboard.Latest!.StateAt("E5"));
        long p0Before = match.Scoreboard.Latest!.Of(P0).Total;
        long p1Before = match.Scoreboard.Latest!.Of(P1).Total;
        Assert.Equal((16L, 3L), (p0Before, p1Before));

        match.PlayTurn("E4");   // B 围杀 E5

        PowerSnapshot after = match.Scoreboard.Latest!;
        Assert.False(match.Board[Coord.Parse("E5")].Occupant.HasValue);
        Assert.Equal(new SiteState(Coord.Parse("E5"), SiteTier.Campfire, SiteControlKind.UniqueCoverage, P1), after.StateAt("E5"));
        Assert.Equal((0L, 15L), (after.Of(P0).SiteScore, after.Of(P1).SiteScore));
        Assert.Equal(p0Before - 16, after.Of(P0).Total);   // 失去篝火 15 与被提的 1 子
        Assert.Equal(p1Before + 1 + 15, after.Of(P1).Total);   // 新落 1 子 + 篝火 15
    }

    [Fact]
    public void 弃赛者封锁据点()
    {
        // 规格 Scenario（site-control「弃赛者封锁据点」/ elimination-endgame 弃赛 R-4）：已弃赛 D 的棋子占据某营帐 → 该营帐由 D 控制，参赛玩家均不得分；
        // D 的盘面势力含这 5 分，但 D 不占名次。
        // 变异验证 M-S11（段 A2）：PowerCalculator 只给 IsRanked 玩家计据点分 → 红，含本测试。
        MatchFlow match = SiteFixtures.Started(null, ("E5", SiteTier.Tent)).AtRound(5, MatchFixtures.All)
            .Stones(P3, "E5").Stones(P0, "E4");
        match.Resign(P3);

        PowerSnapshot snapshot = match.Scoreboard.Latest!;
        Assert.Equal(PlayerStatus.Resigned, match.StateOf(P3).Status);
        Assert.Equal(new SiteState(Coord.Parse("E5"), SiteTier.Tent, SiteControlKind.Occupied, P3), snapshot.StateAt("E5"));
        PlayerPower d = snapshot.Of(P3);
        Assert.Equal((5L, 6L), (d.SiteScore, d.Total));
        Assert.False(d.IsRanked);
        Assert.Null(snapshot.RankOf(P3));
        Assert.All([P0, P1, P2], p => Assert.Equal(0, snapshot.Of(p).SiteScore));
    }

    [Fact]
    public void 弃赛者与参赛者同时覆盖空据点为争议()
    {
        // 规格 Scenario（elimination-endgame「遗留棋子继续生效」/ coverage-territory「弃赛者遗留棋子制造争议」）：
        // 弃赛者 D 的棋子与参赛玩家 A 的棋子同时覆盖某个空的据点格 → 争议，A 不获得该据点分。走 MatchFlow 名册接线。
        MatchFlow match = SiteFixtures.Started(null, ("E5", SiteTier.Stele)).AtRound(5, MatchFixtures.All)
            .Stones(P3, "E6").Stones(P0, "E4");
        match.Resign(P3);

        PowerSnapshot snapshot = match.Scoreboard.Latest!;
        Assert.Equal(new SiteState(Coord.Parse("E5"), SiteTier.Stele, SiteControlKind.Contested, null), snapshot.StateAt("E5"));
        Assert.Equal((0L, 0L), (snapshot.Of(P0).SiteScore, snapshot.Of(P3).SiteScore));
    }
}
