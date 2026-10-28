using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Godot;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.PieceRelics;
using Siege.Presentation.Hud;
using Siege.Presentation.Text;
using Siege.Presentation.Visibility;

namespace Siege.Godot;

/// <summary>
/// 带入带出的入口（relic-redesign R25 / D8，段 C2）：<c>--profile=&lt;路径&gt;</c> / <c>--no-carry</c>、选图之后插旗之前的带入面板（看档案、兑换、选 ≤ 5 件、勾 ≤ 2 件保险）、
/// 部署阶段的带入栏（每枚暂放点选一件挂上）、弃赛、结算面板（本人结局 + 每件信物去向）与关窗提示。
/// </summary>
/// <remarks>
/// <para>档案、校验与结算都在 Core：本类只读命令行、构造档案存取（注入墙钟——Core 不读时钟，图形版里唯一一处系统墙钟在 <see cref="ReadCarryArgs"/> 里，只用于损坏备份的文件名）、
/// 转发面板事件。带入必须在建局时进 <c>MatchOptions</c>，所以带入面板在建局之前：先用未插旗的预览会话显示盘面，确认后按所选带入重建对局（AI 同等件数由 Core 的 <c>RelicCarryAi</c> 抽）。</para>
/// <para><c>--auto-demo</c>、<c>--pick-check</c>、截图等自动化模式一律关闭带入带出、不构造档案存取（<c>_carryStore</c> 为 <c>null</c> → <see cref="BeginSupply"/> 直接返回 <c>false</c> → 对局以关闭带入带出开局），档案文件不被读写。
/// <c>--carry-preview=supply|settlement</c> 只做展示：用内存里的示例档案 / 示例结算挂出面板供截图，同样不碰任何档案。</para>
/// </remarks>
public sealed partial class GameRoot
{
    private RelicProfileStore? _carryStore;
    private MapData? _supplyMap;
    private SupplyPanelData? _supplyData;
    private string? _carryPreview;
    private bool _supplyCarryOff;
    private bool _destinationsShown;
    private readonly List<string> _supplyNotices = [];
    private readonly CarryPicker _picker = new();

    /// <summary>是否处于带入面板阶段（建局之前）。</summary>
    private bool Supplying => _supplyMap is not null;

    /// <summary>
    /// 读带入带出的启动选项（在 <c>EnsureRecognized</c> 之前调用）。严格解析与终端同口径：<c>--no-carry</c> 是开关，两者不能同时给出。
    /// 自动化模式（<paramref name="unattended"/>）一律关闭；否则缺省开启、读写缺省档案 <c>%APPDATA%\Siege\profile.json</c>（与终端共用一份）。
    /// </summary>
    private void ReadCarryArgs(LaunchArgs args, bool unattended)
    {
        string? path = args.Text("profile", "档案路径");
        bool noCarry = args.Flag("no-carry");
        _carryPreview = args.Text("carry-preview", "supply 或 settlement");
        if (_carryPreview is not (null or "supply" or "settlement"))
        {
            throw new System.FormatException($"--carry-preview={_carryPreview} 无效：应为 supply 或 settlement。");
        }

        if (noCarry && path is not null)
        {
            throw new System.FormatException("--no-carry 关闭带入带出、不读写档案，不能与 --profile= 同时给出。");
        }

        if (unattended || _carryPreview is not null)
        {
            if (path is not null)
            {
                GD.Print("[carry] 自动化 / 预览模式关闭带入带出，--profile= 不生效，不读写任何档案。");
            }

            return;
        }

        if (!noCarry)
        {
            _carryStore = new RelicProfileStore(path ?? RelicProfileStore.DefaultPath(), TimeProvider.System.GetLocalNow);
        }
    }

