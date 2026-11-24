using System.Collections.Immutable;
using System.Linq;
using Godot;
using Siege.Core.Board;
using Siege.Core.Simul;
using Siege.Presentation.Camera;
using Siege.Presentation.Layers;
using Siege.Presentation.Show;
using Siege.Presentation.Text;
using Siege.Presentation.Visibility;

namespace Siege.Godot;

/// <summary>
/// 新玩法（simul-a，同时决策）在主场景里的接线（A3 裁决 D1：分部类 + 少量分支点）。只接线，规则全部在 Core：
/// 选座复用 3D 座位点选（D6，选座阶段的显示仍由未插旗的 <see cref="MatchSession"/> 预览负责）；准备阶段本机经 <see cref="SimulSession"/> 操作；
/// 本机提交后 AI 每帧一名（D7）；全员提交即揭晓，演出复用 <c>BeginShow</c> / <c>AdvanceShow</c> / <c>FinishShow</c>。
/// 无人值守探针 <c>--auto-demo --simul</c>（D8）：本机也由贪心 AI 代打，跑满 <c>--rounds</c> 个回合后断言回合数与报告数，打 <c>[simul]</c> 行、经 <c>QuitChecked</c> 退出。
/// </summary>
public sealed partial class GameRoot
{
    /// <summary>新玩法模式（新局面板选"新玩法"或命令行 <c>--simul</c>）。为真时 <c>_Process</c> 走 <see cref="DriveSimul"/>。</summary>
    private bool _simulMode;

    /// <summary>新玩法对局；选座之前为 <c>null</c>。</summary>
    private SimulSession? _simul;

    /// <summary>开局时的回合号（探针断言用）。</summary>
    private int _simulStartRound;

    /// <summary>探针已收尾（只收尾一次）。</summary>
    private bool _simulProbeDone;

    /// <summary>当前观察者世界：新玩法开局后取新玩法会话的，否则取现有会话的。</summary>
    private ViewerWorld CurrentWorld => _simul?.World ?? _session.World;

    /// <summary>当前地图：新玩法开局后取新玩法对局的，否则取现有对局的。</summary>
    private MapData CurrentMap => _simul?.Map ?? _session.Match.Map;

    /// <summary>当前回合（大回合）号。</summary>
    private int CurrentRound => _simul?.Round ?? _session.Match.MajorRound;

    /// <summary>镜头跟随的"对手行动中"判定：新玩法全员同时准备，没有"对手行动中"。</summary>
    private bool MyTurnNow => _simul is not null || _session.IsMyTurn;

    /// <summary>新玩法的 HUD 事件（只在新玩法开局后生效）。</summary>
    private void ConnectSimul()
    {
        _hud.SimulTypeSelected += type => SimulDo(s => s.SelectedType = type);
        _hud.SimulRecruitOpened += () => SimulDo(s => s.OpenRecruit());
        _hud.SimulRecruitPicked += index => SimulDo(s => s.Pick(index));
        _hud.SimulRecruitClosed += () => SimulDo(s => s.CloseRecruit());
        _hud.SimulSubmitPressed += () => SimulDo(s => s.Submit());
        _hud.SimulSkipPressed += () => SimulDo(s => s.SubmitEmpty());
        _hud.SimulWithdrawPressed += () => SimulDo(s => s.Withdraw());
        _hud.SimulClearPressed += () => SimulDo(s => s.Clear());
        _hud.SimulResignPressed += () => SimulDo(s => s.Resign());
    }

    private void SimulDo(System.Action<SimulSession> action)
    {
        if (_simul is null || ShowPlaying)
        {
            return;
        }

        action(_simul);
        _dirty = true;
    }

    /// <summary>选座之后开局（D6）：本机选的座位 + 其余玩家盲选，重搭场景、开局对准自己的出生岛。</summary>
    private void StartSimul(int zone)
    {
        _simul = SimulSession.Start(CurrentMap, _matchSeed, zone);
        _simulStartRound = _simul.Round;
        _board.SetFlagPreview(null, false);
        _board.Build(_simul.World.Board(), _simul.ZoneOwners);
        FocusHome(opening: true);
        _dirty = true;
        GD.Print($"[simul] 新玩法开局：地图 {CurrentMap.Id}，对局种子 {_matchSeed}，你是 {Labels.Player(_simul.Me)}，出生座位 {BirthZoneLabel.Of(zone)}，结算顺位 {string.Join(" > ", _simul.World.Public.View.ActionOrder.Select(Labels.Player))}");
    }

