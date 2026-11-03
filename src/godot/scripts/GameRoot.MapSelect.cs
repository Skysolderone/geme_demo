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
/// 显示随机取好的地图种子与完整标识 <c>link:&lt;种子&gt;:&lt;人数&gt;</c>，可输入种子、换一张、选人数（map-expand-25 D1：2 / 3 / 4）、选难度，点"开始"才建真正的对局、进入插旗。旧地图清单已删除。
/// </summary>
/// <remarks>
/// <para>新局状态机在零引擎依赖的 <see cref="MapSelectModel"/> 里，它只产出地图标识；地图一律经三个入口共用的目录解析，这里不自带清单、不自行出图。</para>
/// <para>预览不另写渲染：用候选地图建一个<b>尚未插旗</b>的会话，只为取它的默认棋盘视图模型交给 <see cref="BoardView.Build"/>（与开局插旗前的盘面同构）；
/// 确认开局时按最终选择重新建局。一屏看不全的图切全局预览（map-expand-25 D9：浮空岛一屏看不全，初始取景即整岛）。背景是各块出生平台的占位轮廓（规格「界面背景 SHALL 显示出生棋盘的占位轮廓，MUST NOT 显示棋盘内部布局」）：
/// 预览会话尚未插旗，按座位过滤后的默认棋盘没有任何格、只有各块占位，渲染层据此只画边框与编号（段 D-b，D18）。</para>
/// <para>该阶段不接受落子、推屏与缩放：<c>_Process</c> 不走 <c>Drive</c> / 悬停 / 平移，<c>_UnhandledInput</c> 整体不处理，
/// <c>_Input</c> 不认领相机键——否则 W A S D、空格、方向键进不了种子输入框。</para>
/// <para>大图经面板进来时在右侧大陆缩略上点格插旗（relic-sites 裁决 1–4）：像素 → 格由叠层经 <c>BoardGeometry.MinimapCellAt</c> 换算，合规与原因只问表现层的 <see cref="FlagPickModel"/>
/// （Core 唯一实现 <c>FlagRules.FlagCellProblem</c>）；点"插旗并开始"即建局、本机 <see cref="MatchSession.ChooseCell"/>、AI 盲抽、锁定、直接进构筑期。
/// 小图与 <c>--map=</c> 直接开局仍走原 3D 插旗阶段（裁决 5）。</para>
/// </remarks>
public sealed partial class GameRoot
{
    private MapSelectModel? _select;
    private MapData? _previewMap;
    private ImageTexture? _previewThumbnail;
    private string _previewThumbnailId = string.Empty;
    private DefaultBoardView? _previewBoard;
    private readonly FlagPickModel _pick = new();
    private Coord? _pendingFlag;
    private Coord? _selfCheckFlag;
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
    /// 进入新局界面并搭好第一张预览。<paramref name="preselect"/> 只来自 <c>--map-select --map=link:&lt;种子&gt;:&lt;人数&gt;</c>（截图 / 自检），只作为预填种子与人数；
    /// 启动时解析失败与其他命令行错误同样处理（由 <c>_Ready</c> 报错退出）。
    /// </summary>
    private MatchSession BeginMapSelect(string? preselect)
    {
        _select = new MapSelectModel(NewMapSeed(), _difficulty);
        if (preselect is not null && !_select.TrySelectId(preselect))
        {
            throw new System.FormatException($"--map-select 只能用完整的链接图标识预填种子与人数（{LinkMapId.Prefix}:<地图种子>:<人数>），{preselect} 不是：新局界面只有 {LinkMapId.Prefix}: 一种地图。");
        }

        MatchSession preview = PreviewSession();
        _select.Accept();
        _selectEntryId = _select.CurrentId;
        return preview;
    }

    /// <summary>按视图模型当前的标识解析地图，建一个尚未插旗的会话供预览（人数 = 地图人数，map-expand-25 D1）。</summary>
    private MatchSession PreviewSession()
    {
        MapData map = MapCatalog.Resolve(_select!.CurrentId);
        MatchSession preview = MatchSession.Create(map, _matchSeed, map.MaxPlayers, 1, AiDifficulty.Standard, _cellLimit);
        _previewMap = map;
        _pick.Bind(map);   // relic-sites 裁决 4：标识变了（换种子 / 人数 / 尺寸）即清空面板上已选的旗格
        return preview;
    }