    /// <summary>
    /// 要开真正的对局之前：带入带出开启时进入带入面板阶段（当前会话留作未插旗的预览，返回 <c>true</c>）；否则返回 <c>false</c>，调用方照常开局。
    /// 档案来自更新的版本、或人数没有点数表时本局按关闭进行（提示打印在控制台与通知条）。
    /// </summary>
    private bool BeginSupply(MapData map)
    {
        _picker.Clear();
        if (_carryPreview == "supply")
        {
            _supplyNotices.Clear();
            _supplyNotices.Add("预览：示例档案（内存中），不读写任何档案文件。");
            var sample = new RelicProfile
            {
                Points = 17,
                Inventory = ImmutableSortedDictionary<RelicKind, int>.Empty.Add(RelicKind.WarDrum, 1).Add(RelicKind.Watchtower, 2).Add(RelicKind.Banner, 1),
            };
            _picker.Add(RelicKind.WarDrum);
            _picker.ToggleInsured(0);
            _picker.Add(RelicKind.Watchtower);
            _supplyMap = map;
            _supplyData = new SupplyPanelData([.. _supplyNotices], sample, _picker, string.Empty, CarryAllowed: true, Preview: true);
            _dirty = true;
            return true;
        }

        if (_carryStore is null)
        {
            return false;
        }

        _supplyNotices.Clear();
        RelicProfileLoad load = _carryStore.Load();
        _supplyNotices.Add(load.Message);
        GD.Print($"[carry] {load.Message}");
        _supplyCarryOff = load.Status == RelicProfileLoadStatus.NewerVersion;
        if (_supplyCarryOff)
        {
            // 版本不符：档案只读不写，本局关闭带入带出；提示仍在带入面板上显示，面板只剩"开始（不带入）"。
            _supplyMap = map;
            _supplyData = new SupplyPanelData([.. _supplyNotices], RelicProfile.Empty, _picker, "本局不提供带入、局终不结算。", CarryAllowed: false, Preview: false);
            _dirty = true;
            return true;
        }

        int players = map.MaxPlayers;
        if (!RelicPoints.Supports(players))
        {
            GD.Print($"[carry] {players} 人局没有名次补给点表，本局不提供带入、局终不结算。");
            return false;
        }

        if (_carryStore.SettleAbandoned() is { } abandoned)
        {
            CarriedRelic[] returned = [.. abandoned.Carried.Where(c => c.Insured)];
            string back = returned.Length == 0 ? "没有保险件" : $"保险件 {string.Join("、", returned.Select(c => CarryTexts.Relic(c.Kind)))} 已归还";
            _supplyNotices.Add($"上一局中途退出：{back}，其余带入件丢失，补给点不变。");
        }

        _supplyMap = map;
        RefreshSupplyData(string.Empty);
        GD.Print($"[carry] 信物带入阶段：{CarryTexts.Stock(_carryStore.Current)}（档案 {_carryStore.FilePath}）");
        return true;
    }

    private void RefreshSupplyData(string feedback)
    {
        _supplyData = new SupplyPanelData([.. _supplyNotices], _carryStore!.Current, _picker, feedback, CarryAllowed: true, Preview: false);
        _dirty = true;
    }

    /// <summary>带入面板能否操作：有档案存取、在带入阶段、档案版本相符。预览模式按钮不接任何操作。</summary>
    private bool SupplyInteractive => _carryStore is not null && Supplying && !_supplyCarryOff;

    private void ConnectCarry()
    {
        _hud.SupplyExchangePressed += kind =>
        {
            if (!SupplyInteractive)
            {
                return;
            }

            bool ok = _carryStore!.TryExchange(kind, out string? refusal);
            RefreshSupplyData(ok ? $"已兑换 1 件{CarryTexts.Relic(kind)}。" : $"兑换失败：{refusal}。");
        };
        _hud.SupplyAddPressed += kind =>
        {
            if (!SupplyInteractive)
            {
                return;
            }

            _picker.Add(kind);
            RefreshSupplyData(string.Empty);
        };
        _hud.SupplyRemovePressed += index =>
        {
            if (SupplyInteractive && _picker.Remove(index))
            {
                RefreshSupplyData(string.Empty);
            }
        };
        _hud.SupplyInsurePressed += index =>
        {
            if (SupplyInteractive && _picker.ToggleInsured(index))
            {
                RefreshSupplyData(string.Empty);
            }
        };
        _hud.SupplyStartPressed += () => FinishSupply(_picker.Loadout);
        _hud.AttachPicked += (coord, index) =>
        {
            _session.AttachCarried(coord, index);
            _dirty = true;
        };
        _hud.ResignConfirmed += () =>
        {
            CutShow();   // 演出中弃赛：演出先以终态结束（settlement-show A5）
            _session.Resign();
            _dirty = true;
            ShowCarrySettlement(_session.CarrySettled);
        };
    }