    /// <summary>新玩法的每帧推进（取代 <c>Drive</c>）：演出中不推进；本机准备中等待（无人值守由 AI 代打）；本机完成后 AI 每帧一名；全员提交即揭晓。</summary>
    private void DriveSimul(double delta)
    {
        if (!_show.IsFinished)
        {
            if (!_follow.IsTravelling)
            {
                AdvanceShow(delta);
            }

            return;
        }

        if (_simul is null)
        {
            if (_autoDemo)
            {
                StartSimul(AutoZone - 1);
            }

            return;
        }

        if (_autoDemo && (_simul.IsOver || (_rounds > 0 && _simul.Reports.Count >= _rounds)))
        {
            FinishSimulProbe();
            return;
        }

        if (_simul.IsOver)
        {
            return;
        }

        if (!_simul.MyPrepDone)
        {
            if (_autoDemo)
            {
                _simul.AutoPlayMe();
                _dirty = true;
            }

            return;
        }

        _aiTimer -= delta;
        if (_aiTimer > 0d)
        {
            return;
        }

        if (_simul.NextAi() is { } ai)
        {
            _simul.PlayAi(ai);
            _aiTimer = _pause;
            _dirty = true;
            return;
        }

        if (!_simul.ReadyToResolve)
        {
            return;
        }

        (SimulRoundReport report, ImmutableArray<SettlementBeat> beats) = _simul.Resolve();
        GD.Print($"[simul] 第 {report.Round} 回合揭晓：部署 {report.SubmittedCount}、落下 {report.Landed.Length}、冲突 {report.Conflicts.Length}、掷骰 {report.DiceRolled}、"
            + $"移除 {report.Captures.Sum(s => s.Removed.Length)}；势力 {string.Join(" ", report.Power.Players.Select(p => $"{Labels.Player(p.Player)}={p.Total}"))}"
            + (report.EndReason is { } end ? $"；终局：{Labels.SimulEndReason(end)}" : string.Empty));
        foreach (SimulConflict conflict in report.Conflicts)
        {
            GD.Print($"[simul] {Labels.SimulConflictBanner(conflict)}");
        }

        TurnFlash flash = TurnFlash.Of(beats);
        if (Unattended)
        {
            BeginShow(beats, ShowDuration.Zero, "新玩法");
            _flash = flash;
            _flashTimer = FlashSeconds;
        }
        else
        {
            _pendingFlash = flash;
            BeginShow(beats, ShowDuration.Normal, "新玩法");
        }

        _aiTimer = 0d;
        _dirty = true;
    }

    /// <summary>新玩法的整页刷新（取代 <c>RefreshViews</c> 在新玩法开局之后的部分）。</summary>
    private void RefreshViewsSimul()
    {
        SimulSession simul = _simul!;
        ShowMask mask = ShownMask();
        ViewerWorld world = simul.World;
        _board.Refresh(world, _layers.Active, _layers.Reading, _layers.Treatment, LibertyThresholds.Default, _flash, _hover, mask);
        _hud.SetHoverReadout(HoverReadout.Of(_hover, world.Board()));
        _minimap.Visible = false;
        _hud.OverviewActive = _board.Rig.IsOverview;
        _hud.FollowActive = _follow.Enabled;
        _hud.OverviewAvailable = _board.Rig.OverviewAvailable;
        bool aiThinking = !simul.IsOver && simul.MyPrepDone && !simul.ReadyToResolve;
        _hud.RefreshSimul(simul, _layers, _handPanel, mask, deferResult: ShowPlaying, aiThinking);
    }

    /// <summary>新玩法下点格：选座阶段点带编号的出生座位即开局（D6）；开局后切换本机暂放。演出中不接受。</summary>
    private void OnBoardClickedSimul(Coord coord)
    {
        if (_simul is null)
        {
            if (_session.BirthZoneAt(coord) is int zone)
            {
                StartSimul(zone);
            }

            return;
        }

        if (!ShowPlaying)
        {
            _simul.ToggleStage(coord);
            _dirty = true;
        }
    }

    /// <summary>
    /// 新玩法的按键（D13）：Enter 提交、P 本回合不部署、Backspace 撤回提交、C 清空暂放、右键撤回一枚、E 换改造目标。
    /// 返回是否已处理；其余按键（信息层、镜头、手牌面板等）照常走现有处理。
    /// </summary>
    private bool HandleSimulInput(InputEvent @event)
    {
        if (_simul is null || ShowPlaying)
        {
            return false;
        }

        if (@event.IsActionPressed(InputBindings.ConfirmAction))
        {
            _simul.Submit();
        }
        else if (@event.IsActionPressed(InputBindings.PassAction))
        {
            _simul.SubmitEmpty();
        }
        else if (@event.IsActionPressed(InputBindings.CycleEditAction))
        {
            _simul.CycleEdit(_hover);
        }
        else if (@event.IsActionPressed(InputBindings.UnstageAction) && PickCell() is { } unstage)
        {
            _simul.Unstage(unstage);
        }
        else if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Backspace })
        {
            _simul.Withdraw();
        }
        else if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.C })
        {
            _simul.Clear();
        }
        else
        {
            return false;
        }

        _dirty = true;
        return true;
    }

    /// <summary>
    /// 无人值守探针收尾（D8）：断言"回合号 = 开局回合 + 已结算回合数"（终局那一回合之后不再开新回合，相应减一）且已结算回合数 = <c>--rounds</c>（0 = 跑到终局，不比这一项），
    /// 打 <c>[simul]</c> 行，经 <c>QuitChecked</c> 退出。
    /// </summary>
    private void FinishSimulProbe()
    {
        if (_simulProbeDone || _simul is null)
        {
            return;
        }

        _simulProbeDone = true;
        int done = _simul.Reports.Count;
        int expectedRound = _simulStartRound + done - (_simul.IsOver ? 1 : 0);
        bool roundsOk = _rounds == 0 ? _simul.IsOver : done == _rounds;
        bool ok = _simul.Round == expectedRound && roundsOk && done > 0;
        GD.Print($"[simul] 探针：开局第 {_simulStartRound} 回合，已结算 {done} 回合（--rounds={_rounds}），现为第 {_simul.Round} 回合"
            + (_simul.Result is { } result ? $"，终局：{Labels.SimulEndReason(result.Reason)}，胜者 {string.Join("、", result.Winners.Select(Labels.Player))}" : "，未终局")
            + $"；{(ok ? "通过" : "失败")}");
        SetProcess(false);
        QuitChecked(ok ? 0 : 1);
    }
}