    private void ConnectMapSelect()
    {
        _hud.MapSeedSubmitted += text => OnSelectionChanged(_select?.SubmitSeed(text));
        _hud.MapRerollPressed += () => OnSelectionChanged(_select?.Reroll(NewMapSeed()));
        _hud.MapStartPressed += StartMatch;
        _hud.MapPlayersPicked += players => OnSelectionChanged(_select?.SelectPlayers(players));   // 人数是标识的一部分：变了即重解析、重搭预览
        _hud.MapTierPicked += tier => OnSelectionChanged(_select?.SelectTier(tier));   // 尺寸同理（big-map 裁决 11）：选大即四段标识、预览换成 150×150 大陆
        // 大图缩略上的插旗（relic-sites 裁决 1 / 2）：悬停只换读数与框，点格经视图模型判合规（不合规只提示）；都只刷面板。
        _hud.MapThumbnailHovered += cell =>
        {
            _pick.SetHover(cell);
            _dirty = true;
        };
        _hud.MapThumbnailClicked += cell =>
        {
            _pick.Pick(cell);
            _dirty = true;
        };
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
        // 大图预览（big-map）：没有全局预览，相机停在画幅中心、分块流式只建中心附近（Build 末尾已按此位姿加载；这里兜住重定界后的位姿变化）。
        if (_board.UpdateStreaming())
        {
            _dirty = true;
        }
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
    /// 面板上选的旗格（relic-sites 裁决 3）：建局后（带入带出开启时是补给阶段重建会话之后）本机 <see cref="MatchSession.ChooseCell"/> → Core 盲抽其余玩家、锁定，
    /// 随后与 3D 插旗阶段锁定后同一条路（<see cref="AfterFlagsLocked"/>：重搭落座图、相机对准本机旗），直接进构筑期；无人值守时补记 <c>[flow]</c> 的"插旗锁定"审计。
    /// 没有待插的格、还在补给阶段、或 Core 拒绝（原因在 <see cref="MatchSession.Notice"/>）时返回 <c>false</c>，调用方照常建局进 3D 插旗阶段。
    /// </summary>
    private bool PlantPendingFlag()
    {
        if (_pendingFlag is not { } cell || Supplying)
        {
            return false;
        }

        _pendingFlag = null;
        if (!_session.AwaitingZone || !_session.IsLargeMap)
        {
            return false;
        }

        _session.ChooseCell(cell);
        if (_session.AwaitingZone)
        {
            GD.PrintErr($"[flag] 面板上选的旗格 {cell.ToNotation()} 被规则拒绝：{_session.Notice}；退回 3D 插旗阶段。");
            return false;
        }

        AfterFlagsLocked();
        if (_autoDemo)
        {
            DefaultBoardView locked = _session.World.Board();
            Flow(_audit.FlagsLocked(locked, _session.World.OwnZone ?? -1, _session.MyBattlefieldCells));
        }

        GD.Print($"[flag] 面板插旗：本机旗锁定在 {_session.Match.Flags.FlagCellOf(_session.Me)!.Value.ToNotation()}（面板上点的 {cell.ToNotation()}），直接进构筑期。{_session.Notice}");
        return true;
    }

    /// <summary>
    /// 无人值守自检（<c>--map-select --auto-demo</c>，规格 map-selection「强制进入新局界面的自检选项」）：每帧一步，把新局操作经与面板事件<b>同一组处理函数</b>走一遍——
    /// 输入非法种子 → 输入合法种子 → 换一张 → 选 2 人 → 选 3 人 → 切到另一尺寸 → 切回 → 回到进入时的标识 →（大图：未选格开始禁用 + 点不合规格 → 点合规格 → 换人数清空再回来 → 再点合规格）→ 开始；
    /// 随后照常自动演示。任何一步不符预期即以退出码 1 结束（否则新局 → 建局这条路径只有人工能验）。大图的"开始"即面板插旗并直接进构筑期（relic-sites 裁决 1–4）；小图入口（<c>--map=link:12345:4</c> 预填）步骤不变。
    /// </summary>
    private void SelfCheckMapSelect()
    {
        MapSelectModel model = _select!;
        int step = _selectStep++;
        string before = model.CurrentId;
        string what;
        bool ok;

        // 节点不泄漏：每步在上一帧的延迟释放落定之后读数。反复重搭预览之后，棋盘子树的节点数与游离节点数都应回到进入时的值（平台块数与边长随种子与人数变——
        // 开始那一步按"进入时的那张图"比，所以最后一步先把种子与人数换回进入时的值再开始）。
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
            // 预览会话未插旗：按座位过滤后没有任何格，只有各块占位（D18：背景只画占位轮廓，不显示棋盘布局；map-expand-25：块数 = 棋盘清单块数）。
            ok = model.MapSeed == 12345UL && model.Notice.Length == 0 && model.CurrentId == LinkMapId.Format(12345UL, model.Players, model.Tier) && _session.Match.Map.Id == model.CurrentId
                && BackdropOk();
        }
        else if (step == 2)
        {
            what = "换一张";
            OnSelectionChanged(model.Reroll(NewMapSeed()));
            ok = model.CurrentId != before && model.MapSeed != 12345UL && _session.Match.Map.Id == model.CurrentId
                && BackdropOk();
        }
        else if (step is 3 or 4)
        {
            // map-expand-25 D1：人数选项经与面板同一组处理函数（SelectPlayers → OnSelectionChanged）；标识带上人数、预览会话的人数 = 地图人数、种子不变。
            int players = step == 3 ? 2 : 3;
            ulong seedBefore = model.MapSeed;
            what = $"选 {players} 人";
            OnSelectionChanged(model.SelectPlayers(players));
            ok = model.Players == players && model.MapSeed == seedBefore && model.CurrentId == LinkMapId.Format(seedBefore, players, model.Tier)
                && _session.Match.Map.Id == model.CurrentId && _session.Match.Map.MaxPlayers == players && _session.Match.Players.Length == players
                && BackdropOk();
        }
        else if (step is 5 or 6)
        {
            // large-default 裁决 2 / 4：尺寸选项经与面板同一组处理函数（SelectTier → OnSelectionChanged）；第 5 步切到另一档、第 6 步切回，缺省大图与 --map= 小图预填两种入口都把两个方向走到。
            // 标识随档位换成四段 / 三段、种子与人数不变、预览背景按档位口径重搭；面板的整块大陆缩略只在大图显示——面板刷新发生在 RefreshViews，这里显式刷一次再读显隐。
            MapTier target = model.Tier == MapTier.Large ? MapTier.Small : MapTier.Large;
            ulong seedBefore = model.MapSeed;
            int playersBefore = model.Players;
            what = $"{(step == 5 ? "切到" : "切回")}尺寸「{Labels.TierName(target)}」";
            OnSelectionChanged(model.SelectTier(target));
            RefreshViews();
            ok = model.Tier == target && model.MapSeed == seedBefore && model.Players == playersBefore
                && model.CurrentId == LinkMapId.Format(seedBefore, playersBefore, target) && _session.Match.Map.Id == model.CurrentId
                && _session.IsLargeMap == (target == MapTier.Large) && BackdropOk()
                && _hud.MapThumbnailVisible == _session.IsLargeMap
                && (!_session.IsLargeMap || _previewBoard is not null);   // relic-sites：切回大图时缩略叠层的视图模型（遗址清单、尺寸）须与纹理一起就位
            what += $"，缩略 {(_hud.MapThumbnailVisible ? "显示" : "隐藏")}";
        }
        else if (step == 7)
        {
            what = "回到进入时的标识（种子、人数与尺寸）";
            ok = model.TrySelectId(_selectEntryId);
            OnSelectionChanged(model.CurrentId != before);
            ok &= model.CurrentId == _selectEntryId && _session.Match.Map.Id == _selectEntryId;
        }
        else if (step == 8 && model.Tier == MapTier.Large)
        {
            // relic-sites 裁决 3：大图未选格时"开始"禁用并提示；在缩略上点不合规格（A1：画布角，通常是留白格、不可落子；判据次序在前的那条先给原因）只给 Core 的原因、不选中、按钮仍禁用。
            // 点击经 Hud → 叠层像素 → BoardGeometry.MinimapCellAt → 视图模型，与鼠标同一条路径。
            var illegal = new Coord(0, 0);
            bool disabled = !_hud.MapStartEnabled && !_pick.CanStart && _pick.Picked is null && _hud.MapThumbnailVisible;
            what = $"大图未选格：开始{(disabled ? "禁用" : "竟然可用")}；在缩略上点不合规格 {illegal.ToNotation()}";
            _hud.ClickThumbnail(_hud.ThumbnailPixelOf(illegal));
            RefreshViews();
            ok = disabled && _pick.Picked is null && !_hud.MapStartEnabled && _pick.Notice.Length > 0;
            what += $" → 提示「{_pick.Notice}」";
        }
        else if (step is 9 or 11 && model.Tier == MapTier.Large)
        {
            // 裁决 1 / 2：点一个合规格（合规旗格集合的正中一格，经像素反算再按点击路径送回）→ 选中、按钮启用、文案"插旗并开始"。第 11 步是换人数清空之后再选一次。
            Coord legal = _session.DemoFlagCell();
            _selfCheckFlag = legal;
            what = $"{(step == 11 ? "重新" : string.Empty)}在缩略上点合规格 {legal.ToNotation()}";
            _hud.ClickThumbnail(_hud.ThumbnailPixelOf(legal));
            RefreshViews();
            ok = _pick.Picked == legal && _pick.IsLegal(legal) && _hud.MapStartEnabled && _hud.MapStartText == Labels.PlantAndStart;
        }
        else if (step == 10 && model.Tier == MapTier.Large)
        {
            // 裁决 4：换人数后选中格清空、按钮回禁用；再回到进入时的标识（仍是清空的）。
            what = "选 2 人后选中格清空、开始回禁用，再回到进入时的标识";
            OnSelectionChanged(model.SelectPlayers(2));
            RefreshViews();
            ok = _pick.Picked is null && !_hud.MapStartEnabled && model.Players == 2;
            ok &= model.TrySelectId(_selectEntryId);
            OnSelectionChanged(true);
            RefreshViews();
            ok &= model.CurrentId == _selectEntryId && _session.Match.Map.Id == _selectEntryId && _pick.Picked is null && !_hud.MapStartEnabled;
        }
        else
        {
            bool large = model.Tier == MapTier.Large;
            what = $"开始（棋盘节点 {boardNodes} / 进入时 {_selectEntryBoardNodes}，游离节点 {orphans} / 进入时 {_selectEntryOrphans}）";
            StartMatch();
            RefreshViews();
            if (large)
            {
                // relic-sites 裁决 3：点"插旗并开始"即建局、本机旗锁定在所点格、AI 盲抽、直接进构筑期；面板连同缩略一起撤掉。
                // 锁定后的盘面是落座图 + 旗心附近的区块，不再与进入时的预览比节点数（进入时的节点数与游离数仍在开始前核对）。
                Coord? mine = _session.Match.Flags.FlagCellOf(_session.Me);
                what += $"，面板插旗：本机旗 {mine?.ToNotation() ?? "无"}（所点 {_selfCheckFlag?.ToNotation()}），{(_session.AwaitingZone ? "仍在插旗阶段" : "已进构筑期")}，区块已加载 {_board.ChunkCounts.Loaded}，缩略 {(_hud.MapThumbnailVisible ? "残留" : "已撤")}";
                ok = !Selecting && !_session.AwaitingZone && mine is not null && mine == _selfCheckFlag && _session.Match.Map.Id == _selectEntryId && !_hud.MapSelectOpen && !_hud.MapThumbnailVisible
                    && _session.Match.Players.Length == _session.Match.Map.MaxPlayers
                    && boardNodes == _selectEntryBoardNodes && orphans == _selectEntryOrphans && _board.ChunkCounts.Loaded > 0;
            }
            else
            {
                // 插旗阶段的占位比新局界面的背景多一行主题名（link-map-terrain 6.1 / Q5：背景不带主题名）：建局后的节点数 = 进入时 + 主题名标注数，多出别的即是重搭泄漏。
                // map-expand-25 D9：建局后保持全局预览（整岛一屏看全）——一屏看不全的图上预览开着，看得全的图上没有预览。
                int themeLabels = _session.World.Board().Placeholders.Count(p => !p.Expanded && p.ThemeName is not null);
                int afterStart = CountNodes(_board);
                what += $"，建局后棋盘节点 {afterStart} = 进入时 + 主题名 {themeLabels}，全局预览 {(_board.Rig.IsOverview ? "开" : "关")}，缩略 {(_hud.MapThumbnailVisible ? "残留" : "已撤")}";
                ok = !Selecting && _session.AwaitingZone && _session.Match.Map.Id == _selectEntryId && _board.Rig.IsOverview == _board.Rig.OverviewAvailable && !_hud.MapSelectOpen && !_hud.MapThumbnailVisible
                    && _session.Match.Players.Length == _session.Match.Map.MaxPlayers
                    && boardNodes == _selectEntryBoardNodes && orphans == _selectEntryOrphans && afterStart == _selectEntryBoardNodes + themeLabels;
            }
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

    /// <summary>
    /// 预览会话（未插旗）的按座位棋盘：没有任何格，占位块数 = 地图出生座位数（seat-gap-25：= 人数，中立平台不给占位），
    /// 另有全部中立平台的轮廓（块数 = 棋盘清单里的公共棋盘数，至少 1 块——中立平台照常画出、不带编号）。
    /// </summary>
    private bool PlaceholdersOnly()
    {
        DefaultBoardView board = _session.World.Board();
        int neutral = _session.Match.Map.Boards.Count(b => b.Kind == BoardPlateKind.Public);
        return board.Cells.IsEmpty && board.Placeholders.Length > 0 && board.Placeholders.Length == _session.Match.Map.BirthZones.Length
            && neutral > 0 && board.NeutralPlates.Length == neutral;
    }

    /// <summary>
    /// 预览背景是否正确：小图只有占位（<see cref="PlaceholdersOnly"/>）；大图（big-map 裁决 11、段 B 追加裁决 33）是整张基图的全部格、没有占位、中立平台全部展开、走分块流式
    /// （map-selection「大图没有座位占位，背景就是整张基图的地形」）。`--map-select --auto-demo --map=link:<种子>:<人数>:l` 即按大图口径跑同一组步骤。
    /// </summary>
    private bool BackdropOk() => _session.IsLargeMap ? LargeBackdrop() : PlaceholdersOnly();

    private bool LargeBackdrop()
    {
        DefaultBoardView board = _session.World.Board();
        return board.Cells.Length == board.Width * board.Height && board.Placeholders.IsEmpty && board.NeutralPlates.IsEmpty && _board.Streaming;
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
    /// 新局面板大图的整块大陆缩略（large-default 裁决 4）：复用小地图的那一份地形像素图（<see cref="Minimap.BuildTerrain"/>），按预览会话的地图标识缓存、标识变了才重画；
    /// 小图返回 null（面板不显示缩略，小图预览与改前逐像素相同）。3D 预览照旧只建注视点附近的区块，缩略盖在其上；点"开始"后面板连同缩略一起撤掉。
    /// </summary>
    private Texture2D? PreviewThumbnail()
    {
        if (!_session.IsLargeMap)
        {
            _previewBoard = null;
            return null;
        }

        string id = _session.Match.Map.Id;
        if (_previewThumbnail is null || _previewBoard is null || _previewThumbnailId != id)
        {
            // 视图模型与纹理一起按标识缓存：缩略叠层（遗址标记、框）每次悬停都要读尺寸与遗址清单，不逐次重取 150×150 的视图。
            // 切到小图会把视图模型清掉而纹理留着（同标识切回时纹理可复用）：三者任一缺就整组重取，缩略叠层不能拿到 null 的视图模型（否则不画遗址、点不了格）。
            _previewBoard = _session.World.Board();
            _previewThumbnail = Minimap.BuildTerrain(_previewBoard);
            _previewThumbnailId = id;
        }

        return _previewThumbnail;
    }

    /// <summary>
    /// 点"开始"：按视图模型确认的标识建真正的对局（人数 = 地图人数），进入插旗；相机保持全局预览（map-expand-25 D9：整座岛一屏看全，锁定后开局对准自己的平台时退出预览）。
    /// </summary>
    private void StartMatch()
    {
        if (_select is null || _previewMap is null)
        {
            return;
        }

        if (!_pick.CanStart)
        {
            // relic-sites 裁决 3：大图未选格时"开始"本就禁用；这里再守一道（回车 / 自检路径），只刷面板提示。
            _dirty = true;
            return;
        }

        string id = _select.Confirm();
        if (_previewMap.Id != id)
        {
            throw new System.InvalidOperationException($"新局界面确认的是 {id}，预览的却是 {_previewMap.Id}。");
        }

        _difficulty = _select.Difficulty;
        _pendingFlag = _pick.Enabled ? _pick.Picked : null;   // 面板上选的旗格（裁决 3）：建局后立即插；带入带出开启时等补给阶段重建会话之后再插
        _pick.Bind(null);
        _session = MatchSession.Create(_previewMap, _matchSeed, _previewMap.MaxPlayers, 1, _difficulty, _cellLimit);
        _select = null;
        _previewThumbnail = null;   // 面板连同缩略一起撤掉（HideMapSelect 帧末释放节点），主场景不再持有那张纹理
        _previewThumbnailId = string.Empty;
        _previewBoard = null;
        BeginSupply(_previewMap);   // 带入带出开启时：新局之后、插旗之前进补给阶段（本会话留作未插旗的预览，确认后按所选带入重建）
        if (!PlantPendingFlag())
        {
            _board.Build(_session.World.Board(), _session.ZoneOwners);
            EnterOverviewForPlanting();
        }

        ApplyCamera("新局确认");
        _hud.HideMapSelect();
        _hud.CameraHintVisible = !_board.Rig.FitsOneScreen;
        _dirty = true;
        GD.Print($"[siege] 地图 {id}，对局种子 {_matchSeed}，你是 {Labels.Player(_session.Me)}，{DifficultyText()}{(_autoDemo ? $"，自动演示模式（{(_rounds == 0 ? "跑到终局" : $"跑满 {_rounds} 个大回合停止")}）" : string.Empty)}");
    }
}
