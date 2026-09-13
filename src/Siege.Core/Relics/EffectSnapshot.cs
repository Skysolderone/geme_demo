using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Relics;

/// <summary>
/// 小回合效果快照（设计文档 §5.1，design.md D5）：小回合开始时生成一次的<b>不可变值对象</b>，小回合内所有流程只读它。
/// 本小回合内信物控制的任何变化都不影响它——这是结构性保证，不靠各消费点自觉。
/// </summary>
/// <remarks>
/// <para>这里<b>没有</b>先手修正字段：先锋在大回合结束时另行读取（<see cref="RelicLedger.ReadInitiativeBonuses"/>），放进来只会诱导误用。</para>
/// <para>徽记权重调整以「数量」表达（D6）：高阶徽记数量 2。§9.1 公式 <c>基础权重 × (1 + 0.75 × 数量)</c> 用整数写成
/// <c>基础权重 × (4 + 3 × 数量) / 4</c>；分母 4 对全部类型相同、归一化后消去，故 <see cref="EmblemWeightNumerator"/> 直接给出分子倍数。</para>
/// <para>兵站缩水（D7）：只给出合法槽位数与 <see cref="OverflowTypeCount"/>，整类弃牌由 add-recruit-hand 执行，本层不碰手牌。</para>
/// </remarks>
public sealed record EffectSnapshot
{
    /// <summary>默认基础值（设计文档 §5.2–§5.4）。</summary>
    public const int BaseRevealCount = 5;

    public const int BaseFreePickCount = 3;

    public const int BaseTypeSlots = 5;

    public const int BaseDeployLimit = 3;

    public EffectSnapshot(
        PlayerId player,
        int majorRound,
        int revealCount,
        int freePickCount,
        int typeSlots,
        int deployLimit,
        ImmutableSortedDictionary<PieceType, int> emblemCounts,
        int heldTypeCount)
    {
        ArgumentNullException.ThrowIfNull(emblemCounts);
        if (heldTypeCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(heldTypeCount), heldTypeCount, "持有类型数不得为负。");
        }

        Player = player;
        MajorRound = majorRound;
        RevealCount = revealCount;
        FreePickCount = freePickCount;
        TypeSlots = typeSlots;
        DeployLimit = deployLimit;
        EmblemCounts = emblemCounts;
        HeldTypeCount = heldTypeCount;
    }

    /// <summary>快照所属玩家。</summary>
    public PlayerId Player { get; }

    /// <summary>生成时所在的大回合。</summary>
    public int MajorRound { get; }

    /// <summary>征募展示数（默认 5，探勘 +）。</summary>
    public int RevealCount { get; }

    /// <summary>免费选取数（默认 3，征召 +）。</summary>
    public int FreePickCount { get; }

    /// <summary>手牌类型槽（默认 5，兵站 +）。</summary>
    public int TypeSlots { get; }

    /// <summary>部署上限（默认 3，军令 +）。不设统一硬上限。</summary>
    public int DeployLimit { get; }

    /// <summary>各棋子类型的等效徽记数量；未列出的类型为 0。高阶徽记计 2。</summary>
    public ImmutableSortedDictionary<PieceType, int> EmblemCounts { get; }

    /// <summary>快照生成时玩家持有的棋子类型数（由手牌层提供的输入，原样携带）。</summary>
    public int HeldTypeCount { get; }

    /// <summary>超限种数：持有类型数超出槽位的部分，0 即合法。大于 0 时征募流程在整类弃牌前不得进行。</summary>
    public int OverflowTypeCount => Math.Max(0, HeldTypeCount - TypeSlots);

    /// <summary>某棋子类型的等效徽记数量。</summary>
    public int EmblemCountOf(PieceType type) => EmblemCounts.TryGetValue(type, out int n) ? n : 0;

    /// <summary>徽记权重分子倍数 <c>4 + 3 × 数量</c>（对应 <c>1 + 0.75 × 数量</c> 的 4 倍）。无徽记为 4。</summary>
    public int EmblemWeightNumerator(PieceType type) => 4 + (3 * EmblemCountOf(type));

    /// <summary>§9.1 调整后权重的整数形式：<c>基础权重 × (4 + 3 × 数量)</c>。全部类型共用分母 4，归一化后消去。</summary>
    public int AdjustedWeight(PieceType type, int baseWeight)
    {
        if (baseWeight < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(baseWeight), baseWeight, "基础权重不得为负。");
        }

        return checked(baseWeight * EmblemWeightNumerator(type));
    }

    /// <summary>值相等：徽记表逐项比较（<see cref="ImmutableSortedDictionary{TKey, TValue}"/> 默认是引用相等，对值对象没有意义）。</summary>
    public bool Equals(EffectSnapshot? other) =>
        other is not null
        && Player == other.Player
        && MajorRound == other.MajorRound
        && RevealCount == other.RevealCount
        && FreePickCount == other.FreePickCount
        && TypeSlots == other.TypeSlots
        && DeployLimit == other.DeployLimit
        && HeldTypeCount == other.HeldTypeCount
        && EmblemCounts.SequenceEqual(other.EmblemCounts);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Player);
        hash.Add(MajorRound);
        hash.Add(RevealCount);
        hash.Add(FreePickCount);
        hash.Add(TypeSlots);
        hash.Add(DeployLimit);
        hash.Add(HeldTypeCount);
        foreach ((PieceType piece, int count) in EmblemCounts)
        {
            hash.Add(piece);
            hash.Add(count);
        }

        return hash.ToHashCode();
    }

    /// <summary>无信物时的默认快照。</summary>
    public static EffectSnapshot Defaults(PlayerId player, int majorRound, int heldTypeCount) =>
        new(player, majorRound, BaseRevealCount, BaseFreePickCount, BaseTypeSlots, BaseDeployLimit,
            ImmutableSortedDictionary<PieceType, int>.Empty, heldTypeCount);
}
