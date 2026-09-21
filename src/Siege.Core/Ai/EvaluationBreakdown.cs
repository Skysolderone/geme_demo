using System.Collections.Immutable;
using System.Numerics;

namespace Siege.Core.Ai;

/// <summary>
/// 一次评价的分维度分解（design.md D2）：七个维度各自的原始值与权重，总分 = Σ 权重 × 原始值。
/// 全部整数，不含浮点；势力增量来自任意精度的势力值（restore-go-core-rules D1），因此原始值与总分同为 <see cref="BigInteger"/>，不溢出。
/// </summary>
public sealed record EvaluationBreakdown(ImmutableArray<BigInteger> Raw, EvaluationWeights Weights)
{
    /// <summary>维度数。</summary>
    public const int DimensionCount = 7;

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
        $"势力{Raw[0]} 敌损{Raw[1]} 信物{Raw[2]} 安全{Raw[3]} 成长{Raw[4]} 先手{Raw[5]} 供给{Raw[6]} = {Total}";
}
