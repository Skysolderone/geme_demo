using Godot;
using Siege.Presentation.Style;

namespace Siege.Godot;

/// <summary>
/// HUD 的控件工厂：石墨蓝面板、金属镶边、青色操作提示和紧凑的 PC 策略游戏信息密度。
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

    /// <summary>当前操作与选中态的冷色强调。</summary>
    public static Color Accent => Visuals.ToColor(UiTheme.Accent);

    /// <summary>次要文字色。</summary>
    public static Color MutedText => Visuals.ToColor(UiTheme.InfoText) with { A = 0.62f };

    /// <summary>系统中文字体（界面主题与盘面上的中文 3D 标注共用一份）。Godot 自带字体没有中文字形，必须指定系统字体。</summary>
    public static readonly SystemFont ChineseFont = new()
    {
        FontNames = ["Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Noto Sans CJK SC", "Sans-Serif"],
    };

    /// <summary>界面主题：系统中文字体 + 正文字号。</summary>
    public static Theme BuildTheme() => new() { DefaultFont = ChineseFont, DefaultFontSize = UiTheme.BodyFontPx };

    /// <summary>上沿金线和左侧铭牌棱线让各块信息面板成为同一套沙盘仪表。</summary>
    public static StyleBoxFlat PanelStyle(int margin = 8)
    {
        var box = new StyleBoxFlat
        {
            BgColor = PanelFill,
            BorderColor = PanelBorder,
            ContentMarginLeft = margin + 3,
            ContentMarginRight = margin + 2,
            ContentMarginTop = margin + 2,
            ContentMarginBottom = margin,
            BorderWidthLeft = UiTheme.BorderWidthPx + 1,
            BorderWidthTop = 2,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 2,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 2,
            CornerRadiusBottomRight = 4,
            ShadowColor = new Color(0.015f, 0.025f, 0.035f, 0.66f),
            ShadowSize = 7,
            ShadowOffset = new Vector2(0f, 3f),
        };
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
            label.AutowrapMode = TextServer.AutowrapMode.Arbitrary;
        }

        return label;
    }

    /// <summary>标题行：小型铭牌符号与明金文字，不占额外一行。</summary>
    public static Label Heading(string text)
    {
        Label label = Text("◆  " + text, Visuals.ToColor(UiTheme.PanelTop), UiTheme.BodyFontPx);
        label.AddThemeColorOverride("font_outline_color", PanelFill);
        label.AddThemeConstantOverride("outline_size", 1);
        return label;
    }

    private static StyleBoxFlat ButtonStyle(Color fill, Color edge, int topWidth = 1) => new()
    {
        BgColor = fill,
        BorderColor = edge,
        BorderWidthLeft = 2,
        BorderWidthTop = topWidth,
        BorderWidthRight = 1,
        BorderWidthBottom = 1,
        CornerRadiusTopLeft = 2,
        CornerRadiusTopRight = 5,
        CornerRadiusBottomLeft = 2,
        CornerRadiusBottomRight = 5,
        ContentMarginLeft = 7,
        ContentMarginRight = 7,
        ContentMarginTop = 3,
        ContentMarginBottom = 3,
    };

    private static void StyleButton(Button button, Color edge)
    {
        button.AddThemeStyleboxOverride("normal", ButtonStyle(Visuals.ToColor(UiTheme.ButtonFill), edge));
        button.AddThemeStyleboxOverride("hover", ButtonStyle(Visuals.ToColor(UiTheme.ButtonHover), Accent, 2));
        button.AddThemeStyleboxOverride("pressed", ButtonStyle(Visuals.ToColor(UiTheme.ButtonPressed), PanelBorder, 2));
        button.AddThemeStyleboxOverride("disabled", ButtonStyle(Visuals.ToColor(UiTheme.ButtonDisabled), Visuals.ToColor(UiTheme.PanelInset)));
        button.AddThemeStyleboxOverride("focus", ButtonStyle(new Color(0f, 0f, 0f, 0f), Accent, 2));
        button.AddThemeColorOverride("font_color", InfoText);
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_pressed_color", Visuals.ToColor(UiTheme.PanelTop));
        button.AddThemeColorOverride("font_disabled_color", MutedText);
    }

    /// <summary>
    /// 标准按钮。高度固定为 <see cref="UiTheme.ButtonHeightPx"/>，远低于
    /// <see cref="UiTheme.MobileStyleButtonHeightPx"/>——默认 UI 不出现手游式大按钮（5.5）。
    /// </summary>
    public static Button Action(string text, int minWidth = 0)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(minWidth, UiTheme.ButtonHeightPx) };
        button.AddThemeFontSizeOverride("font_size", UiTheme.BodyFontPx - 1);
        StyleButton(button, Visuals.ToColor(UiTheme.PanelInset));
        return button;
    }

    /// <summary>主要确认动作使用亮金镶边，与普通按钮层级不同。</summary>
    public static void Primary(Button button)
    {
        StyleButton(button, PanelBorder);
        button.AddThemeColorOverride("font_color", Visuals.ToColor(UiTheme.PanelTop));
    }

    /// <summary>危险动作使用红色棱线，保留同一按钮形状与键盘焦点表现。</summary>
    public static void Danger(Button button)
    {
        StyleButton(button, DangerText);
        button.AddThemeColorOverride("font_color", DangerText);
    }

    /// <summary>可切换状态的按钮（信息层按钮）。</summary>
    public static Button Toggle(string text, bool active, int minWidth = 0)
    {
        Button button = Action(text, minWidth);
        Selected(button, active);
        return button;
    }

    /// <summary>复用切换态皮肤，供不重建按钮的选图难度栏刷新。</summary>
    public static void Selected(Button button, bool active)
    {
        if (active)
        {
            button.AddThemeStyleboxOverride("normal", ButtonStyle(Visuals.ToColor(UiTheme.ActiveFill), PanelBorder, 2));
            button.AddThemeStyleboxOverride("hover", ButtonStyle(Visuals.ToColor(UiTheme.ActiveFill), Accent, 2));
            button.AddThemeColorOverride("font_color", Visuals.ToColor(UiTheme.PanelTop));
        }
        else
        {
            StyleButton(button, Visuals.ToColor(UiTheme.PanelInset));
        }
    }

    /// <summary>横向分隔。</summary>
    public static HSeparator Separator()
    {
        var separator = new HSeparator();
        separator.AddThemeConstantOverride("separation", 6);
        separator.AddThemeStyleboxOverride("separator", new StyleBoxFlat
        {
            BgColor = new Color(PanelBorder, 0.55f),
            ContentMarginTop = 1,
            ContentMarginBottom = 0,
        });
        return separator;
    }

    /// <summary>输入框和下拉框与按钮共用深色底和可见焦点。</summary>
    public static void Field(LineEdit field)
    {
        field.AddThemeStyleboxOverride("normal", ButtonStyle(Visuals.ToColor(UiTheme.ButtonFill), Visuals.ToColor(UiTheme.PanelInset)));
        field.AddThemeStyleboxOverride("focus", ButtonStyle(new Color(0f, 0f, 0f, 0f), Accent, 2));
        field.AddThemeColorOverride("font_color", InfoText);
        field.AddThemeColorOverride("font_placeholder_color", MutedText);
        field.AddThemeColorOverride("caret_color", Accent);
    }

    public static void Field(OptionButton field) => StyleButton(field, Visuals.ToColor(UiTheme.PanelInset));

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
