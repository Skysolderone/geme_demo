using Siege.Core.Determinism;

namespace Siege.Core.Board.Maps;

/// <summary>
/// 地图随机源（map-generator D2）：一条只由（地图种子, 人数, 尝试序号）决定的整数随机序列。
/// 与对局随机共用同一份 PRNG 实现（SplitMix64 展开 + <see cref="RandomStream"/> 的 xoshiro256**），但<b>不是</b>对局种子的子流——
/// 生成器拿不到对局种子，对局流程拿不到地图种子。
/// </summary>
/// <remarks>规格：openspec/changes/map-generator/specs/map-generation —— Requirement: 地图种子与确定性 / 校验闭环</remarks>
internal static class MapRandom
{
    /// <summary>
    /// 棋盘档生成（board-map D7）的第 <paramref name="attempt"/> 个随机源（0 起）：只由（地图种子, 人数, 序号）决定，
    /// 用独立的域常量（引入时与已删除的边疆档随机源互不同构；retire-legacy-maps 段 B 删掉边疆档随机源后，域常量原样保留，内置棋盘图因此不变）。
    /// 人数进入派生（board-isolated-gen：4 人也一样），同一种子的 2 / 3 / 4 人图互不共享棋盘布局。
    /// </summary>
    internal static RandomStream ForBoardAttempt(ulong mapSeed, int players, int attempt)
    {
        if (attempt < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt), attempt, "尝试序号不得为负。");
        }

        if (players < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(players), players, "人数至少为 1。");
        }

        ulong root = SplitMix64.Mix(mapSeed ^ BoardDomain) ^ SplitMix64.Mix((ulong)attempt ^ BoardAttemptDomain)
            ^ SplitMix64.Mix((ulong)players ^ BoardPlayersDomain);
        ulong s0 = SplitMix64.Next(ref root);
        ulong s1 = SplitMix64.Next(ref root);
        ulong s2 = SplitMix64.Next(ref root);
        ulong s3 = SplitMix64.Next(ref root);
        return new RandomStream("map-board", s0, s1, s2, s3);
    }

    /// <summary>域分隔常量：棋盘档的地图种子一侧、序号一侧与人数一侧各一个。MUST NOT 改动——改了内置棋盘图就变。</summary>
    private const ulong BoardDomain = 0x6B0A_94D7_E1C3_52F8UL;
    private const ulong BoardAttemptDomain = 0xD2F4_8A61_3B7C_E905UL;
    private const ulong BoardPlayersDomain = 0x9E37_51C4_0AF8_2D6BUL;
}
