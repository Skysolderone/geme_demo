using System.Text.RegularExpressions;
using Siege.Core.Board;
using static Siege.Core.Tests.LifeShapeFixtures;

namespace Siege.Core.Tests.LifeShape;

/// <summary>规格：openspec/changes/life-shape/specs/life-shape —— Requirement: 空区与封闭眼空间</summary>
public class 空区与封闭眼空间Tests
{
    [Fact]
    public void 单格眼()
    {
        // E5 的四个气边邻格 D5 / F5 / E4 / E6 都是 A 的棋子（四条互不相连的单子棋串）；J1 的 B 子让外部大空区不封闭。
        GameBoard board = Grid([
            ".........",
            "....0....",
            "...0.0...",
            "....0....",
            ".........",
            ".........",
            "........1",
        ]);
        LifeShapeReport report = LifeShapeReport.Analyze(board);

        EyeSpace eye = report.EyeSpaceAt(TestMaps.At("E5"))!;
        Assert.Equal(["E5"], eye.Cells());
        Assert.Equal(A, eye.Owner);
        Assert.Equal(1, eye.EyeValue);
        foreach (string stone in new[] { "D5", "F5", "E4", "E6" })
        {
            Assert.Equal(["E5"], report.EyeSpacesOf(stone));
        }

        Assert.Equal(4, eye.GroupIndices.Length);
        Assert.Null(report.EyeSpaceAt(TestMaps.At("A1")));
        Assert.Single(report.EyeSpaces);
    }

    [Fact]
    public void 贴到敌子不封闭()
    {
        // 同上局面，E6 换成 B 的棋子：E5 同时贴 A 与 B，是公气。
        GameBoard board = Grid([
            ".........",
            "....1....",
            "...0.0...",
            "....0....",
            ".........",
            ".........",
            "........1",
        ]);
        LifeShapeReport report = LifeShapeReport.Analyze(board);

        Assert.Null(report.EyeSpaceAt(TestMaps.At("E5")));
        Assert.Empty(report.EyeSpaces);
        Assert.All(report.Groups, g => Assert.Empty(g.EyeSpaces));
    }

    [Fact]
    public void 棋盘边缘与障碍算墙()
    {
        // 角上 A1 只有 A2、B1 两个气边邻格，都是 A 的棋子；另在 E4 南侧放一块岩石 E3 作墙：E4 的邻格 D4 / F4 / E5 为 A、E3 为岩石。
        GameBoard board = Grid([
            "....0....",
            "...0.0...",
            "....#....",
            "0........",
            ".0......1",
        ]);
        LifeShapeReport report = LifeShapeReport.Analyze(board);

        EyeSpace corner = report.EyeSpaceAt(TestMaps.At("A1"))!;
        Assert.Equal(["A1"], corner.Cells());
        Assert.Equal(A, corner.Owner);
        Assert.Equal(["A1"], report.EyeSpacesOf("A2"));
        Assert.Equal(["A1"], report.EyeSpacesOf("B1"));

        EyeSpace byRock = report.EyeSpaceAt(TestMaps.At("E4"))!;
        Assert.Equal(["E4"], byRock.Cells());
        Assert.Equal(A, byRock.Owner);
    }

    [Fact]
    public void 崖壁与栅栏算墙()
    {
        // E5（h=0）东侧 F5 高 2（崖壁），F5 上是 B 的棋子；北侧 E5–E6 有栅栏，E6 上也是 B 的棋子。
        // 其余两个气边邻格 D5、E4 是 A 的棋子 → {E5} 对 A 封闭，崖上与栅栏另一侧的 B 子不算邻格。
        GameBoard board = Grid(
            [
                ".........",
                "....1....",
                "...0.1...",
                "....0....",
                ".........",
                ".........",
                "........1",
            ],
            heights: [("F5", 2)],
            fences: [("E5", "E6")]);
        LifeShapeReport report = LifeShapeReport.Analyze(board);

        EyeSpace eye = report.EyeSpaceAt(TestMaps.At("E5"))!;
        Assert.Equal(["E5"], eye.Cells());
        Assert.Equal(A, eye.Owner);
        Assert.Equal(["E5"], report.EyeSpacesOf("D5"));
        Assert.Equal(["E5"], report.EyeSpacesOf("E4"));
        Assert.Empty(report.GroupLifeAt(TestMaps.At("F5"))!.EyeSpaces);
        Assert.Empty(report.GroupLifeAt(TestMaps.At("E6"))!.EyeSpaces);
    }

