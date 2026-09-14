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
public sealed record AiSearchConfig(int CandidatePointCount, int CandidateBatchCount, bool ImmediateOnly)
{
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

    /// <summary>参数校验：N、M 至少为 1。</summary>
    public AiSearchConfig Validated()
    {
        if (CandidatePointCount < 1 || CandidateBatchCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(CandidateBatchCount), "候选落点数 N 与候选批次数 M 至少为 1。");
        }

        return this;
    }
}
