using Siege.Core.Board;

namespace Siege.Core.Tests.TerrainSpec;

/// <summary>规格：terrain —— Requirement: 气边</summary>
public class 气边Tests
{
    [Fact]
    public void 崖壁切断气()
    {
        // 孤子在 h=0 的 F6，上方 F7 为 h=2 的空格，其余三面为 h=0 或 h=1 的空格 → 气数 3，F7 不计入
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(heights: [("F7", 2), ("E6", 1)]))
            .Place("F6", TestMaps.P0);

        Assert.Equal(["F5", "E6", "G6"], board.LibertiesOf(board.GroupAt(TestMaps.At("F6"))!).Notations());
    }

    [Fact]
    public void 缓坡不切断气()
    {
        // 同一玩家的两枚棋子分别位于 h=1 的 F6 与 h=2 的 F7 → 同一棋串
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(heights: [("F6", 1), ("F7", 2)]))
            .Place("F6", TestMaps.P0)
            .Place("F7", TestMaps.P0);

        Group group = board.GroupAt(TestMaps.At("F6"))!;
        Assert.Equal(2, group.Size);
        Assert.Equal(["F6", "F7"], group.Stones.Notations());
    }

    [Fact]
    public void 栅栏切断气与连接()
    {
        // 同一玩家的两枚棋子位于 F6 与 G6，两格之间有栅栏 → 分属两个棋串，且 G6 不是 F6 所在棋串的气
        // 变异验证 M-A1：Adjacency.LibertyNeighbors 去掉 HasFence 判断 → 本测试红。
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(fences: [("F6", "G6")]))
            .Place("F6", TestMaps.P0)
            .Place("G6", TestMaps.P0);

        Assert.Equal(1, board.GroupAt(TestMaps.At("F6"))!.Size);
        Assert.Equal(1, board.GroupAt(TestMaps.At("G6"))!.Size);

        // G6 腾空后仍然不是 F6 的气：栅栏切的是边，不是占用
        board.Clear(TestMaps.At("G6"));
        Assert.Equal(["F5", "E6", "F7"], board.LibertiesOf(board.GroupAt(TestMaps.At("F6"))!).Notations());
        Assert.DoesNotContain(TestMaps.At("G6"), board.LibertyNeighbors(TestMaps.At("F6")));
    }

    [Fact]
    public void 深水切断气()
    {
        // 孤子在 F6，右侧 G6 为未架桥的深水，其余三面为空 → 气数 3
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("G6", Surface.DeepWater)]))
            .Place("F6", TestMaps.P0);

        Assert.Equal(["F5", "E6", "F7"], board.LibertiesOf(board.GroupAt(TestMaps.At("F6"))!).Notations());
    }

    [Fact]
    public void 桥恢复气()
    {
        // 上一场景中 G6 架有预置桥且为空 → 气数 4
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("G6", Surface.DeepWater)], bridges: ["G6"]))
            .Place("F6", TestMaps.P0);

        Assert.Equal(["F5", "E6", "G6", "F7"], board.LibertiesOf(board.GroupAt(TestMaps.At("F6"))!).Notations());
    }

    [Fact]
    public void 气边对称()
    {
        // 任取两格 a、b：a→b 有气边 ⇔ b→a 有气边。用一张同时含崖壁、缓坡、深水、桥、栅栏、林地与障碍的 9×9 图跑全对。
        GameBoard board = 地形齐全的盘面();
        MapData map = board.Map;

        int edges = 0;
        int blockedGeometric = 0;
        foreach (Coord a in map.AllCoords())
        {
            foreach (Coord b in map.AllCoords())
            {
                bool ab = board.LibertyNeighbors(a).Contains(b);
                bool ba = board.LibertyNeighbors(b).Contains(a);
                Assert.True(ab == ba, $"{a}→{b} = {ab}，但 {b}→{a} = {ba}");
                if (ab)
                {
                    edges++;
                }
                else if (board.Neighbors(a).Contains(b))
                {
                    blockedGeometric++;
                }
            }
        }

        // 样本口径：既要有气边，也要有"几何相邻但无气边"的对，否则这条属性测试是空的
        Assert.True(edges > 0);
        Assert.True(blockedGeometric > 0);
    }

    [Fact]
    public void 不可落子格没有气边()
    {
        // 障碍与未架桥深水自身不发出气边，桥格发出
        GameBoard board = 地形齐全的盘面();

        Assert.Empty(board.LibertyNeighbors(TestMaps.At("C3")));
        Assert.Empty(board.LibertyNeighbors(TestMaps.At("E5")));
        Assert.NotEmpty(board.LibertyNeighbors(TestMaps.At("E6")));
    }

    /// <summary>9×9：C3 障碍；E5/E6/E7 深水，E6 架桥；F7 h=2、G7 h=1、H7 h=2；B2-B3 栅栏；D4 林地。</summary>
    private static GameBoard 地形齐全的盘面() =>
        TestMaps.Blank(
            TestMaps.Terrain(
                heights: [("F7", 2), ("G7", 1), ("H7", 2), ("F8", 1)],
                surfaces: [("E5", Surface.DeepWater), ("E6", Surface.DeepWater), ("E7", Surface.DeepWater), ("D4", Surface.Forest)],
                bridges: ["E6"],
                fences: [("B2", "B3"), ("G3", "H3")]),
            size: 9,
            "C3");
}
