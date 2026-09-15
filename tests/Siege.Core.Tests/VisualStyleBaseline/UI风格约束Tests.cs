using Siege.Presentation.Style;

namespace Siege.Core.Tests.VisualStyleBaseline;

/// <summary>规格：visual-style-baseline —— Requirement: UI 风格约束</summary>
public class UI风格约束Tests
{
    [Fact]
    public void 信息密度()
    {
        // 设计文档 §20：深色半透明面板、克制金色边框、高对比信息色，PC 策略游戏信息密度，无手游式大按钮。
        // 数据层基准（1080p 参考）：面板半透明（alpha 128–240）且暗（亮度 ≤ 48）；边框为金色系（R > G > B）且线宽 ≤ 2；
        // 信息文字与面板亮度差 ≥ 160；按钮高度低于手游式下限、正文字号 ≤ 16。实际观感与弹窗形态归阶段 B + 人工检查清单。
        // 变异验证 M-UI1：UiTheme.PanelFill 的 alpha 改为 255（不透明）→ 本测试红 1。
        Assert.InRange(UiTheme.PanelFill.A, (byte)128, (byte)240);
        Assert.True(UiTheme.PanelFill.Luma <= 48, $"面板亮度 {UiTheme.PanelFill.Luma}");
        Assert.True(UiTheme.PanelBorder.R > UiTheme.PanelBorder.G && UiTheme.PanelBorder.G > UiTheme.PanelBorder.B, UiTheme.PanelBorder.Hex);
        Assert.InRange(UiTheme.BorderWidthPx, 1, 2);
        Assert.True(UiTheme.InfoText.Luma - UiTheme.PanelFill.Luma >= 160, $"对比 {UiTheme.InfoText.Luma - UiTheme.PanelFill.Luma}");
        Assert.True(UiTheme.DangerText.Luma - UiTheme.PanelFill.Luma >= 80, $"警示色对比 {UiTheme.DangerText.Luma - UiTheme.PanelFill.Luma}");
        Assert.True(UiTheme.ButtonHeightPx < UiTheme.MobileStyleButtonHeightPx);
        Assert.InRange(UiTheme.BodyFontPx, 12, 16);
    }
}
