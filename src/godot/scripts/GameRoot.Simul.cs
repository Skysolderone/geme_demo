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
/// 选座复用 3D 座位点选（D6，选座阶段的显示仍由未插旗的 <see cref="MatchSession"/> 预览负责，中央说明换成新玩法版）；准备阶段本机经 <see cref="SimulSession"/> 操作，
/// 可部署格铺色（D5）；本机提交后 AI 每帧一名（D7）；全员提交即揭晓，演出复用 <c>BeginShow</c> / <c>AdvanceShow</c> / <c>FinishShow</c>。
/// </summary>
/// <remarks>
/// 命令行（都经 <see cref="LaunchArgs"/> 严格解析）：
/// <list type="bullet">
/// <item><c>--simul</c> + <c>--auto-demo</c>：无人值守探针（D8），本机也由贪心 AI 代打，跑满 <c>--rounds</c> 个回合后断言回合数与报告数，打 <c>[simul]</c> 行、经 <c>QuitChecked</c> 退出。</item>
/// <item><c>--simul</c> + <c>--map-select</c>：新局面板预选"新玩法"（截图与直达用）。</item>
/// <item><c>--simul-hold=N</c>（须与 <c>--simul --auto-demo --screenshot=</c> 同用）：第 N 回合本机暂放一批后停住、不提交，定帧截图取"准备阶段"。</item>
/// <item><c>--shot-show=conflict</c>（须与 <c>--simul --auto-demo --screenshot=</c> 同用）：等到含冲突的揭晓，按正常时长播演出，冲突节拍进行中取图。</item>
/// <item><c>--simul-check</c> + <c>--map=</c>：有人值守路径的自检——不开无人值守（AI 之间照常停顿、演出照常时长），用点格入口与真实按键事件（<c>Input.ParseInputEvent</c>）
/// 走"选座 → 点格暂放 → C 清空 → 再暂放 → Enter 提交 → Backspace 撤回 → Enter 再提交 → AI 依次提交 → 揭晓演出 → 下一回合"，打 <c>[simul-check]</c> 行后退出。</item>
/// </list>
/// </remarks>
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

    /// <summary><c>--simul-hold=N</c>：第 N 回合停在准备阶段（0 = 不停）。</summary>
    private int _simulHold;

    /// <summary>已停在 <see cref="_simulHold"/> 回合的准备阶段。</summary>
    private bool _simulHeld;

    /// <summary><c>--simul-check</c>：有人值守路径自检。</summary>
    private bool _simulCheck;

    private int _simulCheckStep;
    private int _simulCheckWait;
    private int _simulCheckFrames;
    private int _simulCheckFailures;
    private Coord? _simulCheckCell;
    private bool _simulCheckSawAiWait;
    private int _simulLastShowMs;

    /// <summary>当前观察者世界：新玩法开局后取新玩法会话的，否则取现有会话的。</summary>
    private ViewerWorld CurrentWorld => _simul?.World ?? _session.World;

    /// <summary>当前地图：新玩法开局后取新玩法对局的，否则取现有对局的。</summary>
    private MapData CurrentMap => _simul?.Map ?? _session.Match.Map;

    /// <summary>当前回合（大回合）号。</summary>
    private int CurrentRound => _simul?.Round ?? _session.Match.MajorRound;

    /// <summary>镜头跟随的"对手行动中"判定：新玩法全员同时准备，没有"对手行动中"。</summary>
    private bool MyTurnNow => _simul is not null || _session.IsMyTurn;

    /// <summary>定帧截图是否可以取（只有 <c>--simul-hold</c> 要等停到准备阶段；其余一律可以）。</summary>
    private bool SimulShotReady() => _simulHold == 0 || _simulHeld;

    /// <summary>读新玩法的命令行选项（在结算 <c>EnsureRecognized</c> 之前）。</summary>
    private void ReadSimulArgs(LaunchArgs args)
    {
        _simulMode = args.Flag("simul");
        _simulHold = args.Value<int>("simul-hold", "正整数（新玩法：第几回合停在准备阶段截图）", t => int.TryParse(t, out int v) && v > 0 ? v : null) ?? 0;
        _simulCheck = args.Flag("simul-check");
    }

    /// <summary>新玩法选项的组合校验（结算之后、建局之前）：不合法即抛 <see cref="System.FormatException"/>，由 <c>_Ready</c> 以退出码 1 结束。</summary>
    private void CheckSimulArgs(bool mapSelect, string? mapId)
    {
        if (_simulCheck)
        {
            if (_simulMode || _autoDemo || _pickCheck || mapSelect || _screenshotFrame >= 0 || mapId is null)
            {
                throw new System.FormatException("--simul-check 须单独与 --map=<小图标识> 同用（不与 --simul / --auto-demo / --pick-check / --map-select / --screenshot= 同用）。");
            }

            _simulMode = true;
            return;
        }

        if (_simulMode && !_autoDemo && !mapSelect)
        {
            throw new System.FormatException("--simul 须与 --auto-demo（无人值守探针）或 --map-select（新局面板预选新玩法）同用。");
        }

        if (_simulMode && _autoDemo && mapSelect)
        {
            throw new System.FormatException("--simul 不与 --map-select --auto-demo 同用（新局界面的自检按经典口径走）。");
        }

        if (_simulHold > 0 && (!_simulMode || !_autoDemo || _screenshotFrame < 0 || _shotShow is not null))
        {
            throw new System.FormatException("--simul-hold= 须与 --simul --auto-demo --screenshot= 同用，且不与 --shot-show= 同用。");
        }

        if (_shotShow == "conflict" && !(_simulMode && _autoDemo))
        {
            throw new System.FormatException("--shot-show=conflict 只在 --simul --auto-demo 下有（新玩法的冲突节拍）。");
        }
    }

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

        if (_simulCheck && StepSimulCheck())
        {
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
                if (_simulHold > 0 && _simul.Round >= _simulHold)
                {
                    // --simul-hold：停在本回合的准备阶段（有暂放、未提交），等定帧截图。
                    if (!_simulHeld)
                    {
                        _simul.AutoStageMe();
                        _simulHeld = true;
                        _dirty = true;
                        GD.Print($"[simul] 第 {_simul.Round} 回合停在准备阶段：暂放 {_simul.Staged.Length} 个，可部署格 {_simul.DeployableCells.Length} 格（--simul-hold 截图）");
                    }

                    return;
                }

                _simul.AutoPlayMe();
                _dirty = true;
            }

            return;
        }

        _aiTimer -= delta;
        if (_aiTimer > 0d)
        {
            _simulCheckSawAiWait |= _simulCheck;
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
        if (Unattended && !ShotShowWants(beats))
        {
            // 无人值守：演出零时长（与经典的无人值守口径相同）；--shot-show=conflict 等到含冲突的那次揭晓才按正常时长播。
            BeginShow(beats, ShowDuration.Zero, "新玩法");
            _flash = flash;
            _flashTimer = FlashSeconds;
        }
        else
        {
            _pendingFlash = flash;
            BeginShow(beats, ShowDuration.Normal, "新玩法");
        }

        _simulLastShowMs = _show.TotalDurationMs;
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
        // 可部署格铺色（D5）：只在本机准备中、未提交、不在演出中时铺；数据只来自 Core。
        _board.SetDeployable(simul.CanEdit && !ShowPlaying ? simul.DeployableCells : []);
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
            + (_shotShow == "conflict" ? "（--shot-show=conflict：跑满仍没有含冲突的揭晓，未取图）" : string.Empty)
            + $"；{(ok && _shotShow != "conflict" ? "通过" : "失败")}");
        SetProcess(false);
        QuitChecked(ok && _shotShow != "conflict" ? 0 : 1);
    }

    // ---------- --simul-check：有人值守路径自检 ----------

    /// <summary>自检的一步（每帧至多一步）；返回 <c>true</c> 即本帧已由自检消耗，不再推进对局。等待 AI 与揭晓的那一段返回 <c>false</c>，走正常的有人值守推进。</summary>
    private bool StepSimulCheck()
    {
        const int Budget = 20_000;
        if (++_simulCheckFrames > Budget)
        {
            SimulCheckLine(false, $"{Budget} 帧内没有走完");
            FinishSimulCheck();
            return true;
        }

        if (_simulCheckWait > 0)
        {
            _simulCheckWait--;
            return true;
        }

        if (_simul is null)
        {
            int zone = AutoZone - 1;
            Coord seat = CurrentMap.BirthBoard(zone).Cells().First(c => _session.BirthZoneAt(c) == zone);
            OnBoardClicked(seat);
            SimulCheckLine(_simul is not null, $"点出生座位 {seat.ToNotation()} 开局");
            _simulCheckWait = 2;
            return true;
        }

        SimulSession s = _simul;
        switch (_simulCheckStep)
        {
            case 0:
                ImmutableArray<Coord> cells = s.DeployableCells;
                _simulCheckCell = cells[cells.Length / 2];
                OnBoardClicked(_simulCheckCell.Value);
                SimulCheckLine(s.Staged.Any(p => p.Coord == _simulCheckCell), $"点格 {_simulCheckCell.Value.ToNotation()} 暂放（可部署格 {cells.Length} 格）");
                break;
            case 1:
                SimulCheckLine(_board.DeployableShown > 0, $"可部署格铺色 {_board.DeployableShown} 格");
                Press(new InputEventKey { PhysicalKeycode = Key.C, Keycode = Key.C, Pressed = true });
                break;
            case 2:
                SimulCheckLine(s.Staged.IsEmpty && s.CanEdit, "C 清空暂放");
                OnBoardClicked(_simulCheckCell!.Value);
                break;
            case 3:
                SimulCheckLine(s.Staged.Length == 1, "再次点格暂放");
                Press(new InputEventKey { PhysicalKeycode = Key.Enter, Keycode = Key.Enter, Pressed = true });
                break;
            case 4:
                SimulCheckLine(s.MyPrepDone && s.CanWithdraw && _board.DeployableShown == 0, "Enter 提交（提交后可部署格铺色收起）");
                Press(new InputEventKey { PhysicalKeycode = Key.Backspace, Keycode = Key.Backspace, Pressed = true });
                break;
            case 5:
                SimulCheckLine(s.CanEdit && s.Staged.Length == 1, "Backspace 撤回提交（暂放保留）");
                Press(new InputEventKey { PhysicalKeycode = Key.Enter, Keycode = Key.Enter, Pressed = true });
                break;
            case 6:
                SimulCheckLine(s.MyPrepDone, "Enter 再次提交");
                _simulCheckStep++;
                return false;
            default:
                if (s.Reports.Count == 0)
                {
                    return false;   // 等 AI 依次提交、揭晓与演出（有人值守口径）
                }

                SimulRoundReport report = s.Reports[0];
                bool mine = report.Batches.Single(b => b.Player == s.Me).Placements.Any(p => p.Coord == _simulCheckCell);
                SimulCheckLine(mine, $"揭晓：本机批次含 {_simulCheckCell!.Value.ToNotation()}");
                SimulCheckLine(_simulCheckSawAiWait, "AI 之间有停顿（有人值守口径）");
                SimulCheckLine(_simulLastShowMs > 0, $"揭晓演出按正常时长播放（{_simulLastShowMs} ms）并播完");
                SimulCheckLine(s.Round == _simulStartRound + 1 && s.CanEdit && _board.DeployableShown > 0, $"进入第 {s.Round} 回合准备，可部署格重新铺色 {_board.DeployableShown} 格");
                FinishSimulCheck();
                return true;
        }

        _simulCheckStep++;
        _simulCheckWait = 2;   // 留两帧给按键事件派发与整页刷新，再核对
        return true;
    }

    /// <summary>经引擎的输入派发送一个按键事件（走 <c>_Input</c> / <c>_UnhandledInput</c> 的真实路径）。</summary>
    private static void Press(InputEvent @event) => Input.ParseInputEvent(@event);

    private void SimulCheckLine(bool ok, string what)
    {
        _simulCheckFailures += ok ? 0 : 1;
        GD.Print($"[simul-check] 第 {_frame} 帧 {what}：{(ok ? "通过" : "失败")}");
    }

    private void FinishSimulCheck()
    {
        GD.Print($"[simul-check] 有人值守路径自检：失败 {_simulCheckFailures} 条；{(_simulCheckFailures == 0 ? "通过" : "失败")}");
        SetProcess(false);
        QuitChecked(_simulCheckFailures == 0 ? 0 : 1);
    }
}
