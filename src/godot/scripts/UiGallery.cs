using System;
using System.IO;
using Godot;
using Siege.Presentation.Style;

namespace Siege.Godot;

/// <summary>
/// HUD 样式总览页（hud-theme D6，visual-style-baseline「样式总览页」）：<c>--ui-gallery=&lt;PNG 路径&gt;</c> 时不建局，
/// 摆一页三级面板、三种语义 × 五种状态的按钮样例、一对选中 / 未选中的可切换按钮、五级字号，截图后退出。
/// </summary>
/// <remarks>
/// 悬停、按下、焦点不能靠模拟鼠标得到：样例按（类型或变体，状态名）从根节点的主题里取出样式盒与字色画出来（<see cref="Theme.GetStylebox"/> / <see cref="Theme.GetColor"/>），
/// 与真按钮用的是同一个对象，不另写取值。焦点照引擎按钮的画法叠在常态之上。常态、禁用两列旁边另放一个真按钮作对照。
/// 截图失败、文件没生成或中途抛异常都以退出码 1 结束；无头模式没有画面可截，同样算失败。
/// </remarks>
public sealed partial class UiGallery : CanvasLayer
{
    private static readonly ButtonState[] States = [ButtonState.Normal, ButtonState.Hover, ButtonState.Pressed, ButtonState.Disabled, ButtonState.Focus];
    private static readonly ButtonKind[] Kinds = [ButtonKind.Default, ButtonKind.Primary, ButtonKind.Danger];

    private const int SampleWidth = 120;
    private readonly string _path;
    private Theme _theme = null!;
    private int _frame;
    private bool _pending;

    public UiGallery(string path)
    {
        _path = path;
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        _theme = Ui.BuildTheme();
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = _theme };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        (PanelContainer page, VBoxContainer body) = Ui.Panel(PanelTier.Primary, UiTheme.Space5, UiTheme.Space5);
        Ui.Anchor(page, 0f, 0f, 24f, 24f, 24f, 24f);
        root.AddChild(page);

        body.AddChild(Ui.Text("HUD 样式总览（--ui-gallery）", Ui.PanelBorder, UiTheme.Banner));
        body.AddChild(Ui.Text("样例取自对局界面同一份主题：悬停 / 按下 / 焦点按状态名从主题取样式盒画出，常态与禁用另放真按钮对照。", Ui.MutedText, UiTheme.Caption));

        body.AddChild(Section("面板三级"));
        body.AddChild(Panels());

        body.AddChild(Section("按钮：三种语义 × 五种状态"));
        body.AddChild(ButtonGrid());

        body.AddChild(Section("可切换按钮 · 输入框 · 下拉框"));
        body.AddChild(Toggles());

        body.AddChild(Section("字号阶梯"));
        body.AddChild(FontLadder());

