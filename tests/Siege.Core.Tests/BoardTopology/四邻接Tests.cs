using Siege.Core.Board;

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
