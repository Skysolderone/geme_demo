using System;
using System.Linq;
using Godot;
using Siege.Core.Board;
using Siege.Core.Recruit;
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
/// 新玩法（simul-a）的 HUD（A3 裁决 D2：新的分部类，现有 <c>Refresh*</c> 一行不改）。S2 只做最低限度能玩：回合与本机状态、全员提交状态、
/// 失败原因文字（D4）、手牌选类型、征召选牌、提交 / 不部署 / 撤回 / 清空 / 认输、终局结果；排名、个人势力卡片、顺序栏、预演、信息层与演出叠层复用现有面板。
/// 正式的面板布局留给 S3。文案一律经 <see cref="Labels"/> / <see cref="SimulTexts"/>。
/// </summary>
public sealed partial class Hud
{
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

    private void RefreshSimulHand(SimulSession session, ViewerWorld world)
    {
        Ui.Clear(_handBody);
        OwnHandAreaView own = world.HandPanel().Own;
        if (own.Rows.IsDefaultOrEmpty)
        {
            _handBody.AddChild(Ui.Text("手牌为空", Ui.MutedText));
            return;
        }

        foreach (OwnHandRowView row in own.Rows)
        {
            HandRowView view = HudPanelRows.HandRow(row, false);
            bool selected = session.SelectedType == view.Type;
            Button button = Ui.Toggle(SimulTexts.HandRow(view.NameText, row.Count), selected);
            button.TooltipText = view.EffectText;
            button.Disabled = !session.CanEdit;
            PieceType type = view.Type;
            button.Pressed += () => SimulTypeSelected?.Invoke(type);
            _handBody.AddChild(button);
        }
    }

    private void RefreshSimulActions(SimulSession session)
    {
        Ui.Clear(_actionBody);
        foreach (string line in session.SeatLines)
        {
            _actionBody.AddChild(Ui.Text(line, Ui.InfoText, UiTheme.Small));
        }

        if (session.IsOver)
        {
            return;
        }

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

    private void RefreshSimulCenter(SimulSession session, ViewerWorld world, HandPanelState handPanel, bool deferResult)
    {
        Ui.Clear(_centerBody);
        _recruitBar.Visible = false;
        if (session.Result is { } result && !deferResult)
        {
            CenterBox(0.5f, 560f, 120f + (36f * result.Placing.Length));
            _centerBody.AddChild(Ui.Heading(SimulTexts.ResultTitle));
            _centerBody.AddChild(Ui.Text(SimulTexts.ResultReason(result.Reason, result.Round), Ui.InfoText));
            _centerBody.AddChild(Ui.Separator());
            for (int i = 0; i < result.Placing.Length; i++)
            {
                PlayerId player = result.Placing[i];
                _centerBody.AddChild(Ui.Text(SimulTexts.PlacingRow(i + 1, player, result.Winners.Contains(player)), player == session.Me ? Ui.PanelBorder : Ui.InfoText));
            }

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
            CenterBox(0.5f, 520f, 140f + (52f * panel.Candidates.Length));
            _centerBody.AddChild(Ui.Heading(SimulTexts.RecruitTitle));
            _centerBody.AddChild(Ui.Text(HudPanelRows.RecruitHeader(panel).PicksText, Ui.Accent, UiTheme.Small));
            foreach (RecruitCandidateView candidate in panel.Candidates)
            {
                RecruitRowView row = HudPanelRows.RecruitRow(candidate, world.OwnHand);
                Button button = Ui.Toggle(row.NameText, row.State == RecruitRowState.Picked);
                button.TooltipText = row.DetailText;
                button.Disabled = row.State != RecruitRowState.Pickable;
                int index = candidate.Index;
                button.Pressed += () => SimulRecruitPicked?.Invoke(index);
                _centerBody.AddChild(button);
            }

            Button done = Ui.Action(SimulTexts.CloseRecruit, 200);
            Ui.Primary(done);
            done.Pressed += () => SimulRecruitClosed?.Invoke();
            _centerBody.AddChild(done);
            _centerPanel.Visible = true;
            return;
        }

        _centerPanel.Visible = false;
    }
}