    /// <summary>带入面板"开始"：按所选带入建真正的对局（AI 同等件数），开局登记在途记录（<see cref="RelicProfileStore.Begin"/>），进入插旗。</summary>
    private void FinishSupply(RelicLoadout mine)
    {
        if (_supplyMap is not { } map || _carryStore is null)
        {
            return;   // 预览模式没有档案存取：面板只供截图，不开局
        }

        if (_supplyCarryOff)
        {
            // 版本不符：本局按关闭带入带出开局，不写档案。
            _session = MatchSession.Create(map, _matchSeed, map.MaxPlayers, 1, _difficulty, _cellLimit);
        }
        else
        {
            if (mine.RefusalAgainst(_carryStore.Current) is { } refusal)
            {
                RefreshSupplyData($"不能这样带入：{refusal}。");
                return;
            }

            _session = MatchSession.Create(map, _matchSeed, map.MaxPlayers, 1, _difficulty, _cellLimit, carryInOut: true, carry: mine);
            // 对局标识只用于在途记录的"同一局只结算一次"：种子 + 计时器读数（不参与任何对局随机）。
            _session.BeginCarry(_carryStore, $"{_matchSeed:X16}-{Stopwatch.GetTimestamp():X}");
            GetTree().AutoAcceptQuit = false;   // 有带入时关窗先提示（见 OnCarryNotification）
        }

        _destinationsShown = false;
        _supplyMap = null;
        _supplyData = null;
        _hud.HideSupply();
        _board.Build(_session.World.Board(), _session.ZoneOwners);
        _dirty = true;
        GD.Print(_session.CarryStore is null ? "[carry] 本局关闭带入带出。" : $"[carry] 开局{CarryTexts.Loadout(_session.MyCarry)}；{CarryTexts.Stock(_carryStore.Current)}");
    }

    /// <summary>带入阶段的单帧：只同步宽高比与相机、按需刷新；不推进对局、不采悬停与平移。</summary>
    private void ProcessSupply()
    {
        SyncAspect();
        ApplyCamera("带入");
        if (_dirty)
        {
            _dirty = false;
            RefreshViews();
        }
    }

    /// <summary>带入阶段的面板刷新（由 <c>RefreshViews</c> 调用）。</summary>
    private void RefreshSupply() => _hud.ShowSupply(_supplyData!);

