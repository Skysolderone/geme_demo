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
    /// 默认权重。<see cref="Safety"/> = 35 是校准值（artisan-terrain-edit 段 D，负责人 2026-09-18 拍板，裁决 T-13）：4 人基准图 v4、据点分值 5 / 15 / 45、匠人征募权重 5、4 人 Standard AI、种子 1–200、大回合上限 15，
    /// 据点分值 5 / 15 / 45、4 人 Standard AI、种子 1–200、大回合上限 15，
    /// 按 25 / 27 / 30 / 35 / 40 / 45 各跑 200 局（数据在 <c>sim-out/artisan-w5-s&lt;N&gt;/</c>，匠人权重档在 <c>sim-out/artisan-w&lt;N&gt;/</c>）。
    /// <para>第 3 大回合领先者胜率（匠人权重 5）：25→44.0%、27→35.5%、30→34.5%、35→24.0%、40→22.5%、45→23.0%。
    /// 40 与 45 各有 78 / 94 局整局无提子（目标 ≤ 5%），出局；35 的整局无提子 0 局、不收敛率 26.0%（全档最低）、
    /// 终局据点分占比 31.5%（落进 25–45% 目标）、每批次提子 0.52（scoring-sites 终版为 0.29）。
    /// 匠人权重先扫 5 / 10 / 18（Safety 27）：领先者胜率 35.5% / 45.5% / 51.5%、不收敛率 38.0% / 44.5% / 57.0%，取 5。
    /// 遗留：不收敛率 26% 仍显著高于 scoring-sites 终版的 6.5%，成因是栅栏使盘面更难填满；本轮不动终局条件（Non-goals）。</para>
    /// <para>旧值 5 是地形改造前 v2 领地计分下的校准（ai-safety-weight），在据点计分下已失效。
    /// 其余六维仍是 heuristic-ai 阶段的未校准初值，各自独立成轮；以它们产出的基线数据在引用时须注明权重口径。
    /// 调这一维必须双向扫档，不得只朝一个方向试探。</para>
    /// </summary>
    public static readonly EvaluationWeights Default = new(
        PowerGain: 10, EnemyLoss: 8, Relic: 6, Safety: 35, Growth: 4, Initiative: 20, Supply: 2);

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
