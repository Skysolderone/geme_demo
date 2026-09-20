namespace Siege.Core.Ai;

/// <summary>AI 难度（设计文档 §15.2）。简单只看即时收益、贪心一条；标准 / 高难扩大候选数量与评价深度，MUST NOT 读隐藏信息。</summary>
public enum AiDifficulty
{
    /// <summary>简单：只用即时势力增量与敌方势力损失两维，候选批次 M = 1（对照组）。</summary>
    Easy,

    /// <summary>标准：七维评价，M = 8。</summary>
    Standard,

    /// <summary>高难：七维评价，M = 32，N 更大。</summary>
    Hard,
}

/// <summary>
/// 候选剪枝参数（design.md D3 / 裁决 2）：先筛前 <see cref="CandidatePointCount"/>（N）个高价值落点，再组合不超过
/// <see cref="CandidateBatchCount"/>（M）个候选批次。初值：简单 N 6 / M 1，标准 N 12 / M 8，高难 N 24 / M 32；跑局实测后校准。
/// </summary>
/// <param name="CandidatePointCount">候选落点数 N。</param>
/// <param name="CandidateBatchCount">候选批次数 M。</param>
/// <param name="ImmediateOnly">只评价即时收益（简单难度）。</param>
/// <param name="CandidateCellLimit">
/// 候选格上限 K（frontier-map 裁决 12）：0 = 不限制（缺省，与引入本参数之前逐步相同）。大于 0 且合法空格多于 K 时，
/// 先用一种代表类型对每格预演一次得格分（代表类型落不下而持有匠人的格，退而用匠人带改造的最高合法总分），
/// 取前 K 格（同分按坐标序），再只对这 K 格做完整的"类型 × 改造目标"枚举。
/// N / M 是在穷举<b>之后</b>截断，管不住大图上的预演次数；K 在穷举之前截断。不改评估函数、不消费随机流。
/// </param>
public sealed record AiSearchConfig(int CandidatePointCount, int CandidateBatchCount, bool ImmediateOnly, int CandidateCellLimit = 0)
{
    /// <summary>可落子格数超过它的地图算"大图"：各入口未显式配置 K 时取 <see cref="LargeMapCellLimit"/>，否则取 0。</summary>
    public const int LargeMapPlayableThreshold = 150;

    /// <summary>大图的缺省候选格上限（边疆图 377 格上实测选定，见任务 09-19-frontier-map 的实施记录）。</summary>
    public const int LargeMapCellLimit = 24;

    /// <summary>简单：贪心一条、只看即时收益。</summary>
    public static readonly AiSearchConfig Easy = new(CandidatePointCount: 6, CandidateBatchCount: 1, ImmediateOnly: true);

    /// <summary>标准。</summary>
    public static readonly AiSearchConfig Standard = new(CandidatePointCount: 12, CandidateBatchCount: 8, ImmediateOnly: false);

    /// <summary>高难。</summary>
    public static readonly AiSearchConfig Hard = new(CandidatePointCount: 24, CandidateBatchCount: 32, ImmediateOnly: false);

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

    /// <summary>参数校验：N、M 至少为 1；K 非负。</summary>
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

        return this;
    }
}
