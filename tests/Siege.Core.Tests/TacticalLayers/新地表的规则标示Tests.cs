using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Presentation.Layers;
using Siege.Presentation.Visibility;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.TacticalLayers;

/// <summary>
/// 规格 terrain-surfaces · tactical-layers —— Requirement: 新地表的规则标示；以及「五种战术信息层」的差集地形来源（沼泽源 / 岩台远格 / 空浅滩）。
/// 各段只加本段落地的地表：段 2 沼泽。
/// </summary>
public class 新地表的规则标示Tests
{
    [Fact]
    public void 图例给出规则描述()
    {
        // 规格 Scenario「图例给出规则描述」：地图含沼泽 → 盘面层图例有"沼泽：其上的棋子不产生覆盖"；地图上没有的新地表不出现。
        // 两种读法共用同一份图例。
        MatchFlow match = TerrainMatch(TestMaps.Terrain(surfaces: [("E5", Surface.Marsh)]));

        var ownership = (TerritoryLayerContent)match.World(P0).Layer(TacticalLayer.Board, BoardReading.Ownership);
        var groups = (LibertyLayerContent)match.World(P0).Layer(TacticalLayer.Board, BoardReading.Groups);

        Assert.Equal(["沼泽：其上的棋子不产生覆盖"], ownership.SurfaceLegend);
        Assert.Equal(ownership.SurfaceLegend, groups.SurfaceLegend);

        // 反面：没有新地表的地图，图例为空（草地 / 林地 / 深水不进图例）。
        MatchFlow plain = TerrainMatch(TestMaps.Terrain(surfaces: [("E5", Surface.Forest), ("E3", Surface.DeepWater)]));
        Assert.Empty(((TerritoryLayerContent)plain.World(P0).Layer(TacticalLayer.Board, BoardReading.Ownership)).SurfaceLegend);
    }

    [Fact]
    public void 沼泽上的棋子不点亮归属()
    {
        // 规格「新地表的规则标示」·归属读法：沼泽上的棋子 MUST NOT 为任何空格点亮归属。
        MatchFlow match = TerrainMatch(TestMaps.Terrain(surfaces: [("E5", Surface.Marsh)])).Pieces(P0, PieceType.Basic, "E5");

        var ownership = (TerritoryLayerContent)match.World(P0).Layer(TacticalLayer.Board, BoardReading.Ownership);

        Assert.Contains(ownership.Cells, c => c.Coord == TestMaps.At("E5") && c.State == TerritoryState.Occupied && c.Owner == P0);
        Assert.DoesNotContain(ownership.Cells, c => c.State == TerritoryState.Exclusive && c.Owner == P0);
    }

    [Fact]
    public void 差集可由沼泽源解释()
    {
        // 规格「五种战术信息层」差集来源：是气但未被覆盖的格来自林地或沼泽源。P0 单子在沼泽 E5、四邻空草地 →
        // 四个邻格都是它的气、却无人覆盖，原因都是"沼泽"。修改前这一格会让 ReadingDiff 抛"不是林地"。
        // 变异验证 M-S2b（实跑）：ReadingDiff 去掉沼泽源分支 → 本测试红（抛出）。
        MatchFlow match = TerrainMatch(TestMaps.Terrain(surfaces: [("E5", Surface.Marsh)])).Pieces(P0, PieceType.Basic, "E5");

        var groups = (LibertyLayerContent)match.World(P0).Layer(TacticalLayer.Board, BoardReading.Groups);
        BoardReadingDiff diff = groups.Diff;

        Assert.Empty(diff.CoveredNotLiberty);
        Assert.Equal(["E4", "D5", "F5", "E6"], diff.LibertyNotCovered.Select(d => d.Coord).Notations());
        Assert.All(diff.LibertyNotCovered, d => Assert.Equal([TerrainReason.Marsh], d.Reasons));
    }

    /// <summary>9×9 流程夹具地图换上指定地形后开到第 5 大回合（与「盘面层的读法切换」同一夹具；地形要素须避开四角 3×3 出生区）。</summary>
    private static MatchFlow TerrainMatch(TerrainData terrain)
    {
        MapData map = MatchFixtures.Map() with { TerrainData = terrain };
        MatchFlow match = MatchFlow.CreateUnvalidated(map, MatchFixtures.Seed, MatchFixtures.All, MatchFixtures.Relics(map), MatchOptions.Immediate);
        foreach (PlayerId p in MatchFixtures.All)
        {
            match.Debug.SeedHand(p, (PieceType.Basic, 50));
        }

        match.PlantSequentially(MatchFixtures.All.Select((p, i) => (p, i)));
        return match.AtRound(5, [P0, P1, P2, P3]);
    }
}
