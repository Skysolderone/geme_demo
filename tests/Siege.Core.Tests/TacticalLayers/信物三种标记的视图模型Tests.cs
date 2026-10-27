using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.PieceRelics;
using Siege.Presentation.Layers;
using Siege.Presentation.Visibility;
using static Siege.Core.Tests.LinkModeFixtures;
using static Siege.Core.Tests.PieceRelics.PieceRelicFixtures;

namespace Siege.Core.Tests.TacticalLayers;

/// <summary>
/// tactical-layers「默认棋盘上的信物三种标记」（relic-redesign R25，段 C2）：Godot 画三种标记只读默认棋盘视图模型——标记类别、种类、
/// 保险标记（<see cref="BoardCellView.RelicInsured"/>）与预告落位大回合（<see cref="DefaultBoardView.RelicForecastRound"/>）都从公开快照投影，
/// 且经按座位过滤（<see cref="DefaultBoardView.ForSeat"/>，图形版会话一定走它）后不丢。
/// 变异验证（C2 实跑，均已恢复）：V1 <c>From</c> 不写 <c>RelicInsured</c> → 红 1；V2 <c>From</c> 不写 <c>RelicForecastRound</c> → 红 1。
/// </summary>
public class 信物三种标记的视图模型Tests
{
    private static readonly int[] FourZones = [0, 1, 2, 3];

    /// <summary>四人各锁一块平台、每大回合各落 1 子，推进到第 <paramref name="majorRound"/> 大回合开始。</summary>
    private static MatchFlow PlayedTo(int majorRound)
    {
        MatchFlow match = Started(FourZones);
        while (match.Phase == MatchPhase.InProgress && match.MajorRound < majorRound)
        {
            match.PlayRound(All);
        }

        return match;
    }

    [Fact]
    public void 预告格带落位大回合且地图信物按种类标记()
    {
        // 第 5 大回合开始：第 4 大回合落位的地图信物还在空格上（各家每大回合只落 1 子，不会恰好拾取），同时已预告第 6 大回合那一批。
        MatchFlow match = PlayedTo(5);
        RelicForecast forecast = match.PendingRelicForecast!;

        DefaultBoardView board = match.World(P0).Board();   // 经 ForSeat（按座位视图）

        Assert.Equal(forecast.LandingRound, board.RelicForecastRound);
        foreach (RelicSpawnSlot slot in forecast.Slots)
        {
            BoardCellView cell = board.Cells.Single(c => c.Coord == slot.Cell);
            Assert.Equal((RelicMarker.Forecast, slot.Kind, false), (cell.Relic, cell.RelicKind, cell.RelicInsured));
        }

        foreach (BoardRelic relic in match.Board.RelicsOnBoard())
        {
            BoardCellView cell = board.Cells.Single(c => c.Coord == relic.Cell);
            Assert.Equal(match.Board[relic.Cell].Occupant is null ? RelicMarker.OnMap : RelicMarker.Held, cell.Relic);
            Assert.Equal(relic.Token.Kind, cell.RelicKind);
            Assert.False(cell.RelicInsured);   // 地图刷出的不是保险件
        }

        // 信物层面板的预告行同样带落位大回合。
        var layer = (RelicLayerContent)match.World(P0).Layer(TacticalLayer.Relics);
        Assert.All(layer.Relics.Where(r => r.IsForecast), r => Assert.Equal(forecast.LandingRound, r.LandingRound));
        Assert.All(layer.Relics.Where(r => !r.IsForecast), r => Assert.Null(r.LandingRound));
    }

    [Fact]
    public void 持物棋子的保险标记进视图模型()
    {
        // 直接在正式盘面给 P0 的一枚棋子挂一件保险带入件（只测投影，不走部署）：默认棋盘标为"持物棋子 + 保险"，经 ForSeat 后不变。
        MatchFlow match = PlayedTo(2);
        Coord stone = match.Board.AllCoords().First(c => match.Board[c].Occupant?.Owner == P0);
        match.Board.Attach(stone.ToNotation(), Carried(99, RelicKind.WarDrum, P0, insured: true));

        DefaultBoardView full = DefaultBoardView.From(match.PublicWorldOf());
        DefaultBoardView seated = match.World(P0).Board();

        foreach (DefaultBoardView board in new[] { full, seated })
        {
            BoardCellView cell = board.Cells.Single(c => c.Coord == stone);
            Assert.Equal((RelicMarker.Held, RelicKind.WarDrum, true), (cell.Relic, cell.RelicKind, cell.RelicInsured));
            Assert.Null(board.RelicForecastRound);   // 第 2 大回合还没有预告
        }
    }
}
