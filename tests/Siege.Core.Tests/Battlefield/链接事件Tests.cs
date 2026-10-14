using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.Battlefield;

/// <summary>规格：openspec/changes/board-link-mode/specs/battlefield —— Requirement: 链接事件（地图数据层：合并、不改内容、可重放；对局层见 <c>链接执行Tests</c>）</summary>
/// <remarks>
/// 变异验证（段 F 实跑，只跑本类）：
/// M-F10 <c>GameBoard.ApplySplice</c> 不迁移改造列表（<c>edits</c> 直接取 <c>_edits</c>）→ 「链接不改内容」红（栅栏留在旧坐标）；
/// M-F11 <c>GameBoard.SpliceTargets</c> 删去"可落子格总数不变"核对 → 「ApplySplice拒绝不是逐格搬运的地图」红 1。
/// </remarks>
public class 链接事件Tests
{
    [Fact]
    public void 链接合并战场()
    {
        // Scenario：对战场 0 与 2 执行链接 → 战场数减 1，原战场 0 与 2 的全部格属于同一个战场、沿接缝互为邻居，标识为 0。
        MapData initial = LinkMapGenerator.Generate(3);
        Battlefields before = Battlefields.Of(initial);
        LinkResult result = LinkMapGenerator.Link(initial, 0, 2, LinkSide.East);
        Battlefields after = Battlefields.Of(result.Map);

        Assert.Equal(before.Count - 1, after.Count);
        Siege.Core.Board.Battlefield merged = after.ById(0)!;
        Assert.Null(after.ById(2));
        Assert.Equal([0, 2], merged.BirthZones);
        BattlefieldShift shift = result.Shifts.Single(s => s.Battlefield == 2);
        HashSet<Coord> expected = [.. before.ById(0)!.Cells, .. before.ById(2)!.Cells.Select(c => new Coord(c.X + shift.Dx, c.Y + shift.Dy))];
        Assert.Equal(expected.Order(), merged.Cells);
        Assert.Equal(initial.MapVersion + 1, result.Map.MapVersion);
        // link-map-terrain：棋盘带预置地形，拼接按棋盘整体搬运——栅栏 / 高度 / 地表 / 桥都随所在棋盘的向量平移（测试内按"格所在棋盘 → 所属战场 → 向量"独立换算）。
        Coord Moved(Coord c)
        {
            int board = initial.Boards.IndexOf(initial.Boards.First(p => p.Contains(c)));
            BattlefieldShift s = result.Shifts.Single(x => x.Battlefield == before.All.First(f => f.Boards.Contains(board)).Id);
            return new Coord(c.X + s.Dx, c.Y + s.Dy);
        }

        Assert.NotEmpty(initial.TerrainData.Fences);   // 样本口径：link:3 有栅栏阵
        Assert.True(result.Map.TerrainData.Fences.SetEquals(initial.TerrainData.Fences.Select(e => new FenceEdge(Moved(e.A), Moved(e.B)))));
        Assert.Equal(initial.TerrainData.Surfaces.Select(kv => (Moved(kv.Key), kv.Value)).Order(), result.Map.TerrainData.Surfaces.Select(kv => (kv.Key, kv.Value)).Order());
        Assert.Equal(initial.TerrainData.Heights.Select(kv => (Moved(kv.Key), kv.Value)).Order(), result.Map.TerrainData.Heights.Select(kv => (kv.Key, kv.Value)).Order());
        Assert.Equal(initial.TerrainData.Bridges.Select(Moved).Order(), result.Map.TerrainData.Bridges.Order());

        // 接缝：锚点最东列上至少一格的东邻是被贴方的格。
        int seam = before.ById(0)!.Cells.Max(c => c.X);
        Assert.Contains(merged.Cells, c => c.X == seam && merged.Cells.Contains(new Coord(c.X + 1, c.Y)));

        // 其余三个战场各按自己的向量（只向东平移、底边不动）整体移动，格数不变。
        foreach (int id in new[] { 1, 3, 4 })
        {
            BattlefieldShift other = result.Shifts.Single(s => s.Battlefield == id);
            Assert.Equal(0, other.Dy);
            Assert.Equal(before.ById(id)!.Cells.Select(c => new Coord(c.X + other.Dx, c.Y)).Order(), after.ById(id)!.Cells);
        }
    }

