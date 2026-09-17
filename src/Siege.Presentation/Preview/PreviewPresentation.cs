using System.Collections.Immutable;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Preview;
using Siege.Core.Recruit;
using Siege.Core.Scoring;
using Siege.Presentation.Layers;
using Siege.Presentation.Style;
using Siege.Presentation.Text;

namespace Siege.Presentation.Preview;

/// <summary>预演高亮类别。每类有独立的视觉手法（<see cref="VisualLayering.StyleOf"/>）。</summary>
public enum HighlightKind
{
    /// <summary>暂放棋子（半透明发光）。</summary>
    Staged,

    /// <summary>预计被提走的敌方棋子（虚线 / 轮廓）。</summary>
    PredictedCapture,

    /// <summary>结算后仍无气的己方棋子（自杀手）。</summary>
    SuicideRisk,

    /// <summary>将揭示的未知信物格（只标记，不显示内容）。</summary>
    WillReveal,

    /// <summary>非法批次的相关落点。</summary>
    FailureFocus,
}

/// <summary>一格高亮。</summary>
public sealed record CellHighlight(Coord Coord, HighlightKind Kind);

/// <summary>一枚暂放棋子。</summary>
public sealed record StagedPieceView(Coord Coord, PieceType Type, PieceSilhouette Silhouette);

/// <summary>某类型的手牌消耗文案，如「普通子 ×2（库存 6）」。</summary>
public sealed record HandCostView(PieceType Type, int Used, int Stock, string Text);

/// <summary>一条预计被提走的敌方棋串。</summary>
public sealed record CaptureView(PlayerId Owner, ImmutableArray<Coord> Stones, string Text);

/// <summary>棋串军势明细的呈现。全部数值来自 Core 的 <see cref="GroupPower"/>，本类只拼文字；倍率文字用 <see cref="Multiplier.ToString"/>。
/// 公式文案按 multiplier-rebalance 的顺序拼：倍率只挨着基础军势，位置加值写在倍率之后（不被放大）；取整值直接取 <see cref="GroupPower.Power"/>。</summary>
public sealed record GroupPowerView(
    int BaseTotal,
    int LineBonus,
    int SynergyBonus,
    int HighGroundBonus,
    int PositionBonus,
    int MultiplierCount,
    int EffectiveExponent,
    string MultiplierText,
    long Power,
    string FormulaText)
{
    public static GroupPowerView From(GroupPower power)
    {
        ArgumentNullException.ThrowIfNull(power);
        string multiplier = power.Multiplier.ToString();
        string bonus = power.PositionBonus == 0
            ? "位置加值 0"
            : $"位置加值 {power.PositionBonus}（连珠 {power.LineBonus} / 协同 {power.SynergyBonus} / 高地 {power.HighGroundBonus}）";
        return new GroupPowerView(power.BaseTotal, power.LineBonus, power.SynergyBonus, power.HighGroundBonus, power.PositionBonus,
            power.MultiplierCount, power.EffectiveMultiplierCount, multiplier, power.Power,
            $"基础 {power.BaseTotal} × {multiplier} + {bonus} = {power.Power}");
    }
}

/// <summary>结算后的一条己方棋串。</summary>
public sealed record OwnGroupView(
    ImmutableArray<Coord> Stones,
    ImmutableArray<Coord> Liberties,
    int LibertyCount,
    DangerLevel Danger,
    bool ContainsPlacement,
    bool IsSuicideRisk,
    GroupPowerView? Power,
    string LibertyText);

/// <summary>一名玩家的势力与名次变化。</summary>
public sealed record PowerChangeView(
    PlayerId Player,
    bool IsViewer,
    long Before,
    long After,
    long Delta,
    int? RankBefore,
    int? RankAfter,
    bool RankChanged,
    string Text);

/// <summary>非法原因的呈现：类别、标题（每类不同）、详情、高亮与同形时的重复提交序号。</summary>
public sealed record FailurePresentation(
    BatchFailureKind Kind,
    string Title,
    string Detail,
    ImmutableArray<CellHighlight> Highlights,
    int? DuplicateOfSequence)
{
    /// <summary>标题：规格要求至少区分的七类 + 批次内重复落点，各有明确文案。预占腾空格属于「该格当前已被占据」。</summary>
    public static string TitleOf(BatchFailureKind kind) => kind switch
    {
        BatchFailureKind.Unplayable => "落点不可落子",
        BatchFailureKind.Occupied => "该格当前已被占据",
        BatchFailureKind.OutOfLegalRange => "违反当前合法落子范围",
        BatchFailureKind.DuplicateInBatch => "同一批次内重复落点",
        BatchFailureKind.DeployLimitExceeded => "超出部署上限",
        BatchFailureKind.InsufficientStock => "手牌库存不足",
        BatchFailureKind.Suicide => "自杀手",
        BatchFailureKind.Superko => "盘面同形禁则",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知失败类别。"),
    };

    /// <summary>
    /// 从 Core 的失败原因构建。暂放被拒（<see cref="StagedBatch.Stage"/> 的返回值）与确认被拒（<see cref="SettlementOutcome.Failure"/>）共用。
    /// 详情沿用 Core 文案（含围棋记法坐标与数量）；同形时指出重复的历史提交。
    /// </summary>
    public static FailurePresentation From(BatchFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        string detail = failure.Kind switch
        {
            BatchFailureKind.Suicide => $"结算后己方棋串仍无气：{Labels.Coords(failure.Coords)}",
            BatchFailureKind.Superko => $"结算后盘面与第 {failure.DuplicateOfSequence} 次提交后的盘面完全相同，不可确认",
            _ => failure.Message,
        };
        HighlightKind kind = failure.Kind == BatchFailureKind.Suicide ? HighlightKind.SuicideRisk : HighlightKind.FailureFocus;
        return new FailurePresentation(failure.Kind, TitleOf(failure.Kind), detail,
            [.. failure.Coords.Order().Select(c => new CellHighlight(c, kind))], failure.DuplicateOfSequence);
    }
}

