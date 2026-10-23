using Siege.Presentation.Style;

namespace Siege.Core.Tests.VisualStyleBaseline;

public class 基础主题移植Tests
{
    [Fact]
    public void 三级面板保持基础版配色和对比()
    {
        UiPanelStyle primary = UiTheme.PanelStyleOf(PanelTier.Primary);
        UiPanelStyle secondary = UiTheme.PanelStyleOf(PanelTier.Secondary);
        UiPanelStyle hint = UiTheme.PanelStyleOf(PanelTier.Hint);

        Assert.Equal(new Rgba(16, 25, 34, 222), primary.Fill);
        Assert.Equal(UiTheme.PanelBorder, primary.Border);
        Assert.True(Math.Abs(primary.Fill.Luma - secondary.Fill.Luma) >= 8);
        Assert.True(Math.Abs(primary.Border.Luma - secondary.Border.Luma) >= 40);
        Assert.Equal(0, hint.BorderWidthPx);
        Assert.Equal(0, hint.Border.A);
        Assert.True(hint.CornerRadiusPx > primary.CornerRadiusPx);
    }

    [Fact]
    public void 按钮语义状态和选中边可辨()
    {
        foreach (ButtonKind kind in Enum.GetValues<ButtonKind>())
        {
            UiButtonStyle normal = UiTheme.ButtonStyleOf(kind, ButtonState.Normal);
            UiButtonStyle hover = UiTheme.ButtonStyleOf(kind, ButtonState.Hover);
            UiButtonStyle pressed = UiTheme.ButtonStyleOf(kind, ButtonState.Pressed);
            UiButtonStyle disabled = UiTheme.ButtonStyleOf(kind, ButtonState.Disabled);
            UiButtonStyle focus = UiTheme.ButtonStyleOf(kind, ButtonState.Focus);
            Assert.NotEqual(normal.Fill, hover.Fill);
            Assert.NotEqual(normal.Fill, pressed.Fill);
            Assert.Equal(UiTheme.ButtonDisabled, disabled.Fill);
            Assert.False(focus.DrawFill);
            Assert.Equal(0, focus.Fill.A);
        }

        UiButtonStyle selected = UiTheme.ButtonStyleOf(ButtonKind.Default, ButtonState.Normal, selected: true);
        Assert.Equal(UiTheme.SelectedEdgeWidthPx, selected.Bottom.WidthPx);
        Assert.Equal(UiTheme.BorderWidthPx, selected.Top.WidthPx);
        Assert.Equal(UiTheme.PanelBorder, selected.Bottom.Color);
    }

    [Fact]
    public void 字号和间距遵循阶梯()
    {
        Assert.Equal([13, 14, 15, 17, 19], UiTheme.FontLadder);
        Assert.Equal([0, 2, 4, 6, 8, 12], UiTheme.SpaceLadder);
        Assert.True(UiTheme.ButtonHeightPx < UiTheme.MobileStyleButtonHeightPx);
    }
}
