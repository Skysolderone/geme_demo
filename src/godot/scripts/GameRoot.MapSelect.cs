using System.Diagnostics;
using System.Linq;
using Godot;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Presentation.MapSelect;
using Siege.Presentation.Text;
using Siege.Presentation.Visibility;

namespace Siege.Godot;

/// <summary>
/// 建局之前的选图阶段（map-generator D7 / D8）：未给 <c>--map=</c> 且非无人值守时进入；确认后才建真正的对局、进入插旗。
/// </summary>
/// <remarks>
/// <para>选图状态机在零引擎依赖的 <see cref="MapSelectModel"/> 里，它只产出地图标识；地图一律经三个入口共用的目录解析，这里不自带清单、不自行出图。</para>
/// <para>预览不另写渲染：用候选地图建一个<b>尚未插旗</b>的会话，只为取它的默认棋盘视图模型交给 <see cref="BoardView.Build"/>（与开局插旗前的盘面同构）；
/// 确认开局时按最终选择重新建局。一屏看不全的图切全局预览，一屏看全的图保持初始位姿。</para>
/// <para>该阶段不接受落子、插旗、推屏与缩放：<c>_Process</c> 不走 <c>Drive</c> / 悬停 / 平移，<c>_UnhandledInput</c> 整体不处理，
/// <c>_Input</c> 不认领相机键——否则 W A S D、空格、方向键进不了种子输入框。</para>
/// </remarks>
public sealed partial class GameRoot
{
    private MapSelectModel? _select;
    private MapData? _previewMap;
    private ulong _matchSeed;
    private int _rounds;
    private int? _cellLimit;
    private int _selectStep;
    private string _selectEntryId = string.Empty;
    private int _selectEntryBoardNodes;
    private int _selectEntryOrphans;

    /// <summary>是否处于选图阶段。</summary>
    private bool Selecting => _select is not null;

    /// <summary>取一个新的地图种子：入口最外层读一次计数器，折成便于读写的短种子（视图模型与规则内核都不读时钟）。</summary>
    private static ulong NewMapSeed() => BoardMapId.FriendlySeed((ulong)Stopwatch.GetTimestamp());

    /// <summary>
    /// 进入选图阶段并搭好第一张预览。<paramref name="preselect"/> 只来自 <c>--map-select --map=&lt;标识&gt;</c>（截图 / 自检）；
    /// 启动时解析失败与其他命令行错误同样处理（由 <c>_Ready</c> 报错退出）。
    /// </summary>
    private MatchSession BeginMapSelect(string? preselect)
    {
        _select = new MapSelectModel(NewMapSeed(), _difficulty);
        if (preselect is not null && !_select.TrySelectId(preselect))
        {
            // 选图界面只列内置棋盘图与随机棋盘图（builtin-board-maps D5）：已删除的旧标识（retire-legacy-maps）、地图文件与写错的标识都不是界面上的项，
            // 预选不了就报错退出，不静默忽略预选（不带 --map-select 时这些标识照旧直接建局）。
            throw new System.FormatException($"--map-select 只能预选内置棋盘图或完整的棋盘图标识（board:<种子>[:p<人数>][:n<棋盘数>]），{preselect} 不在选图界面的清单里；不带 --map-select 时可用 --map={preselect} 直接建局。");
        }

        MatchSession preview = PreviewSession();
        _select.Accept();
        _selectEntryId = _select.CurrentId;
        return preview;
    }

    /// <summary>按视图模型当前的标识解析地图，建一个尚未插旗的会话供预览。</summary>
    private MatchSession PreviewSession()
    {
        MapData map = MapCatalog.Resolve(_select!.CurrentId);
        MatchSession preview = MatchSession.Create(map, _matchSeed, System.Math.Min(4, map.MaxPlayers), 1, AiDifficulty.Standard, _cellLimit);
        _previewMap = map;
        return preview;
    }

    private void ConnectMapSelect()
    {
        _hud.MapOptionPicked += index => OnSelectionChanged(_select?.Select(index));
        _hud.MapSeedSubmitted += text => OnSelectionChanged(_select?.SubmitSeed(text));
        _hud.MapRerollPressed += () => OnSelectionChanged(_select?.Reroll(NewMapSeed()));
        _hud.MapPlayersAdjusted += delta => OnSelectionChanged(_select?.AdjustPlayers(delta));
        _hud.MapBoardsAdjusted += delta => OnSelectionChanged(_select?.AdjustBoards(delta));
        _hud.MapStartPressed += StartMatch;
        _hud.MapDifficultyPicked += difficulty =>
        {
            _select?.SelectDifficulty(difficulty);
            _dirty = true;
        };
    }

