using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.LinkMapGeneration;

/// <summary>
/// 规格：任务 10-26-map-expand-25 design D7（链接档校验改为按人数的浮空岛规则）；map-definition —— Requirement: 地图规格档（Scenario 规格档读写往返 / 规格档不改规则）。
/// 载体：生成图 <c>link:1:2</c>（合规基线），每条用例只改坏一处，断言对应的失败码被报出；另断言生成图逐张通过校验。
/// 旧的紧贴布局规则（外圈平坦、棋盘外不得有深水、可落子格都属于棋盘、连通分量恰 1、接缝气边、主题两两不同、每块至多 1 个公共信物）随 D7 删除，其用例一并删除。
/// 段 A3：浮空岛不放静态信物格（relic-redesign R29，2026-10-26 用户裁决），ISLAND_PUBLIC_RELIC 与 BIRTH_BOARD_RELIC_COUNT 删除，
/// 「公共信物在平台内或超过人数加二被拒」「每块平台恰两个出生区信物」两条随之删除（下面 M7 一项随之作废）。
/// </summary>
/// <remarks>
/// 变异验证（map-expand-25 段 A1 实跑，过滤 LinkMapGeneration 命名空间）：
/// M1 ISLAND_EXTENT 下限改 W−1 → 「可落子区外接矩形不足被拒」红 1；M2 PlatesApart 间隔改 gap−1 → 本类「平台间隔不足被拒」+ 浮空岛布局Tests「平台数与尺寸」×3 共红 4；
/// M5 坡道朝外只查在盘内 → 「平台高度与坡道不合规被拒」红 1；M6 LibertySplit 恒空 → 「全图气边不连通被拒」+ 浮空岛布局Tests「全图可落子格沿气边连通」×3 共红 4；
/// M7 公共信物上限 +10 → 「公共信物在平台内或超过人数加二被拒」红 1；M8 平台高度只查坡道 → 「平台高度与坡道不合规被拒」红 1；M9 不拒高台主题 → 「高台主题被拒」红 1。
/// </remarks>
public class 物化状态校验Tests
{
    private static MapData Base => LinkMapGenerator.Generate(1, 2);

    private static ImmutableArray<MapValidationFailure> Failures(MapData map) => MapValidator.Validate(map).Failures;

    /// <summary>在地图原有地形之上再加若干地表 / 栅栏 / 高度（其他测试类共用，如 构筑期内的改造范围Tests）。</summary>
    internal static TerrainData With(MapData map, (string Cell, Surface Surface)[]? surfaces = null, (string A, string B)[]? fences = null, (string Cell, int Height)[]? heights = null)
    {
        TerrainData extra = TestMaps.Terrain(heights: heights, surfaces: surfaces, fences: fences);
        TerrainData t = map.TerrainData;
        return new TerrainData(t.Heights.SetItems(extra.Heights), t.Surfaces.SetItems(extra.Surfaces), t.Bridges, t.Fences.Union(extra.Fences));
    }

    private static MapData WithHeights(MapData map, params (Coord Cell, int Height)[] heights)
    {
        TerrainData t = map.TerrainData;
        return map with { TerrainData = new TerrainData(t.Heights.SetItems(heights.Select(h => KeyValuePair.Create(h.Cell, h.Height))), t.Surfaces, t.Bridges, t.Fences) };
    }

    private static Coord[] Ramps(MapData map, BoardPlate plate) => [.. plate.Cells().Where(c => map.IsPlayable(c) && map.HeightAt(c) == 1)];

    [Theory]
    [InlineData(1ul, 2)]
    [InlineData(7ul, 2)]
    [InlineData(20ul, 2)]
    [InlineData(1ul, 3)]
    [InlineData(7ul, 3)]
    [InlineData(20ul, 3)]
    [InlineData(1ul, 4)]
    [InlineData(7ul, 4)]
    [InlineData(20ul, 4)]
    public void 生成后校验(ulong seed, int players)
    {
        // Scenario：由任一 (种子, 人数) 生成地图 → 通过校验，版本号为 0，没有报告项；能直接加载。
        MapData map = LinkMapGenerator.Generate(seed, players);
        MapValidationResult result = MapValidator.Validate(map);
        Assert.True(result.IsValid, result.ToString());
        Assert.Empty(result.Reports);
        Assert.Equal(0, map.MapVersion);
        _ = GameBoard.Load(map);
    }

