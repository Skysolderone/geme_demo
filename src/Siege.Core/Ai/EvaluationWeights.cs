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
    /// 默认权重。<see cref="Safety"/> = 5 是校准值：4 人基准图 v2、4 人 Standard AI、种子 1–200、大回合上限 15，
    /// 按 3 / 5 / 7 / 8 / 10 / 20 / 30 / 40 / 60 九档各跑 200 局（数据在 <c>sim-out/safety&lt;N&gt;/</c>，<c>Safety = 20</c> 即 <c>sim-out/denser-map-200/</c>）。
    /// <para>曲线非单调，且调高更不收敛：不收敛率 3→10.0%、5→9.5%、7→3.5%、8→13.0%、10→31.5%、20→27.5%、30→34.0%、40→42.0%、60→57.5%。
    /// 机理是安全权重越高，被威胁的棋串越是总有"补一口气就加分"的手可下，AI 因此不 Pass，对局拖到大回合上限。
    /// 取 5 而不取不收敛率最低的 7：7 的第 3 大回合领先者胜率 61.5%，与旧值 20 的 64.5% 没有差别，滚雪球原样保留；5 是 51.0%，
    /// 而"领先者胜率 ≤ 50%"是设计文档 §16 / §17 的目标项，"不收敛率"只是"越低越好"。取 3 的领先者胜率跌到 16.0%，低于 25% 的随机基线，同样出局。</para>
    /// <para>其余六维仍是 heuristic-ai 阶段的未校准初值，各自独立成轮；以它们产出的基线数据在引用时须注明权重口径。
    /// 调这一维必须双向扫档，不得只朝一个方向试探。</para>
    /// </summary>
    public static readonly EvaluationWeights Default = new(
        PowerGain: 10, EnemyLoss: 8, Relic: 6, Safety: 5, Growth: 4, Initiative: 20, Supply: 2);

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
