using System.Reflection;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Presentation.Layers;
using Siege.Presentation.Visibility;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.TacticalLayers;

/// <summary>规格：tactical-layers —— Requirement: 默认棋盘上的未发现信物</summary>
public class 默认棋盘上的未发现信物Tests
{
    [Fact]
    public void 默认棋盘不显示分区()
    {
        // 裁决 5：B2（出生区档）与 E5（公共争夺区高档）两个未发现信物格在默认棋盘上显示为相同的"未知信物"标记。
        // 结构上默认棋盘格没有任何分区 / 档位字段。
        // 变异验证 M-B1：BoardCellView 加 `RelicZone? Zone` 并在 DefaultBoardView.From 里填入分区 → 本测试红 1。
        MatchFlow match = StartedWithZonedRelics(
            ("B2", RelicFixtures.Command(), RelicZone.BirthZone, BudgetTier.Birth),
            ("E5", RelicFixtures.Depot(2), RelicZone.Contested, BudgetTier.High));

        DefaultBoardView board = match.World(P1).Board();
        BoardCellView b2 = board.CellAt(TestMaps.At("B2"));
        BoardCellView e5 = board.CellAt(TestMaps.At("E5"));

        Assert.Equal(RelicMarker.Unknown, b2.Relic);
        Assert.Equal(Dump(b2 with { Coord = default, BirthZone = null }), Dump(e5 with { Coord = default, BirthZone = null }));
        Type[] zoning = [typeof(RelicZone), typeof(BudgetTier), typeof(RelicCellSpec)];
        Assert.DoesNotContain(typeof(BoardCellView).GetProperties(BindingFlags.Instance | BindingFlags.Public),
            p => zoning.Contains(Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType));
    }

    [Fact]
    public void 分区信息在信物层可查()
    {
        // 裁决 5：打开信物层 → 每个信物格的分区可见，未发现信物的类型与强度仍隐藏。
        // 变异验证 M-B2：TacticalLayers.RelicCell 的 Zone 固定填 RelicZone.Contested → 本测试红 1。
        MatchFlow match = StartedWithZonedRelics(
            ("B2", RelicFixtures.Command(), RelicZone.BirthZone, BudgetTier.Birth),
            ("E5", RelicFixtures.Depot(2), RelicZone.Contested, BudgetTier.High));

        var relics = ((RelicLayerContent)match.World(P1).Layer(TacticalLayer.Relics)).Relics;

        RelicCellView b2 = relics.Single(r => r.Coord == TestMaps.At("B2"));
        RelicCellView e5 = relics.Single(r => r.Coord == TestMaps.At("E5"));
        Assert.Equal((RelicZone.BirthZone, BudgetTier.Birth, "出生区 · 出生档"), (b2.Zone, b2.Budget, b2.ZoneText));
        Assert.Equal((RelicZone.Contested, BudgetTier.High, "公共争夺区 · 高档"), (e5.Zone, e5.Budget, e5.ZoneText));
        Assert.All(relics, r => Assert.Equal((false, (Core.Relics.RelicContent?)null, "未知信物"), (r.IsRevealed, r.Content, r.ContentText)));
    }
}
