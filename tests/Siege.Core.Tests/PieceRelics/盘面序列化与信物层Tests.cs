using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.PieceRelics;
using static Siege.Core.Tests.PieceRelics.PieceRelicFixtures;

namespace Siege.Core.Tests.PieceRelics;

/// <summary>
/// relic-redesign design.md D1 的序列化拆分守门：<see cref="GameBoard.SerializeShape"/>（棋子 + 改造，不含信物层）给同形比对，
/// <see cref="GameBoard.Serialize"/>（含信物层）给存档与结算核对。<b>信物层为空时两者是同一个串，与信物层上线之前逐字节相同</b>；
/// 同形禁则不比较信物层（R28）。
/// </summary>
/// <remarks>
/// 期望串一律在测试内手写（每格两字符、行自下而上、行间 <c>/</c>；信物段 <c>#记法:编号:类型码:带入者:保险</c>），不调用被测的写出拼期望。
/// 变异验证（加法段实跑，过滤本目录）：M2 Serialize 空层时也追加 <c>#</c> → 红 3（空层两条 + 挂载使序列化记忆失效）；
/// M1 SuperkoKey 不剥信物段（条件改为恒假）→ 红 2（同形比对键不含信物层、已提交盘面历史把只差信物的盘面判为同形）。
/// </remarks>
public class 盘面序列化与信物层Tests
{
    /// <summary>3×3 小盘：A1 有 P0 普通子、C3 有 P1 堡垒子。</summary>
    private static GameBoard Small() => TestMaps.Blank(size: 3).Place("A1", TestMaps.P0).Place("C3", TestMaps.P1, PieceType.Fortress);

    private const string SmallShape = "0B----/------/----1F";

    [Fact]
    public void 空信物层时完整序列化与棋形序列化逐字节相同()
    {
        GameBoard board = Small();

        Assert.Equal(SmallShape, board.SerializeShape());
        Assert.Equal(SmallShape, board.Serialize());
        Assert.Same(board.SerializeShape(), board.Serialize());
        Assert.DoesNotContain('#', board.Serialize());
    }

