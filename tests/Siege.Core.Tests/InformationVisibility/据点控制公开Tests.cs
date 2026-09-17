using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;
using Siege.Presentation.Layers;
using Siege.Presentation.Text;
using Siege.Presentation.Visibility;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.InformationVisibility;

/// <summary>规格：information-visibility —— Requirement: 始终公开的信息（据点控制公开，scoring-sites 4.1）</summary>
public class 据点控制公开Tests
{
    [Fact]
    public void 据点控制公开()
    {
        // 规格 Scenario：任一玩家查看任一据点 → 可见其档位、分值与控制状态（占据 / 唯一覆盖 / 争议 / 无人及控制者）。
        // 9×9 合成图四个据点：C5 营帐被 P0 占据、E5 石碑只被 P0 覆盖、G5 篝火被 P1 与 P2 同时覆盖、E7 营帐无人。
        // 默认棋盘（地标）与势力层（据点项）两处、四名观察者看到的逐项相同，且与 Core 快照一致。
        // 变异验证 M-B2（段 B）：SiteViews.Build 的控制者恒写 null → 本测试红。
        MatchFlow match = SiteFixtures.Started(null, ("C5", SiteTier.Tent), ("E5", SiteTier.Stele), ("G5", SiteTier.Campfire), ("E7", SiteTier.Tent))
            .AtRound(5)
            .Stones(P0, "C5", "E4")
            .Stones(P1, "G4")
            .Stones(P2, "G6");

        string[] expected =
        [
            $"C5 Tent 5 Occupied P0 [] 由 {Labels.Player(P0)} 占据",
            $"E5 Stele 45 UniqueCoverage P0 [] 由 {Labels.Player(P0)} 唯一覆盖",
            "E7 Tent 5 Unclaimed  [] 无人",
            $"G5 Campfire 15 Contested  [P1,P2] 争议：{Labels.Player(P1)}、{Labels.Player(P2)} 同时覆盖",
        ];

        foreach (PlayerId viewer in MatchFixtures.All)
        {
            ViewerWorld world = match.World(viewer);
            foreach (var sites in new[] { world.Board().Sites, ((PowerLayerContent)world.Layer(TacticalLayer.Power)).Sites })
            {
                Assert.Equal(expected, sites.Select(s => $"{s.Coord.ToNotation()} {s.Tier} {s.Value} {s.Kind} {s.Controller} [{string.Join(",", s.Coverers)}] {s.StatusText}").Order(StringComparer.Ordinal));
                Assert.Equal(
                    match.Scoreboard.Latest!.SiteStates.Select(s => (s.Coord, s.Kind, s.Controller)),
                    sites.Select(s => (s.Coord, s.Kind, s.Controller)));
            }
        }
    }

    [Fact]
    public void 据点分值取自对局配置()
    {
        // site-control「据点档位与分值」：10 / 30 / 90 下，据点视图的分值跟随对局配置，而不是写死标准局。
        // 变异验证 M-B3（段 B）：SiteViews 分值改用 SiteValues.Standard → 本测试红。
        MatchOptions options = MatchFixtures.DominanceOff with { SiteValues = new SiteValues(10, 30, 90) };
        MatchFlow match = SiteFixtures.Started(options, ("C5", SiteTier.Tent), ("E5", SiteTier.Stele), ("G5", SiteTier.Campfire));

        foreach (PlayerId viewer in MatchFixtures.All)
        {
            ViewerWorld world = match.World(viewer);
            Assert.Equal(["C5=10", "E5=90", "G5=30"], world.Board().Sites.Select(s => $"{s.Coord.ToNotation()}={s.Value}").Order(StringComparer.Ordinal));
            Assert.Equal(["C5=10", "E5=90", "G5=30"], ((PowerLayerContent)world.Layer(TacticalLayer.Power)).Sites.Select(s => $"{s.Coord.ToNotation()}={s.Value}").Order(StringComparer.Ordinal));
        }
    }
}
