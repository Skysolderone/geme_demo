using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.PieceEffects;

/// <summary>规格：piece-effects —— Requirement: 高地压制加值（scoring-sites 2.3）</summary>
/// <remarks>
/// 变异验证（段 A2，均为 <c>PieceEffects.HighGroundBonus</c>）：
/// M-H1 去掉"严格低于"（<c>&lt;</c> 改 <c>&lt;=</c>）→ 红，含「同高不加」；
/// M-H2 去掉"每枚至多 1"（删 <c>break</c>）→ 红，含「多个低处敌子只加1」；
/// M-H3 改走几何邻居（<c>CoverageTargets</c> 换成 <c>Neighbors</c>）→ 红，含「林地里的敌子压制不到」「隔河压制」。
/// </remarks>
public class 高地压制加值Tests
{
    private static GroupPower GroupAt(GameBoard board, PlayerId owner, string cell) =>
        PowerCalculator.Compute(board).GroupContaining(owner, cell);

    [Fact]
    public void 跨崖居高临下()
    {
        // 规格 Scenario：A 位于 h=2 的 F7，B 位于几何相邻的 h=0 的 F6 → A 的棋子提供 1 点；B 的不提供。
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(heights: [("F7", 2)])).Place("F7", TestMaps.P0).Place("F6", TestMaps.P1);

