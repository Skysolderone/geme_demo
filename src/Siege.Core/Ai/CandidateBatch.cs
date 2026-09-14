using System.Collections.Immutable;
using Siege.Core.Batch;
using Siege.Core.Board;

namespace Siege.Core.Ai;

/// <summary>单点评价结果：某落点放某类型棋子的分解。</summary>
public sealed record PointScore(Coord Coord, PieceType Type, EvaluationBreakdown Evaluation)
{
    /// <summary>加权总分。</summary>
    public long Total => Evaluation.Total;

    public override string ToString() => $"{Coord.ToNotation()}:{Type}={Total}";
}

/// <summary>一个候选批次：落点序列（批次内顺序）与整批评价分解。</summary>
public sealed record CandidateBatch(ImmutableArray<Placement> Placements, EvaluationBreakdown Evaluation)
{
    /// <summary>加权总分。</summary>
    public long Total => Evaluation.Total;

    /// <summary>落点按 <see cref="Coord"/> 字典序排好的序列（并列打破用，裁决 D4）。</summary>
    public ImmutableArray<Coord> SortedCoords => [.. Placements.Select(p => p.Coord).Order()];

    /// <summary>与批次内顺序无关的键：落点排序后的 "坐标:类型" 列表。</summary>
    public string Key => string.Join(",", Placements.OrderBy(p => p.Coord).Select(p => p.ToString()));

    /// <summary>是否 Pass（空批次）。</summary>
    public bool IsPass => Placements.IsDefaultOrEmpty;

    public override string ToString() => $"[{Key}] {Evaluation}";
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
