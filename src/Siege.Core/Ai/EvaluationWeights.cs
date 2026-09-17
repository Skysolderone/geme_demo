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
    /// 默认权重。<see cref="Safety"/> = 27 是校准值（scoring-sites 段 C，负责人 2026-09-17 拍板，裁决 S-15）：4 人基准图 v4、
    /// 据点分值 5 / 15 / 45、4 人 Standard AI、种子 1–200、大回合上限 15，
    /// 按 3 / 5 / 7 / 8 / 10 / 20 / 30 / 40 / 60 九档加 22 / 25 / 27 加密档各跑 200 局（数据在 <c>sim-out/sites-safety&lt;N&gt;/</c>）。
    /// <para>第 3 大回合领先者胜率：3→80.5%、5→95.5%、10→95.0%、20→95.5%、22→76.0%、25→31.0%、27→25.5%、30→17.0%、40→13.5%、60→15.5%。
    /// 20 以下领先者在第 4 大回合先手抢光岛上石碑、滚雪球；22–27 之间逐步转为围绕石碑相持。
    /// 取 27：领先者胜率落在 25% 基线上，不收敛率 6.5%，整局无提子 0 局；40 / 60 有过半对局整局无提子而出局，30 的 17% 显著低于基线。
    /// 代价：打法偏保守，每批次提子 0.29，石碑争议 52%，终局据点分占比 22.1%（低于 25% 目标）。</para>
    /// <para>旧值 5 是地形改造前 v2 领地计分下的校准（ai-safety-weight），在据点计分下已失效。
    /// 其余六维仍是 heuristic-ai 阶段的未校准初值，各自独立成轮；以它们产出的基线数据在引用时须注明权重口径。
    /// 调这一维必须双向扫档，不得只朝一个方向试探。</para>
    /// </summary>
    public static readonly EvaluationWeights Default = new(
        PowerGain: 10, EnemyLoss: 8, Relic: 6, Safety: 27, Growth: 4, Initiative: 20, Supply: 2);

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
