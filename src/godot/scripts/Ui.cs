using Godot;
using Siege.Presentation.Style;

namespace Siege.Godot;

/// <summary>
/// HUD 的控件工厂：深色半透明面板、克制的金色边框、高对比信息色、PC 策略游戏的信息密度（§20 / 5.5）。
/// 全部尺寸与颜色取自 <see cref="UiTheme"/>，这里不另立一套数值。
/// </summary>
public static class Ui
{
    /// <summary>面板底色。</summary>
    public static Color PanelFill => Visuals.ToColor(UiTheme.PanelFill);

    /// <summary>面板边框（金色）。</summary>
    public static Color PanelBorder => Visuals.ToColor(UiTheme.PanelBorder);

    /// <summary>正文色。</summary>
    public static Color InfoText => Visuals.ToColor(UiTheme.InfoText);

    /// <summary>警示色。</summary>
    public static Color DangerText => Visuals.ToColor(UiTheme.DangerText);

    /// <summary>次要文字色。</summary>
    public static Color MutedText => Visuals.ToColor(UiTheme.InfoText) with { A = 0.62f };

    /// <summary>界面主题：系统中文字体 + 正文字号。Godot 自带字体没有中文字形，必须指定系统字体。</summary>
    public static Theme BuildTheme()
    {
        var font = new SystemFont
        {
            FontNames = ["Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Noto Sans CJK SC", "Sans-Serif"],
        };
        return new Theme { DefaultFont = font, DefaultFontSize = UiTheme.BodyFontPx };
    }

    /// <summary>深色半透明面板样式。</summary>
    public static StyleBoxFlat PanelStyle(int margin = 8)
    {
        var box = new StyleBoxFlat
        {
            BgColor = PanelFill,
            BorderColor = PanelBorder,
            ContentMarginLeft = margin,
            ContentMarginRight = margin,
            ContentMarginTop = margin,
            ContentMarginBottom = margin,
            CornerRadiusTopLeft = 3,
            CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3,
            CornerRadiusBottomRight = 3,
        };
        box.SetBorderWidthAll(UiTheme.BorderWidthPx);
        return box;
    }

    /// <summary>一个带边框的面板容器，内含一个纵向列表。</summary>
    public static (PanelContainer Panel, VBoxContainer Body) Panel(int margin = 8, int separation = 2)
    {
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", PanelStyle(margin));
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", separation);
        panel.AddChild(body);
        return (panel, body);
    }

    /// <summary>一行文字。</summary>
    public static Label Text(string text, Color? color = null, int? size = null, bool wrap = false)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeColorOverride("font_color", color ?? InfoText);
        label.AddThemeFontSizeOverride("font_size", size ?? UiTheme.BodyFontPx);
        if (wrap)
        {
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        }

        return label;
    }

    /// <summary>标题行（小字号 + 金色）。</summary>
    public static Label Heading(string text) => Text(text, PanelBorder, UiTheme.BodyFontPx - 2);

    /// <summary>
    /// 标准按钮。高度固定为 <see cref="UiTheme.ButtonHeightPx"/>，远低于
    /// <see cref="UiTheme.MobileStyleButtonHeightPx"/>——默认 UI 不出现手游式大按钮（5.5）。
    /// </summary>
    public static Button Action(string text, int minWidth = 0)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(minWidth, UiTheme.ButtonHeightPx) };
        button.AddThemeFontSizeOverride("font_size", UiTheme.BodyFontPx - 1);
        button.AddThemeColorOverride("font_color", InfoText);
        return button;
    }

    /// <summary>可切换状态的按钮（信息层按钮）。</summary>
    public static Button Toggle(string text, bool active, int minWidth = 0)
    {
        Button button = Action(text, minWidth);
        if (active)
        {
            button.AddThemeColorOverride("font_color", PanelBorder);
        }

        return button;
    }

    /// <summary>横向分隔。</summary>
    public static HSeparator Separator()
    {
        var separator = new HSeparator();
        separator.AddThemeConstantOverride("separation", 4);
        return separator;
    }

    /// <summary>用锚点 + 偏移固定某个控件，分辨率变化时位置保持。</summary>
    public static void Anchor(Control control, float anchorX, float anchorY, float left, float top, float right, float bottom)
    {
        control.AnchorLeft = anchorX;
        control.AnchorRight = anchorX;
        control.AnchorTop = anchorY;
        control.AnchorBottom = anchorY;
        control.OffsetLeft = left;
        control.OffsetTop = top;
        control.OffsetRight = right;
        control.OffsetBottom = bottom;
    }

    /// <summary>清空容器。</summary>
    public static void Clear(Node container)
    {
        foreach (Node child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }
}
