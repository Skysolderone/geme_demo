using Siege.Core.Determinism;

namespace Siege.Core.Board.Maps;

/// <summary>
/// 地图随机源（map-generator D2）：一条只由（地图种子, 尝试序号）决定的整数随机序列。
/// 与对局随机共用同一份 PRNG 实现（SplitMix64 展开 + <see cref="RandomStream"/> 的 xoshiro256**），但<b>不是</b>对局种子的子流——
/// 生成器拿不到对局种子，对局流程拿不到地图种子。
/// </summary>
/// <remarks>规格：openspec/changes/map-generator/specs/map-generation —— Requirement: 地图种子与确定性 / 校验闭环</remarks>
internal static class MapRandom
{
    /// <summary>域分隔常量：让（种子, 尝试序号）的混合不与对局侧"种子 ^ 子流名散列"的任何取值同构。</summary>
    private const ulong AttemptDomain = 0xA5C3_1D2B_7E49_F06DUL;

    /// <summary>第 <paramref name="attempt"/> 次尝试（0 起）的随机源。同输入 → 逐项相同的序列；每次调用都从头开始。</summary>
    internal static RandomStream ForAttempt(ulong mapSeed, int attempt)
    {
        if (attempt < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt), attempt, "尝试序号不得为负。");
        }

        ulong root = SplitMix64.Mix(mapSeed) ^ SplitMix64.Mix((ulong)attempt ^ AttemptDomain);
        ulong s0 = SplitMix64.Next(ref root);
        ulong s1 = SplitMix64.Next(ref root);
        ulong s2 = SplitMix64.Next(ref root);
        ulong s3 = SplitMix64.Next(ref root);
        return new RandomStream("map-gen", s0, s1, s2, s3);
    }

    /// <summary>域分隔常量：新地表投放子流（terrain-surfaces D6），与尝试子流的任何取值不同构。</summary>
    private const ulong SurfacesDomain = 0x3F71_C8E2_5AD9_0B64UL;

    /// <summary>
    /// 新地表投放的独立随机源：只由地图种子决定，不依赖尝试序号，也不消耗布局子流——
    /// 同一种子开 / 关新地表的两张图，布局部分因此逐格相同。
    /// </summary>
    internal static RandomStream ForSurfaces(ulong mapSeed)
    {
        ulong root = SplitMix64.Mix(mapSeed ^ SurfacesDomain) ^ SplitMix64.Mix(SurfacesDomain);
        ulong s0 = SplitMix64.Next(ref root);
        ulong s1 = SplitMix64.Next(ref root);
        ulong s2 = SplitMix64.Next(ref root);
        ulong s3 = SplitMix64.Next(ref root);
        return new RandomStream("map-gen-surfaces", s0, s1, s2, s3);
    }
}
