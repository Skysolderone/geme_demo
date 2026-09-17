using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Presentation.Hand;
using Siege.Presentation.Layers;
using Siege.Presentation.Text;

namespace Siege.Godot;

/// <summary>
/// 主场景脚本：搭 3D 棋盘与 HUD、接输入、按 tactical-ui 裁决 11 在<b>主线程</b>推进对局。
/// </summary>
/// <remarks>
/// <para>AI 回合之间用 <c>_Process</c> 的计时器留出可见停顿，不开线程、不 <c>Sleep</c>。</para>
/// <para>无人值守自检：命令行传 <c>-- --auto-demo</c> 时自动插旗、每回合自动 Pass，跑到终局打印一行结果后退出；
/// 传 <c>-- --pick-check</c> 时对每个可落子格做"格心投影到屏幕再分层拾取回同一格"的往返检查（terrain-model 6.1），全过退出码 0，否则 1。</para>
/// </remarks>
public sealed partial class GameRoot : Node3D
{
    private const double AiPauseSeconds = 0.6;
    private const double FlashSeconds = 1.2;

    private readonly TacticalLayerState _layers = new();
    private readonly HandPanelState _handPanel = new();
    private readonly InputBindings _bindings = new();
    private MatchSession _session = null!;
    private BoardView _board = null!;
    private Hud _hud = null!;
    private TurnFlash _flash = TurnFlash.None;
    private double _aiTimer;
    private double _flashTimer;
    private double _pause = AiPauseSeconds;
    private int _demoStep;
    private int _frame;
    private int _screenshotFrame = -1;
    private string _screenshotPath = string.Empty;
    private bool _autoDemo;
    private bool _pickCheck;
    private bool _dirty = true;
    private Coord? _hover;

    /// <inheritdoc/>
    public override void _Ready()
    {
        List<string> args = [.. OS.GetCmdlineUserArgs(), .. OS.GetCmdlineArgs()];
        _autoDemo = args.Contains("--auto-demo");
        _pickCheck = args.Contains("--pick-check");
        ulong seed = ReadSeed(args) ?? (_autoDemo ? 20260915UL : (ulong)Stopwatch.GetTimestamp());
        int rounds = _autoDemo ? 4 : MatchOptions.DefaultMaxMajorRounds;
        _pause = _autoDemo ? 0d : AiPauseSeconds;

        ReadScreenshotArg(args);
        _bindings.Install();
        _session = MatchSession.Create(seed, 4, 1, AiDifficulty.Standard, rounds);

        _board = new BoardView { Name = "Board" };
        AddChild(_board);
        _board.Build(_session.World.Board(), _session.ZoneOwners);

        _hud = new Hud { Name = "Hud" };
        AddChild(_hud);
        _hud.Build();
        Connect();

        GD.Print($"[siege] 种子 {seed}，你是 {Labels.Player(_session.Me)}，大回合上限 {rounds}{(_autoDemo ? "，自动演示模式" : string.Empty)}");
    }

    private static ulong? ReadSeed(IReadOnlyList<string> args)
    {
        foreach (string arg in args)
        {
            if (arg.StartsWith("--seed=", System.StringComparison.Ordinal) && ulong.TryParse(arg[7..], out ulong seed))
            {
                return seed;
            }
        }

        return null;
    }

    /// <summary>
    /// <c>--screenshot=&lt;路径&gt;[:第几帧]</c>：跑到指定帧存一张 PNG 后退出。
    /// 裁决 7 的人工检查清单要的就是截图，这里给一条不用手忙脚乱按键的路径；运行中随时按 F12 也能存一张。
    /// </summary>
    private void ReadScreenshotArg(IEnumerable<string> args)
    {
        foreach (string arg in args)
        {
            if (!arg.StartsWith("--screenshot=", System.StringComparison.Ordinal))
            {
                continue;
            }

            string value = arg["--screenshot=".Length..];
            int split = value.LastIndexOf(':');
            if (split > 2 && int.TryParse(value[(split + 1)..], out int frame))
            {
                _screenshotPath = value[..split];
                _screenshotFrame = frame;
            }
            else
            {
                _screenshotPath = value;
                _screenshotFrame = 60;
            }

            return;
        }
    }

