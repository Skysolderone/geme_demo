using Siege.Core.Board;

namespace Siege.Core.Tests.BoardTopology;

/// <summary>规格：board-topology —— Requirement: 坐标记法</summary>
public class 坐标记法Tests
{
    [Fact]
    public void 左下角为原点()
    {
        // 11×11 棋盘最左下角
        Coord corner = new(0, 0);

        Assert.Equal("A1", corner.ToNotation());
    }

    [Theory]
    [InlineData(11, "ABCDEFGHJKL")]
    [InlineData(13, "ABCDEFGHJKLMN")]
    public void 列字母跳过I(int width, string expected)
    {
        // 列字母个数由棋盘宽度决定：11 列到 L，13 列到 N；都不含 I
        string letters = new(Enumerable.Range(0, width).Select(x => new Coord(x, 0).Column).ToArray());

        Assert.Equal(expected, letters);
        Assert.DoesNotContain('I', letters);
        Assert.Equal(new Coord(width - 1, 0), Coord.Parse($"{expected[^1]}1"));
    }

    [Fact]
    public void 双向映射唯一()
    {
        // 全盘往返：记法 → 内部索引 → 记法，必须逐个恒等
        for (int y = 0; y < 11; y++)
        {
            for (int x = 0; x < 11; x++)
            {
                Coord original = new(x, y);
                Coord roundTripped = Coord.Parse(original.ToNotation());

                Assert.Equal(original, roundTripped);
                Assert.Equal(original.ToNotation(), roundTripped.ToNotation());
            }
        }
    }

    [Fact]
    public void 映射实现只有一处()
    {
        // Scenario「双向映射唯一」的后半句："全项目只存在一处映射实现"。
        // 往返恒等测不出第二份列字母表——两份表只要都跳过 I，往返照样成立，
        // 直到有人在第二份表里忘了跳 I，而那时错的是日志坐标，极难归因。
        // 规范：.trellis/spec/core/coordinates.md —— 映射唯一
        string source = Path.Combine(RepoRoot(), "src");
        char sep = Path.DirectorySeparatorChar;

        string[] offenders = [.. Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{sep}obj{sep}", StringComparison.Ordinal)
                        && !p.Contains($"{sep}bin{sep}", StringComparison.Ordinal))
            .Where(p => Path.GetFileName(p) != "Coord.cs")
            .Where(p => File.ReadAllText(p).Contains("ABCDEFGH", StringComparison.Ordinal))
            .Select(p => Path.GetRelativePath(RepoRoot(), p))];

        Assert.True(Directory.Exists(source), $"找不到源码目录：{source}");
        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData("A1", 0, 0)]
    [InlineData("F6", 5, 5)]
    [InlineData("L11", 10, 10)]
    [InlineData("H4", 7, 3)]
    [InlineData("J10", 8, 9)]
    public void 记法与索引对应(string notation, int x, int y)
    {
        Coord c = Coord.Parse(notation);

        Assert.Equal(x, c.X);
        Assert.Equal(y, c.Y);
        Assert.Equal(y + 1, c.Row);
    }

    [Theory]
    [InlineData("I5")]   // I 不是合法列字母
    [InlineData("A0")]   // 行号自 1 起
    [InlineData("5A")]
    [InlineData("")]
    [InlineData(null)]
    public void 非法记法被拒绝(string? notation)
    {
        Assert.False(Coord.TryParse(notation, out _));
    }

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
}
