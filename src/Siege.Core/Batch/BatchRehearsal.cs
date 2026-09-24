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
/// 八步合法性预演（设计文档 §6.1 + artisan-terrain-edit「改造先于提子生效」+ life-shape）的唯一实现。
/// 顺序为：① 落点（含活棋禁入）与改造目标合法性 → ② 额度与库存 → ③ 放置 → ④ 应用全部改造 → ⑤ 同时提子
/// → ⑥ 破坏活形 → ⑦ 自杀手 → ⑧ 同形。
/// 活形 / 禁入只经 <see cref="LifeShapeReport"/> 查询（唯一实现）：批次开始前的正式盘面每次预演分析一次，供第 1 步与第 6 步共用，不跨调用缓存。
/// 以<b>整批最终状态</b>判定，MUST NOT 逐枚结算。
/// 全部计算在 <see cref="GameBoard.Clone"/> 出的副本上进行，对正式盘面零副作用（副本自带独立的地形快照）。
/// </summary>
public static class BatchRehearsal
{
    /// <summary>
    /// 第 1–2 步：落点为空、地形可落子、不在当前玩家的禁入格内（活棋禁入）、符合合法落子范围、改造目标满足 <c>terrain-edit</c> 的全部约束；
    /// 数量不超上限、类型不超库存。
    /// 暂放操作与确认路径共用这一份实现——绕过 UI 直接提交超量批次时同样被拒绝。
    /// 返回 <c>null</c> 表示通过。
    /// </summary>
    /// <remarks>
    /// 第 1 步的<b>全部</b>判定基于本批次开始前的地形，即 <paramref name="board"/> 此刻的 <see cref="GameBoard.Map"/>——
    /// 本批新架的桥还没写进去，"当批不能站上新桥"因此由既有的"地形可落子"判定天然成立，不需要额外规则（D-D）。
    /// 活棋禁入同样基于这一时点的正式盘面；它排在"合法落子范围"之前——契约给出的范围已扣除禁入格（turn-sequence），
    /// 顺序反过来时落进禁入格只会被报成"违反合法落子范围"。所有者自己的眼空间不在其禁入格内，不受限制。
    /// </remarks>
    public static BatchFailure? ValidateShape(GameBoard board, BatchContext context, IReadOnlyList<Placement> placements)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(placements);
        return placements.Count == 0 ? null : ValidateShape(board, context, placements, LifeShapeReport.Analyze(board));
    }

    /// <summary><see cref="ValidateShape(GameBoard, BatchContext, IReadOnlyList{Placement})"/> 的本体；<paramref name="life"/> 是 <paramref name="board"/> 的活形分析。</summary>
    private static BatchFailure? ValidateShape(GameBoard board, BatchContext context, IReadOnlyList<Placement> placements, LifeShapeReport life)
    {
        // 第 1 步：逐个落点 + 改造目标
        var seen = new HashSet<Coord>();
        var seenEdits = new HashSet<TerrainEdit>();
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

            if (life.IsForbiddenFor(context.Player, c))
            {
                EyeSpace space = life.EyeSpaceAt(c)!;
                ImmutableArray<Coord> group =
                    [.. life.GroupsOf(space).Where(g => g.Life == LifeState.Alive).SelectMany(g => g.Group.Stones).Order()];
                return BatchFailure.LifeForbidden(c, space.Owner, group);
            }

            if (!context.LegalRange.Contains(c))
            {
                return BatchFailure.OutOfLegalRange(c);
            }

            if (placement.Edit is not { } edit)
            {
                continue;
            }

            if (placement.Type != TerrainEditRules.EditorType)
            {
                return BatchFailure.NotArtisan(c, placement.Type);
            }

            // 合法性唯一实现；按批次开始前的地形判定（本批的改造还没应用到 board.Map 上）。
            if (TerrainEditRules.Reject(board.Map, c, edit) is { } reason)
            {
                return BatchFailure.IllegalEdit(c, edit, reason);
            }

            // 同一目标批内唯一（D-D）。FenceEdge 构造时已归一，(a,b) 与 (b,a) 是同一个键。
            if (!seenEdits.Add(edit))
            {
                return BatchFailure.DuplicateEdit(c, edit);
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
    /// 完整八步预演。空批次即 Pass：合法、无提子、不走第 3–8 步（否则"结算后盘面"必然等于上一次提交而误触同形）。
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

        // 批次开始前的正式盘面活形：第 1 步（活棋禁入）与第 6 步（破坏活形）共用这一份。
        LifeShapeReport before = LifeShapeReport.Analyze(board);

        // 第 1–2 步
        if (ValidateShape(board, context, placements, before) is { } shapeFailure)
        {
            return Rejected(shapeFailure, projected: null);
        }

        // 第 3 步：在副本上模拟放置整个批次
        GameBoard projected = board.Clone();
        foreach (Placement placement in placements)
        {
            projected.Place(placement.Coord, context.Player, placement.Type);
        }

        // 第 4 步：同时应用本批次的全部改造（terrain-edit「改造先于提子生效」/ 裁决 T-3）。
        // 顺序不可调换：立栅能敲掉敌串最后一口气而直接提子，对称地，堵死自己就是自杀手。
        projected.ApplyTerrainEdits(EditsOf(placements));

        // 第 5 步：一次性求出全部无气敌串的并集，再统一移除
        ImmutableArray<CapturedStone> captures = CaptureResolver.FindCaptured(projected, context.Player);
        projected.RemoveStones(captures.Select(s => s.Coord));

        // 第 6 步：破坏活形（life-shape D4，结果导向）——在提子之后看
        if (BrokenLife(before, projected, context.Player) is { } broken)
        {
            return Rejected(BatchFailure.BreaksLife(broken.Owner, broken.Stones, [.. placements]), projected, captures);
        }

        // 第 7 步：提子后重算当前玩家的全部棋串（裁决记录 2：全量，不收窄到受影响子集）
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

        // 第 8 步：结算后盘面与任一历史提交同形即拒绝。比较的是同形比对键（每格占用者 + 设施与地表，不含棋子类型），投影在 BoardHistory 内经 GameBoard.SuperkoKey 完成
        if (history.FindDuplicate(projected.Serialize()) is { } sequence)
        {
            return Rejected(BatchFailure.Superko(sequence, [.. placements.Select(p => p.Coord)]), projected, captures);
        }

        return new RehearsalResult(IsLegal: true, IsPass: false, Failure: null, captures, projected);
    }

    /// <summary>
    /// 第 6 步：批次开始前每条所有者不是 <paramref name="mover"/> 的已确定活形棋串，按其原有棋子在副本上所在的棋串重查；
    /// 任一棋子已不在副本上（第 5 步被提走）或所在棋串不再是已确定活形，即返回该棋串（批次开始前的形态）。按棋串顺序取第一条。
    /// 批次开始前没有这样的棋串时不分析副本。
    /// </summary>
    private static Group? BrokenLife(LifeShapeReport before, GameBoard projected, PlayerId mover)
    {
        LifeShapeReport? after = null;
        foreach (GroupLife life in before.Groups)
        {
            if (life.Life != LifeState.Alive || life.Group.Owner == mover)
            {
                continue;
            }

            after ??= LifeShapeReport.Analyze(projected);
            foreach (Coord stone in life.Group.Stones)
            {
                if (after.GroupLifeAt(stone) is not { Life: LifeState.Alive } now || now.Group.Owner != life.Group.Owner)
                {
                    return life.Group;
                }
            }
        }

        return null;
    }

    /// <summary>批次里全部非空的改造目标，按批次内落子顺序。</summary>
    internal static ImmutableArray<TerrainEdit> EditsOf(IReadOnlyList<Placement> placements) =>
        [.. placements.Where(p => p.Edit is not null).Select(p => p.Edit!.Value)];

    private static RehearsalResult Rejected(
        BatchFailure failure, GameBoard? projected, ImmutableArray<CapturedStone> captures = default) =>
        new(IsLegal: false, IsPass: false, failure, captures.IsDefault ? [] : captures, projected);
}
