using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Recruit;

/// <summary>小回合内手牌层的阶段（设计文档 §5.2–§5.5）。整类弃牌只在 <see cref="Organize"/> 允许（裁决记录 3）。</summary>
public enum TurnPhase
{
    /// <summary>不在该玩家的小回合内。</summary>
    Idle,

    /// <summary>整理手牌：可主动整类弃牌；槽位超限必须在此解除。</summary>
    Organize,

    /// <summary>征募：面板已生成，可选取候选；之后的部署由批次层驱动，确认或 Pass 通过回调结算。</summary>
    Recruit,

    /// <summary>已结算（确认落子或 Pass），等待小回合结束。</summary>
    Settled,
}

/// <summary>
/// 某类型的两段账（design.md D1）：<see cref="Carried"/> 是进入本小回合前已有的数量，<see cref="Gained"/> 是本小回合从征募面板新获得的数量。
/// Pass 只清 <see cref="Gained"/>；确认落子（≥1 枚）把 <see cref="Gained"/> 折进 <see cref="Carried"/>。
/// </summary>
public readonly record struct HandEntry(int Carried, int Gained)
{
    /// <summary>当前总数。</summary>
    public int Total => Carried + Gained;

    public override string ToString() => Gained == 0 ? Total.ToString() : $"{Carried}+{Gained}";
}

/// <summary>
/// 手牌私有视图（设计文档 §14.3「自己的区域」）：类型 → 两段账、槽位占用与本轮新征募尚未提交的数量。
/// 只通过 <see cref="PlayerHandAccess"/> 交给该玩家本人；对手与对战 AI 的读取路径拿不到这个类型。
/// </summary>
public sealed record HandPrivateView(
    PlayerId Player,
    ImmutableSortedDictionary<PieceType, HandEntry> Entries,
    TurnPhase Phase,
    int? TypeSlots)
{
    /// <summary>某类型的当前数量，未持有为 0。</summary>
    public int CountOf(PieceType type) => Entries.TryGetValue(type, out HandEntry e) ? e.Total : 0;

    /// <summary>某类型的两段账，未持有为 (0, 0)。</summary>
    public HandEntry EntryOf(PieceType type) => Entries.TryGetValue(type, out HandEntry e) ? e : default;

    /// <summary>当前存在的类型集合。</summary>
    public IEnumerable<PieceType> Types => Entries.Keys;

    /// <summary>手牌总枚数。</summary>
    public int TotalCount => Entries.Values.Sum(e => e.Total);

    /// <summary>本小回合新征募且尚未提交（未经确认落子折叠）的枚数。</summary>
    public int PendingGained => Entries.Values.Sum(e => e.Gained);

    /// <summary>已占用的类型槽 = 数量 &gt; 0 的类型数（design.md D2：派生，不存储）。</summary>
    public int OccupiedSlots => Entries.Count;

    /// <summary>超出本小回合槽位的种数；不在小回合内（无快照）时为 0。</summary>
    public int Overflow => TypeSlots is int slots ? Math.Max(0, OccupiedSlots - slots) : 0;

    /// <summary>手牌是否为空。</summary>
    public bool IsEmpty => Entries.Count == 0;

    /// <summary>值相等：逐类型比较。</summary>
    public bool Equals(HandPrivateView? other) =>
        other is not null
        && Player == other.Player
        && Phase == other.Phase
        && TypeSlots == other.TypeSlots
        && Entries.SequenceEqual(other.Entries);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Player);
        hash.Add(Phase);
        hash.Add(TypeSlots);
        foreach ((PieceType type, HandEntry entry) in Entries)
        {
            hash.Add(type);
            hash.Add(entry);
        }

        return hash.ToHashCode();
    }

    public override string ToString() =>
        $"{Player} [{string.Join(", ", Entries.Select(kv => $"{kv.Key}×{kv.Value}"))}]";
}

/// <summary>
/// 手牌公开视图（设计文档 §13.1「每名玩家手牌中当前存在的棋子类型」）：<b>只有类型集合</b>，结构上不存在任何数量字段。
/// 已弃赛玩家保留最后的公开类型并标记为不再行动（§14.3）。
/// </summary>
public sealed record HandPublicView(PlayerId Player, ImmutableSortedSet<PieceType> Types, bool IsActing)
{
    /// <summary>手牌是否为空（类型集合为空 ⇔ 一枚都没有）。供 add-match-flow 判定出局条件。</summary>
    public bool IsEmpty => Types.IsEmpty;

    /// <summary>值相等：逐类型比较。</summary>
    public bool Equals(HandPublicView? other) =>
        other is not null
        && Player == other.Player
        && IsActing == other.IsActing
        && Types.SetEquals(other.Types);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Player);
        hash.Add(IsActing);
        foreach (PieceType type in Types)
        {
            hash.Add(type);
        }

        return hash.ToHashCode();
    }

    public override string ToString() =>
        $"{Player} [{string.Join(", ", Types)}]{(IsActing ? string.Empty : " (不再行动)")}";
}
