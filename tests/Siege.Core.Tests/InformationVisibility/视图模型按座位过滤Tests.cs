using System.Text.Json;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Presentation.Layers;
using Siege.Presentation.MapSelect;
using Siege.Presentation.Visibility;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.InformationVisibility;

/// <summary>
/// information-visibility「其他战场不可见」「数据层过滤」在表现层的镜像（board-link-mode 段 D-b，tasks 4.2 / 4.3）：
/// 观察者世界（<see cref="ViewerWorld"/>）在链接模式下带本座位的 <see cref="SeatPublicView"/>，默认棋盘 <see cref="ViewerWorld.Board"/> 与信息层 <see cref="ViewerWorld.Layer"/>
/// 只含本座位所在战场的格；其余出生棋盘只剩占位（编号 + 外接矩形）。retire-legacy-rules：旧模式全图可见、无占位的对照随旧模式删除。
/// 规格：match-setup「开局信息公开范围」「匿名同时插旗」；visual-style-baseline「未展开区域的可视表现」（视图模型部分）。
/// </summary>
/// <remarks>
/// 变异验证（段 D-b 实跑）：M-E1 <c>DefaultBoardView.ForSeat</c> 不过滤 <c>Cells</c>（只填占位）→ 红 5（插旗阶段只有五个占位、构筑期只见自己的棋盘、序列化不含其他出生棋盘的任何坐标、同区两人互见、链接后合并战场的格一起可见）；
/// M-E2 <c>ViewerWorld.Layer</c> 对链接模式恒不过滤 → 「信息层只含本战场的格」红 1。
/// </remarks>
public class 视图模型按座位过滤Tests
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    /// <summary>某块出生棋盘的全部坐标（围棋记法）：整块外接矩形（link-map-terrain：读棋盘清单，不读出生区；G1 起战斗阶段可见格即整块棋盘，含岩石与深水）。</summary>
    private static string[] ZoneNotations(int zone) => [.. Map.BirthBoard(zone).Cells().Select(c => c.ToNotation()).Order()];

    [Fact]
    public void 插旗阶段只有五个占位_没有任何格()
    {
        // match-setup Scenario「插旗阶段的可见信息」：5 个占位的位置与边长可见，看不到任何棋盘内部的格与信物格位置。
        MatchFlow match = Create();
        DefaultBoardView board = match.World(P0).Board();

        Assert.Empty(board.Cells);
        Assert.Empty(board.Boards);
        Assert.Equal(5, board.Placeholders.Length);
        Assert.Equal([0, 1, 2, 3, 4], board.Placeholders.Select(p => p.Zone));
        Assert.All(board.Placeholders, p => Assert.False(p.Expanded || p.Own));
        // 占位取整块棋盘的外接矩形（match-setup「占位尺寸取整块棋盘」），不是出生区（核心区）。
        Assert.All(board.Placeholders, p => Assert.Equal(Map.BirthBoard(p.Zone).Origin, p.Origin));
        Assert.All(board.Placeholders, p => Assert.Equal((Map.BirthBoard(p.Zone).Width, Map.BirthBoard(p.Zone).Height), (p.Width, p.Height)));
        Assert.Equal((Map.Width, Map.Height), (board.Width, board.Height));
        Assert.Null(match.World(P0).OwnZone);
        Assert.Empty(match.World(P0).SameBattlefieldPlayers);

        // 新局界面的一行说明只提占位，不提信物与可落子格。
        string info = MapPreviewInfo.Of(board);
        Assert.Contains("5 块出生棋盘", info, StringComparison.Ordinal);
        Assert.DoesNotContain("信物", info, StringComparison.Ordinal);
        Assert.DoesNotContain("可落子", info, StringComparison.Ordinal);
    }

    [Fact]
    public void 构筑期只见自己的棋盘_其余四块是占位()
    {
        // match-setup Scenario「锁定后只见自己的棋盘」/ visual-style-baseline「他人棋盘为占位」：P0 在棋盘 1（索引 0），其余 4 块只剩占位。
        MatchFlow match = Started([0, 1, 3, 4]);
        ViewerWorld world = match.World(P0);
        DefaultBoardView board = world.Board();
        SeatPublicView seat = match.PublishFor(P0);

        Assert.Equal(seat.Cells.Select(c => c.Coord).Order(), board.Cells.Select(c => c.Coord.ToNotation()).Order());
        Assert.Equal(ZoneNotations(0), board.Cells.Select(c => c.Coord.ToNotation()).Order());
        Assert.All(board.Cells, c => Assert.Equal(Map.BirthZones[0].Contains(c.Coord) ? (int?)0 : null, c.BirthZone));   // 出生区 = 核心区，棋盘内其余格不属于出生区
        Assert.Single(board.Boards);
        Assert.Equal(0, world.OwnZone);
        BoardPlaceholderView own = Assert.Single(board.Placeholders, p => p.Own);
        Assert.Equal((0, true), (own.Zone, own.Expanded));
        Assert.Equal(4, board.Placeholders.Count(p => !p.Expanded && !p.Own));
        Assert.Empty(world.SameBattlefieldPlayers);

        // 他人棋盘上的格一个都不在视图里；拾取 / 悬停用的 CellOrNull 对它们返回 null，不抛。
        foreach (Coord other in Map.BirthBoard(1).Cells().Concat(Map.BirthBoard(3).Cells()))
        {
            Assert.Null(board.CellOrNull(other));
            Assert.Throws<ArgumentOutOfRangeException>(() => board.CellAt(other));
        }
    }

    [Fact]
    public void 序列化不含其他出生棋盘的任何坐标()
    {
        // information-visibility Scenario「数据层过滤」的表现层镜像：按座位的默认棋盘序列化后不含任何未展开棋盘的坐标；全知棋盘（反面）含。
        // Coord 序列化为 {X,Y} 对象，这里按围棋记法投影后再序列化，便于按坐标文本断言。
        MatchFlow match = Started([0, 1, 3, 4]);
        string seat = JsonSerializer.Serialize(match.World(P0).Board().Cells.Select(c => c.Coord.ToNotation()), Json);
        string omniscient = JsonSerializer.Serialize(DefaultBoardView.From(match.PublicWorldOf()).Cells.Select(c => c.Coord.ToNotation()), Json);

        foreach (int zone in new[] { 1, 2, 3, 4 })
        {
            foreach (string notation in ZoneNotations(zone))
            {
                Assert.DoesNotContain($"\"{notation}\"", seat, StringComparison.Ordinal);
            }
        }

        Assert.All(ZoneNotations(0), n => Assert.Contains($"\"{n}\"", seat, StringComparison.Ordinal));
        Assert.All(ZoneNotations(3), n => Assert.Contains($"\"{n}\"", omniscient, StringComparison.Ordinal));   // 反面
    }

    [Fact]
    public void 同区两人互见_他人不见()
    {
        // match-setup Scenario「多人同区」：A、B 都在棋盘 3 → 彼此可见、其他玩家看不到棋盘 3 上有谁。
        MatchFlow match = Started([2, 2, 0, 4]);
        Assert.Equal([P1], match.World(P0).SameBattlefieldPlayers);
        Assert.Equal([P0], match.World(P1).SameBattlefieldPlayers);
        Assert.Empty(match.World(P2).SameBattlefieldPlayers);
        Assert.Equal(ZoneNotations(2), match.World(P1).Board().Cells.Select(c => c.Coord.ToNotation()).Order());
    }

    [Fact]
    public void 占位透传主题()
    {
        // match-setup Scenario「插旗阶段的可见信息」（link-map-terrain 裁决 M3）在视图模型上的镜像：5 个占位带主题名，原样取自座位视图（即棋盘清单），表现层不另算。
        // 样本口径：主题确实存在且两两不同（全是 null 时"相等"恒真）。
        // 变异验证（link-map-terrain 第 1 组收尾实跑，只跑本类）：M-V1 ForSeat 构造占位时主题恒写 null → 本测试红 1。
        MatchFlow match = Create();
        DefaultBoardView board = match.World(P0).Board();
        BoardTheme?[] expected = [.. Map.Boards.Select(b => b.Theme)];
        Assert.All(expected, t => Assert.NotNull(t));
        Assert.Equal(5, expected.Distinct().Count());

        Assert.Equal(expected, board.Placeholders.Select(p => p.Theme));
        Assert.Equal(match.PublishFor(P0).Placeholders.Select(p => p.Theme), board.Placeholders.Select(p => p.Theme));
    }

    [Fact]
    public void 棋盘内有岩石时本座位棋盘仍可见()
    {
        // link-map-terrain 第 1 组裁决④：棋盘清单只留"整块可见"的台面，判据不得假设棋盘内每格可落子。
        // 手写链接档夹具：棋盘 1（下标 0）的左下角 C3 与内部 E6 是岩石，其余格连通。P0 锁定棋盘 1 进入第 1 大回合：
        // 本座位的棋盘仍留在清单里，其占位已展开；他人棋盘仍只是占位、不在清单里。
        // 第 2 组 G1（裁决 A）起战斗阶段可见格 = 本战场棋盘的整块外接矩形，岩石也在可见格里（36 格，岩石格的地形为障碍）。
        // 变异验证（第 1 组收尾实跑）：M-V2 Boards 过滤改回"棋盘内每格都可见"、M-V3 占位展开改回"左下角格可见" 各红 1——G1 之后棋盘内每格都可见，
        // 这两条变异在本测试上不再可分（两种判据结果相同），本测试改为同时钉住 G1 在表现层的镜像（岩石格出现、地形为障碍）。
        MapData map = RockyMap();
        MatchFlow match = MatchFlow.CreateUnvalidated(map, TestMaps.Seed, All, TestMaps.Relics(map), Options);
        foreach (PlayerId p in All)
        {
            match.Debug.SeedHand(p, (PieceType.Basic, 50));
        }

        match.PlantSequentially(All.Select((p, i) => (p, new[] { 0, 1, 3, 4 }[i])));
        Assert.Equal(MatchPhase.InProgress, match.Phase);

        DefaultBoardView board = match.World(P0).Board();
        Assert.Equal(36, board.Cells.Length);   // 6×6 整块，含 2 块岩石
        Assert.All(new[] { new Coord(2, 2), new Coord(4, 5) }, rock => Assert.Equal(Terrain.Obstacle, board.CellAt(rock).Terrain));
        Assert.Equal([map.Boards[0]], board.Boards);
        BoardPlaceholderView own = Assert.Single(board.Placeholders, p => p.Own);
        Assert.Equal((0, true), (own.Zone, own.Expanded));
        Assert.Equal(4, board.Placeholders.Count(p => !p.Expanded));
    }

    /// <summary>链接档手写夹具（link:90002，不发生链接）：五块 6×6 出生棋盘，棋盘 1 的左下角 C3 与内部 E6 是岩石；没有信物格。只经 CreateUnvalidated 建局。</summary>
    private static MapData RockyMap()
    {
        MapData built = LinkMapFixtures.Build("link:90002", LinkMapFixtures.Births);
        return built with
        {
            Obstacles = built.Obstacles.Add(new Coord(2, 2)).Add(new Coord(4, 5)),
            RelicCells = System.Collections.Immutable.ImmutableDictionary<Coord, RelicCellSpec>.Empty,
        };
    }

    [Fact]
    public void 链接后合并战场的格一起可见()
    {
        // information-visibility Scenario「链接后立即可见」：四人四区、脚本配对 (1,2)(4,5) → P0 的视图含棋盘 1 与（平移后的）棋盘 2，占位展开 2 块（段 F：拼接，没有公共棋盘与通道）。
        MatchFlow match = Started([0, 1, 3, 4], new ScriptedPairingSource()).PassRounds(3);
        Assert.Equal(2, match.Links.Count);
        DefaultBoardView board = match.World(P0).Board();
        SeatPublicView seat = match.PublishFor(P0);

        Assert.Equal(seat.Cells.Select(c => c.Coord).Order(), board.Cells.Select(c => c.Coord.ToNotation()).Order());
        Assert.Equal(ZoneNotations(0).Length + ZoneNotations(1).Length, board.Cells.Length);   // 拼接不新增格；G1：两块棋盘的整块外接矩形
        Assert.Equal([0, 1], board.Placeholders.Where(p => p.Expanded).Select(p => p.Zone));
        Assert.Equal([2, 3, 4], board.Placeholders.Where(p => !p.Expanded).Select(p => p.Zone));
        Assert.Equal(2, board.Boards.Length);   // 两块出生棋盘边对边紧贴
        Assert.Equal([P1], match.World(P0).SameBattlefieldPlayers);
        Assert.Contains(board.Cells, c => c.BirthZone == 1);
        Assert.DoesNotContain(board.Cells, c => c.BirthZone is 3 or 4);
    }

    [Fact]
    public void 信息层只含本战场的格()
    {
        // visual-style-baseline：信息层只在本战场的格上出现。两块棋盘各有棋子；P0 的盘面层 / 势力层 / 信物层不含棋盘 4 的任何格或棋串。
        MatchFlow match = Started([0, 1, 3, 4]).PlayRound(P0, P1, P2, P3);
        ViewerWorld world = match.World(P0);
        HashSet<Coord> mine = [.. world.Board().Cells.Select(c => c.Coord)];
        Assert.Contains(match.Board.AllGroups(), g => g.Owner == P2);   // 样本口径：他人战场上确有棋串

        var territory = Assert.IsType<TerritoryLayerContent>(world.Layer(TacticalLayer.Board, BoardReading.Ownership));
        Assert.NotEmpty(territory.Cells);
        Assert.All(territory.Cells, c => Assert.Contains(c.Coord, mine));

        var liberties = Assert.IsType<LibertyLayerContent>(world.Layer(TacticalLayer.Board, BoardReading.Groups));
        Assert.NotEmpty(liberties.Groups);
        Assert.All(liberties.Groups, g => Assert.All(g.Stones, s => Assert.Contains(s, mine)));
        Assert.DoesNotContain(liberties.Groups, g => g.Owner == P2 || g.Owner == P3);

        var power = Assert.IsType<PowerLayerContent>(world.Layer(TacticalLayer.Power));
        Assert.All(power.Groups, g => Assert.All(g.Stones, s => Assert.Contains(s, mine)));
        Assert.All(power.Territory, c => Assert.Contains(c.Coord, mine));

        var relics = Assert.IsType<RelicLayerContent>(world.Layer(TacticalLayer.Relics));
        Assert.NotEmpty(relics.Relics);
        Assert.All(relics.Relics, r => Assert.Contains(r.Coord, mine));

        // 反面：全知世界的同一层含他人战场的棋串。
        Assert.Contains(Assert.IsType<LibertyLayerContent>(Siege.Presentation.Layers.TacticalLayers.Build(match.PublicWorldOf(), TacticalLayer.Board, BoardReading.Groups)).Groups, g => g.Owner == P2);
    }

    [Fact]
    public void 别人的座位视图不得进入观察者世界()
    {
        MatchFlow match = Started([0, 1, 3, 4]);
        ArgumentException ex = Assert.Throws<ArgumentException>(() => ViewerWorld.Build(
            P0, match.Publish(), match.PublishSupplement(), match.Hands.AccessFor(P0).PrivateView(), null, null, match.PublishFor(P1)));
        Assert.Contains("座位视图属于", ex.Message, StringComparison.Ordinal);
    }
}
