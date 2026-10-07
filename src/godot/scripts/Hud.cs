using System;
using System.Collections.Immutable;
using System.Linq;
using Godot;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Core.Scoring;
using Siege.Presentation.Hand;
using Siege.Presentation.Hud;
using Siege.Presentation.Layers;
using Siege.Presentation.Preview;
using Siege.Presentation.Show;
using Siege.Presentation.Style;
using Siege.Presentation.Text;
using Siege.Presentation.Visibility;

namespace Siege.Godot;

/// <summary>
/// HUD（tactical-ui 裁决 6 的布局）：左上回合旗标、顶部中央行动顺序条、右上排名、左下手牌、
/// 底部中央五个信息层按钮、右下确认与 Pass。
/// </summary>
/// <remarks>
/// 本类<b>不计算任何东西</b>：每一处数值与文案都取自 <see cref="ViewerWorld"/> 的呈现模型
/// （<see cref="PreviewPresentation"/>、<see cref="LayerContent"/>、<see cref="HandInfoPanelView"/>）。
/// </remarks>
public sealed partial class Hud : CanvasLayer
{
    private Control _root = null!;
    private EmblemIcon _turnEmblem = null!;
    private Label _turnTitle = null!;
    private Label _turnSubtitle = null!;
    private Button _orderButton = null!;
    private HBoxContainer _orderContent = null!;
    private VBoxContainer _rankBody = null!;
    private PanelContainer _rankPanel = null!;
    private VBoxContainer _handBody = null!;
    private VBoxContainer _actionBody = null!;
    private VBoxContainer _previewBody = null!;
    private PanelContainer _previewPanel = null!;
    private VBoxContainer _layerBody = null!;
    private PanelContainer _layerPanel = null!;
    private HBoxContainer _layerButtons = null!;
    private VBoxContainer _centerBody = null!;
    private PanelContainer _centerPanel = null!;

    /// <summary>中央面板此刻是插旗提示（高度由内容撑开）：延迟的 <see cref="FitCenterHeight"/> 只在它为真时生效。</summary>
    private bool _centerFitsContent;
    private PanelContainer _recruitBar = null!;
    private Button _recruitBarButton = null!;
    private Label _notice = null!;
    private Label _turnSummary = null!;
    private Label _showCaption = null!;
    private Label _showBanner = null!;
    private Label _hoverReadout = null!;

    /// <summary>点选手牌类型（决定下一次点格子放什么）。</summary>
    public event Action<PieceType>? HandTypeSelected;

    /// <summary>整类弃牌（手牌类型超出槽位时）。</summary>
    public event Action<PieceType>? DiscardRequested;

    /// <summary>征募：选取候选。</summary>
    public event Action<int>? RecruitPicked;

    /// <summary>征募完成，进入部署。</summary>
    public event Action? RecruitFinished;

    /// <summary>征募面板的"收起"按钮或顶部提示条被点（与 V 键同一入口，recruit-panel-collapse）。</summary>
    public event Action? RecruitCollapsePressed;

    /// <summary>确认批次。</summary>
    public event Action? ConfirmPressed;

    /// <summary>Pass。</summary>
    public event Action? PassPressed;

    /// <summary>清空暂放。</summary>
    public event Action? ClearPressed;

    /// <summary>点信息层按钮。</summary>
    public event Action<TacticalLayer>? LayerPressed;

    /// <summary>开关手牌信息面板。</summary>
    public event Action? HandPanelPressed;

    /// <summary>切换「按住显示 / 点击切换」。</summary>
    public event Action? LayerModePressed;

    /// <summary>盘面层的读法切换（Tab 或按钮）。</summary>
    public event Action? ReadingPressed;

    /// <summary>搭出全部面板。只调用一次。</summary>
    public void Build()
    {
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = Ui.BuildTheme() };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        BuildTurnBanner();
        BuildOrderBar();
        BuildRankPanel();
        BuildHandBar();
        BuildLayerButtons();
        BuildActionPanel();
        BuildPreviewPanel();
        BuildLayerPanel();
        BuildCenterPanel();
        BuildRecruitBar();

        _notice = Ui.Text(string.Empty, Ui.MutedText);
        _notice.HorizontalAlignment = HorizontalAlignment.Center;
        // 通知条放在顶部顺序条之下：13×13 + 60° 相机下棋盘近边的行 / 列标注已经贴到底部 HUD 上沿，放底部会压住近边字母。
        Ui.Anchor(_notice, 0.5f, 0f, -420f, 66f, 420f, 90f);
        _root.AddChild(_notice);

        // 对手回合摘要：占通知行的位置，字大一号、带描边（压在棋盘上也读得清）。
        // 提示条一级的深色半透明底：摘要压在棋盘与列标上也读得清；宽度随文案（外面套一个居中容器）。
        _turnSummary = Ui.HintBanner(string.Empty, Ui.PanelBorder);
        _turnSummary.HorizontalAlignment = HorizontalAlignment.Center;
        _turnSummary.Visible = false;
        var summaryBox = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        Ui.Anchor(summaryBox, 0.5f, 0f, -620f, 60f, 620f, 96f);
        summaryBox.AddChild(_turnSummary);
        _root.AddChild(summaryBox);

        // 结算演出的顶部合计（"提 N 子"）与中央横幅（"金方出局" / "对局结束"，settlement-show-callouts D6）：
        // 都只在演出遮罩给出文案时可见，由 RefreshShow 逐帧刷新；横幅放中央面板的位置（此时终局面板尚未显示）。
        _showCaption = Ui.Text(string.Empty, Ui.PanelBorder, UiTheme.Banner);
        _showCaption.HorizontalAlignment = HorizontalAlignment.Center;
        Ui.Anchor(_showCaption, 0.5f, 0f, -300f, 92f, 300f, 122f);
        _showCaption.Visible = false;
        _root.AddChild(_showCaption);
        _showBanner = Ui.ShowBanner(string.Empty, Ui.PanelBorder);
        _showBanner.HorizontalAlignment = HorizontalAlignment.Center;
        _showBanner.VerticalAlignment = VerticalAlignment.Center;
        Ui.Anchor(_showBanner, 0.5f, 0.5f, -400f, -60f, 400f, 60f);
        _showBanner.Visible = false;
        _root.AddChild(_showBanner);

