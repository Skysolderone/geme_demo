using System.Collections.Immutable;

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
    /// 此前的扫档结论全部作废；ai-eye 段 D 在新规则下重新扫档（内容集 V1——当时还没有内容集概念，缺省内容集后来改为 V2），只扫了 <see cref="Eye"/>、<see cref="Safety"/>、<see cref="Threat"/> 三维与停手阈值，
    /// 其余六维如实标注"沿用旧值、新规则下未单独扫档"——逐维口径见 <see cref="CalibrationOf"/>。停手阈值此后由 v2-recalibration 段 A 在内容集 V2 上以每档 20 局复核
    /// （口径见 <c>AiSearchConfig.PassThresholdCalibrationStatus</c>）；九维取值未随之改动。
    /// <para>守门测试 <c>默认评价权重的校准Tests</c> 断言本常量、<see cref="CalibrationOf"/> 与 <see cref="Default"/> 的每一维同步。
    /// 以未单独扫档维度的当前默认值产出的基线数据，引用时 MUST 注明权重口径，MUST NOT 与别的权重口径下的数据直接比较。</para>
    /// <para><b>计分规则 v2 下未校准</b>：以上全部是在计分规则 v1（不计阵型）下取得的；formation-tiers 把新局缺省改为计分规则 v2（阵型）之后未重扫，
    /// 见 <see cref="FormationScoringStatus"/>。本常量的文字记录的是 v1 下的校准经过，保持原样。</para>
    /// </summary>
    public const string CalibrationStatus = "ai-eye 段 D 校准（内容集 V1）：Eye / Safety / Threat 三维与停手阈值已在新规则下双向扫档；PowerGain / EnemyLoss / Relic / Growth / Initiative / Supply 六维沿用旧值、新规则下未单独扫档；停手阈值另经 v2-recalibration 在内容集 V2 上复核（每档 20 局，见 AiSearchConfig.PassThresholdCalibrationStatus）；more-pieces-relics 扩展计分后未重扫";

    /// <summary>
    /// more-pieces-relics（D10，负责人裁决不重新校准）扩展了计分——四种新棋子的位置加值、连营 / 犄角——之后的补注：上面的扫档全部是在旧内容上做的，
    /// 新内容下<b>没有</b>重扫。<see cref="CalibrationStatus"/> 与 <see cref="CalibrationOf"/> 的九维一律以它结尾；权重数值不变。以当前默认权重在内容集 v2 下产出的数据，
    /// 引用时 MUST 注明这一口径。停手阈值已由 v2-recalibration 段 A 在内容集 V2 上复核，<c>AiSearchConfig.PassThresholdCalibrationStatus</c> 不再带本补注。
    /// </summary>
    public const string ScoringExtendedStatus = "more-pieces-relics 扩展计分后未重扫";

    /// <summary>
    /// formation-tiers（design D4）之后的补注：阵型（计分规则 v2）让成档的棋串军势成倍跳升，势力的数值分布随之改变；而九维默认权重、
    /// 缺省停手阈值（<c>AiSearchConfig.DefaultPassThreshold</c>）与 2 人图覆盖表（<see cref="MapOverrides"/>，该表项已随 <c>siege-2p-base-v1</c> 于 retire-legacy-maps 删除，常量文字保留为历史口径）全部是在计分规则 v1（不计阵型）下取得的——
    /// 在新局缺省的计分规则 v2 下它们一律<b>未校准</b>，取值未改。评价函数与搜索也未改：各维仍取"结算后 − 开始前"，跨过门槛的跳升自然进入势力维；
    /// AI 不做跨门槛的多步规划，属已知限制。以这些取值在计分规则 v2 下产出的数据，引用时 MUST 注明这一口径（testing.md「计分口径一变，默认权重必须重扫」）；
    /// 是否改动由负责人按 formation-tiers D6 的验证跑局（v1 / v2 × 停手阈值 10 / 20 / 40）裁决。
    /// </summary>
    public const string FormationScoringStatus = "计分规则 v2 下未校准：九维权重、停手阈值与 2 人图覆盖表均在计分规则 v1（不计阵型）下取得，formation-tiers 引入阵型后未重扫";

    /// <summary>
    /// 校准所用地图已删除的补注（retire-legacy-maps 段 B）：ai-eye / v2-recalibration 的扫档都在 <c>siege-4p-base-v5</c> 上做，该图已删除；
    /// 九维默认权重与停手阈值在棋盘图上<b>未校准</b>（母任务 board-terrain 裁决 6），由 AI 校准 change 在棋盘图上重定。
    /// 凡校准口径里写到 v5 的地方都紧跟这条补注（<see cref="CalibrationOf"/> 三维、<c>AiSearchConfig.PassThresholdCalibrationStatus</c>）；
    /// 补注在那些口径里写成字面量（守门要求口径以单个字面量出现在源码里），与本常量的一致性由 <c>默认评价权重的校准Tests</c> 校对。
    /// </summary>
    public const string RetiredCalibrationMapNote = "该图已于 retire-legacy-maps 删除，棋盘图上未校准";

    /// <summary>未单独扫档维度的标注（<see cref="CalibrationOf"/> 对这六维返回它）。</summary>
    public const string NotSweptStatus = "沿用旧值、新规则下未单独扫档";

    /// <summary>
    /// 默认权重。校准口径（ai-eye 段 D，design D6，<b>内容集 V1</b>——缺省内容集已改为 V2，V2 上未重扫九维）：<c>siege-4p-base-v5</c>（<see cref="RetiredCalibrationMapNote"/>）、种子 1–200、4 名标准难度 AI、每档 200 局、小回合数截断 600；
    /// 按 <c>Eye → PassThreshold → Safety → Threat</c> 顺序逐维双向扫档，其余维度固定。选档（design D6 第 3 条 + 裁决 R20 / R24）：
    /// 截断率 ≤ 5% → 整局无提子占比取最低档、并列带宽 1 个二项标准误 → 已终局局的平均结束大回合离 7–10 最近 → 再并列取离初值 / 上一选值最近。
    /// 下列每档写作"截断 / 整局无提子 / 已终局局的平均结束大回合"；数据目录均在 <c>sim-out/</c> 下，扫档表见任务 09-23-ai-eye 的 implement 记录（段 D1、D2）。
    /// 下文的"停手阈值 80"是 ai-eye 在 V1 上的校准值；现行缺省停手阈值为 v2-recalibration 在 V2 上复核的 20（见 <c>AiSearchConfig.DefaultPassThreshold</c>）。
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
    /// <para>改任何一维仍须双向扫档、同种子同地图同内容集不少于 200 局（或经负责人裁决的更小规模，须写明局数并注明小样本）的前后对照，并连同本段、<see cref="CalibrationStatus"/> 与 <see cref="CalibrationOf"/> 一起更新；
    /// 计分、出局、终局或活形规则再变更时，校准随之失效，须重新标注。</para>
    /// <para><b>计分规则 v2 下未校准</b>：formation-tiers 引入阵型后，上面的扫档结论只对计分规则 v1 成立；v2 下九维均未重扫（<see cref="FormationScoringStatus"/>）。</para>
    /// </summary>
    public static readonly EvaluationWeights Default = new(
        PowerGain: 10, EnemyLoss: 8, Relic: 6, Safety: 35, Growth: 4, Initiative: 20, Supply: 2, Eye: 200, Threat: 25);

    /// <summary>
    /// 地图专属评价权重覆盖（v2-recalibration，ai-decision「地图专属评价权重覆盖」，design D4–D8）："地图标识 → 整表权重"的只读登记表。
    /// 它是 AI 配置，不是规则：不进地图数据、不改停手阈值与搜索参数；按 <see cref="Board.MapData.Id"/>（= 日志首部的地图标识）登记，
    /// 覆盖值写成"缺省表 + 显式差异"。<b>当前为空表</b>（retire-legacy-maps 段 B，design D2 / 已知歧义 4）：唯一一项 <c>siege-2p-base-v1</c>（Eye 50，
    /// v2-recalibration 段 B 在该图上扫出、计分规则 v1 下取得，<b>计分规则 v2 下未校准</b>，见 <see cref="FormationScoringStatus"/>）随该图删除；机制保留，
    /// 内置棋盘图是否登记覆盖留给 AI 校准 change。未登记的地图一律取 <see cref="Default"/>。
    /// 每一项的校准口径见 <see cref="MapOverrideCalibrationOf"/>；守门 <c>默认评价权重的校准Tests</c> / <c>地图专属评价权重覆盖Tests</c> 钉住键集合、取值与口径。
    /// <para>地图文件若自带与内置图相同的 <c>id</c> 会被套上覆盖（按标识键、不按摘要键）；首部的 <c>MapDigest</c> 可事后核对。</para>
    /// </summary>
    public static IReadOnlyDictionary<string, EvaluationWeights> MapOverrides { get; } =
        ImmutableSortedDictionary.Create<string, EvaluationWeights>(StringComparer.Ordinal);

    /// <summary>
    /// 按地图标识取权重的唯一实现（批量跑局的落成、终端与图形版建 AI 三个入口共用，design D5 / D6）。优先级：
    /// 显式权重（配置文件逐玩家的权重，或回放时日志首部记录的权重）&gt; 该地图登记的覆盖 &gt; <see cref="Default"/>。
    /// 显式权重整表生效，不与覆盖逐维合并。不分难度（design D7：简单难度只读其中三维，专家前瞻的模拟对手沿用专家本人的权重）。
    /// <para>AI 层 <c>HeuristicTurnController</c> 的 <c>weights ?? Default</c> 回落 MUST NOT 改调本函数：首部权重为空的旧日志回放时要按缺省表重建。</para>
    /// </summary>
    public static EvaluationWeights ForMapId(string mapId, EvaluationWeights? explicitWeights = null) => ForMapId(mapId, explicitWeights, MapOverrides);

    /// <summary>同 <see cref="ForMapId(string, EvaluationWeights?)"/>，登记表由参数给出（纯函数；测试接缝注入与缺省表不同的登记表，不引入可变静态）。</summary>
    internal static EvaluationWeights ForMapId(string mapId, EvaluationWeights? explicitWeights, IReadOnlyDictionary<string, EvaluationWeights> overrides)
    {
        ArgumentNullException.ThrowIfNull(mapId);
        ArgumentNullException.ThrowIfNull(overrides);
        return explicitWeights ?? (overrides.TryGetValue(mapId, out EvaluationWeights? table) ? table : Default);
    }

    /// <summary>某地图覆盖表的校准口径；未登记的地图为 <c>null</c>。登记表为空（retire-legacy-maps），故恒为 <c>null</c>；新增登记时须同时在这里给出口径。</summary>
    public static string? MapOverrideCalibrationOf(string mapId)
    {
        ArgumentNullException.ThrowIfNull(mapId);
        return null;
    }

    /// <summary>
    /// 某维度默认值的校准口径：扫过档的三维返回"内容集 / 地图 / 局数 / 种子 / 档位 / 数据目录"（ai-eye 段 D 在内容集 V1 上扫档），其余六维返回 <see cref="NotSweptStatus"/>；
    /// 九维一律以 <see cref="ScoringExtendedStatus"/> 结尾（more-pieces-relics 扩展计分后未重扫）。
    /// </summary>
    public static string CalibrationOf(EvaluationDimension dimension) => dimension switch
    {
        EvaluationDimension.Eye => "ai-eye 段 D 校准（内容集 V1）：siege-4p-base-v5（该图已于 retire-legacy-maps 删除，棋盘图上未校准）、种子 1–200、4 人标准难度、每档 200 局；0 / 50 / 100 / 200 / 400 五档 + 停手阈值 80 下 100 / 200 / 400 复核；sim-out/ai-eye-eye-*、sim-out/ai-eye-eyecheck-*；" + ScoringExtendedStatus,
        EvaluationDimension.Safety => "ai-eye 段 D 校准（内容集 V1）：siege-4p-base-v5（该图已于 retire-legacy-maps 删除，棋盘图上未校准）、种子 1–200、4 人标准难度、每档 200 局；10 / 20 / 35 / 50 / 70 五档；sim-out/ai-eye-safety-*、sim-out/ai-eye-pass-80；" + ScoringExtendedStatus,
        EvaluationDimension.Threat => "ai-eye 段 D 校准（内容集 V1）：siege-4p-base-v5（该图已于 retire-legacy-maps 删除，棋盘图上未校准）、种子 1–200、4 人标准难度、每档 200 局；0 / 10 / 25 / 50 / 100 五档；sim-out/ai-eye-threat-*、sim-out/ai-eye-pass-80；" + ScoringExtendedStatus,
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
