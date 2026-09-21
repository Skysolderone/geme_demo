using System;
using System.Collections.Generic;
using Godot;
using Siege.Presentation.MapSelect;
using Siege.Presentation.Style;

namespace Siege.Godot;

/// <summary>
/// HUD 的开局选图面板（map-generator D7 / D8）：清单、种子输入框、"换一张"、平台数 − / +、完整标识、"开始"。
/// </summary>
/// <remarks>
/// 本类<b>不持有任何选图状态、不自带地图清单</b>：每一项文案与可用性都取自 <see cref="MapSelectModel"/>，操作只经事件转发给主场景。
/// 控件只搭一次、刷新时改属性（整块重搭会让输入框丢焦点与光标）。选图期间对局面板整体隐藏。
/// </remarks>
public sealed partial class Hud
{
    private readonly List<Button> _mapOptionButtons = [];
    private Control? _selectRoot;
    private PanelContainer? _selectPanel;
    private LineEdit _mapSeedInput = null!;
    private Button _mapSeedApply = null!;
    private Button _mapReroll = null!;
    private Button _mapFewer = null!;
    private Button _mapMore = null!;
    private Label _mapPlatforms = null!;
    private Label _mapIdText = null!;
    private Label _mapInfoText = null!;
    private Label _mapNotice = null!;

    /// <summary>点选清单里的第几项。</summary>
    public event Action<int>? MapOptionPicked;

    /// <summary>在种子输入框里回车，或点"生成"：带上输入框原文。</summary>
    public event Action<string>? MapSeedSubmitted;

    /// <summary>点"换一张"。</summary>
    public event Action? MapRerollPressed;

    /// <summary>平台数 −1 / +1。</summary>
    public event Action<int>? MapPlatformsAdjusted;

    /// <summary>点"开始"。</summary>
    public event Action? MapStartPressed;

    /// <summary>选图面板此刻是否显示。</summary>
    public bool MapSelectOpen => _selectRoot is { Visible: true };

    /// <summary>选图面板在屏幕上的矩形（截图自检打印用：核对面板靠左、没有盖到地图主体）。</summary>
    public Rect2 MapSelectRect => _selectPanel?.GetGlobalRect() ?? default;

    /// <summary>
    /// 显示 / 刷新选图面板，并隐藏对局面板。<paramref name="mapInfo"/> 是当前预览地图的一行说明（尺寸与出生区数，由主场景读棋盘视图模型给出）。
    /// </summary>
    public void ShowMapSelect(MapSelectModel model, string mapInfo)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (_selectRoot is null)
        {
            BuildMapSelect(model);
        }

        _root.Visible = false;
        _selectRoot!.Visible = true;

        for (int i = 0; i < _mapOptionButtons.Count; i++)
        {
            bool active = i == model.SelectedIndex;
            Button button = _mapOptionButtons[i];
            button.Text = (active ? "● " : "○ ") + model.Options[i].Title;
            button.AddThemeColorOverride("font_color", active ? Ui.PanelBorder : Ui.InfoText);
        }

        bool random = model.IsRandomSelected;
        if (_mapSeedInput.Text != model.SeedText)
        {
            _mapSeedInput.Text = model.SeedText;
            _mapSeedInput.CaretColumn = model.SeedText.Length;
        }