    [Fact]
    public void 链接不改内容()
    {
        // Scenario：链接前记录两个战场全部格的地形、占用、设施与信物，然后执行链接 → 锚点战场逐格不变；被平移战场每一格的记录出现在平移后的坐标上、逐项不变；可落子格总数不变。
        MapData initial = LinkMapGenerator.Generate(2);
        GameBoard board = GameBoard.Load(initial);
        var p0 = new PlayerId(0);
        var p4 = new PlayerId(4);
        Coord anchorStone = initial.RelicCells.Keys.First(initial.BirthZones[0].Contains);
        Coord moverStone = initial.RelicCells.Keys.First(initial.BirthZones[4].Contains);
        board.Place(anchorStone, p0, PieceType.Fortress);
        board.Place(moverStone, p4, PieceType.Chain);
        Coord edgeA = initial.Boards[0].Origin;
        Coord edgeM = initial.Boards[4].Origin;
        board.ApplyTerrainEdits([TerrainEdit.Fence(edgeA, new Coord(edgeA.X + 1, edgeA.Y)), TerrainEdit.Fence(edgeM, new Coord(edgeM.X, edgeM.Y + 1))]);

        Dictionary<Coord, string> recorded = Snapshot(board);
        int playableBefore = board.Map.PlayableCount;

        // 链接在"开局 + 链接"的地图上拼接（栅栏是对局状态，不进地图数据），换图后改造按同一映射重新落在新图上。
        Assert.Same(initial, board.LinkedMap);
        Assert.NotSame(initial, board.Map);
        LinkResult result = LinkMapGenerator.Link(board.LinkedMap, 0, 4, LinkSide.East);
        board.ApplySplice(result.Map, result.Translate);
        Assert.Same(result.Map, board.LinkedMap);

        Dictionary<Coord, string> after = Snapshot(board);
        Assert.Equal(recorded.Count, after.Count);
        Assert.Equal(playableBefore, board.Map.PlayableCount);
        foreach ((Coord c, string record) in recorded)
        {
            Coord moved = result.Translate(c);
            Assert.Equal(record, after[moved]);
            if (Battlefields.Of(initial).At(c)!.Id == 0)
            {
                Assert.Equal(c, moved);   // 锚点逐格不变
            }
        }

        BattlefieldShift shift = result.Shifts.Single(s => s.Battlefield == 4);
        Assert.False(shift.IsZero);
        Assert.Equal(new Occupant(p0, PieceType.Fortress), board[anchorStone].Occupant);
        Assert.Equal(new Occupant(p4, PieceType.Chain), board[new Coord(moverStone.X + shift.Dx, moverStone.Y + shift.Dy)].Occupant);
        Assert.True(board.Map.HasFence(edgeA, new Coord(edgeA.X + 1, edgeA.Y)));
        Assert.True(board.Map.HasFence(new Coord(edgeM.X + shift.Dx, edgeM.Y + shift.Dy), new Coord(edgeM.X + shift.Dx, edgeM.Y + shift.Dy + 1)));
        Assert.Equal(2, board.TerrainEdits.Length);
        Assert.Equal(1, board.Map.MapVersion);
        Assert.Equal(1, board.LinkedMap.MapVersion);
        Assert.Same(initial, board.BaseMap);
        Assert.Equal(2, board.AllGroups().Length);

        // 副本带着同一份链接后的地图与盘面。
        GameBoard clone = board.Clone();
        Assert.Same(board.LinkedMap, clone.LinkedMap);
        Assert.Same(board.Map, clone.Map);
        Assert.Equal(board.Serialize(), clone.Serialize());
    }

    [Fact]
    public void 接缝两侧朝间隙的边界栅栏合并为一条()
    {
        // R5 下匠人可以在棋盘边缘朝场景格立栅（合法无效果）。锚点东缘朝东、被贴方西缘朝西的两道栅栏在东贴后落成同一条接缝边：
        // 只保留先出现的一条（否则 TerrainWriter 对重复边抛"该边已有栅栏"，配对事件中途崩溃），它随之成为有效的接缝栅栏（J8-4）。
        MapData initial = LinkMapGenerator.Generate(2);
        LinkResult result = LinkMapGenerator.Link(initial, 0, 4, LinkSide.East);
        BattlefieldShift shift = result.Shifts.Single(s => s.Battlefield == 4);
        BoardPlate a = initial.Boards[0];
        BoardPlate b = initial.Boards[4];
        int rowA = a.Origin.Y + 1;
        var eastEdge = new Coord(a.Origin.X + a.Width - 1, rowA);
        var westEdge = new Coord(b.Origin.X, rowA - shift.Dy);   // 平移后与 eastEdge 同一行
        Assert.True(b.Contains(westEdge));

        GameBoard board = GameBoard.Load(initial);
        board.ApplyTerrainEdits([TerrainEdit.Fence(eastEdge, new Coord(eastEdge.X + 1, eastEdge.Y)), TerrainEdit.Fence(westEdge, new Coord(westEdge.X - 1, westEdge.Y))]);
        Assert.Equal(2, board.TerrainEdits.Length);
        board.ApplySplice(result.Map, result.Translate);

        Coord seamEast = new(eastEdge.X + 1, rowA);
        Assert.Equal(seamEast, result.Translate(westEdge));
        Assert.Equal([TerrainEdit.Fence(eastEdge, seamEast)], board.TerrainEdits);
        Assert.True(board.Map.HasFence(eastEdge, seamEast));
        Assert.DoesNotContain(seamEast, board.LibertyNeighbors(eastEdge));   // 变成了有效的接缝栅栏
        Assert.Contains(seamEast, board.Neighbors(eastEdge));
    }

