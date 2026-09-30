using System.Globalization;
using System.Text.RegularExpressions;
using Siege.Presentation.Style;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.VisualStyleBaseline;

/// <summary>规格：visual-style-baseline —— Requirement: 禁手标记的远近可辨（change `forbidden-marks` D5，tasks 2.1 / 2.3）。</summary>
public class 禁手标记的远近可辨Tests
{
    /// <summary>地砖边长与格心间距之比的千分数（Godot 侧 <c>BoardGeometry.TileSize</c> = 0.90）：标记不得画出地砖。</summary>
    private const int TilePermille = 900;

    private static string Script(string name) => File.ReadAllText(Path.Combine(RepoRoot(), "src", "godot", "scripts", name));

    [Fact]
    public void 全局预览下可辨()
    {
        // 规格 Scenario：45 列的棋盘图切到全局预览，盘面上有一个自杀点 → 该格的叉清晰可见，可见范围不小于格边长的一半。
        // 尺寸是格心间距的千分数：叉的每一划长 ≥ 500（半格）且比近景长，线宽与方框线宽都比近景粗一倍以上；整个标记不超出地砖。
        // 变异 M-G1（全局预览取近景尺寸：For 恒返回 Near）→ 红 1；M-G3（渲染层恒按近景取尺寸）→ 红 1；都是本测试。
        PlacementMarkGeometry near = PlacementMarkGeometry.For(overview: false);
        PlacementMarkGeometry far = PlacementMarkGeometry.For(overview: true);

        Assert.True(far.CrossSpan >= 500, $"叉长 {far.CrossSpan}‰");
        Assert.True(far.CrossSpan > near.CrossSpan);
        Assert.True(far.CrossSpan <= TilePermille);
        Assert.True(far.CrossStroke >= near.CrossStroke * 2, $"叉线宽 {far.CrossStroke}‰ 对近景 {near.CrossStroke}‰");
        Assert.True(far.FrameStroke >= near.FrameStroke * 2, $"方框线宽 {far.FrameStroke}‰ 对近景 {near.FrameStroke}‰");
        Assert.True(far.AccentSize >= near.AccentSize * 2);

        // 渲染层按"是否处于全局预览"取尺寸；切换全局预览会重画棋盘（标记随之换尺寸）。
        string view = Script("BoardView.cs");
        Assert.Contains("PlacementMarkGeometry.For(Rig.IsOverview)", view, StringComparison.Ordinal);
        Assert.Contains("AddCross(cell.Coord, color, _overlay, span, stroke)", view, StringComparison.Ordinal);
        string root = Script("GameRoot.cs");
        Assert.Contains("_dirty |= wasOverview != _board.Rig.IsOverview", root, StringComparison.Ordinal);
        Assert.Matches(@"if \(_board\.Rig\.ToggleOverview\(\)\)\s*\{\s*ApplyCamera\(""全局预览""\);\s*_dirty = true;", root);
    }

    [Fact]
    public void 近景不变()
    {
        // 规格 Scenario：标准图缺省镜头下查看一个活棋禁入格 → 标记外观与引入本条之前相同。
        // 引入之前的尺寸是渲染层里的字面量：叉 0.62 × 0.07、方框线宽 0.055（现在仍是这两个图元的缺省参数，供预演与演出的叉 / 环沿用）。
        // 近景尺寸 MUST 与它们逐个相等——从源码里把字面量读出来比，而不是在测试里再抄一遍。
        // 变异 M-G2（近景叉长改 700）→ 红 1（本测试）。
        string view = Script("BoardView.cs");
        System.Text.RegularExpressions.Match cross = Regex.Match(view, @"void AddCross\([^)]*float span = ([0-9.]+)f, float stroke = ([0-9.]+)f\)");
        System.Text.RegularExpressions.Match ring = Regex.Match(view, @"void AddRing\([^)]*float thickness = ([0-9.]+)f\)");
        Assert.True(cross.Success && ring.Success, "没有在 BoardView 里找到叉 / 方框的缺省尺寸。");

        PlacementMarkGeometry near = PlacementMarkGeometry.Near;
        Assert.Equal(Permille(cross.Groups[1].Value), near.CrossSpan);
        Assert.Equal(Permille(cross.Groups[2].Value), near.CrossStroke);
        Assert.Equal(Permille(ring.Groups[1].Value), near.FrameStroke);
        Assert.Equal((620, 70, 55), (near.CrossSpan, near.CrossStroke, near.FrameStroke));
        Assert.Same(near, PlacementMarkGeometry.For(overview: false));

        // 活棋禁入的图元组成与引入之前相同：压暗底 + 方框 + 叉，颜色取所有者阵营色。
        Assert.Equal(
            PlacementMarkParts.Shade | PlacementMarkParts.Frame | PlacementMarkParts.Cross,
            PlacementBlocks.PartsOf(PlacementMarkStyle.LifeSeal));
        Assert.Equal(PlacementMarkTint.OwnerFaction, PlacementBlocks.TintOf(PlacementMarkStyle.LifeSeal));
        Assert.Contains("AddTint(cell.Coord, Visuals.ForbiddenShade, 0.45f);", view, StringComparison.Ordinal);
        Assert.Contains("AddRing(_overlay, cell.Coord, color, false, 0.03f, frame);", view, StringComparison.Ordinal);
    }

    private static int Permille(string literal) =>
        (int)decimal.Round(decimal.Parse(literal, CultureInfo.InvariantCulture) * 1000m);
}
