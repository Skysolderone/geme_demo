using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Batch;
using Siege.Core.Board;

namespace Siege.Core.Ai;

/// <summary>
/// 单点评价结果：某落点放某类型棋子（匠人可再带一个改造目标）的分解。
/// <see cref="Edit"/> 非空时这一项代表"落这枚匠人<b>并且</b>做这次改造"，与同落点的"不改造"是两个独立候选。
/// </summary>
/// <param name="Captures">该单点落下会提走的棋子数（预演结果的提子数；cycle-stop D3 的"会提子"口径）。唯一构造点在 <c>HeuristicTurnController.ScoreOne</c>。</param>
public sealed record PointScore(Coord Coord, PieceType Type, TerrainEdit? Edit, EvaluationBreakdown Evaluation, int Captures = 0)
{
    /// <summary>加权总分。</summary>
    public BigInteger Total => Evaluation.Total;

    /// <summary>该单点对应的暂放。</summary>
    public Placement Placement => new(Coord, Type, Edit);

    public override string ToString() => $"{Placement}={Total}";
}

/// <summary>
/// 一次"循环提子停手"（cycle-stop D3，ai-decision「循环提子停手」）的留痕：K、本人最近 K + 1 个大回合首次决策时采样的公开势力（旧 → 新）、
/// 最近 K 个大回合内观察到被提过的格、本次排序前 N 的候选落点。只是留痕，决策日志仍是 <c>D:pass</c>。
/// </summary>
public sealed record StagnationStop(int Rounds, ImmutableArray<BigInteger> PowerByRound, ImmutableArray<Coord> RecentlyCaptured, ImmutableArray<Coord> Candidates);

/// <summary>一个候选批次：落点序列（批次内顺序）与整批评价分解。</summary>
public sealed record CandidateBatch(ImmutableArray<Placement> Placements, EvaluationBreakdown Evaluation)
{
    /// <summary>加权总分。</summary>
    public BigInteger Total => Evaluation.Total;

    /// <summary>落点按 <see cref="Coord"/> 字典序排好的序列（并列打破用，裁决 D4）。</summary>
    public ImmutableArray<Coord> SortedCoords => [.. Placements.Select(p => p.Coord).Order()];

    /// <summary>与批次内顺序无关的键：落点排序后的 "坐标:类型" 列表。</summary>
    public string Key => string.Join(",", Placements.OrderBy(p => p.Coord).Select(p => p.ToString()));

    /// <summary>是否 Pass（空批次）。</summary>
    public bool IsPass => Placements.IsDefaultOrEmpty;

    public override string ToString() => $"[{Key}] {Evaluation}";
}

/// <summary>整批候选的来源（ai-whole-batch D1）。</summary>
public enum WholeBatchSource
{
    /// <summary>不是整批候选（贪心 / 扰动候选，或整批候选与它们键相同而被去重）。</summary>
    None,

    /// <summary>剪枝候选：强制贪心后留一法剪枝。</summary>
    Pruned,

    /// <summary>配对候选：最佳有效次阈值配对后继续逐枚贪心。</summary>
    Paired,
}

/// <summary>
/// 一次部署决策的整批候选留痕（ai-whole-batch D1，ai-decision「整批候选」）：剪枝候选与配对候选（空批次 / 无有效配对为 <c>null</c>），
/// 以及各自是否作为新键加入了候选集（键与既有候选相同时按去重不加）。只是留痕，不改决策日志。
/// </summary>
public sealed record WholeBatchCandidates(CandidateBatch? Pruned, bool PrunedAdded, CandidateBatch? Paired, bool PairedAdded)
{
    /// <summary>被选中的候选来自哪个整批候选：键与某个<b>已加入</b>的整批候选相同即为该来源，否则 <see cref="WholeBatchSource.None"/>。</summary>
    public WholeBatchSource SourceOf(CandidateBatch? choice) =>
        choice is null || choice.IsPass ? WholeBatchSource.None
        : PrunedAdded && Pruned is not null && Pruned.Key == choice.Key ? WholeBatchSource.Pruned
        : PairedAdded && Paired is not null && Paired.Key == choice.Key ? WholeBatchSource.Paired
        : WholeBatchSource.None;
}

/// <summary>
/// 候选选择的确定性规则（design.md D4）：总分高者优先；总分相同比落点排序后的坐标序列字典序（小者优先，前缀更短者优先）；
/// 仍相同比同一排序下的棋子类型序列。MUST NOT 引入随机。
/// </summary>
public static class CandidateSelection
{
    /// <summary>比较两个候选：负数表示 <paramref name="a"/> 更优。</summary>
    public static int Compare(CandidateBatch a, CandidateBatch b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        int byTotal = b.Total.CompareTo(a.Total);
        if (byTotal != 0)
        {
            return byTotal;
        }

        int byCoords = CompareCoords(a.SortedCoords, b.SortedCoords);
        if (byCoords != 0)
        {
            return byCoords;
        }

        ImmutableArray<PieceType> typesA = [.. a.Placements.OrderBy(p => p.Coord).Select(p => p.Type)];
        ImmutableArray<PieceType> typesB = [.. b.Placements.OrderBy(p => p.Coord).Select(p => p.Type)];
        for (int i = 0; i < Math.Min(typesA.Length, typesB.Length); i++)
        {
            int byType = typesA[i].CompareTo(typesB[i]);
            if (byType != 0)
            {
                return byType;
            }
        }

        return typesA.Length.CompareTo(typesB.Length);
    }

    /// <summary>坐标序列的字典序：逐元素用 <see cref="Coord.CompareTo"/>，前缀更短者优先。</summary>
    public static int CompareCoords(ImmutableArray<Coord> a, ImmutableArray<Coord> b)
    {
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            int c = a[i].CompareTo(b[i]);
            if (c != 0)
            {
                return c;
            }
        }

        return a.Length.CompareTo(b.Length);
    }

    /// <summary>最优候选；空集抛出。</summary>
    public static CandidateBatch Best(IEnumerable<CandidateBatch> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        CandidateBatch? best = null;
        foreach (CandidateBatch candidate in candidates)
        {
            if (best is null || Compare(candidate, best) < 0)
            {
                best = candidate;
            }
        }

        return best ?? throw new ArgumentException("候选集合为空。", nameof(candidates));
    }
}