    /// <summary>把当前画面存成 PNG。无头模式下没有可截取的画面，直接跳过（不去碰 <c>GetViewport().GetTexture()</c>）。</summary>
    private void Capture(string path)
    {
        if (DisplayServer.GetName() == "headless")
        {
            GD.Print("[siege] 无头模式没有可截取的画面，已跳过截图。");
            return;
        }

        Image image = GetViewport().GetTexture().GetImage();
        Error error = image.SavePng(path);
        GD.Print($"[siege] 截图 {path}：{error}（{image.GetWidth()}×{image.GetHeight()}，第 {_frame} 帧，第 {_session.Match.MajorRound} 大回合，信息层 {(_layers.Active is { } layer ? Names.Layer(layer) : "关")}）");
        // 截图当刻的据点状态（读视图模型），供 art/sites-v4/README 的人工清单对照"哪张图里有争议 / 控制"。
        GD.Print("[siege] 据点：" + string.Join("；", _session.World.Board().Sites.Select(s => $"{s.Coord.ToNotation()} {s.TierText} {s.StatusText}")));
    }

    private void Connect()
    {
        _hud.HandTypeSelected += type =>
        {
            _session.SelectedType = type;
            _dirty = true;
        };
        _hud.DiscardRequested += type =>
        {
            _session.Discard(type);
            _dirty = true;
        };
        _hud.RecruitPicked += index =>
        {
            _session.Pick(index);
            _dirty = true;
        };
        _hud.RecruitFinished += () =>
        {
            _session.EnterDeploy();
            _dirty = true;
        };
        _hud.ConfirmPressed += () =>
        {
            _session.Confirm();
            _dirty = true;
        };
        _hud.PassPressed += () =>
        {
            _session.Pass();
            _dirty = true;
        };
        _hud.ClearPressed += () =>
        {
            _session.ClearBatch();
            _dirty = true;
        };
        _hud.HandPanelPressed += () =>
        {
            _handPanel.ClickButton();
            _dirty = true;
        };
        _hud.LayerModePressed += () =>
        {
            _layers.SetMode(_layers.Mode == LayerInputMode.HoldToShow ? LayerInputMode.ClickToToggle : LayerInputMode.HoldToShow);
            _dirty = true;
        };
        _hud.LayerPressed += ToggleLayer;
        _hud.ReadingPressed += CycleReading;
    }

    /// <summary>按钮点选信息层：与按键走同一个可见性状态机（D5），只是进入 / 退出的触发条件不同——判断本身在状态机里，这里不复写。</summary>
    private void ToggleLayer(TacticalLayer layer)
    {
        _layers.Toggle(layer);
        _dirty = true;
    }

    /// <summary>切换盘面层的读法（Tab 或按钮）。不改变任何层的可见性——读法是盘面层内部的维度（merge-board-layer D3）。</summary>
    private void CycleReading()
    {
        _layers.CycleReading();
        _dirty = true;
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (_flashTimer > 0d)
        {
            _flashTimer -= delta;
            if (_flashTimer <= 0d)
            {
                _flash = TurnFlash.None;
                _dirty = true;
            }
        }

        Drive(delta);
        UpdateHover();

        if (_dirty)
        {
            _dirty = false;
            _board.Refresh(_session.World, _layers.Active, _layers.Reading, _layers.Treatment, LibertyThresholds.Default, _flash);
            _hud.Refresh(_session, _layers, _handPanel);
        }

        _frame++;
        if (_pickCheck && _frame >= 2)
        {
            // 等到相机与视口都就绪的第 2 帧再投影，_Ready 里视口尺寸可能还没定。
            _pickCheck = false;
            GetTree().Quit(RunPickCheck() ? 0 : 1);
            return;
        }

        if (_screenshotFrame >= 0 && _frame >= _screenshotFrame)
        {
            _screenshotFrame = -1;
            Capture(_screenshotPath);
            GetTree().Quit(0);
        }
    }