    /// <summary>
    /// 视图模型的一次操作之后：标识变了就重新解析并重搭预览；解析 / 生成 / 校验失败不崩溃——视图模型回到上一张成功的图，面板显示原因。
    /// </summary>
    private void OnSelectionChanged(bool? idChanged)
    {
        if (_select is null)
        {
            return;
        }

        if (idChanged == true)
        {
            try
            {
                _session = PreviewSession();
                _select.Accept();
                _board.Build(_session.World.Board(), _session.ZoneOwners);
                GD.Print($"[siege] 选图预览：{_select.CurrentId}");
            }
            catch (System.Exception ex) when (ex is MapGenerationException or MapValidationException or System.FormatException or System.IO.FileNotFoundException)
            {
                _select.RollBack($"这张图没有生成出来：{ex.Message}");
                GD.PrintErr($"[siege] 选图预览失败：{ex.Message}");
            }
        }

        _dirty = true;
    }

    /// <summary>选图阶段的单帧：只同步宽高比、维持预览位姿、按需刷新；不推进对局、不采悬停与平移。</summary>
    private void ProcessMapSelect()
    {
        SyncAspect();

        // 一屏看不全的图切全局预览（换到外接矩形不同的图会重建相机视图模型，故逐帧核对）；一屏看全的图 ToggleOverview 不动相机。
        if (!_board.Rig.FitsOneScreen && !_board.Rig.IsOverview)
        {
            _board.Rig.ToggleOverview();
        }

        ApplyCamera("全局预览");
        if (_autoDemo)
        {
            SelfCheckMapSelect();
            if (!Selecting)
            {
                return;
            }
        }

        if (_dirty)
        {
            _dirty = false;
            RefreshViews();
        }
    }

    /// <summary>
    /// 无人值守自检（<c>--map-select --auto-demo</c>）：每帧一步，把选图操作经与面板事件<b>同一组处理函数</b>走一遍——
    /// 随机棋盘图 → 换一张 → 人数 −1（已是下限则 +1）→ 棋盘数 +1（到上限则 −1）→ 非法种子 → 合法种子 → 逐个内置棋盘图 → 回到进入时的那一项 → 开始；随后照常自动演示。
    /// 任何一步不符预期即以退出码 1 结束（否则选图 → 建局这条路径只有人工能验）。
    /// </summary>
    private void SelfCheckMapSelect()
    {
        MapSelectModel model = _select!;
        int[] builtins = [.. Enumerable.Range(0, model.Options.Count).Where(i => model.Options[i].Kind == MapOptionKind.Builtin)];
        int boardOption = Enumerable.Range(0, model.Options.Count).First(i => model.Options[i].IsBoard);
        const int FirstBuiltinStep = 6;
        int step = _selectStep++;
        string before = model.CurrentId;
        string what;
        bool ok;

        // 节点不泄漏：每步在上一帧的延迟释放落定之后读数。反复重搭预览之后，回到进入时那张图，棋盘子树的节点数与游离节点数都应回到进入时的值。
        // 第 0 步此刻还没刷新过（标注、信物标记等由 Refresh 搭），先刷一次，与后面各步"搭完并刷新过"的状态同口径。
        if (step == 0)
        {
            RefreshViews();
        }

        int boardNodes = CountNodes(_board);
        int orphans = (int)Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
        if (step == 0)
        {
            _selectEntryBoardNodes = boardNodes;
            _selectEntryOrphans = orphans;
        }

        // 预览与面板一致：预览地图就是当前标识，块数与人数（地图人数上限）都与面板显示的相同。
        bool BoardPreviewMatches() =>
            model.IsBoardSelected && _session.Match.Map.Id == model.CurrentId
            && _session.World.Board().Boards.Length == model.BoardCount && _previewMap!.MaxPlayers == model.Players;

        if (step == 0)
        {
            what = "选中随机棋盘图";
            OnSelectionChanged(model.Select(boardOption));
            ok = BoardPreviewMatches();
        }
        else if (step == 1)
        {
            what = "换一张";
            int playersBefore = model.Players;
            int boardsBefore = model.BoardCount;
            OnSelectionChanged(model.Reroll(NewMapSeed()));
            ok = model.CurrentId != before && model.Players == playersBefore && model.BoardCount == boardsBefore && BoardPreviewMatches();
        }
        else if (step == 2)
        {
            // 改人数：棋盘数回到该人数的缺省值、种子不变；标识的 :p 段只在非 4 人时出现。
            int delta = model.CanDecreasePlayers ? -1 : +1;
            what = $"人数 {(delta < 0 ? "−1" : "+1")}";
            int playersBefore = model.Players;
            ulong seedBefore = model.MapSeed;
            OnSelectionChanged(model.AdjustPlayers(delta));
            ok = model.CurrentId != before && model.Players == playersBefore + delta && model.MapSeed == seedBefore
                && model.BoardCount == BoardMapParameters.DefaultBoardsFor(model.Players)
                && model.CurrentId.Contains($":p{model.Players}", System.StringComparison.Ordinal) == (model.Players != BoardMapParameters.DefaultPlayers)
                && BoardPreviewMatches();
        }
        else if (step == 3)
        {
            what = "棋盘数 +1（到上限则 −1）";
            int boardsBefore = model.BoardCount;
            ulong seedBefore = model.MapSeed;
            int delta = model.CanIncreaseBoards ? +1 : -1;
            OnSelectionChanged(model.AdjustBoards(delta));
            ok = model.CurrentId != before && model.BoardCount == boardsBefore + delta && model.MapSeed == seedBefore
                && model.CurrentId.EndsWith($":n{model.BoardCount}", System.StringComparison.Ordinal) == (model.BoardCount != BoardMapParameters.DefaultBoardsFor(model.Players))
                && BoardPreviewMatches();
        }
        else if (step == 4)
        {
            what = "输入非法种子 abc";
            OnSelectionChanged(model.SubmitSeed("abc"));
            ok = model.CurrentId == before && model.Notice.Length > 0 && _session.Match.Map.Id == before;
        }
        else if (step == 5)
        {
            what = "输入种子 12345";
            OnSelectionChanged(model.SubmitSeed("12345"));
            ok = model.MapSeed == 12345UL && model.Notice.Length == 0 && BoardPreviewMatches();
        }
        else if (step < FirstBuiltinStep + builtins.Length)
        {
            // 内置棋盘图：不是随机棋盘图（种子与调节控件隐藏），预览的是带棋盘清单的棋盘档地图，出生棋盘数 = 地图人数上限 + 1。
            int option = builtins[step - FirstBuiltinStep];
            what = $"选中第 {step - FirstBuiltinStep + 1} 项内置棋盘图";
            OnSelectionChanged(model.Select(option));
            DefaultBoardView view = _session.World.Board();
            ok = model.Options[option].Kind == MapOptionKind.Builtin && !model.IsBoardSelected && _session.Match.Map.Id == model.CurrentId
                && !view.Boards.IsEmpty && view.Boards.Count(b => b.Kind == BoardPlateKind.Birth) == _previewMap!.MaxPlayers + 1;
        }
        else if (step == FirstBuiltinStep + builtins.Length)
        {
            what = "回到进入时的那一项";
            ok = model.TrySelectId(_selectEntryId);
            OnSelectionChanged(model.CurrentId != before);
            ok &= model.CurrentId == _selectEntryId && _session.Match.Map.Id == _selectEntryId;
        }
        else
        {
            what = $"开始（棋盘节点 {boardNodes} / 进入时 {_selectEntryBoardNodes}，游离节点 {orphans} / 进入时 {_selectEntryOrphans}）";
            StartMatch();
            RefreshViews();
            ok = !Selecting && _session.AwaitingZone && _session.Match.Map.Id == _selectEntryId && !_board.Rig.IsOverview && !_hud.MapSelectOpen
                && boardNodes == _selectEntryBoardNodes && orphans == _selectEntryOrphans && CountNodes(_board) == _selectEntryBoardNodes;
        }

        string shown = Selecting ? $"{model.CurrentId}（{PreviewInfo()}；全局预览 {(_board.Rig.IsOverview ? "开" : "关")}）" : $"已建局 {_session.Match.Map.Id}";
        GD.Print($"[map-select] 自检第 {step} 步「{what}」→ {shown}：{(ok ? "通过" : "失败")}");
        if (!ok)
        {
            GD.PrintErr($"[map-select] 自检失败于「{what}」：之前 {before}，提示「{model.Notice}」。");
            SetProcess(false);
            GetTree().Quit(1);
        }
    }

