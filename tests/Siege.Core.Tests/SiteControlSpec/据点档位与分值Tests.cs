using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Relics;
using Siege.Core.Scoring;
using Siege.Sim.Config;

namespace Siege.Core.Tests.SiteControlSpec;

/// <summary>规格：site-control —— Requirement: 据点档位与分值（scoring-sites 2.2）</summary>
public class 据点档位与分值Tests
{
    private static readonly PlayerId P0 = MatchFixtures.P0;

    // 段 A（restore-go-core-rules）删除「分值取自对局配置」「AI评价使用对局据点分值」：两条都断言"据点分进入总势力 / AI 势力增量"，
    // 据点分自本段起不计入总势力（过渡，见 总势力Tests.据点分不计入总势力）；据点类型与分值配置到段 B 整体删除。

    [Theory]
    [InlineData(5, 0, 45, "篝火")]
    [InlineData(20, 15, 45, "营帐")]
    [InlineData(0, 15, 45, "营帐")]
    [InlineData(5, 50, 45, "篝火")]
    [InlineData(5, 15, -1, "石碑")]
    public void 分值配置非法被拒(int tent, int campfire, int stele, string tier)
    {
        // 规格 Scenario：以 5 / 0 / 45 或 20 / 15 / 45 开局 → 对局配置校验失败并指出违规的档位（另补 0 营帐、篝火高于石碑、负石碑）。
        // 对局建局与跑局配置都走唯一的 SiteValues.Validated。
        // 变异验证 M-S9（段 A2）：Validated 去掉"营帐 ≤ 篝火"一条 → 红 1（20 / 15 / 45 这一行）。
        var values = new SiteValues(tent, campfire, stele);

        ArgumentException direct = Assert.ThrowsAny<ArgumentException>(() => values.Validated());
        Assert.Contains(tier, direct.Message, StringComparison.Ordinal);

        MatchOptions options = MatchOptions.Immediate with { SiteValues = values };
        ArgumentException viaMatch = Assert.ThrowsAny<ArgumentException>(() => MatchFixtures.Create(options: options));
        Assert.Contains(tier, viaMatch.Message, StringComparison.Ordinal);

        ArgumentException viaRun = Assert.ThrowsAny<ArgumentException>(() => (new RunConfig() with { SiteValues = values }).Validated());
        Assert.Contains(tier, viaRun.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 标准局分值初值()
    {
        // 规格：标准局初值 5 / 15 / 45（待扫档校准）；对局配置与跑局配置默认都取它。
        Assert.Equal((5, 15, 45), (SiteValues.Standard.Tent, SiteValues.Standard.Campfire, SiteValues.Standard.Stele));
        Assert.Equal(SiteValues.Standard, MatchOptions.Default.SiteValues);
        Assert.Equal(SiteValues.Standard, MatchOptions.Immediate.SiteValues);
        Assert.Equal(SiteValues.Standard, new RunConfig().SiteValues);
        Assert.Equal((5, 15, 45), (SiteValues.Standard.Of(SiteTier.Tent), SiteValues.Standard.Of(SiteTier.Campfire), SiteValues.Standard.Of(SiteTier.Stele)));
    }

    [Fact]
    public void 据点不带额外效果()
    {
        // 规格 Scenario：A 控制全部 4 个石碑 → 征募展示数、免费选取数、部署上限、类型槽、先手修正与未控制时完全相同。
        // 对照组：同一盘面、同一种子，地图不带据点。
        (string Cell, SiteTier Tier)[] steles = [("C4", SiteTier.Stele), ("G4", SiteTier.Stele), ("C6", SiteTier.Stele), ("G6", SiteTier.Stele)];
        MatchFlow with4 = SiteFixtures.Started(null, steles).AtRound(5, MatchFixtures.All).Stones(P0, "D4", "F4", "D6", "F6");
        MatchFlow without = SiteFixtures.Started(null).AtRound(5, MatchFixtures.All).Stones(P0, "D4", "F4", "D6", "F6");
        Assert.Equal(180, with4.Scoreboard.Latest!.Of(P0).SiteScore);
        Assert.Equal(0, without.Scoreboard.Latest!.Of(P0).SiteScore);

        with4.BeginTurn();
        without.BeginTurn();
        EffectSnapshot a = with4.CurrentSnapshot!;
        EffectSnapshot b = without.CurrentSnapshot!;
        Assert.Equal(
            (b.RevealCount, b.FreePickCount, b.DeployLimit, b.TypeSlots),
            (a.RevealCount, a.FreePickCount, a.DeployLimit, a.TypeSlots));
        Assert.Equal(
            without.Relics.ReadInitiativeBonuses(without.Board, without.Roster),
            with4.Relics.ReadInitiativeBonuses(with4.Board, with4.Roster));
    }
}
