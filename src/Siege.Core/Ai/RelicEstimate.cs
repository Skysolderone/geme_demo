using Siege.Core.Board;
using Siege.Core.Relics;

namespace Siege.Core.Ai;

/// <summary>
/// 对信物的价值估计（设计文档 §15.1）。未揭示信物只用<b>公开输入</b>：对局内容集（<c>MatchPublicView.ContentSet</c>，开局固定、始终公开）
/// 下所在分区的权重表（直接引用 <see cref="RelicWeights"/>，单一实现）。
/// 输入类型是 <see cref="RelicPublicState"/>——未揭示时其 <c>Content</c> 为 <c>null</c>，真实内容在类型上不可达。
/// </summary>
public static class RelicEstimate
{
    /// <summary>
    /// 类型价值（强度 +1）。军令最高：部署上限直接放大每回合的行动量。
    /// 新四类（more-pieces-relics D10）是<b>未校准的初值</b>：驿站 6、工坊 4、连营 3、犄角 3。连营 / 犄角取低值，
    /// 因为它们的计分效果已经经唯一的势力计算进入"即时势力增量"，信物维只反映"占到它"的一次性价值。
    /// </summary>
    public static int ValueOf(RelicType type) => type switch
    {
        RelicType.Command => 10,
        RelicType.Conscription => 8,
        RelicType.Vanguard => 7,
        RelicType.Prospecting => 6,
        RelicType.Depot => 5,
        RelicType.SchoolEmblem => 4,
        RelicType.Relay => 6,         // 未校准初值（D10）
        RelicType.Workshop => 4,      // 未校准初值（D10）
        RelicType.Encampment => 3,    // 未校准初值（D10）
        RelicType.Pincer => 3,        // 未校准初值（D10）
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "未知信物类型。"),
    };

    /// <summary>已揭示内容的价值：类型价值 × 强度。</summary>
    public static int ValueOf(RelicContent content) => ValueOf(content.Type) * content.Magnitude;

    /// <summary>某内容集、某分区中某类型的先验权重（设计文档 §8.2；v1 百分制、v2 千分制），直接读生成权重表。</summary>
    public static int PriorWeight(RelicZone zone, RelicType type, ContentSet set) => RelicWeights.WeightOf(zone, type, set);

    /// <summary>归一分母 = 该内容集权重表的合计（v1 100、v2 1000，D6：取自表，不写死）。</summary>
    public static int ScaleOf(ContentSet set) => RelicWeights.TotalOf(set);

    /// <summary>未揭示信物的期望价值（× <see cref="ScaleOf"/> 单位）：Σ 该内容集下所在分区的先验权重 × 类型价值，按强度 +1 计。</summary>
    public static int ExpectedValueScaled(RelicZone zone, ContentSet set)
    {
        int total = 0;
        foreach (RelicType type in RelicWeights.OrderOf(set))
        {
            total = checked(total + (PriorWeight(zone, type, set) * ValueOf(type)));
        }

        return total;
    }

    /// <summary>
    /// 对一个公开状态的价值估计（价值单位）：已揭示 → 真实内容；未揭示 → 该对局内容集下的分区先验期望，按表合计归一后向下取整
    /// （v2：出生区 5152 / 1000 → 5、公共区 5692 / 1000 → 5；v1：544 / 100 → 5、635 / 100 → 6）。
    /// </summary>
    public static int Estimate(RelicPublicState state, ContentSet set)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Content is { } content ? ValueOf(content) : ExpectedValueScaled(state.Spec.Zone, set) / ScaleOf(set);
    }
}
