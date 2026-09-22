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

/// <summary>规格：tactical-layers —— Requirement: 五种战术信息层（势力层的领地分与高地拆分）</summary>
public class 势力层领地与高地Tests
{
    [Fact]
    public void 势力层显示领地分()
    {
        // 规格 Scenario「势力层显示领地分」：玩家 A 有 N 个独占空格 → 势力层可读到 A 的领地分为 N，且总势力 = 领地分 + 全部棋串军势。
        // restore-go-core-rules 段 B：取代旧的「势力层显示据点控制」与「势力层视图模型不含领地贡献字段」两条——
        // 据点项已随据点摘除，领地分恢复计分后势力层必须给得出它。
        // 变异验证 M-B11（段 B，实跑红 3）：LayerContents.Power 把 PlayerPowerRowView 的领地分改成 0 → 本测试红。
        MatchFlow match = MatchFixtures.Started().AtRound(5).Stones(P0, "E5").Stones(P1, "J9");

        var layer = (PowerLayerContent)match.World(P2).Layer(TacticalLayer.Power);
        PowerSnapshot truth = match.Scoreboard.Latest!;

        foreach (PlayerPowerRowView row in layer.Players)
        {
            PlayerPower detail = truth.Of(row.Player);
            Assert.Equal(detail.ExclusiveCells.Length, row.TerritoryScore);
            Assert.Equal(detail.Total, row.Total);

            // 独立复算：总势力 = 领地分 + 该玩家全部棋串军势（不回调被测视图）。
            System.Numerics.BigInteger groups = System.Numerics.BigInteger.Zero;
            foreach (GroupScoreView g in layer.Groups.Where(g => g.Owner == row.Player))
            {
                groups += g.Power.Power;
            }

            Assert.Equal(row.TerritoryScore + groups, row.Total);
        }

        Assert.Equal(4, layer.Players.Length);
        Assert.Contains(layer.Players, r => r.TerritoryScore > 0);
    }

    [Fact]
    public void 势力层显示高地加值()
    {
        // 规格 Scenario：某棋串的位置加值中有 2 点来自高地压制 → 该棋串的分数拆分中可读出高地 2 点。
        // D5 / E5 为 h=2，P0 占两格、各自覆盖正下方 h=0 的 P1 敌子（D4 / E4）→ 每枚 +1，高地 2；无连珠 / 协同。
        // 变异验证 M-B6（段 B）：GroupPowerView.From 的公式文案去掉高地一项 → 本测试红。
        MapData map = MatchFixtures.Map() with { TerrainData = TestMaps.Terrain(heights: [("D5", 2), ("E5", 2)]) };
        MatchFlow match = MatchFlow.CreateUnvalidated(map, MatchFixtures.Seed, MatchFixtures.All, MatchFixtures.Relics(map), MatchOptions.Immediate);
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
        // 段 A 改写：文案顺序随公式改为"（基础 + 加值）× 倍率"；无倍增子，军势 ⌊(2 + 2) × 1⌋ = 4（新旧同值）。
        Assert.Equal(4, truth.Power);
        Assert.Equal("（基础 2 + 位置加值 2（连珠 0 / 协同 0 / 高地 2））× 1 = 4", group.Power.FormulaText);
        Assert.Equal(group.Power.PositionBonus, group.Power.LineBonus + group.Power.SynergyBonus + group.Power.HighGroundBonus);
    }

    [Fact]
    public void 倍率热区等级只是显示档位()
    {
        // restore-go-core-rules 段 A：倍率不封顶后，势力层的热区等级（柱高 / 着色档位）取 min(倍增子数量, 3)，不随数量无限加高；
        // 它只是显示档位，不是倍率封顶——同一条棋串的倍率文字与军势仍是精确值：5 枚倍增子 → 倍率 7.59375、军势 ⌊5 × 243 / 32⌋ = ⌊37.96…⌋ = 37。
        // 变异验证 M-AC15（段 A check 实跑）：LayerContents 的热区等级去掉 Math.Min（直接取倍增子数量）→ 全套只红本测试 1 条（5 ≠ 3）。
        MatchFlow match = MatchFixtures.Started().AtRound(5).Pieces(P0, PieceType.Multiplier, "C5", "D5", "E5", "F5", "G5").Pieces(P1, PieceType.Multiplier, "C2", "D2");

        var layer = (PowerLayerContent)match.World(P3).Layer(TacticalLayer.Power);
        GroupScoreView five = Assert.Single(layer.Groups, g => g.Owner == P0);
        GroupScoreView two = Assert.Single(layer.Groups, g => g.Owner == P1);

        Assert.Equal(3, GroupScoreView.MaxHeatLevel);
        Assert.Equal((5, 3, "7.59375"), (five.Power.MultiplierCount, five.HeatLevel, five.Power.MultiplierText));
        Assert.Equal(37, five.Power.Power);
        Assert.Equal("（基础 5 + 位置加值 0）× 7.59375 = 37", five.Power.FormulaText);
        Assert.Equal((2, 2), (two.Power.MultiplierCount, two.HeatLevel));
    }
}
