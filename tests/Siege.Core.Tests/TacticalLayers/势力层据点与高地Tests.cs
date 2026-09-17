using System.Reflection;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;
using Siege.Presentation.Layers;
using Siege.Presentation.Preview;
using Siege.Presentation.Text;
using Siege.Presentation.Visibility;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.TacticalLayers;

/// <summary>规格：tactical-layers —— Requirement: 五种战术信息层（势力层据点项与高地拆分，scoring-sites 4.1）</summary>
public class 势力层据点与高地Tests
{
    [Fact]
    public void 势力层显示据点控制()
    {
        // 规格 Scenario：某石碑正被玩家 A 与玩家 B 同时覆盖 → 势力层显示档位、分值 45 与"争议"，并标出 A、B 两个覆盖方；
        // 势力层中任何空格都不显示领地贡献（结构守门见下一条）。
        // 变异验证 M-B4（段 B）：SiteViews.Build 的覆盖方恒为空 → 本测试红。
        MatchFlow match = SiteFixtures.Started(null, ("E5", SiteTier.Stele)).AtRound(5).Stones(P0, "E4").Stones(P1, "E6");

        var layer = (PowerLayerContent)match.World(P2).Layer(TacticalLayer.Power);

        SiteView stele = Assert.Single(layer.Sites);
        Assert.Equal((SiteTier.Stele, "石碑", 45, SiteControlKind.Contested, (PlayerId?)null), (stele.Tier, stele.TierText, stele.Value, stele.Kind, stele.Controller));
        Assert.Equal([P0, P1], stele.Coverers);
        Assert.Equal($"争议：{Labels.Player(P0)}、{Labels.Player(P1)} 同时覆盖", stele.StatusText);
    }

    [Fact]
    public void 势力层视图模型不含领地贡献字段()
    {
        // 守门（tasks 4.1）：势力层及其行视图结构上没有"空格领地贡献"——没有 Territory / Exclusive 命名的成员，也不引用盘面层归属读法的类型；
        // 旧的领地贡献视图类型整个不存在。盘面层归属读法（TerritoryLayerContent / TerritoryCellView）是合法保留，不在扫描范围内。
        // 变异验证 M-B5（段 B）：PowerLayerContent 加回 `ImmutableArray<Coord> ExclusiveCells` 参数 → 本测试红。
        Type[] powerTypes = [typeof(PowerLayerContent), typeof(GroupScoreView), typeof(PlayerPowerRowView), typeof(GroupPowerView), typeof(SiteView)];
        Type[] ownershipTypes = [typeof(TerritoryLayerContent), typeof(TerritoryCellView), typeof(TerritoryState)];
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        string[] violations =
        [
            .. powerTypes.SelectMany(t => t.GetProperties(all).Select(p => (t, p.Name, p.PropertyType))
                    .Concat(t.GetConstructors(all).SelectMany(c => c.GetParameters().Select(p => (t, p.Name!, p.ParameterType)))))
                .Where(m => m.Item2.Contains("Territory", StringComparison.OrdinalIgnoreCase)
                    || m.Item2.Contains("Exclusive", StringComparison.OrdinalIgnoreCase)
                    || Mentions(m.Item3, ownershipTypes))
                .Select(m => $"{m.t.Name}.{m.Item2}: {m.Item3.Name}")
                .Distinct()
                .Order(StringComparer.Ordinal),
        ];

        Assert.Empty(violations);
        Assert.DoesNotContain(PresentationAssembly.GetTypes(), t => t.Name == "TerritoryContributionView");

        // 反面：扫描确实读到了成员（据点项与棋串分数字段都在）
        string[] seen = [.. typeof(PowerLayerContent).GetProperties(all).Select(p => p.Name)];
        Assert.Contains("Sites", seen);
        Assert.Contains("Groups", seen);

        static bool Mentions(Type type, Type[] targets) =>
            targets.Contains(type) || (type.IsGenericType && type.GetGenericArguments().Any(a => Mentions(a, targets))) || (type.IsArray && Mentions(type.GetElementType()!, targets));
    }

    [Fact]
    public void 势力层显示高地加值()
    {
        // 规格 Scenario：某棋串的位置加值中有 2 点来自高地压制 → 该棋串的分数拆分中可读出高地 2 点。
        // D5 / E5 为 h=2，P0 占两格、各自覆盖正下方 h=0 的 P1 敌子（D4 / E4）→ 每枚 +1，高地 2；无连珠 / 协同。
        // 变异验证 M-B6（段 B）：GroupPowerView.From 的公式文案去掉高地一项 → 本测试红。
        MapData map = MatchFixtures.Map() with { TerrainData = TestMaps.Terrain(heights: [("D5", 2), ("E5", 2)]) };
        MatchFlow match = MatchFlow.CreateUnvalidated(map, MatchFixtures.Seed, MatchFixtures.All, MatchFixtures.Relics(map), MatchFixtures.DominanceOff);
        foreach (PlayerId p in MatchFixtures.All)
        {
            match.Debug.SeedHand(p, (PieceType.Basic, 50));
        }

        match.PlantSequentially(MatchFixtures.All.Select((p, i) => (p, i)));
        match.AtRound(5).Stones(P0, "D5", "E5").Stones(P1, "D4", "E4");

        var layer = (PowerLayerContent)match.World(P3).Layer(TacticalLayer.Power);
        GroupScoreView group = Assert.Single(layer.Groups, g => g.Owner == P0);
        GroupPower truth = match.Scoreboard.Latest!.Of(P0).Groups.Single();

        Assert.Equal((0, 0, 2, 2), (group.Power.LineBonus, group.Power.SynergyBonus, group.Power.HighGroundBonus, group.Power.PositionBonus));
        Assert.Equal((truth.LineBonus, truth.SynergyBonus, truth.HighGroundBonus, truth.Power), (group.Power.LineBonus, group.Power.SynergyBonus, group.Power.HighGroundBonus, group.Power.Power));
        Assert.Equal($"基础 2 × {truth.Multiplier} + 位置加值 2（连珠 0 / 协同 0 / 高地 2） = {truth.Power}", group.Power.FormulaText);
        Assert.Equal(group.Power.PositionBonus, group.Power.LineBonus + group.Power.SynergyBonus + group.Power.HighGroundBonus);
    }
}
