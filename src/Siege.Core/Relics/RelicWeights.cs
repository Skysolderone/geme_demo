using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Relics;

/// <summary>
/// 生成权重表（设计文档 §8.2）与稀有度评分（design.md D2）。全部为整数；权重表与稀有度刻度都<b>按对局内容集</b>取（more-pieces-relics D6 / D8）：
/// <list type="bullet">
/// <item>v1：原六类、<b>百分制</b>两表（合计 100），稀有度 = <c>10000 / 权重</c>——与引入新信物之前逐位相同，旧存档 / 旧日志按同一种子重建出同一份分布。</item>
/// <item>v2：十类、<b>千分制</b>两表（合计 1000；原六类按 80% / 72% 等比缩减，新四类出生区各 50‰、公共区各 70‰），稀有度 = <c>100000 / 权重</c>。</item>
/// </list>
/// 稀有度刻度 = <c>100 × 表合计</c>（<see cref="RarityScaleOf"/>）：两个内容集的稀有度因此处在同一量级（徽记 10000/45 与 100000/450 都是 222），
/// 出生区预算校正的容差与重抽逻辑不需要按内容集另写。
/// </summary>
public static class RelicWeights
{
    /// <summary>
    /// 权重表的类型顺序（十类 = 内容集 v2），也是加权抽样的下标顺序；v1 取其前六项（<see cref="OrderOf"/>）。
    /// 原六类的次序沿用改动前的表序，新四类按枚举次序追加在末尾（D9）。
    /// </summary>
    public static readonly ImmutableArray<RelicType> Order =
    [
        RelicType.SchoolEmblem,
        RelicType.Prospecting,
        RelicType.Depot,
        RelicType.Conscription,
        RelicType.Command,
        RelicType.Vanguard,
        RelicType.Encampment,
        RelicType.Pincer,
        RelicType.Relay,
        RelicType.Workshop,
    ];

    /// <summary>v1 出生区（百分制）：徽记 45 / 探勘 20 / 兵站 15 / 征召 8 / 军令 7 / 先锋 5。</summary>
    private static readonly int[] V1BirthTable = [45, 20, 15, 8, 7, 5];

    /// <summary>v1 公共争夺区（百分制）：徽记 30 / 探勘 15 / 兵站 10 / 征召 15 / 军令 15 / 先锋 15。</summary>
    private static readonly int[] V1ContestedTable = [30, 15, 10, 15, 15, 15];

    /// <summary>v2 出生区（千分制）：原六类 × 80% = 360 / 160 / 120 / 64 / 56 / 40，连营 / 犄角 / 驿站 / 工坊各 50。</summary>
    private static readonly int[] V2BirthTable = [360, 160, 120, 64, 56, 40, 50, 50, 50, 50];

    /// <summary>v2 公共争夺区（千分制）：原六类 × 72% = 216 / 108 / 72 / 108 / 108 / 108，新四类各 70。</summary>
    private static readonly int[] V2ContestedTable = [216, 108, 72, 108, 108, 108, 70, 70, 70, 70];

    /// <summary>某内容集的信物类型，按表序：v1 六类、v2 十类（v1 是 v2 的前缀）。</summary>
    public static ImmutableArray<RelicType> OrderOf(ContentSet set) => set switch
    {
        ContentSet.V1 => Order[..6],
        ContentSet.V2 => Order,
        _ => throw new ArgumentOutOfRangeException(nameof(set), set, "未知对局内容集。"),
    };

    /// <summary>某内容集、某分区的权重表，按 <see cref="OrderOf"/> 排列。</summary>
    public static ReadOnlySpan<int> TableOf(RelicZone zone, ContentSet set) => (zone, set) switch
    {
        (RelicZone.BirthZone, ContentSet.V1) => V1BirthTable,
        (RelicZone.Contested, ContentSet.V1) => V1ContestedTable,
        (RelicZone.BirthZone, ContentSet.V2) => V2BirthTable,
        (RelicZone.Contested, ContentSet.V2) => V2ContestedTable,
        (not (RelicZone.BirthZone or RelicZone.Contested), _) => throw new ArgumentOutOfRangeException(nameof(zone), zone, "未知信物分区。"),
        _ => throw new ArgumentOutOfRangeException(nameof(set), set, "未知对局内容集。"),
    };

    /// <summary>某内容集权重表的合计（v1 = 100 百分制，v2 = 1000 千分制）。两个分区的表合计相同（守门测试）。</summary>
    public static int TotalOf(ContentSet set)
    {
        int total = 0;
        foreach (int w in TableOf(RelicZone.BirthZone, set))
        {
            total += w;
        }

        return total;
    }

    /// <summary>稀有度基准分的分子：<c>100 × 表合计</c>，v1 为 10000、v2 为 100000（D6）。</summary>
    public static int RarityScaleOf(ContentSet set) => 100 * TotalOf(set);

    /// <summary>某内容集、某分区中某类型的权重（v1 百分比、v2 千分比）。类型不在该内容集中时抛出。</summary>
    public static int WeightOf(RelicZone zone, RelicType type, ContentSet set)
    {
        int index = IndexOf(type);
        ReadOnlySpan<int> table = TableOf(zone, set);
        return index < table.Length
            ? table[index]
            : throw new ArgumentOutOfRangeException(nameof(type), type, $"信物类型 {type} 不在对局内容集 {set} 中。");
    }

    /// <summary>稀有度基准分：权重的倒数（<c>刻度 / 权重</c> 取整），高阶按 2 倍计（design.md D2）。</summary>
    public static int RarityOf(RelicZone zone, RelicContent content, ContentSet set) =>
        RarityScaleOf(set) / WeightOf(zone, content.Type, set) * content.Magnitude;

    /// <summary>类型在 <see cref="Order"/> 中的下标（两个内容集通用）。</summary>
    public static int IndexOf(RelicType type)
    {
        int index = Order.IndexOf(type);
        return index >= 0 ? index : throw new ArgumentOutOfRangeException(nameof(type), type, "未知信物类型。");
    }
}