    [Fact]
    public void 删除的紧贴布局规则不再报出()
    {
        // D7：低地是棋盘之外的可落子格、低地上有深水河道，主题可重复——这些在旧规则下分别报 SCENERY_CELL_PLAYABLE / BOARD_MAP_DEEP_WATER / BOARD_THEME_INVALID，现在合法。
        MapData map = Base;
        Assert.Contains(map.AllCoords(), c => map.IsPlayable(c) && !map.Boards.Any(p => p.Contains(c)));
        Assert.Contains(map.AllCoords(), c => map.SurfaceAt(c) == Surface.DeepWater && !map.Boards.Any(p => p.Contains(c)));
        MapData repeated = Enumerable.Range(1, 20).Select(s => LinkMapGenerator.Generate((ulong)s, 2)).First(m => m.Boards.GroupBy(p => p.Theme).Any(g => g.Count() > 1));
        Assert.True(MapValidator.Validate(repeated).IsValid);
        string[] retired = ["BOARD_RIM_NOT_FLAT", "BOARD_MAP_DEEP_WATER", "RELIC_OUTSIDE_BOARD", "SCENERY_CELL_PLAYABLE", "BOARD_OVERLAP", "BATTLEFIELD_WITHOUT_BIRTH_BOARD", "BATTLEFIELD_COUNT_MISMATCH", "LINK_SEAM_NO_LIBERTY", "BOARD_THEME_INVALID"];
        string source = File.ReadAllText(Path.Combine(TestPaths.RepoRoot(), "src", "Siege.Core", "Board", "MapValidator.cs"));
        Assert.All(retired, code => Assert.DoesNotContain($"\"{code}\"", source, StringComparison.Ordinal));
        Assert.Contains("\"ISLAND_LIBERTY_SPLIT\"", source, StringComparison.Ordinal);   // 反面：扫的确实是校验器
    }

    [Fact]
    public void 平台间隔不足被拒()
    {
        // ISLAND_PLATE_GAP：两块平台外接矩形在横纵两个方向上的间隔都小于 3（含重叠）即拒。判定式先单测边界（恰 3 通过、2 不通过、重叠不通过）。
        var a = new BoardPlate(new Coord(4, 4), 6, 6, BoardPlateKind.Birth);
        Assert.True(MapValidator.PlatesApart(a, a with { Origin = new Coord(13, 4) }, 3));    // 间隔 3
        Assert.False(MapValidator.PlatesApart(a, a with { Origin = new Coord(12, 4) }, 3));   // 间隔 2
        Assert.True(MapValidator.PlatesApart(a, a with { Origin = new Coord(12, 13) }, 3));   // 纵向间隔 3 即可（斜对角）
        Assert.False(MapValidator.PlatesApart(a, a with { Origin = new Coord(7, 7) }, 3));    // 重叠

        MapData map = Base;
        BoardPlate first = map.Boards[0];
        var near = new BoardPlate(new Coord(first.Origin.X + first.Width + 2, first.Origin.Y), 6, 6, BoardPlateKind.Public);
        MapData bad = map with { Boards = map.Boards.Add(near) };
        Assert.Contains(Failures(bad), f => f.Code == "ISLAND_PLATE_GAP" && f.Coords.SequenceEqual([first.Origin, near.Origin]));
        Assert.DoesNotContain(Failures(map), f => f.Code == "ISLAND_PLATE_GAP");
    }

