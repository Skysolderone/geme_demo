using System.Text.Json.Serialization;

namespace Siege.Core.Ai;

/// <summary>
/// AI 难度（设计文档 §15.2）。简单只看即时收益与眼位、贪心一条；标准 / 高难扩大候选数量与评价深度；专家在高难的候选生成之上做一层前瞻（expert-lookahead）。
/// 任何难度 MUST NOT 读隐藏信息。活形硬约束与停手阈值对四档一律生效（ai-eye D7：它们是对局收敛的底线，不是强度手段）；眼位维同样四档生效（ai-eye R26），威胁维从标准难度起生效。
/// 次序即序号：新档只在末尾追加，既有三档的名称与序号 MUST NOT 改变（日志、配置按名称读写，旧数据按序号也不漂移）。
/// </summary>
public enum AiDifficulty
{
    /// <summary>简单：只用即时势力增量、敌方势力损失与眼位三维（眼位见 ai-eye R26），候选批次 M = 1（对照组）。</summary>
    Easy,

    /// <summary>标准：九维评价，M = 8。</summary>
    Standard,

    /// <summary>高难：九维评价，M = 32，N 更大。</summary>
    Hard,

    /// <summary>
    /// 专家（expert-lookahead / expert-strength）：候选生成与高难完全相同，按落点格集合去重并以排除重跑补足落点不同的候选（多样候选，零随机），
    /// 再对前瞻集做一层前瞻、加上近似两层加分后重排。
    /// </summary>
    Expert,
}

