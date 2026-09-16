using Siege.Core.Board;
using Siege.Core.Relics;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.TerrainSpec;

/// <summary>规格：terrain —— Requirement: 覆盖关系</summary>
public class 覆盖关系Tests
{
    [Fact]
    public void 居高临下()
    {
        // 玩家 A 的棋子位于 h=2 的 F7，其下方 F6 为 h=0 的空格 → F6 获得 A 的覆盖
        // 变异验证 M-A2：Adjacency.CoverageTargets 把 h_t − h_s ≤ 1 写成 |Δh| ≤ 1（覆盖对称化）→ 本测试红。
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(heights: [("F7", 2)])).Place("F7", TestMaps.P0);

        Assert.Equal(TestMaps.P0, CoverageMap.Compute(board).UniqueCoverer(TestMaps.At("F6")));
        Assert.Contains(TestMaps.At("F6"), board.CoverageTargets(TestMaps.At("F7")));
    }

    [Fact]
    public void 仰视不覆盖()
    {
        // 玩家 A 的棋子位于 h=0 的 F6，其上方 F7 为 h=2 的空格 → F7 不获得覆盖
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(heights: [("F7", 2)])).Place("F6", TestMaps.P0);

        Assert.Equal(CellCoverage.None, CoverageMap.Compute(board).CoverageOf(TestMaps.At("F7")));
        Assert.Equal(["F5", "E6", "G6"], board.CoverageTargets(TestMaps.At("F6")).Notations());
    }

    [Fact]
    public void 缓坡可覆盖()
    {
        // h=0 的 F6 向 h=1 的 F7 覆盖（Δh = 1 不是崖壁）
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(heights: [("F7", 1)])).Place("F6", TestMaps.P0);

        Assert.Equal(TestMaps.P0, CoverageMap.Compute(board).UniqueCoverer(TestMaps.At("F7")));
    }

    [Fact]
    public void 林地不接收覆盖()
    {
        // 玩家 A 的棋子位于 F6，其右侧 G6 为空的林地信物格 → G6 无覆盖；信物不因此被发现
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(
            TestMaps.Terrain(surfaces: [("G6", Surface.Forest)]),
            ("G6", RelicFixtures.Command()));
        board.Place("F6", TestMaps.P0);

        Assert.Equal(CellCoverage.None, CoverageMap.Compute(board).CoverageOf(TestMaps.At("G6")));
        Assert.Empty(ledger.Settle(board, majorRound: 1));
        Assert.False(ledger.IsRevealed(TestMaps.At("G6")));
        Assert.Equal(RelicControl.Uncontrolled, ledger.ControlOf(TestMaps.At("G6")));
    }

    [Fact]
    public void 林地上的棋子仍向外覆盖()
    {
        // 玩家 A 的棋子位于林地格 G6，其右侧 H6 为空的草地 → H6 获得 A 的覆盖
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("G6", Surface.Forest)])).Place("G6", TestMaps.P0);

        Assert.Equal(TestMaps.P0, CoverageMap.Compute(board).UniqueCoverer(TestMaps.At("H6")));
        Assert.Equal(["G5", "F6", "H6", "G7"], board.CoverageTargets(TestMaps.At("G6")).Notations());
    }

    [Fact]
    public void 栅栏不挡覆盖()
    {
        // 玩家 A 的棋子位于 F6，F6-G6 之间有栅栏，G6 为空草地 → G6 获得覆盖，但不是 F6 所在棋串的气
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(fences: [("F6", "G6")])).Place("F6", TestMaps.P0);

        Assert.Equal(TestMaps.P0, CoverageMap.Compute(board).UniqueCoverer(TestMaps.At("G6")));
        Assert.DoesNotContain(TestMaps.At("G6"), board.LibertiesOf(board.GroupAt(TestMaps.At("F6"))!));
    }

    [Fact]
    public void 隔岸覆盖()
    {
        // 玩家 A 的棋子位于 F6，G6 为未架桥深水，H6 为同高空草地 → H6 获得覆盖，G6 不记录覆盖
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("G6", Surface.DeepWater)])).Place("F6", TestMaps.P0);
        CoverageMap coverage = CoverageMap.Compute(board);

        Assert.Equal(TestMaps.P0, coverage.UniqueCoverer(TestMaps.At("H6")));
        Assert.Equal(CellCoverage.None, coverage.CoverageOf(TestMaps.At("G6")));
        Assert.Equal(OwnershipKind.Obstacle, coverage.OwnershipOf(TestMaps.At("G6")).Kind);
        Assert.Equal(["F5", "E6", "H6", "F7"], board.CoverageTargets(TestMaps.At("F6")).Notations());
    }

    [Fact]
    public void 隔岸的桥格可被覆盖()
    {
        // 对岸的桥格按普通格处理：F6 → 深水 G6 → 桥格 H6（H6 也是深水但架桥）
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(
            surfaces: [("G6", Surface.DeepWater), ("H6", Surface.DeepWater)], bridges: ["H6"])).Place("F6", TestMaps.P0);

        Assert.Equal(TestMaps.P0, CoverageMap.Compute(board).UniqueCoverer(TestMaps.At("H6")));
    }

    [Fact]
    public void 隔岸不能跨崖()
    {
        // 对岸 H6 比 F6 高 2 → 不覆盖；对岸 H6 是林地 → 不覆盖
        GameBoard cliff = TestMaps.Blank(TestMaps.Terrain(
            heights: [("H6", 2)], surfaces: [("G6", Surface.DeepWater)])).Place("F6", TestMaps.P0);
        GameBoard forest = TestMaps.Blank(TestMaps.Terrain(
            surfaces: [("G6", Surface.DeepWater), ("H6", Surface.Forest)])).Place("F6", TestMaps.P0);

        Assert.Equal(CellCoverage.None, CoverageMap.Compute(cliff).CoverageOf(TestMaps.At("H6")));
        Assert.Equal(CellCoverage.None, CoverageMap.Compute(forest).CoverageOf(TestMaps.At("H6")));
    }

    [Fact]
    public void 隔岸对岸为林地不覆盖()
    {
        // 主会话核实段 A 时的变异（隔岸分支把 Receives 换成 IsPlayable）只被"隔岸不能跨崖"抓住，
        // 说明对岸林地这一条件没有测试钉住；规格覆盖关系第 2 步要求 u "不是林地"。
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("G6", Surface.DeepWater), ("H6", Surface.Forest)])).Place("F6", TestMaps.P0);
        CoverageMap coverage = CoverageMap.Compute(board);

        Assert.Equal(CellCoverage.None, coverage.CoverageOf(TestMaps.At("H6")));
        Assert.Equal(["F5", "E6", "F7"], board.CoverageTargets(TestMaps.At("F6")).Notations());
    }

    [Fact]
    public void 宽河不可隔岸()
    {
        // 玩家 A 的棋子位于 F6，G6 与 H6 都是未架桥深水 → G6、H6、J6 均不获得覆盖
        // 变异验证 M-A3：CoverageTargets 遇深水后继续沿同方向再走一格（穿两格水）→ 本测试红。
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(
            surfaces: [("G6", Surface.DeepWater), ("H6", Surface.DeepWater)])).Place("F6", TestMaps.P0);
        CoverageMap coverage = CoverageMap.Compute(board);

        Assert.Equal(CellCoverage.None, coverage.CoverageOf(TestMaps.At("G6")));
        Assert.Equal(CellCoverage.None, coverage.CoverageOf(TestMaps.At("H6")));
        Assert.Equal(CellCoverage.None, coverage.CoverageOf(TestMaps.At("J6")));
        Assert.Equal(["F5", "E6", "F7"], board.CoverageTargets(TestMaps.At("F6")).Notations());
    }

    [Fact]
    public void 隔岸对岸越界不覆盖()
    {
        // 7×7：G6 是最右列，G6 深水，对岸越界 → 什么都不覆盖，也不抛
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("G6", Surface.DeepWater)]), size: 7).Place("F6", TestMaps.P0);

        Assert.Equal(["F5", "E6", "F7"], board.CoverageTargets(TestMaps.At("F6")).Notations());
    }

    [Fact]
    public void 障碍不可隔岸也不被覆盖()
    {
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("G6", Surface.DeepWater)]), 11, "H6", "E6").Place("F6", TestMaps.P0);

        Assert.Equal(["F5", "F7"], board.CoverageTargets(TestMaps.At("F6")).Notations());
    }

    [Fact]
    public void 覆盖不等于气()
    {
        // 盘面有崖壁：P0 在 h=2 的 F7，F6 为 h=0（崖下），E7/G7/F8 为 h=1（缓坡）→ 覆盖集含 F6，气集不含；两集合不同
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(heights: [("F7", 2), ("E7", 1), ("G7", 1), ("F8", 1)])).Place("F7", TestMaps.P0);
        CoverageMap coverage = CoverageMap.Compute(board);

        string[] covered = [.. board.AllCoords().Where(c => coverage.UniqueCoverer(c) == TestMaps.P0).Notations()];
        string[] liberties = board.LibertiesOf(board.GroupAt(TestMaps.At("F7"))!).Notations();

        Assert.Equal(["F6", "E7", "G7", "F8"], covered);
        Assert.Equal(["E7", "G7", "F8"], liberties);
        Assert.NotEqual(covered, liberties);
        Assert.Equal(["F6"], covered.Except(liberties));
    }
}
