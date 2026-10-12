using Siege.Presentation.Style;

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
        string visuals = File.ReadAllText(Path.Combine(FrontierFixtures.RepoRoot(), "src", "godot", "scripts", "Visuals.cs"));
        Assert.Contains("PublicBoardFrame = ToColor(BoardPlatePalette.PublicFrame)", visuals, StringComparison.Ordinal);
        Assert.Contains("PlaceholderFrame = ToColor(BoardPlatePalette.PlaceholderFrame)", visuals, StringComparison.Ordinal);
        Assert.Contains("PlaceholderLabel = ToColor(BoardPlatePalette.PlaceholderLabel)", visuals, StringComparison.Ordinal);
        Assert.Contains("BirthHint = ToColor(BoardPlatePalette.BirthHint)", visuals, StringComparison.Ordinal);
        Assert.DoesNotContain("Color8(232, 228, 214)", visuals, StringComparison.Ordinal);
        Assert.DoesNotContain("Color8(232, 198, 96)", visuals, StringComparison.Ordinal);

        // 占位只画边框与编号：BoardView 的占位方法里不得出现地砖 / 格线 / 棋子 / 信物的搭建调用。
        string boardView = File.ReadAllText(Path.Combine(FrontierFixtures.RepoRoot(), "src", "godot", "scripts", "BoardView.cs"));
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
}