    [Fact]
    public void 未架桥深水算墙()
    {
        GameBoard board = Grid([
            ".........",
            "....0....",
            "...0.~...",
            "....0....",
            ".........",
            ".........",
            "........1",
        ]);
        LifeShapeReport report = LifeShapeReport.Analyze(board);

        EyeSpace eye = report.EyeSpaceAt(TestMaps.At("E5"))!;
        Assert.Equal(["E5"], eye.Cells());
        Assert.Equal(A, eye.Owner);
    }

    [Fact]
    public void 超过上限不算眼空间()
    {
        // 空区 = A1..H1（8 格）+ A2..E2（5 格）= 13 格，四周只有 A 的棋子与棋盘边。
        GameBoard board = Grid([
            "........1",
            "00000....",
            ".....0000",
            "........0",
        ]);
        LifeShapeReport report = LifeShapeReport.Analyze(board);

        Assert.Null(report.EyeSpaceAt(TestMaps.At("A1")));
        Assert.Empty(report.EyeSpaces);
        Assert.All(report.Groups, g => Assert.Empty(g.EyeSpaces));

        // 对照：A 再往 A1 落一子，空区缩成 12 格，恰为上限，成为眼空间（钉住 ≤ 12 而非 < 12）。
        board.Place(TestMaps.At("A1"), A, PieceType.Basic);
        EyeSpace twelve = LifeShapeReport.Analyze(board).EyeSpaceAt(TestMaps.At("B1"))!;
        Assert.Equal(LifeShapeReport.EyeSpaceMax, twelve.Cells.Length);
        Assert.Equal(12, twelve.Cells.Length);
    }

    [Fact]
    public void 超过上限的空区从多个起点出发也不算眼空间()
    {
        // 实现守门（非 Scenario）：第 1、2 行共 20 格只贴 A 的第 3 行。实现从 A2 出发走到第 13 格即放弃，
        // 右侧剩下不到 12 格；从 H2 之类的气再出发时，必须认出"碰到上一次的标记 = 同一个大空区"，不得把剩余部分当成眼空间。
        GameBoard board = Grid([
            "........1.",
            "0000000000",
            "..........",
            "..........",
        ]);
        LifeShapeReport report = LifeShapeReport.Analyze(board);

        Assert.Empty(report.EyeSpaces);
        Assert.Null(report.EyeSpaceAt(TestMaps.At("K1")));
        Assert.Empty(report.GroupLifeAt(TestMaps.At("A3"))!.EyeSpaces);
    }

    [Fact]
    public void 林地可以是眼空间()
    {
        GameBoard board = Grid([
            ".........",
            "....0....",
            "...0T0...",
            "....0....",
            ".........",
            ".........",
            "........1",
        ]);
        LifeShapeReport report = LifeShapeReport.Analyze(board);

        EyeSpace eye = report.EyeSpaceAt(TestMaps.At("E5"))!;
        Assert.Equal(Surface.Forest, board.Map.SurfaceAt(TestMaps.At("E5")));
        Assert.Equal(["E5"], eye.Cells());
        Assert.Equal(A, eye.Owner);
    }

