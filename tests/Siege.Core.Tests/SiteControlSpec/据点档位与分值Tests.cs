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

    [Fact]
    public void 分值取自对局配置()
    {
        // 规格 Scenario：以据点分值 10 / 30 / 90 开局，A 控制 1 个篝火 → A 从据点获得 30 分。
        // 端到端走 MatchFlow：对局配置 → 结算第 5 步势力榜 → 公开视图；另钉预演（BatchPreview，AI 与界面用它）与实际结算一致，防止预演路径拿默认分值。
        // 变异验证 M-S4（段 A2）：MatchFlow.OnRecalculatePower 改传 SiteValues.Standard → 红，含本测试（15 ≠ 30）；
        //           M-S4b：BatchPreviewBuilder 的 after 改用 SiteValues.Standard → 红，含本测试（预演 16 ≠ 实际 31）。
        MatchOptions options = MatchFixtures.DominanceOff with { SiteValues = new SiteValues(10, 30, 90) };
        MatchFlow match = SiteFixtures.Started(options, ("E5", SiteTier.Campfire)).AtRound(5, MatchFixtures.All);
        Assert.Equal(new SiteValues(10, 30, 90), match.Publish().SiteValues);

        match.BeginTurn();
        match.EnterRecruit();
        StagedBatch batch = match.EnterDeploy();
        Assert.Null(batch.Stage(Coord.Parse("E4"), PieceType.Basic));
        Core.Preview.BatchPreview preview = match.PreviewCurrentBatch();
        long predicted = preview.PowerChanges.Single(c => c.Player == P0).After;
        Assert.True(match.Confirm().Confirmed);

        PlayerPower p0 = match.Scoreboard.Latest!.Of(P0);
        Assert.Equal(new SiteHolding(Coord.Parse("E5"), SiteTier.Campfire, 30, SiteControlKind.UniqueCoverage), Assert.Single(p0.Sites));
        Assert.Equal(30, p0.SiteScore);
        Assert.Equal(31, p0.Total);
        Assert.Equal(p0.Total, predicted);
    }

    [Fact]
    public void AI评价使用对局据点分值()
    {
        // 「分值取自对局配置」的 AI 路径：AI 的势力增量维度 MUST 用对局配置的据点分值（经公开视图 SiteValues），不得用标准局值。
        // 10 / 30 / 90 下 P0 在 E4 落 1 枚普通子即唯一覆盖篝火 E5 → 势力增量 = 军势 1 + 篝火 30 = 31（标准局值会是 16）。
        // 变异验证 N-2（段 A2 检查）：BatchEvaluator 构造里 `_siteValues = view.SiteValues` 改为 `SiteValues.Standard` → 补本测试前全绿 797（缺口），补后红 1（本测试）。
        MatchOptions options = MatchFixtures.DominanceOff with { SiteValues = new SiteValues(10, 30, 90) };
        MatchFlow match = SiteFixtures.Started(options, ("E5", SiteTier.Campfire)).AtRound(5, MatchFixtures.All);

        HeuristicTurnController ai = HeuristicAi.Create(match, P0);
        StagedBatch batch = match.OpenDeploy();
        RehearsalResult result = match.RehearseBatch(batch, ("E4", PieceType.Basic));
        EvaluationBreakdown e = ai.CreateEvaluator().Evaluate(batch.Placements, result, batch.Context);

        Assert.Equal(1 + 30, e.RawOf(EvaluationDimension.PowerGain));
    }

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
