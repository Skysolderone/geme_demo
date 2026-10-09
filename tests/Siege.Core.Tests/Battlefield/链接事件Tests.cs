using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.Battlefield;

/// <summary>规格：openspec/changes/board-link-mode/specs/battlefield —— Requirement: 链接事件（段 A 只覆盖地图数据层：合并、不动已有格、可重放；日志与存档在段 B）</summary>
/// <remarks>
/// 变异验证（段 A 实跑，只跑本类）：
/// M-A4 LinkMapGenerator.Link 不把 MapVersion +1 → 「链接合并战场」与「GameBoard 换图不动占用与改造」红；
/// M-A5 GameBoard.LinkMismatch 删去"已可落子的格仍可落子"核对 → 「ApplyLink 拒绝改写已有格的地图」红。
/// </remarks>
public class 链接事件Tests
{
    [Fact]
    public void 链接合并战场()
    {
        // Scenario：对战场 0 与 2 执行链接 → 战场数减 1，原战场 0 与 2 的全部格、新公共棋盘与两条通道属于同一个战场，标识为 0。
        MapData initial = LinkMapGenerator.Generate(3);
        Battlefields before = Battlefields.Of(initial);
        LinkResult result = LinkMapGenerator.Link(initial, 0, 2);
        Battlefields after = Battlefields.Of(result.Map);

        Assert.Equal(before.Count - 1, after.Count);
        Siege.Core.Board.Battlefield merged = after.ById(0)!;
        Assert.Null(after.ById(2));
        Assert.Equal([0, 2], merged.BirthZones);
        HashSet<Coord> expected = [.. before.ById(0)!.Cells, .. before.ById(2)!.Cells, .. result.Corridors.SelectMany(c => c.Cells())];
        if (result.PublicBoard is { } plate)
        {
            expected.UnionWith(plate.Cells());
        }

        Assert.Equal(expected.Order(), merged.Cells);
        Assert.Equal(initial.MapVersion + 1, result.Map.MapVersion);
        Assert.Same(initial.TerrainData, result.Map.TerrainData);

        // 其余三个战场逐格不变。
        foreach (int id in new[] { 1, 3, 4 })
        {
            Assert.Equal(before.ById(id)!.Cells, after.ById(id)!.Cells);
        }
    }

    [Fact]
    public void 链接不动已有格()
    {
        // Scenario：链接前记录全部已物化格的地形、占用、设施与信物，然后执行链接 → 这些格的记录逐项不变，只新增了格。
        MapData initial = LinkMapGenerator.Generate(2);
        GameBoard board = GameBoard.Load(initial);
        var p0 = new PlayerId(0);
        Coord stone = initial.RelicCells.Keys.First(initial.BirthZones[0].Contains);
        board.Place(stone, p0, PieceType.Fortress);
        Coord edgeA = initial.Boards[0].Origin;
        board.ApplyTerrainEdits([TerrainEdit.Fence(edgeA, new Coord(edgeA.X + 1, edgeA.Y))]);

        Dictionary<Coord, string> recorded = Snapshot(board);
        string serialized = board.Serialize();

        // 链接在"开局 + 链接"的地图上增长（栅栏是对局状态，不进地图数据），换图后改造重新落在新图上。
        Assert.Same(initial, board.LinkedMap);
        Assert.NotSame(initial, board.Map);
        LinkResult result = LinkMapGenerator.Link(board.LinkedMap, 0, 4);
        board.ApplyLink(result.Map);
        Assert.Same(result.Map, board.LinkedMap);
        Assert.True(board.Map.HasFence(edgeA, new Coord(edgeA.X + 1, edgeA.Y)));

        Dictionary<Coord, string> after = Snapshot(board);
        foreach ((Coord c, string record) in recorded)
        {
            Assert.Equal(record, after[c]);
        }

        Assert.True(after.Count > recorded.Count);
        Assert.Equal(recorded.Count + result.Corridors.Sum(c => c.Width * c.Height) + (result.PublicBoard is { } p ? p.Width * p.Height : 0), after.Count);
        Assert.Equal(serialized, board.Serialize());          // 占用与改造段逐字节相同：画布不变、已有格不变
        Assert.Single(board.TerrainEdits);
        Assert.Equal(new Occupant(p0, PieceType.Fortress), board[stone].Occupant);
        Assert.Equal(1, board.Map.MapVersion);
        Assert.Equal(1, board.LinkedMap.MapVersion);
        Assert.Same(initial, board.BaseMap);

        // 副本带着同一份链接后的地图。
        GameBoard clone = board.Clone();
        Assert.Same(board.LinkedMap, clone.LinkedMap);
        Assert.Same(board.Map, clone.Map);
    }

