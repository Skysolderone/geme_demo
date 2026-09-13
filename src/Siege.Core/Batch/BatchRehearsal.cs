using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Batch;

/// <summary>合法性预演的结果：是否合法、失败原因、预计提子集合与预计结算后盘面（design.md D1）。</summary>
/// <remarks>
/// <see cref="ProjectedBoard"/> 是预演用的副本，只供 UI 展示与 AI 评估；结算驱动器 MUST NOT 把它提升为正式盘面，
/// 确认时 MUST 重跑一次完整预演（裁决记录 1）。第 1–2 步失败时没有副本，为 <c>null</c>。
/// </remarks>
public sealed record RehearsalResult(
    bool IsLegal,
    bool IsPass,
    BatchFailure? Failure,
    ImmutableArray<CapturedStone> Captures,
    GameBoard? ProjectedBoard);

/// <summary>
/// 六步合法性预演（设计文档 §6.1）的唯一实现。以<b>整批最终状态</b>判定，MUST NOT 逐枚结算。
/// 全部计算在 <see cref="GameBoard.Clone"/> 出的副本上进行，对正式盘面零副作用。
/// </summary>
public static class BatchRehearsal
{
    /// <summary>
    /// 第 1–2 步：落点为空、地形可落子、符合合法落子范围；数量不超上限、类型不超库存。
    /// 暂放操作与确认路径共用这一份实现——绕过 UI 直接提交超量批次时同样被拒绝。
    /// 返回 <c>null</c> 表示通过。
    /// </summary>
    public static BatchFailure? ValidateShape(GameBoard board, BatchContext context, IReadOnlyList<Placement> placements)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(placements);

        // 第 1 步：逐个落点
        var seen = new HashSet<Coord>();
        foreach (Placement placement in placements)
        {
            Coord c = placement.Coord;
            if (!seen.Add(c))
            {
                return BatchFailure.DuplicateInBatch(c);
            }

            if (!board.Contains(c))
            {
                return BatchFailure.Unplayable(c);
            }

            Cell cell = board[c];
            if (cell.Occupant is not null)
            {
                // 含"预计会在本批次提子中腾空的格"——此刻它仍被占据，设计文档 §5.4 明令禁止预占。
                return BatchFailure.Occupied(c);
            }

            if (cell.Terrain == Terrain.Obstacle)
            {
                return BatchFailure.Unplayable(c);
            }

            if (!context.LegalRange.Contains(c))
            {
                return BatchFailure.OutOfLegalRange(c);
            }
        }

        // 第 2 步：数量与库存
        if (placements.Count > context.DeployLimit)
        {
            return BatchFailure.DeployLimitExceeded(
                context.DeployLimit,
                placements.Count,
                [.. placements.Skip(context.DeployLimit).Select(p => p.Coord)]);
        }

        foreach (IGrouping<PieceType, Placement> byType in placements.GroupBy(p => p.Type).OrderBy(g => g.Key))
        {
            int needed = byType.Count();
            int stock = context.StockOf(byType.Key);
            if (needed > stock)
            {
                return BatchFailure.InsufficientStock(byType.Key, stock, needed, [.. byType.Select(p => p.Coord)]);
            }
        }

        return null;
    }

    /// <summary>
    /// 完整六步预演。空批次即 Pass：合法、无提子、不走第 3–6 步（否则"结算后盘面"必然等于上一次提交而误触同形）。
    /// </summary>
    public static RehearsalResult Rehearse(
        GameBoard board, BatchContext context, IReadOnlyList<Placement> placements, BoardHistory history)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(placements);
        ArgumentNullException.ThrowIfNull(history);

        if (placements.Count == 0)
        {
            return new RehearsalResult(IsLegal: true, IsPass: true, Failure: null, Captures: [], ProjectedBoard: null);
        }

        // 第 1–2 步
        if (ValidateShape(board, context, placements) is { } shapeFailure)
        {
            return Rejected(shapeFailure, projected: null);
        }

        // 第 3 步：在副本上模拟放置整个批次
        GameBoard projected = board.Clone();
        foreach (Placement placement in placements)
        {
            projected.Place(placement.Coord, context.Player, placement.Type);
        }

        // 第 4 步：一次性求出全部无气敌串的并集，再统一移除
        ImmutableArray<CapturedStone> captures = CaptureResolver.FindCaptured(projected, context.Player);
        projected.RemoveStones(captures.Select(s => s.Coord));

        // 第 5 步：提子后重算当前玩家的全部棋串（裁决记录 2：全量，不收窄到受影响子集）
        var dead = new SortedSet<Coord>();
        foreach (Group group in projected.GroupsOf(context.Player))
        {
            if (projected.IsCaptured(group))
            {
                dead.UnionWith(group.Stones);
            }
        }

        if (dead.Count > 0)
        {
            return Rejected(BatchFailure.Suicide([.. dead]), projected, captures);
        }

        // 第 6 步：结算后盘面与任一历史提交相同即同形（只比较每格占用者与类型，即 Serialize 的全部内容）
        if (history.FindDuplicate(projected.Serialize()) is { } sequence)
        {
            return Rejected(BatchFailure.Superko(sequence, [.. placements.Select(p => p.Coord)]), projected, captures);
        }

        return new RehearsalResult(IsLegal: true, IsPass: false, Failure: null, captures, projected);
    }

    private static RehearsalResult Rejected(
        BatchFailure failure, GameBoard? projected, ImmutableArray<CapturedStone> captures = default) =>
        new(IsLegal: false, IsPass: false, failure, captures.IsDefault ? [] : captures, projected);
}