    [Fact]
    public void 多条棋串共享眼空间()
    {
        // A 的两条棋串 {D5,D6,E6} 与 {E4,F4,F5} 互不相连，共同围住 E5。
        GameBoard board = Grid([
            ".........",
            "...00....",
            "...0.0...",
            "....00...",
            ".........",
            ".........",
            "........1",
        ]);
        LifeShapeReport report = LifeShapeReport.Analyze(board);

        Assert.NotSame(report.GroupLifeAt(TestMaps.At("D5")), report.GroupLifeAt(TestMaps.At("F5")));
        Assert.Equal(2, report.Groups.Count(g => g.Group.Owner == A));
        Assert.Equal(["E5"], report.EyeSpacesOf("D5"));
        Assert.Equal(["E5"], report.EyeSpacesOf("F5"));
        EyeSpace eye = report.EyeSpaceAt(TestMaps.At("E5"))!;
        Assert.Equal(2, eye.GroupIndices.Length);
        Assert.Equal(
            ["D5,D6,E6", "E4,F4,F5"],
            report.GroupsOf(eye).Select(g => string.Join(",", g.Group.Stones.Notations())).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void 没有任何邻子的空区不封闭()
    {
        // 规格封闭条件第 2 条"至少存在一个这样的被占格"：一块被岩石围住、无人贴边的小空区不是任何人的眼空间（外部空区同时贴 A、B，也不封闭）。
        GameBoard board = Grid([
            ".#..0",
            "#....",
            "....1",
        ]);
        LifeShapeReport report = LifeShapeReport.Analyze(board);

        Assert.Null(report.EyeSpaceAt(TestMaps.At("A3")));
        Assert.Empty(report.EyeSpaces);
    }

    [Fact]
    public void 活形分析只经气边邻格查询取邻接()
    {
        // 守门（tasks 1.2）：活形分析文件里不得出现几何四邻偏移量、几何邻居调用或第二处邻接实现，只能调 LibertyNeighbors。
        // 变异验证记录见 .trellis/tasks/09-22-life-shape/implement.md 段 A。
        string code = StripComments(File.ReadAllText(LifeShapeSource()));
        Assert.Matches("LibertyNeighbors\\(", code);
        string[] forbidden =
        [
            "(?<!Liberty)Neighbors\\s*\\(",
            "Adjacency\\.",
            "CoverageTargets",
            "\\.[XY]\\s*[+-]\\s*1",
            "\\(\\s*-?[01]\\s*,\\s*-?[01]\\s*\\)",
            "new\\s+Coord\\s*\\(",
            "HasFence|HeightAt|CliffDrop|IsUnbridgedDeepWater|Obstacles",
        ];
        string[] hits = [.. forbidden.Where(p => Regex.IsMatch(code, p))];
        Assert.Empty(hits);
    }

    internal static string LifeShapeSource([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "")
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
        string path = Path.Combine(root, "src", "Siege.Core", "Board", "LifeShape.cs");
        Assert.True(File.Exists(path), $"找不到活形分析源码：{path}");
        return path;
    }

    internal static string StripComments(string code) =>
        Regex.Replace(code, "//[^\\n]*", string.Empty);

    // ---------- terrain-surfaces 段 4：浅滩 ----------

    [Fact]
    public void 空浅滩不是眼()
    {
        // 规格 terrain-surfaces · life-shape「空浅滩不是眼」：空浅滩 E5 的四个气边邻格全是 A → E5 不属于任何空区、不是眼空间。
        // 变异验证 M-S4b（实跑）：空区起点与 flood 不排除空浅滩 → 本测试与「贴着空浅滩不封闭」红。
        GameBoard board = Grid([
            ".........",
            "....0....",
            "...0s0...",
            "....0....",
            ".........",
            ".........",
            "........1",
        ]);
        LifeShapeReport report = LifeShapeReport.Analyze(board);

        Assert.Null(report.EyeSpaceAt(TestMaps.At("E5")));
        Assert.Empty(report.EyeSpaces);
    }

    [Fact]
    public void 贴着空浅滩不封闭()
    {
        // 规格 Scenario：空草地 E5 的气边邻格三个是 A、一个是空浅滩 F5 → {E5} 对 A 不封闭（空浅滩不是墙，任何人都能落进去）。
        // F5 另三面也用 A 包住，排除"F5 那一侧通向外部大空区"这条别的开口。
        // 变异验证 M-S4c（实跑）：把空浅滩当墙（跳过、不破坏封闭）→ 本测试红。
        GameBoard board = Grid([
            ".........",
            "....00...",
            "...0.s0..",
            "....00...",
            ".........",
            ".........",
            "........1",
        ]);
        LifeShapeReport report = LifeShapeReport.Analyze(board);

        Assert.Null(report.EyeSpaceAt(TestMaps.At("E5")));
        Assert.Null(report.EyeSpaceAt(TestMaps.At("F5")));
    }

    [Fact]
    public void 浅滩被己方占据后可封闭()
    {
        // 规格 Scenario：上例中 F5 被 A 的棋子占据（F5 仍是浅滩地表）→ {E5} 对 A 封闭。
        GameBoard board = Grid([
            ".........",
            "....00...",
            "...0.00..",
            "....00...",
            ".........",
            ".........",
            "........1",
        ], under: [("F5", Surface.Shallows)]);
        LifeShapeReport report = LifeShapeReport.Analyze(board);

        Assert.Equal(Surface.Shallows, board.Map.SurfaceAt(TestMaps.At("F5")));
        Assert.Equal(["E5"], report.EyeSpaceAt(TestMaps.At("E5"))!.Cells());
    }

    [Theory]
    [InlineData('d')]
    [InlineData('m')]
    [InlineData('p')]
    public void 荒漠沼泽岩台可以是眼空间(char surface)
    {
        // 规格 Scenario「荒漠、沼泽、岩台可以是眼空间」：它们有气边、空着时是气，照常构成空区。
        GameBoard board = Grid([
            ".........",
            "....0....",
            $"...0{surface}0...",
            "....0....",
            ".........",
            ".........",
            "........1",
        ]);
        LifeShapeReport report = LifeShapeReport.Analyze(board);

        Assert.Equal(["E5"], report.EyeSpaceAt(TestMaps.At("E5"))!.Cells());
    }
}
