namespace Siege.Core.Ai;

/// <summary>九个评价维度（设计文档 §15.2；ai-eye 在末尾追加 8、9 两维），顺序即分解数组下标——前七维的下标不得变动。</summary>
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

    /// <summary>8. 眼位：己方眼值之和 + 己方已确定活形棋串数 × 3（ai-eye D1），眼信息只来自 <see cref="Siege.Core.Board.LifeShapeReport"/>。</summary>
    Eye,

    /// <summary>9. 威胁：敌方非已确定活形、气数 ≤ <see cref="GroupSafety.DangerLiberties"/> 的棋串棋子总数（ai-eye D1）。</summary>
    Threat,
}

/// <summary>
/// 九维权重，全部为整数，可整体替换以便跑局调参（design.md D2）。<see cref="Eye"/> / <see cref="Threat"/> 是 ai-eye 追加的两维；
/// 缺这两项的旧配置 / 旧日志首部按 0 读入（System.Text.Json 对缺失的构造参数取类型默认值）。
/// </summary>
public sealed record EvaluationWeights(
    int PowerGain,
    int EnemyLoss,
    int Relic,
    int Safety,
    int Growth,
    int Initiative,
    int Supply,
    int Eye,
    int Threat)
{
    /// <summary>
    /// 默认权重表的校准口径。restore-go-core-rules 起<b>七维全部未校准</b>：计分口径已变（军势整体乘倍率、不封顶、位置加值进倍率、
    /// 领地恢复计分、总势力回归"独占空格 + 棋串军势"），此前每一档扫档结论所依赖的分数尺度都不复存在，MUST NOT 再被当作校准依据。
    /// 重新扫档由 <c>ai-eye</c> 负责，届时解除本标注。
    /// <para>ai-decision「默认评价权重的校准」要求"尚未校准的维度 MUST 在代码中显式标注"——本常量即该标注；
    /// 守门测试 <c>默认评价权重的校准Tests</c> 断言它与 <see cref="Default"/> 的每一维同步。
    /// 以当前权重产出的基线数据，引用时 MUST 注明"未校准口径"，MUST NOT 与别的权重口径下的数据直接比较。</para>
    /// </summary>
    public const string CalibrationStatus = "未校准（restore-go-core-rules 起失效，待 ai-eye）";

    /// <summary>
    /// 默认权重。七维取值一律沿用旧值不动（restore-go-core-rules Non-goal：本 change 不调 AI 权重），但校准依据已随计分口径作废：
    /// <list type="bullet">
    /// <item><b>Safety = 35</b>——未校准（restore-go-core-rules 起失效，待 ai-eye）。旧依据是 artisan-terrain-edit 段 D 在旧计分口径下的扫档，已作废。</item>
    /// <item><b>PowerGain = 10、EnemyLoss = 8、Relic = 6、Growth = 4、Initiative = 20、Supply = 2</b>——未校准（restore-go-core-rules 起失效，待 ai-eye）：
    /// 自 heuristic-ai 阶段起就是初值，从未扫过档。</item>
    /// <item><b>Eye = 0、Threat = 0</b>——未校准（ai-eye 段 A 新增，先取 0：权重为 0 时决策与七维实现逐步相同）。初值与扫档归 ai-eye 段 D。</item>
    /// </list>
    /// <para>改任何一维仍须双向扫档、同种子同地图不少于 200 局的前后对照，并连同本段与 <see cref="CalibrationStatus"/> 一起更新。</para>
    /// </summary>
    public static readonly EvaluationWeights Default = new(
        PowerGain: 10, EnemyLoss: 8, Relic: 6, Safety: 35, Growth: 4, Initiative: 20, Supply: 2, Eye: 0, Threat: 0);

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
        EvaluationDimension.Eye => Eye,
        EvaluationDimension.Threat => Threat,
        _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, "未知评价维度。"),
    };
}
