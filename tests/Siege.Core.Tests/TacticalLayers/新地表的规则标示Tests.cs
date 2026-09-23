using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Presentation.Layers;
using Siege.Presentation.Visibility;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.TacticalLayers;

/// <summary>
/// 规格 terrain-surfaces · tactical-layers —— Requirement: 新地表的规则标示；以及「五种战术信息层」的差集地形来源（沼泽源 / 岩台远格 / 空浅滩）。
/// 各段只加本段落地的地表：段 2 沼泽、段 3 岩台、段 4 浅滩。
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

    [Fact]
    public void 岩台远格的归属()
    {
        // 规格 Scenario「岩台远格的归属」：A 在岩台 E5，G5 为空草地且只被它覆盖 → 归属读法显示 G5 为 A 独占，样式与其他独占格相同（不另设状态）。
        MatchFlow match = TerrainMatch(TestMaps.Terrain(surfaces: [("E5", Surface.Crag)])).Pieces(P0, PieceType.Basic, "E5");

        var ownership = (TerritoryLayerContent)match.World(P0).Layer(TacticalLayer.Board, BoardReading.Ownership);

        Assert.Contains(new TerritoryCellView(TestMaps.At("G5"), TerritoryState.Exclusive, P0), ownership.Cells);
        Assert.Equal(8, ownership.Cells.Count(c => c.State == TerritoryState.Exclusive && c.Owner == P0));
    }

    [Fact]
    public void 差集可由岩台远格解释()
    {
        // 规格「五种战术信息层」差集来源：被覆盖但不是气的格来自岩台远格。A 单子在岩台 E5 → C5、G5、E3、E7 被覆盖却不是气，原因"岩台"。
        // 修改前这些格会被错标成"隔岸"（来源不相邻）。
        // 变异验证 M-S3e（实跑）：ReasonFor 对不相邻来源恒返回隔岸 → 本测试红。
        MatchFlow match = TerrainMatch(TestMaps.Terrain(surfaces: [("E5", Surface.Crag)])).Pieces(P0, PieceType.Basic, "E5");

        BoardReadingDiff diff = ((TerritoryLayerContent)match.World(P0).Layer(TacticalLayer.Board, BoardReading.Ownership)).Diff;

        Assert.Empty(diff.LibertyNotCovered);
        Assert.Equal(["E3", "C5", "G5", "E7"], diff.CoveredNotLiberty.Select(d => d.Coord).Notations());
        Assert.All(diff.CoveredNotLiberty, d => Assert.Equal([TerrainReason.Crag], d.Reasons));
    }

    [Fact]
    public void 差集可由新地表解释()
    {
        // 规格 Scenario「差集可由新地表解释」：一块只含岩台、浅滩、沼泽三种特殊地形的区域——
        // 岩台 C5 上 P0（其远格 C7 另设为空浅滩）、沼泽 G6 上 P1（两子相隔足够远，互不干扰）→ 差集每一格都属于岩台远格、空浅滩或沼泽源。
        MatchFlow match = TerrainMatch(TestMaps.Terrain(surfaces: [("C5", Surface.Crag), ("C7", Surface.Shallows), ("G6", Surface.Marsh)]))
            .Pieces(P0, PieceType.Basic, "C5").Pieces(P1, PieceType.Basic, "G6");

        BoardReadingDiff diff = ((LibertyLayerContent)match.World(P0).Layer(TacticalLayer.Board, BoardReading.Groups)).Diff;

        Assert.All(diff.CoveredNotLiberty.Concat(diff.LibertyNotCovered), d => Assert.Contains(d.Reasons.Single(), new[] { TerrainReason.Crag, TerrainReason.Shallows, TerrainReason.Marsh }));
        Assert.Contains(diff.CoveredNotLiberty, d => d.Reasons.Single() == TerrainReason.Crag);
        Assert.Equal([TerrainReason.Shallows], diff.CoveredNotLiberty.Single(d => d.Coord == TestMaps.At("C7")).Reasons);
        Assert.NotEmpty(diff.LibertyNotCovered);
        Assert.All(diff.LibertyNotCovered, d => Assert.Equal([TerrainReason.Marsh], d.Reasons));
    }

    [Fact]
    public void 空浅滩在棋串读法中标为不算气()
    {
        // 规格 Scenario「空浅滩在棋串读法中标为不算气」：某棋串右侧 F5 是空浅滩、左侧 D5 是空草地 →
        // D5 是它的气，F5 单独列为"贴着的空浅滩"，不在气里；二者分属两个集合，外观可区分（Godot 画暗灰小点，图例标明）。
        // 变异验证 M-S4d（实跑）：LibertySnapshot 不填 ShallowsBeside → 本测试红。
        MatchFlow match = TerrainMatch(TestMaps.Terrain(surfaces: [("F5", Surface.Shallows)])).Pieces(P0, PieceType.Basic, "E5");

        var groups = (LibertyLayerContent)match.World(P0).Layer(TacticalLayer.Board, BoardReading.Groups);
        LibertyGroupView group = groups.Groups.Single(g => g.Owner == P0);

        Assert.Equal(["E4", "D5", "E6"], group.Liberties.Notations());
        Assert.Equal(["F5"], group.ShallowsBeside.Notations());
        Assert.Contains("浅滩：空着时不算气", groups.SurfaceLegend);
    }

    [Fact]
    public void 差集可由空浅滩解释()
    {
        // 规格「五种战术信息层」差集来源：被覆盖但不是气的格可来自空浅滩。E5 上的 P0 覆盖空浅滩 F5，F5 不是气 → 原因"浅滩"。
        // 修改前这一格会让 ReasonFor 抛"既无栅栏也非崖壁"。
        MatchFlow match = TerrainMatch(TestMaps.Terrain(surfaces: [("F5", Surface.Shallows)])).Pieces(P0, PieceType.Basic, "E5");

        BoardReadingDiff diff = ((TerritoryLayerContent)match.World(P0).Layer(TacticalLayer.Board, BoardReading.Ownership)).Diff;

        Assert.Empty(diff.LibertyNotCovered);
        ReadingDiffCell cell = Assert.Single(diff.CoveredNotLiberty);
        Assert.Equal(TestMaps.At("F5"), cell.Coord);
        Assert.Equal([TerrainReason.Shallows], cell.Reasons);
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