    /// <summary>
    /// 分层拾取往返检查：对视图模型里每个可落子格，把它（含高度）的格心投影到屏幕，再用 <see cref="BoardGeometry.TryPick"/> 拾取，
    /// 必须回到同一格。高台边缘格是最容易错的地方——射线若先落到身后低地格就会在这里暴露。只用视图模型与 BoardGeometry，不读地图。
    /// </summary>
    private bool RunPickCheck()
    {
        int width = _session.Match.Map.Width;
        int height = _session.Match.Map.Height;
        var failures = new List<string>();
        int total = 0;
        foreach (var cell in _session.World.Board().Cells)
        {
            if (cell.Terrain != Terrain.Playable)
            {
                continue;
            }

            total++;
            Vector2 screen = _board.Camera.UnprojectPosition(_board.CenterOf(cell.Coord));
            bool hit = BoardGeometry.TryPick(_board.Camera, screen, width, height, _board.LevelOf, out Coord picked);
            if (!hit || picked != cell.Coord)
            {
                failures.Add($"{cell.Coord.ToNotation()}(h{cell.Height}) → {(hit ? picked.ToNotation() : "未命中")}");
            }
        }

        GD.Print($"[pick-check] 可落子格 {total}，往返一致 {total - failures.Count}，失败 {failures.Count}{(failures.Count == 0 ? string.Empty : "：" + string.Join("、", failures))}");
        return failures.Count == 0;
    }

    private void Drive(double delta)
    {
        if (_session.IsOver)
        {
            FinishAutoDemo();
            return;
        }

        if (_session.AwaitingZone)
        {
            if (_autoDemo)
            {
                _session.ChooseZone(0);
                _board.Build(_session.World.Board(), _session.ZoneOwners);
                _dirty = true;
            }

            return;
        }

        if (_session.IsMyTurn)
        {
            if (_autoDemo)
            {
                AutoStep();
                _dirty = true;
                return;
            }

            TurnStage before = _session.Match.Stage;
            _session.AdvanceHumanStages();
            if (_session.Match.Stage != before)
            {
                _dirty = true;
            }

            return;
        }

        // AI 回合：每回合之间留可见停顿，逐个小回合推进，高亮它的落子与被提子。
        _aiTimer -= delta;
        if (_aiTimer > 0d)
        {
            return;
        }

        _flash = _session.RunAiTurn();
        _flashTimer = FlashSeconds;
        _aiTimer = _pause;
        _dirty = true;
    }

    /// <summary>
    /// 无人值守演示的单帧步进：每帧只走一步，让 HUD 在每个阶段都真正渲染一次
    /// （征募面板 → 暂放与批次预演 → 逐个打开五个信息层 → 确认或 Pass），
    /// 这样 <c>--headless</c> 也能验证整条对局链路与全部面板不崩。
    /// </summary>
    private void AutoStep()
    {
        switch (_session.Match.Stage)
        {
            case TurnStage.Idle:
            case TurnStage.OrganizeHand:
                _session.AutoAdvanceToRecruit();
                _demoStep = 0;
                break;

            case TurnStage.Recruit:
                if (_demoStep++ == 0)
                {
                    break;
                }

                _session.EnterDeploy();
                _demoStep = 0;
                break;

            case TurnStage.Deploy:
                AutoDeployStep();
                break;

            default:
                break;
        }
    }

    private void AutoDeployStep()
    {
        if (_demoStep == 0)
        {
            _session.SelectedType ??= _session.World.OwnHand.Types.Cast<PieceType?>().FirstOrDefault();
            _session.StageFirstLegal();
            _demoStep++;
            return;
        }

        TacticalLayer[] all = System.Enum.GetValues<TacticalLayer>();
        if (_demoStep <= all.Length)
        {
            _layers.Press(all[_demoStep - 1]);
            _demoStep++;
            return;
        }

        if (_demoStep == all.Length + 1)
        {
            _layers.Back();
            _handPanel.ClickButton();
            _demoStep++;
            return;
        }

        _handPanel.Back();
        if (_session.World.Preview() is { CanConfirm: true, DeployUsed: > 0 })
        {
            _session.Confirm();
        }
        else
        {
            _session.Pass();
        }

        _demoStep = 0;
    }

