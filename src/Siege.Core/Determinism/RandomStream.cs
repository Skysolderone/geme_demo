using System.Numerics;

namespace Siege.Core.Determinism;

/// <summary>
/// 一条由 <see cref="GameSeed.Stream"/> 派生的确定性随机序列（xoshiro256**）。
/// 只提供整数原语：等概率整数、加权抽样、千分比判定——没有任何浮点入口，加权抽样用整数累积权重 + 取模。
/// </summary>
/// <remarks>
/// <see cref="Consumed"/> 记录已消费次数，供「改变某条子流的消费次数不影响其他子流」的回归断言使用。
/// 本类不是线程安全的：一条子流只应由一个消费者按确定顺序推进。
/// </remarks>
public sealed class RandomStream
{
    private ulong _s0;
    private ulong _s1;
    private ulong _s2;
    private ulong _s3;

    internal RandomStream(string name, ulong s0, ulong s1, ulong s2, ulong s3)
    {
        Name = name;
        _s0 = s0;
        _s1 = s1;
        _s2 = s2;
        _s3 = s3;
        if ((_s0 | _s1 | _s2 | _s3) == 0)
        {
            // xoshiro 的全零状态是不动点；SplitMix64 展开几乎不可能给出全零，这里只是兜底。
            _s0 = 0x9E3779B97F4A7C15UL;
        }
    }

    /// <summary>子流名。</summary>
    public string Name { get; }

    /// <summary>已产出的 64 位随机数个数（含加权抽样内部拒绝重采的次数）。</summary>
    public long Consumed { get; private set; }

    /// <summary>下一个 64 位随机数。</summary>
    public ulong NextUInt64()
    {
        ulong result = BitOperations.RotateLeft(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = BitOperations.RotateLeft(_s3, 45);
        Consumed++;
        return result;
    }

    /// <summary>
    /// 丢弃接下来的 <paramref name="count"/> 个 64 位随机数，把子流推进到与「已消费 <paramref name="count"/> 次」完全相同的状态。
    /// 存档只记录各子流的消费次数，恢复时从头派生再推进——不持久化内部状态，序列算法升级时旧存档的语义仍清晰。
    /// </summary>
    public void Advance(long count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "推进次数不得为负。");
        }

        for (long i = 0; i < count; i++)
        {
            NextUInt64();
        }
    }

    /// <summary>等概率整数 <c>[0, maxExclusive)</c>。用拒绝采样消除取模偏差；拒绝区极小，且拒绝与否同样由种子决定。</summary>
    public int NextInt(int maxExclusive)
    {
        if (maxExclusive <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "上界必须为正。");
        }

        ulong range = (ulong)maxExclusive;
        ulong bound = RejectionBound(maxExclusive);
        while (true)
        {
            ulong r = NextUInt64();
            if (r < bound)
            {
                return (int)(r % range);
            }
        }
    }

    /// <summary>
    /// 拒绝采样的接受上界：<c>2^64 − (2^64 mod range)</c>，即不超过 2^64 的最大 <c>range</c> 倍数。
    /// 小于它的 64 位值恰好覆盖整数个完整周期，取模后各余数等概率；等于或大于它的值被拒绝重采。
    /// </summary>
    internal static ulong RejectionBound(int maxExclusive)
    {
        ulong range = (ulong)maxExclusive;
        return ulong.MaxValue - (ulong.MaxValue % range);
    }

    /// <summary>千分比判定：以 <paramref name="permille"/>/1000 的概率返回 <c>true</c>。</summary>
    public bool NextPermille(int permille)
    {
        if (permille is < 0 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(permille), permille, "千分比必须在 0..1000 之间。");
        }

        return NextInt(1000) < permille;
    }

    /// <summary>加权抽样：按整数权重返回下标。权重必须非负且总和为正；累积权重 + 整数取模，不做浮点归一化。</summary>
    public int WeightedPick(ReadOnlySpan<int> weights)
    {
        int total = 0;
        foreach (int w in weights)
        {
            if (w < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(weights), w, "权重不得为负。");
            }

            total = checked(total + w);
        }

        if (total <= 0)
        {
            throw new ArgumentException("权重总和必须为正。", nameof(weights));
        }

        int roll = NextInt(total);
        int cumulative = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            cumulative += weights[i];
            if (roll < cumulative)
            {
                return i;
            }
        }

        throw new InvalidOperationException("加权抽样越过了累积权重：这是不可能到达的分支。");
    }
}
