using System.Numerics;
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
        // 段 E（tasks 5.4）：势力层内容改为"独占格着色 + 领地分 + 棋串分"。规格算例：P0 占 C5–G5 一排 5 子 → 覆盖第 4 / 6 行各 5 格 + 两端 B5、H5 = 12 个独占空格，
        // 这 12 格以 P0 的阵营呈现、领地分读得出 12。另摆 P1 J9 与 P2 G9：两家共同覆盖 H9 → 争议格，MUST NOT 以任何玩家的得分呈现。
        // 独占格直接投影势力明细的 ExclusiveCells（空格归属三态的唯一结果），不在表现层另扫覆盖表。
        // 变异验证 M-E8：LayerContents.Power 的独占格集合掺入争议格（争议格也以独占格呈现）→ 实跑红 1（本测试）。
        MatchFlow match = MatchFixtures.Started().AtRound(5).Stones(P0, "C5", "D5", "E5", "F5", "G5").Stones(P1, "J9").Stones(P2, "G9");

        var layer = (PowerLayerContent)match.World(P3).Layer(TacticalLayer.Power);
        PowerSnapshot truth = match.Scoreboard.Latest!;

        TerritoryCellView[] mine = [.. layer.Territory.Where(c => c.Owner == P0)];
        Assert.Equal(12, mine.Length);
        Assert.Equal(12, layer.Players.Single(r => r.Player == P0).TerritoryScore);
        Assert.Equal(
            ["B5", "C4", "C6", "D4", "D6", "E4", "E6", "F4", "F6", "G4", "G6", "H5"],
            mine.Select(c => c.Coord.ToNotation()).Order(StringComparer.Ordinal));
        Assert.All(layer.Territory, c => Assert.Equal(TerritoryState.Exclusive, c.State));
        Assert.Equal(OwnershipKind.Contested, truth.Coverage.OwnershipOf(Coord.Parse("H9")).Kind);
        Assert.DoesNotContain(layer.Territory, c => c.Coord == Coord.Parse("H9"));
        foreach (PlayerPower p in truth.Players)
        {
            Assert.Equal(p.ExclusiveCells.Order(), layer.Territory.Where(c => c.Owner == p.Player).Select(c => c.Coord).Order());
        }

        foreach (PlayerPowerRowView row in layer.Players)
        {
            PlayerPower detail = truth.Of(row.Player);
            Assert.Equal(detail.ScoredCells.Length, row.TerritoryScore);
            Assert.Equal(detail.Total, row.Total);

            // 独立复算：总势力 = 领地分 + 该玩家全部棋串军势（不回调被测视图）。
            System.Numerics.BigInteger groups = System.Numerics.BigInteger.Zero;
            foreach (GroupScoreView g in layer.Groups.Where(g => g.Owner == row.Player))
            {
                groups += g.Power.Power;
            }

            Assert.Equal(row.TerritoryScore + groups, row.Total);
            Assert.Equal(groups, row.GroupScore);
        }

        Assert.Equal(4, layer.Players.Length);
        Assert.Contains(layer.Players, r => r.TerritoryScore > 0);
    }

    [Fact]
    public void 荒漠独占不进领地分()
    {
        // 规格 terrain-surfaces · tactical-layers「荒漠独占不进领地分」：A 独占 5 个空草地格与 3 个空荒漠格 → 领地分显示 5，
        // 3 个荒漠格以"独占、不计分"的样式出现（Scored = false），且仍归 A。
        // 布置：P0 占 C5–E5 一排 3 子 → 独占 B5、F5、C4、D4、E4、C6、D6、E6 共 8 格，其中 B5、C4、C6 为荒漠。
        // 变异验证 M-S1c（实跑）：LayerContents.Power 的 Scored 恒为 true → 本测试红。
        TerrainData terrain = TestMaps.Terrain(surfaces: [("B5", Surface.Desert), ("C4", Surface.Desert), ("C6", Surface.Desert)]);
        MatchFlow match = MatchFixtures.Started(terrain).AtRound(5).Stones(P0, "C5", "D5", "E5");

        var layer = (PowerLayerContent)match.World(P1).Layer(TacticalLayer.Power);

        TerritoryCellView[] mine = [.. layer.Territory.Where(c => c.Owner == P0)];
        Assert.Equal(8, mine.Length);
        Assert.Equal(["B5", "C4", "C6"], mine.Where(c => !c.Scored).Select(c => c.Coord.ToNotation()).Order(StringComparer.Ordinal));
        Assert.Equal(5, mine.Count(c => c.Scored));
        Assert.Equal(5, layer.Players.Single(r => r.Player == P0).TerritoryScore);
        Assert.Equal(5 + 3, layer.Players.Single(r => r.Player == P0).Total);
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

    [Theory]
    [InlineData("0", "0")]
    [InlineData("999999", "999999")]
    [InlineData("1000000", "1.00M")]
    [InlineData("1234567", "1.23M")]
    [InlineData("12345678", "12.3M")]
    [InlineData("123456789", "123M")]
    [InlineData("1999999999", "1.99B")]
    [InlineData("999999999999999", "999T")]
    [InlineData("1000000000000000", "1.00e15")]
    [InlineData("515377520732011331036461129765621272702107522001", "5.15e47")]
    [InlineData("-1234567", "-1.23M")]
    public void 大数势力显示缩写且明细给精确值(string exact, string compact)
    {
        // restore-go-core-rules 段 E（tasks 5.3 / 5.5，design Open Question 4）：势力 ≥ 10^6 起用缩写（M / B / T，再往上用 e 记数），
        // 三位有效数字、一律向零截断（不四舍五入：999999999 不会显示成 1000M）；10^6 以下原样给精确值。只是显示——比较与排序始终用精确值。
        // 期望值是手写的十进制串（3^100 的 48 位值由仓库外独立计算，见 军势精确整数遥测Tests）。全程整数运算，不经浮点（守门 内核与表现层不出现浮点）。
        // 变异验证 M-E9：缩写阈值 10^6 → 10^7 → 实跑红 3（1.00M / 1.23M / -1.23M 三行）。
        BigInteger value = BigInteger.Parse(exact);
        Assert.Equal(compact, PowerNotation.Compact(value));
        Assert.Equal(compact, Labels.CompactPower(value));

        // 势力行：概览用缩写，明细给精确值；两者都拆成"领地 + 棋串"。
        var row = new PlayerPowerRowView(P0, PlayerStatus.Active, value + 7, 7, value, 1, null);
        Assert.Equal($"势力 {PowerNotation.Compact(value + 7)}（领地 7 + 棋串 {compact}）", row.CompactText);
        Assert.Equal($"势力 {value + 7}（领地 7 + 棋串 {value}）", row.ExactText);
    }
}
