using System;
using System.Linq;
using Godot;
using Siege.Core.Board;
using Siege.Core.Recruit;
using Siege.Core.Scoring;
using Siege.Core.Simul;
using Siege.Presentation.Hand;
using Siege.Presentation.Hud;
using Siege.Presentation.Layers;
using Siege.Presentation.Show;
using Siege.Presentation.Style;
using Siege.Presentation.Text;
using Siege.Presentation.Visibility;

namespace Siege.Godot;

/// <summary>
/// 新玩法（simul-a）的 HUD（A3 裁决 D2：新的分部类，现有 <c>Refresh*</c> 一行不改）：回合横幅、手牌（双行按钮，与现有手牌同一风格）、
/// 座位提交状态区（对手只公开"准备中 / 已提交 / 已认输"）、失败原因文字（D4）、动作按钮、征召面板（仿现有征召面板）、终局面板（D10）、
/// 选座阶段的新玩法说明。排名、个人势力卡片、顺序栏、预演、信息层与演出叠层复用现有面板。文案一律经 <see cref="Labels"/> / <see cref="SimulTexts"/>。
/// </summary>
public sealed partial class Hud
{
    private const float SimulRecruitWidthPx = 600f;

    /// <summary>点手牌里的某类（暂放用这一类）。</summary>
    public event Action<PieceType>? SimulTypeSelected;

    /// <summary>打开征召面板。</summary>
    public event Action? SimulRecruitOpened;

    /// <summary>选取一个征召候选。</summary>
    public event Action<int>? SimulRecruitPicked;

    /// <summary>收起征召面板。</summary>
    public event Action? SimulRecruitClosed;

    /// <summary>提交。</summary>
    public event Action? SimulSubmitPressed;

    /// <summary>本回合不部署。</summary>
    public event Action? SimulSkipPressed;

    /// <summary>撤回提交。</summary>
    public event Action? SimulWithdrawPressed;

    /// <summary>清空暂放。</summary>
    public event Action? SimulClearPressed;

    /// <summary>认输。</summary>
    public event Action? SimulResignPressed;

    /// <summary>
    /// 新玩法的整页刷新。<paramref name="aiThinking"/>：本机已提交、AI 正在依次决策（D7）。<paramref name="deferResult"/>：演出未播完，终局面板暂不显示。
    /// </summary>
    public void RefreshSimul(SimulSession session, TacticalLayerState layers, HandPanelState handPanel, ShowMask? mask, bool deferResult, bool aiThinking)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(handPanel);
        ViewerWorld world = session.World;
        ShowMask shown = mask ?? ShowMask.Empty;

        FactionStyle faction = FactionTable.For(session.Me);
        _turnEmblem.Set(faction.Emblem, Visuals.ToColor(faction.Primary));
        _turnTitle.Text = $"{Labels.SimulRoundTitle(session.Round)} · {Labels.Player(session.Me)}";
        _turnSubtitle.Text = session.IsOver ? SimulTexts.ResultTitle
            : !session.IsActive ? SimulTexts.Resigned
            : session.CanEdit ? SimulTexts.KeysHint
            : aiThinking ? $"{SimulTexts.WaitingOthers} · {Labels.SimulAiThinking}" : SimulTexts.WaitingOthers;

