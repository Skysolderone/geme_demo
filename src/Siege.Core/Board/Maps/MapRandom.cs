using Siege.Core.Determinism;

namespace Siege.Core.Board.Maps;

/// <summary>
/// 地图随机源（map-generator D2）：一条只由地图种子决定的整数随机序列。
/// 与对局随机共用同一份 PRNG 实现（SplitMix64 展开 + <see cref="RandomStream"/> 的 xoshiro256**），但<b>不是</b>对局种子的子流——
/// 生成器拿不到对局种子，对局流程拿不到地图种子。
/// </summary>
/// <remarks>
/// 规格：openspec/changes/map-generator/specs/map-generation —— Requirement: 地图种子与确定性。
/// retire-legacy-maps（L10）：只为旧生成器存在的三个随机源（边疆档尝试 <c>map-gen</c>、新地表投放 <c>map-gen-surfaces</c>、棋盘档尝试 <c>map-board</c>）
/// 及其域常量随旧生成器删除；链接档布局随机源 <see cref="ForLinkLayout"/> 一字未改（取值由三条链接图基线与 link:1–20 快照守住）。
/// </remarks>
internal static class MapRandom
{
    /// <summary>
    /// 链接档初始布局（board-link-mode D2 拼接版）的随机源：只由地图种子决定（一张图只布局一次、由构造保证合规，没有尝试序号），
    /// 用独立的域常量，与已删除的旧生成器随机源的任何取值都不同构。
    /// 拼接本身不消费随机（裁决 J1），因此没有"链接对"的随机源。
    /// </summary>
    internal static RandomStream ForLinkLayout(ulong mapSeed)
    {
        ulong root = SplitMix64.Mix(mapSeed ^ LinkDomain) ^ SplitMix64.Mix(LinkLayoutDomain);
        ulong s0 = SplitMix64.Next(ref root);
        ulong s1 = SplitMix64.Next(ref root);
        ulong s2 = SplitMix64.Next(ref root);
        ulong s3 = SplitMix64.Next(ref root);
        return new RandomStream("map-link", s0, s1, s2, s3);
    }

    /// <summary>域分隔常量：链接档的地图种子一侧与布局一侧各一个。</summary>
    private const ulong LinkDomain = 0x17E3_B9A4_6C2D_F581UL;
    private const ulong LinkLayoutDomain = 0x8C4F_2A97_D5E6_031BUL;
}
