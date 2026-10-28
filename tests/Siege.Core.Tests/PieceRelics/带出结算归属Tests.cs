using Siege.Core.Board;
using Siege.Core.PieceRelics;
using static Siege.Core.Tests.PieceRelics.PieceRelicFixtures;

namespace Siege.Core.Tests.PieceRelics;

/// <summary>
/// relic-redesign RQ5 / AC5：design.md D6 的归属细则逐条——① 保险件归原主（任何结局）；② 挂在完赛者棋子上归该完赛者；③ 其余原主完赛归原主、否则消失；
/// 地图刷出的只按 ②；同一件不会被两人带出（R27）；同局结算只发生一次；R19 点数（完赛全额、弃赛 50%、保护期内弃赛 0、出局 0）。
/// 3 人局名次表（未校准）24 / 16 / 10。
/// 变异验证（加法段实跑）：M3 把第 ③ 条"原主完赛归原主"挪到第 ② 条之前 → 红 1（自己的非保险件挂在另一名完赛者棋子上归对方）。
/// </summary>
public class 带出结算归属Tests
{
    private static Dictionary<PlayerId, RelicPlayerOutcome> Outcomes(RelicPlayerOutcome p0, RelicPlayerOutcome p1, RelicPlayerOutcome p2) =>
        new() { [TestMaps.P0] = p0, [TestMaps.P1] = p1, [TestMaps.P2] = p2 };

    private static int[] Ids(RelicPlayerSettlement settlement) => [.. settlement.BroughtOut.Select(t => t.Id)];

    [Fact]
    public void 保险件被敌方完赛者持有时仍归原主()
    {
        RelicToken insured = Carried(0, RelicKind.Banner, TestMaps.P0, insured: true);

        RelicMatchSettlement result = RelicSettlement.Settle(
            Outcomes(RelicPlayerOutcome.Eliminated(), RelicPlayerOutcome.Finished(1), RelicPlayerOutcome.Finished(2)),
            [insured],
            [new HeldRelic(insured, TestMaps.P1)]);

        Assert.Equal([0], Ids(result.Of(TestMaps.P0)));
        Assert.Empty(result.Of(TestMaps.P1).BroughtOut);
    }

    [Fact]
    public void 原主出局时非保险件不回库存()
    {
        RelicToken insured = Carried(0, RelicKind.WarDrum, TestMaps.P0, insured: true);
        RelicToken plain = Carried(1, RelicKind.Command, TestMaps.P0);

        RelicMatchSettlement result = RelicSettlement.Settle(
            Outcomes(RelicPlayerOutcome.Eliminated(), RelicPlayerOutcome.Finished(1), RelicPlayerOutcome.Finished(2)),
            [insured, plain],
            []);

        Assert.Equal([0], Ids(result.Of(TestMaps.P0)));
        Assert.Equal([plain], result.Vanished);
    }

    [Fact]
    public void 弃赛与中途退出同样只拿回保险件()
    {
        RelicToken[] carried =
        [
            Carried(0, RelicKind.WarDrum, TestMaps.P0, insured: true), Carried(1, RelicKind.Command, TestMaps.P0),
            Carried(2, RelicKind.Watchtower, TestMaps.P1, insured: true), Carried(3, RelicKind.Chameleon, TestMaps.P1),
        ];

        RelicMatchSettlement result = RelicSettlement.Settle(
            Outcomes(RelicPlayerOutcome.Resigned(2, 6), RelicPlayerOutcome.Abandoned(), RelicPlayerOutcome.Finished(1)),
            carried,
            []);

        Assert.Equal([0], Ids(result.Of(TestMaps.P0)));
        Assert.Equal([2], Ids(result.Of(TestMaps.P1)));
        Assert.Equal([1, 3], result.Vanished.Select(t => t.Id));
    }

    [Fact]
    public void 自己的非保险件挂在另一名完赛者棋子上归对方()
    {
        // R27：第 ② 条先于第 ③ 条，同一件不会被原主与持有者同时带出。
        RelicToken plain = Carried(4, RelicKind.WarDrum, TestMaps.P0);

        RelicMatchSettlement result = RelicSettlement.Settle(
            Outcomes(RelicPlayerOutcome.Finished(1), RelicPlayerOutcome.Finished(2), RelicPlayerOutcome.Finished(3)),
            [plain],
            [new HeldRelic(plain, TestMaps.P1)]);

        Assert.Empty(result.Of(TestMaps.P0).BroughtOut);
        Assert.Equal([4], Ids(result.Of(TestMaps.P1)));
    }

