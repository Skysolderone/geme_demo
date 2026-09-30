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
        string letters = string.Concat(Enumerable.Range(0, width).Select(x => new Coord(x, 0).Column));

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

    // ---------- frontier-map 1.6：两位数行号与 25 列上限（裁决 7：25 × 28–32 的竖长图） ----------

    [Fact]
    public void 两位数行号往返()
    {
        // tasks 1.6 算例：A27 往返；行号 10–32 × 全部 25 列逐个恒等。
        Coord a27 = Coord.Parse("A27");
        Assert.Equal((0, 26), (a27.X, a27.Y));
        Assert.Equal("A27", a27.ToNotation());
        Assert.Equal("Z32", new Coord(24, 31).ToNotation());
        Assert.Equal(new Coord(24, 31), Coord.Parse("z32"));

        for (int y = 9; y < 32; y++)
        {
            for (int x = 0; x < 25; x++)
            {
                Coord original = new(x, y);
                Assert.Equal(original, Coord.Parse(original.ToNotation()));
            }
        }

        // 排序按数值行号，不按字符串：A9 < A10 < A27（字符串序会把 A10 排到 A9 前面）。
        Assert.Equal(["A9", "A10", "A27"], new[] { "A27", "A9", "A10" }.Select(Coord.Parse).Order().Select(c => c.ToNotation()));
    }

    [Fact]
    public void 列字母共25个且超出列标上限构造被拒()
    {
        // tasks 1.6 算例：25 列字母为 A…Z 跳 I。
        // board-map D6 之后第 26 列是 AA，不再被拒；被拒的是超出列标上限（25 + 25×25 列）的列。
        string letters = string.Concat(Enumerable.Range(0, 25).Select(x => new Coord(x, 0).Column));

        Assert.Equal("ABCDEFGHJKLMNOPQRSTUVWXYZ", letters);
        Assert.Equal(25, Coord.ColumnLetters.Length);
        Assert.Equal("AA1", new Coord(25, 0).ToNotation());
        Assert.Throws<ArgumentOutOfRangeException>(() => new Coord(Coord.MaxColumns, 0));
        Assert.False(Coord.TryParse("I27", out _));
    }

    // ---------- board-map 1.1：第 26 列起双字母列标（D6） ----------

    [Fact]
    public void 第26列起用双字母()
    {
        // Scenario：28 列地图最右侧四列的列标依次为 Z、AA、AB、AC。
        const int width = 28;
        string[] columns = [.. Enumerable.Range(width - 4, 4).Select(x => $"{new Coord(x, 0).Column}")];

        Assert.Equal(["Z", "AA", "AB", "AC"], columns);

        // 前 25 列的记法与引入双字母之前完全相同：仍是字母表里的单个字母。
        for (int x = 0; x < 25; x++)
        {
            Assert.Equal(Coord.ColumnLetters[x].ToString(), $"{new Coord(x, 0).Column}");
            Assert.Equal($"{Coord.ColumnLetters[x]}7", new Coord(x, 6).ToNotation());
        }
    }

    [Fact]
    public void 双字母同样跳过I()
    {
        // Scenario：第 33 列与第 34 列的列标为 AH 与 AJ，不存在 AI。
        Assert.Equal("AH", $"{new Coord(32, 0).Column}");
        Assert.Equal("AJ", $"{new Coord(33, 0).Column}");
        Assert.False(Coord.TryParse("AI5", out _));
        Assert.False(Coord.TryParse("IA5", out _));

        // 首位同样跳过 I：H 行之后是 J 行（HZ → JA）。
        Assert.Equal("HZ", $"{new Coord(25 + (7 * 25) + 24, 0).Column}");
        Assert.Equal("JA", $"{new Coord(25 + (8 * 25), 0).Column}");
    }

    [Fact]
    public void 双字母坐标往返()
    {
        // Scenario：解析 AB12 → 第 27 列、第 12 行，转回的记法为 AB12。
        Coord ab12 = Coord.Parse("AB12");
        Assert.Equal((26, 11), (ab12.X, ab12.Y));
        Assert.Equal(12, ab12.Row);
        Assert.Equal("AB12", ab12.ToNotation());
        Assert.Equal(ab12, Coord.Parse("ab12"));

        // 列标上限 25 + 25×25 列：全部列逐个往返恒等，记法两两不同；再多一列构造被拒。
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int x = 0; x < Coord.MaxColumns; x++)
        {
            Coord original = new(x, 11);
            Assert.Equal(original, Coord.Parse(original.ToNotation()));
            Assert.True(seen.Add(original.ToNotation()), original.ToNotation());
        }

        Assert.Equal(25 + (25 * 25), Coord.MaxColumns);
        Assert.Equal("ZZ1", new Coord(Coord.MaxColumns - 1, 0).ToNotation());
        Assert.Throws<ArgumentOutOfRangeException>(() => new Coord(Coord.MaxColumns, 0));

        // 解析取最长的字母前缀：三个字母不是合法列标，字母之后必须是行号。
        Assert.False(Coord.TryParse("ABC1", out _));
        Assert.False(Coord.TryParse("AB", out _));
        Assert.False(Coord.TryParse("A1B", out _));

        // 排序仍按数值：先行后列，Z 列之后是 AA 列（字符串序会把 AA1 排到 B1 前面）。
        Assert.Equal(["B1", "Z1", "AA1", "A2"], new[] { "A2", "AA1", "Z1", "B1" }.Select(Coord.Parse).Order().Select(c => c.ToNotation()));
    }

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
}
