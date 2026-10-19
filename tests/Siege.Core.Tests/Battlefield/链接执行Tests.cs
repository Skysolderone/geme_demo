using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Core.Relics;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.Battlefield;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/battlefield —— Requirement: 链接事件（对局层执行：段 A 接线契约 + 段 F 拼接——占用、改造、信物账本按同一映射迁移）。
/// 地图数据层的同名测试在 <c>链接事件Tests</c>。
/// </summary>
/// <remarks>
/// 变异验证（段 F 实跑，只跑本类）：
/// M-F12 <c>MatchFlow.ExecuteLink</c> 不调 <c>Relics.Translate</c> → 「链接不改内容」红（已揭示信物留在旧坐标，新坐标不是信物格）；
/// M-F13 <c>ResolvePairing</c> 决赛也贴东侧 → 「决赛贴北侧」红；
/// M-B9 <c>ExecuteLink</c> 改对 <c>Board.Map</c>（含改造）而不是 <c>Board.LinkedMap</c> 调生成器 → 「链接不改内容」红（含栅栏的地图过不了链接档静态校验）。
/// </remarks>
public class 链接执行Tests
{
    [Fact(Skip = "open-map 段 B 删除")]
    public void 链接不改内容()
    {
        // Scenario：被平移战场在链接前有 3 枚棋子、1 道栅栏、1 枚已揭示信物 → 链接后这些内容出现在平移后的对应格，类型、归属、揭示状态逐项相同；锚点战场逐格不变。
        MatchFlow match = Started([0, 1, 3, 4]);
        MapData map = match.Map;
        Coord relic = map.RelicCells.Keys.Where(map.BirthZones[3].Contains).Order().First();
        Coord stoneB = BirthCell(map, 3, 0, 0);
        Coord stoneC = BirthCell(map, 3, 2, 0);
        match.Place(P2, relic, stoneB, stoneC);
        Coord anchorStone = BirthCell(map, 0, 1, 1);
        match.Place(P0, anchorStone);
        Coord fenceFrom = BirthCell(map, 3, 0, 1);
        match.Board.ApplyTerrainEdits([TerrainEdit.Fence(fenceFrom, new Coord(fenceFrom.X + 1, fenceFrom.Y))]);
        match.Relics.Reveal(match.Board, match.MajorRound);
        match.Debug.Recalculate();
        Assert.True(match.Relics.IsRevealed(relic));
        RelicContent content = match.Relics.PublicStateOf(relic).Content!.Value;
        Dictionary<Coord, string> anchorBefore = Snapshot(match, map.BirthBoard(0).Cells());   // link-map-terrain：整块锚点棋盘（棋盘清单），不只是出生区（核心区）
        match.PassRounds(3);

        match.ApplyPairing([(0, 3), (1, 4)]);

        // 第一次链接 (0,3)：锚点 0 不动、3 平移；第二次链接 (1,4) 按 J7 把其余战场（含刚合并出的 0）重排到合并战场东侧——所以 0 号战场的格要按两次链接的向量合成。
        LinkRecord link = match.Links[0];
        Assert.Equal((0, 3, LinkSide.East), (link.X, link.Y, link.Side));
        BattlefieldShift shift = link.ShiftOf(3)!.Value;
        Assert.False(shift.IsZero);
        Assert.True(link.ShiftOf(0)!.Value.IsZero);
        BattlefieldShift again = match.Links[1].ShiftOf(0)!.Value;
        Assert.Equal(0, again.Dy);
        Coord Moved(Coord c) => new(c.X + shift.Dx + again.Dx, c.Y + shift.Dy);
        Coord MovedAnchor(Coord c) => new(c.X + again.Dx, c.Y);

        foreach (Coord stone in new[] { relic, stoneB, stoneC })
        {
            Assert.Equal(new Occupant(P2, PieceType.Basic), match.Board[Moved(stone)].Occupant);
        }

        Assert.True(match.Map.HasFence(Moved(fenceFrom), Moved(new Coord(fenceFrom.X + 1, fenceFrom.Y))));
        Assert.True(match.Relics.IsRevealed(Moved(relic)));
        Assert.Equal(content, match.Relics.PublicStateOf(Moved(relic)).Content);
        Assert.Equal(match.Map.RelicCells.Keys.Order(), match.Relics.Coords);
        Assert.Equal(anchorBefore.ToDictionary(kv => MovedAnchor(kv.Key), kv => kv.Value), Snapshot(match, map.BirthBoard(0).Cells().Select(MovedAnchor)));
        Assert.Equal(new Occupant(P0, PieceType.Basic), match.Board[MovedAnchor(anchorStone)].Occupant);
        Assert.Equal(map.PlayableCount, match.Map.PlayableCount);
        Assert.Equal(2, match.Board.LinkedMap.MapVersion);
        Assert.Same(LinkModeFixtures.Map, match.Board.BaseMap);   // 开局图不变（段 A 接线契约 3）
        Assert.Equal(0, match.BattlefieldOf(P2));
        Assert.Equal(match.BattlefieldOf(P0), match.BattlefieldOf(P2));
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 链接合并战场()
    {
        // Scenario（对局层）：配对 (0,3)(1,4) 后 P0 与 P2 同战场、P1 与 P3 同战场，战场数 3，记录里每个战场各一条向量。
        MatchFlow match = Started([0, 1, 3, 4]).PassRounds(3);
        match.ApplyPairing([(0, 3), (1, 4)]);

        Assert.Equal(3, match.Board.Battlefields.Count);
        Assert.Equal(match.BattlefieldOf(P0), match.BattlefieldOf(P2));
        Assert.Equal(match.BattlefieldOf(P1), match.BattlefieldOf(P3));
        Assert.NotEqual(match.BattlefieldOf(P0), match.BattlefieldOf(P1));
        Assert.Equal(2, match.Links.Count);
        Assert.Equal([5, 4], match.Links.Select(l => l.Shifts.Length));   // 第二次链接时只剩 4 个战场

        // 按座位视图的占位取当前地图的出生棋盘（被平移方与为腾位置而移动的棋盘坐标已变），不是开局图（变异 M-F16：改读 BaseMap → 红）。
        // link-map-terrain：占位取整块棋盘的左下角（棋盘清单），不是出生区（核心区）的最小坐标。
        SeatPublicView view = match.PublishFor(P0);
        Assert.Equal(
            match.Map.BirthBoards().Select(b => b.Origin.ToNotation()),
            view.Placeholders.Select(p => p.Origin));
        Assert.NotEqual(
            match.Board.BaseMap.BirthBoards().Select(b => b.Origin.ToNotation()),
            view.Placeholders.Select(p => p.Origin));
        Assert.All(match.Links, l => Assert.True(l.SharedEdge >= 1));
        Assert.All(match.Links, l => Assert.Equal(64, l.MapDigest.Length));
        Assert.Equal(MapFile.Digest(match.Board.LinkedMap), match.Links[^1].MapDigest);
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 决赛贴北侧()
    {
        // 裁决 J1：首轮配对的链接贴东侧，决赛链接贴北侧，由阶段决定并传给生成器。
        MatchFlow match = Started([0, 1, 3, 4], new ScriptedPairingSource()).PassRounds(4);
        Assert.Equal(3, match.Links.Count);
        Assert.Equal([LinkSide.East, LinkSide.East, LinkSide.North], match.Links.Select(l => l.Side));
        Assert.Equal(PairingKind.Final, match.Pairings[1].Kind);
        Assert.Equal(2, match.Board.Battlefields.Count);
        Assert.Equal([0, 1, 3, 4], match.Board.Battlefields.ById(0)!.BirthZones);
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 链接可重放()
    {
        // Scenario（对局层）：两局同样的操作序列 → 每次链接后地图逐格相同、盘面序列化相同、链接记录相同。
        MatchFlow a = Started([0, 1, 3, 4], new ScriptedPairingSource()).PassRounds(4);
        MatchFlow b = Started([0, 1, 3, 4], new ScriptedPairingSource()).PassRounds(4);
        Assert.Equal(MapFile.ToJson(a.Map), MapFile.ToJson(b.Map));
        Assert.Equal(a.Board.Serialize(), b.Board.Serialize());
        Assert.Equal(a.Links.Select(Text), b.Links.Select(Text));
        Assert.Equal(a.Relics.PublicStates().Select(s => s.ToString()), b.Relics.PublicStates().Select(s => s.ToString()));
    }

    internal static string Text(LinkRecord l) =>
        $"{l.Sequence}|{l.PairingSequence}|{l.X}|{l.Y}|{l.Side}|{l.MapVersion}|{l.SharedEdge}|{l.MapDigest}|{string.Join(" ", l.Shifts)}";

    private static Dictionary<Coord, string> Snapshot(MatchFlow match, IEnumerable<Coord> cells)
    {
        GameBoard board = match.Board;
        return cells.ToDictionary(
            c => c,
            c => $"{board[c].Terrain}|{board.Map.HeightAt(c)}|{board.Map.SurfaceAt(c)}|{board[c].BirthZone}|{board[c].IsRelicCell}|{board[c].Occupant}|"
                 + string.Join(",", board.Neighbors(c).Where(n => board.Map.HasFence(c, n)).Select(n => n.ToNotation()))
                 + (board.Map.RelicCells.ContainsKey(c) ? $"|{match.Relics.IsRevealed(c)}" : string.Empty));
    }
}