/// <summary>
/// 候选剪枝参数（design.md D3 / 裁决 2）：先筛前 <see cref="CandidatePointCount"/>（N）个高价值落点，再组合不超过
/// <see cref="CandidateBatchCount"/>（M）个候选批次。初值：简单 N 6 / M 1，标准 N 12 / M 8，高难 N 24 / M 32；跑局实测后校准。
/// 另含停手阈值（ai-eye D4）——AI 配置而不是游戏规则，不影响人类玩家的合法操作。
/// </summary>
/// <param name="CandidatePointCount">候选落点数 N。</param>
/// <param name="CandidateBatchCount">候选批次数 M。</param>
/// <param name="ImmediateOnly">只评价即时收益与眼位（简单难度；眼位自 ai-eye R26 起计入，字段名沿用以保持配置与日志首部兼容）。</param>
/// <param name="CandidateCellLimit">
/// 候选格上限 K（frontier-map 裁决 12）：<c>null</c> = 未写，按地图取缺省（<see cref="CellLimitOn"/>：可落子格多于 <see cref="LargeMapPlayableThreshold"/> 取
/// <see cref="LargeMapCellLimit"/>，否则 0；retire-legacy-maps 段 A 起，此前未写即 0）；0 = 显式不限制；大于 0 = 显式上限。四档预设都不写。
/// 未写时不序列化；该项出现之前的旧记录缺这个字段，由跑局层按首部重建时补成 0（当时就是不限制），不经地图缺省。大于 0 且合法空格多于 K 时，
/// 先用一种代表类型对每格预演一次得格分（代表类型落不下而持有匠人的格，退而用匠人带改造的最高合法总分），
/// 取前 K 格（同分按坐标序），再只对这 K 格做完整的"类型 × 改造目标"枚举。
/// N / M 是在穷举<b>之后</b>截断，管不住大图上的预演次数；K 在穷举之前截断。不改评估函数、不消费随机流。
/// </param>
/// <param name="PassThreshold">
/// 停手阈值（ai-eye D4，非负整数，单位为加权总分）：贪心组批逐枚加入落点时，这一枚使整批加权总分的提升<b>严格大于</b>它才保留，否则撤回；
/// 一枚都没保留即 Pass。取 0 时退化为"严格提高"，与引入本参数之前逐步相同。四档难度的预设都取 <see cref="DefaultPassThreshold"/>（裁决 R1：共用；专家档见 expert-lookahead）。
/// 构造参数的缺省值是 0 而不是 <see cref="DefaultPassThreshold"/>：该项出现之前记录的剪枝参数（配置 / 日志首部里的 <c>Search</c>）缺这个字段，
/// 当时的保留条件就是严格提高，按 0 读入才能原样重建。
/// </param>
/// <param name="LookaheadWidth">
/// 前瞻宽度 W（expert-lookahead D9，非负整数）：0 与 1 都表示不前瞻；大于 1 时对自身评价前 W 个非空候选各模拟下一名对手的回应后重排（<see cref="ExpertLookahead"/>）。
/// 简单 / 标准 / 高难预设为 0，专家预设为 <see cref="DefaultLookaheadWidth"/>。为 0 时序列化 MUST NOT 写出该字段——三档旧难度的配置记录与日志首部与引入之前逐字节相同；
/// 缺该字段的旧记录按 0 读入（同 <see cref="PassThreshold"/> 的先例）。
/// </param>
/// <param name="DiverseSupplementLimit">
/// 多样补充上限 S（expert-strength D2 / D3 / D7，非负整数）：前瞻集按落点格集合去重后不足前瞻宽度个时，排除重跑（不扰动的贪心组批）的次数上限。
/// 0 = 多样候选关闭——既不去重也不补足，前瞻集与 expert-lookahead 逐项相同。四档预设都为 0（专家预设已退回一层，见 <see cref="Expert"/>）；可配置项，<c>Search</c> 显式给出时生效。
/// 为 0 时不写出，缺字段按 0 读（与 <see cref="LookaheadWidth"/> 同一口径）。
/// </param>
/// <param name="TwoPlyWeightPermille">
/// 两层权重 λ（expert-strength D4 / D7，千分数，非负整数）：前瞻后分数 = 一层分数 + ⌊λ‰ × max(0, 专家下一手最佳单点增量) / 1000⌋。
/// 0 = 不计两层加分、不做两层扫描。四档预设都为 0（专家预设已退回一层，见 <see cref="Expert"/>）；可配置项，<c>Search</c> 显式给出时生效。为 0 时不写出，缺字段按 0 读。
/// </param>
public sealed record AiSearchConfig(
    int CandidatePointCount,
    int CandidateBatchCount,
    bool ImmediateOnly,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? CandidateCellLimit = null,
    int PassThreshold = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int LookaheadWidth = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int DiverseSupplementLimit = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int TwoPlyWeightPermille = 0)
{
    /// <summary>专家预设的前瞻宽度（负责人裁决 2026-09-26：前 K = 4 个候选；规格里 K 专指候选格上限，故称 W）。</summary>
    public const int DefaultLookaheadWidth = 4;

    /// <summary>可落子格数超过它的地图算"大图"：各入口未显式配置 K 时取 <see cref="LargeMapCellLimit"/>，否则取 0。</summary>
    public const int LargeMapPlayableThreshold = 150;

    /// <summary>
    /// 默认停手阈值。<b>PassThreshold = 20</b>——v2-recalibration 段 A 在内容集 V2 上复核选定（负责人 2026-09-28 裁决 ①，design D1 / D2）：
    /// <c>siege-4p-base-v5</c>、4 名标准难度 AI、内容集 V2、带入 0、小回合数截断 600、种子 1–20、每档 20 局（小样本，胜率类只看方向），
    /// 九维权重逐玩家写死为当时的 <see cref="EvaluationWeights.Default"/>（Eye 200 / Safety 35 / Threat 25，未随之改动），档位 0 / 20 / 40 / 80。
    /// 每档写作"截断 / 整局无提子 / 已终局局的平均结束大回合"：0 → 0 / 3 / 9.15；20 → 0 / 3 / 9.25；40 → 0 / 5 / 9.05；80 → 0 / 8 / 8.40
    /// （<c>sim-out/v2-recalibration/pass-*</c>；80 / 0 两档与诊断 engagement-diagnosis b06 / b07 逐局相同）。
    /// 选档：① 截断 ≤ 1 / 20，四档全过；② 无提子最低 3 / 20（0、20 两档），带宽 1 个二项标准误 √(0.15 × 0.85 / 20) ≈ 0.080，比例 ≤ 0.230 进带，
    /// 40（0.25）与 80（0.40）出带；③ 0 与 20 的平均结束大回合都落在 7–10，距离同为 0；④ 取离现值 80 最近者 → 20。
    /// 80 是档位上界，本次不是完整的双向扫档（V1 下 80 → 160 无提子陡升，方向上不支持提高）；只在 v5 上复核，边疆图 / 生成图未复核。
    /// 选定值不是 80，按 design D3 另跑两批冒烟（同地图、同种子、同阈值 20）：简单 × 4 截断 0 / 20、最长 24 大回合、无"第 1 大回合全员一子不落即终局"
    /// （<c>sim-out/v2-recalibration/easy20-pass20</c>）；1 专家 + 3 标准截断 0 / 20（<c>sim-out/v2-recalibration/expert20-pass20</c>），均未回退。
    /// 四档难度共用（ai-eye 裁决 R1）。
    /// <para>历史口径（内容集 V1）：ai-eye 段 D 校准为 80——<c>siege-4p-base-v5</c>、种子 1–200、每档 200 局，0 / 10 / 20 / 40 / 80 / 160 六档
    /// （0 → 3.5% / 29.0% / 12.24；10 → 4.0% / 32.0% / 11.49；20 → 4.0% / 32.5% / 10.59；40 → 2.5% / 36.0% / 11.15；80 → 1.0% / 32.0% / 9.81；160 → 0% / 73.5% / 7.73，
    /// <c>sim-out/ai-eye-pass-*</c>、<c>sim-out/ai-eye-eye-200</c>）。当时简单难度在阈值 80 下 v5 种子 1–200 截断 36 / 200（按裁决 R26 下放眼位维之后，
    /// <c>sim-out/ai-eye-final-easy-eye</c>）；这些数据都在内容集 V1 上取得，不与上面的 V2 数据直接比较。</para>
    /// 改值须连同本段与 <see cref="PassThresholdCalibrationStatus"/> 一起更新（守门：<c>默认评价权重的校准Tests.默认停手阈值被改动</c>）。
    /// <para><b>计分规则 v2 下未校准</b>：阈值 20 是在计分规则 v1（不计阵型）下复核的；formation-tiers 引入阵型后势力增量的量级变了（成阵的一手可多出数倍），
    /// 同一个阈值在 v2 下的松紧未经验证（<see cref="EvaluationWeights.FormationScoringStatus"/>）。是否改动由负责人按 formation-tiers D6 的
    /// 验证跑局（阈值 10 / 20 / 40）裁决，本 change 不自行改值。</para>
    /// </summary>
    public const int DefaultPassThreshold = 20;

    /// <summary>默认停手阈值的校准口径（ai-decision「默认评价权重的校准」要求的显式标注）。</summary>
    /// <remarks>
    /// v2-recalibration 段 A 在内容集 V2 上复核后改写，不再带"more-pieces-relics 扩展计分后未重扫"的补注（九维权重仍带，见 <see cref="EvaluationWeights.ScoringExtendedStatus"/>）。
    /// 这里的"V2"指<b>内容集</b>；计分规则 v2（阵型，formation-tiers）下未校准，见 <see cref="EvaluationWeights.FormationScoringStatus"/>。
    /// </remarks>
    public const string PassThresholdCalibrationStatus = "v2-recalibration 段 A 复核（内容集 V2、小样本）：siege-4p-base-v5、4 人标准难度、带入 0、种子 1–20、每档 20 局；0 / 20 / 40 / 80 四档（截断 / 整局无提子 / 已终局局平均结束大回合：0 → 0 / 3 / 9.15；20 → 0 / 3 / 9.25；40 → 0 / 5 / 9.05；80 → 0 / 8 / 8.40）；选定 20（0 与 20 无提子并列最低、均在 7–10，取离 80 最近）；80 为档位上界、非完整双向扫档；sim-out/v2-recalibration/pass-*";

    /// <summary>大图的缺省候选格上限（边疆图上实测选定——当时 377 格，平台留白后为 411 格，见任务 09-19-frontier-map 的实施记录）。</summary>
    public const int LargeMapCellLimit = 24;

    /// <summary>简单：贪心一条、只看即时收益与眼位。</summary>
    public static readonly AiSearchConfig Easy = new(CandidatePointCount: 6, CandidateBatchCount: 1, ImmediateOnly: true, PassThreshold: DefaultPassThreshold);

    /// <summary>标准。</summary>
    public static readonly AiSearchConfig Standard = new(CandidatePointCount: 12, CandidateBatchCount: 8, ImmediateOnly: false, PassThreshold: DefaultPassThreshold);

    /// <summary>高难。</summary>
    public static readonly AiSearchConfig Hard = new(CandidatePointCount: 24, CandidateBatchCount: 32, ImmediateOnly: false, PassThreshold: DefaultPassThreshold);

    /// <summary>
    /// 专家：候选生成同高难（N 24 / M 32、同一扰动子流、同一停手阈值），另加前瞻宽度 <see cref="DefaultLookaheadWidth"/> 的一层前瞻（expert-lookahead）。
    /// 多样补充上限与两层权重为 0——<b>预设退回一层</b>（负责人裁决 2026-09-28，expert-strength 段 B 后）：v5、V2、阈值 20、1 被测 + 3 标准、
    /// 成对种子 1–60 扩样中，S = 8、λ = 1000‰ 的专家弱于高难——配对名次 好 7 / 同 33 / 差 20（符号检验 p = 0.019），胜 6 / 60 对 12 / 60，
    /// 平均名次 2.867 对 2.533（设计文档 v1.18 §15.2）。退回后的预设与改动前的专家逐步相同（守门：
    /// <c>专家难度的一层前瞻Tests.缺省预设的专家与改动前的专家逐步相同</c>）。多样候选与近似两层保留为可配置项，在 <c>Search</c> 里显式给出时生效；归因实验另开。
    /// </summary>
    public static readonly AiSearchConfig Expert = Hard with
    {
        LookaheadWidth = DefaultLookaheadWidth,
        DiverseSupplementLimit = 0,
        TwoPlyWeightPermille = 0,
    };

    /// <summary>某难度的默认参数。</summary>
    public static AiSearchConfig ForDifficulty(AiDifficulty difficulty) => difficulty switch
    {
        AiDifficulty.Easy => Easy,
        AiDifficulty.Standard => Standard,
        AiDifficulty.Hard => Hard,
        AiDifficulty.Expert => Expert,
        _ => throw new ArgumentOutOfRangeException(nameof(difficulty), difficulty, "未知难度。"),
    };

    /// <summary>
    /// 按地图大小取缺省候选格上限——这条阈值逻辑的<b>唯一</b>实现，批量 / 终端 / 图形三个入口共用。
    /// 只看可落子格数，不看地图的其他属性。
    /// </summary>
    public static int DefaultCellLimitFor(int playableCells) => playableCells > LargeMapPlayableThreshold ? LargeMapCellLimit : 0;

    /// <summary>
    /// 本配置在可落子格数为 <paramref name="playableCells"/> 的地图上实际生效的候选格上限：写了（含 0）取所写值，未写按 <see cref="DefaultCellLimitFor"/> 取。
    /// AI 决策（单点排序与专家的两层扫描）只经它读 K。
    /// </summary>
    public int CellLimitOn(int playableCells) => CandidateCellLimit ?? DefaultCellLimitFor(playableCells);

    /// <summary>
    /// 某难度在某张地图上的参数：<paramref name="cellLimit"/> 显式给出（含 0 = 不限制）优先，
    /// 未给出按 <see cref="DefaultCellLimitFor"/> 取。结果总是写明具体值（不为 <c>null</c>）：小图上是显式 0，
    /// 与 <see cref="ForDifficulty"/>（未写）在该图上的生效值相同、但不相等（retire-legacy-maps 段 A2 起预设不写上限）。
    /// </summary>
    public static AiSearchConfig ForMap(AiDifficulty difficulty, int playableCells, int? cellLimit = null) =>
        ForDifficulty(difficulty) with { CandidateCellLimit = cellLimit ?? DefaultCellLimitFor(playableCells) };

    /// <summary>参数校验：N、M 至少为 1；K、停手阈值、前瞻宽度、多样补充上限与两层权重非负。</summary>
    public AiSearchConfig Validated()
    {
        if (CandidatePointCount < 1 || CandidateBatchCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(CandidateBatchCount), "候选落点数 N 与候选批次数 M 至少为 1。");
        }

        if (CandidateCellLimit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(CandidateCellLimit), "候选格上限 K 须为非负整数（0 = 不限制）。");
        }

        if (PassThreshold < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PassThreshold), "停手阈值须为非负整数（0 = 严格提高即保留）。");
        }

        if (LookaheadWidth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(LookaheadWidth), "前瞻宽度须为非负整数（0 与 1 = 不前瞻）。");
        }

        if (DiverseSupplementLimit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(DiverseSupplementLimit), "多样补充上限须为非负整数（0 = 多样候选关闭）。");
        }

        if (TwoPlyWeightPermille < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(TwoPlyWeightPermille), "两层权重须为非负整数（千分数；0 = 不计两层加分）。");
        }

        return this;
    }
}
