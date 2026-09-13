using Siege.Core.Board;

namespace Siege.Core.Relics;

/// <summary>
/// 生成权重表（设计文档 §8.2）与稀有度评分（design.md D2）。全部为整数：权重是百分比整数，稀有度是 <c>10000 / 权重</c> 取整。
/// </summary>
public static class RelicWeights
{
    /// <summary>权重表的类型顺序，也是加权抽样的下标顺序。</summary>
    public static readonly RelicType[] Order =
    [
        RelicType.SchoolEmblem,
        RelicType.Prospecting,
        RelicType.Depot,
        RelicType.Conscription,
        RelicType.Command,
        RelicType.Vanguard,
    ];

    /// <summary>出生区：徽记 45 / 探勘 20 / 兵站 15 / 征召 8 / 军令 7 / 先锋 5。</summary>
    private static readonly int[] BirthTable = [45, 20, 15, 8, 7, 5];

    /// <summary>公共争夺区：徽记 30 / 探勘 15 / 兵站 10 / 征召 15 / 军令 15 / 先锋 15。</summary>
    private static readonly int[] ContestedTable = [30, 15, 10, 15, 15, 15];

    /// <summary>稀有度基准分的分子：稀有度 = <c>RarityScale / 权重</c>。</summary>
    public const int RarityScale = 10000;

    /// <summary>某分区的权重表，按 <see cref="Order"/> 排列。</summary>
    public static ReadOnlySpan<int> TableOf(RelicZone zone) => zone switch
    {
        RelicZone.BirthZone => BirthTable,
        RelicZone.Contested => ContestedTable,
        _ => throw new ArgumentOutOfRangeException(nameof(zone), zone, "未知信物分区。"),
    };

    /// <summary>某分区中某类型的百分比权重。</summary>
    public static int WeightOf(RelicZone zone, RelicType type) => TableOf(zone)[IndexOf(type)];

    /// <summary>稀有度基准分：权重的倒数（<c>10000 / 权重</c> 取整），高阶按 2 倍计（design.md D2）。</summary>
    public static int RarityOf(RelicZone zone, RelicContent content) =>
        RarityScale / WeightOf(zone, content.Type) * content.Magnitude;

    /// <summary>类型在 <see cref="Order"/> 中的下标。</summary>
    public static int IndexOf(RelicType type)
    {
        int index = Array.IndexOf(Order, type);
        return index >= 0 ? index : throw new ArgumentOutOfRangeException(nameof(type), type, "未知信物类型。");
    }
}