        GD.Print($"[ui-gallery] 面板 {Enum.GetValues<PanelTier>().Length} 级，按钮样例 {Kinds.Length * States.Length} 个（另有真按钮 {Kinds.Length * 2} 个），字号 {UiTheme.FontLadder.Length} 级");
    }

    private static Label Section(string title) => Ui.Text(title, Ui.PanelBorder, UiTheme.Title);

    private static HBoxContainer Panels()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.Space5);
        (PanelTier Tier, string Name, string Text)[] tiers =
        [
            (PanelTier.Primary, "主面板", "玩家读主信息、做决定的面板"),
            (PanelTier.Secondary, "次面板", "一排按钮或一段说明"),
            (PanelTier.Hint, "提示条", "临时压在棋盘上的一行字"),
        ];
        foreach ((PanelTier tier, string name, string text) in tiers)
        {
            (PanelContainer panel, VBoxContainer body) = Ui.Panel(tier, UiTheme.Space4, UiTheme.Space1);
            panel.CustomMinimumSize = new Vector2(240f, 0f);
            body.AddChild(Ui.Heading(name));
            body.AddChild(Ui.Text(text));
            row.AddChild(panel);
        }

        var hint = new VBoxContainer();
        hint.AddChild(Ui.Text("回合摘要横幅（提示条文字）", Ui.MutedText, UiTheme.Caption));
        hint.AddChild(Ui.HintBanner("铁方落 3 子，提 2 子", Ui.PanelBorder));
        row.AddChild(hint);
        return row;
    }

    private GridContainer ButtonGrid()
    {
        var grid = new GridContainer { Columns = 1 + States.Length + 2 };
        grid.AddThemeConstantOverride("h_separation", UiTheme.Space4);
        grid.AddThemeConstantOverride("v_separation", UiTheme.Space3);

        grid.AddChild(Ui.Text(string.Empty));
        foreach (ButtonState state in States)
        {
            grid.AddChild(Ui.Text(StateName(state), Ui.MutedText, UiTheme.Caption));
        }

        grid.AddChild(Ui.Text("真按钮·常态", Ui.MutedText, UiTheme.Caption));
        grid.AddChild(Ui.Text("真按钮·禁用", Ui.MutedText, UiTheme.Caption));

        foreach (ButtonKind kind in Kinds)
        {
            grid.AddChild(Ui.Text(KindName(kind), Ui.InfoText, UiTheme.Small));
            string type = Ui.ButtonTypeOf(kind);
            foreach (ButtonState state in States)
            {
                grid.AddChild(Sample(type, state, KindName(kind)));
            }

            grid.AddChild(Ui.Action(KindName(kind), kind, SampleWidth));
            Button disabled = Ui.Action(KindName(kind), kind, SampleWidth);
            disabled.Disabled = true;
            grid.AddChild(disabled);
        }

        return grid;
    }

    /// <summary>一个按钮样例：样式盒与字色按（类型，状态名）从主题取；焦点叠在常态之上（与引擎按钮的画法相同）。</summary>
    private Control Sample(string type, ButtonState state, string text)
    {
        StyleBox[] boxes = state == ButtonState.Focus
            ? [_theme.GetStylebox(Ui.StyleboxNameOf(ButtonState.Normal), type), _theme.GetStylebox(Ui.StyleboxNameOf(ButtonState.Focus), type)]
            : [_theme.GetStylebox(Ui.StyleboxNameOf(state), type)];
        var sample = new StyleSample(boxes)
        {
            CustomMinimumSize = new Vector2(SampleWidth, UiTheme.ButtonHeightPx),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        Label label = Ui.Text(text, _theme.GetColor(Ui.FontColorNameOf(state), type), _theme.GetFontSize(Ui.FontSizeItem, Ui.ButtonType));
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        sample.AddChild(label);
        return sample;
    }

    private static HBoxContainer Toggles()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.Space4);
        row.AddChild(Ui.Toggle("● 已选中", true, SampleWidth));
        row.AddChild(Ui.Toggle("○ 未选中", false, SampleWidth));
        row.AddChild(new LineEdit { Text = "12345", CustomMinimumSize = new Vector2(SampleWidth, UiTheme.ButtonHeightPx) });
        row.AddChild(new LineEdit { Text = "只读", Editable = false, CustomMinimumSize = new Vector2(SampleWidth, UiTheme.ButtonHeightPx) });
        var options = new OptionButton { CustomMinimumSize = new Vector2(SampleWidth, UiTheme.ButtonHeightPx) };
        options.AddItem("普通子");
        options.AddItem("堡垒子");
        row.AddChild(options);
        return row;
    }

    private static VBoxContainer FontLadder()
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", UiTheme.Space1);
        (string Name, int Size)[] ladder =
        [
            ("注释 Caption", UiTheme.Caption),
            ("按钮与次要正文 Small", UiTheme.Small),
            ("正文 Body", UiTheme.Body),
            ("标题 Title", UiTheme.Title),
            ("横幅 Banner", UiTheme.Banner),
        ];
        foreach ((string name, int size) in ladder)
        {
            column.AddChild(Ui.Text($"{size} px　{name}　第 3 大回合 · 势力 1,024", Ui.InfoText, size));
        }

        return column;
    }

    private static string StateName(ButtonState state) => state switch
    {
        ButtonState.Normal => "常态",
        ButtonState.Hover => "悬停",
        ButtonState.Pressed => "按下",
        ButtonState.Disabled => "禁用",
        ButtonState.Focus => "焦点",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "未知按钮状态。"),
    };

    private static string KindName(ButtonKind kind) => kind switch
    {
        ButtonKind.Default => "默认",
        ButtonKind.Primary => "主操作",
        ButtonKind.Danger => "危险操作",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知按钮语义。"),
    };

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (_pending || ++_frame < 5)
        {
            return;
        }

        _pending = true;
        if (DisplayServer.GetName() == "headless")
        {
            GD.PrintErr("[ui-gallery] 无头模式没有可截取的画面：不要加 --headless。");
            GetTree().Quit(1);
            return;
        }

        CaptureWhenDrawn();
    }

    private async void CaptureWhenDrawn()
    {
        try
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Image image = GetViewport().GetTexture().GetImage();
            Error error = image.SavePng(_path);
            bool written = File.Exists(_path) && new FileInfo(_path).Length > 0;
            GD.Print($"[ui-gallery] 截图 {_path}：{error}，文件{(written ? "已生成" : "未生成")}（{image.GetWidth()}×{image.GetHeight()}，第 {_frame} 帧）");
            GetTree().Quit(error == Error.Ok && written ? 0 : 1);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ui-gallery] 截图失败：{ex}");
            GetTree().Quit(1);
        }
    }

    /// <summary>按次序画一组样式盒的控件（样例用；样式盒本身取自主题）。</summary>
    private sealed partial class StyleSample : Control
    {
        private readonly StyleBox[] _boxes;

        public StyleSample(StyleBox[] boxes)
        {
            _boxes = boxes;
        }

        /// <inheritdoc/>
        public override void _Draw()
        {
            foreach (StyleBox box in _boxes)
            {
                DrawStyleBox(box, new Rect2(Vector2.Zero, Size));
            }
        }
    }
}
