using System.Reflection;
using System.Reflection.Emit;
using Siege.Core.Board;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.BoardTopology;

/// <summary>规格：board-topology —— Requirement: 四邻接是唯一邻接语义</summary>
public class 四邻接Tests
{
    [Fact]
    public void 斜向不相邻()
    {
        // 同一玩家的两枚棋子位于 D4 与 E5，二者之间无其他己方棋子
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("D4", TestMaps.P0)
            .Place("E5", TestMaps.P0);

        Group a = board.GroupAt(TestMaps.At("D4"))!;
        Group b = board.GroupAt(TestMaps.At("E5"))!;

        Assert.Equal(1, a.Size);
        Assert.Equal(1, b.Size);
        Assert.DoesNotContain(TestMaps.At("E5"), a.Stones);
    }

    [Fact]
    public void 边角格邻居数量()
    {
        GameBoard board = TestMaps.Blank(size: 11);

        Assert.Equal(["B1", "A2"], board.Neighbors(TestMaps.At("A1")).Notations());
    }

    [Fact]
    public void 邻居不含斜向()
    {
        GameBoard board = TestMaps.Blank(size: 11);

        Assert.Equal(["F5", "E6", "G6", "F7"], board.Neighbors(TestMaps.At("F6")).Notations());
    }

    [Theory]
    [InlineData("A1", new[] { "B1", "A2" })]
    [InlineData("L11", new[] { "L10", "K11" })]
    [InlineData("A6", new[] { "A5", "B6", "A7" })]
    [InlineData("F6", new[] { "F5", "E6", "G6", "F7" })]
    public void 邻居集合与手工期望一致(string center, string[] expected)
    {
        GameBoard board = TestMaps.Blank(size: 11);

        Assert.Equal(expected, board.Neighbors(TestMaps.At(center)).Notations());
    }

    [Fact]
    public void 邻接只走四方向()
    {
        // 期望值在测试里独立算出（手写的四个方向偏移），不调用被测实现。
        // 这样 Adjacency 本身写错（少一个方向、混入斜向、边界差一）时这个测试会红。
        const int size = 11;
        (int Dx, int Dy)[] directions = [(0, -1), (-1, 0), (1, 0), (0, 1)];
        GameBoard board = TestMaps.Blank(size);

        foreach (Coord c in board.AllCoords())
        {
            IEnumerable<Coord> expected = directions
                .Select(d => (X: c.X + d.Dx, Y: c.Y + d.Dy))
                .Where(p => p.X >= 0 && p.X < size && p.Y >= 0 && p.Y < size)
                .Select(p => new Coord(p.X, p.Y));

            Assert.Equal(expected, board.Neighbors(c));
        }
    }

    [Fact]
    public void 几何相邻但无气边()
    {
        // F6 为 h=0、F7 为 h=2，两格均为空草地 → 互为几何邻居，但之间不存在气边
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(heights: [("F7", 2)]));

