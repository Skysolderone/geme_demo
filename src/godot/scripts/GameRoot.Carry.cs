using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Godot;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Carry;
using Siege.Presentation.Text;

namespace Siege.Godot;

/// <summary>
/// 带入带出的入口（carry-in-out D9 / D10）：<c>--profile=&lt;路径&gt;</c> / <c>--no-carry</c>、选图之后插旗之前的补给阶段、弃赛、结算面板与关窗提示。
/// </summary>
/// <remarks>
/// <para>档案与结算都在 Core：本类只读命令行、构造档案存取（注入墙钟——Core 不读时钟，图形版里唯一一处系统墙钟在 <see cref="ReadCarryArgs"/> 里，只用于损坏备份的文件名）、
/// 转发面板事件。带入必须在建局时进 <c>MatchOptions</c>，所以补给面板在建局之前：先用未插旗的预览会话显示盘面，确认后按所选带入重建对局。</para>
/// <para><c>--auto-demo</c>、<c>--pick-check</c>、截图等自动化模式一律关闭带入带出、不构造档案存取，档案文件不被读写。
/// <c>--carry-preview=supply|settlement</c> 只做展示：用内存里的示例档案 / 示例结算挂出面板供截图，同样不碰任何档案。</para>
/// </remarks>
public sealed partial class GameRoot
{
    private CarryProfileStore? _carryStore;
    private MapData? _supplyMap;
    private SupplyPanelData? _supplyData;
    private string? _carryPreview;
    private bool _supplyCarryOff;
    private readonly List<string> _supplyNotices = [];

