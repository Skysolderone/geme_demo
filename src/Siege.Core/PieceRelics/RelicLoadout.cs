using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.PieceRelics;

/// <summary>带入清单的一项：种类与是否勾为保险（R15：开局锁定）。</summary>
public readonly record struct CarriedRelic(RelicKind Kind, bool Insured)
{
    public override string ToString() => $"{Kind.DisplayName()}{(Insured ? "（保险）" : string.Empty)}";
}

/// <summary>
/// 一名玩家一局的带入清单（relic-redesign design.md D5，替换旧 <c>CarrySetup</c> 的单件补给；接线段再接入建局）：
/// 至多 <see cref="RelicKinds.MaxCarried"/> 件、其中至多 <see cref="RelicKinds.MaxInsured"/> 件保险、每种不超过库存。
/// </summary>
/// <remarks>带入件在挂出之前不在盘面上，因而不产生任何效果（R14：未挂出的不生效）——效果只经盘面信物层读取。</remarks>
public sealed record RelicLoadout(ImmutableArray<CarriedRelic> Items)
{
    /// <summary>不带入。</summary>
    public static RelicLoadout Empty { get; } = new(ImmutableArray<CarriedRelic>.Empty);

    /// <summary>件数。</summary>
    public int Count => Items.IsDefault ? 0 : Items.Length;

    /// <summary>保险件数。</summary>
    public int InsuredCount => Items.IsDefault ? 0 : Items.Count(i => i.Insured);

    /// <summary>某种信物的带入件数。</summary>
    public int CountOf(RelicKind kind) => Items.IsDefault ? 0 : Items.Count(i => i.Kind == kind);

    /// <summary>
    /// 只看清单本身的规则（不看库存）：件数 ≤ 5、保险 ≤ 2、种类已知。合规返回 <c>null</c>，否则返回给用户的拒绝原因。
    /// AI 带入（R21，不读档案）只走这一步。
    /// </summary>
    public string? ShapeRefusal()
    {
        if (Items.IsDefault)
        {
            return "带入清单未初始化";
        }

        if (Count > RelicKinds.MaxCarried)
        {
            return $"每局至多带入 {RelicKinds.MaxCarried} 件信物（选了 {Count} 件）";
        }

        if (InsuredCount > RelicKinds.MaxInsured)
        {
            return $"至多 {RelicKinds.MaxInsured} 件可勾为保险（勾了 {InsuredCount} 件）";
        }

        foreach (CarriedRelic item in Items)
        {
            if (!Enum.IsDefined(item.Kind))
            {
                return $"未知信物种类 {item.Kind}";
            }
        }

        return null;
    }

    /// <summary>
    /// 对照档案 <paramref name="profile"/> 校验（R4 / AC4）：先过 <see cref="ShapeRefusal"/>，再要求每种信物的件数不超过库存。
    /// 合规返回 <c>null</c>，否则返回拒绝原因。
    /// </summary>
    public string? RefusalAgainst(RelicProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (ShapeRefusal() is { } shape)
        {
            return shape;
        }

        foreach (RelicKind kind in RelicKinds.Order)
        {
            int wanted = CountOf(kind);
            if (wanted > profile.StockOf(kind))
            {
                return $"{kind.DisplayName()}库存不足（要带 {wanted} 件，库存 {profile.StockOf(kind)} 件）";
            }
        }

        return null;
    }

    /// <summary>值相等：逐项比较（<see cref="ImmutableArray{T}"/> 默认是引用相等）。</summary>
    public bool Equals(RelicLoadout? other) =>
        other is not null && (Items.IsDefault ? other.Items.IsDefault : !other.Items.IsDefault && Items.SequenceEqual(other.Items));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        if (!Items.IsDefault)
        {
            foreach (CarriedRelic item in Items)
            {
                hash.Add(item);
            }
        }

        return hash.ToHashCode();
    }

    public override string ToString() => Count == 0 ? "不带入" : string.Join("、", Items);
}
