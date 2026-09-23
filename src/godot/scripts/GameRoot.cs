using System.Collections.Immutable;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Presentation.Camera;
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
    private bool _shotOverview;
    private bool _shotPower;
    private bool _shotGroups;
    private bool _dirty = true;
    private bool _mouseInside;
    private bool _opened;
    private int _poseChangesAfterOpening;
    private ulong _firstFrameMsec;
    private Coord? _hover;

    /// <inheritdoc/>
    public override void _Ready()
    {
        _bindings.Install();

        // 命令行：先把全部选项读完，再结算未知选项（LaunchArgs：合法选项集合 = 读取过的名字，没有第二张表）。
        // --map=<地图标识或文件>：与批量 / 终端版共用 Core 的 MapCatalog（frontier-map D5）。缺省 v5（MapCatalog.DefaultId）；
        // 选项拼错、值解析不了、地图解析不了或校验不过，都在建局之前报错退出，MUST NOT 静默回落到缺省值。
        ulong seed;
        int rounds;
        try
        {
            var args = new LaunchArgs(OS.GetCmdlineUserArgs(), OS.GetCmdlineArgs());
            _autoDemo = args.Flag("auto-demo");
            _pickCheck = args.Flag("pick-check");
            _shotOverview = args.Flag("overview");

            // --shot-power：截图时打开势力层（restore-go-core-rules 段 E：给负责人看独占格着色与"领地 + 棋串"）。
            // 势力层的文字面板在左侧、不盖棋盘中心；手牌信息面板照旧收起。
            _shotPower = args.Flag("shot-power");

            // --shot-groups：截图时打开盘面层的棋串读法（life-shape 3.4：给负责人看"已活"标记——实线环 + 悬浮眼徽记）。
            _shotGroups = args.Flag("shot-groups");

            // --map-select：强制进入选图界面（仅用于截图 / 自检；可再给 --map=<标识> 预选一项）。
            // 配 --screenshot 截选图界面；配 --auto-demo 则先把选图操作自动走一遍（SelfCheckMapSelect）再照常演示。
            bool mapSelect = args.Flag("map-select");
            seed = args.Value<ulong>("seed", "无符号整数种子", t => ulong.TryParse(t, out ulong v) ? v : null)
                ?? (_autoDemo ? 20260915UL : (ulong)Stopwatch.GetTimestamp());

            // --rounds=N：自动演示跑满 N 个大回合后停止（缺省 4；0 = 跑到终局）。这只是无人值守演示的停止点，<b>不是规则</b>——
            // 规则层已无大回合上限（restore-go-core-rules 裁决 #4），手动对局不受它约束。
            rounds = args.Value<int>("rounds", "非负整数（自动演示的停止大回合，0 = 跑到终局）", t => int.TryParse(t, out int v) && v >= 0 ? v : null)
                ?? (_autoDemo ? 4 : 0);

            // --cell-limit=K：AI 候选格上限（0 = 不限制）；未给出由 Core 按地图的可落子格数取缺省值。
            int? cellLimit = args.Value<int>("cell-limit", "非负整数（AI 候选格上限，0 = 不限制）", t => int.TryParse(t, out int v) && v >= 0 ? v : null);
            string? mapId = args.Text("map", "地图标识或地图文件路径");
            ReadScreenshotArg(args.Text("screenshot", "截图路径[:第几帧]"));

            // --export-parts=<目录>：把地形 / 设施部件导出成 .tscn（PartExport），导完即退出，不建局。
            string? exportParts = args.Text("export-parts", "导出目录（如 res://parts/terrain）");
            args.EnsureRecognized();
            if (exportParts is not null)
            {
                SetProcess(false);
                SetProcessInput(false);
                SetProcessUnhandledInput(false);
                GetTree().Quit(PartExport.Run(exportParts) == 0 ? 0 : 1);
                return;
            }

            if (mapSelect && _pickCheck && !_autoDemo)
            {
                throw new System.FormatException("--map-select 下没有人点「开始」：--pick-check 须与 --auto-demo 同用（先自动走完选图再自检拾取）。");
            }

            _pause = _autoDemo ? 0d : AiPauseSeconds;
            _matchSeed = seed;
            _rounds = rounds;
            _cellLimit = cellLimit;

            // --map=gen：随机取一个地图种子。规则内核不读时钟，取种子只在入口最外层做；时间戳折成九位以内的短种子（与选图界面"换一张"同一个折叠函数），
            // 拼成完整标识、打印出来，再交给 MapCatalog。
            // 地图种子与上面的对局种子各取各的，互不相干（map-generator D2 / D3）。
            if (GeneratedMapId.IsBareRequest(mapId))
            {
                mapId = GeneratedMapId.Format(GeneratedMapId.FriendlySeed((ulong)Stopwatch.GetTimestamp()), MapGenParameters.RandomPick);
                GD.Print($"[siege] 随机取了一个地图种子：本次地图为 {mapId}（用 --map={mapId} 可重开同一张图）");
            }

            // 选图阶段（map-generator D7）：未给 --map= 且非无人值守才进入；无人值守未给 --map= 时取缺省图（v5）、跳过选图（既有自检命令不变）。
            if (mapSelect || (mapId is null && !Unattended))
            {
                _session = BeginMapSelect(mapId);
            }
            else
            {
                MapData map = MapCatalog.Resolve(mapId);
                _session = MatchSession.Create(map, seed, System.Math.Min(4, map.MaxPlayers), 1, AiDifficulty.Standard, cellLimit);
            }
        }
        catch (System.Exception ex) when (ex is System.IO.FileNotFoundException or System.FormatException or System.Text.Json.JsonException or MapValidationException or MapGenerationException)
        {
            GD.PrintErr($"[siege] 错误：{ex.Message}");
            SetProcess(false);
            SetProcessInput(false);
            SetProcessUnhandledInput(false);
            GetTree().Quit(1);
            return;
        }

        _board = new BoardView { Name = "Board" };
        AddChild(_board);
        _board.Build(_session.World.Board(), _session.ZoneOwners);

        _hud = new Hud { Name = "Hud" };
        AddChild(_hud);
        _hud.Build();
        _hud.CameraHintVisible = !_board.Rig.FitsOneScreen;
        Connect();
        ConnectMapSelect();

        GD.Print(Selecting
            ? $"[siege] 选图界面：当前 {_select!.CurrentId}（点「开始」后建局；命令行给 --map=<标识> 可跳过选图）"
            : $"[siege] 地图 {_session.Match.Map.Id}，对局种子 {seed}，你是 {Labels.Player(_session.Me)}{(_autoDemo ? $"，自动演示模式（{(rounds == 0 ? "跑到终局" : $"跑满 {rounds} 个大回合停止")}）" : string.Empty)}");
    }

    /// <summary>
    /// <c>--screenshot=&lt;路径&gt;[:第几帧]</c>：跑到指定帧存一张 PNG 后退出。
    /// 裁决 7 的人工检查清单要的就是截图，这里给一条不用手忙脚乱按键的路径；运行中随时按 F12 也能存一张。
    /// </summary>
    private void ReadScreenshotArg(string? value)
    {
        if (value is null)
        {
            return;
        }

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
        DefaultBoardView shot = _session.World.Board();

        // 势力栏与独占格（取景自证：HUD 概览栏的"领地 + 棋串"文字、势力层着色的独占格数，都读视图模型）。
        var powerLayer = (PowerLayerContent)_session.World.Layer(TacticalLayer.Power);
        GD.Print("[siege] 势力栏：" + string.Join("；", powerLayer.Players.Select(p => $"{Labels.Player(p.Player)} {p.CompactText}")));
        GD.Print($"[siege] 独占格 {powerLayer.Territory.Length}（" + string.Join("、", powerLayer.Territory.GroupBy(c => c.Owner!.Value).OrderBy(g => g.Key.Value).Select(g => $"{Labels.Player(g.Key)} {g.Count()}")) + "）");
        Rect2 rank = _hud.RankPanelRect;
        float viewportWidth = GetViewport().GetVisibleRect().Size.X;
        GD.Print($"[siege] 势力排名面板 左 {rank.Position.X:0} 右 {rank.End.X:0} / 视口宽 {viewportWidth:0}：{(rank.End.X <= viewportWidth && rank.Position.X >= 0 ? "完整可见" : "越界")}");

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

        if (Selecting)
        {
            Rect2 panel = _hud.MapSelectRect;
            GD.Print($"[siege] 选图界面：选中「{_select!.Options[_select.SelectedIndex].Title}」，完整标识 {_select.CurrentId}，{PreviewInfo()}；全局预览 {(_board.Rig.IsOverview ? "开" : "关（一屏看全）")}；"
                + $"面板 ({panel.Position.X:0}, {panel.Position.Y:0}) {panel.Size.X:0}×{panel.Size.Y:0}，对局面板 {(_hud.MapSelectOpen ? "隐藏" : "显示")}");
        }

        GD.Print($"[siege] 落成反馈：{(_flash.Edits.IsEmpty ? "无" : string.Join("、", _flash.Edits.Select(Labels.TerrainEdit)))}");

        // 渲染开销读数（frontier-map 5.6）：帧率受垂直同步封顶，绘制调用数才反映"750 格要不要合批"。
        CameraPose pose = _board.Rig.Pose;
        GD.Print($"[perf] 帧率 {Performance.GetMonitor(Performance.Monitor.TimeFps):0}，单帧处理 {Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000d:0.0} ms，"
            + $"绘制调用 {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame):0}，对象 {Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame):0}，"
            + $"图元 {Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame):0}；相机注视点 ({pose.FocusX:0.###}, {pose.FocusZ:0.###}) 距离 {pose.Distance:0.###}");
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
        _hud.OverviewPressed += ToggleOverview;
    }

    /// <summary>全局预览开关（按钮与 M 键同一入口）：位姿计算全在视图模型里，这里只转发并刷新按钮状态。</summary>
    private void ToggleOverview()
    {
        if (_board.Rig.ToggleOverview())
        {
            ApplyCamera("全局预览");
            _dirty = true;
        }
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

        if (_frame == 0)
        {
            _firstFrameMsec = Time.GetTicksMsec();
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

        if (Selecting)
        {
            // 选图阶段：不推进对局、不采悬停与平移；只剩定帧截图这一条无人值守路径（--map-select --screenshot）。
            ProcessMapSelect();
            _frame++;
            if (_screenshotFrame >= 0 && _frame >= _screenshotFrame)
            {
                _screenshotFrame = -1;
                BeginCapture();
            }

            return;
        }

        Drive(delta);
        UpdateCamera(delta);
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

            RefreshViews();
        }

        _frame++;
        if (_pickCheck && _frame >= 2)
        {
            // 等到相机与视口都就绪的第 2 帧再投影，_Ready 里视口尺寸可能还没定。
            // 自检中途抛异常也必须以失败退出：否则异常只被引擎记一笔，自动演示接着跑完、退出码 0，等于自检没跑却报通过。
            _pickCheck = false;
            bool passed = false;
            try
            {
                passed = RunPickCheck();
            }
            finally
            {
                GetTree().Quit(passed ? 0 : 1);
            }

            return;
        }

        // --overview：截图前两帧切到全局预览（只用于截全图；对局中用 M 键 / "全局"按钮）。
        if (_shotOverview && _screenshotFrame >= 0 && _frame >= _screenshotFrame - 2 && !_board.Rig.IsOverview)
        {
            ToggleOverview();
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
        if (_shotPower)
        {
            _layers.Toggle(TacticalLayer.Power);
        }

        if (_shotGroups)
        {
            _layers.Toggle(TacticalLayer.Board);
            if (_layers.Reading != BoardReading.Groups)
            {
                _layers.CycleReading();
            }
        }

        _dirty = false;
        RefreshViews();

        // life-shape 3.4 取景自证：打印本帧视图模型里的禁入格与已活棋串数量（都取自 Presentation，不在引擎侧判活形）。
        DefaultBoardView shotBoard = _session.World.Board();
        int alive = _session.World.Layer(TacticalLayer.Board, BoardReading.Groups) is LibertyLayerContent groups
            ? groups.Groups.Count(g => g.Mark == GroupMark.Alive)
            : 0;
        GD.Print($"[life-shape] 截图取景：当前行动 {(_session.Match.CurrentPlayer is { } actor ? Labels.Player(actor) : "无")}，禁入格 {shotBoard.Cells.Count(c => c.Block == PlacementBlock.LifeForbidden)}，已活棋串 {alive}，棋串读法 {(_layers.Active == TacticalLayer.Board && _layers.Reading == BoardReading.Groups ? "开" : "关")}");
        CaptureWhenDrawn(_screenshotPath);
    }

    /// <summary>把当前状态刷到棋盘与 HUD。选图阶段 HUD 只显示选图面板（对局面板隐藏），棋盘照常刷新——预览与插旗前的盘面是同一条渲染路径。</summary>
    private void RefreshViews()
    {
        _board.Refresh(_session.World, _layers.Active, _layers.Reading, _layers.Treatment, LibertyThresholds.Default, _flash, _hover);
        _hud.SetHoverReadout(HoverReadout.Of(_hover, _session.World.Board())); // life-shape 3.4：盘面变了（如新成活形），悬停格的禁入读数随之刷新
        if (Selecting)
        {
            _hud.ShowMapSelect(_select!, PreviewInfo());
            return;
        }

        _hud.OverviewActive = _board.Rig.IsOverview;
        _hud.Refresh(_session, _layers, _handPanel);
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
    /// <remarks>
    /// <para>动态相机（viewport-camera「动态相机下的拾取正确」）分两层，任一层失败即整体失败，失败行指出位姿与格坐标：</para>
    /// <para>① <b>逐格居中</b>（全严格）：把每个可落子格尽量移到画面中心（受夹取），在最近、最远两种缩放下各验一次，必须往返到同一格。
    /// 这是「俯角 60° 下高台向远处只投 0.40 格遮挡」那条论证的直接验证——它只在注视点附近成立，所以就在注视点附近验；
    /// 一屏看全的地图上「最远」居中位姿就是旧固定相机。
    /// <b>例外只有一种</b>（map-generator 3.5，生成图上实测到）：注视点在<b>纵深方向</b>被边界夹取、该格落在注视点的<b>远侧</b>（最远缩放下贴近地图远边的几行），
    /// 视线比 60° 浅，上面那条论证的前提不成立——此时按 ② 的口径分类，且只认<b>紧邻</b>的格：拾到层数严格更高、离相机更近、在该格<b>正前或斜前一行</b>（行号小 1、列差不超过 1；同一行的左右邻格遮不住格心，不算）的格 → 记「遮挡」并打印，不算失败；其余仍是失败。
    /// 判据是注视点与格心的实际偏差，不是"凡失败就算没居中"。只有横向被夹取（纵深居中）的格<b>不在例外内</b>：台面向远处的遮挡只取决于纵深方向的视线分量，
    /// 横向偏移不加深它——段 C 检查实测：把横向夹取也算进例外，"缩放带 5° 俯角变化"这个真错误在 v4 上会被当成遮挡放过（<c>B5 → B4</c>，退出码 0）。
    /// 落在注视点近侧的格视线更陡，同样全严格。并且<b>每个可落子格至少要在一种缩放下严格往返到自己</b>，否则整体失败——
    /// 例外只免掉"这一档缩放下看不见它"，免不掉"哪一档都点不到它"。</para>
    /// <para>② <b>7 个位姿</b>（中心、四角夹取位、最近、最远；位姿表在视图模型里）：每个位姿验<b>格心落在画面内</b>的全部格。
    /// 透视相机下屏幕上部的射线俯角远小于 60°（视场 54° → 顶边只有 33°），紧贴 h=2 崖壁身后的低地格心在那里会被高台顶面<b>真实遮住</b>，
    /// 此时拾到高台才是对的（规格：点在某格顶面的屏幕投影内即选中该格）。故不一致时分类：拾到的格层数严格更高、且离相机更近 → 记「遮挡」并打印，不算失败；
    /// 未命中、拾到同层或更低的格 → 失败。另要求每个可落子格至少在一个位姿下被验到。
    /// 注视地图中心的位姿（中心 / 最近 / 最远 / 全局预览）画面内 MUST 有可落子格；四个角位姿在随机生成图上可能整屏都是深水与障碍
    /// （map-generator 3.5 实测 <c>gen:1:p5</c> 右上角），此时没有可验的格——打印出来、不算失败，由"每格至少验到一次"兜住覆盖面。</para>
    /// </remarks>
    private bool RunPickCheck()
    {
        int width = _session.Match.Map.Width;
        int height = _session.Match.Map.Height;
        Vector2 size = GetViewport().GetVisibleRect().Size;
        var view = new Rect2(Vector2.Zero, size);
        BoardCellView[] cells = [.. _session.World.Board().Cells.Where(c => c.Terrain == Terrain.Playable)];
        CameraPose saved = _board.Rig.Pose;
        bool ok = cells.Length > 0;

        // ① 逐格居中
        var strict = new HashSet<Coord>();
        foreach ((string label, float distance) in new[] { ("最近", _board.Rig.Nearest), ("最远", _board.Rig.Farthest) })
        {
            var failures = new List<string>();
            var offCenter = new List<string>();
            foreach (BoardCellView cell in cells)
            {
                (float x, float z) = _board.PlaneCenterOf(cell.Coord);
                _board.Rig.Set(new CameraPose(x, z, distance));
                _board.ApplyCameraPose();
                Vector3 center = _board.CenterOf(cell.Coord);
                Vector2 screen = _board.Camera.UnprojectPosition(center);
                bool hit = BoardGeometry.TryPick(_board.Camera, screen, width, height, _board.LevelOf, out Coord picked);
                if (hit && picked == cell.Coord)
                {
                    strict.Add(cell.Coord);
                }
                else
                {
                    CameraPose at = _board.Rig.Pose;
                    Vector3 eye = _board.Camera.Position;
                    bool beyondFocus = at.FocusZ - z > 1e-3f;
                    bool adjacent = hit && picked.Y == cell.Coord.Y - 1 && System.Math.Abs(picked.X - cell.Coord.X) <= 1;
                    bool blocked = beyondFocus && adjacent && _board.LevelOf(picked) > cell.Height && Flat(_board.CenterOf(picked) - eye) < Flat(center - eye);
                    (blocked ? offCenter : failures).Add(
                        $"{cell.Coord.ToNotation()}(h{cell.Height}) → {(hit ? $"{picked.ToNotation()}(h{_board.LevelOf(picked)})" : "未命中")}〔注视点 ({at.FocusX:0.###}, {at.FocusZ:0.###})〕");
                }
            }

            ok &= failures.Count == 0;
            GD.Print($"[pick-check] 逐格居中·{label}（距离 {distance:0.###}）：可落子格 {cells.Length}，往返一致 {cells.Length - failures.Count - offCenter.Count}，"
                + $"纵深夹取后落在注视点远侧、被紧邻的更近高台遮挡 {offCenter.Count}{(offCenter.Count == 0 ? string.Empty : "（" + string.Join("、", offCenter) + "）")}，"
                + $"失败 {failures.Count}{(failures.Count == 0 ? string.Empty : "：" + string.Join("、", failures))}");
        }

        string[] neverStrict = [.. cells.Where(c => !strict.Contains(c.Coord)).Select(c => c.Coord.ToNotation())];
        ok &= neverStrict.Length == 0;
        GD.Print($"[pick-check] 逐格居中·合计：至少在一种缩放下严格往返的格 {strict.Count} / {cells.Length}"
            + (neverStrict.Length == 0 ? string.Empty : "；哪一档都点不到：" + string.Join("、", neverStrict)));

        // ② 7 个位姿
        var verified = new HashSet<Coord>();
        // 一屏看不全的地图另加"全局预览"一档：距离越过平时的最远上限，是拾取要另验的新距离段。
        List<(string Name, System.Action Apply)> stages = [.. _board.Rig.CheckPoses().Select(p => (p.Name, (System.Action)(() => _board.Rig.Set(p.Pose))))];
        if (!_board.Rig.FitsOneScreen)
        {
            stages.Add(("全局预览", () =>
            {
                _board.Rig.Set(saved);
                _board.Rig.ToggleOverview();
            }));
        }

        foreach ((string name, System.Action apply) in stages)
        {
            apply();
            CameraPose pose = _board.Rig.Pose;
            _board.ApplyCameraPose();
            Vector3 eye = _board.Camera.Position;
            var failures = new List<string>();
            var occluded = new List<string>();
            int inView = 0;
            foreach (BoardCellView cell in cells)
            {
                Vector3 center = _board.CenterOf(cell.Coord);
                if (_board.Camera.IsPositionBehind(center))
                {
                    continue;
                }

                Vector2 screen = _board.Camera.UnprojectPosition(center);
                if (!view.HasPoint(screen))
                {
                    continue;
                }

                inView++;
                verified.Add(cell.Coord);
                bool hit = BoardGeometry.TryPick(_board.Camera, screen, width, height, _board.LevelOf, out Coord picked);
                if (hit && picked == cell.Coord)
                {
                    continue;
                }

                string line = $"{cell.Coord.ToNotation()}(h{cell.Height}) → {(hit ? $"{picked.ToNotation()}(h{_board.LevelOf(picked)})" : "未命中")}";
                bool blocked = hit && _board.LevelOf(picked) > cell.Height && Flat(_board.CenterOf(picked) - eye) < Flat(center - eye);
                (blocked ? occluded : failures).Add(line);
            }

            PlaneRect bounds = _board.Rig.Bounds;
            bool focusedOnCenter = Mathf.Abs(pose.FocusX - bounds.CenterX) < 1e-3f && Mathf.Abs(pose.FocusZ - bounds.CenterZ) < 1e-3f;
            ok &= failures.Count == 0 && (inView > 0 || !focusedOnCenter);
            GD.Print($"[pick-check] 位姿「{name}」注视点 ({pose.FocusX:0.###}, {pose.FocusZ:0.###}) 距离 {pose.Distance:0.###}：画面内可落子格 {inView}，"
                + $"往返一致 {inView - failures.Count - occluded.Count}，被更近的高台遮挡 {occluded.Count}{(occluded.Count == 0 ? string.Empty : "（" + string.Join("、", occluded) + "）")}，"
                + $"失败 {failures.Count}{(failures.Count == 0 ? string.Empty : "：" + string.Join("、", failures))}"
                + (inView == 0 && !focusedOnCenter ? "（该角视野内没有可落子格，无可验）" : string.Empty));
        }

        _board.Rig.Set(saved);
        _board.ApplyCameraPose();

        string[] missed = [.. cells.Where(c => !verified.Contains(c.Coord)).Select(c => c.Coord.ToNotation())];
        ok &= missed.Length == 0;
        GD.Print($"[pick-check] 可落子格 {cells.Length}，至少在一个位姿下验到 {verified.Count}{(missed.Length == 0 ? string.Empty : "，未覆盖：" + string.Join("、", missed))}；{(ok ? "通过" : "失败")}");
        return ok;

        static float Flat(Vector3 v) => (v.X * v.X) + (v.Z * v.Z);
    }

    private void Drive(double delta)
    {
        // 自动演示的停止点（--rounds）：表现层自己的无人值守收尾，不改对局状态、不产生名次。
        if (_session.IsOver || (_autoDemo && _rounds > 0 && _session.Match.MajorRound > _rounds))
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
                FocusHome(opening: true);
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
        // --rounds 停止点（表现层的无人值守收尾，不是规则终局）：规则层没有结果，如实写"演示停止"而不是"终局"。
        GD.Print(result is null
            ? $"[auto-demo] 演示停止：跑满 {_rounds} 个大回合（--rounds 停止点，不是终局；对局停在第 {_session.Match.MajorRound} 大回合）；{standings}"
            : $"[auto-demo] 终局：第 {result.MajorRound} 大回合，{Names.End(result.Reason)}；{standings}");
        GD.Print($"[perf] 启动到首帧 {_firstFrameMsec} ms，启动到终局 {Time.GetTicksMsec()} ms，共 {_frame} 帧");
        if (_screenshotFrame >= 0)
        {
            // 还等着截图：留在结算画面，由 --screenshot 的帧号或 --quit-after 决定何时退出，
            // 否则终局画面永远截不到（自检时最该看的就是它）。
            return;
        }

        GetTree().Quit(HomePlatformFramed() ? 0 : 1);
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
        // 无人值守（自动演示 / 定帧截图 / 拾取自检）同样不采悬停：指针恰好停在窗口内不该给定帧截图带上悬停读数与光标格。
        if (DisplayServer.GetName() == "headless" || Unattended)
        {
            return;
        }

        // 指针在界面面板上（被控件吃掉）或在窗口之外：不算悬停在格上，读数为空。
        Vector2 mouse = GetViewport().GetMousePosition();
        Coord? hover = _mouseInside && GetViewport().GuiGetHoveredControl() is null
            && BoardGeometry.TryPick(_board.Camera, mouse, _session.Match.Map.Width, _session.Match.Map.Height, _board.LevelOf, out Coord coord)
            ? coord
            : null;
        if (hover != _hover)
        {
            _hover = hover;
            _board.SetCursor(hover);
            _hud.SetHoverReadout(HoverReadout.Of(hover, _session.World.Board()));
        }
    }

    /// <summary>无人值守（自动演示 / 定帧截图 / 拾取自检）：不采贴边推屏，画面才可复现——指针恰好停在窗口边上不该改变截图。</summary>
    private bool Unattended => _autoDemo || _pickCheck || _screenshotFrame >= 0 || _shotPending;

    /// <summary>
    /// 相机输入（viewport-camera）：贴边感应带 + 方向键 / WASD 合成一个平移意图交给视图模型，夹取与速度都在那边算。
    /// 贴边推屏在三种情况下不触发：窗口失焦、指针在窗口之外、指针停在界面面板上（用控件现成的悬停信息，不另画感应区表）。
    /// </summary>
    private void UpdateCamera(double delta)
    {
        Vector2 size = SyncAspect();
        float right = Input.GetActionStrength(InputBindings.CameraRightAction) - Input.GetActionStrength(InputBindings.CameraLeftAction);
        float up = Input.GetActionStrength(InputBindings.CameraUpAction) - Input.GetActionStrength(InputBindings.CameraDownAction);
        if (!Unattended && _mouseInside && GetWindow().HasFocus() && GetViewport().GuiGetHoveredControl() is null)
        {
            Vector2 mouse = GetViewport().GetMousePosition();
            (float edgeRight, float edgeUp) = EdgePan.Intent(mouse.X, mouse.Y, size.X, size.Y);
            right += edgeRight;
            up += edgeUp;
        }

        if (right != 0f || up != 0f)
        {
            _board.Rig.Pan(right, up, (float)delta);
        }

        ApplyCamera("输入");
    }

    /// <summary>把视口当前的宽高比同步给视图模型（所见范围、夹取、开局距离都依赖它）；返回视口尺寸。</summary>
    private Vector2 SyncAspect()
    {
        Vector2 size = GetViewport().GetVisibleRect().Size;
        if (size.X > 0f && size.Y > 0f)
        {
            _board.Rig.SetAspect(size.X / size.Y);
        }

        return size;
    }

    /// <summary>把视图模型位姿写到相机节点；无人值守模式下位姿一变就打一行，供核对"对手行动时相机不动"。</summary>
    private void ApplyCamera(string why)
    {
        if (_board.ApplyCameraPose() && Unattended)
        {
            // 开局对准之后，无人值守模式下没有任何输入：位姿再变就是相机在"自己动"（规格：MUST NOT 因其他玩家行动而自动移动）。
            // --overview 为截全图主动切的全局预览不算在内。
            _poseChangesAfterOpening += _opened && why != "全局预览" ? 1 : 0;
            CameraPose pose = _board.Rig.Pose;
            GD.Print($"[camera] 第 {_frame} 帧（{why}）注视点 ({pose.FocusX:0.###}, {pose.FocusZ:0.###})，距离 {pose.Distance:0.###}");
        }
    }

    /// <summary>
    /// 回家：注视点移到本机玩家出生平台（出生区格子的外接矩形）中心；尚未选区时为地图中心。出生区格子读默认棋盘视图模型，不读地图。
    /// <paramref name="opening"/> 为真即插旗锁定后的开局对准（距离取"平台整个可见并留余量"，平台整个可见优先于居中），否则是空格回家（缩放不变）。
    /// </summary>
    private void FocusHome(bool opening)
    {
        Coord[] cells = HomeCells();
        if (opening)
        {
            // 自动演示在第 0 帧的 Drive 里就锁定，早于本帧的 UpdateCamera：视图模型此时还是缺省宽高比，先同步再算开局距离。
            SyncAspect();
            _board.Rig.Open(CameraHome.Platform(cells, _board.PlaneCenterOf));
        }
        else
        {
            (float x, float z) = CameraHome.Target(cells, _board.PlaneCenterOf, _board.Rig.Bounds);
            _board.Rig.Home(x, z);
        }

        ApplyCamera(opening ? "开局对准出生平台" : "回家");
        _opened |= opening;
    }

    private Coord[] HomeCells()
    {
        int? zone = _session.ZoneOwners.Where(z => z.Value == _session.Me).Select(z => (int?)z.Key).FirstOrDefault();
        return zone is null ? [] : [.. _session.World.Board().Cells.Where(c => c.BirthZone == zone).Select(c => c.Coord)];
    }

    /// <summary>
    /// 无人值守自检（viewport-camera「回到出生平台」：平台整个可见是 MUST）：用引擎的<b>真实透视投影</b>核对本机出生平台每格的四个格角都在视口内——
    /// 视图模型里的所见范围只是线性近似，不能自己给自己作证。自动演示全程不碰相机，终局时的位姿就是开局对准的位姿——
    /// 这一点本身也一并核对（「不跟随对手」）：开局对准之后位姿变过即失败。
    /// </summary>
    private bool HomePlatformFramed()
    {
        Coord[] cells = HomeCells();
        Rect2 view = GetViewport().GetVisibleRect();
        float half = 0.5f * BoardGeometry.CellSize;
        (float, float)[] corners = [(-half, -half), (half, -half), (-half, half), (half, half)];
        int inside = cells.Count(c => corners.All(d =>
        {
            Vector3 corner = _board.CenterOf(c) + new Vector3(d.Item1, 0f, d.Item2);
            return !_board.Camera.IsPositionBehind(corner) && view.HasPoint(_board.Camera.UnprojectPosition(corner));
        }));
        bool ok = inside == cells.Length && cells.Length > 0 && _poseChangesAfterOpening == 0;
        GD.Print($"[camera] 开局对准自检：出生平台 {cells.Length} 格，整格在画面内 {inside}；开局对准之后位姿变化 {_poseChangesAfterOpening} 次（对手行动时相机不得移动）；{(ok ? "通过" : "失败")}");
        return ok;
    }

    /// <inheritdoc/>
    public override void _Notification(int what)
    {
        // 指针进出窗口：出窗后 GetMousePosition 仍停在最后的窗内位置（往往正好在边上），不记这一笔就会一直推屏。
        if (what == NotificationWMMouseEnter)
        {
            _mouseInside = true;
        }
        else if (what == NotificationWMMouseExit)
        {
            _mouseInside = false;
        }
    }

    /// <inheritdoc/>
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion)
        {
            // 启动时指针若本来就在窗内，不会有"进入窗口"的通知；第一次鼠标移动同样说明它在窗内。
            // 反过来初值不能取 true：启动时指针在窗外也不会有"离开"通知，GetMousePosition 常报 (0, 0)——正好在左上角感应带里。
            _mouseInside = true;
            return;
        }

        // 选图阶段不认领相机键（map-generator D8）：相机本来就锁在预览位姿，而 W A S D / 空格 / 方向键 / 退格要能落到种子输入框。
        if (@event is not InputEventKey || _session is null || Selecting)
        {
            return;
        }

        // 相机键先于界面控件认领：否则空格会按下当前获得焦点的按钮（误确认 / 误 Pass），方向键会在按钮间挪焦点。
        // 平移键的按住状态由 UpdateCamera 逐帧轮询，这里只负责"吃掉"事件与处理一次性的回家。
        foreach (string action in InputBindings.CameraKeyActions)
        {
            if (!@event.IsAction(action))
            {
                continue;
            }

            if (action == InputBindings.CameraHomeAction && @event.IsActionPressed(action))
            {
                FocusHome(opening: false);
                _dirty = true;
            }
            else if (action == InputBindings.CameraOverviewAction && @event.IsActionPressed(action))
            {
                ToggleOverview();
            }

            GetViewport().SetInputAsHandled();
            return;
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

        // 选图阶段不接受落子、插旗、缩放与信息层按键：点棋盘无效果（map-selection「预览阶段 MUST NOT 接受落子与插旗」）。
        if (Selecting)
        {
            return;
        }

        // 滚轮缩放：只改距离（俯角恒定），夹取在视图模型里。指针在面板上时事件到不了这里，滚轮不会穿透面板。
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel)
        {
            bool wasOverview = _board.Rig.IsOverview;
            _board.Rig.Zoom(wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1);
            ApplyCamera("缩放");
            _dirty |= wasOverview != _board.Rig.IsOverview;
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
            FocusHome(opening: true);
            _dirty = true;
            return;
        }

        _session.ToggleStage(coord);
        _dirty = true;
    }
}
