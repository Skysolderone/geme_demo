using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Presentation.Hand;
using Siege.Presentation.Layers;
using Siege.Presentation.Show;
using Siege.Presentation.Style;
using Siege.Presentation.Text;

namespace Siege.Presentation.Hud;

/// <summary>
/// 势力排名的一行，按列给出（visual-style-baseline「信息面板的分列排版」，hud-panels D1）。引擎层按列摆放，不再自己拼文字。
/// </summary>
/// <param name="Player">这一行的玩家（引擎层据此取徽记与阵营色）。</param>
/// <param name="RankText">名次列："第 2 名"；没有名次（出局 / 弃赛）为 "—"。演出前按旧名次。</param>
/// <param name="NameText">阵营列：阵营名。</param>
/// <param name="PowerValue">势力列要显示的数：演出中取滚动中的显示值，否则取总势力。引擎层据此配合 <c>RankFontPx</c> 定字号。</param>
/// <param name="PowerText">势力列的文字：<see cref="PowerValue"/> 的缩写（与概览栏同一份缩写规则）。</param>
/// <param name="DetailText">明细列：领地与棋串的拆分；有状态时状态文字在前、拆分在后；演出中为本段增量 / 总增量与名次变动提示。可能为空串（演出未开始）。
/// 恒等于 <paramref name="DetailStatusText"/>、<paramref name="DetailRestText"/>、<paramref name="DetailRankHintText"/> 三段中非空者依次用全角空格连接。</param>
/// <param name="DetailStatusText">明细的状态部分（出局 / 弃赛的状态文字）；没有状态为 <c>null</c>。引擎层恒用次要色画它（hud-panels D9-6）。</param>
/// <param name="DetailRestText">明细的增量部分：平时是拆分，演出中是本段增量 / 总增量；可能为空串。引擎层按 <paramref name="DetailIsDelta"/> / <paramref name="DeltaIsNegative"/> 取色，领地段 / 军势段随段首放大。</param>
/// <param name="DetailRankHintText">明细的名次变动提示（"名次 a → b ↑ / ↓"，只在滚动期间）；没有为 <c>null</c>。颜色与增量相同，字号恒为正文字号、不随段首放大。</param>
/// <param name="DetailIsDelta">明细里是否含演出增量或名次变动提示：为真时引擎层对增量部分与名次提示用增量色，否则用次要色。</param>
/// <param name="DeltaIsNegative">演出中当前显示的那段增量是否为负（领地段取领地增量、军势段取军势增量、其余取总增量）：为真时增量色取警示色，否则取阵营色。只在 <see cref="DetailIsDelta"/> 为真时有意义。</param>
/// <param name="IsMuted">玩家有状态（出局 / 弃赛）：整行次要。</param>
/// <param name="IsViewer">本机玩家的那一行：引擎层在行首画金色竖条（宽 <see cref="UiTheme.ViewerMarkWidthPx"/>）。</param>
public sealed record RankRowView(
    PlayerId Player,
    string RankText,
    string NameText,
    BigInteger PowerValue,
    string PowerText,
    string DetailText,
    string? DetailStatusText,
    string DetailRestText,
    string? DetailRankHintText,
    bool DetailIsDelta,
    bool DeltaIsNegative,
    bool IsMuted,
    bool IsViewer);

/// <summary>行动顺序条的一个条目：玩家、阵营名、是否为当前行动者（引擎层用金字 + 金色底边标出）。</summary>
public sealed record OrderBarEntry(PlayerId Player, string NameText, bool IsCurrent);

/// <summary>
/// 行动顺序条（hud-panels D1 / D3）：按行动顺序的条目；顺序未定时 <see cref="Entries"/> 为空、<see cref="EmptyText"/> 为 "未定"，否则为 <c>null</c>。
/// <see cref="HintText"/> 是末尾的快捷键提示。
/// </summary>
public sealed record OrderBarView(ImmutableArray<OrderBarEntry> Entries, string? EmptyText, string HintText);

/// <summary>
/// 手牌的一行（hud-panels D1 / D4）：类型名靠左、数量靠右。<see cref="Kind"/> 是按钮语义：弃牌阶段为危险操作，否则为默认。
/// </summary>
/// <param name="Type">棋子类型（引擎层据此回调选中 / 弃牌）。</param>
/// <param name="NameText">类型名列："普通子"；弃牌阶段为 "弃掉整类：普通子"。</param>
/// <param name="CountText">数量列："×5"；有本轮新征募未提交时 "×5（新 1）"。</param>
/// <param name="Kind">按钮语义。</param>
public sealed record HandRowView(PieceType Type, string NameText, string CountText, ButtonKind Kind);

