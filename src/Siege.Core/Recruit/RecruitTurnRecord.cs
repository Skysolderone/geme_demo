using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Recruit;

/// <summary>
/// 一个小回合的征募记录（设计文档 §17「每轮征募候选、玩家选择和被撤销的 Pass 征募」）。
/// </summary>
/// <remarks>
/// <see cref="RecruitedCount"/> 与 <see cref="DeployedCount"/> 是「玩家是否用至少落 1 子规避 Pass 撤销」这条 §17 监控项的原始量；
/// 「势力无变化」由势力层提供，信号合成留给遥测（add-heuristic-ai），本层只输出原始量。
/// </remarks>
public sealed record RecruitTurnRecord(
    int Sequence,
    PlayerId Player,
    int MajorRound,
    ImmutableArray<PieceType> Candidates,
    ImmutableArray<int> PickedIndices,
    ImmutableArray<PieceType> Discarded,
    bool Passed,
    int DeployedCount,
    int RevokedCount)
{
    /// <summary>本小回合从面板选取的枚数。</summary>
    public int RecruitedCount => PickedIndices.Length;

    /// <summary>被选取的候选类型（按选取顺序）。</summary>
    public ImmutableArray<PieceType> PickedTypes => [.. PickedIndices.Select(i => Candidates[i])];

    /// <summary>值相等：逐项比较。</summary>
    public bool Equals(RecruitTurnRecord? other) =>
        other is not null
        && Sequence == other.Sequence
        && Player == other.Player
        && MajorRound == other.MajorRound
        && Passed == other.Passed
        && DeployedCount == other.DeployedCount
        && RevokedCount == other.RevokedCount
        && Candidates.SequenceEqual(other.Candidates)
        && PickedIndices.SequenceEqual(other.PickedIndices)
        && Discarded.SequenceEqual(other.Discarded);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Sequence);
        hash.Add(Player);
        hash.Add(MajorRound);
        hash.Add(Passed);
        hash.Add(DeployedCount);
        hash.Add(RevokedCount);
        foreach (PieceType c in Candidates)
        {
            hash.Add(c);
        }

        foreach (int i in PickedIndices)
        {
            hash.Add(i);
        }

        foreach (PieceType d in Discarded)
        {
            hash.Add(d);
        }

        return hash.ToHashCode();
    }

    /// <summary>单行文本：供对局日志与人工复盘（与 <see cref="Relics.RelicRevealEvent"/> 同风格：<c>R{大回合}</c> 前缀，<c>key=value</c> 字段）。不涉及坐标。</summary>
    public override string ToString() =>
        $"#{Sequence} {Player} R{MajorRound} candidates={string.Join(",", Candidates)} picks={string.Join(",", PickedIndices)}"
        + $" discards={string.Join(",", Discarded)} deployed={DeployedCount} revoked={RevokedCount} pass={(Passed ? "yes" : "no")}";
}
