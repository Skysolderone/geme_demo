using System.Numerics;
using System.Text.Json;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Presentation.Layers;
using Siege.Presentation.Visibility;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.InformationVisibility;

/// <summary>
/// 规格：openspec/changes/open-map/specs/information-visibility —— Requirement: 始终公开的信息（Scenario 他人棋盘从第 1 大回合起可见）、本大回合的 Pass 名单（Scenario 全员 Pass 可见）；
/// tasks 1.4：锁定后按座位视图含全图（棋子、地形、设施、信物揭示、全部玩家势力明细与禁入格、全员 Pass 名单），5 个占位全部展开；HUD 视图模型不再遮蔽任何玩家数值。
/// 按座位过滤的数据层机制保留，过滤边界"所在战场"= 全图，所以这里断言的都是反向结果（没有任何格 / 玩家被过滤掉）。
/// </summary>
/// <remarks>
/// 变异验证（open-map 1.4 实跑，只跑本类）：M-O4 <c>MatchFlow.PublishFor</c> 的可见格改回只取本座位锁定的那块棋盘 → 「他人棋盘从第1大回合起可见」「按座位序列化含全部五块棋盘」「HUD视图模型不遮蔽任何玩家」红 3。
/// </remarks>
public class 锁定后全图可见Tests
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private static string[] ZoneNotations(int zone) => [.. Map.BirthBoard(zone).Cells().Select(c => c.ToNotation()).Order()];

    [Fact]
    public void 他人棋盘从第1大回合起可见()
    {
        // Scenario：第 1 大回合，玩家 A 锁定棋盘 1，查询棋盘 4 内任一格与锁定棋盘 4 的玩家 C 的势力明细 → 返回该格的地形与占用，以及 C 的领地分与各棋串军势。
        MatchFlow match = Started([0, 1, 3, 4]);
        Assert.Equal(1, match.MajorRound);
        Assert.True(match.PublishFor(P0).IsExpanded(BirthCell(match.Map, 3, 0, 0)));   // 第 1 大回合一开始即可见
        match.PlayRound(P0, P1, P2, P3);   // 四人各落一子（走到第 2 大回合），再看势力明细
        SeatPublicView seat = match.PublishFor(P0);

        Assert.Equal(0, seat.Battlefield);
        Assert.Equal(match.Map.Width * match.Map.Height, seat.Cells.Length);   // map-expand-25 段 B：整张图的全部格（平台、低地与画布留白；原为各棋盘外接矩形之和）
        Coord onBoard4 = BirthCell(match.Map, 3, 0, 0);
        Assert.True(seat.IsExpanded(onBoard4));
        Assert.NotNull(seat.CellAt(onBoard4));
        Assert.Contains(seat.Groups, g => g.Owner == P2);   // 棋盘 4 上 C 的棋串可见

        SeatPlayerView c = seat.Players.Single(p => p.Player == P2);
        Assert.True(c.SameBattlefield);
        Assert.True(c.Power is { } power && power > BigInteger.Zero);
        Assert.NotNull(c.TerritoryScore);
        Assert.NotNull(c.HandTypes);
        Assert.All(seat.Players, p => Assert.True(p.SameBattlefield));

        // 全部占位展开（map-expand-25：link:1:4 为 10 块平台）；插旗阶段仍只有占位（D5）。
        DefaultBoardView board = match.World(P0).Board();
        Assert.Equal(10, board.Placeholders.Count(p => p.Expanded));
        Assert.Equal(10, board.Boards.Length);
        Assert.Equal([P1, P2, P3], match.World(P0).SameBattlefieldPlayers);
        DefaultBoardView planting = Create().World(P0).Board();
        Assert.Empty(planting.Cells);
        Assert.All(planting.Placeholders, p => Assert.False(p.Expanded));
    }

    [Fact]
    public void 按座位序列化含全部五块棋盘()
    {
        // tasks 1.4 验收：按座位序列化含全部 5 块棋盘的格数据，与全知棋盘的格集合相同。
        MatchFlow match = Started([0, 1, 3, 4]);
        string seat = JsonSerializer.Serialize(match.World(P0).Board().Cells.Select(c => c.Coord.ToNotation()), Json);
        for (int zone = 0; zone < match.Map.Boards.Length; zone++)
        {
            Assert.All(ZoneNotations(zone), n => Assert.Contains($"\"{n}\"", seat, StringComparison.Ordinal));
        }

        // map-expand-25 段 B：按座位的格集合与全知棋盘相同（含平台之间的低地与画布留白；原为"只有棋盘内的格"）。
        Assert.Equal(
            DefaultBoardView.From(match.PublicWorldOf()).Cells.Select(c => c.Coord).Order(),
            match.World(P0).Board().Cells.Select(c => c.Coord).Order());
    }

    [Fact]
    public void 插旗阶段之后座位可见整座岛含低地()
    {
        // map-expand-25 段 B：浮空岛的平台之间是低地（河道、桥、岩石），不在任何平台外接矩形里。插旗阶段结束后按座位视图 = 整张图的全部格，
        // 低地的地形与占用都给出；插旗阶段锁定后仍只见自己那块平台（match-setup「锁定后只见自己的棋盘」不变）。
        // 变异 MB-5（实跑，过滤本类与 自动演示全流程自检Tests）：PublishFor 插旗阶段之后的可见格改回"本战场各棋盘外接矩形" → 本测试、「他人棋盘从第1大回合起可见」
        // 「按座位序列化含全部五块棋盘」与 自动演示全流程自检Tests 的「真对局走一遍_四步全通过」「全图解禁」「插旗锁定与构筑期」红 6。
        MatchFlow match = Started([0, 1, 3, 4]);
        HashSet<Coord> boards = [.. match.Map.Boards.SelectMany(b => b.Cells())];
        Coord[] lowland = [.. match.Map.AllCoords().Where(c => match.Map.IsPlayable(c) && !boards.Contains(c))];
        Coord[] water = [.. match.Map.AllCoords().Where(c => !boards.Contains(c) && match.Map.TerrainData.IsUnbridgedDeepWater(c))];
        Assert.NotEmpty(lowland);   // 样本口径：link:1:4 确有低地可落子格与低地河道
        Assert.NotEmpty(water);

        SeatPublicView seat = match.PublishFor(P0);
        Assert.Equal(match.Map.AllCoords().Order().Select(c => c.ToNotation()), seat.Cells.Select(c => c.Coord));
        Assert.All(lowland, c => Assert.Equal((Terrain.Playable, match.Map.HeightAt(c)), (seat.CellAt(c)!.Terrain, seat.CellAt(c)!.Height)));
        Assert.Equal((Terrain.Obstacle, Surface.DeepWater), (seat.CellAt(water[0])!.Terrain, seat.CellAt(water[0])!.Surface));

        // 低地上的棋子对其他座位可见（第 4 大回合起可落低地）。
        match.Place(P2, lowland[0]);
        Assert.Equal(P2, match.PublishFor(P0).CellAt(lowland[0])!.Owner);
        Assert.Equal(P2, match.World(P0).Board().CellAt(lowland[0]).Occupant!.Value.Owner);   // 表现层按座位过滤同一组格

        // 插旗阶段：锁定后只见自己那块平台，低地一格也没有；未锁定者什么也看不见。
        MatchFlow planting = Create();
        planting.Flags.Plant(P0, 2);
        SeatPublicView locked = planting.PublishFor(P0);
        Assert.Equal(planting.Map.BirthBoard(2).Cells().Order().Select(c => c.ToNotation()), locked.Cells.Select(c => c.Coord));
        Assert.DoesNotContain(lowland, locked.IsExpanded);
        Assert.Empty(planting.PublishFor(P1).Cells);
    }

    [Fact]
    public void HUD视图模型不遮蔽任何玩家()
    {
        // tasks 1.4 验收：势力排名栏、顺序层、手牌对比面板对四人四区的任何玩家都不遮蔽（原「他战场玩家数值不进 HUD 视图模型」的反向）。
        MatchFlow match = Started([0, 1, 3, 4]).PlayRound(P0, P1, P2, P3);
        ViewerWorld world = match.World(P0);

        var power = Assert.IsType<PowerLayerContent>(world.Layer(TacticalLayer.Power));
        Assert.Equal(4, power.Players.Length);
        Assert.All(power.Players, p => Assert.True(p.Total > BigInteger.Zero));
        Assert.All(power.Players, p => Assert.NotNull(p.Rank));
        Assert.All(world.HandPanel().Opponents, o => Assert.NotEmpty(o.Types));
        Assert.All(Assert.IsType<OrderLayerContent>(world.Layer(TacticalLayer.Order)).Rows, r => Assert.True(r.Value > 0 || r.Rank > 0));   // 顺序层数值全员可读（open-map 段 B：遮蔽字段已删除）

        // 信息层含全图：他人棋盘上的棋串出现在气层里。
        var liberties = Assert.IsType<LibertyLayerContent>(world.Layer(TacticalLayer.Board, BoardReading.Groups));
        Assert.Contains(liberties.Groups, g => g.Owner == P2);
        Assert.Contains(liberties.Groups, g => g.Owner == P3);
    }

    [Fact]
    public void 全员Pass可见()
    {
        // Scenario：本大回合任一在局对手已 Pass → 本机座位视图的 Pass 名单含该对手（不论其锁定哪块棋盘）。
        MatchFlow match = Started([0, 1, 3, 4]);
        PlayerId first = match.CurrentPlayer!.Value;
        match.PassTurn();
        foreach (PlayerId seat in All.Where(p => p != first))
        {
            Assert.Contains(first, match.PublishSeatProjection(seat).PassedThisRound);
        }

        Assert.Equal(match.Publish().PassedThisRound, match.PublishSeatProjection(P0).PassedThisRound);
    }
}