    [Fact]
    public void 空信物层时带改造的序列化同样不变()
    {
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("B2", Surface.Forest)]), size: 3).Place("A1", TestMaps.P0);
        board.ApplyTerrainEdits([TerrainEdit.Burn(TestMaps.At("B2"))]);

        string expected = board.SerializeShape();
        Assert.Contains('|', expected);
        Assert.Equal(expected, board.Serialize());
        Assert.DoesNotContain('#', board.Serialize());
    }

    [Fact]
    public void 非空信物层时在末尾追加信物段()
    {
        GameBoard board = Small()
            .Attach("C3", Carried(7, RelicKind.Banner, TestMaps.P1, insured: true))
            .Attach("A2", Map(0, RelicKind.Watchtower));

        Assert.Equal(SmallShape, board.SerializeShape());
        Assert.Equal(SmallShape + "#A2:0:W:-:0,C3:7:G:1:1", board.Serialize());
    }

    [Fact]
    public void 挂载使序列化记忆失效()
    {
        GameBoard board = Small();
        string before = board.Serialize();

        board.AttachRelic(TestMaps.At("B2"), Map(4, RelicKind.Chameleon));

        Assert.Equal(before + "#B2:4:V:-:0", board.Serialize());
        Assert.Equal(before, board.SerializeShape());
    }

    [Fact]
    public void 含信物层的存档往返()
    {
        GameBoard board = Small()
            .Attach("A1", Carried(2, RelicKind.WarDrum, TestMaps.P0))
            .Attach("B2", Map(5, RelicKind.Command))
            .Attach("C3", Carried(9, RelicKind.Banner, TestMaps.P1, insured: true));
        string saved = board.Serialize();

        GameBoard restored = GameBoard.RestoreUnvalidated(board.Map, saved);

        Assert.Equal(saved, restored.Serialize());
        Assert.Equal(Carried(2, RelicKind.WarDrum, TestMaps.P0), restored.RelicAt(TestMaps.At("A1")));
        Assert.Equal(Map(5, RelicKind.Command), restored.RelicAt(TestMaps.At("B2")));
        Assert.Equal(Carried(9, RelicKind.Banner, TestMaps.P1, insured: true), restored.RelicAt(TestMaps.At("C3")));
    }

    [Fact]
    public void 含改造与信物层的存档往返()
    {
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("B2", Surface.Forest)]), size: 3).Place("A1", TestMaps.P0);
        board.ApplyTerrainEdits([TerrainEdit.Burn(TestMaps.At("B2"))]);
        board.AttachRelic(TestMaps.At("B2"), Map(0, RelicKind.WarDrum));
        string saved = board.Serialize();

        // 存档只存本局的改造：恢复时从开局地图起（改造段先写、信物最后挂到改造后的地形上）。
        GameBoard restored = GameBoard.RestoreUnvalidated(board.BaseMap, saved);

        Assert.Equal(saved, restored.Serialize());
        Assert.Equal(Map(0, RelicKind.WarDrum), restored.RelicAt(TestMaps.At("B2")));
    }

    [Fact]
    public void 同形比对键不含信物层()
    {
        // R28：只有信物位置不同的两个盘面算同形；喂完整序列化或棋形序列化得到同一个键。
        GameBoard a = Small().Attach("B2", Map(0, RelicKind.WarDrum));
        GameBoard b = Small().Attach("A3", Map(0, RelicKind.Banner));

        Assert.NotEqual(a.Serialize(), b.Serialize());
        Assert.Equal(GameBoard.SuperkoKey(a.Serialize()), GameBoard.SuperkoKey(b.Serialize()));
        Assert.Equal(GameBoard.SuperkoKey(Small().Serialize()), GameBoard.SuperkoKey(a.Serialize()));
        Assert.Equal(GameBoard.SuperkoKey(a.SerializeShape()), GameBoard.SuperkoKey(a.Serialize()));
    }

    [Fact]
    public void 已提交盘面历史把只差信物的盘面判为同形()
    {
        var history = new BoardHistory();
        history.Record(Small().Serialize());

        Assert.Equal(1, history.FindDuplicate(Small().Attach("B2", Map(0, RelicKind.WarDrum)).Serialize()));
    }

    [Fact]
    public void 挂出只在一侧生效时完整序列化不同()
    {
        // 结算核对比较的是完整序列化：正式盘面与预演副本只差一件信物时必须看得出来（棋形序列化看不出来）。
        GameBoard formal = Small();
        GameBoard rehearsal = formal.Clone();
        rehearsal.AttachRelic(TestMaps.At("A1"), Carried(0, RelicKind.WarDrum, TestMaps.P0));

        Assert.Equal(formal.SerializeShape(), rehearsal.SerializeShape());
        Assert.NotEqual(formal.Serialize(), rehearsal.Serialize());
    }

    [Theory]
    [InlineData(SmallShape + "#")]
    [InlineData(SmallShape + "#A2:x:W:-:0")]
    [InlineData(SmallShape + "#A2:0:Q:-:0")]
    [InlineData(SmallShape + "#A2:0:W:-:1")]
    [InlineData(SmallShape + "#A2:0:W:-")]
    [InlineData(SmallShape + "#A2:0:W:g:0")]
    [InlineData(SmallShape + "#A2:0:W:-:0,A2:1:D:-:0")]
    [InlineData(SmallShape + "#A2:0:W:-:0,B2:0:D:-:0")]
    [InlineData(SmallShape + "#D4:0:W:-:0")]
    public void 信物段格式不符即视为存档损坏(string serialized)
    {
        Assert.Throws<FormatException>(() => GameBoard.RestoreUnvalidated(Small().Map, serialized));
    }

    [Fact]
    public void 已有信物的盘不能写入存档盘面()
    {
        GameBoard board = TestMaps.Blank(size: 3).Attach("B2", Map(0, RelicKind.WarDrum));

        Assert.Throws<SiegeRuleException>(() => board.LoadState(SmallShape));
    }
}