/// <summary>将揭示提示：只有坐标与固定文案，结构上没有信物内容字段（tactical-ui D3）。</summary>
public sealed record RevealHintView(Coord Coord, string Text);

/// <summary>
/// 批次预演的呈现（batch-preview 全部 Requirement）。只把 Core 的 <see cref="BatchPreview"/> 映射为界面数据，不做任何规则计算。
/// </summary>
public sealed record PreviewPresentation(
    PlayerId Player,
    int DeployUsed,
    int DeployLimit,
    string QuotaText,
    ImmutableArray<StagedPieceView> StagedPieces,
    ImmutableArray<HandCostView> HandCosts,
    ImmutableArray<CaptureView> Captures,
    ImmutableArray<OwnGroupView> OwnGroups,
    ImmutableArray<PowerChangeView> PowerChanges,
    ImmutableArray<RevealHintView> RevealHints,
    ImmutableArray<CellHighlight> Highlights,
    FailurePresentation? Failure,
    string? PassWarning,
    bool CanConfirm)
{
    /// <summary>「将揭示」固定文案。</summary>
    public const string RevealHintText = "将揭示";

    /// <summary>
    /// 构建。<paramref name="ownHand"/> 只用于 Pass 警示的「本轮新征募」枚数（tactical-ui 裁决 4），MUST 属于预演玩家本人。
    /// </summary>
    public static PreviewPresentation Build(BatchPreview preview, HandPrivateView ownHand, LibertyThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(ownHand);
        ArgumentNullException.ThrowIfNull(thresholds);
        if (ownHand.Player != preview.Player)
        {
            throw new ArgumentException($"手牌视图属于 {ownHand.Player}，预演属于 {preview.Player}。", nameof(ownHand));
        }

        FailurePresentation? failure = preview.Failure is { } f ? FailurePresentation.From(f) : null;

        ImmutableArray<CellHighlight>.Builder highlights = ImmutableArray.CreateBuilder<CellHighlight>();
        highlights.AddRange(preview.Placements.Select(p => new CellHighlight(p.Coord, HighlightKind.Staged)));
        highlights.AddRange(preview.CapturedCoords.Select(c => new CellHighlight(c, HighlightKind.PredictedCapture)));
        highlights.AddRange(preview.WillReveal.Select(c => new CellHighlight(c, HighlightKind.WillReveal)));
        if (failure is not null)
        {
            highlights.AddRange(failure.Highlights);
        }

        return new PreviewPresentation(
            preview.Player,
            preview.DeployUsed,
            preview.DeployLimit,
            $"{preview.DeployUsed} / {preview.DeployLimit}",
            [.. preview.Placements.Select(p => new StagedPieceView(p.Coord, p.Type, PieceStyleTable.For(p.Type).Silhouette))],
            [.. preview.HandCosts.Select(c => new HandCostView(c.Type, c.Used, c.Stock, $"{Labels.Piece(c.Type)} ×{c.Used}（库存 {c.Stock}）"))],
            [.. preview.Captures.Select(g => new CaptureView(g.Owner, g.Coords,
                $"将提走 {Labels.Player(g.Owner)} 的 {g.Size} 子棋串：{Labels.Coords(g.Coords)}"))],
            [.. preview.OwnGroups.Select(g => OwnGroup(g, thresholds))],
            [.. preview.PowerChanges.Select(c => PowerChangeOf(c, preview.Player))],
            [.. preview.WillReveal.Select(c => new RevealHintView(c, RevealHintText))],
            [.. highlights.OrderBy(h => h.Kind).ThenBy(h => h.Coord)],
            failure,
            preview.IsPass && ownHand.PendingGained > 0 ? $"确认 0 落子将撤销本轮新征募的 {ownHand.PendingGained} 枚棋子" : null,
            preview.IsLegal);
    }

    private static OwnGroupView OwnGroup(GroupOutlook g, LibertyThresholds thresholds)
    {
        DangerLevel danger = thresholds.Classify(g.LibertyCount);
        string text = g.IsSuicideRisk
            ? $"自杀风险：结算后无气（{Labels.Coords(g.Stones)}）"
            : $"气 {g.LibertyCount}：{Labels.Coords(g.Liberties)}";
        return new OwnGroupView(g.Stones, g.Liberties, g.LibertyCount, danger, g.ContainsPlacement, g.IsSuicideRisk,
            g.Power is null ? null : GroupPowerView.From(g.Power), text);
    }

    private static PowerChangeView PowerChangeOf(PowerChange c, PlayerId viewer)
    {
        string rank = c.RankBefore is null && c.RankAfter is null ? "不参与排名" : $"排名 {Rank(c.RankBefore)} → {Rank(c.RankAfter)}";
        return new PowerChangeView(c.Player, c.Player == viewer, c.Before, c.After, c.Delta, c.RankBefore, c.RankAfter, c.RankChanged,
            $"{Labels.Player(c.Player)} 势力 {c.Before} → {c.After}，{rank}");
    }

    private static string Rank(int? rank) => rank is int r ? $"第 {r}" : "—";
}
