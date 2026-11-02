using System;
using System.Collections.Generic;
using Godot;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Presentation.MapSelect;
using Siege.Presentation.Style;
using Siege.Presentation.Text;

namespace Siege.Godot;

/// <summary>
/// HUD 的新局面板（board-link-mode D8 / 实现裁决 D7，规格 map-selection「开局选图界面」）：当前地图种子与完整标识 <c>link:&lt;种子&gt;:&lt;人数&gt;</c>、种子输入框、"换一张"、
/// 人数（map-expand-25 D1：2 / 3 / 4，缺省 4）、AI 难度（expert-lookahead D10）、"开始"。旧地图清单、平台数 / 棋盘数调节已删除——整个游戏只剩链接模式一种地图。
/// </summary>
/// <remarks>
/// 本类<b>不持有任何新局状态、不自带地图清单</b>：每一项文案与可用性都取自 <see cref="MapSelectModel"/>，操作只经事件转发给主场景。
/// 控件只搭一次、刷新时改属性（整块重搭会让输入框丢焦点与光标）。新局期间对局面板整体隐藏。
/// </remarks>
public sealed partial class Hud
{
    private readonly List<Button> _difficultyButtons = [];
    private readonly List<Button> _playerButtons = [];
    private readonly List<Button> _tierButtons = [];
    private Label _mapIntroText = null!;
    private Control? _selectRoot;
    private PanelContainer? _selectPanel;
    private AspectRatioContainer? _mapThumbnailFrame;
    private TextureRect? _mapThumbnail;
    private LineEdit _mapSeedInput = null!;
    private Button _mapSeedApply = null!;
    private Button _mapReroll = null!;
    private Label _mapIdText = null!;
    private Label _mapInfoText = null!;
    private Label _mapNotice = null!;

    /// <summary>在种子输入框里回车，或点"采用"：带上输入框原文。</summary>
    public event Action<string>? MapSeedSubmitted;

    /// <summary>点"换一张"。</summary>
    public event Action? MapRerollPressed;

    /// <summary>点选人数（2 / 3 / 4 之一）。</summary>
    public event Action<int>? MapPlayersPicked;
    /// <summary>点选尺寸（小 / 大，big-map 裁决 11）。</summary>
    public event Action<MapTier>? MapTierPicked;

    /// <summary>点选 AI 难度（四档之一）。</summary>
    public event Action<AiDifficulty>? MapDifficultyPicked;

    /// <summary>点"开始"。</summary>
    public event Action? MapStartPressed;

    /// <summary>新局面板此刻是否显示。</summary>
    public bool MapSelectOpen => _selectRoot is { Visible: true };

    /// <summary>新局面板的大图整块大陆缩略此刻是否显示（自检探针，large-default 裁决 4：只在选大图时显示；面板撤掉后为 false）。</summary>
    public bool MapThumbnailVisible => _mapThumbnailFrame is { Visible: true };

    /// <summary>新局面板在屏幕上的矩形（截图自检打印用：核对面板靠左、没有盖到地图主体）。</summary>
    public Rect2 MapSelectRect => _selectPanel?.GetGlobalRect() ?? default;

    /// <summary>
    /// 显示 / 刷新新局面板，并隐藏对局面板。<paramref name="mapInfo"/> 是当前预览地图的一行说明（尺寸、出生棋盘数等，由主场景读棋盘视图模型给出）。
    /// <paramref name="thumbnail"/> 是大图整块大陆的像素缩略（large-default 裁决 4，由主场景经 <see cref="Minimap.BuildTerrain"/> 给出，放大居中盖在局部 3D 预览之上）；小图传 null，缩略隐藏、画面与改前相同。
    /// </summary>
    public void ShowMapSelect(MapSelectModel model, string mapInfo, Texture2D? thumbnail)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (_selectRoot is null)
        {
            BuildMapSelect();
        }

        _root.Visible = false;
        _selectRoot!.Visible = true;
        if (_mapThumbnail!.Texture != thumbnail)
        {
            _mapThumbnail.Texture = thumbnail;
        }

        _mapThumbnailFrame!.Visible = thumbnail is not null;
        if (thumbnail is not null && thumbnail.GetHeight() > 0)
        {
            _mapThumbnailFrame.Ratio = (float)thumbnail.GetWidth() / thumbnail.GetHeight();
        }

        if (_mapSeedInput.Text != model.SeedText)
        {
            _mapSeedInput.Text = model.SeedText;
            _mapSeedInput.CaretColumn = model.SeedText.Length;
        }

        _mapIdText.Text = model.CurrentId;
        _mapInfoText.Text = mapInfo;
        _mapNotice.Text = model.Notice;
        _mapNotice.Visible = model.Notice.Length > 0;

