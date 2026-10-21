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
    public void 崖壁算墙()
    {
        // fence-eye-rule 改写（原「崖壁与栅栏算墙」拆成本条与「栅栏不是墙」）：E5（h=0）东侧 F5 高 2（崖壁），F5 上是 B 的棋子；
        // 其余三个眼边邻格 D5、E4、E6 是 A 的棋子 → {E5} 对 A 封闭，崖上的 B 子不算邻格。
        GameBoard board = Grid(
            [
                ".........",
                "....0....",
                "...0.1...",
                "....0....",
                ".........",
                ".........",
                "........1",
            ],
            heights: [("F5", 2)]);
        LifeShapeReport report = LifeShapeReport.Analyze(board);

        Assert.DoesNotContain(TestMaps.At("F5"), board.EyeNeighbors(TestMaps.At("E5")));   // 前提：崖壁处没有眼边
        EyeSpace eye = report.EyeSpaceAt(TestMaps.At("E5"))!;
        Assert.Equal(["E5"], eye.Cells());
        Assert.Equal(A, eye.Owner);
        Assert.Equal(["E5"], report.EyeSpacesOf("D5"));
        Assert.Equal(["E5"], report.EyeSpacesOf("E4"));
        Assert.Equal(["E5"], report.EyeSpacesOf("E6"));
        Assert.Empty(report.GroupLifeAt(TestMaps.At("F5"))!.EyeSpaces);
    }

    [Fact]
    public void 栅栏不是墙()
    {
        // 规格 fence-eye-rule「栅栏不是墙」：E5 北侧隔着栅栏是 B 的棋子 E6，其余三个眼边邻格 D5、F5、E4 是 A 的棋子
        // → {E5} 对 A 不封闭、不是 A 的眼空间；E5 仍不是 E6 那条棋串的气（栅栏照常挡气）。
        // 变异验证见 .trellis/tasks/10-21-fence-eye-rule/implement.md（M-F1：眼边加回"无栅栏"一条 → 本测试红）。
        GameBoard board = Grid(
            [
                ".........",
                "....1....",
                "...0.0...",
                "....0....",
                ".........",
                ".........",
                "........1",
            ],
            fences: [("E5", "E6")]);
        LifeShapeReport report = LifeShapeReport.Analyze(board);

        Assert.Null(report.EyeSpaceAt(TestMaps.At("E5")));
        Assert.Empty(report.EyeSpaces);
        Assert.All(report.Groups, g => Assert.Empty(g.EyeSpaces));
        Siege.Core.Board.Group e6 = board.GroupAt(TestMaps.At("E6"))!;
        Assert.DoesNotContain(TestMaps.At("E5"), board.LibertiesOf(e6));
        Assert.DoesNotContain(TestMaps.At("E5"), board.LibertyNeighbors(TestMaps.At("E6")));
        Assert.Contains(TestMaps.At("E5"), board.EyeNeighbors(TestMaps.At("E6")));
    }

    [Fact]
    public void 栅栏不切分空区()
    {
        // 规格 fence-eye-rule「栅栏不切分空区」：A 的棋子围住 2 格空区 {E5, F5}，E5–F5 之间立栅 → 仍是同一个 2 格空区，眼值 0，不是两个单格眼。
        // 变异验证见 implement.md（M-F1 → 本测试红；M-F3：flood 改回气边 → 本测试红）。
        GameBoard board = Grid(
            [
                ".........",
                "....00...",
                "...0..0..",
                "....00...",
                ".........",
                ".........",
                "........1",
            ],
            fences: [("E5", "F5")]);
        LifeShapeReport report = LifeShapeReport.Analyze(board);

        EyeSpace space = report.EyeSpaceAt(TestMaps.At("E5"))!;
        Assert.Same(space, report.EyeSpaceAt(TestMaps.At("F5")));
        Assert.Equal(["E5", "F5"], space.Cells());
        Assert.Equal(A, space.Owner);
        Assert.Equal(0, space.EyeValue);
        Assert.Single(report.EyeSpaces);
        Assert.All(report.Groups.Where(g => g.Group.Owner == A), g => Assert.Equal(0, g.EyeValueSum));
    }

    [Fact]
    public void 只隔栅栏贴着空区的棋串也拥有该眼空间()
    {
        // 实现守门（design D2，非 Scenario）：E5 四面立栅，四个几何邻格 D5、F5、E4、E6 都是 A 的单子——E5 不是任何一条棋串的气，
        // 却与四条棋串都有眼边相连，所以 {E5} 对 A 封闭，是这四条棋串的眼空间（活形只决定禁入与"不得破坏他人活形"，不决定提子）。
        // 空区起点只能沿眼边找到：变异验证见 implement.md（M-F2：起点改回气边 → 本测试红；M-F3：flood 改回气边 → 本测试红）。
        GameBoard board = Grid(
            [
                ".........",
                ".........",
                "....0....",
                "...0.0...",
                "....0....",
                ".........",
                ".........",
                "........1",
            ],
            fences: [("E5", "E6"), ("E5", "D5"), ("E5", "F5"), ("E5", "E4")]);
        LifeShapeReport report = LifeShapeReport.Analyze(board);

        Assert.Empty(board.LibertyNeighbors(TestMaps.At("E5")));   // 前提：E5 四面皆栅，不是任何棋串的气
        foreach (string stone in new[] { "D5", "F5", "E4", "E6" })
        {
            Assert.DoesNotContain(TestMaps.At("E5"), board.LibertiesOf(board.GroupAt(TestMaps.At(stone))!));
            Assert.Equal(["E5"], report.EyeSpacesOf(stone));
        }

        EyeSpace eye = report.EyeSpaceAt(TestMaps.At("E5"))!;
        Assert.Equal(["E5"], eye.Cells());
        Assert.Equal(A, eye.Owner);
        Assert.Equal(4, eye.GroupIndices.Length);
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
    public void 活形分析只经眼边邻格查询取邻接()
    {
        // 守门（life-shape tasks 1.2 → fence-eye-rule 1.2 改写）：活形分析文件里不得出现几何四邻偏移量、几何邻居调用或第二处邻接实现，
        // 空区、封闭、眼空间归属与方四只能调 EyeNeighbors；气边（LibertyNeighbors）在本文件里一处也不许出现——棋串由 AllGroups 给出。
        // 原变异验证记录见 .trellis/tasks/09-22-life-shape/implement.md 段 A；fence-eye-rule 的变异（M-F2 / M-F3 / M-F4：任一处改回 LibertyNeighbors → 本测试红）见该任务 implement.md。
        string code = StripComments(File.ReadAllText(LifeShapeSource()));
        Assert.Matches("EyeNeighbors\\(", code);
        string[] forbidden =
        [
            "(?<!Eye)Neighbors\\s*\\(",
            "Adjacency\\.",
            "CoverageTargets",
            "\\.[XY]\\s*[+-]\\s*1",
            "\\(\\s*-?[01]\\s*,\\s*-?[01]\\s*\\)",
            "new\\s+Coord\\s*\\(",
            "HasFence|HeightAt|CliffDrop|IsUnbridgedDeepWater|Obstacles",
        ];
        string[] hits = [.. forbidden.Where(p => Regex.IsMatch(code, p))];
        Assert.Empty(hits);

        // 反面：判据确实命中改造前的写法（否则"不含"只是正则写错）。
        Assert.Matches(forbidden[0], "foreach (Coord n in board.LibertyNeighbors(region[head]))");
        Assert.DoesNotMatch(forbidden[0], "foreach (Coord n in board.EyeNeighbors(region[head]))");
    }

    [Fact]
    public void 眼边只去掉气边的栅栏一条()
    {
        // fence-eye-rule tasks 1.1：同一对格子之间立栅后，气边消失、眼边保留；崖壁 / 未架桥深水 / 障碍处两者都没有；双向对称。
        // E5–E6 立栅；F5 高 2（崖壁）；D5 未架桥深水；E4 岩石。E5 的四个几何邻居恰好各占一种情形。
        // 变异验证见 implement.md（M-F1：眼边加回"无栅栏"一条 → 本测试红；M-F5：眼边去掉高差一条 → 本测试红）。
        GameBoard board = Grid(
            [
                ".........",
                "....0....",
                "...~.0...",
                "....#....",
                ".........",
                ".........",
                ".........",
            ],
            heights: [("F5", 2)],
            fences: [("E5", "E6")]);
        Coord e5 = TestMaps.At("E5");

        Assert.Equal(["E6"], board.EyeNeighbors(e5).Notations());
        Assert.Empty(board.LibertyNeighbors(e5));
        Assert.Contains(e5, board.EyeNeighbors(TestMaps.At("E6")));
        Assert.DoesNotContain(e5, board.LibertyNeighbors(TestMaps.At("E6")));
        Assert.Empty(board.EyeNeighbors(TestMaps.At("D5")));   // 深水自身不可落子：两种边都没有
        Assert.Empty(board.EyeNeighbors(TestMaps.At("E4")));   // 障碍同上
        Assert.DoesNotContain(e5, board.EyeNeighbors(TestMaps.At("F5")));   // 崖壁：反方向同样没有

        // 盘面的记忆表与唯一实现逐格相同，且气边恰是眼边去掉有栅栏的那一对。
        foreach (Coord c in board.AllCoords())
        {
            Assert.Equal(Adjacency.EyeNeighbors(board.Map, c), board.EyeNeighbors(c));
            Assert.Equal(
                board.EyeNeighbors(c).Where(n => !board.Map.HasFence(c, n)).Notations(),
                board.LibertyNeighbors(c).Notations());
        }

        // 对照：拆掉栅栏的同一盘面上，E5–E6 两种边都在。
        GameBoard open = Grid(
            [
                ".........",
                "....0....",
                "...~.0...",
                "....#....",
                ".........",
                ".........",
                ".........",
            ],
            heights: [("F5", 2)]);
        Assert.Equal(["E6"], open.EyeNeighbors(e5).Notations());
        Assert.Equal(["E6"], open.LibertyNeighbors(e5).Notations());
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