/// <summary>征募候选行的状态（recruit-panel-redesign 裁决 2 / 3）：可选、已选（确认态，选了不能退回）、不可选（引擎给出原因）。</summary>
public enum RecruitRowState
{
    Pickable,
    Picked,
    Blocked,
}

/// <summary>
/// 征募面板的一个候选行（recruit-panel-redesign 裁决 1–4）：第一行"序号 + 类型名"靠左、手牌情况靠右，第二行是效果一句话；
/// 不可选时第二行换成引擎给的原因原文（<see cref="RecruitCandidateView.Reason"/>，本层不改写）。
/// </summary>
/// <param name="Index">候选位序号（0 起；引擎层据此回调选取）。</param>
/// <param name="Type">棋子类型（引擎层据此取轮廓）。</param>
/// <param name="NameText">"[1] 连珠子"。</param>
/// <param name="DetailText">可选 / 已选：效果一句话；不可选：引擎原因原文。</param>
/// <param name="HandText">手牌情况："已有 ×2"；手牌里没有这一类时 "新类型 · 占 1 槽"。</param>
/// <param name="State">行状态。</param>
/// <param name="StateText">状态角标："✓ 已选" / "不可选"；可选为 <c>null</c>。</param>
public sealed record RecruitRowView(
    int Index,
    PieceType Type,
    string NameText,
    string DetailText,
    string HandText,
    RecruitRowState State,
    string? StateText);

/// <summary>
/// 征募面板头部（recruit-panel-redesign 裁决 5）：标题、单独成行的剩余免费选取数、一行小字的结构参数。
/// </summary>
/// <param name="TitleText">"征募"。</param>
/// <param name="PicksText">"还可免费选取 2 枚"；用完为 "免费选取已用完"。</param>
/// <param name="PicksExhausted">免费选取是否已用完（引擎层据此把 <see cref="PicksText"/> 画成次要色）。</param>
/// <param name="MetaText">"展示 5 枚候选 · 类型槽 4 / 5 · 选了就不能退回"。</param>
public sealed record RecruitHeaderView(string TitleText, string PicksText, bool PicksExhausted, string MetaText);

/// <summary>
/// 势力排名、行动顺序、手牌三处面板的按列文字（visual-style-baseline「信息面板的分列排版」）。纯函数，只读视图模型，零 Godot 依赖。
/// 行序不在这里决定：势力排名的行序仍由引擎层按演出前旧名次 / 演出中新名次排。
/// </summary>
public static class HudPanelRows
{
    /// <summary>行动顺序条的标题（引擎层放在条目之前，hud-panels D9-5：重排前按钮文字以"行动顺序："开头，不得隐式删掉）。</summary>
    public const string OrderTitleText = "行动顺序";

    /// <summary>行动顺序条末尾的快捷键提示。</summary>
    public const string OrderHintText = "[5] 顺序层";

    /// <summary>行动顺序未定时的文字。</summary>
    public const string OrderEmptyText = "未定";