    private void FinishAutoDemo()
    {
        if (!_autoDemo)
        {
            return;
        }

        _autoDemo = false;
        MatchResult? result = _session.Match.Result;
        string standings = result is null
            ? "无名次"
            : string.Join("、", result.Standings.Select(s => $"第{s.Rank}名 {Labels.Player(s.Player)} 势力 {s.Input.Power}"));
        GD.Print($"[auto-demo] 终局：第 {result?.MajorRound} 大回合，{(result is null ? "未知" : Names.End(result.Reason))}；{standings}");
        if (_screenshotFrame >= 0)
        {
            // 还等着截图：留在结算画面，由 --screenshot 的帧号或 --quit-after 决定何时退出，
            // 否则终局画面永远截不到（自检时最该看的就是它）。
            return;
        }

        GetTree().Quit(0);
    }

    private void UpdateHover()
    {
        if (DisplayServer.GetName() == "headless")
        {
            return;
        }

        Vector2 mouse = GetViewport().GetMousePosition();
        Coord? hover = BoardGeometry.TryPick(_board.Camera, mouse, _session.Match.Map.Width, _session.Match.Map.Height, _board.LevelOf, out Coord coord)
            ? coord
            : null;
        if (hover != _hover)
        {
            _hover = hover;
            _board.SetCursor(hover);
        }
    }

    /// <inheritdoc/>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is null)
        {
            return;
        }

        if (@event is InputEventKey { Keycode: Key.F12, Pressed: true })
        {
            Capture($"user://siege-{System.DateTime.Now:yyyyMMdd-HHmmss}.png");
            return;
        }

        // 信息层：键鼠与手柄两套独立动作，解析全部交给 Presentation 的键位表。
        foreach ((string action, InputDevice device, string binding) in _bindings.LayerActions)
        {
            if (@event.IsActionPressed(action))
            {
                if (_bindings.Layers.Resolve(device, binding) is { } layer)
                {
                    _layers.Press(layer);
                    _dirty = true;
                }
            }
            else if (@event.IsActionReleased(action))
            {
                if (_bindings.Layers.Resolve(device, binding) is { } layer)
                {
                    _layers.Release(layer);
                    _dirty = true;
                }
            }
        }

        if (@event.IsActionPressed(InputBindings.CycleReadingAction))
        {
            CycleReading();
        }
        else if (@event.IsActionPressed(InputBindings.HandPanelAction))
        {
            _handPanel.ClickButton();
            _dirty = true;
        }
        else if (@event.IsActionPressed(InputBindings.BackAction))
        {
            _layers.Back();
            _handPanel.Back();
            _dirty = true;
        }
        else if (@event.IsActionPressed(InputBindings.ToggleModeAction))
        {
            _layers.SetMode(_layers.Mode == LayerInputMode.HoldToShow ? LayerInputMode.ClickToToggle : LayerInputMode.HoldToShow);
            _dirty = true;
        }
        else if (@event.IsActionPressed(InputBindings.ConfirmAction))
        {
            _session.Confirm();
            _dirty = true;
        }
        else if (@event.IsActionPressed(InputBindings.PassAction))
        {
            _session.Pass();
            _dirty = true;
        }
        else if (@event.IsActionPressed(InputBindings.UnstageAction) && PickCell() is { } unstage)
        {
            _session.Unstage(unstage);
            _dirty = true;
        }
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } && PickCell() is { } coord)
        {
            OnBoardClicked(coord);
        }
    }

    private Coord? PickCell() =>
        BoardGeometry.TryPick(_board.Camera, GetViewport().GetMousePosition(), _session.Match.Map.Width, _session.Match.Map.Height, _board.LevelOf, out Coord coord)
            ? coord
            : null;

    private void OnBoardClicked(Coord coord)
    {
        if (_session.AwaitingZone)
        {
            if (_session.BirthZoneAt(coord) is not int zone)
            {
                return;
            }

            _session.ChooseZone(zone);
            _board.Build(_session.World.Board(), _session.ZoneOwners);
            _dirty = true;
            return;
        }

        _session.ToggleStage(coord);
        _dirty = true;
    }
}