    [Fact]
    public void 原主完赛时未挂出或无人拾取或挂在出局者身上的非保险件归原主()
    {
        RelicToken inHand = Carried(0, RelicKind.WarDrum, TestMaps.P0);
        RelicToken onGround = Carried(1, RelicKind.Command, TestMaps.P0);
        RelicToken onLoser = Carried(2, RelicKind.Watchtower, TestMaps.P0);

        RelicMatchSettlement result = RelicSettlement.Settle(
            Outcomes(RelicPlayerOutcome.Finished(1), RelicPlayerOutcome.Resigned(2, 8), RelicPlayerOutcome.Eliminated()),
            [inHand, onGround, onLoser],
            [new HeldRelic(onLoser, TestMaps.P1)]);

        Assert.Equal([0, 1, 2], Ids(result.Of(TestMaps.P0)));
        Assert.Empty(result.Of(TestMaps.P1).BroughtOut);
        Assert.Empty(result.Vanished);
    }

    [Fact]
    public void 地图信物只按持有的完赛者带出()
    {
        RelicToken heldByFinisher = Map(10, RelicKind.Banner);
        RelicToken heldByLoser = Map(11, RelicKind.WarDrum);

        RelicMatchSettlement result = RelicSettlement.Settle(
            Outcomes(RelicPlayerOutcome.Finished(1), RelicPlayerOutcome.Resigned(2, 8), RelicPlayerOutcome.Finished(2)),
            [],
            [new HeldRelic(heldByFinisher, TestMaps.P2), new HeldRelic(heldByLoser, TestMaps.P1)]);

        Assert.Equal([10], Ids(result.Of(TestMaps.P2)));
        Assert.Empty(result.Of(TestMaps.P1).BroughtOut);
        Assert.Equal([heldByLoser], result.Vanished);
    }

    [Fact]
    public void 从盘面取持有关系结算且每件只归属一次()
    {
        // 盘面：P1 的棋子挂着 P0 的保险件与 P0 的非保险件、P2 的棋子挂着地图军旗、空格上躺着 P2 的非保险件。P0 出局，P1、P2 完赛。
        GameBoard board = TestMaps.Blank(size: 9)
            .Place("B2", TestMaps.P1).Place("C2", TestMaps.P1).Place("G7", TestMaps.P2)
            .Attach("B2", Carried(0, RelicKind.WarDrum, TestMaps.P0, insured: true))
            .Attach("C2", Carried(1, RelicKind.Command, TestMaps.P0))
            .Attach("G7", Map(5, RelicKind.Banner))
            .Attach("E5", Carried(3, RelicKind.Chameleon, TestMaps.P2));
        RelicToken[] carried =
        [
            Carried(0, RelicKind.WarDrum, TestMaps.P0, insured: true), Carried(1, RelicKind.Command, TestMaps.P0),
            Carried(2, RelicKind.Watchtower, TestMaps.P0), Carried(3, RelicKind.Chameleon, TestMaps.P2),
        ];

        RelicMatchSettlement result = RelicSettlement.Settle(
            board, Outcomes(RelicPlayerOutcome.Eliminated(), RelicPlayerOutcome.Finished(1), RelicPlayerOutcome.Finished(2)), carried);

        Assert.Equal([0], Ids(result.Of(TestMaps.P0)));
        Assert.Equal([1], Ids(result.Of(TestMaps.P1)));
        Assert.Equal([3, 5], Ids(result.Of(TestMaps.P2)));
        Assert.Equal([2], result.Vanished.Select(t => t.Id));

        int[] everyId = [.. result.Players.Values.SelectMany(p => p.BroughtOut).Concat(result.Vanished).Select(t => t.Id)];
        Assert.Equal([0, 1, 2, 3, 5], everyId.Order());
    }

    [Fact]
    public void 同一编号对应两种内容即抛出()
    {
        Assert.Throws<ArgumentException>(() => RelicSettlement.Settle(
            Outcomes(RelicPlayerOutcome.Finished(1), RelicPlayerOutcome.Finished(2), RelicPlayerOutcome.Finished(3)),
            [Carried(0, RelicKind.WarDrum, TestMaps.P0)],
            [new HeldRelic(Carried(0, RelicKind.Banner, TestMaps.P0), TestMaps.P1)]));
        Assert.Throws<ArgumentException>(() => RelicSettlement.Settle(
            Outcomes(RelicPlayerOutcome.Finished(1), RelicPlayerOutcome.Finished(2), RelicPlayerOutcome.Finished(3)),
            [],
            [new HeldRelic(Map(0, RelicKind.Banner), TestMaps.P1), new HeldRelic(Map(0, RelicKind.Banner), TestMaps.P2)]));
    }

