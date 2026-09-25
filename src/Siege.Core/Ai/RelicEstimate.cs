using Siege.Core.Board;
using Siege.Core.Relics;

namespace Siege.Core.Ai;

/// <summary>
/// 对信物的价值估计（设计文档 §15.1）。未揭示信物只用<b>公开输入</b>：所在分区的权重表（直接引用 <see cref="RelicWeights"/>，单一实现）。
/// 输入类型是 <see cref="RelicPublicState"/>——未揭示时其 <c>Content</c> 为 <c>null</c>，真实内容在类型上不可达。
/// </summary>
public static class RelicEstimate
{
    /// <summary>百分制换算：期望值 = Σ 百分比权重 × 类型价值，除以 100 回到价值单位。</summary>
    public const int PercentScale = 100;

    /// <summary>类型价值（强度 +1）。军令最高：部署上限直接放大每回合的行动量。</summary>
    public static int ValueOf(RelicType type) => type switch
    {
        RelicType.Command => 10,
        RelicType.Conscription => 8,
        RelicType.Vanguard => 7,
        RelicType.Prospecting => 6,
        RelicType.Depot => 5,
        RelicType.SchoolEmblem => 4,
        // more-pieces-relics 段 B 最小占位（D10 初值，未校准）：v2 局会揭示新四类，本 switch 缺项即抛。正式接入与守门测试属段 C（tasks 3.2）。
        RelicType.Relay => 6,
        RelicType.Workshop => 4,
        RelicType.Encampment => 3,
        RelicType.Pincer => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "未知信物类型。"),
    };

    /// <summary>已揭示内容的价值：类型价值 × 强度。</summary>
    public static int ValueOf(RelicContent content) => ValueOf(content.Type) * content.Magnitude;

    /// <summary>某分区中某类型的先验百分比（设计文档 §8.2），直接读生成权重表。</summary>
    /// <remarks>more-pieces-relics 段 B 占位：生成表已按内容集分为 v1 百分制 / v2 千分制，这里暂时钉住 v1 表（与改动前逐位相同）；按内容集取表与表合计归一属段 C（tasks 3.2）。</remarks>
    public static int PriorPercent(RelicZone zone, RelicType type) => RelicWeights.WeightOf(zone, type, ContentSet.V1);

    /// <summary>未揭示信物的期望价值（×100 单位）：Σ 先验百分比 × 类型价值，按强度 +1 计。</summary>
    public static int ExpectedValueScaled(RelicZone zone)
    {
        int total = 0;
        foreach (RelicType type in RelicWeights.OrderOf(ContentSet.V1))
        {
            total = checked(total + (PriorPercent(zone, type) * ValueOf(type)));
        }

        return total;
    }

    /// <summary>
    /// 对一个公开状态的价值估计（价值单位）：已揭示 → 真实内容；未揭示 → 分区先验期望（整数除法向下取整）。
    /// </summary>
    public static int Estimate(RelicPublicState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Content is { } content ? ValueOf(content) : ExpectedValueScaled(state.Spec.Zone) / PercentScale;
    }
}