    /// <summary>是否处于补给选择阶段（建局之前）。</summary>
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
            _carryStore = new CarryProfileStore(path ?? CarryProfileStore.DefaultPath(), TimeProvider.System.GetLocalNow);
        }
    }

    /// <summary>
    /// 要开真正的对局之前：带入带出开启时进入补给阶段（当前会话留作未插旗的预览，返回 <c>true</c>）；否则返回 <c>false</c>，调用方照常开局。
    /// 档案来自更新的版本、或人数没有点数表时本局按关闭进行（提示打印在控制台与通知条）。
    /// </summary>
    private bool BeginSupply(MapData map)
    {
        if (_carryPreview == "supply")
        {
            _supplyNotices.Clear();
            _supplyNotices.Add("预览：示例档案（内存中），不读写任何档案文件。");
            var sample = new CarryProfile
            {
                Points = 17,
                Inventory = ImmutableSortedDictionary<SupplyKind, int>.Empty.Add(SupplyKind.SpareStone, 1).Add(SupplyKind.Commission, 2),
            };
            _supplyMap = map;
            _supplyData = new SupplyPanelData([.. _supplyNotices], sample, CarryCandidates.Of(_session.Match.ContentSet), string.Empty, CarryAllowed: true, Preview: true);
            _dirty = true;
            return true;
        }

        if (_carryStore is null)
        {
            return false;
        }

        _supplyNotices.Clear();
        CarryProfileLoad load = _carryStore.Load();
        _supplyNotices.Add(load.Message);
        GD.Print($"[carry] {load.Message}");
        _supplyCarryOff = load.Status == CarryProfileLoadStatus.NewerVersion;
        if (_supplyCarryOff)
        {
            // 版本不符：档案只读不写，本局关闭带入带出；提示仍在补给面板上显示，面板只剩"不带入，开始"。
            _supplyMap = map;
            _supplyData = new SupplyPanelData([.. _supplyNotices], CarryProfile.Empty, CarryCandidates.Of(_session.Match.ContentSet), "本局不提供带入、局终不结算。", CarryAllowed: false, Preview: false);
            _dirty = true;
            return true;
        }

        int players = System.Math.Min(4, map.MaxPlayers);
        if (!CarryPoints.Supports(players))
        {
            GD.Print($"[carry] {players} 人局没有名次补给点表，本局不提供带入、局终不结算。");
            return false;
        }

        if (_carryStore.SettleAbandoned() is { } abandoned)
        {
            string lost = abandoned.Carry is { } c ? $"{CarryTexts.Supply(c.Kind)}已丢失" : "上一局未带入补给";
            _supplyNotices.Add($"上一局中途退出：{lost}，补给点不变。");
        }

        _supplyMap = map;
        RefreshSupplyData(string.Empty);
        GD.Print($"[carry] 补给阶段：{CarryTexts.Stock(_carryStore.Current)}（档案 {_carryStore.FilePath}）");
        return true;
    }

    private void RefreshSupplyData(string feedback)
    {
        _supplyData = new SupplyPanelData([.. _supplyNotices], _carryStore!.Current, CarryCandidates.Of(_session.Match.ContentSet), feedback, CarryAllowed: true, Preview: false);
        _dirty = true;
    }

    private void ConnectCarry()
    {
        _hud.SupplyExchangePressed += kind =>
        {
            if (_carryStore is null || !Supplying || _supplyCarryOff)
            {
                return;
            }

            bool ok = _carryStore.TryExchange(kind, out string? refusal);
            RefreshSupplyData(ok ? $"已兑换 1 件{CarryTexts.Supply(kind)}。" : $"兑换失败：{refusal}。");
        };
        _hud.SupplyCarryPressed += (kind, type) => FinishSupply(new CarryIn(kind, kind == SupplyKind.Commission ? type : null));
        _hud.SupplySkipPressed += () => FinishSupply(null);
        _hud.ResignConfirmed += () =>
        {
            _session.Resign();
            _dirty = true;
            ShowCarrySettlement(_session.CarrySettled);
        };
    }

    /// <summary>补给面板确认：按所选带入建真正的对局（AI 同等数量），开局登记在途记录，进入插旗。</summary>
    private void FinishSupply(CarryIn? mine)
    {
        if (_supplyMap is not { } map || _carryStore is null)
        {
            return;   // 预览模式没有档案存取：面板只供截图，不开局
        }

        if (_supplyCarryOff)
        {
            // 版本不符：本局按关闭带入带出开局，不写档案。
            _session = MatchSession.Create(map, _matchSeed, System.Math.Min(4, map.MaxPlayers), 1, AiDifficulty.Standard, _cellLimit);
        }
        else
        {
            if (mine is not null && _carryStore.Current.StockOf(mine.Kind) < 1)
            {
                RefreshSupplyData($"没有{CarryTexts.Supply(mine.Kind)}库存，先兑换。");
                return;
            }

            _session = MatchSession.Create(map, _matchSeed, System.Math.Min(4, map.MaxPlayers), 1, AiDifficulty.Standard, _cellLimit, carryInOut: true, carry: mine);
            // 对局标识只用于在途记录的"同一局只结算一次"：种子 + 计时器读数（不参与任何对局随机）。
            _session.BeginCarry(_carryStore, $"{_matchSeed:X16}-{Stopwatch.GetTimestamp():X}");
            GetTree().AutoAcceptQuit = false;   // 有带入时关窗先提示（见 OnCarryNotification）
        }

        _supplyMap = null;
        _supplyData = null;
        _hud.HideSupply();
        _board.Build(_session.World.Board(), _session.ZoneOwners);
        _dirty = true;
        GD.Print(_session.CarryStore is null ? "[carry] 本局关闭带入带出。" : $"[carry] 开局{CarryListText()}；{CarryTexts.Stock(_carryStore.Current)}");
    }

    /// <summary>补给阶段的单帧：只同步宽高比与相机、按需刷新；不推进对局、不采悬停与平移。</summary>
    private void ProcessSupply()
    {
        SyncAspect();
        ApplyCamera("补给");
        if (_dirty)
        {
            _dirty = false;
            RefreshViews();
        }
    }

    /// <summary>补给阶段的面板刷新（由 <c>RefreshViews</c> 调用）。</summary>
    private void RefreshSupply() => _hud.ShowSupply(_supplyData!);

    /// <summary>对局面板刷新之后：带入一栏（公开信息）与弃赛按钮。</summary>
    private void RefreshCarry()
    {
        if (Unattended && !_session.Match.CarryInOut)
        {
            _hud.RefreshCarryBar(null, false);   // 自动化模式画面与引入之前相同
            return;
        }

        bool inProgress = !_session.AwaitingZone && !_session.IsOver;
        string? text = _session.Match.CarryInOut ? CarryListText() : inProgress ? "带入带出未开启" : null;
        _hud.RefreshCarryBar(inProgress || _session.Match.CarryInOut ? text : null, _session.CanResign && !Unattended);
    }

    /// <summary>全部玩家的带入（公开视图里的带入，从插旗阶段起公开）。</summary>
    private string CarryListText() =>
        CarryTexts.List(_session.World.Public.View.CarryIns, p => p == _session.Me ? "你" : Labels.Player(p));

    /// <summary>每帧推进之后：本机玩家出局或对局终局即结算并弹出结算面板（弃赛在按钮回调里已结算）。</summary>
    private void CheckCarrySettlement()
    {
        if (_session.SettleCarryIfDue() is { } settled)
        {
            ShowCarrySettlement(settled);
            _dirty = true;
        }
    }

    private void ShowCarrySettlement(CarryOutResult? settled)
    {
        if (settled is null || _session.CarryStore is not { } store)
        {
            return;
        }

        GetTree().AutoAcceptQuit = true;   // 已结算：关窗不再有可丢的补给
        _hud.ShowSettlement(CarryTexts.Settlement(settled), "结算后：" + CarryTexts.Stock(store.Current));
        GD.Print($"[carry] 结算：{CarryTexts.Settlement(settled)}；{CarryTexts.Stock(store.Current)}");
    }

    /// <summary>
    /// 关窗（carry-in-out「中途退出与截断」）：本机玩家有带入且尚未结算时先提示"退出将丢失…弃赛可返还…"，确认后才退出；
    /// 其余情形直接退出。未确认就被杀进程的，残留的在途记录在下次开局前按中途退出结算。
    /// </summary>
    private void OnCarryNotification(int what)
    {
        if (what != NotificationWMCloseRequest || GetTree().AutoAcceptQuit)
        {
            return;
        }

        if (_session.CarryStore is null || _session.CarrySettled is not null || _session.MyCarry is null || _session.IsOver)
        {
            GetTree().Quit();
            return;
        }

        _hud.ShowConfirm("退出将丢失带入的补给；弃赛可返还补给并带出 50%。确认退出？", "确认退出", () => GetTree().Quit());
    }

    /// <summary>
    /// <c>--carry-preview=settlement</c>：对局面板照常，叠一张示例结算面板（4 人局完赛第 2 名、带入备用子），只做展示、不碰档案。
    /// </summary>
    private void ShowCarryPreview()
    {
        if (_carryPreview != "settlement")
        {
            return;
        }

        CarryOutResult sample = CarryOutSettlement.Finished(4, 2, new CarryIn(SupplyKind.SpareStone));
        var after = new CarryProfile
        {
            Points = 33,
            Inventory = ImmutableSortedDictionary<SupplyKind, int>.Empty.Add(SupplyKind.Commission, 1),
        };
        _hud.ShowSettlement(CarryTexts.Settlement(sample), "结算后：" + CarryTexts.Stock(after) + "（预览：示例结算，不读写任何档案）");
    }

    /// <summary>截图取景自证的一行：两个带入带出面板的开关与位置。</summary>
    private string CarryShotLine()
    {
        Rect2 supply = _hud.SupplyRect;
        Rect2 settlement = _hud.SettlementRect;
        string stock = _supplyData is { } data ? string.Join("、", Hud.SupplyLines(data.Current)) : "—";
        return $"[carry] 补给面板 {(_hud.SupplyOpen ? $"开 ({supply.Position.X:0}, {supply.Position.Y:0}) {supply.Size.X:0}×{supply.Size.Y:0}" : "关")}，"
            + $"结算面板 {(_hud.SettlementOpen ? $"开 ({settlement.Position.X:0}, {settlement.Position.Y:0}) {settlement.Size.X:0}×{settlement.Size.Y:0}" : "关")}；补给 {stock}；"
            + $"档案存取 {(_carryStore is null ? "无（不读写档案）" : _carryStore.FilePath)}";
    }
}
