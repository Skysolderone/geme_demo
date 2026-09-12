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

    [Fact]
    public void 列字母跳过I()
    {
        string letters = new(Enumerable.Range(0, 11).Select(x => new Coord(x, 0).Column).ToArray());

        Assert.Equal("ABCDEFGHJKL", letters);
        Assert.DoesNotContain('I', letters);
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
}