        Assert.Equal(1, GroupAt(board, TestMaps.P0, "F7").HighGroundBonus);
        Assert.Equal(0, GroupAt(board, TestMaps.P1, "F6").HighGroundBonus);
        Assert.Equal(2, GroupAt(board, TestMaps.P0, "F7").Power);   // 基础 1 + 高地 1
    }

    [Fact]
    public void 缓坡压制()
    {
        // 规格 Scenario：A 位于 h=1 的 F6，B 位于 h=0 的 G6 → A 提供 1 点。
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(heights: [("F6", 1)])).Place("F6", TestMaps.P0).Place("G6", TestMaps.P1);

        Assert.Equal(1, GroupAt(board, TestMaps.P0, "F6").HighGroundBonus);
        Assert.Equal(0, GroupAt(board, TestMaps.P1, "G6").HighGroundBonus);
    }

    [Fact]
    public void 同高不加()
    {
        // 规格 Scenario：A 与 B 的棋子相邻且同高（平地与 h=2 高台各一组）→ 双方都不获得高地加值。
        GameBoard flat = TestMaps.Blank().Place("E5", TestMaps.P0).Place("F5", TestMaps.P1);
        Assert.Equal((0, 0), (GroupAt(flat, TestMaps.P0, "E5").HighGroundBonus, GroupAt(flat, TestMaps.P1, "F5").HighGroundBonus));

        GameBoard plateau = TestMaps.Blank(TestMaps.Terrain(heights: [("E5", 2), ("F5", 2)])).Place("E5", TestMaps.P0).Place("F5", TestMaps.P1);
        Assert.Equal((0, 0), (GroupAt(plateau, TestMaps.P0, "E5").HighGroundBonus, GroupAt(plateau, TestMaps.P1, "F5").HighGroundBonus));
    }

    [Fact]
    public void 林地里的敌子压制不到()
    {
        // 规格 Scenario：A 位于 h=1 的 F6，B 位于 h=0 的林地 G6 → G6 不是 A 的覆盖目标，A 不获得高地加值。
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(heights: [("F6", 1)], surfaces: [("G6", Surface.Forest)]))
            .Place("F6", TestMaps.P0).Place("G6", TestMaps.P1);

        Assert.DoesNotContain(TestMaps.At("G6"), board.CoverageTargets(TestMaps.At("F6")));   // 前提：几何相邻但不是覆盖目标
        Assert.Equal(0, GroupAt(board, TestMaps.P0, "F6").HighGroundBonus);
    }

    [Fact]
    public void 隔河压制()
    {
        // 规格 Scenario：A 位于 h=1 的 F6，G6 为未架桥深水，B 位于 h=0 的 H6 → A 提供 1 点。
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(heights: [("F6", 1)], surfaces: [("G6", Surface.DeepWater)]))
            .Place("F6", TestMaps.P0).Place("H6", TestMaps.P1);

        Assert.Contains(TestMaps.At("H6"), board.CoverageTargets(TestMaps.At("F6")));   // 前提：隔一格深水落到对岸
        Assert.Equal(1, GroupAt(board, TestMaps.P0, "F6").HighGroundBonus);
    }

    [Fact]
    public void 多个低处敌子只加1()
    {
        // 规格 Scenario：A 位于 h=2 的一枚棋子能覆盖到三枚更低处的敌子 → 只提供 1 点。
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(heights: [("F7", 2)]))
            .Place("F7", TestMaps.P0).Place("F6", TestMaps.P1).Place("E7", TestMaps.P1).Place("G7", ScoringFixtures.P2);

        Assert.Equal(1, GroupAt(board, TestMaps.P0, "F7").HighGroundBonus);
    }

    [Fact]
    public void 己方棋子不算()
    {
        // 规格 Scenario：A 位于 h=2，其覆盖目标上只有 A 自己在 h=0 的棋子 → 不提供高地加值。
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(heights: [("F7", 2)])).Place("F7", TestMaps.P0).Place("F6", TestMaps.P0);

        Assert.Equal(2, PowerCalculator.Compute(board).Of(TestMaps.P0).Groups.Length);   // 前提：崖壁切断气边，两枚各成一串
        Assert.Equal(0, GroupAt(board, TestMaps.P0, "F7").HighGroundBonus);
    }

    [Fact]
    public void 按棋串求和且每枚棋子独立判定()
    {
        // Requirement 正文：高地加值按棋串求和计入位置加值；弃赛者的遗留棋子同样判定（不看玩家状态）。
        // 第 7 行 B7–E7 为 h=2 的一条棋串，其中 B7、C7、D7 下方各有一枚 h=0 敌子（D6 属已弃赛玩家），E7 下方无敌子 → 3。
        TerrainData terrain = TestMaps.Terrain(heights: [("B7", 2), ("C7", 2), ("D7", 2), ("E7", 2)]);
        GameBoard board = TestMaps.Blank(terrain)
            .Place("B7", TestMaps.P0).Place("C7", TestMaps.P0).Place("D7", TestMaps.P0).Place("E7", TestMaps.P0)
            .Place("B6", TestMaps.P1).Place("C6", TestMaps.P1).Place("D6", ScoringFixtures.P3);

        PowerSnapshot snapshot = PowerCalculator.Compute(board, ScoringFixtures.Roster(
            (TestMaps.P0, PlayerStatus.Active), (TestMaps.P1, PlayerStatus.Active), (ScoringFixtures.P3, PlayerStatus.Resigned)));

        GroupPower group = Assert.Single(snapshot.Of(TestMaps.P0).Groups);
        Assert.Equal(3, group.HighGroundBonus);
        Assert.Equal(3, group.PositionBonus);
        Assert.Equal(4 + 3, group.Power);
    }

    [Fact]
    public void 高地加值随倍率放大()
    {
        // restore-go-core-rules piece-effects 规格：含普通子×2、倍增子×1 的棋串中有 2 枚棋子各提供 1 点高地加值 → 军势 ⌊(3 + 2) × 1.5⌋ = ⌊7.5⌋ = 7
        //（加值不进倍率会得 ⌊3 × 1.5⌋ + 2 = 6）。第 7 行 B7–D7 为 h=2：B7、C7 下方各有一枚 h=0 敌子，倍增子 D7 下方无敌子。
        // 变异验证 M-A1（段 A：加值挪到乘法之外）→ 红，含本测试（6）。
        TerrainData terrain = TestMaps.Terrain(heights: [("B7", 2), ("C7", 2), ("D7", 2)]);
        GameBoard board = TestMaps.Blank(terrain)
            .Place("B7", TestMaps.P0).Place("C7", TestMaps.P0).Place("D7", TestMaps.P0, PieceType.Multiplier)
            .Place("B6", TestMaps.P1).Place("C6", TestMaps.P1);

        GroupPower group = PowerCalculator.Compute(board).GroupContaining(TestMaps.P0, "B7");

        Assert.Equal((3, 2, 1), (group.BaseTotal, group.HighGroundBonus, group.MultiplierCount));
        Assert.Equal(7, group.Power);
        Assert.NotEqual(6, group.Power);
    }

    [Fact]
    public void 沼泽上的棋子没有压制()
    {
        // 规格 terrain-surfaces · piece-effects「沼泽上的棋子没有压制」（裁决 S-2′）：A 在 h=1 的沼泽 F6，B 在 h=0 的 G6 → A 无高地加值；
        // 压制沿用唯一覆盖关系，不设地表例外。对照：同样布置在草地上为 1（「缓坡压制」）。
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(heights: [("F6", 1)], surfaces: [("F6", Surface.Marsh)]))
            .Place("F6", TestMaps.P0).Place("G6", TestMaps.P1);

        Assert.DoesNotContain(TestMaps.At("G6"), board.CoverageTargets(TestMaps.At("F6")));
        Assert.Equal(0, GroupAt(board, TestMaps.P0, "F6").HighGroundBonus);
    }

    [Fact]
    public void 岩台远格压制()
    {
        // 规格 terrain-surfaces · piece-effects「岩台远格压制」：A 在 h=1 的岩台 F6，G6 为 h=1 空格，B 在 h=0 的 H6 → A 提供 1 点高地加值。
        // 对照：F6 若是草地，H6 不是覆盖目标，没有加值。
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(heights: [("F6", 1), ("G6", 1)], surfaces: [("F6", Surface.Crag)]))
            .Place("F6", TestMaps.P0).Place("H6", TestMaps.P1);
        GameBoard grass = TestMaps.Blank(TestMaps.Terrain(heights: [("F6", 1), ("G6", 1)]))
            .Place("F6", TestMaps.P0).Place("H6", TestMaps.P1);

        Assert.Equal(1, GroupAt(board, TestMaps.P0, "F6").HighGroundBonus);
        Assert.Equal(0, GroupAt(grass, TestMaps.P0, "F6").HighGroundBonus);
    }
}
