using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.VisualStyleBaseline;

/// <summary>
/// 规格：visual-style-baseline —— Requirement: 棋盘坐标标注（change `board-coordinates`）。
/// </summary>
/// <remarks>
/// <para>标注本身是视觉的，能被自动化钉住的只有一件事，而这件事恰恰是最容易出事的：
/// <b>表现层不得再写一份跳过 <c>I</c> 的字母表</b>。一旦写了，界面与对局日志会指向不同的格子，
/// 而两边各自看起来都对——这类错误没有人会在截图里发现。</para>
/// <para><c>src/godot/</c> 用独立解决方案、不进 <c>siege.sln</c>，<c>dotnet test</c> 对它完全隐身
/// （testing.md「不在解决方案里的工程对守门测试完全隐身」），所以只能做源码文本扫描。</para>
/// </remarks>
public class 棋盘坐标标注Tests
{
    private static (string Name, string Text)[] Scripts()
    {
        string dir = Path.Combine(RepoRoot(), "src", "godot", "scripts");
        Assert.True(Directory.Exists(dir), dir);
        (string, string)[] all =
            [.. Directory.GetFiles(dir, "*.cs").Order().Select(f => (Path.GetFileName(f), File.ReadAllText(f)))];

        // 样本口径下界：路径写错或扫到空目录时，下面所有"不含"断言都会恒真。
        Assert.True(all.Length >= 10, $"只扫到 {all.Length} 个脚本");
        Assert.True(all.Sum(s => s.Item2.Length) >= 50_000, $"只扫到 {all.Sum(s => s.Item2.Length)} 字符");
        return all;
    }

    [Fact]
    public void 表现层不得自带跳过I的列字母表()
    {
        // 正面先立住：列字母的唯一来源是 Coord.ColumnLetters，它本身已经跳过 I。
        Assert.Equal("ABCDEFGHJKLMNOPQRSTUVWXYZ", Siege.Core.Board.Coord.ColumnLetters);
        Assert.DoesNotContain('I', Siege.Core.Board.Coord.ColumnLetters);

        // 反面：Godot 层不得出现第二份字母表。不写成 \b 开头的正则——testing.md 记过，
        // 那样匹配不到 BoardColumnLetters 这类复合标识符。
        foreach ((string name, string text) in Scripts())
        {
            Assert.DoesNotContain("ABCDEFGH", text, StringComparison.Ordinal);
            Assert.DoesNotContain("\"ABCDEFG", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void 标注文本取自坐标记法而非自行推算()
    {
        string view = Scripts().Single(s => s.Name == "BoardView.cs").Text;

        // 标注文本必须走 Coord 的记法属性。
        Assert.Contains(".Column", view, StringComparison.Ordinal);
        Assert.Contains(".Row", view, StringComparison.Ordinal);

        // 位置必须走 BoardGeometry 的锚点，不在视图层另算一份坐标→世界的映射。
        Assert.Contains("BoardGeometry.ColumnLabelAnchor", view, StringComparison.Ordinal);
        Assert.Contains("BoardGeometry.RowLabelAnchor", view, StringComparison.Ordinal);
    }

    [Fact]
    public void 标注锚点由格心推出且四边各在一侧()
    {
        string geometry = Scripts().Single(s => s.Name == "BoardGeometry.cs").Text;

        // 锚点必须由 Center 推出——它是 Coord ↔ 3D 的唯一映射，标注与格子才会永远对齐。
        int columnAt = geometry.IndexOf("ColumnLabelAnchor", StringComparison.Ordinal);
        int rowAt = geometry.IndexOf("RowLabelAnchor", StringComparison.Ordinal);
        Assert.True(columnAt > 0 && rowAt > 0, "两个锚点方法都应在 BoardGeometry 内");

        // 列标注沿 Z 偏移（棋盘上下两边），行标注沿 X 偏移（左右两边）；写反了就会挤在同一侧。
        string columnBody = geometry[columnAt..rowAt];
        string rowBody = geometry[rowAt..];
        Assert.Contains("Z = edge.Z", columnBody, StringComparison.Ordinal);
        Assert.DoesNotContain("X = edge.X", columnBody, StringComparison.Ordinal);
        Assert.Contains("X = edge.X", rowBody, StringComparison.Ordinal);
    }

    [Fact]
    public void 标注不用billboard()
    {
        // design.md D2 修订：Godot 的 Label3D 在 billboard 下渲染文字面的背面，四边标注全部左右镜像。
        // 1 / 0 / 8 字形对称看不出来，2 / 3 / 5 / 7 与 B / K / L 才暴露。改为平铺在棋盘平面。
        // 这条挡的是"将来有人顺手把 billboard 打开"——那会直接回到镜像，且很难从截图上察觉。
        string view = Scripts().Single(s => s.Name == "BoardView.cs").Text;
        Assert.DoesNotContain("BillboardModeEnum.Enabled", view, StringComparison.Ordinal);
        Assert.DoesNotContain("BillboardModeEnum.FixedY", view, StringComparison.Ordinal);
        Assert.Contains("BillboardModeEnum.Disabled", view, StringComparison.Ordinal);
    }
}
