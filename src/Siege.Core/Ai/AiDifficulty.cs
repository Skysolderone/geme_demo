namespace Siege.Core.Ai;

/// <summary>
/// AI 难度（设计文档 §15.2）。简单只看即时收益与眼位、贪心一条；标准 / 高难扩大候选数量与评价深度，MUST NOT 读隐藏信息。
/// 活形硬约束与停手阈值对三档一律生效（ai-eye D7：它们是对局收敛的底线，不是强度手段）；眼位维同样三档生效（ai-eye R26），威胁维从标准难度起生效。
/// </summary>
public enum AiDifficulty
{
    /// <summary>简单：只用即时势力增量、敌方势力损失与眼位三维（眼位见 ai-eye R26），候选批次 M = 1（对照组）。</summary>
    Easy,

    /// <summary>标准：九维评价，M = 8。</summary>
    Standard,

    /// <summary>高难：九维评价，M = 32，N 更大。</summary>
    Hard,
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
/// 候选格上限 K（frontier-map 裁决 12）：0 = 不限制（缺省，与引入本参数之前逐步相同）。大于 0 且合法空格多于 K 时，
/// 先用一种代表类型对每格预演一次得格分（代表类型落不下而持有匠人的格，退而用匠人带改造的最高合法总分），
/// 取前 K 格（同分按坐标序），再只对这 K 格做完整的"类型 × 改造目标"枚举。
/// N / M 是在穷举<b>之后</b>截断，管不住大图上的预演次数；K 在穷举之前截断。不改评估函数、不消费随机流。
/// </param>
/// <param name="PassThreshold">
/// 停手阈值（ai-eye D4，非负整数，单位为加权总分）：贪心组批逐枚加入落点时，这一枚使整批加权总分的提升<b>严格大于</b>它才保留，否则撤回；
/// 一枚都没保留即 Pass。取 0 时退化为"严格提高"，与引入本参数之前逐步相同。三档难度的预设都取 <see cref="DefaultPassThreshold"/>（裁决 R1：三档共用）。
/// 构造参数的缺省值是 0 而不是 <see cref="DefaultPassThreshold"/>：该项出现之前记录的剪枝参数（配置 / 日志首部里的 <c>Search</c>）缺这个字段，
/// 当时的保留条件就是严格提高，按 0 读入才能原样重建。
/// </param>
public sealed record AiSearchConfig(int CandidatePointCount, int CandidateBatchCount, bool ImmediateOnly, int CandidateCellLimit = 0, int PassThreshold = 0)
{
    /// <summary>可落子格数超过它的地图算"大图"：各入口未显式配置 K 时取 <see cref="LargeMapCellLimit"/>，否则取 0。</summary>
    public const int LargeMapPlayableThreshold = 150;

    /// <summary>
    /// 默认停手阈值。<b>PassThreshold = 80</b>（= 8 × <see cref="EvaluationWeights.PowerGain"/> 默认权重 10）——ai-eye 段 D 校准（design D4 / D6，裁决 R24）：
    /// <c>siege-4p-base-v5</c>、种子 1–200、4 名标准难度 AI、每档 200 局、小回合数截断 600，Eye 200 / Safety 35 / Threat 25 下双向扫六档，
    /// 每档写作"截断 / 整局无提子 / 已终局局的平均结束大回合"：0 → 3.5% / 29.0% / 12.24；10 → 4.0% / 32.0% / 11.49；20 → 4.0% / 32.5% / 10.59；
    /// 40 → 2.5% / 36.0% / 11.15；80 → 1.0% / 32.0% / 9.81；160 → 0% / 73.5% / 7.73（<c>sim-out/ai-eye-pass-*</c>、<c>sim-out/ai-eye-eye-200</c>）。
    /// 0 / 10 / 80 在无提子最低档的 1 个二项标准误内并列，只有 80 的平均结束大回合落在 7–10；80 → 160 无提子陡升，最优不在边界。
    /// 三档难度共用（裁决 R1）。简单难度原只算 PowerGain / EnemyLoss 两维，在本阈值下 v5 种子 1–200 一子不落（<c>sim-out/ai-eye-final-easy</c>）；
    /// 按裁决 R26 把眼位维度下放到简单难度后同口径重跑：截断 36 / 200（18%）、74 局第 1 大回合全员一子不落（<c>sim-out/ai-eye-final-easy-eye</c>）——
    /// 简单难度仍未达到「截断 ≤ 5%」，按 R26 停下、未再调整，处理方式交负责人裁决（见任务 09-23-ai-eye 的段 D2 记录）。标准难度不受 R26 影响。
    /// 改值须连同本段与 <see cref="PassThresholdCalibrationStatus"/> 一起更新（守门：<c>默认评价权重的校准Tests.默认停手阈值被改动</c>）。
    /// </summary>
    public const int DefaultPassThreshold = 80;

