using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Core.Relics;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.Battlefield;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/battlefield —— Requirement: 链接事件（段 B 前半：对局层执行——段 A 接线契约、公共棋盘信物（实现裁决 B6）、
/// 链接不动已有格（含棋子与改造））。地图数据层的同名测试在 <c>链接事件Tests</c>。
/// </summary>
/// <remarks>
/// 变异验证（段 B 前半实跑）：
/// M-B4 <c>GameSeed.RelicLink</c> 的子流名改为含链接序号（<c>$"relic-link:{x}:{y}:{_links.Count}"</c> 的等价写法：在 <c>ExecuteLink</c> 里改传 <c>x + _links.Count</c>）
/// → 「信物子流独立」红 1；
/// M-B9 <c>ExecuteLink</c> 改对 <c>Board.Map</c>（含改造）而不是 <c>Board.LinkedMap</c> 调生成器 → 「链接不动已有格」红 1（含栅栏的地图过不了链接档静态校验）。
/// </remarks>
public class 链接执行Tests
{
    [Fact]
    public void 链接不动已有格()
    {
        // Scenario：链接前记录全部已物化格的地形、占用、设施与信物，然后执行链接 → 这些格的记录逐项不变，只新增了格。
        MatchFlow match = Started([0, 1, 3, 4]);
        MapData map = match.Map;
        Coord stoneA = BirthCell(map, 0, 1, 1);
        Coord stoneB = BirthCell(map, 3, 2, 2);
        match.Place(P0, stoneA).Place(P2, stoneB);
        Coord fenceFrom = BirthCell(map, 1, 0, 0);
        match.Board.ApplyTerrainEdits([TerrainEdit.Fence(fenceFrom, new Coord(fenceFrom.X + 1, fenceFrom.Y))]);
        match.PassRounds(3);

        Dictionary<Coord, string> before = Snapshot(match);
        string relicsBefore = RelicText(match, before.Keys);
        int playableBefore = match.Map.PlayableCount;

        match.ApplyPairing([(0, 3), (1, 4)]);

        Dictionary<Coord, string> after = Snapshot(match);
        Assert.All(before, kv => Assert.Equal(kv.Value, after[kv.Key]));
        Assert.Equal(relicsBefore, RelicText(match, before.Keys));
        Assert.True(match.Map.PlayableCount > playableBefore);
        Assert.Equal(P0, match.Board[stoneA].Occupant!.Value.Owner);
        Assert.True(match.Map.HasFence(fenceFrom, new Coord(fenceFrom.X + 1, fenceFrom.Y)));
        Assert.Equal(2, match.Board.LinkedMap.MapVersion);
        Assert.Same(LinkModeFixtures.Map, match.Board.BaseMap);   // 开局图不变（段 A 接线契约 3）
    }

    [Fact]
    public void 公共棋盘信物由链接子流生成并追加进账本()
    {
        MatchFlow match = Started([0, 1, 3, 4]).PassRounds(3);
        int relicsBefore = match.Relics.Coords.Count();
        match.ApplyPairing([(0, 3), (1, 4)]);

        Assert.Equal(match.Links.Sum(l => l.NewRelicCells.Length), match.Relics.LinkedPlacements.Count);
        Assert.Equal(relicsBefore + match.Relics.LinkedPlacements.Count, match.Relics.Coords.Count());
        Assert.Equal(match.Map.RelicCells.Keys.Order(), match.Relics.Coords);
        foreach (LinkRecord link in match.Links)
        {
            Assert.InRange(link.NewRelicCells.Length, 2, 3);       // 公共棋盘按边长 2–3 枚（D2）
        }

        // 新信物未揭示、无人控制。
        Assert.All(match.Relics.LinkedPlacements, p => Assert.False(match.Relics.IsRevealed(p.Coord)));
        Assert.All(match.Relics.LinkedPlacements, p => Assert.Equal(RelicZone.Contested, p.Spec.Zone));
    }

    [Fact]
    public void 信物子流独立()
    {
        // 实现裁决 B6：同一对 (X, Y) 无论先链接还是后链接，公共棋盘信物相同；出生棋盘信物逐格相同。
        MatchFlow first = Started([0, 1, 3, 4]).PassRounds(3);
        first.Debug.Link(0, 3);
        first.Debug.Link(1, 4);
        MatchFlow second = Started([0, 1, 3, 4]).PassRounds(3);
        second.Debug.Link(1, 4);
        second.Debug.Link(0, 3);

        Assert.Equal(PairRelics(first, 0, 3), PairRelics(second, 0, 3));
        Assert.Equal(PairRelics(first, 1, 4), PairRelics(second, 1, 4));
        Assert.Equal(
            string.Join(";", first.Relics.Generation.Placements.Select(p => p.ToString())),
            string.Join(";", second.Relics.Generation.Placements.Select(p => p.ToString())));
    }

    [Fact]
    public void 信物内容只由对局种子与战场对决定()
    {
        // 同一地图、不同对局种子 → 同一组信物格，内容来自各自的 relic-link 子流（钉住：与手算的子流名一致的生成结果相同）。
        MatchFlow match = Started([0, 1, 3, 4]).PassRounds(3);
        match.ApplyPairing([(0, 3), (1, 4)]);
        LinkRecord link = match.Links[0];
        IEnumerable<KeyValuePair<Coord, RelicCellSpec>> cells = link.NewRelicCells.Select(c => KeyValuePair.Create(c, match.Map.RelicCells[c]));
        Assert.Equal(
            RelicGenerator.GenerateLinked(cells, MatchFixtures.Seed, 3, 0, match.ContentSet).Select(p => p.ToString()),
            PairRelics(match, 0, 3));
        Assert.Equal("relic-link:0:3", GameSeed.RelicLink(3, 0));
    }

    private static IEnumerable<string> PairRelics(MatchFlow match, int x, int y)
    {
        LinkRecord link = match.Links.Single(l => l.X == x && l.Y == y);
        return [.. match.Relics.LinkedPlacements.Where(p => link.NewRelicCells.Contains(p.Coord)).Select(p => p.ToString())];
    }

    private static Dictionary<Coord, string> Snapshot(MatchFlow match)
    {
        GameBoard board = match.Board;
        return board.AllCoords().Where(board.Map.IsPlayable).ToDictionary(
            c => c,
            c => $"{board[c].Terrain}|{board.Map.HeightAt(c)}|{board.Map.SurfaceAt(c)}|{board.Map.HasBridge(c)}|{board[c].BirthZone}|{board[c].IsRelicCell}|{board[c].Occupant}|"
                 + string.Join(",", board.Neighbors(c).Where(n => board.Map.HasFence(c, n)).Select(n => n.ToNotation())));
    }

    private static string RelicText(MatchFlow match, IEnumerable<Coord> cells) =>
        string.Join(";", match.Relics.PublicStates().Where(s => cells.Contains(s.Coord)).Select(s => $"{s.Coord}:{s.IsRevealed}:{s.Control}"));
}
