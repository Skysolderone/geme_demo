using System;
using Godot;
using Siege.Presentation.Style;

namespace Siege.Godot;

/// <summary>
/// HUD 的控件工厂：深色半透明面板、克制的金色边框、高对比信息色、PC 策略游戏的信息密度（§20 / 5.5）。
/// 全部尺寸与颜色取自 <see cref="UiTheme"/>，这里不另立一套数值，也不做亮度、对比度或"禁用盖过语义"之类的判断——
/// 只把 <see cref="UiTheme.PanelStyleOf"/> / <see cref="UiTheme.ButtonStyleOf"/> 查到的值翻成 Godot 的样式盒，按控件类型写进 <see cref="Theme"/>（hud-theme D4）。
/// HUD 脚本只说"这是什么"（哪一级面板、哪种语义的按钮、哪一级字号），不说"长什么样"。
/// </summary>
public static class Ui
{
    // ---------- 主题类型名：只在这里写字符串（写错不会报错，按钮会静默落回默认样式） ----------

    /// <summary>默认按钮的主题类型（引擎的 <c>Button</c>）。</summary>
    public const string ButtonType = "Button";

    /// <summary>主操作按钮的主题变体。</summary>
    public const string PrimaryButtonVariation = "SiegePrimaryButton";

    /// <summary>危险操作按钮的主题变体。</summary>
    public const string DangerButtonVariation = "SiegeDangerButton";

    /// <summary>选中的可切换按钮的主题变体。</summary>
    public const string SelectedButtonVariation = "SiegeSelectedButton";

    /// <summary>提示条文字（带提示条样式盒与描边的 <c>Label</c>）的主题变体。</summary>
    public const string HintLabelVariation = "SiegeHintLabel";

    /// <summary>演出横幅文字（带描边的 <c>Label</c>）的主题变体。</summary>
    public const string ShowBannerVariation = "SiegeShowBanner";

    /// <summary>字号在主题里的项名。</summary>
    public const string FontSizeItem = "font_size";

    private const string PanelType = "PanelContainer";
    private const string PanelVariationPrefix = "SiegePanel";

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