    [Fact]
    public void 平台高度与坡道不合规被拒()
    {
        // PLATE_HEIGHT（D3 / D7）：平台内可落子格除坡道外须 h=2；坡道 2–3 个、外圈非角格、草地、朝外是 h=0 可落子低地。
        MapData map = Base;
        BoardPlate plate = map.Boards[0];
        Coord[] ramps = Ramps(map, plate);
        Assert.InRange(ramps.Length, 2, 3);

        // ① 只剩 1 个坡道。
        MapData fewer = WithHeights(map, [.. ramps.Skip(1).Select(r => (r, 2))]);
        Assert.Contains(Failures(fewer), f => f.Code == "PLATE_HEIGHT" && f.Message.Contains("有 1 个坡道", StringComparison.Ordinal));

        // ② 中间带一格降到 0。
        Coord middle = plate.Cells().First(c => plate.InMiddleBand(c) && map.IsPlayable(c));
        Assert.Contains(Failures(WithHeights(map, (middle, 0))), f => f.Code == "PLATE_HEIGHT" && f.Coords.SequenceEqual([middle]));

        // ③ 坡道朝外的低地格改成深水（不可落子）。
        Coord ramp = ramps[0];
        Coord outlet = Outlets(map, plate)[ramp];
        TerrainData t = map.TerrainData;
        MapData flooded = map with { TerrainData = new TerrainData(t.Heights, t.Surfaces.SetItem(outlet, Surface.DeepWater), t.Bridges, t.Fences) };
        Assert.Contains(Failures(flooded), f => f.Code == "PLATE_HEIGHT" && f.Coords.SequenceEqual([ramp]));

        // ④ 坡道放在中间带（不在外圈）。
        MapData inner = WithHeights(map, (middle, 1));
        Assert.Contains(Failures(inner), f => f.Code == "PLATE_HEIGHT" && f.Coords.Contains(middle) && f.Message.Contains("外圈非角格", StringComparison.Ordinal));

        Assert.DoesNotContain(Failures(map), f => f.Code == "PLATE_HEIGHT");
    }

    [Fact]
    public void 核心区不是高二草地被拒()
    {
        MapData map = Base;
        Coord core = map.Boards[1].CoreCells().First();
        MapData lowered = WithHeights(map, (core, 1));
        Assert.Contains(Failures(lowered), f => f.Code == "BIRTH_CORE_NOT_FLAT" && f.Coords.SequenceEqual([core]));
        Assert.DoesNotContain(Failures(map), f => f.Code == "BIRTH_CORE_NOT_FLAT");
    }

    [Fact]
    public void 高台主题被拒()
    {
        // PLATE_THEME（R3）：平台主题不得为高台；缺主题照旧被拒。
        MapData map = Base;
        MapData plateau = map with { Boards = map.Boards.SetItem(0, map.Boards[0] with { Theme = BoardTheme.Plateau }) };
        Assert.Contains(Failures(plateau), f => f.Code == "PLATE_THEME" && f.Message.Contains("高台", StringComparison.Ordinal) && f.Coords.SequenceEqual([map.Boards[0].Origin]));
        MapData none = map with { Boards = map.Boards.SetItem(0, map.Boards[0] with { Theme = null }) };
        Assert.Contains(Failures(none), f => f.Code == "PLATE_THEME");
        Assert.DoesNotContain(Failures(map), f => f.Code == "PLATE_THEME");
    }

    [Fact]
    public void 全图气边不连通被拒()
    {
        // ISLAND_LIBERTY_SPLIT：把平台 0 的坡道全部抬到 h=2，平台四周全成崖壁 → 平台 0 的可落子格与低地隔开，失败项指出这些格。
        MapData map = Base;
        BoardPlate plate = map.Boards[0];
        MapData sealedOff = WithHeights(map, [.. Ramps(map, plate).Select(r => (r, 2))]);
        MapValidationFailure split = Assert.Single(Failures(sealedOff), f => f.Code == "ISLAND_LIBERTY_SPLIT");
        Assert.Equal(plate.Cells().Where(sealedOff.IsPlayable).Order(), split.Coords);
        Assert.Contains(Failures(sealedOff), f => f.Code == "PLATE_HEIGHT");   // 同时坡道数为 0
        Assert.Empty(MapValidator.LibertySplit(map));
    }

    [Fact]
    public void 可落子区外接矩形不足被拒()
    {
        // ISLAND_EXTENT（验收的核心断言）：把岛最南一行全部改成障碍，可落子区外接矩形变成 W×(W−1) → 拒。
        MapData map = Base;
        int bottom = map.AllCoords().Where(map.IsPlayable).Min(c => c.Y);
        MapData cut = map with { Obstacles = map.Obstacles.Union(map.AllCoords().Where(c => c.Y == bottom)) };
        MapValidationFailure extent = Assert.Single(Failures(cut), f => f.Code == "ISLAND_EXTENT");
        Assert.Contains("30×29", extent.Message, StringComparison.Ordinal);
        Assert.Contains("至少 30×30", extent.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Failures(map), f => f.Code == "ISLAND_EXTENT");
    }