    /// <summary>
    /// 对局面板刷新之后：带入栏（本人未挂出的清单，私有，R22；本人部署阶段再列每枚暂放的挂出下拉，R14）与弃赛按钮。
    /// 下拉里不列已挂在别的暂放上的件；落点上有地图信物（默认棋盘的 <see cref="RelicMarker.OnMap"/>，落子即拾取）时禁用并写原因。合法性最终由 Core 在 <see cref="MatchSession.AttachCarried"/> 判。
    /// </summary>
    private void RefreshCarry()
    {
        if (Unattended && !_session.Match.CarryInOut)
        {
            _hud.RefreshCarryBar(null, false);   // 自动化模式画面与引入之前相同
            return;
        }

        bool inProgress = !_session.AwaitingZone && !_session.IsOver;
        bool canResign = _session.CanResign && !Unattended;
        if (!_session.Match.CarryInOut)
        {
            _hud.RefreshCarryBar(inProgress ? new CarryBarData("带入带出未开启", []) : null, canResign);
            return;
        }

        ImmutableArray<SeatCarriedView> pending = _session.PendingCarried;
        string text = CarryTexts.Pending(pending.Select(c => KeyValuePair.Create(c.Index, new CarriedRelic(c.Kind, c.Insured))))
            + (pending.IsEmpty ? string.Empty : "（只有你看得到，挂上即公开）");
        ImmutableArray<CarryAttachRow>.Builder rows = ImmutableArray.CreateBuilder<CarryAttachRow>();
        if (_session.IsMyTurn && _session.Match.Stage == TurnStage.Deploy && !pending.IsEmpty)
        {
            DefaultBoardView board = _session.World.Board();
            ImmutableArray<Placement> staged = _session.Staged;
            foreach (Placement placement in staged)
            {
                BoardCellView? cell = board.Cells.FirstOrDefault(c => c.Coord == placement.Coord);
                string? blocked = cell is { Relic: RelicMarker.OnMap } ? "落点有地图信物（落子即拾取），不能再挂" : null;
                HashSet<int> elsewhere = [.. staged.Where(p => p.Coord != placement.Coord && p.AttachCarried is not null).Select(p => p.AttachCarried!.Value)];
                ImmutableArray<CarryAttachOption> options =
                [
                    .. pending.Where(c => !elsewhere.Contains(c.Index))
                        .Select(c => new CarryAttachOption(c.Index, $"[{c.Index + 1}] {CarryTexts.Carried(new CarriedRelic(c.Kind, c.Insured))}")),
                ];
                rows.Add(new CarryAttachRow(placement.Coord, Labels.Piece(placement.Type), placement.AttachCarried, blocked, options));
            }
        }

        _hud.RefreshCarryBar(new CarryBarData(text, rows.ToImmutable()), canResign);
    }

    /// <summary>
    /// 每帧推进之后：本机玩家出局或对局终局即结算并弹出结算面板（弃赛在按钮回调里已结算）；本机中途已结算（弃赛 / 出局）的，终局时再弹一次列出每件信物的全局去向。
    /// 演出播完再弹（与终局中央面板同口径）。
    /// </summary>
    private void CheckCarrySettlement()
    {
        if (ShowPlaying)
        {
            return;
        }

        if (_session.SettleCarryIfDue() is { } settled)
        {
            ShowCarrySettlement(settled);
            _dirty = true;
        }
        else if (!_destinationsShown && _session.IsOver && _session.CarryStore is not null && _session.CarrySettled is { } early)
        {
            ShowCarrySettlement(early);
            _dirty = true;
        }
    }

    /// <summary>
    /// 结算面板：本人一行（结局、名次、点数、带出的信物）+ 每件信物的去向——终局时读 <see cref="MatchSession.FinalDestinations"/>（D6：谁带走了哪些、哪些消失），
    /// 中途（弃赛 / 出局）只写 R17 的说明，不编造他人的去向。
    /// </summary>
    private void ShowCarrySettlement(RelicPlayerSettlement? settled)
    {
        if (settled is null || _session.CarryStore is not { } store)
        {
            return;
        }

        GetTree().AutoAcceptQuit = true;   // 已结算：关窗不再有可丢的信物
        string headline = CarryTexts.Settlement(settled, PlayerName);
        IReadOnlyList<string> lines;
        if (_session.FinalDestinations() is { } all)
        {
            lines = CarryTexts.Destinations(all, PlayerName);
            _destinationsShown = true;
        }
        else
        {
            lines = settled.Outcome.IsFinisher ? [] : ["未挂出的非保险带入件已掉到地图供争夺（保险件带回）；每件信物的最终去向在终局时列出。"];
        }

        _hud.ShowSettlement(headline, lines, "结算后：" + CarryTexts.Stock(store.Current));
        GD.Print($"[carry] 结算：{headline}；{string.Join("；", lines)}；{CarryTexts.Stock(store.Current)}");
    }

