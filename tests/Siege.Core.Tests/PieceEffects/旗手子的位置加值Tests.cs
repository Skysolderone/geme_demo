using Siege.Core.Board;
using Siege.Core.PieceRelics;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.PieceEffects;

/// <summary>规格：more-pieces-relics piece-effects —— Requirement: 旗手子的位置加值（relic-redesign R23 改写）</summary>
/// <remarks>
/// 旗手子：所在棋串每挂 1 件信物 +3（每枚旗手子各计一次；不看信物种类、带入者与保险）；只数挂在本串棋子上的信物，地图信物（空格上）与别的棋串上的不计。
/// 原口径"自身格 ∪ 气边邻格中每个静态信物格 +3"随静态信物格删除（relic-redesign D4）。
/// 用例里的信物多取望台：它只扩大覆盖、不进军势，算例里的军势只多旗手这一项。
/// </remarks>
public class 旗手子的位置加值Tests
{
    private static GameBoard Board() => TestMaps.Blank(size: 11);

    private static GameBoard Attach(GameBoard board, string cell, RelicToken token)
    {
        board.AttachRelic(TestMaps.At(cell), token);
        return board;
    }

    [Fact]
    public void 本串每挂一件信物加三()
    {
        // 规格 Scenario（R23）：旗手子 F6 与普通子 G6 成串，F6、G6 各挂一件望台 → 3 × 2 = 6；军势 = 基础 2 + 旗手 6 = 8（无倍率）。
        GameBoard board = Board().Place("F6", TestMaps.P0, PieceType.Bannerman).Place("G6", TestMaps.P0);
        Attach(board, "F6", RelicToken.Spawned(0, RelicKind.Watchtower));
        Attach(board, "G6", RelicToken.Spawned(1, RelicKind.Watchtower));

        GroupPower group = PowerCalculator.Compute(board).GroupContaining(TestMaps.P0, "F6");

        Assert.Equal(6, group.BannerBonus);
        Assert.Equal(6, group.PositionBonus);
        Assert.Equal(2 + 6, group.Power);
    }

    [Fact]
    public void 没有信物时为零()
    {
        // 反面：同一棋串不挂信物 → 旗手加值 0（原"站在信物格上"的口径已不存在：格子本身没有信物属性）。
        GameBoard board = Board().Place("F6", TestMaps.P0, PieceType.Bannerman).Place("G6", TestMaps.P0);

        Assert.Equal(0, PowerCalculator.Compute(board).GroupContaining(TestMaps.P0, "F6").BannerBonus);
    }

    [Fact]
    public void 只数本串上的信物()
    {
        // 规格 Scenario：旗手子 F6 单独成串；气边相邻的空格 F7 上有一件地图信物、相邻的敌子 G6 挂着一件、己方另一串 H8 也挂着一件 → 都不计，0。
        // 变异 M-B2（BannerBonus 改数全盘信物）→ 本测试红。
        GameBoard board = Board().Place("F6", TestMaps.P0, PieceType.Bannerman).Place("G6", TestMaps.P1).Place("H8", TestMaps.P0);
        Attach(board, "F7", RelicToken.Spawned(0, RelicKind.WarDrum));
        Attach(board, "G6", RelicToken.Spawned(1, RelicKind.Watchtower));
        Attach(board, "H8", RelicToken.Spawned(2, RelicKind.Watchtower));

        PlayerPower p0 = PowerCalculator.Compute(board).Of(TestMaps.P0);

        Assert.Equal(2, p0.Groups.Length);   // 前提：F6 与 H8 分属两串
        Assert.Equal(0, p0.Groups.Single(g => g.Stones.Contains(TestMaps.At("F6"))).BannerBonus);
    }

    [Fact]
    public void 两枚旗手子各计一次()
    {
        // 规格 Scenario：同一棋串里两枚旗手子 F6、G6，本串挂 1 件信物（挂在 G6 上）→ 每枚各 3，合计 6。
        // 变异 M-B1（BannerBonus 不乘旗手子枚数）→ 本测试红。
        GameBoard board = Board().Place("F6", TestMaps.P0, PieceType.Bannerman).Place("G6", TestMaps.P0, PieceType.Bannerman);
        Attach(board, "G6", RelicToken.Spawned(0, RelicKind.Watchtower));

        GroupPower group = PowerCalculator.Compute(board).GroupContaining(TestMaps.P0, "F6");

        Assert.Equal(6, group.BannerBonus);
        Assert.Equal(2 + 6, group.Power);
    }

    [Fact]
    public void 不看信物种类与带入者()
    {
        // R23："每挂 1 件信物"——别人带入的保险件挂在本串上同样计入（持有者即该格棋子的主人，D1）。
        // 两串各挂一件：一件地图刷出的望台、一件 P1 带入的保险望台 → 两串旗手加值都是 3。
        GameBoard board = Board().Place("C3", TestMaps.P0, PieceType.Bannerman).Place("J9", TestMaps.P0, PieceType.Bannerman);
        Attach(board, "C3", RelicToken.Spawned(0, RelicKind.Watchtower));
        Attach(board, "J9", RelicToken.Carried(1, RelicKind.Watchtower, TestMaps.P1, insured: true));

        PlayerPower p0 = PowerCalculator.Compute(board).Of(TestMaps.P0);

        Assert.Equal(2, p0.Groups.Length);
        Assert.All(p0.Groups, g => Assert.Equal(3, g.BannerBonus));
    }

    [Fact]
    public void 持物棋子被提后旗手加值随之消失()
    {
        // 全量重算（boundaries「全量重算」）：G6 挂着信物的子被移走后信物留在空格上（R4），本串不再挂件 → 旗手加值由 3 变 0。
        GameBoard board = Board().Place("F6", TestMaps.P0, PieceType.Bannerman).Place("G6", TestMaps.P0);
        Attach(board, "G6", RelicToken.Spawned(0, RelicKind.Watchtower));
        Assert.Equal(3, PowerCalculator.Compute(board).GroupContaining(TestMaps.P0, "F6").BannerBonus);

        board.RemoveStones([TestMaps.At("G6")]);

        Assert.Equal(0, PowerCalculator.Compute(board).GroupContaining(TestMaps.P0, "F6").BannerBonus);
        Assert.Equal(RelicKind.Watchtower, board.RelicAt(TestMaps.At("G6"))?.Kind);
    }
}