    [Fact]
    public void 链接可重放()
    {
        // Scenario：按记录的链接事件顺序在同一地图种子上重新执行 → 每次链接后的地图与原局逐格相同（两次独立执行逐字节相同）。
        LinkStep[] steps = [new(0, 3, LinkSide.East), new(1, 4, LinkSide.East), new(0, 1, LinkSide.North)];
        List<string> first = Replay(5, steps);
        List<string> second = Replay(5, steps);
        Assert.Equal(first, second);
        Assert.Equal(steps.Length, first.Count);
        Assert.Equal(first.Count, first.Distinct().Count());   // 每一步都确实改变了地图

        // 规格「链接档地图标识与确定性」Scenario 同链接序列同最终图：link:12345 上两次都依次链接 (0,3)、(1,4)、(0,1) → 最终地图逐格相同。
        Assert.Equal(Replay(12345, steps)[^1], Replay(12345, steps)[^1]);

        // (X, Y) 与 (Y, X) 的拼接相同（锚点恒为标识较小者）；贴边不同则不同。
        Assert.Equal(MapFile.ToJson(LinkMapGenerator.Link(LinkMapGenerator.Generate(5), 3, 0, LinkSide.East).Map), MapFile.ToJson(LinkMapGenerator.Link(LinkMapGenerator.Generate(5), 0, 3, LinkSide.East).Map));
        Assert.NotEqual(MapFile.ToJson(LinkMapGenerator.Link(LinkMapGenerator.Generate(5), 0, 3, LinkSide.North).Map), MapFile.ToJson(LinkMapGenerator.Link(LinkMapGenerator.Generate(5), 0, 3, LinkSide.East).Map));
    }

    [Fact]
    public void 河上架的桥随棋盘平移()
    {
        // link-map-terrain 第 2 组：河流主题的未架桥深水是搭桥目标；对局中架的桥（改造列表）在拼接时按所在棋盘平移。
        // 深水格不可落子、不属于任何战场，拼接映射（LinkResult.Translate）对棋盘内的不可落子格按所在棋盘给出向量，与 LinkLayout 同口径；
        // 改用"只接受可落子格"的旧映射时，这里在 ApplySplice 迁移改造时抛 ArgumentException。
        MapData initial = LinkMapGenerator.Generate(1);
        int river = initial.Boards.IndexOf(initial.Boards.Single(b => b.Theme == BoardTheme.River));
        Coord water = initial.Boards[river].Cells().First(initial.TerrainData.IsUnbridgedDeepWater);
        Coord shore = initial.Boards[river].Cells().First(c => initial.IsPlayable(c) && Adjacency.AreAdjacent(c, water));
        int anchor = river == 0 ? 1 : 0;
        GameBoard board = GameBoard.Load(initial);
        board.ApplyTerrainEdits([TerrainEdit.Bridge(water), TerrainEdit.Fence(water, shore)]);
        Assert.True(board.Map.IsPlayable(water));
        // 桥上有棋子：桥格只在改造后的地图上可落子（拼接搬的是不含改造的 LinkedMap），盘面迁移不能只认 LinkedMap 的可落子格。
        // 第 2 组实测：真实跑局（link:1、对局种子 3）第 27 大回合拼接时在这里抛"有棋子却不是当前地图的可落子格"。
        board.Place(water, new PlayerId(3), PieceType.Basic);

        LinkResult result = LinkMapGenerator.Link(board.LinkedMap, anchor, river, LinkSide.East);
        board.ApplySplice(result.Map, result.Translate);

        BattlefieldShift shift = result.Shifts.Single(s => s.Battlefield == river);
        var moved = new Coord(water.X + shift.Dx, water.Y + shift.Dy);
        var movedShore = new Coord(shore.X + shift.Dx, shore.Y + shift.Dy);
        Assert.True(shift.Dx != 0 || shift.Dy != 0, "样本口径：河流棋盘确实被平移");
        Assert.True(board.Map.HasBridge(moved));
        Assert.True(board.Map.IsPlayable(moved));
        Assert.True(board.Map.HasFence(moved, movedShore));
        Assert.Equal(new Occupant(new PlayerId(3), PieceType.Basic), board[moved].Occupant);
        Assert.False(board.LinkedMap.HasBridge(moved));   // 静态地形里仍是未架桥深水，桥只在改造列表里
        Assert.Equal(result.Map.PlayableCount + 1, board.Map.PlayableCount);
    }

