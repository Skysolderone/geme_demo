using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Godot;
using Siege.Core.Board;
using Siege.Core.Carry;
using Siege.Presentation.Style;
using Siege.Presentation.Text;

namespace Siege.Godot;

/// <summary>补给选择面板的一份数据（全部取自档案存取的当前档案与 Core 的补给表 / 候选表；本类不判断能否兑换或带入之外的任何规则）。</summary>
/// <param name="Notices">档案提示（新建 / 损坏已备份 / 版本不符 / 上一局中途退出），逐行显示。</param>
/// <param name="Current">当前档案（补给点与库存）。</param>
/// <param name="Candidates">换型令可指定的类型（本局内容集的换型候选，固定次序）。</param>
/// <param name="Feedback">最近一次兑换的结果；无则为空串。</param>
/// <param name="CarryAllowed">本局能否兑换与带入：档案来自更新的版本时为 <c>false</c>（只剩"不带入，开始"，本局关闭带入带出）。</param>
/// <param name="Preview">只做展示（截图预览）：按钮不接任何档案操作。</param>
public sealed record SupplyPanelData(ImmutableArray<string> Notices, CarryProfile Current, ImmutableArray<CarryCandidate> Candidates, string Feedback, bool CarryAllowed, bool Preview);

/// <summary>
/// HUD 的带入带出面板（carry-in-out「图形版的补给选择面板与结算面板」）：选图之后、插旗之前的补给选择面板；对局中的带入一栏与"弃赛"按钮；
/// 结算面板；以及二次确认框（弃赛、有带入时关窗）。只用现有控件与文字，文案一律取 <see cref="CarryTexts"/>（与终端共用）。
/// </summary>
/// <remarks>
/// 本类<b>不持有档案、不做结算</b>：兑换、带入、弃赛都只经事件转发给主场景，主场景再交给 <see cref="MatchSession"/> / <see cref="CarryProfileStore"/>。
/// 这些面板挂在独立的根节点上，不随对局面板的刷新重建，截图前"收起可关面板"也不会动到它们。
/// </remarks>
public sealed partial class Hud
{
    private Control? _carryRoot;
    private PanelContainer? _supplyPanel;
    private VBoxContainer? _supplyBody;
    private PanelContainer? _carryBar;
    private Label? _carryBarText;
    private Button? _resignButton;
    private PanelContainer? _settlementPanel;
    private VBoxContainer? _settlementBody;
    private PanelContainer? _confirmPanel;
    private VBoxContainer? _confirmBody;
    private int _commissionIndex;

    /// <summary>点某补给的"兑换"。</summary>
    public event Action<SupplyKind>? SupplyExchangePressed;

    /// <summary>点某补给的"带入"（换型令附下拉里选中的类型）。</summary>
    public event Action<SupplyKind, PieceType?>? SupplyCarryPressed;

    /// <summary>点"不带入，开始"。</summary>
    public event Action? SupplySkipPressed;

    /// <summary>弃赛已二次确认。</summary>
    public event Action? ResignConfirmed;

    /// <summary>补给选择面板此刻是否显示。</summary>
    public bool SupplyOpen => _supplyPanel is { Visible: true };

    /// <summary>结算面板此刻是否显示。</summary>
    public bool SettlementOpen => _settlementPanel is { Visible: true };

    /// <summary>确认框此刻是否显示。</summary>
    public bool ConfirmOpen => _confirmPanel is { Visible: true };

    /// <summary>补给选择面板在屏幕上的矩形（截图取景自证）。</summary>
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

        // 补给选择面板：靠左（与选图面板同位），背景是插旗前的盘面。
        (PanelContainer supply, VBoxContainer supplyBody) = Ui.Panel(12, 6);
        Ui.Anchor(supply, 0f, 0f, 14f, 14f, 454f, 14f);
        supply.GrowVertical = Control.GrowDirection.End;
        supply.Visible = false;
        _carryRoot.AddChild(supply);
        _supplyPanel = supply;
        _supplyBody = supplyBody;

        // 对局中的带入一栏 + 弃赛按钮：左下，手牌栏之上。
        (PanelContainer bar, VBoxContainer barBody) = Ui.Panel(6, 2);
        Ui.Anchor(bar, 0f, 1f, 14f, -250f, 378f, -194f);
        bar.GrowVertical = Control.GrowDirection.Begin;
        bar.Visible = false;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        _carryBarText = Ui.Text(string.Empty, Ui.InfoText, UiTheme.BodyFontPx - 2, wrap: true);
        _carryBarText.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(_carryBarText);
        _resignButton = Ui.Action("弃赛", 64);
        _resignButton.AddThemeColorOverride("font_color", Ui.DangerText);
        _resignButton.Pressed += () => ShowConfirm(
            "确认弃赛？弃赛后不再行动，AI 继续把对局下完。带入带出开启时：返还带入的补给，并按弃赛时的势力名次带出一半补给点（构筑保护期内为 0）。",
            "确认弃赛", () => ResignConfirmed?.Invoke());
        row.AddChild(_resignButton);
        barBody.AddChild(row);
        _carryRoot.AddChild(bar);
        _carryBar = bar;

        // 结算面板：居中偏上，窄条，不整块盖住棋盘。
        (PanelContainer settlement, VBoxContainer settlementBody) = Ui.Panel(12, 6);
        Ui.Anchor(settlement, 0.5f, 0f, -260f, 110f, 260f, 110f);
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