    [Fact]
    public void 链接可重放()
    {
        // Scenario：按记录的链接事件顺序在同一地图种子上重新执行 → 每次链接后的地图与原局逐格相同（两次独立执行逐字节相同）。
        (int, int)[] steps = [(0, 3), (1, 4), (0, 1)];
        List<string> first = Replay(5, steps);
        List<string> second = Replay(5, steps);
        Assert.Equal(first, second);
        Assert.Equal(steps.Length, first.Count);
        Assert.Equal(first.Count, first.Distinct().Count());   // 每一步都确实改变了地图

        // 规格「链接档地图标识与确定性」Scenario 同链接序列同最终图：link:12345 上两次都依次链接 (0,3)、(1,4)、(0,1) → 最终地图逐格相同。
        Assert.Equal(Replay(12345, steps)[^1], Replay(12345, steps)[^1]);

        // 链接次序不同 → 中间地图可以不同，但 (X, Y) 与 (Y, X) 的几何相同（随机源按升序归一）。
        Assert.Equal(MapFile.ToJson(LinkMapGenerator.Link(LinkMapGenerator.Generate(5), 3, 0).Map), MapFile.ToJson(LinkMapGenerator.Link(LinkMapGenerator.Generate(5), 0, 3).Map));
    }

    [Fact]
    public void ApplyLink拒绝改写已有格的地图()
    {
        // GameBoard.ApplyLink 只接受"只增不改"的扩展：画布、标识、版本号、地形、出生区、信物、已有棋盘、已可落子格任一不符即拒绝，地图不变。
        MapData initial = LinkMapFixtures.Initial();
        GameBoard board = GameBoard.Load(initial);
        MapData linked = LinkMapFixtures.Linked01();

        board.ApplyLink(linked);                                   // 合规扩展
        Assert.Same(linked, board.Map);
        Assert.Same(linked, board.LinkedMap);
        Assert.Equal(4, board.Battlefields.Count);

        GameBoard fresh() => GameBoard.Load(initial);
        Assert.Throws<SiegeRuleException>(() => fresh().ApplyLink(linked with { MapVersion = 2 }));
        Assert.Throws<SiegeRuleException>(() => fresh().ApplyLink(linked with { Id = "other" }));
        Assert.Throws<SiegeRuleException>(() => fresh().ApplyLink(linked with { Width = linked.Width + 1 }));
        Assert.Throws<SiegeRuleException>(() => fresh().ApplyLink(linked with { Obstacles = linked.Obstacles.Add(new Coord(2, 2)) }));   // 已可落子的格变障碍
        Assert.Throws<SiegeRuleException>(() => fresh().ApplyLink(linked with { Boards = [linked.Boards[5], .. linked.Boards.Take(5)] }));
        Assert.Throws<SiegeRuleException>(() => fresh().ApplyLink(linked with { RelicCells = linked.RelicCells.Remove(new Coord(2, 2)) }));
        Assert.Throws<SiegeRuleException>(() => fresh().ApplyLink(linked with { TerrainData = TestMaps.Terrain(fences: [("B2", "C2")]) }));

        // 拒绝后地图不变。
        GameBoard rejected = fresh();
        Assert.Throws<SiegeRuleException>(() => rejected.ApplyLink(linked with { MapVersion = 2 }));
        Assert.Same(initial, rejected.Map);
    }

    private static List<string> Replay(ulong seed, (int X, int Y)[] steps)
    {
        var maps = new List<string>();
        MapData map = LinkMapGenerator.Generate(seed);
        foreach ((int x, int y) in steps)
        {
            map = LinkMapGenerator.Link(map, x, y).Map;
            maps.Add(MapFile.ToJson(map));
        }

        return maps;
    }

    private static Dictionary<Coord, string> Snapshot(GameBoard board) =>
        board.AllCoords().Where(board.Map.IsPlayable).ToDictionary(
            c => c,
            c => $"{board[c].Terrain}|{board.Map.HeightAt(c)}|{board.Map.SurfaceAt(c)}|{board.Map.HasBridge(c)}|{board[c].BirthZone}|{board[c].IsRelicCell}|{board[c].Occupant}|"
                 + string.Join(",", board.Neighbors(c).Where(n => board.Map.HasFence(c, n)).Select(n => n.ToNotation())));
}