    [Fact]
    public void ApplySplice拒绝不是逐格搬运的地图()
    {
        // GameBoard.ApplySplice 只接受"逐格搬过去"的新图：标识、版本号、每个可落子格的落点、可落子格总数、地形、出生区、信物任一不符即拒绝，盘面不变。
        MapData initial = LinkMapFixtures.Initial();
        LinkResult result = LinkMapGenerator.Link(initial, 0, 1, LinkSide.East);
        MapData linked = result.Map;
        Assert.Equal(MapFile.ToJson(LinkMapFixtures.Linked01()), MapFile.ToJson(linked));

        GameBoard board = GameBoard.Load(initial);
        board.ApplySplice(linked, result.Translate);                   // 合规拼接
        Assert.Same(linked, board.Map);
        Assert.Same(linked, board.LinkedMap);
        Assert.Equal(4, board.Battlefields.Count);

        GameBoard fresh() => GameBoard.Load(initial);
        Assert.Throws<SiegeRuleException>(() => fresh().ApplySplice(linked with { MapVersion = 2 }, result.Translate));
        Assert.Throws<SiegeRuleException>(() => fresh().ApplySplice(linked with { Id = "other" }, result.Translate));
        Assert.Throws<SiegeRuleException>(() => fresh().ApplySplice(linked, c => c));                                              // 恒等映射：被平移方落到障碍格
        Assert.Throws<SiegeRuleException>(() => fresh().ApplySplice(linked, c => result.Translate(initial.Boards[0].Contains(c) && c.X == 2 ? new Coord(3, c.Y) : c)));   // 两格映射到同一格
        Assert.Throws<SiegeRuleException>(() => fresh().ApplySplice(linked with { Obstacles = linked.Obstacles.Add(new Coord(3, 3)) }, result.Translate));             // 已可落子的格变障碍
        Assert.Throws<SiegeRuleException>(() => fresh().ApplySplice(linked with { RelicCells = linked.RelicCells.Remove(new Coord(3, 3)) }, result.Translate));
        Assert.Throws<SiegeRuleException>(() => fresh().ApplySplice(linked with { TerrainData = TestMaps.Terrain(fences: [("D4", "E4")]) }, result.Translate));
        Assert.Throws<SiegeRuleException>(() => fresh().ApplySplice(linked with { Obstacles = linked.Obstacles.Remove(new Coord(15, 4)) }, result.Translate));           // 新图多出一个可落子格（棋盘 2 与 3 之间的场景格）

        // 拒绝后地图不变。
        GameBoard rejected = fresh();
        Assert.Throws<SiegeRuleException>(() => rejected.ApplySplice(linked with { MapVersion = 2 }, result.Translate));
        Assert.Same(initial, rejected.Map);
    }

    private static List<string> Replay(ulong seed, LinkStep[] steps)
    {
        var maps = new List<string>();
        MapData map = LinkMapGenerator.Generate(seed);
        foreach (LinkStep step in steps)
        {
            map = LinkMapGenerator.Link(map, step.X, step.Y, step.Side).Map;
            maps.Add(MapFile.ToJson(map));
        }

        return maps;
    }

    private static Dictionary<Coord, string> Snapshot(GameBoard board) =>
        board.AllCoords().Where(board.Map.IsPlayable).ToDictionary(
            c => c,
            c => $"{board[c].Terrain}|{board.Map.HeightAt(c)}|{board.Map.SurfaceAt(c)}|{board.Map.HasBridge(c)}|{board[c].BirthZone}|{board[c].IsRelicCell}|{board[c].Occupant}|"
                 + string.Join(",", board.Neighbors(c).Where(n => board.Map.HasFence(c, n)).Select(n => $"{n.X - c.X},{n.Y - c.Y}")));   // 栅栏按相对方向记，平移后可比
}