        RefreshOrderBar(world);
        RefreshRank(world, shown);
        RefreshPowerCard(world, shown);
        RefreshSimulHand(session, world);
        RefreshLayerButtons(layers, handPanel);
        RefreshSimulActions(session);
        RefreshPreview(world);
        RefreshLayerPanel(world, layers);
        RefreshSimulCenter(session, world, handPanel, deferResult);
        RefreshShowOverlay(shown);
        _notice.Text = session.Notice;
    }

    /// <summary>
    /// 选座阶段（新玩法开局之前，D6）：现有刷新照常画盘面与座位，这里把中央说明与副标题换成新玩法版——不出现经典规则的终局说明。
    /// 在现有 <see cref="Refresh"/> 之后调用。
    /// </summary>
    public void RefreshSimulPickSeat()
    {
        _turnSubtitle.Text = SimulTexts.PickSeat;
        Ui.Clear(_centerBody);
        _recruitBar.Visible = false;
        Ui.Anchor(_centerPanel, 0f, 0f, 14f, 160f, 392f, CameraHintVisible ? 520f : 470f);
        _centerBody.AddChild(Ui.Heading(SimulTexts.PickSeatTitle));
        _centerBody.AddChild(Ui.Text(SimulTexts.PickSeat, Ui.InfoText, wrap: true));
        _centerBody.AddChild(Ui.Text(SimulTexts.PickSeatDetail, Ui.InfoText, wrap: true));
        _centerBody.AddChild(Ui.Text(Labels.SimulIntro, Ui.MutedText, wrap: true));
        if (CameraHintVisible)
        {
            _centerBody.AddChild(Ui.Text(SimulTexts.CameraHint, Ui.MutedText, wrap: true));
        }

        _centerPanel.Visible = true;
    }

    /// <summary>手牌：每类一个双行按钮（类型名 + 数量 / 效果一句），与现有手牌面板同一风格；选中的类型即点格暂放用的类型。</summary>
    private void RefreshSimulHand(SimulSession session, ViewerWorld world)
    {
        Ui.Clear(_handBody);
        OwnHandAreaView own = world.HandPanel().Own;
        _handBody.AddChild(Ui.Text(SimulTexts.HandHeader(own.OccupiedSlots, own.TypeSlots, own.PendingGained), Ui.MutedText, UiTheme.Caption));
        if (own.Rows.IsDefaultOrEmpty)
        {
            _handBody.AddChild(Ui.Text(SimulTexts.HandEmpty, Ui.MutedText));
            return;
        }

        foreach (OwnHandRowView row in own.Rows)
        {
            HandRowView view = HudPanelRows.HandRow(row, false);
            bool selected = session.SelectedType == view.Type;
            Button button = view.Kind == ButtonKind.Default ? Ui.Toggle(string.Empty, selected) : Ui.Action(string.Empty, view.Kind);
            button.CustomMinimumSize = new Vector2(0f, HandRowHeightPx);
            button.TooltipText = view.EffectText;
            button.Disabled = !session.CanEdit;
            Color color = Ui.ButtonTextColor(view.Kind, selected);
            var lines = new VBoxContainer();
            lines.AddThemeConstantOverride("separation", UiTheme.Space0);
            lines.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            var columns = new HBoxContainer();
            columns.AddThemeConstantOverride("separation", UiTheme.Space3);
            Label name = Ui.Text(view.NameText, color, UiTheme.Small);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            columns.AddChild(name);
            Label count = Ui.Text(view.CountText, color, UiTheme.Small);
            count.HorizontalAlignment = HorizontalAlignment.Right;
            columns.AddChild(count);
            lines.AddChild(columns);
            Label effect = Ui.Text(view.EffectText, Ui.MutedText, UiTheme.Caption);
            effect.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            lines.AddChild(effect);
            Ui.FillButton(button, lines);

            PieceType type = view.Type;
            button.Pressed += () => SimulTypeSelected?.Invoke(type);
            _handBody.AddChild(button);
        }
    }

    /// <summary>动作区：座位提交状态（阵营徽记 + 名字 + 状态）、失败原因文字（D4）、提交 / 不部署 / 征召 / 清空 / 撤回 / 认输。</summary>
    private void RefreshSimulActions(SimulSession session)
    {
        Ui.Clear(_actionBody);
        _actionBody.AddChild(Ui.Text(SimulTexts.SeatsTitle, Ui.MutedText, UiTheme.Caption));
        foreach ((PlayerId player, PlayerStatus status, bool submitted) in session.Seats)
        {
            FactionStyle faction = FactionTable.For(player);
            var line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            line.AddThemeConstantOverride("separation", UiTheme.Space2);
            var icon = new EmblemIcon { CustomMinimumSize = new Vector2(14f, 14f), MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            icon.Set(faction.Emblem, Visuals.ToColor(faction.Primary));
            line.AddChild(icon);
            Label name = Ui.Text(Labels.Player(player), player == session.Me ? Ui.PanelBorder : Ui.InfoText, UiTheme.Small);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            line.AddChild(name);
            bool active = status == PlayerStatus.Active;
            line.AddChild(Ui.Text(SimulTexts.SeatState(status, submitted), !active ? Ui.MutedText : submitted ? Ui.Accent : Ui.InfoText, UiTheme.Small));
            _actionBody.AddChild(line);
        }

        if (session.IsOver)
        {
            return;
        }

        _actionBody.AddChild(Ui.Separator());

        // 失败原因只显示文字（D4）：取 Core 校验的全部原因，坐标用围棋记法。
        if (session.Check is { IsValid: false } check)
        {
            foreach (SimulBatchIssue issue in check.Issues)
            {
                _actionBody.AddChild(Ui.Text(issue.Message, Ui.DangerText, UiTheme.Small, wrap: true));
            }
        }

        if (session.CanEdit)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 4);
            Button submit = Ui.Action(SimulTexts.Submit, 132);
            Ui.Primary(submit);
            submit.Disabled = session.Check is { IsValid: false };
            submit.Pressed += () => SimulSubmitPressed?.Invoke();
            row.AddChild(submit);
            Button skip = Ui.Action(SimulTexts.Skip, 132);
            skip.Pressed += () => SimulSkipPressed?.Invoke();
            row.AddChild(skip);
            _actionBody.AddChild(row);

            var second = new HBoxContainer();
            second.AddThemeConstantOverride("separation", 4);
            Button recruit = Ui.Action(SimulTexts.OpenRecruit, 92);
            recruit.Pressed += () => SimulRecruitOpened?.Invoke();
            second.AddChild(recruit);
            Button clear = Ui.Action(SimulTexts.Clear, 120);
            clear.Pressed += () => SimulClearPressed?.Invoke();
            second.AddChild(clear);
            _actionBody.AddChild(second);
        }
        else if (session.CanWithdraw)
        {
            Button withdraw = Ui.Action(SimulTexts.Withdraw, 160);
            withdraw.Pressed += () => SimulWithdrawPressed?.Invoke();
            _actionBody.AddChild(withdraw);
        }

        if (session.IsActive)
        {
            Button resign = Ui.Action(SimulTexts.Resign, ButtonKind.Danger, 92);
            resign.Pressed += () => SimulResignPressed?.Invoke();
            _actionBody.AddChild(resign);
        }
    }

    /// <summary>中央面板：终局（D10）&gt; 手牌信息面板 &gt; 征召面板；都不需要时收起。</summary>
    private void RefreshSimulCenter(SimulSession session, ViewerWorld world, HandPanelState handPanel, bool deferResult)
    {
        Ui.Clear(_centerBody);
        _recruitBar.Visible = false;
        if (session.Result is { } result && !deferResult)
        {
            CenterBox(0.5f, 620f, 170f + (34f * result.Placing.Length));
            BuildSimulResult(result, session.Me);
            _centerPanel.Visible = true;
            return;
        }

        if (handPanel.IsOpen)
        {
            HandInfoPanelView handInfo = world.HandPanel();
            CenterBox(0.5f, HandInfoWidthPx, HandInfoHeightOf(handInfo));
            BuildHandInfo(handInfo);
            _centerPanel.Visible = true;
            return;
        }

        if (session.RecruitOpen && session.RecruitPanel is { } panel)
        {
            CenterBox(0.5f, SimulRecruitWidthPx, RecruitPanelHeightOf(panel));
            BuildSimulRecruit(panel, world.OwnHand);
            _centerPanel.Visible = true;
            return;
        }

        _centerPanel.Visible = false;
    }

    /// <summary>终局面板（D10）：原因与回合、胜利名额与胜者、严格名次（种子兜底后的次序）与终局势力。</summary>
    private void BuildSimulResult(SimulResult result, PlayerId me)
    {
        _centerBody.AddChild(Ui.Heading(SimulTexts.ResultTitle));
        _centerBody.AddChild(Ui.Text(SimulTexts.ResultReason(result.Reason, result.Round), Ui.InfoText));
        _centerBody.AddChild(Ui.Text(SimulTexts.WinnerSlots(result.Winners.Length, result.Winners), Ui.PanelBorder, UiTheme.BodyFontPx + 2));
        _centerBody.AddChild(Ui.Separator());
        for (int i = 0; i < result.Placing.Length; i++)
        {
            PlayerId player = result.Placing[i];
            System.Numerics.BigInteger power = result.Of(player).Input.Power;
            _centerBody.AddChild(Ui.Text(SimulTexts.PlacingRow(i + 1, player, result.Winners.Contains(player), power), player == me ? Ui.PanelBorder : Ui.InfoText));
        }
    }

    /// <summary>
    /// 征召面板（仿现有征召面板：头部三行 + 每个候选一个双行按钮 + 底部按钮）。不复用现有构建方法——它的选取事件接的是经典流程；
    /// 文字全部来自 <see cref="HudPanelRows"/> / <see cref="SimulTexts"/>，本方法只摆放。候选是回合开始时预抽好的那一组（K5）。
    /// </summary>
    private void BuildSimulRecruit(RecruitPanelView panel, HandPrivateView ownHand)
    {
        RecruitHeaderView header = HudPanelRows.RecruitHeader(panel);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", UiTheme.Space4);
        Label title = Ui.Heading(SimulTexts.RecruitTitle);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(title);
        head.AddChild(Ui.Text(header.MetaText, Ui.MutedText, UiTheme.Caption));
        _centerBody.AddChild(head);
        _centerBody.AddChild(Ui.Text(header.PicksText, header.PicksExhausted ? Ui.MutedText : Ui.Accent, UiTheme.Title));
        _centerBody.AddChild(Ui.Separator());

        foreach (RecruitCandidateView candidate in panel.Candidates)
        {
            _centerBody.AddChild(SimulRecruitRowButton(HudPanelRows.RecruitRow(candidate, ownHand)));
        }

        _centerBody.AddChild(Ui.Separator());
        Button done = Ui.Action(SimulTexts.CloseRecruit, 300);
        Ui.Primary(done);
        done.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        done.Pressed += () => SimulRecruitClosed?.Invoke();
        _centerBody.AddChild(done);
    }

    /// <summary>征召候选行（仿现有候选行：图标 | 名字 + 手牌情况 / 效果或原因 | 状态角标），选取接新玩法的事件。</summary>
    private Button SimulRecruitRowButton(RecruitRowView row)
    {
        bool picked = row.State == RecruitRowState.Picked;
        bool blocked = row.State == RecruitRowState.Blocked;
        Button button = Ui.Toggle(string.Empty, picked);
        button.CustomMinimumSize = new Vector2(0f, blocked ? RecruitBlockedRowHeightPx : RecruitRowHeightPx);
        button.Disabled = blocked;
        if (picked)
        {
            button.MouseFilter = Control.MouseFilterEnum.Ignore;
        }

        Color ink = blocked ? Ui.MutedText : Ui.ButtonTextColor(ButtonKind.Default, picked);
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", UiTheme.Space4);
        columns.AddChild(PieceIcon.Create(row.Type, ink));

        var lines = new VBoxContainer();
        lines.AddThemeConstantOverride("separation", UiTheme.Space1);
        lines.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        lines.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var first = new HBoxContainer();
        first.AddThemeConstantOverride("separation", UiTheme.Space4);
        Label name = Ui.Text(row.NameText, ink, UiTheme.Body);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        first.AddChild(name);
        Label hand = Ui.Text(row.HandText, blocked ? Ui.MutedText : Ui.InfoText, UiTheme.Caption);
        hand.VerticalAlignment = VerticalAlignment.Bottom;
        first.AddChild(hand);
        lines.AddChild(first);
        lines.AddChild(Ui.Text(row.DetailText, Ui.MutedText, UiTheme.Caption, wrap: true));
        columns.AddChild(lines);

        if (row.StateText is { } stateText)
        {
            Label state = Centered(Ui.Text(stateText, ink, UiTheme.Small));
            state.CustomMinimumSize = new Vector2(64f, 0f);
            state.HorizontalAlignment = HorizontalAlignment.Right;
            columns.AddChild(state);
        }

        Ui.FillButton(button, columns);
        if (row.State == RecruitRowState.Pickable)
        {
            int index = row.Index;
            button.Pressed += () => SimulRecruitPicked?.Invoke(index);
        }

        return button;
    }
}