        Assert.Contains(TestMaps.At("F7"), board.Neighbors(TestMaps.At("F6")));
        Assert.Contains(TestMaps.At("F6"), board.Neighbors(TestMaps.At("F7")));
        Assert.DoesNotContain(TestMaps.At("F7"), board.LibertyNeighbors(TestMaps.At("F6")));
        Assert.DoesNotContain(TestMaps.At("F6"), board.LibertyNeighbors(TestMaps.At("F7")));
        Assert.Equal(["F5", "E6", "G6"], board.LibertyNeighbors(TestMaps.At("F6")).Notations());
    }

    [Fact]
    public void 几何邻居枚举只在允许名单内直接调用()
    {
        // 规格「几何邻居枚举、气边与覆盖关系 MUST 各自只有一处实现；任何规则 MUST NOT 绕过它们手写邻居遍历或地形过滤」。
        // 逐条解析 Siege.Core 全部方法体的 IL，收集两类 call：
        //   ① Adjacency.Neighbors：只允许 Adjacency 自身（两个导出关系）与 GameBoard.Neighbors（几何委托）。
        //      MapValidator 在段 A 已改走气边、段 B 的盘内 / 旋转校验只做坐标算术，按裁决 A-3 从名单移除。
        //   ② GameBoard.Neighbors：Core 内公开的几何入口，段 B 3.4 起 Core 内不再有任何调用者
        //      （PieceEffects.Step 改走 LibertyNeighbors，裁决 A-6）；它只留给表现层几何。
        // lambda / 迭代器落在编译器生成的嵌套类型里，沿 DeclaringType 走到最外层再比。
        // 变异验证 M-A5：CoverageMap.Compute 里加一行 `_ = Adjacency.Neighbors(board.Width, board.Height, c);` → 本测试红 1，报出 CoverageMap.Compute。
        // check 变异 N-4：GroupSafety 改回 `board.Neighbors(liberty)` + 手写 `cell.Terrain == Terrain.Obstacle` 判墙——只扫 ① 时 0 红，
        // 因为经 GameBoard.Neighbors 绕回"几何邻居 + 手写地形过滤"不经过 Adjacency；补 ② 后红 1，报出 GroupSafety。
        // 段 B 变异 M-B3：PieceEffects.Step 改回 board.Neighbors → ② 红（报出 PieceEffects.Step）；
        // M-B2：MapValidator 距离 BFS 改走 Adjacency.Neighbors → ① 红（报出 MapValidator）。
        (MethodBase Caller, MemberInfo Target, OpCode OpCode)[] refs = [.. IlReferences(typeof(GameBoard).Assembly)];
        MethodBase[] adjacencyCallers = CallersOf(refs, typeof(Adjacency), nameof(Adjacency.Neighbors));
        MethodBase[] boardCallers = CallersOf(refs, typeof(GameBoard), nameof(GameBoard.Neighbors));

        static Type Outermost(Type t)
        {
            while (t.DeclaringType is { } outer)
            {
                t = outer;
            }

            return t;
        }

        static bool AdjacencyAllowed(MethodBase caller)
        {
            Type outer = Outermost(caller.DeclaringType!);
            return outer == typeof(Adjacency)
                   || (outer == typeof(GameBoard) && caller.Name == nameof(GameBoard.Neighbors));
        }

        static string Describe(MethodBase c) => $"{c.DeclaringType!.FullName}.{c.Name}";

        string[] violations =
        [
            .. adjacencyCallers.Where(c => !AdjacencyAllowed(c)).Select(c => $"Adjacency.Neighbors ← {Describe(c)}"),
            .. boardCallers.Select(c => $"GameBoard.Neighbors ← {Describe(c)}"),
        ];
        Assert.Empty(violations.Order());

        // 反面断言：① 段扫描确实命中了允许名单里的调用者，不是扫了个空集；② 段与 ① 共用同一份 refs，扫描器坏了 ① 先红。
        Assert.Contains(adjacencyCallers, c => Outermost(c.DeclaringType!) == typeof(GameBoard) && c.Name == nameof(GameBoard.Neighbors));
        Assert.Contains(adjacencyCallers, c => Outermost(c.DeclaringType!) == typeof(Adjacency) && c.Name == nameof(Adjacency.LibertyNeighbors));
        Assert.Contains(adjacencyCallers, c => Outermost(c.DeclaringType!) == typeof(Adjacency) && c.Name == nameof(Adjacency.CoverageTargets));
    }

    private static MethodBase[] CallersOf((MethodBase Caller, MemberInfo Target, OpCode OpCode)[] refs, Type declaringType, string methodName) =>
        [.. refs
            .Where(r => r.Target is MethodBase m && m.DeclaringType == declaringType && m.Name == methodName)
            .Select(r => r.Caller)
            .Distinct()];

    [Fact]
    public void 内核不引用Godot()
    {
        // 比 csproj 里的 MSBuild 守门更强：它只看 PackageReference，
        // 这里看真正落进程序集的引用，ProjectReference 与直接 dll 引用也拦得住。
        var referenced = typeof(GameBoard).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(
            referenced,
            r => r.Name is not null && r.Name.StartsWith("Godot", StringComparison.OrdinalIgnoreCase));
    }
}