    /// <summary>默认停手阈值的校准口径（ai-decision「默认评价权重的校准」要求的显式标注）。</summary>
    /// <remarks>末尾补注"more-pieces-relics 扩展计分后未重扫"（同 <see cref="EvaluationWeights.ScoringExtendedStatus"/>，ai-decision「规则变更使校准失效」）；取值不变。</remarks>
    public const string PassThresholdCalibrationStatus = "ai-eye 段 D 校准：siege-4p-base-v5、种子 1–200、4 人标准难度、每档 200 局；0 / 10 / 20 / 40 / 80 / 160 六档；sim-out/ai-eye-pass-*；more-pieces-relics 扩展计分后未重扫";

    /// <summary>大图的缺省候选格上限（边疆图上实测选定——当时 377 格，平台留白后为 411 格，见任务 09-19-frontier-map 的实施记录）。</summary>
    public const int LargeMapCellLimit = 24;

    /// <summary>简单：贪心一条、只看即时收益与眼位。</summary>
    public static readonly AiSearchConfig Easy = new(CandidatePointCount: 6, CandidateBatchCount: 1, ImmediateOnly: true, PassThreshold: DefaultPassThreshold);

    /// <summary>标准。</summary>
    public static readonly AiSearchConfig Standard = new(CandidatePointCount: 12, CandidateBatchCount: 8, ImmediateOnly: false, PassThreshold: DefaultPassThreshold);

    /// <summary>高难。</summary>
    public static readonly AiSearchConfig Hard = new(CandidatePointCount: 24, CandidateBatchCount: 32, ImmediateOnly: false, PassThreshold: DefaultPassThreshold);

    /// <summary>某难度的默认参数。</summary>
    public static AiSearchConfig ForDifficulty(AiDifficulty difficulty) => difficulty switch
    {
        AiDifficulty.Easy => Easy,
        AiDifficulty.Standard => Standard,
        AiDifficulty.Hard => Hard,
        _ => throw new ArgumentOutOfRangeException(nameof(difficulty), difficulty, "未知难度。"),
    };

    /// <summary>
    /// 按地图大小取缺省候选格上限——这条阈值逻辑的<b>唯一</b>实现，批量 / 终端 / 图形三个入口共用。
    /// 只看可落子格数，不看地图的其他属性。
    /// </summary>
    public static int DefaultCellLimitFor(int playableCells) => playableCells > LargeMapPlayableThreshold ? LargeMapCellLimit : 0;

    /// <summary>
    /// 某难度在某张地图上的参数：<paramref name="cellLimit"/> 显式给出（含 0 = 不限制）优先，
    /// 未给出按 <see cref="DefaultCellLimitFor"/> 取。小图上与 <see cref="ForDifficulty"/> 相等。
    /// </summary>
    public static AiSearchConfig ForMap(AiDifficulty difficulty, int playableCells, int? cellLimit = null) =>
        ForDifficulty(difficulty) with { CandidateCellLimit = cellLimit ?? DefaultCellLimitFor(playableCells) };

    /// <summary>参数校验：N、M 至少为 1；K 与停手阈值非负。</summary>
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

        return this;
    }
}