    /// <summary>显示 / 刷新补给选择面板，并隐藏对局面板（与选图阶段同样的做法）。</summary>
    public void ShowSupply(SupplyPanelData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        EnsureCarryRoot();
        _root.Visible = false;
        _carryBar!.Visible = false;
        _supplyPanel!.Visible = true;
        VBoxContainer body = _supplyBody!;
        Ui.Clear(body);

        body.AddChild(Ui.Text("补给（开局前）" + (data.Preview ? "　· 预览" : string.Empty), Ui.PanelBorder, UiTheme.BodyFontPx + 4));
        foreach (string notice in data.Notices)
        {
            body.AddChild(Ui.Text(notice, Ui.DangerText, UiTheme.BodyFontPx - 2, wrap: true));
        }

        body.AddChild(Ui.Text("补给只改开局手牌，每局至多带入 1 件；局终按名次带出补给点（4 人局 24 / 16 / 12 / 10），弃赛带出一半并返还补给，出局补给丢失。",
            Ui.MutedText, UiTheme.BodyFontPx - 2, wrap: true));
        body.AddChild(Ui.Separator());
        body.AddChild(Ui.Text(CarryTexts.Stock(data.Current), Ui.InfoText, UiTheme.BodyFontPx + 1));

        foreach (SupplyKind kind in Supplies.Order)
        {
            int price = Supplies.PriceOf(kind);
            int stock = data.Current.StockOf(kind);
            body.AddChild(Ui.Separator());
            body.AddChild(Ui.Text($"{CarryTexts.Supply(kind)}　价 {price}　库存 {stock}", Ui.PanelBorder));
            body.AddChild(Ui.Text(CarryTexts.Effect(kind), Ui.MutedText, UiTheme.BodyFontPx - 2, wrap: true));

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            Button exchange = Ui.Action($"兑换（{price} 点）", 110);
            exchange.Disabled = !data.CarryAllowed || data.Current.Points < price;
            SupplyKind captured = kind;
            exchange.Pressed += () => SupplyExchangePressed?.Invoke(captured);
            row.AddChild(exchange);

            OptionButton? types = null;
            if (kind == SupplyKind.Commission)
            {
                types = new OptionButton { CustomMinimumSize = new Vector2(110f, UiTheme.ButtonHeightPx) };
                types.AddThemeFontSizeOverride("font_size", UiTheme.BodyFontPx);
                foreach (CarryCandidate candidate in data.Candidates)
                {
                    types.AddItem(Labels.Piece(candidate.Type));
                }

                _commissionIndex = Math.Clamp(_commissionIndex, 0, Math.Max(0, data.Candidates.Length - 1));
                types.Selected = _commissionIndex;
                types.ItemSelected += index => _commissionIndex = (int)index;
                row.AddChild(types);
            }

            Button carry = Ui.Action("带入并开始", 110);
            carry.Disabled = !data.CarryAllowed || stock < 1;
            ImmutableArray<CarryCandidate> candidates = data.Candidates;
            carry.Pressed += () => SupplyCarryPressed?.Invoke(captured,
                captured == SupplyKind.Commission && candidates.Length > 0 ? candidates[Math.Clamp(_commissionIndex, 0, candidates.Length - 1)].Type : null);
            row.AddChild(carry);
            body.AddChild(row);
        }

        if (data.Feedback.Length > 0)
        {
            body.AddChild(Ui.Text(data.Feedback, Ui.InfoText, UiTheme.BodyFontPx - 1, wrap: true));
        }

        body.AddChild(Ui.Separator());
        Button skip = Ui.Action("不带入，开始");
        skip.AddThemeColorOverride("font_color", Ui.PanelBorder);
        skip.Pressed += () => SupplySkipPressed?.Invoke();
        body.AddChild(skip);
    }

    /// <summary>确认补给之后：撤掉补给面板，恢复对局面板。</summary>
    public void HideSupply()
    {
        if (_supplyPanel is not null)
        {
            _supplyPanel.Visible = false;
        }

        _root.Visible = true;
    }

    /// <summary>对局中的带入一栏（公开信息，全部玩家）与弃赛按钮；<paramref name="text"/> 为 <c>null</c> 时整栏隐藏。</summary>
    public void RefreshCarryBar(string? text, bool canResign)
    {
        EnsureCarryRoot();
        _carryBar!.Visible = text is not null;
        _carryBarText!.Text = text ?? string.Empty;
        _resignButton!.Visible = canResign;
    }

    /// <summary>弹出结算面板：<paramref name="headline"/> 如"完赛 · 第 2 名 · 带出 16 · 补给已消耗"，<paramref name="detail"/> 为结算后的补给点与库存。</summary>
    public void ShowSettlement(string headline, string detail)
    {
        EnsureCarryRoot();
        VBoxContainer body = _settlementBody!;
        Ui.Clear(body);
        body.AddChild(Ui.Text("带出结算", Ui.PanelBorder, UiTheme.BodyFontPx + 4));
        body.AddChild(Ui.Text(headline, Ui.InfoText, UiTheme.BodyFontPx + 2, wrap: true));
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
        ok.AddThemeColorOverride("font_color", Ui.DangerText);
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

    /// <summary>补给名单（供主场景打印取景自证）。</summary>
    internal static IEnumerable<string> SupplyLines(CarryProfile current) =>
        Supplies.Order.Select(k => $"{CarryTexts.Supply(k)} 价 {Supplies.PriceOf(k)} 库存 {current.StockOf(k)}");
}
