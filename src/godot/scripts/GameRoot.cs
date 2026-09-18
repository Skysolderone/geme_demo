using System.Collections.Immutable;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Presentation.Hand;
using Siege.Presentation.Layers;
using Siege.Presentation.Preview;
using Siege.Presentation.Text;
using Siege.Presentation.Visibility;

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
    private int _seenEdits;
    private int _frame;
    private int _screenshotFrame = -1;
    private string _screenshotPath = string.Empty;
    private bool _shotPending;
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
        int rounds = ReadRounds(args) ?? (_autoDemo ? 4 : MatchOptions.DefaultMaxMajorRounds);
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
    /// <c>--rounds=N</c>：覆盖本局的大回合上限（缺省：自动演示 4，手动 <see cref="MatchOptions.DefaultMaxMajorRounds"/>）。
    /// 无人值守演示的 4 个大回合走不到岛心，拍不到"烧林前后"——把上限调高是唯一不改规则也不加演示专用分支的取法。
    /// </summary>
    private static int? ReadRounds(IReadOnlyList<string> args)
    {
        foreach (string arg in args)
        {
            if (arg.StartsWith("--rounds=", System.StringComparison.Ordinal) && int.TryParse(arg["--rounds=".Length..], out int rounds) && rounds > 0)
            {
                return rounds;
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
        GD.Print($"[siege] 截图 {path}：{error}（{image.GetWidth()}×{image.GetHeight()}，第 {_frame} 帧，第 {_session.Match.MajorRound} 大回合，信息层 {(_layers.Active is { } layer ? Names.Layer(layer) : "关")}，手牌信息面板 {(_handPanel.IsOpen ? "开" : "关")}，中央面板 {(_hud.CenterPanelOpen ? "开" : "关")}）");
        // 截图当刻的据点状态（读视图模型），供 art/sites-v4/README 的人工清单对照"哪张图里有争议 / 控制"。
        DefaultBoardView shot = _session.World.Board();
        GD.Print("[siege] 据点：" + string.Join("；", shot.Sites.Select(s => $"{s.Coord.ToNotation()} {s.TierText} {s.StatusText}")));

        // 盘上六种棋子各多少枚、本局已完成哪些改造，供 art/artisan-v4/README 的人工清单对照（都读视图模型，不读地图、不判规则）。
        GD.Print("[siege] 棋子：" + string.Join("；", shot.Cells
            .Where(c => c.Occupant is not null)
            .GroupBy(c => c.Occupant!.Value.Type)
            .OrderBy(g => g.Key)
            .Select(g => $"{Labels.Piece(g.Key)} {g.Count()}（{string.Join("、", g.Select(c => c.Coord.ToNotation()))}）")));
        GD.Print($"[siege] 本局改造 {shot.Edits.Length} 处：" + string.Join("、", shot.Edits.Select(Labels.TerrainEdit)));
        if (_session.World.Preview() is { } shownPreview)
        {
            GD.Print("[siege] 暂放：" + string.Join("；", shownPreview.StagedPieces.Select(s =>
                $"{s.Coord.ToNotation()} {Labels.Piece(s.Type)}{(s.EditText is null ? string.Empty : " + " + s.EditText)}")));
            GD.Print("[siege] 改造高亮：" + string.Join("；", shownPreview.ArtisanEdits.Select(a =>
                $"{a.ArtisanCell.ToNotation()} 已选 {a.ChosenText}，候选 桥 {a.BridgeCells.Length} / 栅 {a.FenceEdges.Length} / 林 {a.BurnCells.Length}")));
        }

        GD.Print($"[siege] 落成反馈：{(_flash.Edits.IsEmpty ? "无" : string.Join("、", _flash.Edits.Select(Labels.TerrainEdit)))}");
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
        if (_shotPending)
        {
            // 已进入截图流程：冻结演示与刷新，等 FramePostDraw 取图后退出（见 BeginCapture）。
            return;
        }

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
            if (NewEdits() is { IsEmpty: false } fresh)
            {
                _flash = _flash with { Edits = fresh };
                _flashTimer = FlashSeconds;
                GD.Print($"[terrain-edit] 第 {_frame} 帧、第 {_session.Match.MajorRound} 大回合落成：{string.Join("、", fresh.Select(Labels.TerrainEdit))}");
            }

            _board.Refresh(_session.World, _layers.Active, _layers.Reading, _layers.Treatment, LibertyThresholds.Default, _flash, _hover);
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
            BeginCapture();
        }
    }

    /// <summary>
    /// 进入截图流程：<b>先把盖住棋盘的面板全部关掉</b>再重刷一次，等本帧绘制完成后才取画面。
    /// </summary>
    /// <remarks>
    /// 两个坑都在这里治：①「全玩家手牌信息面板」与信息层会整块盖住棋盘（无人值守演示每个部署回合都会开一次），
    /// 截到就废；②<c>GetViewport().GetTexture()</c> 拿的是<b>上一帧已绘制</b>的画面，在 <c>_Process</c> 里直接取
    /// 会截到本帧刷新之前的状态——控制台打印的读数与图像因此对不上（旧的 edit-targets / fence-after 两张就是这么废的）。
    /// 进入本流程后 <see cref="_shotPending"/> 冻结 <c>Drive</c>，画面不再变，打印的读数即图像所示。
    /// </remarks>
    private void BeginCapture()
    {
        _shotPending = true;
        _layers.Back();
        _handPanel.Back();
        _dirty = false;
        _board.Refresh(_session.World, _layers.Active, _layers.Reading, _layers.Treatment, LibertyThresholds.Default, _flash, _hover);
        _hud.Refresh(_session, _layers, _handPanel);
        CaptureWhenDrawn(_screenshotPath);
    }

    private async void CaptureWhenDrawn(string path)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Capture(path);
        GetTree().Quit(0);
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
                    // 征募面板原先只渲染不点：本人手里因此永远只有开局那几种棋子，匠人与改造在无人值守自检下一次都跑不到。
                    // 现在挑一个候选：有匠人就要匠人，否则要第一个可选的。
                    AutoPick();
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

    /// <summary>无人值守演示：征募一枚——优先匠人（本轮要覆盖的正是它），否则第一个可选候选。选不动就算了，不拦流程。</summary>
    private void AutoPick()
    {
        if (_session.RecruitPanel is not { } panel)
        {
            return;
        }

        RecruitCandidateView? pick = panel.Candidates.FirstOrDefault(c => c.IsSelectable && c.Type == PieceType.Artisan)
            ?? panel.Candidates.FirstOrDefault(c => c.IsSelectable);
        if (pick is not null)
        {
            _session.Pick(pick.Index);
        }
    }

    /// <summary>当前那枚暂放匠人有烧林目标、但还没选中它。</summary>
    private bool WantsBurn() =>
        _session.World.Preview()?.ArtisanEdits.LastOrDefault() is { BurnCells.IsEmpty: false } a
        && a.Chosen?.Kind != TerrainEditKind.Burn;

    private void AutoDeployStep()
    {
        if (_demoStep == 0)
        {
            // 手里有匠人就优先摆匠人，摆下后按一次"轮换改造目标"：让无人值守自检也走一遍
            // 可改造目标枚举 → 选目标 → 带改造结算这条链路（原先这条链路在 --auto-demo 下完全没被跑到）。
            PieceType[] types = [.. _session.World.OwnHand.Types];
            bool artisan = types.Contains(PieceType.Artisan);
            _session.SelectedType = artisan ? PieceType.Artisan : _session.SelectedType ?? types.Cast<PieceType?>().FirstOrDefault();
            bool staged = artisan ? _session.StageArtisanPreferringBurn() : _session.StageFirstLegal();
            if (staged && artisan)
            {
                // 轮到第一个目标；若这个落点能烧林就一路轮到烧林——三种动作里烧林最难被跑到（全图 4 格林地 + 裁决 T-12 的候选拥挤）。
                _session.CycleEdit(null);
                for (int i = 0; i < 24 && WantsBurn(); i++)
                {
                    _session.CycleEdit(null);
                }

                if (_session.World.Preview() is { ArtisanEdits.IsEmpty: false } p)
                {
                    ArtisanEditView view = p.ArtisanEdits[^1];
                    GD.Print($"[auto-demo] 第 {_frame} 帧暂放匠人 {view.ArtisanCell.ToNotation()}："
                        + $"已选 {view.ChosenText}，可改造目标 {view.Targets.Length} 个"
                        + $"（桥 {view.BridgeCells.Length} / 栅 {view.FenceEdges.Length} / 林 {view.BurnCells.Length}）");
                }
            }

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
            if (_session.World.Preview() is { Failure: { } why, DeployUsed: > 0 })
            {
                GD.Print($"[auto-demo] 第 {_frame} 帧本批被拒，改为 Pass：{why.Title}——{why.Detail}");
            }

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

    /// <summary>
    /// 自上次刷新以来<b>新落成</b>的改造（visual-style-baseline「改造完成的那一刻 SHALL 有一次可察觉的反馈」）。
    /// 走默认棋盘视图的 <c>Edits</c>——AI 结算、本人确认、无人值守演示三条路径都经过它，不必各写一遍；
    /// 该清单无归属、不含改造者（R-3）。
    /// </summary>
    private ImmutableArray<TerrainEdit> NewEdits()
    {
        ImmutableArray<TerrainEdit> edits = _session.World.Board().Edits;
        if (edits.Length <= _seenEdits)
        {
            _seenEdits = edits.Length;
            return [];
        }

        ImmutableArray<TerrainEdit> fresh = [.. edits.Skip(_seenEdits)];
        _seenEdits = edits.Length;
        return fresh;
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
        else if (@event.IsActionPressed(InputBindings.CycleEditAction))
        {
            // 光标停在某枚已暂放的匠人上就换那一枚的目标，否则换最后暂放的那一枚。
            _session.CycleEdit(_hover);
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
