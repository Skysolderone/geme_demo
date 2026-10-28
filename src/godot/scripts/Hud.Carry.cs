using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Godot;
using Siege.Core.Board;
using Siege.Core.PieceRelics;
using Siege.Presentation.Hud;
using Siege.Presentation.Style;
using Siege.Presentation.Text;

namespace Siege.Godot;

/// <summary>
/// 开局前带入面板的一份数据（relic-redesign R25 / D8，段 C2）：档案提示、当前档案、选件状态与最近一次操作的反馈。
/// </summary>
/// <param name="Notices">档案提示（新建 / 损坏已备份 / 版本不符 / v1 迁移 / 上一局中途退出），逐行显示。</param>
/// <param name="Current">当前档案（补给点与信物库存）。</param>
/// <param name="Picker">选件状态（选 ≤ 5、勾 ≤ 2 件保险）；规则由 Core 回答，面板只按 <see cref="CarryPicker.Refusal"/> 提示并禁用"开始"。</param>
/// <param name="Feedback">最近一次兑换 / 开始的结果；无则为空串。</param>
/// <param name="CarryAllowed">本局能否带入与兑换：档案来自更新的版本时为 <c>false</c>（只剩"开始"，本局关闭带入带出）。</param>
/// <param name="Preview">只做展示（截图预览）：按钮不接任何档案操作。</param>
public sealed record SupplyPanelData(ImmutableArray<string> Notices, RelicProfile Current, CarryPicker Picker, string Feedback, bool CarryAllowed, bool Preview);

/// <summary>带入栏里一枚暂放棋子可挂的一件带入件：清单下标（0 起）与文案。</summary>
public sealed record CarryAttachOption(int Index, string Text);

/// <summary>
/// 带入栏里的一枚暂放棋子（R14：每枚至多 1 件）：落点、棋子名、当前挂的清单下标、不能挂的原因（落点有地图信物——落子即拾取；<c>null</c> = 可挂）、可选的件。
/// </summary>
public sealed record CarryAttachRow(Coord Coord, string Piece, int? Attached, string? Blocked, ImmutableArray<CarryAttachOption> Options);

/// <summary>对局中带入栏的一份数据：本人未挂出的清单一行（私有，R22）与本批每枚暂放的挂出选择；不在本人部署阶段时 <see cref="Rows"/> 为空。</summary>
public sealed record CarryBarData(string Text, ImmutableArray<CarryAttachRow> Rows);

/// <summary>
/// HUD 的带入带出面板（carry-in-out「图形版的带入面板、挂出与结算面板」）：选图之后、插旗之前的带入面板（看档案、兑换、选件、勾保险）；
/// 对局中的带入栏（未挂出清单 + 每枚暂放的挂出下拉）与"弃赛"按钮；结算面板（本人结局 + 每件信物去向）；以及二次确认框（弃赛、有带入时关窗）。
/// 只用现有控件与文字，文案一律取 <see cref="CarryTexts"/>（与终端共用）。
/// </summary>
/// <remarks>
/// 本类<b>不持有档案、不做结算、不判合法性</b>：兑换、选件、挂出、弃赛都只经事件转发给主场景，主场景再交给 <see cref="MatchSession"/> / <see cref="RelicProfileStore"/> / <see cref="CarryPicker"/>。
/// 这些面板挂在独立的根节点上，不随对局面板的刷新重建，截图前"收起可关面板"也不会动到它们。
/// </remarks>
public sealed partial class Hud
{
    private Control? _carryRoot;
    private PanelContainer? _supplyPanel;
    private VBoxContainer? _supplyBody;
    private PanelContainer? _carryBar;
    private VBoxContainer? _carryBarBody;
    private PanelContainer? _settlementPanel;
    private VBoxContainer? _settlementBody;
    private PanelContainer? _confirmPanel;
    private VBoxContainer? _confirmBody;

    /// <summary>点某种普通信物的"兑换"。</summary>
    public event Action<RelicKind>? SupplyExchangePressed;

    /// <summary>点某种信物的"带入 1 件"。</summary>
    public event Action<RelicKind>? SupplyAddPressed;

    /// <summary>点已选第 N 件（0 起）的"移除"。</summary>
    public event Action<int>? SupplyRemovePressed;

    /// <summary>点已选第 N 件（0 起）的"保险"开关。</summary>
    public event Action<int>? SupplyInsurePressed;

    /// <summary>点"开始"（按当前选件开局；没选即不带入）。</summary>
    public event Action? SupplyStartPressed;

    /// <summary>在带入栏给某枚暂放选了挂出件（<c>null</c> = 不挂）。</summary>
    public event Action<Coord, int?>? AttachPicked;