    [Fact]
    public void 名册外的玩家即抛出()
    {
        Assert.Throws<ArgumentException>(() => RelicSettlement.Settle(
            Outcomes(RelicPlayerOutcome.Finished(1), RelicPlayerOutcome.Finished(2), RelicPlayerOutcome.Finished(3)),
            [Carried(0, RelicKind.WarDrum, TestMaps.P3)],
            []));
    }

    [Fact]
    public void 补给点完赛全额弃赛一半保护期内弃赛与出局为零()
    {
        RelicMatchSettlement result = RelicSettlement.Settle(
            Outcomes(RelicPlayerOutcome.Finished(1), RelicPlayerOutcome.Resigned(2, 4), RelicPlayerOutcome.Eliminated()), [], []);

        Assert.Equal(24, result.Of(TestMaps.P0).Points);
        Assert.Equal(8, result.Of(TestMaps.P1).Points);
        Assert.Equal(0, result.Of(TestMaps.P2).Points);
        Assert.Equal(0, RelicSettlement.PointsOf(RelicPlayerOutcome.Resigned(1, 3), 3));
        Assert.Equal(0, RelicSettlement.PointsOf(RelicPlayerOutcome.Abandoned(), 3));
    }

    [Fact]
    public void 同局结算只发生一次()
    {
        using var temp = new TempRelicProfile();
        RelicProfileStore store = temp.Store();
        store.Load();
        store.Begin("m7", new RelicLoadout([new(RelicKind.WarDrum, true), new(RelicKind.Command, false)]));
        RelicMatchSettlement result = RelicSettlement.Settle(
            Outcomes(RelicPlayerOutcome.Finished(1), RelicPlayerOutcome.Finished(2), RelicPlayerOutcome.Finished(3)),
            [Carried(0, RelicKind.WarDrum, TestMaps.P0, insured: true), Carried(1, RelicKind.Command, TestMaps.P0)],
            [new HeldRelic(Map(9, RelicKind.Banner), TestMaps.P0)]);

        Assert.True(store.Settle("m7", result.Of(TestMaps.P0)));
        string once = temp.Read();
        Assert.False(store.Settle("m7", result.Of(TestMaps.P0)));

        Assert.Equal(once, temp.Read());
        Assert.Equal(24, store.Current.Points);
        Assert.Equal(1, store.Current.StockOf(RelicKind.WarDrum));
        Assert.Equal(1, store.Current.StockOf(RelicKind.Command));
        Assert.Equal(1, store.Current.StockOf(RelicKind.Banner));
        Assert.Null(store.Current.InFlight);
    }

    [Fact]
    public void 对局标识不符时不结算()
    {
        using var temp = new TempRelicProfile();
        RelicProfileStore store = temp.Store();
        store.Load();
        store.Begin("m7", RelicLoadout.Empty);

        Assert.False(store.Settle("other", new RelicPlayerSettlement(TestMaps.P0, RelicPlayerOutcome.Finished(1), 24, [])));
        Assert.Equal("m7", store.Current.InFlight!.MatchId);
    }

    [Fact]
    public void 中途退出的残留在途记录只回保险件()
    {
        using var temp = new TempRelicProfile();
        RelicProfileStore store = temp.Store();
        store.Load();
        store.Begin("m8", new RelicLoadout([new(RelicKind.WarDrum, true), new(RelicKind.Command, false)]));

        RelicInFlight? abandoned = new TempRelicProfileStoreReader(temp).Reload().SettleAbandoned();

        Assert.Equal("m8", abandoned!.MatchId);
        RelicProfile after = RelicProfile.Parse(temp.Read());
        Assert.Equal(1, after.StockOf(RelicKind.WarDrum));
        Assert.Equal(0, after.StockOf(RelicKind.Command));
        Assert.Equal(0, after.Points);
        Assert.Null(after.InFlight);
    }

    /// <summary>模拟"重新启动后读档"：另起一个存取对象读同一份文件。</summary>
    private sealed class TempRelicProfileStoreReader(TempRelicProfile temp)
    {
        internal RelicProfileStore Reload()
        {
            RelicProfileStore store = temp.Store();
            Assert.Equal(RelicProfileLoadStatus.Loaded, store.Load().Status);
            return store;
        }
    }
}
