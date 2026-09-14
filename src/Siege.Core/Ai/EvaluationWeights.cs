namespace Siege.Core.Ai;

/// <summary>七个评价维度（设计文档 §15.2），顺序即分解数组下标。</summary>
public enum EvaluationDimension
{
    /// <summary>1. 即时势力增量。</summary>
    PowerGain,

    /// <summary>2. 敌方势力损失与提子规模。</summary>
    EnemyLoss,

    /// <summary>3. 信物发现、控制与阻断价值。</summary>
    Relic,

    /// <summary>4. 棋串剩余气、两眼潜力与被围杀风险。</summary>
    Safety,

    /// <summary>5. 连珠、倍增与协同的组合成长。</summary>
    Growth,

    /// <summary>6. 下一大回合先手位变化。</summary>
    Initiative,

    /// <summary>7. 手牌供给与部署能力匹配度。</summary>
    Supply,
}

/// <summary>七维权重，全部为整数，可整体替换以便跑局调参（design.md D2）。</summary>
public sealed record EvaluationWeights(
    int PowerGain,
    int EnemyLoss,
    int Relic,
    int Safety,
    int Growth,
    int Initiative,
    int Supply)
{
    /// <summary>
    /// 默认权重。初值，跑局后校准。安全权重曾取 3：基准图 5 局全部 600 小回合不终局（落子数 ≈ 被提数，纯绞肉循环）；
    /// 取 10 仍不终局，取 20 两颗种子分别在第 11 / 53 大回合整轮 Pass 收尾，取 40 一半收敛——因此初值 20，并列为阶段 B 的首个校准项。
    /// </summary>
    public static readonly EvaluationWeights Default = new(
        PowerGain: 10, EnemyLoss: 8, Relic: 6, Safety: 20, Growth: 4, Initiative: 20, Supply: 2);

    /// <summary>某维度的权重。</summary>
    public int Of(EvaluationDimension dimension) => dimension switch
    {
        EvaluationDimension.PowerGain => PowerGain,
        EvaluationDimension.EnemyLoss => EnemyLoss,
        EvaluationDimension.Relic => Relic,
        EvaluationDimension.Safety => Safety,
        EvaluationDimension.Growth => Growth,
        EvaluationDimension.Initiative => Initiative,
        EvaluationDimension.Supply => Supply,
        _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, "未知评价维度。"),
    };
}
