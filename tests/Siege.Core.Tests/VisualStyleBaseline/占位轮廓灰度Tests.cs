using Siege.Presentation.Style;

// retire-legacy-maps：仓库根目录改取 TestPaths.RepoRoot()（原 FrontierFixtures.RepoRoot，随旧地图夹具删除而原样迁出）。
namespace Siege.Core.Tests.VisualStyleBaseline;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/visual-style-baseline —— Requirement: 未展开区域的可视表现（Scenario「占位灰度可辨」）。
/// 占位台面边框在灰度下 MUST 与已展开棋盘的台面（公共棋盘中性边框、出生棋盘的阵营色边框、锁定前的出生区提示色）可辨——
/// 规格引用的"既有棋盘台面条款阈值"在既有规格与裁决里没有数值（D-b 报告 D21），本 change 取 <see cref="BoardPlatePalette.MinPlaceholderLumaGap"/> = 40。
/// 颜色的唯一来源是 Presentation 调色板，Godot 经 <c>Visuals.ToColor</c> 取用（源码扫描守住）。
/// </summary>
/// <remarks>变异验证（段 D-b 实跑）：M-E3 占位边框改成 (110, 112, 120)（与阵营色亮度接近）→ 「占位边框与每种已展开台面的亮度差不小于阈值」红 1。</remarks>
public class 占位轮廓灰度Tests
{
    [Fact]
    public void 占位边框与每种已展开台面的亮度差不小于阈值()
    {
        int placeholder = BoardPlatePalette.PlaceholderFrame.Luma;
        Rgba[] frames = [.. BoardPlatePalette.ExpandedFrames];
        Assert.Equal(6, frames.Length);   // 公共中性色 + 出生区提示色 + 四方
        foreach (Rgba frame in frames)
        {
            Assert.True(Math.Abs(frame.Luma - placeholder) >= BoardPlatePalette.MinPlaceholderLumaGap,
                $"占位边框亮度 {placeholder} 与台面 {frame.Hex}（亮度 {frame.Luma}）只差 {Math.Abs(frame.Luma - placeholder)}");
        }

        // 低饱和：三通道极差很小；编号文字与边框本身亮度差够读。
        Rgba p = BoardPlatePalette.PlaceholderFrame;
        Assert.True(Math.Max(p.R, Math.Max(p.G, p.B)) - Math.Min(p.R, Math.Min(p.G, p.B)) <= 24, $"占位边框饱和过高：{p.Hex}");
        Assert.True(BoardPlatePalette.PlaceholderLabel.Luma - placeholder >= 120);
    }

    [Fact]
    public void 台面与占位颜色只在调色板定义_Godot经转换取用()
    {
        // Godot 的 Visuals 不得再手写一份台面 / 占位颜色字面量（否则调色板的灰度断言守不住画面）。
        string visuals = File.ReadAllText(Path.Combine(TestPaths.RepoRoot(), "src", "godot", "scripts", "Visuals.cs"));
        Assert.Contains("PublicBoardFrame = ToColor(BoardPlatePalette.PublicFrame)", visuals, StringComparison.Ordinal);
        Assert.Contains("PlaceholderFrame = ToColor(BoardPlatePalette.PlaceholderFrame)", visuals, StringComparison.Ordinal);
        Assert.Contains("PlaceholderLabel = ToColor(BoardPlatePalette.PlaceholderLabel)", visuals, StringComparison.Ordinal);
        Assert.Contains("BirthHint = ToColor(BoardPlatePalette.BirthHint)", visuals, StringComparison.Ordinal);
        Assert.DoesNotContain("Color8(232, 228, 214)", visuals, StringComparison.Ordinal);
        Assert.DoesNotContain("Color8(232, 198, 96)", visuals, StringComparison.Ordinal);

        // 占位只画边框与编号：BoardView 的占位方法里不得出现地砖 / 格线 / 棋子 / 信物的搭建调用。
        string boardView = File.ReadAllText(Path.Combine(TestPaths.RepoRoot(), "src", "godot", "scripts", "BoardView.cs"));
        int start = boardView.IndexOf("private void AddPlaceholders(", StringComparison.Ordinal);
        int end = boardView.IndexOf("private const float WaterDrop", start, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "BoardView 缺少 AddPlaceholders");
        string body = boardView[start..end];
        foreach (string banned in new[] { "AddTileStack(", "lineWidth", "DrawPieces(", "DrawRelicMarkers(", "BuildPiece(", "TerrainParts." })
        {
            Assert.DoesNotContain(banned, body, StringComparison.Ordinal);
        }

        Assert.Contains("Where(p => !p.Expanded)", body, StringComparison.Ordinal);
        Assert.Contains("BirthZoneLabel.Number(", body, StringComparison.Ordinal);
    }

    [Fact]
    public void 占位主题名只经视图模型取用_Godot不另写一份()
    {
        // link-map-terrain 6.1：插旗阶段的占位在编号下方显示主题名，字样只经 BoardPlaceholderView.ThemeName（→ Core BoardThemeNames.DisplayName）。
        // Godot 与 Presentation 不得再写一份"主题 → 中文名"的映射：不出现按 BoardTheme 分支的 switch 臂；Godot 侧也不出现五个主题名的整串字面量。
        // 变异 MLT-G3（AddPlaceholders 改用本地 switch：BoardTheme.Plateau => "高台" …，实跑）→ 本测试红 1；MLT-G4（AddPlaceholders 不画主题名，实跑）→ 本测试红 1。
        string root = TestPaths.RepoRoot();
        string godotDir = Path.Combine(root, "src", "godot", "scripts");
        string presentationDir = Path.Combine(root, "src", "Siege.Presentation");
        (string Name, string Text)[] godot = [.. Directory.GetFiles(godotDir, "*.cs").Order().Select(f => (Path.GetFileName(f), File.ReadAllText(f)))];
        (string Name, string Text)[] presentation = [.. Directory.GetFiles(presentationDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Order().Select(f => (Path.GetFileName(f), File.ReadAllText(f)))];
        Assert.True(godot.Length >= 10 && presentation.Length >= 10, $"只扫到 Godot {godot.Length} / Presentation {presentation.Length} 个文件");

        var switchArm = new System.Text.RegularExpressions.Regex(@"BoardTheme\.(Plateau|River|Forest|Palisade|Mixed)\s*=>");
        string[] literals = ["\"高台\"", "\"河流\"", "\"林地\"", "\"栅栏阵\"", "\"混合\""];
        string[] violations =
        [
            .. godot.Concat(presentation).Where(s => switchArm.IsMatch(s.Text)).Select(s => $"{s.Name}: BoardTheme 分支"),
            .. godot.SelectMany(s => literals.Where(l => s.Text.Contains(l, StringComparison.Ordinal)).Select(l => $"{s.Name}: {l}")),
        ];
        Assert.Empty(violations);

        // 正向：占位方法确实画主题名，且取自视图模型。
        string boardView = File.ReadAllText(Path.Combine(godotDir, "BoardView.cs"));
        int start = boardView.IndexOf("private void AddPlaceholders(", StringComparison.Ordinal);
        int end = boardView.IndexOf("private const float WaterDrop", start, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "BoardView 缺少 AddPlaceholders");
        Assert.Contains("placeholder.ThemeName", boardView[start..end], StringComparison.Ordinal);
    }
}
