using System.Collections.Immutable;
using System.Numerics;

namespace Siege.Core.Ai;

/// <summary>
/// 一次评价的分维度分解（design.md D2）：九个维度各自的原始值与权重，总分 = Σ 权重 × 原始值。
/// 全部整数，不含浮点；势力增量来自任意精度的势力值（restore-go-core-rules D1），因此原始值与总分同为 <see cref="BigInteger"/>，不溢出。
/// </summary>
public sealed record EvaluationBreakdown(ImmutableArray<BigInteger> Raw, EvaluationWeights Weights)
{
    /// <summary>维度数。</summary>
    public const int DimensionCount = 9;

    /// <summary>
    /// AI 评价版本，写入跑局日志首部（ai-eye 1.1）。1 = 七维（ai-eye 之前，旧日志首部缺该字段）；2 = ai-eye 九维（眼位、威胁）。
    /// 维度数或任一维原始值的算法变化 MUST 递增它：旧版本日志的 AI 决策不能用新评价逐步重现。
    /// </summary>
    public const int Version = 2;

    /// <summary>全零分解（空批次 / Pass）。</summary>
    public static EvaluationBreakdown Zero(EvaluationWeights weights) => new([.. new BigInteger[DimensionCount]], weights);

    /// <summary>某维度的原始值。</summary>
    public BigInteger RawOf(EvaluationDimension dimension) => Raw[(int)dimension];

    /// <summary>某维度的加权贡献。</summary>
    public BigInteger ContributionOf(EvaluationDimension dimension) => Weights.Of(dimension) * Raw[(int)dimension];

    /// <summary>加权总分。</summary>
    public BigInteger Total
    {
        get
        {
            BigInteger total = BigInteger.Zero;
            for (int i = 0; i < DimensionCount; i++)
            {
                total += ContributionOf((EvaluationDimension)i);
            }

            return total;
        }
    }

    /// <summary>值相等：逐维比较（<see cref="ImmutableArray{T}"/> 默认是引用相等）。</summary>
    public bool Equals(EvaluationBreakdown? other) =>
        other is not null && Weights == other.Weights && Raw.SequenceEqual(other.Raw);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Weights);
        foreach (BigInteger value in Raw)
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }

    public override string ToString() =>
        $"势力{Raw[0]} 敌损{Raw[1]} 信物{Raw[2]} 安全{Raw[3]} 成长{Raw[4]} 先手{Raw[5]} 供给{Raw[6]} 眼位{Raw[7]} 威胁{Raw[8]} = {Total}";
}
