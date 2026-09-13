using Siege.Core.Board;

namespace Siege.Core.Relics;

/// <summary>
/// 生成参数。默认值来自设计文档 §8.2 与 design.md 裁决记录；地图可按图覆盖（容差、重试上限），但权重表不在此处调整。
/// </summary>
public sealed record RelicGenerationOptions
{
    /// <summary>裁决记录 2：各出生区总稀有度与均值的偏差 ≤ 8%。</summary>
    public const int DefaultRarityTolerancePermille = 80;

    /// <summary>默认参数。</summary>
    public static readonly RelicGenerationOptions Default = new();

    /// <summary>出生区稀有度容差（千分比）。1000 即不做任何校正。</summary>
    public int RarityTolerancePermille { get; init; } = DefaultRarityTolerancePermille;

    /// <summary>第二阶段重抽上限。达到上限仍未收敛时放弃约束（裁决记录 4），MUST NOT 死循环。</summary>
    public int MaxRerolls { get; init; } = 200;

    /// <summary>
    /// 同区同类型惩罚（裁决记录 3）：同一出生区每多一枚同类型信物，该区允许的稀有度偏差收窄这一千分比份额。
    /// 默认 500 即「有一对同类型的出生区只享有一半容差」——概率显著降低但不为零；1000 以上则等于禁止。
    /// </summary>
    public int SameTypePenaltyPermille { get; init; } = 500;

    /// <summary>公共区标准档升级概率（千分比），裁决记录 1：六类统一。</summary>
    public int StandardUpgradePermille { get; init; } = 200;

    /// <summary>公共区高档升级概率（千分比）。规格只要求公共区约 20% 且严格高于出生区，默认与标准档相同。</summary>
    public int HighUpgradePermille { get; init; } = 200;

    /// <summary>某预算档位的升级概率。出生区 MUST NOT 生成高阶，恒为 0。</summary>
    public int UpgradePermilleOf(BudgetTier tier) => tier switch
    {
        BudgetTier.Birth => 0,
        BudgetTier.Standard => StandardUpgradePermille,
        BudgetTier.High => HighUpgradePermille,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "未知预算档位。"),
    };

    /// <summary>
    /// 某预算档位的强度预算：单格期望稀有度（定点，<see cref="RelicWeights.RarityScale"/> 为单位）。
    /// 期望稀有度 = Σ 权重% × 稀有度 × (1 + 升级率)，权重倒数定义下 Σ 权重% × 稀有度 恒为 <c>100 × 类型数</c>。
    /// 用于「中央区预算严格高于出生区」的地图交叉检查。
    /// </summary>
    public int BudgetOf(BudgetTier tier)
    {
        int baseline = 100 * RelicWeights.Order.Length;
        return baseline * (1000 + UpgradePermilleOf(tier)) / 1000;
    }
}
