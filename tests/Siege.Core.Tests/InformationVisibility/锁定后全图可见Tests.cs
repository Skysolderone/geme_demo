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
        Assert.Equal(match.Map.Boards.Sum(b => b.Width * b.Height), seat.Cells.Length);   // 全图 5 块棋盘的整块外接矩形（含岩石与未架桥深水）
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

        // 5 个占位全部展开；插旗阶段仍只有占位（D5）。
        DefaultBoardView board = match.World(P0).Board();
        Assert.Equal(5, board.Placeholders.Count(p => p.Expanded));
        Assert.Equal(5, board.Boards.Length);
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
        for (int zone = 0; zone < 5; zone++)
        {
            Assert.All(ZoneNotations(zone), n => Assert.Contains($"\"{n}\"", seat, StringComparison.Ordinal));
        }

        // 全知棋盘另含画布留白的场景格，按座位的只有 5 块棋盘的格：棋盘内的格集合相同。
        HashSet<Coord> boards = [.. match.Map.Boards.SelectMany(b => b.Cells())];
        Assert.Equal(
            DefaultBoardView.From(match.PublicWorldOf()).Cells.Select(c => c.Coord).Where(boards.Contains).Order(),
            match.World(P0).Board().Cells.Select(c => c.Coord).Order());
    }

    [Fact]
    public void HUD视图模型不遮蔽任何玩家()
    {
        // tasks 1.4 验收：势力排名栏、顺序层、手牌对比面板对四人四区的任何玩家都不遮蔽（原「他战场玩家数值不进 HUD 视图模型」的反向）。
        MatchFlow match = Started([0, 1, 3, 4]).PlayRound(P0, P1, P2, P3);
        ViewerWorld world = match.World(P0);
        Assert.All(All, p => Assert.False(world.IsConcealed(p)));

        var power = Assert.IsType<PowerLayerContent>(world.Layer(TacticalLayer.Power));
        Assert.Equal(4, power.Players.Length);
        Assert.All(power.Players, p => Assert.False(p.Concealed));
        Assert.All(power.Players, p => Assert.True(p.Total > BigInteger.Zero));
        Assert.All(power.Players, p => Assert.NotNull(p.Rank));
        Assert.All(world.HandPanel().Opponents, o => Assert.False(o.Concealed));
        Assert.All(world.HandPanel().Opponents, o => Assert.NotEmpty(o.Types));
        Assert.All(Assert.IsType<OrderLayerContent>(world.Layer(TacticalLayer.Order)).Rows, r => Assert.False(r.Concealed));

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