    private static int CountNodes(Node node)
    {
        int count = 1;
        foreach (Node child in node.GetChildren())
        {
            count += CountNodes(child);
        }

        return count;
    }

    /// <summary>当前预览地图的一行说明（尺寸、出生区数、可落子格数，棋盘图另有棋盘数）：文案由表现层给出，读默认棋盘视图模型，不读地图、不判规则。</summary>
    private string PreviewInfo() => MapPreviewInfo.Of(_session.World.Board());

    /// <summary>
    /// 点"开始"：按视图模型确认的标识建真正的对局，进入插旗；相机退出全局预览回到平时的初始位姿（最远缩放、地图中心）。
    /// </summary>
    private void StartMatch()
    {
        if (_select is null || _previewMap is null)
        {
            return;
        }

        string id = _select.Confirm();
        if (_previewMap.Id != id)
        {
            throw new System.InvalidOperationException($"选图界面确认的是 {id}，预览的却是 {_previewMap.Id}。");
        }

        _difficulty = _select.Difficulty;
        _session = MatchSession.Create(_previewMap, _matchSeed, System.Math.Min(4, _previewMap.MaxPlayers), 1, _difficulty, _cellLimit);
        _select = null;
        BeginSupply(_previewMap);   // 带入带出开启时：选图之后、插旗之前进补给阶段（本会话留作未插旗的预览，确认后按所选带入重建）
        _board.Build(_session.World.Board(), _session.ZoneOwners);
        if (_board.Rig.IsOverview)
        {
            _board.Rig.ToggleOverview();
        }

        ApplyCamera("选图确认");
        _hud.HideMapSelect();
        _hud.CameraHintVisible = !_board.Rig.FitsOneScreen;
        _dirty = true;
        GD.Print($"[siege] 地图 {id}，对局种子 {_matchSeed}，你是 {Labels.Player(_session.Me)}，{DifficultyText()}{(_autoDemo ? $"，自动演示模式（{(_rounds == 0 ? "跑到终局" : $"跑满 {_rounds} 个大回合停止")}）" : string.Empty)}");
    }
}
