using System.Collections.Immutable;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.PieceRelics;

namespace Siege.Core.Tests.PieceRelics;

/// <summary>
/// relic-redesign 接线段 W1-3（design.md D1 / R28）：同形比对与同形历史只用不含信物层的 <see cref="GameBoard.SerializeShape"/>
/// （<c>SettlementDriver</c> 记入历史、<c>BatchRehearsal.DuplicateOf</c>、<c>ExpertLookahead</c> 的模拟历史三处）；
/// 结算时"正式盘面 vs 预演副本"的一致性核对用含信物层的 <see cref="GameBoard.Serialize"/>——只差信物层的分歧也要抓到。
/// 变异验证（W1 实跑，只跑 PieceRelics 命名空间，与刷新的变异分两批同时施加、互不相交）：MS-1 SettlementDriver 核对改用 SerializeShape → 「信物层只在正式盘面一侧变化时核对抛错」红；
/// MS-2 记入历史改用 Serialize → 「正式结算记入同形历史的是棋形」红。
/// </summary>
public class 信物层与同形及结算核对Tests
{
    private static GameBoard Board() => TestMaps.Blank(size: 7).Place("D4", TestMaps.P1);

    [Fact]
    public void 正式结算记入同形历史的是棋形()
    {
        // 盘面上有一件地图信物（E5）时确认一个批次：历史条目不含信物段（没有 '#'），恰等于结算后盘面的棋形序列化；
        // 完整序列化（含信物层）与之不同——信物层确实非空，"不含"不是空层的巧合。
        GameBoard board = Board();
        board.AttachRelic(TestMaps.At("E5"), RelicToken.Spawned(0, RelicKind.Command));
        SettlementDriver driver = BatchFixtures.Driver(board);

        SettlementOutcome outcome = driver.Confirm(BatchFixtures.Context(board, TestMaps.P0), [BatchFixtures.P("A1")]);

        Assert.True(outcome.Confirmed);
        CommittedBoard entry = Assert.Single(driver.History.Entries);
        Assert.DoesNotContain('#', entry.Board);
        Assert.Equal(board.SerializeShape(), entry.Board);
        Assert.NotEqual(board.Serialize(), entry.Board);
    }

    [Fact]
    public void 只差信物层的结算后盘面判为同形()
    {
        // R28：历史里已有某个棋形；同一棋形只多一件信物的结算后盘面经 DuplicateOf 判为与它同形（喂的是 SerializeShape）。
        GameBoard settled = Board().Place("A1", TestMaps.P0);
        var history = new BoardHistory();
        int sequence = history.Record(settled.SerializeShape());

        GameBoard withRelic = settled.Clone();
        withRelic.AttachRelic(TestMaps.At("E5"), RelicToken.Spawned(0, RelicKind.WarDrum));

        Assert.Equal(sequence, BatchRehearsal.DuplicateOf(history, withRelic));
        Assert.Null(BatchRehearsal.DuplicateOf(history, withRelic.Clone().Place("B2", TestMaps.P0)));
    }

    [Fact]
    public void 信物层只在正式盘面一侧变化时核对抛错()
    {
        // D1："挂出只在一侧生效"一类的分歧——预演之后、正式落子之前（扣手牌回调里）正式盘面多挂了一件信物，棋形完全相同。
        // 核对用含信物层的 Serialize → 抛 SiegeRuleException（若用 SerializeShape 就会漏过）。
        GameBoard board = Board();
        var hooks = new AttachingHooks(board);
        var driver = new SettlementDriver(board, new BoardHistory(), hooks);

        SiegeRuleException ex = Assert.Throws<SiegeRuleException>(() =>
            driver.Confirm(BatchFixtures.Context(board, TestMaps.P0), [BatchFixtures.P("A1")]));

        Assert.Contains("预演副本不一致", ex.Message, StringComparison.Ordinal);
        Assert.True(hooks.Attached);
    }

    [Fact]
    public void 持有信物件数只数挂在该玩家棋子上的()
    {
        // R24 的唯一实现 GameBoard.HeldRelicCount：挂在该玩家棋子上的件数，不论种类 / 带入者 / 保险；空格上的地图信物与别人棋子上的不计；空层为 0。
        GameBoard board = Board().Place("A1", TestMaps.P0).Place("B1", TestMaps.P0);
        Assert.Equal(0, board.HeldRelicCount(TestMaps.P0));
        board.AttachRelic(TestMaps.At("A1"), RelicToken.Spawned(0, RelicKind.Banner));
        board.AttachRelic(TestMaps.At("B1"), RelicToken.Carried(1, RelicKind.WarDrum, TestMaps.P1, insured: true));
        board.AttachRelic(TestMaps.At("D4"), RelicToken.Spawned(2, RelicKind.Command));
        board.AttachRelic(TestMaps.At("F6"), RelicToken.Spawned(3, RelicKind.Chameleon));

        Assert.Equal(2, board.HeldRelicCount(TestMaps.P0));
        Assert.Equal(1, board.HeldRelicCount(TestMaps.P1));
        Assert.Equal(0, board.HeldRelicCount(BatchFixtures.P2));
    }

    [Fact]
    public void 出生区域取出生平台()
    {
        // W1-1（R32 岛图口径）：带棋盘清单的地图上，出生区域 = 出生平台的外接矩形（含核心区之外的平台格），平台之外的低地不是；
        // 没有棋盘清单的手写夹具才退回到出生区本身。
        MapData link = Siege.Core.Board.Maps.MapCatalog.Resolve("link:1:4");
        var area = new BirthBoardArea(link);
        BoardPlate plate = link.BirthBoard(0);
        Coord rim = plate.Cells().First(c => link.BirthZoneOf(c) is null && link.IsPlayable(c));
        Coord lowland = link.AllCoords().First(c => link.IsPlayable(c) && link.BirthBoardAt(c) is null);
        Assert.True(area.IsBirthArea(rim));            // 平台上、核心区外
        Assert.False(area.IsBirthArea(lowland));       // 平台之间的低地

        MapData nine = TestMaps.NineByNine();
        var fallback = new BirthBoardArea(nine);
        Assert.True(fallback.IsBirthArea(TestMaps.At("A1")));    // 出生区 0
        Assert.False(fallback.IsBirthArea(TestMaps.At("E5")));
    }

    /// <summary>扣手牌回调里给正式盘面挂一件信物（预演已做完、正式落子尚未开始）。</summary>
    private sealed class AttachingHooks(GameBoard board) : ISettlementHooks
    {
        internal bool Attached { get; private set; }

        public void DeductHand(PlayerId player, IReadOnlyDictionary<PieceType, int> deployed)
        {
            board.AttachRelic(TestMaps.At("F6"), RelicToken.Spawned(9, RelicKind.WarDrum));
            Attached = true;
        }

        public void OnRecalculatePower(SettlementContext context)
        {
        }

        public void OnCheckEndConditions(SettlementContext context)
        {
        }

        public void OnPass(PlayerId player)
        {
        }
    }
}