    [Fact]
    public void 画布不合人数被拒()
    {
        // 画布边长恰等于 W(n)+2M：2 人图标成 3 人 → 画布 34 不足 39 → MAP_TOO_NARROW，到此为止（不再报平台规则）；人数 5 没有预算行 → UNSUPPORTED_PLAYER_COUNT，画布不查。
        MapData map = Base;
        ImmutableArray<MapValidationFailure> three = Failures(map with { MaxPlayers = 3 });
        Assert.Equal(["MAP_TOO_NARROW"], three.Select(f => f.Code));
        Assert.Contains("39", three[0].Message, StringComparison.Ordinal);
        ImmutableArray<MapValidationFailure> five = Failures(map with { MaxPlayers = 5 });
        Assert.Contains(five, f => f.Code == "UNSUPPORTED_PLAYER_COUNT");
        Assert.DoesNotContain(five, f => f.Code.StartsWith("MAP_TOO_", StringComparison.Ordinal));
    }

    [Fact]
    public void 规格档读写往返()
    {
        // Scenario：把一张链接档地图的初始状态导出为文件再读入 → 规格档仍为链接，棋盘清单与其余字段逐项相等；版本号零时省略。
        MapData map = Base;
        string json = MapFile.ToJson(map);
        Assert.Contains("\"Profile\": \"Link\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"MapVersion\"", json, StringComparison.Ordinal);
        MapData restored = MapFile.FromJson(json);
        Assert.Equal(MapProfile.Link, restored.Profile);
        Assert.Equal(0, restored.MapVersion);
        Assert.Equal(2, restored.MaxPlayers);
        Assert.Equal(map.Boards, restored.Boards);
        Assert.Equal(map.Obstacles.Order(), restored.Obstacles.Order());
        Assert.Equal(map.RelicCells.OrderBy(kv => kv.Key), restored.RelicCells.OrderBy(kv => kv.Key));
        Assert.Equal(json, MapFile.ToJson(restored));
        Assert.True(MapValidator.Validate(restored).IsValid);
    }

    [Fact]
    public void 规格档不改规则()
    {
        // Scenario：在链接档地图上，一条棋串的全部气边邻格均被敌子占据 → 无气，判定与标准档相同。
        MapData map = Base;
        GameBoard board = GameBoard.Load(map);
        var a = new PlayerId(0);
        var b = new PlayerId(1);
        Coord center = map.Boards[0].CoreCells().ElementAt(4);
        board.Place(center, a, PieceType.Basic);
        foreach (Coord n in board.LibertyNeighbors(center))
        {
            board.Place(n, b, PieceType.Basic);
        }

        Group group = board.GroupAt(center)!;
        Assert.Empty(board.LibertiesOf(group));
        Assert.True(board.IsCaptured(group));

        // 崖壁（D3）：平台外圈非坡道格与朝外的低地格几何相邻，但没有气边；坡道格与朝外格之间有气边。
        BoardPlate plate = map.Boards[0];
        Dictionary<Coord, Coord> outlets = Outlets(map, plate);
        Coord ramp = outlets.Keys.First();
        Assert.Contains(outlets[ramp], Adjacency.LibertyNeighbors(map, ramp));
        Coord wall = plate.Cells().First(c => plate.OnRim(c) && map.IsPlayable(c) && map.HeightAt(c) == 2 && c.Y == plate.Origin.Y && c.X > plate.Origin.X && c.X < plate.Origin.X + plate.Width - 1 && map.IsPlayable(new Coord(c.X, c.Y - 1)));
        var below = new Coord(wall.X, wall.Y - 1);
        Assert.True(map.IsPlayable(below) && map.HeightAt(below) == 0);
        Assert.True(Adjacency.AreAdjacent(wall, below));
        Assert.DoesNotContain(below, Adjacency.LibertyNeighbors(map, wall));
    }

    /// <summary>平台的坡道 → 朝外格（坐标算术，测试内独立实现）。</summary>
    private static Dictionary<Coord, Coord> Outlets(MapData map, BoardPlate p)
    {
        int l = p.Origin.X, r = p.Origin.X + p.Width - 1, b = p.Origin.Y, t = p.Origin.Y + p.Height - 1;
        return Ramps(map, p).ToDictionary(
            c => c,
            c => c.X == l ? new Coord(c.X - 1, c.Y) : c.X == r ? new Coord(c.X + 1, c.Y) : c.Y == b ? new Coord(c.X, c.Y - 1) : new Coord(c.X, c.Y + 1));
    }
}
