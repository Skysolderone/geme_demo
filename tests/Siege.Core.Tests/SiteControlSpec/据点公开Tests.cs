using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Core.Scoring;
using Siege.Presentation.Layers;
using Siege.Presentation.Visibility;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.SiteControlSpec;

/// <summary>规格：site-control —— Requirement: 据点公开（scoring-sites 4.1；段 A2 检查 C-3）</summary>
public class 据点公开Tests
{
    private static readonly string[] Tents = ["B3", "L2", "M11", "C12"];
    private static readonly string[] Campfires = ["J2", "M9", "E12", "B5"];
    private static readonly string[] Steles = ["H5", "E6", "J8", "F9"];

    [Fact]
    public void 开局即可见()
    {
        // 规格 Scenario：对局开始、尚无任何棋子落下 → 所有玩家都能看到 12 个据点的位置、档位与分值，控制状态均为无人。
        // v4 真图（S-13 布点）；插旗阶段（势力快照尚未生成）与插旗锁定后（快照已生成）两个时刻都查，四名观察者看到的完全相同。
        // 原 M-B1 变异对象（SiteViews 插旗阶段 null 分支）已在段 B 检查删除。段 B 检查改写：插旗阶段的据点状态由 Core（MatchFlow.Publish → SiteControl）给出，表现层不再自记"无人"；
        // 变异 M-BC1：Publish 在势力快照为 null 时 SiteStates 传空数组 → 本测试红。
        MatchFlow match = MatchFlow.Create(FourPlayerBaseMap.Create(), MatchFixtures.Seed, MatchFixtures.All, MatchOptions.Immediate);
        Assert.Equal(MatchPhase.FlagPlanting, match.Phase);
        MatchPublicView flagView = match.Publish();
        Assert.Null(flagView.Power);
        Assert.Equal(12, flagView.SiteStates.Length);
        Assert.All(flagView.SiteStates, s => Assert.Equal((SiteControlKind.Unclaimed, (PlayerId?)null), (s.Kind, s.Controller)));
        AssertAllVisibleAndUnclaimed(match);

        match.PlantSequentially(MatchFixtures.All.Select((p, i) => (p, i)));
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Equal(0, match.Board.AllCoords().Count(c => match.Board[c].Occupant is not null));

        // 公开快照本身（Core）：12 个据点全部无人
        MatchPublicView view = match.Publish();
        Assert.Equal(12, view.Power!.SiteStates.Length);
        Assert.Equal(view.Power.SiteStates, view.SiteStates);
        Assert.All(view.Power.SiteStates, s => Assert.Equal((SiteControlKind.Unclaimed, (PlayerId?)null), (s.Kind, s.Controller)));
        Assert.Equal(SiteValues.Standard, view.SiteValues);
        AssertAllVisibleAndUnclaimed(match);
    }

    private static void AssertAllVisibleAndUnclaimed(MatchFlow match)
    {
        foreach (PlayerId viewer in MatchFixtures.All)
        {
            ViewerWorld world = match.World(viewer);
            foreach (var sites in new[] { world.Board().Sites, ((PowerLayerContent)world.Layer(TacticalLayer.Power)).Sites })
            {
                Assert.Equal(12, sites.Length);
                Assert.Equal(Tents.Order(StringComparer.Ordinal), sites.Where(s => s.Tier == SiteTier.Tent).Select(s => s.Coord).Notations().Order(StringComparer.Ordinal));
                Assert.Equal(Campfires.Order(StringComparer.Ordinal), sites.Where(s => s.Tier == SiteTier.Campfire).Select(s => s.Coord).Notations().Order(StringComparer.Ordinal));
                Assert.Equal(Steles.Order(StringComparer.Ordinal), sites.Where(s => s.Tier == SiteTier.Stele).Select(s => s.Coord).Notations().Order(StringComparer.Ordinal));
                Assert.All(sites, s =>
                {
                    // 变异 M-BC2（段 B 检查）：Labels.SiteTier 营帐 / 篝火文案互换 → 本测试红（此前全绿：其余测试只钉枚举或"石碑"）。
                    Assert.Equal(s.Tier switch { SiteTier.Tent => (5, "营帐"), SiteTier.Campfire => (15, "篝火"), _ => (45, "石碑") }, (s.Value, s.TierText));
                    Assert.Equal((SiteControlKind.Unclaimed, (PlayerId?)null, 0, "无人"), (s.Kind, s.Controller, s.Coverers.Length, s.StatusText));
                });
            }
        }
    }
}