    /// <summary>弃赛已二次确认。</summary>
    public event Action? ResignConfirmed;

    /// <summary>带入面板此刻是否显示。</summary>
    public bool SupplyOpen => _supplyPanel is { Visible: true };

    /// <summary>结算面板此刻是否显示。</summary>
    public bool SettlementOpen => _settlementPanel is { Visible: true };

    /// <summary>确认框此刻是否显示。</summary>
    public bool ConfirmOpen => _confirmPanel is { Visible: true };

    /// <summary>带入面板在屏幕上的矩形（截图取景自证）。</summary>
    public Rect2 SupplyRect => _supplyPanel?.GetGlobalRect() ?? default;

    /// <summary>结算面板在屏幕上的矩形（截图取景自证）。</summary>
    public Rect2 SettlementRect => _settlementPanel?.GetGlobalRect() ?? default;

    private void EnsureCarryRoot()
    {
        if (_carryRoot is not null)
        {
            return;
        }

        _carryRoot = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = Ui.BuildTheme() };
        _carryRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_carryRoot);

        // 带入面板：靠左（与选图面板同位），背景是插旗前的盘面。
        (PanelContainer supply, VBoxContainer supplyBody) = Ui.Panel(12, 6);
        Ui.Anchor(supply, 0f, 0f, 14f, 14f, 500f, 14f);
        supply.GrowVertical = Control.GrowDirection.End;
        supply.Visible = false;
        _carryRoot.AddChild(supply);
        _supplyPanel = supply;
        _supplyBody = supplyBody;

        // 对局中的带入栏 + 弃赛按钮：左下，手牌栏之上，向上生长（暂放越多行越多）。
        (PanelContainer bar, VBoxContainer barBody) = Ui.Panel(6, 2);
        Ui.Anchor(bar, 0f, 1f, 14f, -250f, 420f, -194f);
        bar.GrowVertical = Control.GrowDirection.Begin;
        bar.Visible = false;
        _carryRoot.AddChild(bar);
        _carryBar = bar;
        _carryBarBody = barBody;

        // 结算面板：居中偏上，不整块盖住棋盘。
        (PanelContainer settlement, VBoxContainer settlementBody) = Ui.Panel(12, 6);
        Ui.Anchor(settlement, 0.5f, 0f, -300f, 110f, 300f, 110f);
        settlement.GrowVertical = Control.GrowDirection.End;
        settlement.Visible = false;
        _carryRoot.AddChild(settlement);
        _settlementPanel = settlement;
        _settlementBody = settlementBody;

        // 二次确认框：居中。
        (PanelContainer confirm, VBoxContainer confirmBody) = Ui.Panel(12, 6);
        Ui.Anchor(confirm, 0.5f, 0.5f, -240f, -70f, 240f, -70f);
        confirm.GrowVertical = Control.GrowDirection.End;
        confirm.Visible = false;
        _carryRoot.AddChild(confirm);
        _confirmPanel = confirm;
        _confirmBody = confirmBody;
    }

    /// <summary>显示 / 刷新带入面板，并隐藏对局面板（与选图阶段同样的做法）。</summary>
    public void ShowSupply(SupplyPanelData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        EnsureCarryRoot();
        _root.Visible = false;
        _carryBar!.Visible = false;
        _supplyPanel!.Visible = true;
        VBoxContainer body = _supplyBody!;
        Ui.Clear(body);

        body.AddChild(Ui.Text("信物带入（开局前）" + (data.Preview ? "　· 预览" : string.Empty), Ui.PanelBorder, UiTheme.BodyFontPx + 4));
        foreach (string notice in data.Notices)
        {
            body.AddChild(Ui.Text(notice, Ui.DangerText, UiTheme.BodyFontPx - 2, wrap: true));
        }

        body.AddChild(Ui.Text(
            $"每局至多带入 {RelicKinds.MaxCarried} 件，其中至多 {RelicKinds.MaxInsured} 件勾为保险；部署时挂到新落的棋子上才生效，挂上即公开。"
            + "完赛（任何名次）带出带入的与终局挂在你棋子上的信物；出局 / 弃赛 / 中途退出只带回保险件。"
            + $"局终按名次带出补给点，普通信物 {RelicKinds.UncalibratedExchangePrice} 点兑换 1 件，军旗只能局内带出。",
            Ui.MutedText, UiTheme.BodyFontPx - 2, wrap: true));
        body.AddChild(Ui.Separator());
        body.AddChild(Ui.Text(CarryTexts.Stock(data.Current), Ui.InfoText, UiTheme.BodyFontPx + 1));

        // 库存：每种一行"名称 库存 n 已选 k　效果"，下面"带入 1 件"与普通件的"兑换"。按钮可点与否只问 Core（经 CarryPicker 的试加）。
        bool interactive = data.CarryAllowed && !data.Preview;
        foreach (RelicKind kind in RelicKinds.Order)
        {
            body.AddChild(Ui.Separator());
            body.AddChild(Ui.Text($"{CarryTexts.Relic(kind)}　库存 {data.Current.StockOf(kind)}　已选 {data.Picker.CountOf(kind)}", Ui.PanelBorder));
            body.AddChild(Ui.Text(CarryTexts.Effect(kind), Ui.MutedText, UiTheme.BodyFontPx - 2, wrap: true));
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            Button add = Ui.Action("带入 1 件", 110);
            add.Disabled = !interactive || !data.Picker.CanAdd(kind, data.Current);
            RelicKind captured = kind;
            add.Pressed += () => SupplyAddPressed?.Invoke(captured);
            row.AddChild(add);
            if (RelicKinds.TierOf(kind) == RelicTier.Common)
            {
                int price = RelicKinds.UncalibratedExchangePrice;
                Button exchange = Ui.Action($"兑换（{price} 点）", 110);
                exchange.Disabled = !interactive || data.Current.Points < price;
                exchange.Pressed += () => SupplyExchangePressed?.Invoke(captured);
                row.AddChild(exchange);
            }

            body.AddChild(row);
        }

        // 已选清单：每件一行"[N] 名称"，"保险"开关与"移除"。保险超限时开关禁用（已勾的总能取消）。
        body.AddChild(Ui.Separator());
        ImmutableArray<CarriedRelic> items = data.Picker.Items;
        body.AddChild(Ui.Text(CarryTexts.Loadout(data.Picker.Loadout) + $"　（{items.Length} / {RelicKinds.MaxCarried} 件，保险 {data.Picker.InsuredCount} / {RelicKinds.MaxInsured}）", Ui.InfoText, wrap: true));
        for (int i = 0; i < items.Length; i++)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            Label name = Ui.Text($"[{i + 1}] {CarryTexts.Carried(items[i])}", Ui.InfoText);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(name);
            Button insure = Ui.Toggle("保险", items[i].Insured, 72);
            insure.Disabled = !interactive || !data.Picker.CanToggleInsured(i);
            int index = i;
            insure.Pressed += () => SupplyInsurePressed?.Invoke(index);
            row.AddChild(insure);
            Button remove = Ui.Action("移除", 64);
            remove.Disabled = !interactive;
            remove.Pressed += () => SupplyRemovePressed?.Invoke(index);
            row.AddChild(remove);
            body.AddChild(row);
        }

        // 即时提示（AC4）：超限或库存不足的原因直接来自 Core；有原因时"开始"不可点。
        string? refusal = data.CarryAllowed ? data.Picker.Refusal(data.Current) : null;
        if (refusal is not null)
        {
            body.AddChild(Ui.Text($"不能这样带入：{refusal}。", Ui.DangerText, UiTheme.BodyFontPx - 1, wrap: true));
        }

        if (data.Feedback.Length > 0)
        {
            body.AddChild(Ui.Text(data.Feedback, Ui.InfoText, UiTheme.BodyFontPx - 1, wrap: true));
        }

        body.AddChild(Ui.Separator());
        Button start = Ui.Action(!data.CarryAllowed || items.Length == 0 ? "开始（不带入）" : $"开始（带入 {items.Length} 件）");
        Ui.Primary(start);
        start.Disabled = refusal is not null || data.Preview;
        start.Pressed += () => SupplyStartPressed?.Invoke();
        body.AddChild(start);
    }

    /// <summary>确认带入之后：撤掉带入面板，恢复对局面板。</summary>
    public void HideSupply()
    {
        if (_supplyPanel is not null)
        {
            _supplyPanel.Visible = false;
        }

        _root.Visible = true;
    }

    /// <summary>
    /// 对局中的带入栏（本人未挂出的清单，私有，R22）、本批每枚暂放的挂出下拉（R14）与弃赛按钮；<paramref name="data"/> 为 <c>null</c> 时整栏隐藏。
    /// 下拉第 0 项恒为"不挂"，其后是 <see cref="CarryAttachRow.Options"/>；落点有地图信物时下拉禁用并写原因。
    /// </summary>
    public void RefreshCarryBar(CarryBarData? data, bool canResign)
    {
        EnsureCarryRoot();
        VBoxContainer body = _carryBarBody!;
        Ui.Clear(body);
        _carryBar!.Visible = data is not null;
        if (data is null)
        {
            return;
        }

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 6);
        Label text = Ui.Text(data.Text, Ui.InfoText, UiTheme.BodyFontPx - 2, wrap: true);
        text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(text);
        Button resign = Ui.Action("弃赛", 64);
        Ui.Danger(resign);
        resign.Visible = canResign;
        resign.Pressed += () => ShowConfirm(
            "确认弃赛？弃赛后不再行动，AI 继续把对局下完。带入带出开启时：带回保险件（未挂出的非保险件掉到地图），并按弃赛时的势力名次带出一半补给点（构筑保护期内为 0）。",
            "确认弃赛", () => ResignConfirmed?.Invoke());
        head.AddChild(resign);
        body.AddChild(head);

        foreach (CarryAttachRow row in data.Rows)
        {
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 6);
            Label piece = Ui.Text($"{row.Coord.ToNotation()} {row.Piece}", Ui.InfoText, UiTheme.BodyFontPx - 2);
            piece.CustomMinimumSize = new Vector2(96f, 0f);
            line.AddChild(piece);
            var pick = new OptionButton { CustomMinimumSize = new Vector2(200f, 0f) };
            Ui.Field(pick);
            pick.AddItem("不挂", 0);
            int selected = 0;
            for (int i = 0; i < row.Options.Length; i++)
            {
                pick.AddItem(row.Options[i].Text, i + 1);
                if (row.Attached == row.Options[i].Index)
                {
                    selected = i + 1;
                }
            }

            pick.Selected = selected;
            pick.Disabled = row.Blocked is not null;
            Coord coord = row.Coord;
            ImmutableArray<CarryAttachOption> options = row.Options;
            pick.ItemSelected += index => AttachPicked?.Invoke(coord, index <= 0 ? null : options[(int)index - 1].Index);
            line.AddChild(pick);
            if (row.Blocked is { } blocked)
            {
                line.AddChild(Ui.Text(blocked, Ui.MutedText, UiTheme.BodyFontPx - 2, wrap: true));
            }

            body.AddChild(line);
        }
    }

    /// <summary>
    /// 弹出结算面板：<paramref name="headline"/> 是本人一行（如"完赛 · 第 2 名 · 带出 16 点 · 带出信物 …"），<paramref name="lines"/> 是每件信物的去向
    /// （终局：谁带走了哪些、哪些消失；中途：R17 的说明），<paramref name="detail"/> 为结算后的补给点与库存。
    /// </summary>
    public void ShowSettlement(string headline, IReadOnlyList<string> lines, string detail)
    {
        ArgumentNullException.ThrowIfNull(lines);
        EnsureCarryRoot();
        VBoxContainer body = _settlementBody!;
        Ui.Clear(body);
        body.AddChild(Ui.Text("带出结算", Ui.PanelBorder, UiTheme.BodyFontPx + 4));
        body.AddChild(Ui.Text(headline, Ui.InfoText, UiTheme.BodyFontPx + 2, wrap: true));
        if (lines.Count > 0)
        {
            body.AddChild(Ui.Separator());
            foreach (string line in lines)
            {
                body.AddChild(Ui.Text(line, Ui.InfoText, UiTheme.BodyFontPx - 1, wrap: true));
            }
        }

        body.AddChild(Ui.Text(detail, Ui.MutedText, UiTheme.BodyFontPx - 1, wrap: true));
        Button close = Ui.Action("关闭");
        close.Pressed += () => _settlementPanel!.Visible = false;
        body.AddChild(close);
        _settlementPanel!.Visible = true;
    }

    /// <summary>二次确认框：点"<paramref name="yes"/>"执行 <paramref name="onYes"/>，点"取消"只关闭。</summary>
    public void ShowConfirm(string text, string yes, Action onYes)
    {
        ArgumentNullException.ThrowIfNull(onYes);
        EnsureCarryRoot();
        VBoxContainer body = _confirmBody!;
        Ui.Clear(body);
        body.AddChild(Ui.Text(text, Ui.InfoText, wrap: true));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        Button ok = Ui.Action(yes, 110);
        Ui.Danger(ok);
        ok.Pressed += () =>
        {
            _confirmPanel!.Visible = false;
            onYes();
        };
        row.AddChild(ok);
        Button cancel = Ui.Action("取消", 80);
        cancel.Pressed += () => _confirmPanel!.Visible = false;
        row.AddChild(cancel);
        body.AddChild(row);
        _confirmPanel!.Visible = true;
    }

    /// <summary>信物库存名单（供主场景打印取景自证）。</summary>
    internal static IEnumerable<string> SupplyLines(RelicProfile current) =>
        RelicKinds.Order.Select(k => $"{CarryTexts.Relic(k)} 库存 {current.StockOf(k)}");
}
