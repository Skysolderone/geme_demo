using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Presentation.Visibility;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.PieceRelics;

/// <summary>
/// 规格：openspec/specs/tactical-layers —— Requirement: 默认棋盘上的信物三种标记（relic-sites 裁决 10：信物遗址标记）。
/// 默认棋盘视图模型原样带出地图的遗址清单（格 + 档位），按座位过滤与锁定前后都保留；小图为空。Godot 只读这里，不读地图。
/// </summary>
public class 信物遗址标记视图Tests
{
    [Fact]
    public void 大图默认棋盘带遗址清单_按座位过滤与锁定前后都保留()
    {
        MapData map = MapCatalog.Resolve("link:7:4:l");
        MatchFlow match = MatchFlow.Create(map, TestMaps.Seed, [P0, P1, P2, P3], MatchOptions.Immediate);
        Assert.NotEmpty(map.RelicSites);

        // 全知视图与本座位视图（插旗阶段：大图全图可见）。
        Assert.Equal(map.RelicSites, DefaultBoardView.From(match.PublicWorldOf()).RelicSites);
        DefaultBoardView planting = match.World(P0).Board();
        Assert.Equal(map.RelicSites, planting.RelicSites);
        Assert.All(planting.RelicSites, s => Assert.Equal(Terrain.Playable, planting.CellAt(s.Cell).Terrain));
        Assert.All(planting.RelicSites, s => Assert.Equal(0, planting.CellAt(s.Cell).Height));   // 追加裁决 13：遗址只落低地

        // 锁定后（落座图）清单不变；布局指纹不含遗址（静态量，锁定本身已让指纹变）。
        match.PlantPrototype();
        DefaultBoardView locked = match.World(P0).Board();
        Assert.Equal(map.RelicSites, locked.RelicSites);
        Assert.Equal(map.RelicSites, locked.ForSeat(match.PublishFor(P0), locked.Placeholders.FirstOrDefault(p => p.Own)?.Zone).RelicSites);
        Assert.DoesNotContain("Rare", locked.LayoutKey(), StringComparison.Ordinal);
    }

    [Fact]
    public void 小图没有遗址清单()
    {
        MapData map = MapCatalog.Resolve("link:1:4");
        MatchFlow match = MatchFlow.Create(map, TestMaps.Seed, [P0, P1, P2, P3], MatchOptions.Immediate);
        Assert.Empty(DefaultBoardView.From(match.PublicWorldOf()).RelicSites);
        Assert.Empty(match.World(P0).Board().RelicSites);
    }
}
