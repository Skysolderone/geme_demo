using System.Diagnostics;
using Godot;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Presentation.MapSelect;
using Siege.Presentation.Text;
using Siege.Presentation.Visibility;

namespace Siege.Godot;

/// <summary>
/// 建局之前的新局界面（board-link-mode D8 / 实现裁决 D7，规格 map-selection「开局选图界面」）：未给 <c>--map=</c> 且非无人值守时进入；
/// 显示随机取好的地图种子与完整标识 <c>link:&lt;种子&gt;</c>，可输入种子、换一张、选难度，点"开始"才建真正的对局、进入插旗。旧地图清单已删除。
/// </summary>
/// <remarks>
/// <para>新局状态机在零引擎依赖的 <see cref="MapSelectModel"/> 里，它只产出地图标识；地图一律经三个入口共用的目录解析，这里不自带清单、不自行出图。</para>
/// <para>预览不另写渲染：用候选地图建一个<b>尚未插旗</b>的会话，只为取它的默认棋盘视图模型交给 <see cref="BoardView.Build"/>（与开局插旗前的盘面同构）；
/// 确认开局时按最终选择重新建局。一屏看不全的图切全局预览。背景是 5 块出生棋盘的占位轮廓（规格「界面背景 SHALL 显示 5 块出生棋盘的占位轮廓，MUST NOT 显示棋盘内部布局」）：
/// 预览会话尚未插旗，按座位过滤后的默认棋盘没有任何格、只有 5 个占位，渲染层据此只画边框与编号（段 D-b，D18）。</para>
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

    /// <summary>是否处于新局界面。</summary>
    private bool Selecting => _select is not null;

    /// <summary>取一个新的地图种子：入口最外层读一次计数器，折成便于读写的短种子（视图模型与规则内核都不读时钟）。三个入口共用同一个折叠函数。</summary>
    private static ulong NewMapSeed() => LinkMapId.FriendlySeed((ulong)Stopwatch.GetTimestamp());

    /// <summary>
    /// 进入新局界面并搭好第一张预览。<paramref name="preselect"/> 只来自 <c>--map-select --map=link:&lt;种子&gt;</c>（截图 / 自检），只作为预填种子；
    /// 启动时解析失败与其他命令行错误同样处理（由 <c>_Ready</c> 报错退出）。
    /// </summary>
    private MatchSession BeginMapSelect(string? preselect)
    {
        _select = new MapSelectModel(NewMapSeed(), _difficulty);
        if (preselect is not null && !_select.TrySelectId(preselect))
        {
            throw new System.FormatException($"--map-select 只能用完整的链接图标识预填种子（{LinkMapId.Prefix}:<地图种子>），{preselect} 不是：新局界面只有 {LinkMapId.Prefix}: 一种地图。");
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
        _hud.MapSeedSubmitted += text => OnSelectionChanged(_select?.SubmitSeed(text));
        _hud.MapRerollPressed += () => OnSelectionChanged(_select?.Reroll(NewMapSeed()));
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
                _board.Build(MapPreviewInfo.Backdrop(_session.World.Board()), _session.ZoneOwners);   // 新局界面的占位不带主题名（link-map-terrain Q5）
                GD.Print($"[siege] 新局预览：{_select.CurrentId}");
            }
            catch (System.Exception ex) when (ex is MapGenerationException or MapValidationException or System.FormatException or System.IO.FileNotFoundException)
            {
                _select.RollBack($"这张图没有生成出来：{ex.Message}");
                GD.PrintErr($"[siege] 新局预览失败：{ex.Message}");
            }
        }

        _dirty = true;
    }

    /// <summary>新局界面的单帧：只同步宽高比、维持预览位姿、按需刷新；不推进对局、不采悬停与平移。</summary>
    private void ProcessMapSelect()
    {
        SyncAspect();

        // 一屏看不全的图切全局预览（换到外接矩形不同的图会重建相机视图模型，故逐帧核对）；一屏看全的图 ToggleOverview 不动相机。
        if (!_board.Rig.FitsOneScreen && !_board.Rig.IsOverview)
        {
            _board.Rig.ToggleOverview();
        }

        // 反过来：从一屏看不全的图换回看得全的图时，BoardCamera.Rebound 自己退出残留的预览（原 DropStaleOverview 已收进 Rebound）。
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
    /// 无人值守自检（<c>--map-select --auto-demo</c>，规格 map-selection「强制进入新局界面的自检选项」）：每帧一步，把新局操作经与面板事件<b>同一组处理函数</b>走一遍——
    /// 输入非法种子 → 输入合法种子 → 换一张 → 开始；随后照常自动演示。任何一步不符预期即以退出码 1 结束（否则新局 → 建局这条路径只有人工能验）。
    /// </summary>
    private void SelfCheckMapSelect()
    {
        MapSelectModel model = _select!;
        int step = _selectStep++;
        string before = model.CurrentId;
        string what;
        bool ok;

        // 节点不泄漏：每步在上一帧的延迟释放落定之后读数。反复重搭预览之后，棋盘子树的节点数与游离节点数都应回到进入时的值（5 块出生棋盘的格数不随种子变，只随边长变——
        // 开始那一步按"进入时的那张图"比，所以最后一步先把种子换回进入时的值再开始）。
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

        if (step == 0)
        {
            what = "输入非法种子 abc";
            OnSelectionChanged(model.SubmitSeed("abc"));
            ok = model.CurrentId == before && model.Notice.Length > 0 && model.SeedText == "abc" && _session.Match.Map.Id == before;
        }
        else if (step == 1)
        {
            what = "输入种子 12345";
            OnSelectionChanged(model.SubmitSeed("12345"));
            // 预览会话未插旗：按座位过滤后没有任何格，只有 5 个占位（D18：背景只画占位轮廓，不显示棋盘布局）。
            ok = model.MapSeed == 12345UL && model.Notice.Length == 0 && model.CurrentId == "link:12345" && _session.Match.Map.Id == model.CurrentId
                && _session.World.Board().Placeholders.Length == 5 && _session.World.Board().Cells.IsEmpty;
        }
        else if (step == 2)
        {
            what = "换一张";
            OnSelectionChanged(model.Reroll(NewMapSeed()));
            ok = model.CurrentId != before && model.MapSeed != 12345UL && _session.Match.Map.Id == model.CurrentId
                && _session.World.Board().Placeholders.Length == 5 && _session.World.Board().Cells.IsEmpty;
        }
        else if (step == 3)
        {
            what = "回到进入时的种子";
            ok = model.TrySelectId(_selectEntryId);
            OnSelectionChanged(model.CurrentId != before);
            ok &= model.CurrentId == _selectEntryId && _session.Match.Map.Id == _selectEntryId;
        }
        else
        {
            what = $"开始（棋盘节点 {boardNodes} / 进入时 {_selectEntryBoardNodes}，游离节点 {orphans} / 进入时 {_selectEntryOrphans}）";
            StartMatch();
            RefreshViews();
            // 插旗阶段的占位比新局界面的背景多一行主题名（link-map-terrain 6.1 / Q5：背景不带主题名）：建局后的节点数 = 进入时 + 主题名标注数，多出别的即是重搭泄漏。
            int themeLabels = _session.World.Board().Placeholders.Count(p => !p.Expanded && p.ThemeName is not null);
            int afterStart = CountNodes(_board);
            what += $"，建局后棋盘节点 {afterStart} = 进入时 + 主题名 {themeLabels}";
            ok = !Selecting && _session.AwaitingZone && _session.Match.Map.Id == _selectEntryId && !_board.Rig.IsOverview && !_hud.MapSelectOpen
                && boardNodes == _selectEntryBoardNodes && orphans == _selectEntryOrphans && afterStart == _selectEntryBoardNodes + themeLabels;
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

    /// <summary>当前预览地图的一行说明（尺寸、出生区数、可落子格数、棋盘数）：文案由表现层给出，读默认棋盘视图模型，不读地图、不判规则。</summary>
    private string PreviewInfo() => MapPreviewInfo.Of(_session.World.Board());

    /// <summary>
    /// 点"开始"：按视图模型确认的标识建真正的对局，进入插旗；相机退出全局预览回到平时的初始位姿。
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
            throw new System.InvalidOperationException($"新局界面确认的是 {id}，预览的却是 {_previewMap.Id}。");
        }

        _difficulty = _select.Difficulty;
        _session = MatchSession.Create(_previewMap, _matchSeed, System.Math.Min(4, _previewMap.MaxPlayers), 1, _difficulty, _cellLimit);
        _select = null;
        BeginSupply(_previewMap);   // 带入带出开启时：新局之后、插旗之前进补给阶段（本会话留作未插旗的预览，确认后按所选带入重建）
        _board.Build(_session.World.Board(), _session.ZoneOwners);
        if (_board.Rig.IsOverview)
        {
            _board.Rig.ToggleOverview();
        }

        ApplyCamera("新局确认");
        _hud.HideMapSelect();
        _hud.CameraHintVisible = !_board.Rig.FitsOneScreen;
        _dirty = true;
        GD.Print($"[siege] 地图 {id}，对局种子 {_matchSeed}，你是 {Labels.Player(_session.Me)}，{DifficultyText()}{(_autoDemo ? $"，自动演示模式（{(_rounds == 0 ? "跑到终局" : $"跑满 {_rounds} 个大回合停止")}）" : string.Empty)}");
    }
}