    /// <summary>按钮某一状态在主题里的样式盒名。</summary>
    public static string StyleboxNameOf(ButtonState state) => state switch
    {
        ButtonState.Normal => "normal",
        ButtonState.Hover => "hover",
        ButtonState.Pressed => "pressed",
        ButtonState.Disabled => "disabled",
        ButtonState.Focus => "focus",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "未知按钮状态。"),
    };

    /// <summary>按钮某一状态在主题里的字色名。</summary>
    public static string FontColorNameOf(ButtonState state) => state switch
    {
        ButtonState.Normal => "font_color",
        ButtonState.Hover => "font_hover_color",
        ButtonState.Pressed => "font_pressed_color",
        ButtonState.Disabled => "font_disabled_color",
        ButtonState.Focus => "font_focus_color",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "未知按钮状态。"),
    };

    /// <summary>某种语义的按钮在主题里的类型名（默认语义即引擎的 <c>Button</c>）。</summary>
    public static string ButtonTypeOf(ButtonKind kind) => kind switch
    {
        ButtonKind.Default => ButtonType,
        ButtonKind.Primary => PrimaryButtonVariation,
        ButtonKind.Danger => DangerButtonVariation,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知按钮语义。"),
    };

    /// <summary>某一级面板、某一档内边距在主题里的变体名。内边距逐面板不同，所以样式盒按"级别 + 内边距"生成。</summary>
    public static string PanelVariationOf(PanelTier tier, int padding)
    {
        RequireSpace(padding, nameof(padding));
        return $"{PanelVariationPrefix}{tier}{padding}";
    }

    /// <summary>
    /// 界面主题：系统中文字体 + 正文字号（Godot 自带字体没有中文字形，必须指定系统字体），加上按类型下发的样式——
    /// 按钮五态与字色、三个按钮变体（主操作 / 危险 / 选中）、面板三级 × 间距阶梯、提示条与演出横幅文字、输入框、分隔线。
    /// <c>OptionButton</c> 沿类继承链回落到 <c>Button</c> 的样式盒与字色，不另写（只补字号，保持它原来的正文字号）。
    /// </summary>
    public static Theme BuildTheme()
    {
        var font = new SystemFont
        {
            FontNames = ["Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Noto Sans CJK SC", "Sans-Serif"],
        };
        var theme = new Theme { DefaultFont = font, DefaultFontSize = UiTheme.Body };

        WriteButton(theme, ButtonType, ButtonKind.Default, selected: false);
        theme.SetTypeVariation(PrimaryButtonVariation, ButtonType);
        WriteButton(theme, PrimaryButtonVariation, ButtonKind.Primary, selected: false);
        theme.SetTypeVariation(DangerButtonVariation, ButtonType);
        WriteButton(theme, DangerButtonVariation, ButtonKind.Danger, selected: false);
        theme.SetTypeVariation(SelectedButtonVariation, ButtonType);
        WriteButton(theme, SelectedButtonVariation, ButtonKind.Default, selected: true);
        theme.SetFontSize(FontSizeItem, ButtonType, UiTheme.Small);
        theme.SetFontSize(FontSizeItem, "OptionButton", UiTheme.Body);

        theme.SetStylebox("panel", PanelType, PanelBox(PanelTier.Primary, UiTheme.Space4));
        foreach (PanelTier tier in Enum.GetValues<PanelTier>())
        {
            foreach (int padding in UiTheme.SpaceLadder)
            {
                string variation = PanelVariationOf(tier, padding);
                theme.SetTypeVariation(variation, PanelType);
                theme.SetStylebox("panel", variation, PanelBox(tier, padding));
            }
        }

        theme.SetTypeVariation(HintLabelVariation, "Label");
        theme.SetStylebox("normal", HintLabelVariation, HintLabelBox());
        theme.SetColor("font_outline_color", HintLabelVariation, Visuals.ToColor(UiTheme.TextOutline));
        theme.SetConstant("outline_size", HintLabelVariation, UiTheme.HintOutlinePx);
        theme.SetTypeVariation(ShowBannerVariation, "Label");
        theme.SetColor("font_outline_color", ShowBannerVariation, Visuals.ToColor(UiTheme.TextOutline));
        theme.SetConstant("outline_size", ShowBannerVariation, UiTheme.ShowBannerOutlinePx);

        // 输入框：常态 / 焦点 / 只读取按钮常态 / 焦点 / 禁用的取值。
        theme.SetStylebox("normal", "LineEdit", ButtonBox(UiTheme.ButtonStyleOf(ButtonKind.Default, ButtonState.Normal)));
        theme.SetStylebox("focus", "LineEdit", ButtonBox(UiTheme.ButtonStyleOf(ButtonKind.Default, ButtonState.Focus)));
        theme.SetStylebox("read_only", "LineEdit", ButtonBox(UiTheme.ButtonStyleOf(ButtonKind.Default, ButtonState.Disabled)));
        theme.SetColor("font_color", "LineEdit", Visuals.ToColor(UiTheme.ButtonStyleOf(ButtonKind.Default, ButtonState.Normal).Text));
        theme.SetColor("font_uneditable_color", "LineEdit", Visuals.ToColor(UiTheme.ButtonStyleOf(ButtonKind.Default, ButtonState.Disabled).Text));
        theme.SetFontSize(FontSizeItem, "LineEdit", UiTheme.Body);

        // 分隔线：次面板的边框色。
        theme.SetStylebox("separator", "HSeparator", new StyleBoxLine { Color = Visuals.ToColor(UiTheme.SecondaryPanelBorder), Thickness = UiTheme.BorderWidthPx });
        theme.SetConstant("separation", "HSeparator", UiTheme.Space2);
        return theme;
    }

    /// <summary>一组（语义、是否选中）的五态样式盒与字色。按下且悬停沿用按下那一态。</summary>
    private static void WriteButton(Theme theme, string type, ButtonKind kind, bool selected)
    {
        foreach (ButtonState state in Enum.GetValues<ButtonState>())
        {
            UiButtonStyle style = UiTheme.ButtonStyleOf(kind, state, selected);
            theme.SetStylebox(StyleboxNameOf(state), type, ButtonBox(style));
            theme.SetColor(FontColorNameOf(state), type, Visuals.ToColor(style.Text));
        }

        UiButtonStyle pressed = UiTheme.ButtonStyleOf(kind, ButtonState.Pressed, selected);
        theme.SetStylebox("hover_pressed", type, ButtonBox(pressed));
        theme.SetColor("font_hover_pressed_color", type, Visuals.ToColor(pressed.Text));
    }

    /// <summary>
    /// 把一组按钮样式值翻成样式盒。四边同色时就是 <see cref="StyleBoxFlat"/>（各边线宽照搬）；
    /// 底边与其余三边不同色（选中的可切换按钮）时 <see cref="StyleBoxFlat"/> 表达不了，用 <see cref="EdgeStripBox"/>。
    /// </summary>
    private static StyleBox ButtonBox(UiButtonStyle style)
    {
        if (style.Left.Color != style.Top.Color || style.Right.Color != style.Top.Color)
        {
            throw new ArgumentException("按钮样式只支持左、上、右三边同色。", nameof(style));
        }

        return style.Bottom.Color == style.Top.Color ? FlatButtonBox(style) : new EdgeStripBox(style);
    }

    /// <summary>四边同色的按钮样式盒。</summary>
    internal static StyleBoxFlat FlatButtonBox(UiButtonStyle style)
    {
        var box = new StyleBoxFlat
        {
            BgColor = Visuals.ToColor(style.Fill),
            DrawCenter = style.DrawFill,
            BorderColor = Visuals.ToColor(style.Top.Color),
            BorderWidthLeft = style.Left.WidthPx,
            BorderWidthTop = style.Top.WidthPx,
            BorderWidthRight = style.Right.WidthPx,
            BorderWidthBottom = style.Bottom.WidthPx,
        };
        box.SetCornerRadiusAll(style.CornerRadiusPx);
        SetMargins(box, style.PaddingXPx, UiTheme.ButtonPaddingYPx);
        return box;
    }

    /// <summary>某一级面板、某一档内边距的样式盒。</summary>
    private static StyleBoxFlat PanelBox(PanelTier tier, int padding)
    {
        UiPanelStyle style = UiTheme.PanelStyleOf(tier);
        var box = new StyleBoxFlat
        {
            BgColor = Visuals.ToColor(style.Fill),
            BorderColor = Visuals.ToColor(style.Border),
        };
        box.SetBorderWidthAll(style.BorderWidthPx);
        box.SetCornerRadiusAll(style.CornerRadiusPx);
        SetMargins(box, padding, padding);
        return box;
    }

    /// <summary>提示条文字的样式盒：提示条一级的底色与圆角，内边距取提示条的左右 / 上下两档。</summary>
    private static StyleBoxFlat HintLabelBox()
    {
        UiPanelStyle style = UiTheme.PanelStyleOf(PanelTier.Hint);
        var box = new StyleBoxFlat
        {
            BgColor = Visuals.ToColor(style.Fill),
            BorderColor = Visuals.ToColor(style.Border),
        };
        box.SetBorderWidthAll(style.BorderWidthPx);
        box.SetCornerRadiusAll(style.CornerRadiusPx);
        SetMargins(box, UiTheme.HintPaddingXPx, UiTheme.HintPaddingYPx);
        return box;
    }

    private static void SetMargins(StyleBox box, int x, int y)
    {
        box.ContentMarginLeft = x;
        box.ContentMarginRight = x;
        box.ContentMarginTop = y;
        box.ContentMarginBottom = y;
    }

    private static void RequireSpace(int value, string name)
    {
        if (!UiTheme.SpaceLadder.Contains(value))
        {
            throw new ArgumentOutOfRangeException(name, value, "间距与内边距只取间距阶梯里的值（UiTheme.Space0–Space5）。");
        }
    }

    /// <summary>
    /// 一个带边框的面板容器，内含一个纵向列表。<paramref name="padding"/> 与 <paramref name="separation"/> 只接受间距阶梯的值（传 <c>UiTheme.SpaceN</c>），
    /// 不在阶梯上即抛异常。样式由主题按"级别 + 内边距"的变体给出，面板自己不带样式盒。
    /// </summary>
    public static (PanelContainer Panel, VBoxContainer Body) Panel(PanelTier tier, int padding, int separation)
    {
        RequireSpace(separation, nameof(separation));
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, ThemeTypeVariation = PanelVariationOf(tier, padding) };
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", separation);
        panel.AddChild(body);
        return (panel, body);
    }

    /// <summary>一行文字。<paramref name="size"/> 传字号阶梯里的名字（<c>UiTheme.Caption</c> 等）；结算演出的放大字号由呈现层给出。</summary>
    public static Label Text(string text, Color? color = null, int? size = null, bool wrap = false)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeColorOverride("font_color", color ?? InfoText);
        label.AddThemeFontSizeOverride("font_size", size ?? UiTheme.Body);
        if (wrap)
        {
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        }

        return label;
    }

    /// <summary>标题行（注释字号 + 金色）。</summary>
    public static Label Heading(string text) => Text(text, PanelBorder, UiTheme.Caption);

    /// <summary>压在棋盘上的一行提示（提示条一级的底、描边，横幅字号）。如回合摘要。</summary>
    public static Label HintBanner(string text, Color color)
    {
        Label label = Text(text, color, UiTheme.Banner);
        label.ThemeTypeVariation = HintLabelVariation;
        return label;
    }

    /// <summary>结算演出的中央横幅（描边、演出字号 <see cref="UiTheme.ShowBannerPx"/>，没有底）。</summary>
    public static Label ShowBanner(string text, Color color)
    {
        Label label = Text(text, color, UiTheme.ShowBannerPx);
        label.ThemeTypeVariation = ShowBannerVariation;
        return label;
    }

    /// <summary>
    /// 标准按钮。高度固定为 <see cref="UiTheme.ButtonHeightPx"/>，远低于
    /// <see cref="UiTheme.MobileStyleButtonHeightPx"/>——默认 UI 不出现手游式大按钮（5.5）。语义（主操作 / 危险）由 <paramref name="kind"/> 给出，样式与字色都来自主题。
    /// </summary>
    public static Button Action(string text, ButtonKind kind = ButtonKind.Default, int minWidth = 0) =>
        new() { Text = text, CustomMinimumSize = new Vector2(minWidth, UiTheme.ButtonHeightPx), ThemeTypeVariation = ButtonTypeOf(kind) };

    /// <summary>可切换状态的按钮（信息层按钮等）：选中时指到"选中"变体（金色文字 + 金色宽底边）。</summary>
    public static Button Toggle(string text, bool active, int minWidth = 0)
    {
        Button button = Action(text, ButtonKind.Default, minWidth);
        Select(button, active);
        return button;
    }

    /// <summary>改一个（默认语义的）可切换按钮的选中状态：只换主题变体，不逐处改字色。</summary>
    public static void Select(Button button, bool selected)
    {
        ArgumentNullException.ThrowIfNull(button);
        button.ThemeTypeVariation = selected ? SelectedButtonVariation : ButtonType;
    }

    /// <summary>
    /// 按钮里的子控件要用的字色（hud-panels D4）：子 <c>Label</c> 不跟按钮的主题字色走，按"语义 + 是否选中"取按钮常态的字色，
    /// 与按钮自己的文字同一份取值（选中 → 金色、危险 → 警示色、否则正文色）。
    /// </summary>
    public static Color ButtonTextColor(ButtonKind kind, bool selected) =>
        Visuals.ToColor(UiTheme.ButtonStyleOf(kind, ButtonState.Normal, selected).Text);

    /// <summary>
    /// 把一组子控件铺进按钮（hud-panels D3 / D4）：按钮文字置空，<paramref name="content"/> 铺满按钮、左右各让出按钮内边距
    /// （<see cref="UiTheme.ButtonPaddingXPx"/>），并且不接鼠标——点击仍落在按钮上。<paramref name="content"/> 之下后加的子控件须自己设 <c>MouseFilter = Ignore</c>。
    /// </summary>
    public static void FillButton(Button button, Control content)
    {
        ArgumentNullException.ThrowIfNull(button);
        ArgumentNullException.ThrowIfNull(content);
        button.Text = string.Empty;
        content.MouseFilter = Control.MouseFilterEnum.Ignore;
        content.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        content.OffsetLeft = UiTheme.ButtonPaddingXPx;
        content.OffsetRight = -UiTheme.ButtonPaddingXPx;
        content.OffsetTop = 0f;
        content.OffsetBottom = 0f;
        button.AddChild(content);
    }

    /// <summary>
    /// 一块实色条（不接鼠标、纵向居中）：自家标记竖条、当前行动者的底边（hud-panels D2 / D3）。宽或高传 0 即随容器撑开。
    /// </summary>
    public static ColorRect Bar(Color color, float width, float height) => new()
    {
        Color = color,
        CustomMinimumSize = new Vector2(width, height),
        MouseFilter = Control.MouseFilterEnum.Ignore,
        SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
    };

    /// <summary>与 <see cref="Bar"/> 同尺寸的空占位（不画任何东西、不接鼠标），让列与行在没有标记时仍对齐。</summary>
    public static Control Spacer(float width, float height) => new()
    {
        CustomMinimumSize = new Vector2(width, height),
        MouseFilter = Control.MouseFilterEnum.Ignore,
        SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
    };

    /// <summary>横向分隔（线色与上下留白来自主题）。</summary>
    public static HSeparator Separator() => new();

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