        for (int i = 0; i < _playerButtons.Count; i++)
        {
            int players = MapSelectModel.PlayerOptions[i];
            bool active = players == model.Players;
            Button button = _playerButtons[i];
            button.Text = (active ? "● " : "○ ") + $"{players} 人";
            Ui.Selected(button, active);
        }
        for (int i = 0; i < _tierButtons.Count; i++)
        {
            MapTier tier = MapSelectModel.TierOptions[i];
            bool active = tier == model.Tier;
            Button button = _tierButtons[i];
            button.Text = (active ? "● " : "○ ") + Labels.TierName(tier);
            Ui.Selected(button, active);
        }

        // 说明随尺寸切换：大图是全图自由插旗（缺省，large-default 裁决 1 / 3），小图（旧模式）是座位盲选；两句都经 Labels 取 Core 常量。
        _mapIntroText.Text = model.Tier == MapTier.Large ? Labels.LargeMapIntro : Labels.SmallMapIntro;

        for (int i = 0; i < _difficultyButtons.Count; i++)
        {
            bool active = MapSelectModel.DifficultyOptions[i] == model.Difficulty;
            Button button = _difficultyButtons[i];
            button.Text = (active ? "● " : "○ ") + Labels.Difficulty(MapSelectModel.DifficultyOptions[i]);
            Ui.Selected(button, active);
        }
    }

    /// <summary>确认开局之后：撤掉新局面板，恢复对局面板。</summary>
    public void HideMapSelect()
    {
        if (_selectRoot is not null)
        {
            // 本方法会从"开始"按钮自己的 Pressed 回调里走到：不在回调中途把它的祖先摘下树，只隐藏、帧末释放。
            _selectRoot.Visible = false;
            _selectRoot.QueueFree();
            _selectRoot = null;
            _selectPanel = null;
            _mapThumbnailFrame = null;
            _mapThumbnail = null;
            _difficultyButtons.Clear();
            _playerButtons.Clear();
            _tierButtons.Clear();
        }

        _root.Visible = true;
    }

    private void BuildMapSelect()
    {
        _selectRoot = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = Ui.BuildTheme() };
        _selectRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_selectRoot);

        // 大图整块大陆的缩略（large-default 裁决 4）：占面板右侧整片画幅，按位图宽高比取最大的居中矩形，底色与小地图同一份、像素图最近邻放大（每格一个像素，不糊）。
        // 只搭一次、刷新时换纹理与显隐（自检每帧比对游离节点数，不能重建）；不接鼠标、不做任何像素 ↔ 格换算。面板在它之后加入，盖在上层。
        _mapThumbnailFrame = new AspectRatioContainer
        {
            Name = "MapThumbnail",
            Ratio = 1f,
            StretchMode = AspectRatioContainer.StretchModeEnum.Fit,
            AlignmentHorizontal = AspectRatioContainer.AlignmentMode.Center,
            AlignmentVertical = AspectRatioContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _mapThumbnailFrame.AnchorLeft = 0f;
        _mapThumbnailFrame.AnchorTop = 0f;
        _mapThumbnailFrame.AnchorRight = 1f;
        _mapThumbnailFrame.AnchorBottom = 1f;
        _mapThumbnailFrame.OffsetLeft = 388f;
        _mapThumbnailFrame.OffsetTop = 14f;
        _mapThumbnailFrame.OffsetRight = -14f;
        _mapThumbnailFrame.OffsetBottom = -14f;
        _mapThumbnailFrame.AddChild(new ColorRect { Color = Minimap.Backdrop, MouseFilter = Control.MouseFilterEnum.Ignore });
        _mapThumbnail = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _mapThumbnailFrame.AddChild(_mapThumbnail);
        _selectRoot.AddChild(_mapThumbnailFrame);

        // 面板靠左：背景是当前地图各块出生平台的占位，地图主体在画面中央，不去挡它。
        (PanelContainer panel, VBoxContainer body) = Ui.Panel(12, 6);
        Ui.Anchor(panel, 0f, 0f, 14f, 14f, 374f, 14f);
        panel.GrowVertical = Control.GrowDirection.End;
        _selectRoot.AddChild(panel);
        _selectPanel = panel;

        body.AddChild(Ui.Text("开始新局", Ui.PanelBorder, UiTheme.BodyFontPx + 4));
        _mapIntroText = Ui.Text(string.Empty, Ui.MutedText, UiTheme.BodyFontPx - 2, wrap: true);
        body.AddChild(_mapIntroText);

        body.AddChild(Ui.Separator());
        body.AddChild(Ui.Heading("地图种子"));
        var seedRow = new HBoxContainer();
        _mapSeedInput = new LineEdit
        {
            MaxLength = 24,
            PlaceholderText = "非负整数",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, UiTheme.ButtonHeightPx),
        };
        _mapSeedInput.AddThemeFontSizeOverride("font_size", UiTheme.BodyFontPx);
        Ui.Field(_mapSeedInput);
        _mapSeedInput.TextSubmitted += text => MapSeedSubmitted?.Invoke(text);
        seedRow.AddChild(_mapSeedInput);
        _mapSeedApply = Ui.Action("采用", 56);
        _mapSeedApply.Pressed += () => MapSeedSubmitted?.Invoke(_mapSeedInput.Text);
        seedRow.AddChild(_mapSeedApply);
        _mapReroll = Ui.Action("换一张", 96);
        _mapReroll.Pressed += () => MapRerollPressed?.Invoke();
        seedRow.AddChild(_mapReroll);
        body.AddChild(seedRow);
        body.AddChild(Ui.Text("输入种子后回车（或点「采用」）；「换一张」随机取一个新种子。", Ui.MutedText, UiTheme.BodyFontPx - 2, wrap: true));

        body.AddChild(Ui.Separator());
        body.AddChild(Ui.Heading("完整地图标识"));
        _mapIdText = Ui.Text(string.Empty, Ui.InfoText, UiTheme.BodyFontPx + 2, wrap: true);
        body.AddChild(_mapIdText);
        _mapInfoText = Ui.Text(string.Empty, Ui.MutedText, UiTheme.BodyFontPx - 1, wrap: true);
        body.AddChild(_mapInfoText);
        body.AddChild(Ui.Text("命令行加 --map=<标识> 可直接重开这张图（标识含人数）。", Ui.MutedText, UiTheme.BodyFontPx - 2, wrap: true));
        _mapNotice = Ui.Text(string.Empty, Ui.DangerText, UiTheme.BodyFontPx - 1, wrap: true);
        body.AddChild(_mapNotice);

        // 人数（map-expand-25 D1）：2 / 3 / 4，缺省 4；浮空岛按人数生成，人数是地图标识的一部分，改人数即换一张图。
        body.AddChild(Ui.Separator());
        body.AddChild(Ui.Heading("人数"));
        var playersRow = new HBoxContainer();
        foreach (int players in MapSelectModel.PlayerOptions)
        {
            Button option = Ui.Action($"{players} 人", 78);
            option.Pressed += () => MapPlayersPicked?.Invoke(players);
            _playerButtons.Add(option);
            playersRow.AddChild(option);
        }

        body.AddChild(playersRow);
        body.AddChild(Ui.Text("你和 AI 一共几名玩家；岛的大小与平台块数随人数变。", Ui.MutedText, UiTheme.BodyFontPx - 2, wrap: true));
        // 尺寸（big-map 裁决 11 / large-default 裁决 1–2）：大 = 150×150 大陆、全图自由插旗（标识带 :l），小 = 浮空岛旧模式（三段标识）；缺省大，按钮顺序跟随 TierOptions（大在前）。
        body.AddChild(Ui.Separator());
        body.AddChild(Ui.Heading("尺寸"));
        var tierRow = new HBoxContainer();
        foreach (MapTier tier in MapSelectModel.TierOptions)
        {
            Button option = Ui.Action(Labels.TierName(tier), 118);
            option.Pressed += () => MapTierPicked?.Invoke(tier);
            _tierButtons.Add(option);
            tierRow.AddChild(option);
        }

        body.AddChild(tierRow);
        body.AddChild(Ui.Text(Labels.TierHint, Ui.MutedText, UiTheme.BodyFontPx - 2, wrap: true));

        // AI 难度（expert-lookahead D10）：四档，缺省标准（命令行 --difficulty= 预选）。
        body.AddChild(Ui.Separator());
        body.AddChild(Ui.Heading("AI 难度"));
        var difficultyRow = new HBoxContainer();
        foreach (AiDifficulty difficulty in MapSelectModel.DifficultyOptions)
        {
            Button option = Ui.Action(Labels.Difficulty(difficulty), 78);
            option.Pressed += () => MapDifficultyPicked?.Invoke(difficulty);
            _difficultyButtons.Add(option);
            difficultyRow.AddChild(option);
        }

        body.AddChild(difficultyRow);
        body.AddChild(Ui.Text($"命令行加 --difficulty=<{AiDifficultyNames.Usage}> 可预选难度。", Ui.MutedText, UiTheme.BodyFontPx - 2, wrap: true));

        body.AddChild(Ui.Separator());
        Button start = Ui.Action("开始");
        Ui.Primary(start);
        start.Pressed += () => MapStartPressed?.Invoke();
        body.AddChild(start);
    }
}
