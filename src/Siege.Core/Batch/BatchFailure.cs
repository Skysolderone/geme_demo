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

    /// <summary>
    /// 改造目标非法（artisan-terrain-edit）：非匠人携带改造、目标不是几何四邻、目标类型不匹配，或目标已被改造过。
    /// 判定按<b>批次开始前</b>的地形，实现唯一在 <see cref="Board.TerrainEditRules"/>。
    /// </summary>
    TerrainEditIllegal,

    /// <summary>同一批次内两枚匠人指定了同一个改造目标（批次内不链式：同一目标批内唯一）。</summary>
    DuplicateEditInBatch,

    /// <summary>
    /// 活棋禁入（life-shape，预演第 1 步）：落点在当前玩家的禁入格集合内，即他人已确定活形棋串的眼空间。
    /// 判定基于批次开始前的正式盘面，查询只走 <see cref="LifeShapeReport.IsForbiddenFor"/>。
    /// </summary>
    LifeForbidden,

    /// <summary>
    /// 破坏活形（life-shape，预演第 6 步）：批次开始前某条非己方的已确定活形棋串，其原有棋子在结算后不在盘上
    /// 或所在棋串不再是已确定活形。
    /// </summary>
    BreaksLife,
}

/// <summary>
/// 可定位的失败原因。<see cref="Coords"/> 直接服务 UI 高亮：
/// 自杀手时为提子后仍无气的己方棋串全部坐标；同形时为本批次落点，并由 <see cref="DuplicateOfSequence"/> 指出重复的历史提交序号。
/// 活棋禁入时为违规落点（单格），所属活形棋串在 <see cref="LifeGroup"/>；破坏活形时为受影响棋串在批次开始前的全部坐标（与 <see cref="LifeGroup"/> 相同），
/// 触发的落子 / 改造在 <see cref="Triggers"/>。两类都由 <see cref="LifeOwner"/> 给出活形所有者。
/// </summary>
public sealed record BatchFailure(
    BatchFailureKind Kind,
    string Message,
    ImmutableArray<Coord> Coords,
    PieceType? StockType = null,
    int? DuplicateOfSequence = null)
{
    /// <summary>活棋禁入 / 破坏活形：涉及的已确定活形棋串的全部坐标（批次开始前的正式盘面，坐标序）；其余类别为空。</summary>
    public ImmutableArray<Coord> LifeGroup { get; init; } = [];

    /// <summary>活棋禁入 / 破坏活形：该活形的所有者；其余类别为 <c>null</c>。</summary>
    public PlayerId? LifeOwner { get; init; }

    /// <summary>破坏活形：触发的落子与改造（本批次全部暂放，按批内顺序；结果导向检查不归因到单枚）；其余类别为空。</summary>
    public ImmutableArray<Placement> Triggers { get; init; } = [];

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
            $"盘面同形：结算后各格占用者与设施、地表都与第 {sequence} 次提交后的盘面相同（不看棋子类型）。", placements, DuplicateOfSequence: sequence);

    internal static BatchFailure NotArtisan(Coord c, PieceType type) =>
        new(BatchFailureKind.TerrainEditIllegal,
            $"只有匠人能改造：{c.ToNotation()} 上的{DisplayName(type)}携带了改造目标。", [c]);

    internal static BatchFailure IllegalEdit(Coord c, TerrainEdit edit, string reason) =>
        new(BatchFailureKind.TerrainEditIllegal,
            $"改造目标非法（{c.ToNotation()} {TerrainEdit.DisplayName(edit.Kind)} {edit}）：{reason}",
            [c, .. edit.Cells.Where(t => t != c)]);

    internal static BatchFailure DuplicateEdit(Coord c, TerrainEdit edit) =>
        new(BatchFailureKind.DuplicateEditInBatch,
            $"同一批次内重复的改造目标：{edit}。", [c, .. edit.Cells.Where(t => t != c)]);

    internal static BatchFailure LifeForbidden(Coord c, PlayerId owner, ImmutableArray<Coord> group) =>
        new(BatchFailureKind.LifeForbidden,
            $"活棋禁入：{c.ToNotation()} 是 {owner} 已确定活形棋串（{string.Join(",", group.Select(s => s.ToNotation()))}）的眼空间。", [c])
        {
            LifeGroup = group,
            LifeOwner = owner,
        };

    internal static BatchFailure BreaksLife(PlayerId owner, ImmutableArray<Coord> group, ImmutableArray<Placement> triggers) =>
        new(BatchFailureKind.BreaksLife,
            $"破坏活形：{owner} 的已确定活形棋串（{string.Join(",", group.Select(s => s.ToNotation()))}）在本批次（{string.Join("，", triggers.Select(Describe))}）结算后不再是已确定活形。",
            group)
        {
            LifeGroup = group,
            LifeOwner = owner,
            Triggers = triggers,
        };

    private static string Describe(Placement p) =>
        p.Edit is { } edit ? $"{p.Coord.ToNotation()} {DisplayName(p.Type)} {TerrainEdit.DisplayName(edit.Kind)} {edit}" : $"{p.Coord.ToNotation()} {DisplayName(p.Type)}";

    /// <summary>面向人的棋子类型名称，只用于失败文案。</summary>
    internal static string DisplayName(PieceType type) => type switch
    {
        PieceType.Basic => "普通子",
        PieceType.Fortress => "堡垒子",
        PieceType.Line => "连珠子",
        PieceType.Multiplier => "倍增子",
        PieceType.Synergy => "协同子",
        PieceType.Artisan => "匠人",
        PieceType.Bannerman => "旗手子",
        PieceType.Chain => "铁链子",
        PieceType.Sentry => "哨兵子",
        PieceType.Boundary => "界碑子",
        _ => type.ToString(),
    };
}
