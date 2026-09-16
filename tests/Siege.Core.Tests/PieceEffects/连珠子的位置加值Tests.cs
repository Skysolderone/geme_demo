using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.PieceEffects;

/// <summary>规格：piece-effects —— Requirement: 连珠子的位置加值</summary>
public class 连珠子的位置加值Tests
{
    private static GameBoard Lines(PlayerId owner, params string[] cells)
    {
        GameBoard board = TestMaps.Blank(size: 9);
        foreach (string c in cells)
        {
            board.Place(c, owner, PieceType.Line);
        }

        return board;
    }

    [Fact]
    public void 长度为3的横线()
    {
        // 设计文档 §9.2：连珠子 C6/D6/E6 → 3×2 = 6
        // 变异验证 M24：RunBonus 的 `length * (length - 1)` 改为 `length * length` → 红 7，含本测试（9）。
        GameBoard board = Lines(TestMaps.P0, "C6", "D6", "E6");

        GroupPower group = Assert.Single(PowerCalculator.Compute(board).Of(TestMaps.P0).Groups);

        Assert.Equal(6, group.LineBonus);
        Assert.Equal(6, group.PositionBonus);
        Assert.Equal(3 + 6, group.Power);
    }

    [Fact]
    public void 不拆分子区间()
    {
        // 长度 4 的连珠线只计 4×3 = 12，MUST NOT 额外计入长度 2/3 的子区间。
        // 变异验证 M3：RunBonus 去掉"起点反方向若也是连珠子则返回 0"的判断 → 红 6，含本测试（12+6+2 = 20）。
        GameBoard board = Lines(TestMaps.P0, "C6", "D6", "E6", "F6");

        GroupPower group = Assert.Single(PowerCalculator.Compute(board).Of(TestMaps.P0).Groups);

        Assert.Equal(12, group.LineBonus);
    }

    [Fact]
    public void 十字交叉双向计入()
    {
        // D6 同时位于横线 C6-D6-E6（长度 3）与竖线 D6-D7（长度 2）的交点 → 3×2 + 2×1 = 8
        // 变异验证 M11：LineBonus 只做横向扫描（去掉 dy:1 那次 RunBonus）→ 红 1（本测试，6）。
        GameBoard board = Lines(TestMaps.P0, "C6", "D6", "E6", "D7");

        GroupPower group = Assert.Single(PowerCalculator.Compute(board).Of(TestMaps.P0).Groups);

        Assert.Equal(8, group.LineBonus);
    }

    [Fact]
    public void 长度为1不计分()
    {
        // 横向与纵向均无相邻己方连珠子 → 0。斜向相邻（D4/E5）不构成线。
        GameBoard board = Lines(TestMaps.P0, "D4", "E5");

        PlayerPower p0 = PowerCalculator.Compute(board).Of(TestMaps.P0);

        Assert.Equal(2, p0.Groups.Length);
        Assert.All(p0.Groups, g => Assert.Equal(0, g.LineBonus));
    }

    [Fact]
    public void 连珠线不穿越其他类型己方棋子()
    {
        // 裁决记录 1：D4(连珠) - D5(普通) - D6(连珠) 同串但不成线 → 0；连珠线只由连珠子连续构成。
        // 变异验证 M19：IsLineStoneOf 去掉 Type == Line 的判断 → 红 21，含本测试（按 3 子线计 6）。
        GameBoard board = Lines(TestMaps.P0, "D4", "D6").Place("D5", TestMaps.P0, PieceType.Basic);

        GroupPower group = Assert.Single(PowerCalculator.Compute(board).Of(TestMaps.P0).Groups);

        Assert.Equal(3, group.Stones.Length);
        Assert.Equal(0, group.LineBonus);
    }

    [Fact]
    public void 崖壁截断连珠线()
    {
        // terrain-model piece-effects 增量（裁决 A-6）：C6/D6/E6 连珠，D6 h=0、E6 h=2 → C6–D6 成线 2×1 = 2，E6 单独不计，总位置加值 2。
        // E6 与 D6 之间无气边，E6 自成一串；两串各自的 LineBonus 之和就是该玩家的总位置加值。
        // 变异验证 M-B3：PieceEffects.Step 改回 board.Neighbors（几何邻居）→ 本测试红（C6–D6–E6 按 3 子线计 6）。
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(heights: [("E6", 2)]), size: 9);
        foreach (string c in new[] { "C6", "D6", "E6" })
        {
            board.Place(c, TestMaps.P0, PieceType.Line);
        }

        PlayerPower p0 = PowerCalculator.Compute(board).Of(TestMaps.P0);

        Assert.Equal(2, p0.Groups.Length);
        Assert.Equal(2, p0.Groups.Sum(g => g.LineBonus));
        Assert.Equal(2, p0.Groups.Sum(g => g.PositionBonus));
    }

    [Fact]
    public void 栅栏截断连珠线()
    {
        // terrain-model piece-effects 增量：C6/D6/E6 连珠，D6–E6 之间有栅栏 → 总位置加值 2，与崖壁截断相同。
        // 变异验证 M-B3 同上：改回几何邻居 → 红（计 6）。
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(fences: [("D6", "E6")]), size: 9);
        foreach (string c in new[] { "C6", "D6", "E6" })
        {
            board.Place(c, TestMaps.P0, PieceType.Line);
        }

        PlayerPower p0 = PowerCalculator.Compute(board).Of(TestMaps.P0);

        Assert.Equal(2, p0.Groups.Length);
        Assert.Equal(2, p0.Groups.Sum(g => g.LineBonus));
    }

    [Fact]
    public void 连珠线不跨玩家()
    {
        // P0 的 D4-E4 与 P1 的 F4 相邻：P0 计 2×1 = 2，P1 计 0。
        // 变异验证 M25：IsLineStoneOf 去掉 Owner == owner 的判断 → 红 1（本测试，P0 按 3 子线计 6）。
        GameBoard board = Lines(TestMaps.P0, "D4", "E4").Place("F4", TestMaps.P1, PieceType.Line);

        PowerSnapshot snapshot = PowerCalculator.Compute(board);

        Assert.Equal(2, Assert.Single(snapshot.Of(TestMaps.P0).Groups).LineBonus);
        Assert.Equal(0, Assert.Single(snapshot.Of(TestMaps.P1).Groups).LineBonus);
    }
}