        // 悬停格坐标读数（viewport-camera）：固定在回合横幅右侧。需要推屏的地图上四边标注经常不在画面内，读坐标靠它。
        _hoverReadout = Ui.Text(string.Empty, Ui.InfoText, UiTheme.Title);
        Ui.Anchor(_hoverReadout, 0f, 0f, 334f, 28f, 470f, 54f);
        _root.AddChild(_hoverReadout);
    }

    /// <summary>插旗提示里是否带一行推屏说明：只在一屏看不全的地图上显示（一屏看全的小图上插旗画面保持原样）。</summary>
    public bool CameraHintVisible { get; set; }

    /// <summary>悬停格坐标读数。文本由 Presentation 的 <c>HoverReadout</c> 给出（即 <c>Coord</c> 的记法），本类不拼坐标；空串即不显示。</summary>
    public void SetHoverReadout(string text) => _hoverReadout.Text = text;

    /// <summary>点了"全局预览"按钮（与 M 键同一入口）。只在一屏看不全的地图上出现（<see cref="CameraHintVisible"/>）。</summary>
    public event Action? OverviewPressed;

    /// <summary>相机当前是否处于全局预览（由主场景在刷新前同步，按钮据此显示按下态）。</summary>
    public bool OverviewActive { get; set; }

    /// <summary>点了"跟随"按钮（与 F 键同一入口，follow-opponent）。</summary>
    public event Action? FollowPressed;

    /// <summary>对手行动时镜头是否跟随（由主场景在刷新前同步）。</summary>
    public bool FollowActive { get; set; } = true;

    /// <summary>
    /// 对手回合摘要（settlement-show「对手回合摘要」）：文案由 Presentation 的 <c>TurnSummary</c> 给出，这里只显示，用行动者的阵营色。
    /// 传 <c>null</c> 收起。显示期间通知行让位：对手行动时它说的是同一件事（只是没有数值），不再显示；
    /// 轮到本机时通知行要给操作反馈，挪到摘要下面一行（见 <see cref="Refresh"/>）。
    /// </summary>
    public void SetTurnSummary(string? text, Color color)
    {
        _turnSummary.Text = text ?? string.Empty;
        _turnSummary.Visible = text is not null;
        _turnSummary.AddThemeColorOverride("font_color", color.Lightened(0.35f));
    }

    private void BuildTurnBanner()
    {
        (PanelContainer panel, VBoxContainer body) = Ui.Panel(PanelTier.Primary, UiTheme.Space4, UiTheme.Space1);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.Space4);
        _turnEmblem = new EmblemIcon { CustomMinimumSize = new Vector2(34f, 34f), MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddChild(_turnEmblem);
        var texts = new VBoxContainer();
        texts.AddThemeConstantOverride("separation", UiTheme.Space0);
        _turnTitle = Ui.Text("—");
        _turnSubtitle = Ui.Text("—", Ui.MutedText, UiTheme.Caption);
        texts.AddChild(_turnTitle);
        texts.AddChild(_turnSubtitle);
        row.AddChild(texts);
        body.AddChild(row);
        Ui.Anchor(panel, 0f, 0f, 14f, 14f, 322f, 68f);
        _root.AddChild(panel);
    }

    private void BuildOrderBar()
    {
        (PanelContainer panel, VBoxContainer body) = Ui.Panel(PanelTier.Secondary, UiTheme.Space3, UiTheme.Space1);
        _orderButton = Ui.Action(string.Empty);
        _orderButton.Pressed += () => LayerPressed?.Invoke(TacticalLayer.Order);
        // 按钮里放一条居中的徽记条（hud-panels D3）：整条仍是这一个按钮，点了切换顺序层；子控件一律不接鼠标。
        var center = new CenterContainer();
        Ui.FillButton(_orderButton, center);
        _orderContent = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _orderContent.AddThemeConstantOverride("separation", UiTheme.Space2);
        center.AddChild(_orderContent);
        body.AddChild(_orderButton);
        Ui.Anchor(panel, 0.5f, 0f, -290f, 14f, 290f, 60f);
        _root.AddChild(panel);
    }

    private void BuildRankPanel()
    {
        (PanelContainer panel, VBoxContainer body) = Ui.Panel(PanelTier.Primary, UiTheme.Space4, UiTheme.Space1);
        body.AddChild(Ui.Heading("势力排名"));
        _rankBody = new VBoxContainer();
        _rankBody.AddThemeConstantOverride("separation", UiTheme.Space1);
        body.AddChild(_rankBody);
        Ui.Anchor(panel, 1f, 0f, -300f, 14f, -14f, 146f);
        // 贴右上角：内容比最小宽度宽时向左长（缺省 End 会向右长出屏幕——restore-go-core-rules 段 E 截图里"领地 + 棋串"那半行被截掉）。
        panel.GrowHorizontal = Control.GrowDirection.Begin;
        _root.AddChild(panel);
        _rankPanel = panel;
    }

    private void BuildHandBar()
    {
        (PanelContainer panel, VBoxContainer body) = Ui.Panel(PanelTier.Primary, UiTheme.Space4, UiTheme.Space1);
        body.AddChild(Ui.Heading("手牌（点选后再点格子落子）"));
        _handBody = new VBoxContainer();
        _handBody.AddThemeConstantOverride("separation", UiTheme.Space1);
        body.AddChild(_handBody);
        Ui.Anchor(panel, 0f, 1f, 14f, -186f, 378f, -58f);
        _root.AddChild(panel);
    }

    private void BuildLayerButtons()
    {
        (PanelContainer panel, VBoxContainer body) = Ui.Panel(PanelTier.Secondary, UiTheme.Space3, UiTheme.Space1);
        _layerButtons = new HBoxContainer();
        _layerButtons.AddThemeConstantOverride("separation", UiTheme.Space2);
        body.AddChild(_layerButtons);
        Ui.Anchor(panel, 0.5f, 1f, -366f, -50f, 366f, -10f);
        _root.AddChild(panel);
    }

    private void BuildActionPanel()
    {
        (PanelContainer panel, VBoxContainer body) = Ui.Panel(PanelTier.Primary, UiTheme.Space4, UiTheme.Space1);
        _actionBody = body;
        Ui.Anchor(panel, 1f, 1f, -300f, -186f, -14f, -58f);
        _root.AddChild(panel);
    }

    private void BuildPreviewPanel()
    {
        (PanelContainer panel, VBoxContainer body) = Ui.Panel(PanelTier.Primary, UiTheme.Space4, UiTheme.Space1);
        _previewPanel = panel;
        body.AddChild(Ui.Heading("批次预演"));
        _previewBody = new VBoxContainer();
        _previewBody.AddThemeConstantOverride("separation", UiTheme.Space1);
        body.AddChild(_previewBody);
        Ui.Anchor(panel, 1f, 0f, -340f, 158f, -14f, 560f);
        _root.AddChild(panel);
    }

    private void BuildLayerPanel()
    {
        (PanelContainer panel, VBoxContainer body) = Ui.Panel(PanelTier.Secondary, UiTheme.Space4, UiTheme.Space1);
        _layerPanel = panel;
        _layerBody = new VBoxContainer();
        _layerBody.AddThemeConstantOverride("separation", UiTheme.Space1);
        body.AddChild(_layerBody);
        Ui.Anchor(panel, 0f, 0f, 14f, 92f, 392f, 600f);
        _root.AddChild(panel);
        panel.Visible = false;
    }

    private void BuildCenterPanel()
    {
        (PanelContainer panel, VBoxContainer body) = Ui.Panel(PanelTier.Primary, UiTheme.Space5, UiTheme.Space2);
        _centerPanel = panel;
        _centerBody = body;
        CenterBox(0.5f, 660f, 480f);
        _root.AddChild(panel);
        panel.Visible = false;
    }

    /// <summary>
    /// 征募面板收起后的顶部提示条（recruit-panel-collapse D3）：顶部居中、通知条（66–90）之下的一条窄条，整条可点、点了展开。
    /// 横向只占画布中心 ±260：画布宽恒 ≥ 1600（stretch = expand），左缘 ≥ 540，不碰左侧信息层面板（右缘 392）与右上排名面板（左缘 ≥ 1300）。
    /// 与中央面板分开：<see cref="CenterPanelOpen"/> 的截图取景语义不变。
    /// </summary>
    private void BuildRecruitBar()
    {
        (PanelContainer panel, VBoxContainer body) = Ui.Panel(PanelTier.Secondary, UiTheme.Space2, UiTheme.Space1);
        _recruitBar = panel;
        _recruitBarButton = Ui.Action(string.Empty);
        _recruitBarButton.Pressed += () => RecruitCollapsePressed?.Invoke();
        body.AddChild(_recruitBarButton);
        Ui.Anchor(_recruitBar, 0.5f, 0f, -260f, 96f, 260f, 134f);
        _root.AddChild(panel);
        panel.Visible = false;
    }

    /// <summary>征募提示条此刻是否显示（截图取景自证）。</summary>
    public bool RecruitBarOpen => _recruitBar.Visible;

    /// <summary>征募提示条在屏幕上的矩形（截图取景自证：不得压住棋盘四边坐标标注）。</summary>
    public Rect2 RecruitBarRect => _recruitBar.GetGlobalRect();

    /// <summary>
    /// 中央面板（终局结算 / 插旗提示 / 手牌信息 / 征募）此刻是否显示。
    /// 截图自检用：除插旗提示外它都压在棋盘上，取景时必须为 <c>false</c>（见 <c>GameRoot.BeginCapture</c>）。
    /// </summary>
    public bool CenterPanelOpen => _centerPanel.Visible;

    /// <summary>「势力排名」面板在屏幕上的矩形（截图取景自证：右边界不得超出视口）。</summary>
    public Rect2 RankPanelRect => _rankPanel.GetGlobalRect();

    /// <summary>中央面板的高度收到内容的最小高度（宽度不动）：插旗提示的高度由内容撑开（hud-panels D5）。</summary>
    private void FitCenterHeight()
    {
        if (_centerFitsContent && IsInstanceValid(_centerPanel) && _centerPanel.Visible)
        {
            _centerPanel.Size = _centerPanel.Size with { Y = _centerPanel.GetCombinedMinimumSize().Y };
        }
    }

    /// <summary>中央面板按内容定尺寸——插旗提示只占一条，不该盖住棋盘。</summary>
    private void CenterBox(float anchorY, float width, float height) =>
        Ui.Anchor(_centerPanel, 0.5f, anchorY, -width * 0.5f, -height * 0.5f, width * 0.5f, height * 0.5f);

    // ---------- 刷新 ----------

    /// <summary>按当前对局状态刷新全部面板。只在状态变化时调用，不逐帧重建（演出期间逐帧变化的只有势力排名栏，见 <see cref="RefreshRank"/>）。</summary>
    /// <param name="mask">结算演出遮罩（settlement-show D6）；<c>null</c> 即无演出。</param>
    /// <param name="deferResult">演出尚未播完：终局面板暂不显示，等横幅播完（settlement-show-callouts「横幅节拍」）。</param>
    public void Refresh(MatchSession session, TacticalLayerState layers, HandPanelState handPanel, RecruitPanelCollapse recruitCollapse, ShowMask? mask = null, bool deferResult = false)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(handPanel);
        ArgumentNullException.ThrowIfNull(recruitCollapse);
        ViewerWorld world = session.World;

        RefreshTurnBanner(session, world, layers);
        RefreshOrderBar(world);
        RefreshRank(world, mask ?? ShowMask.Empty);
        RefreshHand(session, world);
        RefreshLayerButtons(layers, handPanel);
        RefreshActions(session, world);
        RefreshPreview(world);
        RefreshLayerPanel(world, layers);
        RefreshCenter(session, world, handPanel, recruitCollapse, deferResult);
        RefreshShowOverlay(mask ?? ShowMask.Empty);
        _notice.Text = session.Notice;
        bool summarised = _turnSummary.Visible;
        _notice.Visible = !summarised || session.IsMyTurn;
        Ui.Anchor(_notice, 0.5f, 0f, -420f, summarised ? 98f : 66f, 420f, summarised ? 122f : 90f);
    }

    /// <summary>演出逐帧刷新（settlement-show-callouts D6）：势力排名栏的分段到账与数字弹跳、顶部提子合计、中央横幅。由 GameRoot 每帧调用。</summary>
    public void RefreshShow(ViewerWorld world, ShowMask mask)
    {
        ArgumentNullException.ThrowIfNull(mask);
        RefreshRank(world, mask);
        RefreshShowOverlay(mask);
    }

    /// <summary>顶部合计与横幅：文案来自遮罩；横幅在本条前 20% 淡入、后 25% 淡出，其余时候全亮。透明度只在这里由千分比折算，Presentation 不出浮点。</summary>
    private void RefreshShowOverlay(ShowMask mask)
    {
        _showCaption.Text = mask.CaptureSummary ?? string.Empty;
        _showCaption.Visible = mask.CaptureSummary is not null;

        if (mask.Banner is { } banner)
        {
            float p = banner.ProgressPermille / 1000f;
            float alpha = p < 0.2f ? p / 0.2f : p > 0.75f ? (1f - p) / 0.25f : 1f;
            _showBanner.Text = banner.Text;
            _showBanner.Modulate = Colors.White with { A = Math.Clamp(alpha, 0f, 1f) };
            _showBanner.Visible = true;
        }
        else
        {
            _showBanner.Visible = false;
        }
    }

    private void RefreshTurnBanner(MatchSession session, ViewerWorld world, TacticalLayerState layers)
    {
        MatchPublicView view = world.Public.View;
        PlayerId banner = view.CurrentPlayer ?? session.Me;
        FactionStyle faction = FactionTable.For(banner);
        _turnEmblem.Set(faction.Emblem, Visuals.ToColor(faction.Primary));
        _turnTitle.Text = view.Phase == MatchPhase.FlagPlanting
            ? "插旗阶段"
            : $"第 {view.MajorRound} 大回合 · {Labels.Player(banner)}";
        _turnSubtitle.Text = view.Phase switch
        {
            MatchPhase.FlagPlanting => "点一块出生区地砖插旗",
            MatchPhase.Ended => "对局结束",
            _ => $"{Names.Stage(view.Stage)} · 信息层模式：{(layers.Mode == LayerInputMode.HoldToShow ? "按住显示" : "点击切换")}（T 切换）",
        };
    }

    /// <summary>
    /// 行动顺序条（hud-panels D3）：标题、按顺序的"徽记 + 阵营名"条目、条目间的 ›、末尾快捷键提示，文字全部取自 <see cref="HudPanelRows.OrderBar"/>。
    /// 当前行动者金字 + 金色底边（不只靠颜色）；其他条目底下放同高的空占位，各条目的名字在同一条基线上。
    /// </summary>
    private void RefreshOrderBar(ViewerWorld world)
    {
        OrderBarView bar = HudPanelRows.OrderBar(world.Public.View);
        Ui.Clear(_orderContent);
        _orderContent.AddChild(Ui.Text(HudPanelRows.OrderTitleText, Ui.MutedText, UiTheme.Caption));
        _orderContent.AddChild(Ui.Spacer(UiTheme.Space3, 0f));
        if (bar.EmptyText is { } empty)
        {
            _orderContent.AddChild(Ui.Text(empty, Ui.InfoText, UiTheme.Small));
        }

        for (int i = 0; i < bar.Entries.Length; i++)
        {
            OrderBarEntry entry = bar.Entries[i];
            if (i > 0)
            {
                _orderContent.AddChild(Ui.Text("›", Ui.MutedText, UiTheme.Small));
            }

            FactionStyle faction = FactionTable.For(entry.Player);
            var item = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            item.AddThemeConstantOverride("separation", UiTheme.Space0);
            var line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            line.AddThemeConstantOverride("separation", UiTheme.Space1);
            var icon = new EmblemIcon { CustomMinimumSize = new Vector2(14f, 14f), MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            icon.Set(faction.Emblem, Visuals.ToColor(faction.Primary));
            line.AddChild(icon);
            line.AddChild(Ui.Text(entry.NameText, entry.IsCurrent ? Ui.PanelBorder : Ui.InfoText, UiTheme.Small));
            item.AddChild(line);
            item.AddChild(entry.IsCurrent ? Ui.Bar(Ui.PanelBorder, 0f, UiTheme.SelectedEdgeWidthPx) : Ui.Spacer(0f, UiTheme.SelectedEdgeWidthPx));
            _orderContent.AddChild(item);
        }

        _orderContent.AddChild(Ui.Spacer(UiTheme.Space3, 0f));
        _orderContent.AddChild(Ui.Text(bar.HintText, Ui.MutedText, UiTheme.Caption));
    }

    /// <summary>
    /// 势力排名栏（settlement-show 3.4）。<paramref name="mask"/> 里有该玩家的势力显示时按遮罩画：势力重算节拍播放前显示旧值与旧名次；
    /// 播放中显示滚动中的整数插值、带符号增量与名次变动提示；播完遮罩为空，回到视图模型（结算后快照）的值，增量标记随之消失。
    /// 演出期间由 GameRoot 逐帧调用；其余时候随 <see cref="Refresh"/> 在状态变化时刷新。
    /// </summary>
    public void RefreshRank(ViewerWorld world, ShowMask mask)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(mask);
        Ui.Clear(_rankBody);
        var power = (PowerLayerContent)world.Layer(TacticalLayer.Power);
        if (power.Players.IsDefaultOrEmpty)
        {
            _rankBody.AddChild(Ui.Text("尚未产生势力快照", Ui.MutedText));
            return;
        }

        // 行序：节拍播放前按旧名次（画面保持结算前的样子），播放中及之后按新名次。
        int? ShownRank(PlayerPowerRowView row) =>
            mask.Power.TryGetValue(row.Player, out PowerDisplay? shown) && !shown.Rolling ? shown.Change.OldRank : row.Rank;

        // 六列（hud-panels D2）：自家标记、徽记、名次、阵营、势力（右对齐）、明细。列宽由最宽的一格决定，各行自然对齐。
        // 每行的文字整份取自 HudPanelRows.RankRow（演出前旧名次 / 演出中滚动值 / 明细规则都在那里），这里只摆放与取色。
        var grid = new GridContainer { Columns = 6, MouseFilter = Control.MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", UiTheme.Space3);
        grid.AddThemeConstantOverride("v_separation", UiTheme.Space1);
        foreach (PlayerPowerRowView row in power.Players.OrderBy(p => ShownRank(p) ?? int.MaxValue).ThenBy(p => p.Player.Value))
        {
            PowerDisplay? display = mask.Power.TryGetValue(row.Player, out PowerDisplay? shown) ? shown : null;
            RankRowView view = HudPanelRows.RankRow(row, display, world.Viewer);
            FactionStyle faction = FactionTable.For(view.Player);
            Color textColor = view.IsMuted ? Ui.MutedText : Ui.InfoText;

            // 自家标记：本机玩家一条金色竖条，其他行同尺寸空占位（不靠文字颜色区分，D2）。
            grid.AddChild(view.IsViewer
                ? Ui.Bar(Ui.PanelBorder, UiTheme.ViewerMarkWidthPx, 16f)
                : Ui.Spacer(UiTheme.ViewerMarkWidthPx, 16f));

            var icon = new EmblemIcon { CustomMinimumSize = new Vector2(16f, 16f), MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            icon.Set(faction.Emblem, Visuals.ToColor(faction.Primary));
            grid.AddChild(icon);
            // 一行里势力列放大时行变高，其余各格纵向居中（下同）。
            grid.AddChild(Centered(Ui.Text(view.RankText, textColor)));
            grid.AddChild(Centered(Ui.Text(view.NameText, textColor)));

            // 势力列：演出中按段首放大的字号（呈现层给档位、RankFontPx 折算），其余时候正文字号；放大时这一格变宽，整列跟着变宽。
            Label powerLabel = Ui.Text(view.PowerText, textColor, display is null ? UiTheme.Body : RankFontPx(display));
            powerLabel.HorizontalAlignment = HorizontalAlignment.Right;
            grid.AddChild(Centered(powerLabel));

            // 明细列：状态部分恒为次要色（D9-6），其余部分演出增量取增量色（负数警示色、否则阵营色），平时次要色。不截断（D9-1）。
            var detail = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            detail.AddThemeConstantOverride("separation", UiTheme.Space3);
            if (view.DetailStatusText is { } status)
            {
                detail.AddChild(Centered(Ui.Text(status, Ui.MutedText, UiTheme.Caption)));
            }

            Color restColor = !view.DetailIsDelta ? Ui.MutedText : view.DeltaIsNegative ? Ui.DangerText : Visuals.ToColor(faction.Primary);
            if (view.DetailRestText.Length > 0)
            {
                // 领地段 / 军势段：本段增量与段首数字一起弹（tiered-number-show），字号同势力列；定格与平时仍是注释字号。
                int restSize = display is { Stage: PowerStage.Territory or PowerStage.Group } ? RankFontPx(display) : UiTheme.Caption;
                detail.AddChild(Centered(Ui.Text(view.DetailRestText, restColor, restSize)));
            }

            // 名次提示另起一个标签、恒为正文字号，不随段首放大：面板贴右上角向左长，五档 200% 时连它一起放大会压住顶部的行动顺序条。
            if (view.DetailRankHintText is { } rankHint)
            {
                detail.AddChild(Centered(Ui.Text(rankHint, restColor, UiTheme.Body)));
            }

            grid.AddChild(detail);
        }

        _rankBody.AddChild(grid);
    }

    /// <summary>文字在所在格里纵向居中（格比文字高时：势力排名里放大的势力列、按钮里的两列）。</summary>
    private static Label Centered(Label label)
    {
        label.VerticalAlignment = VerticalAlignment.Center;
        return label;
    }

    /// <summary>
    /// 势力栏分段到账时数字的字号（像素）：领地段 / 军势段的段首放大到样式表的"势力栏放大"幅度（按本段增量的数值档位，档位由 Presentation 给出），
    /// 段内线性回落到正文字号；其余时候（未开始 / 定格）就是正文字号。缩放只在这里由段内千分比折算成整数字号。
    /// </summary>
    public static int RankFontPx(PowerDisplay display)
    {
        ArgumentNullException.ThrowIfNull(display);
        if (display.Stage is not (PowerStage.Territory or PowerStage.Group))
        {
            return UiTheme.BodyFontPx;
        }

        int extraPercent = NumberTierStyle.For(display.Change.TierOf(display.Stage)).RankScalePercent - 100;
        return UiTheme.BodyFontPx + (UiTheme.BodyFontPx * extraPercent * (1000 - display.StagePermille) / 100000);
    }

    private void RefreshHand(MatchSession session, ViewerWorld world)
    {
        Ui.Clear(_handBody);
        OwnHandAreaView own = world.HandPanel().Own;
        bool mustDiscard = session.IsMyTurn && session.Match.Stage == TurnStage.OrganizeHand;
        string slots = own.TypeSlots is int s ? $"{own.OccupiedSlots} / {s}" : $"{own.OccupiedSlots} / —";
        _handBody.AddChild(Ui.Text($"类型槽 {slots}　本轮新征募 {own.PendingGained} 枚", Ui.MutedText, UiTheme.Caption));

        if (own.Rows.IsDefaultOrEmpty)
        {
            _handBody.AddChild(Ui.Text("手牌为空", Ui.MutedText));
            return;
        }

        // 每类一个按钮，按钮里左右两列（hud-panels D4）：类型名靠左、数量靠右，文字取自 HudPanelRows.HandRow。
        // 选中仍由按钮主题画金色底边；子 Label 不随按钮状态变色，字色按"语义 + 是否选中"取按钮常态的字色。
        foreach (OwnHandRowView row in own.Rows)
        {
            HandRowView view = HudPanelRows.HandRow(row, mustDiscard);
            bool selected = !mustDiscard && session.SelectedType == view.Type;
            Button button = view.Kind == ButtonKind.Default ? Ui.Toggle(string.Empty, selected) : Ui.Action(string.Empty, view.Kind);
            Color color = Ui.ButtonTextColor(view.Kind, selected);
            var columns = new HBoxContainer();
            columns.AddThemeConstantOverride("separation", UiTheme.Space3);
            Label name = Centered(Ui.Text(view.NameText, color, UiTheme.Small));
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            columns.AddChild(name);
            Label count = Centered(Ui.Text(view.CountText, color, UiTheme.Small));
            count.HorizontalAlignment = HorizontalAlignment.Right;
            columns.AddChild(count);
            Ui.FillButton(button, columns);

            PieceType type = view.Type;
            if (mustDiscard)
            {
                button.Pressed += () => DiscardRequested?.Invoke(type);
            }
            else
            {
                button.Pressed += () => HandTypeSelected?.Invoke(type);
            }

            _handBody.AddChild(button);
        }
    }

    private void RefreshLayerButtons(TacticalLayerState layers, HandPanelState handPanel)
    {
        Ui.Clear(_layerButtons);
        (TacticalLayer Layer, string Key)[] entries =
        [
            (TacticalLayer.Board, "1"),
            (TacticalLayer.Power, "2"),
            (TacticalLayer.Relics, "3"),
        ];
        foreach ((TacticalLayer layer, string key) in entries)
        {
            Button button = Ui.Toggle($"{Names.Layer(layer)} [{key}]", layers.Active == layer, 104);
            TacticalLayer captured = layer;
            button.Pressed += () => LayerPressed?.Invoke(captured);
            _layerButtons.AddChild(button);
        }

        // 盘面层显示期间给出读法切换入口：按住显示模式下键正被按住，没有"再按一次"可言（merge-board-layer D3）。
        if (layers.Active == TacticalLayer.Board)
        {
            Button reading = Ui.Toggle($"{Names.Reading(layers.Reading)} [Tab]", false, 104);
            reading.Pressed += () => ReadingPressed?.Invoke();
            _layerButtons.AddChild(reading);
        }

        Button hand = Ui.Toggle("手牌 [H]", handPanel.IsOpen, 104);
        hand.Pressed += () => HandPanelPressed?.Invoke();
        _layerButtons.AddChild(hand);

        Button mode = Ui.Toggle(layers.Mode == LayerInputMode.HoldToShow ? "按住 [T]" : "点击 [T]", false, 76);
        mode.Pressed += () => LayerModePressed?.Invoke();
        _layerButtons.AddChild(mode);

        // 镜头跟随对手（follow-opponent）：默认开，按下态即开。
        Button follow = Ui.Toggle("跟随 [F]", FollowActive, 92);
        follow.Pressed += () => FollowPressed?.Invoke();
        _layerButtons.AddChild(follow);

        // 全局预览：只在一屏看不全的地图上给（一屏看全的小图上按钮没有意义，画面也保持不变）。
        if (CameraHintVisible)
        {
            Button overview = Ui.Toggle("全局 [M]", OverviewActive, 92);
            overview.Pressed += () => OverviewPressed?.Invoke();
            _layerButtons.AddChild(overview);
        }
    }

    private void RefreshActions(MatchSession session, ViewerWorld world)
    {
        Ui.Clear(_actionBody);
        PreviewPresentation? preview = world.Preview();
        if (preview is null)
        {
            _actionBody.AddChild(Ui.Text(session.IsOver ? "对局已结束" : session.AwaitingZone ? "先点一块出生区地砖插旗" : "等待其他玩家行动…", Ui.MutedText, wrap: true));
            return;
        }

        _actionBody.AddChild(Ui.Text($"部署额度 {preview.QuotaText}"));
        if (preview.PassWarning is { } warning)
        {
            _actionBody.AddChild(Ui.Text(warning, Ui.DangerText, UiTheme.Caption, wrap: true));
        }

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.Space2);
        Button confirm = Ui.Action("确认落子 [Enter]", minWidth: 132);
        confirm.Disabled = !preview.CanConfirm;
        confirm.Pressed += () => ConfirmPressed?.Invoke();
        row.AddChild(confirm);
        Button pass = Ui.Action("Pass [P]", minWidth: 78);
        pass.Pressed += () => PassPressed?.Invoke();
        row.AddChild(pass);
        _actionBody.AddChild(row);

        Button clear = Ui.Action("清空暂放");
        clear.Pressed += () => ClearPressed?.Invoke();
        _actionBody.AddChild(clear);
    }

    private void RefreshPreview(ViewerWorld world)
    {
        Ui.Clear(_previewBody);
        PreviewPresentation? preview = world.Preview();
        _previewPanel.Visible = preview is not null;
        if (preview is null)
        {
            return;
        }

        _previewBody.AddChild(Ui.Text($"额度 {preview.QuotaText}"));
        foreach (HandCostView cost in preview.HandCosts)
        {
            _previewBody.AddChild(Ui.Text("消耗 " + cost.Text, Ui.MutedText, UiTheme.Small));
        }

        if (preview.Failure is { } failure)
        {
            _previewBody.AddChild(Ui.Separator());
            _previewBody.AddChild(Ui.Text(failure.Title, Ui.DangerText));
            _previewBody.AddChild(Ui.Text(failure.Detail, Ui.DangerText, UiTheme.Small, wrap: true));
        }

        // 改造（artisan-terrain-edit 4.1 第 1 / 7 项）：每枚暂放匠人一行"落点 → 已选动作与目标（可选目标 N 个）"。
        // 目标清单整份来自预演，HUD 只排字。
        AddSection(_previewBody, "改造",
            preview.ArtisanEdits.Select(a => $"{a.ArtisanCell.ToNotation()} → {a.ChosenText}（可选 {a.Targets.Length} 个，[E] 轮换）"),
            Ui.InfoText);
        AddSection(_previewBody, "预计提子", preview.Captures.Select(c => c.Text), Ui.InfoText);
        AddSection(_previewBody, "己方棋串", preview.OwnGroups.Select(GroupLine), Ui.InfoText);
        AddSection(_previewBody, "军势预览", preview.OwnGroups.Where(g => g.Power is not null).Select(g => g.Power!.FormulaText), Ui.MutedText);
        AddSection(_previewBody, "势力与排名", preview.PowerChanges.Select(c => c.Text), Ui.InfoText);
        AddSection(_previewBody, "将揭示", preview.RevealHints.Select(h => $"{h.Coord.ToNotation()} {h.Text}"), Ui.InfoText);
    }

    private static string DiffText(ImmutableArray<ReadingDiffCell> cells) =>
        string.Join("、", cells.Select(c => $"{c.Coord.ToNotation()}（{string.Join("/", c.Reasons.Select(Labels.TerrainReason))}）"));

    private static string GroupLine(OwnGroupView group) =>
        (group.IsSuicideRisk ? "⚠ " : string.Empty) + $"[{Names.Danger(group.Danger)}] " + group.LibertyText;

    private void RefreshLayerPanel(ViewerWorld world, TacticalLayerState layers)
    {
        Ui.Clear(_layerBody);
        _layerPanel.Visible = layers.Active is not null;
        if (layers.Active is not { } active)
        {
            return;
        }

        LayerContent content = world.Layer(active, layers.Reading);
        _layerBody.AddChild(Ui.Heading(
            active == TacticalLayer.Board
                ? $"{Names.Layer(active)}层 · {Names.Reading(layers.Reading)}读法"
                : $"{Names.Layer(active)}层"));
        if (active == TacticalLayer.Board)
        {
            _layerBody.AddChild(Ui.Text("Tab 切换归属／棋串读法。平地上两种读法点亮同一批空格；崖壁、栅栏、深水与林地会让两者不同。", Ui.MutedText, wrap: true));
            BoardReadingDiff? diff = content switch
            {
                TerritoryLayerContent territory => territory.Diff,
                LibertyLayerContent groups => groups.Diff,
                _ => null,
            };
            if (diff is { IsEmpty: false })
            {
                // 差集与地形原因来自视图模型（tactical-layers「差集可由地形解释」），本层只列出来。
                if (!diff.CoveredNotLiberty.IsEmpty)
                {
                    _layerBody.AddChild(Ui.Text("被覆盖但不是气：" + DiffText(diff.CoveredNotLiberty), Ui.InfoText, wrap: true));
                }

                if (!diff.LibertyNotCovered.IsEmpty)
                {
                    _layerBody.AddChild(Ui.Text("是气但无人覆盖：" + DiffText(diff.LibertyNotCovered), Ui.InfoText, wrap: true));
                }
            }
        }
        // 新地表的规则说明（tactical-layers「新地表的规则标示」）：只列当前地图上出现过的，文案取自视图模型。
        ImmutableArray<string> legend = content switch
        {
            TerritoryLayerContent territory => territory.SurfaceLegend,
            LibertyLayerContent groups => groups.SurfaceLegend,
            _ => [],
        };
        foreach (string line in legend)
        {
            _layerBody.AddChild(Ui.Text("图例：" + line, Ui.MutedText, wrap: true));
        }

        switch (content)
        {
            case TerritoryLayerContent:
                _layerBody.AddChild(Ui.Text("图例：实色 = 独占（只作判读，不计分）／浅色 = 被棋子占据", Ui.MutedText, wrap: true));
                _layerBody.AddChild(Ui.Text("金色 = 争议（多方同时覆盖）／暗灰 = 中立（无人覆盖）", Ui.MutedText, wrap: true));
                _layerBody.AddChild(Ui.Text("本层弱化棋子与装饰，只突出归属。", Ui.MutedText, wrap: true));
                break;

            case LibertyLayerContent liberties:
                _layerBody.AddChild(Ui.Text($"阈值：气 ≤ {liberties.Thresholds.Danger} 危险，气 ≤ {liberties.Thresholds.Urgent} 紧急", Ui.MutedText));
                AddScroll(_layerBody, liberties.Groups
                    .Where(g => g.Level != DangerLevel.Safe)
                    .OrderBy(g => g.LibertyCount)
                    .Select(g => ($"{Labels.Player(g.Owner)} {g.Stones.Length} 子 · 气 {g.LibertyCount} · {Names.Danger(g.Level)}：{Labels.Coords(g.Stones)}",
                        g.Level == DangerLevel.Urgent ? Ui.DangerText : Ui.InfoText)));
                break;

            case PowerLayerContent power:
                _layerBody.AddChild(Ui.Text("势力 = 领地分 + 棋串军势。领地分 = 独占空格数（盘上按阵营着色），争议格与中立格不计分。明细为精确值。", Ui.MutedText, wrap: true));
                AddScroll(_layerBody, power.Players
                    .Select(p => ($"{Labels.Player(p.Player)} {p.ExactText}{(p.Rank is int r ? $" 第 {r} 名" : string.Empty)}", Ui.InfoText))
                    .Concat(power.Groups.Select(g => ($"{Labels.Player(g.Owner)} {Labels.Coords(g.Stones)} {g.Power.FormulaText}", Ui.MutedText))));
                break;

            case RelicLayerContent relics:
                AddScroll(_layerBody, relics.Relics.Select(r => (
                    $"{r.Coord.ToNotation()}　{r.ZoneText}　{r.ContentText}　{r.ControlText}" + (r.IneffectiveReason is null ? string.Empty : $"　{r.IneffectiveReason}"),
                    r.IneffectiveReason is null ? Ui.InfoText : Ui.MutedText)));
                break;

            case OrderLayerContent order:
                _layerBody.AddChild(Ui.Text($"第 {order.MajorRound} 大回合顺序：{string.Join(" > ", order.CurrentOrder.Select(p => FactionTable.For(p).Name))}", Ui.InfoText, wrap: true));
                _layerBody.AddChild(Ui.Text($"若此刻结束本大回合，下一轮顺序：{string.Join(" > ", order.PredictedNextOrder.Select(p => FactionTable.For(p).Name))}", Ui.InfoText, wrap: true));
                AddScroll(_layerBody, order.Rows.Select(r => ($"{Labels.Player(r.Player)}　{r.Explanation}", Ui.MutedText)));
                break;

            default:
                break;
        }
    }

    private void RefreshCenter(MatchSession session, ViewerWorld world, HandPanelState handPanel, RecruitPanelCollapse recruitCollapse, bool deferResult)
    {
        Ui.Clear(_centerBody);
        _centerFitsContent = false;
        _recruitBar.Visible = false;
        if (session.IsOver && session.Match.Result is { } result && !deferResult)
        {
            CenterBox(0.5f, 700f, 220f);
            BuildResult(result, session.Me);
            _centerPanel.Visible = true;
            return;
        }

        if (session.AwaitingZone)
        {
            // 插旗提示放左列（信息层面板的位置，此时它是隐藏的）：居中放在顶部会盖住棋盘远边的列标注（terrain-model 6.3）。
            // 只定左、上、右，底边等于顶边：高度由内容撑开（hud-panels D5；原先写死 130 / 176 像素，换行后最后一行被裁掉一半）。
            Ui.Anchor(_centerPanel, 0f, 0f, 14f, 92f, 392f, 92f);
            _centerBody.AddChild(Ui.Heading("开局插旗"));
            _centerBody.AddChild(Ui.Text("点棋盘上任意一块染色的出生区地砖，即可把旗插在那一区。", Ui.InfoText, wrap: true));
            _centerBody.AddChild(Ui.Text("前 3 个大回合只能在自己的出生区落子（构筑保护期）。", Ui.MutedText, wrap: true));
            if (CameraHintVisible)
            {
                _centerBody.AddChild(Ui.Text("地图一屏看不全：贴边 / 方向键 / WASD 推屏，滚轮缩放，空格回家。", Ui.MutedText, wrap: true));
            }
            _centerPanel.Visible = true;
            _centerFitsContent = true;
            // 换行文字要等摆进面板宽度之后才知道自己几行高：本帧布局完成后再按最小尺寸收一次高度（D5）。
            Callable.From(FitCenterHeight).CallDeferred();
            return;
        }

        if (handPanel.IsOpen)
        {
            CenterBox(0.5f, 760f, 470f);
            BuildHandInfo(world.HandPanel());
            _centerPanel.Visible = true;
            return;
        }

        if (session.RecruitPanel is { } panel && session.IsMyTurn && session.Match.Stage == TurnStage.Recruit)
        {
            // 收起 / 展开由 Presentation 的状态模型决定；面板内容吃模型透传的那一份，提示条文案取模型的（本类不拼）。
            RecruitCenterView view = recruitCollapse.Present(panel);
            if (view.IsCollapsed)
            {
                _recruitBarButton.Text = view.BarText;
                _recruitBar.Visible = true;
                _centerPanel.Visible = false;
                return;
            }

            CenterBox(0.5f, 520f, 330f);
            BuildRecruit(view.Panel);
            _centerPanel.Visible = true;
            return;
        }

        _centerPanel.Visible = false;
    }

    private void BuildRecruit(RecruitPanelView panel)
    {
        _centerBody.AddChild(Ui.Heading($"征募　展示 {panel.ShowCount}　还可免费选取 {panel.PicksRemaining} 枚　类型槽 {panel.OccupiedSlots} / {panel.TypeSlots}"));
        foreach (RecruitCandidateView candidate in panel.Candidates)
        {
            string suffix = candidate.IsPicked ? "（已选）" : candidate.IsSelectable ? string.Empty : $"（不可选：{candidate.Reason}）";
            Button button = Ui.Action($"[{candidate.Index + 1}] {Labels.Piece(candidate.Type)}{suffix}", minWidth: 300);
            button.Alignment = HorizontalAlignment.Left;
            button.Disabled = candidate.IsPicked || !candidate.IsSelectable;
            int index = candidate.Index;
            button.Pressed += () => RecruitPicked?.Invoke(index);
            _centerBody.AddChild(button);
        }

        _centerBody.AddChild(Ui.Separator());
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.Space4);
        Button done = Ui.Action("完成征募，进入部署", minWidth: 300);
        done.Pressed += () => RecruitFinished?.Invoke();
        row.AddChild(done);
        Button collapse = Ui.Action("收起 [V]", minWidth: 120);
        collapse.Pressed += () => RecruitCollapsePressed?.Invoke();
        row.AddChild(collapse);
        _centerBody.AddChild(row);
    }

    private void BuildHandInfo(HandInfoPanelView panel)
    {
        _centerBody.AddChild(Ui.Heading("全玩家手牌信息面板（再点一次「手牌」或按 H / Esc 关闭）"));
        OwnHandAreaView own = panel.Own;
        _centerBody.AddChild(Ui.Text($"■ 我方 {Labels.Player(own.Player)}　类型槽 {own.OccupiedSlots} / {own.TypeSlots?.ToString() ?? "—"}　余 {own.FreeSlots?.ToString() ?? "—"}", Ui.InfoText));
        foreach (OwnHandRowView row in own.Rows)
        {
            _centerBody.AddChild(Ui.Text("　" + row.Text, Ui.InfoText, UiTheme.Small));
        }

        AddStructure(own.Structure);
        _centerBody.AddChild(Ui.Separator());

        foreach (OpponentHandAreaView opponent in panel.Opponents)
        {
            string types = opponent.TypeNames.IsDefaultOrEmpty ? "（无）" : string.Join("、", opponent.TypeNames);
            string status = opponent.StatusText is null ? (opponent.IsActing ? "　行动中" : string.Empty) : $"　{opponent.StatusText}";
            _centerBody.AddChild(Ui.Text($"□ {Labels.Player(opponent.Player)}　持有类型：{types}{status}",
                opponent.StatusText is null ? Ui.InfoText : Ui.MutedText));
            AddStructure(opponent.Structure);
        }

        _centerBody.AddChild(Ui.Text("对手只公开手牌「类型」，不含数量、征募候选与暂放批次。", Ui.MutedText, UiTheme.Caption, wrap: true));
    }

    private void AddStructure(StructureView? structure)
    {
        if (structure is null)
        {
            _centerBody.AddChild(Ui.Text("　结构参数：—（非参赛玩家不显示）", Ui.MutedText, UiTheme.Caption));
            return;
        }

        string text = string.Join("　", new[] { structure.RevealCount.Text, structure.FreePickCount.Text, structure.TypeSlots.Text, structure.DeployLimit.Text });
        _centerBody.AddChild(Ui.Text("　" + text, Ui.MutedText, UiTheme.Caption, wrap: true));
    }

    private void BuildResult(MatchResult result, PlayerId me)
    {
        _centerBody.AddChild(Ui.Heading($"对局结束：第 {result.MajorRound} 大回合，{Names.End(result.Reason)}"));
        _centerBody.AddChild(Ui.Text(Names.Outcome(result, me), Ui.PanelBorder, UiTheme.Banner));
        _centerBody.AddChild(Ui.Separator());
        foreach (Standing standing in result.Standings)
        {
            _centerBody.AddChild(Ui.Text(
                $"第 {standing.Rank} 名　{Labels.Player(standing.Player)}　势力 {standing.Input.Power}　信物 {standing.Input.ControlledRelics}　独占空格 {standing.Input.ExclusiveCells}",
                standing.Player == me ? Ui.PanelBorder : Ui.InfoText));
        }
    }

    private static void AddSection(VBoxContainer body, string title, System.Collections.Generic.IEnumerable<string> lines, Color color)
    {
        ImmutableArray<string> items = [.. lines];
        if (items.IsEmpty)
        {
            return;
        }

        body.AddChild(Ui.Separator());
        body.AddChild(Ui.Heading(title));
        foreach (string line in items)
        {
            body.AddChild(Ui.Text(line, color, UiTheme.Small, wrap: true));
        }
    }

    private static void AddScroll(VBoxContainer body, System.Collections.Generic.IEnumerable<(string Text, Color Color)> lines)
    {
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0f, 420f), SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        var inner = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        inner.AddThemeConstantOverride("separation", UiTheme.Space1);
        foreach ((string text, Color color) in lines)
        {
            inner.AddChild(Ui.Text(text, color, UiTheme.Caption, wrap: true));
        }

        scroll.AddChild(inner);
        body.AddChild(scroll);
    }
}