    /// <summary>
    /// 关窗（carry-in-out「中途退出与截断」）：本机玩家有带入且尚未结算时先提示"退出将丢失…弃赛可带回…"，确认后才退出；
    /// 其余情形直接退出。未确认就被杀进程的，残留的在途记录在下次开局前按中途退出结算。
    /// </summary>
    private void OnCarryNotification(int what)
    {
        if (what != NotificationWMCloseRequest || GetTree().AutoAcceptQuit)
        {
            return;
        }

        if (_session.CarryStore is null || _session.CarrySettled is not null || _session.MyCarry.Count == 0 || _session.IsOver)
        {
            GetTree().Quit();
            return;
        }

        _hud.ShowConfirm("退出将丢失带入的非保险信物；弃赛可带回保险件并带出一半补给点。确认退出？", "确认退出", () => GetTree().Quit());
    }

    /// <summary>
    /// <c>--carry-preview=settlement</c>：对局面板照常，叠一张示例结算面板（4 人局完赛第 2 名、带出一件保险战鼓与一件地图军旗，另一名玩家带走一件、一件消失），只做展示、不碰档案。
    /// </summary>
    private void ShowCarryPreview()
    {
        if (_carryPreview != "settlement")
        {
            return;
        }

        var me = new PlayerId(0);
        var rival = new PlayerId(2);
        var mine = new RelicPlayerSettlement(me, RelicPlayerOutcome.Finished(2), RelicPoints.For(4, 2),
            [RelicToken.Carried(0, RelicKind.WarDrum, me, insured: true), RelicToken.Spawned(5, RelicKind.Banner)]);
        var theirs = new RelicPlayerSettlement(rival, RelicPlayerOutcome.Finished(1), RelicPoints.For(4, 1), [RelicToken.Carried(1, RelicKind.Command, me, insured: false)]);
        var all = new RelicMatchSettlement(
            ImmutableSortedDictionary<PlayerId, RelicPlayerSettlement>.Empty.Add(me, mine).Add(rival, theirs),
            [RelicToken.Carried(3, RelicKind.Watchtower, rival, insured: false)]);
        var after = new RelicProfile
        {
            Points = 33,
            Inventory = ImmutableSortedDictionary<RelicKind, int>.Empty.Add(RelicKind.WarDrum, 2).Add(RelicKind.Banner, 1),
        };
        string Name(PlayerId p) => p == me ? "你" : Labels.Player(p);
        _hud.ShowSettlement(CarryTexts.Settlement(mine, Name), CarryTexts.Destinations(all, Name), "结算后：" + CarryTexts.Stock(after) + "（预览：示例结算，不读写任何档案）");
    }

    /// <summary>结算文案里的玩家名：本机玩家写"你"。</summary>
    private string PlayerName(PlayerId p) => p == _session.Me ? "你" : Labels.Player(p);

    /// <summary>截图取景自证的一行：两个带入带出面板的开关与位置。</summary>
    private string CarryShotLine()
    {
        Rect2 supply = _hud.SupplyRect;
        Rect2 settlement = _hud.SettlementRect;
        string stock = _supplyData is { } data ? string.Join("、", Hud.SupplyLines(data.Current)) + $"；已选 {CarryTexts.Loadout(data.Picker.Loadout)}" : "—";
        return $"[carry] 带入面板 {(_hud.SupplyOpen ? $"开 ({supply.Position.X:0}, {supply.Position.Y:0}) {supply.Size.X:0}×{supply.Size.Y:0}" : "关")}，"
            + $"结算面板 {(_hud.SettlementOpen ? $"开 ({settlement.Position.X:0}, {settlement.Position.Y:0}) {settlement.Size.X:0}×{settlement.Size.Y:0}" : "关")}；信物 {stock}；"
            + $"档案存取 {(_carryStore is null ? "无（不读写档案）" : _carryStore.FilePath)}";
    }
}
