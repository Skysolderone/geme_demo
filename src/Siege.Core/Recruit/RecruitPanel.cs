using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Recruit;

/// <summary>
/// 征募面板中的一个候选位。满槽时的新类型候选<b>照常展示</b>但 <see cref="IsSelectable"/> 为 <c>false</c> 并给出 <see cref="Reason"/>（裁决记录 2）。
/// </summary>
public sealed record RecruitCandidateView(int Index, PieceType Type, bool IsPicked, bool IsSelectable, string? Reason)
{
    public override string ToString() =>
        $"#{Index} {Type}{(IsPicked ? " ✓" : string.Empty)}{(IsSelectable || IsPicked ? string.Empty : $" ✗ {Reason}")}";
}

/// <summary>
/// 当前行动玩家的私人征募面板视图（设计文档 §5.3 / §13.2）。只通过 <see cref="PlayerHandAccess"/> 交给该玩家本人。
/// 没有刷新入口——无金币、无付费刷新、无基础免费刷新。
/// </summary>
public sealed record RecruitPanelView(
    PlayerId Player,
    ImmutableArray<RecruitCandidateView> Candidates,
    int FreePickCount,
    int PicksMade,
    int TypeSlots,
    int OccupiedSlots,
    CatchUpBonus CatchUp = default)
{
    /// <summary>展示数。</summary>
    public int ShowCount => Candidates.Length;

    /// <summary>还可免费选取的枚数（免费选取数是上限，不强制取满——裁决记录 1）。</summary>
    public int PicksRemaining => FreePickCount - PicksMade;

    /// <summary>候选类型序列（按候选位顺序）。</summary>
    public ImmutableArray<PieceType> CandidateTypes => [.. Candidates.Select(c => c.Type)];

    /// <summary>值相等：逐候选位比较。</summary>
    public bool Equals(RecruitPanelView? other) =>
        other is not null
        && Player == other.Player
        && FreePickCount == other.FreePickCount
        && PicksMade == other.PicksMade
        && TypeSlots == other.TypeSlots
        && OccupiedSlots == other.OccupiedSlots
        && CatchUp == other.CatchUp
        && Candidates.SequenceEqual(other.Candidates);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Player);
        hash.Add(FreePickCount);
        hash.Add(PicksMade);
        hash.Add(TypeSlots);
        hash.Add(OccupiedSlots);
        hash.Add(CatchUp);
        foreach (RecruitCandidateView c in Candidates)
        {
            hash.Add(c);
        }

        return hash.ToHashCode();
    }
}