        _mapSeedInput.Editable = random;
        _mapSeedApply.Disabled = !random;
        _mapReroll.Disabled = !random;
        _mapFewer.Disabled = !model.CanDecreasePlatforms;
        _mapMore.Disabled = !model.CanIncreasePlatforms;
        _mapPlatforms.Text = model.PlatformCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _mapIdText.Text = model.CurrentId;
        _mapInfoText.Text = mapInfo;
        _mapNotice.Text = model.Notice;
        _mapNotice.Visible = model.Notice.Length > 0;
    }

    /// <summary>确认开局之后：撤掉选图面板，恢复对局面板。</summary>
    public void HideMapSelect()
    {
        if (_selectRoot is not null)
        {
            // 本方法会从"开始"按钮自己的 Pressed 回调里走到：不在回调中途把它的祖先摘下树，只隐藏、帧末释放。
            _selectRoot.Visible = false;
            _selectRoot.QueueFree();
            _selectRoot = null;
            _selectPanel = null;
            _mapOptionButtons.Clear();
        }

        _root.Visible = true;
    }

    private void BuildMapSelect(MapSelectModel model)
    {
        _selectRoot = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = Ui.BuildTheme() };
        _selectRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_selectRoot);

        // 面板靠左：背景是当前地图的全局预览，地图主体在画面中央，不去挡它。
        (PanelContainer panel, VBoxContainer body) = Ui.Panel(12, 6);
        Ui.Anchor(panel, 0f, 0f, 14f, 14f, 374f, 14f);
        panel.GrowVertical = Control.GrowDirection.End;
        _selectRoot.AddChild(panel);
        _selectPanel = panel;

        body.AddChild(Ui.Text("选择地图", Ui.PanelBorder, UiTheme.BodyFontPx + 4));
        for (int i = 0; i < model.Options.Count; i++)
        {
            int index = i;
            Button option = Ui.Action(model.Options[i].Title);
            option.Alignment = HorizontalAlignment.Left;
            option.Pressed += () => MapOptionPicked?.Invoke(index);
            _mapOptionButtons.Add(option);
            body.AddChild(option);
        }

        body.AddChild(Ui.Separator());
        body.AddChild(Ui.Heading("随机图（选中「随机图」后可调）"));

        var seedRow = new HBoxContainer();
        seedRow.AddChild(Ui.Text("地图种子"));
        _mapSeedInput = new LineEdit
        {
            MaxLength = 24,
            PlaceholderText = "非负整数",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, UiTheme.ButtonHeightPx),
        };
        _mapSeedInput.AddThemeFontSizeOverride("font_size", UiTheme.BodyFontPx);
        _mapSeedInput.TextSubmitted += text => MapSeedSubmitted?.Invoke(text);
        seedRow.AddChild(_mapSeedInput);
        _mapSeedApply = Ui.Action("生成", 56);
        _mapSeedApply.Pressed += () => MapSeedSubmitted?.Invoke(_mapSeedInput.Text);
        seedRow.AddChild(_mapSeedApply);
        body.AddChild(seedRow);
        body.AddChild(Ui.Text("输入种子后回车（或点「生成」）。", Ui.MutedText, UiTheme.BodyFontPx - 2));

        var tuneRow = new HBoxContainer();
        _mapReroll = Ui.Action("换一张", 96);
        _mapReroll.Pressed += () => MapRerollPressed?.Invoke();
        tuneRow.AddChild(_mapReroll);
        tuneRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        tuneRow.AddChild(Ui.Text("平台数"));
        _mapFewer = Ui.Action("−", 34);
        _mapFewer.Pressed += () => MapPlatformsAdjusted?.Invoke(-1);
        tuneRow.AddChild(_mapFewer);
        _mapPlatforms = Ui.Text(string.Empty, Ui.PanelBorder, UiTheme.BodyFontPx + 2);
        _mapPlatforms.HorizontalAlignment = HorizontalAlignment.Center;
        _mapPlatforms.CustomMinimumSize = new Vector2(26f, 0f);
        tuneRow.AddChild(_mapPlatforms);
        _mapMore = Ui.Action("+", 34);
        _mapMore.Pressed += () => MapPlatformsAdjusted?.Invoke(+1);
        tuneRow.AddChild(_mapMore);
        body.AddChild(tuneRow);

        body.AddChild(Ui.Separator());
        body.AddChild(Ui.Heading("完整地图标识"));
        _mapIdText = Ui.Text(string.Empty, Ui.InfoText, UiTheme.BodyFontPx + 2, wrap: true);
        body.AddChild(_mapIdText);
        _mapInfoText = Ui.Text(string.Empty, Ui.MutedText, UiTheme.BodyFontPx - 1, wrap: true);
        body.AddChild(_mapInfoText);
        body.AddChild(Ui.Text("命令行加 --map=<标识> 可直接重开这张图。", Ui.MutedText, UiTheme.BodyFontPx - 2, wrap: true));
        _mapNotice = Ui.Text(string.Empty, Ui.DangerText, UiTheme.BodyFontPx - 1, wrap: true);
        body.AddChild(_mapNotice);

        body.AddChild(Ui.Separator());
        Button start = Ui.Action("开始");
        start.AddThemeColorOverride("font_color", Ui.PanelBorder);
        start.Pressed += () => MapStartPressed?.Invoke();
        body.AddChild(start);
    }
}
