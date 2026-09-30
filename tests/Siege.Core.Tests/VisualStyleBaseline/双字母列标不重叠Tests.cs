using Siege.Core.Board;
using Siege.Presentation.Style;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.VisualStyleBaseline;

/// <summary>规格：visual-style-baseline —— Requirement: 双字母列标不重叠（change `forbidden-marks` D7，tasks 2.1 / 2.3）。</summary>
public class 双字母列标不重叠Tests
{
    private static string[] Columns(int width) => [.. Enumerable.Range(0, width).Select(x => new Coord(x, 0).Column)];

    [Fact]
    public void 双字母列标可辨()
    {
        // 规格 Scenario：45 列棋盘图底边的坐标标注，Z、AA、AB、AC 逐个分开，相邻两个标注不相接。
        // 标注在屏幕上大小恒定，列距却是固定的：双字母列标的字号取到"两个字的总宽不超过一个单字母字号的宽字母"——
        // 两个字各宽约 0.65 个字号，合计 1.3 × 双字母字号 ≤ 0.9 × 单字母字号（最宽的单字母 W 约 0.9 个字号）。是否真的留有空隙以截图为准（人工检查项）。
        // 变异 M-L1（双字母列标也用单字母字号）→ 红 1；M-L3（渲染层的列标不取列标字号）→ 红 1；都是本测试。
        string[] columns = Columns(45);
        Assert.Equal(["Z", "AA", "AB", "AC"], columns[24..28]);
        Assert.Equal(25, columns.Count(c => c.Length == 1));
        Assert.Equal(20, columns.Count(c => c.Length == 2));

        foreach (string wide in columns.Where(c => c.Length == 2))
        {
            int size = CoordinateLabelStyle.ColumnFontSizeOf(wide);
            Assert.True(size < CoordinateLabelStyle.FontSize);
            Assert.True(size * 13 <= CoordinateLabelStyle.FontSize * 9, $"{wide} 字号 {size}");
            Assert.True(size >= 40, $"{wide} 字号 {size}：再小就认不出了");
            Assert.True(CoordinateLabelStyle.ColumnOutlineSizeOf(wide) < CoordinateLabelStyle.OutlineSize);
        }

        // 渲染层逐个列标取字号，不自带阈值。
        string view = File.ReadAllText(Path.Combine(RepoRoot(), "src", "godot", "scripts", "BoardView.cs"));
        Assert.Contains("CoordinateLabelStyle.ColumnFontSizeOf(text)", view, StringComparison.Ordinal);
        Assert.Contains("CoordinateLabelStyle.ColumnOutlineSizeOf(text)", view, StringComparison.Ordinal);
    }

    [Fact]
    public void 单字母列标不变()
    {
        // 规格 Scenario：标准图 v5（13 列，全是单字母）的坐标标注画面与引入本条之前相同——字号 96、描边 10 是引入之前渲染层里的字面量。
        // 行号（含两位数）不走列标字号：它们沿左右两边竖排，宽度不影响相邻行。
        // 变异 M-L2（单字母列标字号改 90）→ 红 1（本测试）。
        Assert.Equal((96, 10), (CoordinateLabelStyle.FontSize, CoordinateLabelStyle.OutlineSize));
        foreach (string column in Columns(45).Where(c => c.Length == 1))
        {
            Assert.Equal(96, CoordinateLabelStyle.ColumnFontSizeOf(column));
            Assert.Equal(10, CoordinateLabelStyle.ColumnOutlineSizeOf(column));
        }

        Assert.All(Columns(13), c => Assert.Equal(1, c.Length));

        string view = File.ReadAllText(Path.Combine(RepoRoot(), "src", "godot", "scripts", "BoardView.cs"));
        Assert.Contains("labels.AddChild(Label(text, BoardGeometry.RowLabelAnchor(y, _width, _height, right: false)));", view, StringComparison.Ordinal);
        Assert.Contains("labels.AddChild(Label(text, BoardGeometry.RowLabelAnchor(y, _width, _height, right: true)));", view, StringComparison.Ordinal);
        Assert.Contains("int fontSize = CoordinateLabelStyle.FontSize, int outlineSize = CoordinateLabelStyle.OutlineSize", view, StringComparison.Ordinal);
        Assert.DoesNotContain("FontSize = 96", view, StringComparison.Ordinal);
    }
}
