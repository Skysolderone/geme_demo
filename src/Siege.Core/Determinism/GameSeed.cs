using System.Text;

namespace Siege.Core.Determinism;

/// <summary>
/// 对局种子：全局唯一的随机根。本身不产生随机数，只按<b>名字</b>派生互相独立的子流（<see cref="Stream"/>）。
/// </summary>
/// <remarks>
/// <para>规范 <c>.trellis/spec/core/determinism.md</c>「随机子流隔离」：信物生成（<c>relic-gen</c>）、征募（<c>recruit</c>）、
/// 流程初始化（<c>setup</c>）各走一条子流，改变其中一条的消费次数 MUST NOT 影响其他子流的取值。
/// 做法是每条子流的初始状态只由「种子 + 子流名」决定，与其他子流消费了多少无关。</para>
/// <para>派生用 FNV-1a 64 位散列子流名，与种子混合后经 SplitMix64 展开成 xoshiro256** 的 256 位状态。
/// 全部是位级确定的整数运算，不依赖 BCL 的随机类（其算法不承诺跨版本稳定）。</para>
/// </remarks>
public readonly record struct GameSeed(ulong Value)
{
    /// <summary>信物生成子流名。</summary>
    public const string RelicGeneration = "relic-gen";

    /// <summary>征募子流名。</summary>
    public const string Recruit = "recruit";

    /// <summary>流程初始化子流名。</summary>
    public const string Setup = "setup";

    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;
    private const ulong Golden = 0x9E3779B97F4A7C15UL;

    /// <summary>派生一条命名子流。同名同种子 → 逐项一致的序列；每次调用都从头开始，返回的流各自独立推进。</summary>
    public RandomStream Stream(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("子流名不得为空。", nameof(name));
        }

        ulong hash = FnvOffset;
        foreach (byte b in Encoding.UTF8.GetBytes(name))
        {
            hash ^= b;
            hash *= FnvPrime;
        }

        // 种子与名字散列各自过一次 SplitMix64 再异或，避免「种子 ^ 名字」这种线性混合让不同 (种子, 名字) 对撞出同一状态。
        ulong root = SplitMix64.Mix(Value) ^ SplitMix64.Mix(hash ^ Golden);
        ulong s0 = SplitMix64.Next(ref root);
        ulong s1 = SplitMix64.Next(ref root);
        ulong s2 = SplitMix64.Next(ref root);
        ulong s3 = SplitMix64.Next(ref root);
        return new RandomStream(name, s0, s1, s2, s3);
    }

    /// <summary>十六进制表示，写入对局记录。</summary>
    public override string ToString() => Value.ToString("X16");

    /// <summary>解析 <see cref="ToString"/> 的十六进制表示。</summary>
    public static GameSeed Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new GameSeed(Convert.ToUInt64(text.Trim(), 16));
    }
}

/// <summary>SplitMix64：只用于把 64 位根展开成子流初始状态，不直接对外产随机数。</summary>
internal static class SplitMix64
{
    internal static ulong Next(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        return Mix(state);
    }

    internal static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