/// <summary>
/// 底边与其余三边不同色的样式盒（hud-theme D8-1：选中的可切换按钮，三边中性色 1 像素、底边金色 2 像素）。
/// <see cref="StyleBoxFlat"/> 只有一个边框色，这里先用一个三边样式的 <see cref="StyleBoxFlat"/> 画底与左、上、右三边，再在底部叠一条底边色的条。
/// 取值全部来自传入的 <see cref="UiButtonStyle"/>，本类不做判断。
/// </summary>
public sealed partial class EdgeStripBox : StyleBox
{
    private readonly StyleBoxFlat _body;
    private readonly StyleBoxFlat _strip;
    private readonly int _stripPx;

    public EdgeStripBox(UiButtonStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        _body = Ui.FlatButtonBox(style with { Bottom = new UiEdge(style.Top.Color, 0) });
        _strip = new StyleBoxFlat { BgColor = Visuals.ToColor(style.Bottom.Color) };
        _strip.CornerRadiusBottomLeft = style.CornerRadiusPx;
        _strip.CornerRadiusBottomRight = style.CornerRadiusPx;
        _stripPx = style.Bottom.WidthPx;
        ContentMarginLeft = _body.ContentMarginLeft;
        ContentMarginRight = _body.ContentMarginRight;
        ContentMarginTop = _body.ContentMarginTop;
        ContentMarginBottom = _body.ContentMarginBottom;
    }

    /// <inheritdoc/>
    public override void _Draw(Rid toCanvasItem, Rect2 rect)
    {
        _body.Draw(toCanvasItem, rect);
        _strip.Draw(toCanvasItem, new Rect2(rect.Position.X, rect.End.Y - _stripPx, rect.Size.X, _stripPx));
    }
}