    /// <summary>
    /// 势力排名的一行。<paramref name="display"/> 是该玩家此刻的演出中间态（不在演出里为 <c>null</c>），<paramref name="viewer"/> 是本机玩家。
    /// 明细规则：有状态 → 状态文字在前；演出中处于领地段 / 军势段 → 本段增量文字，定格 → 总增量；滚动期间名次变动提示（加 ↑ / ↓）接在后面；
    /// 各部分用全角空格隔开。不在演出里时接 "领地 a + 棋串 b"（棋串用缩写；有状态时接在状态后面，D9-1）；在演出里但还没有可显示的增量时为空串
    /// （此时势力列显示的是旧值，拆分取自结算后快照，放上去会对不上）。
    /// </summary>
    public static RankRowView RankRow(PlayerPowerRowView row, PowerDisplay? display, PlayerId viewer)
    {
        ArgumentNullException.ThrowIfNull(row);
        int? rank = display is { Rolling: false } ? display.Change.OldRank : row.Rank;
        BigInteger value = display?.Value ?? row.Total;

        // 状态、增量（或拆分）、名次提示三段各自单列（D9-6）：状态恒为次要色；增量随段首放大；名次提示保持正文字号（五档放大时不压顶部顺序条）。
        string rest = string.Empty;
        string? rankHint = null;

        bool isDelta = false;
        bool negative = false;
        if (display is not null)
        {
            string? delta = display.Stage switch
            {
                PowerStage.Territory or PowerStage.Group => display.StageText,
                PowerStage.Hold => display.Change.DeltaText,
                _ => null,
            };
            if (delta is not null)
            {
                rest = delta;
                isDelta = true;
            }

            if (display.Rolling && display.Change.RankText is { } rankText)
            {
                rankHint = $"{rankText}{(display.Change.NewRank < display.Change.OldRank ? " ↑" : " ↓")}";
                isDelta = true;
            }

            BigInteger shown = display.Stage switch
            {
                PowerStage.Territory => display.Change.TerritoryDelta,
                PowerStage.Group => display.Change.GroupDelta,
                _ => display.Change.Delta,
            };
            negative = shown.Sign < 0;
        }
        else
        {
            // 有状态的行同样给拆分（hud-panels D9-1）：状态在前、拆分在后，弃赛者的领地与棋串不因重排读不到。
            rest = $"领地 {row.TerritoryScore} + 棋串 {Labels.CompactPower(row.GroupScore)}";
        }

        string detail = string.Join("　", new[] { row.StatusText, rest, rankHint }.Where(s => !string.IsNullOrEmpty(s)));
        return new RankRowView(
            row.Player,
            rank is int r ? $"第 {r} 名" : "—",
            FactionTable.For(row.Player).Name,
            value,
            Labels.CompactPower(value),
            detail,
            row.StatusText,
            rest,
            rankHint,
            isDelta,
            isDelta && negative,
            row.StatusText is not null,
            row.Player == viewer);
    }

    /// <summary>行动顺序条：按 <see cref="MatchPublicView.ActionOrder"/> 列出每名玩家，只有 <see cref="MatchPublicView.CurrentPlayer"/> 标为当前行动者。</summary>
    public static OrderBarView OrderBar(MatchPublicView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (view.ActionOrder.IsDefaultOrEmpty)
        {
            return new OrderBarView([], OrderEmptyText, OrderHintText);
        }

        return new OrderBarView(
            [.. view.ActionOrder.Select(p => new OrderBarEntry(p, FactionTable.For(p).Name, p == view.CurrentPlayer))],
            null,
            OrderHintText);
    }

    /// <summary>手牌的一行；<paramref name="discard"/> 为真表示本人正处于整理手牌（弃牌）阶段。</summary>
    public static HandRowView HandRow(OwnHandRowView row, bool discard)
    {
        ArgumentNullException.ThrowIfNull(row);
        string count = row.PendingGained > 0 ? $"×{row.Count}（新 {row.PendingGained}）" : $"×{row.Count}";
        return new HandRowView(
            row.Type,
            discard ? $"弃掉整类：{row.Name}" : row.Name,
            count,
            discard ? ButtonKind.Danger : ButtonKind.Default);
    }

    /// <summary>征募面板的头部文字。</summary>
    public static RecruitHeaderView RecruitHeader(RecruitPanelView panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        bool exhausted = panel.PicksRemaining <= 0;
        return new RecruitHeaderView(
            "征募",
            exhausted ? "免费选取已用完" : $"还可免费选取 {panel.PicksRemaining} 枚",
            exhausted,
            $"展示 {panel.ShowCount} 枚候选 · 类型槽 {panel.OccupiedSlots} / {panel.TypeSlots} · 选了就不能退回");
    }

    /// <summary>
    /// 征募面板的一个候选行。<paramref name="ownHand"/> MUST 是本机玩家自己的私有手牌（只用来标"已有 ×N / 新类型"）。
    /// 已选优先于不可选：已选的候选在 Core 里 <c>IsSelectable</c> 也为假，但它是确认态，不是被挡住。
    /// </summary>
    public static RecruitRowView RecruitRow(RecruitCandidateView candidate, HandPrivateView ownHand)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(ownHand);
        RecruitRowState state = candidate.IsPicked ? RecruitRowState.Picked
            : candidate.IsSelectable ? RecruitRowState.Pickable
            : RecruitRowState.Blocked;
        int have = ownHand.CountOf(candidate.Type);
        return new RecruitRowView(
            candidate.Index,
            candidate.Type,
            $"[{candidate.Index + 1}] {Labels.Piece(candidate.Type)}",
            state == RecruitRowState.Blocked && candidate.Reason is { } reason ? reason : Labels.PieceEffect(candidate.Type),
            have > 0 ? $"已有 ×{have}" : "新类型 · 占 1 槽",
            state,
            state switch
            {
                RecruitRowState.Picked => "✓ 已选",
                RecruitRowState.Blocked => "不可选",
                _ => null,
            });
    }
}
