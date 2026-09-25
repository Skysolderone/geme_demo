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
    /// 默认权重表的校准口径（ai-decision「默认评价权重的校准」要求的显式标注）。restore-go-core-rules 改了计分口径、life-shape / life-single-stone 改了活形规则，
    /// 此前的扫档结论全部作废；ai-eye 段 D 在新规则下重新扫档，只扫了 <see cref="Eye"/>、<see cref="Safety"/>、<see cref="Threat"/> 三维与停手阈值，
    /// 其余六维如实标注"沿用旧值、新规则下未单独扫档"——逐维口径见 <see cref="CalibrationOf"/>。
    /// <para>守门测试 <c>默认评价权重的校准Tests</c> 断言本常量、<see cref="CalibrationOf"/> 与 <see cref="Default"/> 的每一维同步。
    /// 以未单独扫档维度的当前默认值产出的基线数据，引用时 MUST 注明权重口径，MUST NOT 与别的权重口径下的数据直接比较。</para>
    /// </summary>
    public const string CalibrationStatus = "ai-eye 段 D 校准：Eye / Safety / Threat 三维与停手阈值已在新规则下双向扫档；PowerGain / EnemyLoss / Relic / Growth / Initiative / Supply 六维沿用旧值、新规则下未单独扫档；more-pieces-relics 扩展计分后未重扫";

    /// <summary>
    /// more-pieces-relics（D10，负责人裁决不重新校准）扩展了计分——四种新棋子的位置加值、连营 / 犄角——之后的补注：上面的扫档全部是在旧内容上做的，
    /// 新内容下<b>没有</b>重扫。<see cref="CalibrationStatus"/>、<see cref="CalibrationOf"/> 的九维与 <c>AiSearchConfig.PassThresholdCalibrationStatus</c> 一律以它结尾；
    /// 权重数值不变。以当前默认值在内容集 v2 下产出的数据，引用时 MUST 注明这一口径。
    /// </summary>
    public const string ScoringExtendedStatus = "more-pieces-relics 扩展计分后未重扫";

    /// <summary>未单独扫档维度的标注（<see cref="CalibrationOf"/> 对这六维返回它）。</summary>
    public const string NotSweptStatus = "沿用旧值、新规则下未单独扫档";

    /// <summary>
    /// 默认权重。校准口径（ai-eye 段 D，design D6）：<c>siege-4p-base-v5</c>、种子 1–200、4 名标准难度 AI、每档 200 局、小回合数截断 600；
    /// 按 <c>Eye → PassThreshold → Safety → Threat</c> 顺序逐维双向扫档，其余维度固定。选档（design D6 第 3 条 + 裁决 R20 / R24）：
    /// 截断率 ≤ 5% → 整局无提子占比取最低档、并列带宽 1 个二项标准误 → 已终局局的平均结束大回合离 7–10 最近 → 再并列取离初值 / 上一选值最近。
    /// 下列每档写作"截断 / 整局无提子 / 已终局局的平均结束大回合"；数据目录均在 <c>sim-out/</c> 下，扫档表见任务 09-23-ai-eye 的 implement 记录（段 D1、D2）。
    /// <list type="bullet">
    /// <item><b>Eye = 200</b>——ai-eye 段 D 校准（Threat 25、Safety 35）。停手阈值 20 下五档：0 → 0% / 53.5% / 7.9；50 → 0% / 66.0% / 7.87；100 → 0.5% / 38.0% / 11.91；
    /// 200 → 4.0% / 32.5% / 10.59；400 → 4.0% / 34.5% / 11.44（<c>sim-out/ai-eye-eye-*</c>、<c>sim-out/ai-eye-base</c>）。
    /// 段 D2 在停手阈值 80 下复核三档：100 → 0% / 70.0% / 7.79；200 → 1.0% / 32.0% / 9.81；400 → 2.0% / 36.5% / 10.11
    /// （<c>sim-out/ai-eye-eyecheck-100</c>、<c>sim-out/ai-eye-pass-80</c>、<c>sim-out/ai-eye-eyecheck-400</c>），200 仍是无提子最低档，维持。</item>
    /// <item><b>Safety = 35</b>——ai-eye 段 D 校准（Eye 200、Threat 25、停手阈值 80），取值恰与旧值相同。五档：10 → 3.0% / 38.5% / 11.05；20 → 2.5% / 32.0% / 10.56；
    /// 35 → 1.0% / 32.0% / 9.81；50 → 0% / 62.0% / 8.19；70 → 0% / 77.0% / 7.76（<c>sim-out/ai-eye-safety-*</c>、<c>sim-out/ai-eye-pass-80</c>）。
    /// 20 与 35 无提子并列，35 的平均结束大回合落在 7–10。</item>
    /// <item><b>Threat = 25</b>——ai-eye 段 D 校准（Eye 200、Safety 35、停手阈值 80），取值即初值。五档：0 → 2.0% / 32.0% / 9.93；10 → 3.0% / 32.0% / 9.91；
    /// 25 → 1.0% / 32.0% / 9.81；50 → 0% / 45.5% / 8.51；100 → 0% / 70.0% / 7.87（<c>sim-out/ai-eye-threat-*</c>、<c>sim-out/ai-eye-pass-80</c>）。
    /// 0 / 10 / 25 三档并列且都在 7–10，取离初值最近。</item>
    /// <item><b>PowerGain = 10、EnemyLoss = 8、Relic = 6、Growth = 4、Initiative = 20、Supply = 2</b>——沿用旧值、新规则下未单独扫档（<see cref="NotSweptStatus"/>）。
    /// 自 heuristic-ai 阶段起就是初值；段 D 的 20 个参数点里 <c>PowerGain</c> 逐批分解中位占比最高 12.5%，未淹没其他维度，不需要对数刻度（任务 4.3）。</item>
    /// </list>
    /// <para>选定组合（= <c>sim-out/ai-eye-pass-80</c>）：截断 2 / 200（1.0%，种子 101、171，均为多人互提循环——同形判重键含棋子类型，见裁决 R25）、
    /// 整局无提子 64 / 200（32.0%，全部 20 个参数点均未达到 ≤ 5%，按 R20 取最低档如实报告，留给后续地图 / 信物数值 change）、
    /// 已终局局平均结束大回合 9.81、第 3 大回合领先者胜率 70.9%（只记录，不作否决）。</para>
    /// <para>改任何一维仍须双向扫档、同种子同地图不少于 200 局的前后对照，并连同本段、<see cref="CalibrationStatus"/> 与 <see cref="CalibrationOf"/> 一起更新；
    /// 计分、出局、终局或活形规则再变更时，校准随之失效，须重新标注。</para>
    /// </summary>
    public static readonly EvaluationWeights Default = new(
        PowerGain: 10, EnemyLoss: 8, Relic: 6, Safety: 35, Growth: 4, Initiative: 20, Supply: 2, Eye: 200, Threat: 25);

    /// <summary>
    /// 某维度默认值的校准口径：扫过档的三维返回"地图 / 局数 / 种子 / 档位 / 数据目录"，其余六维返回 <see cref="NotSweptStatus"/>；
    /// 九维一律以 <see cref="ScoringExtendedStatus"/> 结尾（more-pieces-relics 扩展计分后未重扫）。
    /// </summary>
    public static string CalibrationOf(EvaluationDimension dimension) => dimension switch
    {
        EvaluationDimension.Eye => "ai-eye 段 D 校准：siege-4p-base-v5、种子 1–200、4 人标准难度、每档 200 局；0 / 50 / 100 / 200 / 400 五档 + 停手阈值 80 下 100 / 200 / 400 复核；sim-out/ai-eye-eye-*、sim-out/ai-eye-eyecheck-*；" + ScoringExtendedStatus,
        EvaluationDimension.Safety => "ai-eye 段 D 校准：siege-4p-base-v5、种子 1–200、4 人标准难度、每档 200 局；10 / 20 / 35 / 50 / 70 五档；sim-out/ai-eye-safety-*、sim-out/ai-eye-pass-80；" + ScoringExtendedStatus,
        EvaluationDimension.Threat => "ai-eye 段 D 校准：siege-4p-base-v5、种子 1–200、4 人标准难度、每档 200 局；0 / 10 / 25 / 50 / 100 五档；sim-out/ai-eye-threat-*、sim-out/ai-eye-pass-80；" + ScoringExtendedStatus,
        _ => NotSweptStatus + "；" + ScoringExtendedStatus,
    };

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
