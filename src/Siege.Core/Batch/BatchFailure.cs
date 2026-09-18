using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Batch;

/// <summary>
/// 合法性预演的失败类别。规格要求至少区分七类；<see cref="DuplicateInBatch"/> 是本层额外补充的第八类
/// （同一批次内两枚棋子指向同一格），不能并入"落点已被占据"——那条文案专指正式盘面上的占据。
/// </summary>
public enum BatchFailureKind
{
    /// <summary>落点不可落子：越界或障碍格。</summary>
    Unplayable,

    /// <summary>落点已被占据（含预计会在本批次提子中腾空的格，设计文档 §5.4）。</summary>
    Occupied,

    /// <summary>违反当前合法落子范围（保护期内的出生区限制）。</summary>
    OutOfLegalRange,

    /// <summary>同一批次内重复落点。</summary>
    DuplicateInBatch,

    /// <summary>超出部署上限。</summary>
    DeployLimitExceeded,

    /// <summary>手牌库存不足。</summary>
    InsufficientStock,

    /// <summary>自杀手：提子后己方仍有棋串无气。</summary>
    Suicide,

    /// <summary>盘面同形：结算后盘面与某次历史提交完全相同。</summary>
    Superko,
}

/// <summary>
/// 可定位的失败原因。<see cref="Coords"/> 直接服务 UI 高亮：
/// 自杀手时为提子后仍无气的己方棋串全部坐标；同形时为本批次落点，并由 <see cref="DuplicateOfSequence"/> 指出重复的历史提交序号。
/// </summary>
public sealed record BatchFailure(
    BatchFailureKind Kind,
    string Message,
    ImmutableArray<Coord> Coords,
    PieceType? StockType = null,
    int? DuplicateOfSequence = null)
{
    internal static BatchFailure Unplayable(Coord c) =>
        new(BatchFailureKind.Unplayable, $"落点不可落子：{c.ToNotation()}。", [c]);

    internal static BatchFailure Occupied(Coord c) =>
        new(BatchFailureKind.Occupied, $"该格当前已被占据：{c.ToNotation()}。", [c]);

    internal static BatchFailure OutOfLegalRange(Coord c) =>
        new(BatchFailureKind.OutOfLegalRange, $"落点不在当前合法落子范围内：{c.ToNotation()}。", [c]);

    internal static BatchFailure DuplicateInBatch(Coord c) =>
        new(BatchFailureKind.DuplicateInBatch, $"同一批次内重复落点：{c.ToNotation()}。", [c]);

    internal static BatchFailure DeployLimitExceeded(int limit, int count, ImmutableArray<Coord> beyondLimit) =>
        new(BatchFailureKind.DeployLimitExceeded,
            $"已达部署上限：本小回合最多 {limit} 枚，批次含 {count} 枚。", beyondLimit);

    internal static BatchFailure InsufficientStock(PieceType type, int stock, int needed, ImmutableArray<Coord> ofType) =>
        new(BatchFailureKind.InsufficientStock,
            $"手牌库存不足：{DisplayName(type)}库存 {stock} 枚，批次需要 {needed} 枚。", ofType, StockType: type);

    internal static BatchFailure Suicide(ImmutableArray<Coord> deadStones) =>
        new(BatchFailureKind.Suicide,
            $"自杀手：提子后己方棋串仍无气：{string.Join(",", deadStones.Select(c => c.ToNotation()))}。", deadStones);

    internal static BatchFailure Superko(int sequence, ImmutableArray<Coord> placements) =>
        new(BatchFailureKind.Superko,
            $"盘面同形：结算后盘面与第 {sequence} 次提交后的盘面完全相同。", placements, DuplicateOfSequence: sequence);

    /// <summary>面向人的棋子类型名称，只用于失败文案。</summary>
    internal static string DisplayName(PieceType type) => type switch
    {
        PieceType.Basic => "普通子",
        PieceType.Fortress => "堡垒子",
        PieceType.Line => "连珠子",
        PieceType.Multiplier => "倍增子",
        PieceType.Synergy => "协同子",
        PieceType.Artisan => "匠人",
        _ => type.ToString(),
    };
}
