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
/// 及其域常量随旧生成器删除。link-map-surfaces D1：另加新地表叠加随机源 <see cref="ForLinkSurfaces"/>（独立域常量）。
/// map-expand-25 D8：两条流的派生纳入人数（标识 <c>link:&lt;种子&gt;:&lt;人数&gt;</c>），取值因此与旧的单种子派生全部不同。
/// big-map D1：大图（<see cref="MapTier.Large"/>）在 (种子, 人数) 一侧再混入档位常量 <see cref="TierDomain"/>；小图<b>不混</b>，三条子流的取值与引入档位之前一个比特都不变。
/// </remarks>
internal static class MapRandom
{
    /// <summary>
    /// 链接档初始布局（map-expand-25 D2–D4：平台落位、主题地形、坡道、河道、岛缘；D5 / 段 A3 起不放静态信物格，不为信物消费随机）的随机源：由地图种子与人数共同决定（D8：同种子不同人数是不同的图），
    /// 用独立的域常量，与已删除的旧生成器随机源的任何取值都不同构。
    /// </summary>
    internal static RandomStream ForLinkLayout(ulong mapSeed, int players) => ForLinkLayout(mapSeed, players, MapTier.Small);

    /// <summary>同上，按档位派生（big-map D1：大图混入 <see cref="TierDomain"/>，小图与两参重载相同）。</summary>
    internal static RandomStream ForLinkLayout(ulong mapSeed, int players, MapTier tier) => Create("map-link", mapSeed, players, tier, LinkLayoutDomain);

    /// <summary>
    /// 链接档新地表叠加（link-map-surfaces D1 / 裁决 E4）的随机源：由地图种子与人数共同决定，与 <see cref="ForLinkLayout"/> 同一 (种子, 人数) 一侧、不同的域常量，
    /// 两条流互不扰动——叠加消费多少次都不改变布局流的任何取值。
    /// </summary>
    internal static RandomStream ForLinkSurfaces(ulong mapSeed, int players) => ForLinkSurfaces(mapSeed, players, MapTier.Small);

    /// <summary>同上，按档位派生。</summary>
    internal static RandomStream ForLinkSurfaces(ulong mapSeed, int players, MapTier tier) => Create("map-link-surfaces", mapSeed, players, tier, LinkSurfacesDomain);

    /// <summary>
    /// 链接档岸线与内陆要素（map-coastline 裁决 1–3：切角、海湾、河道加宽、湖泊 / 岩区）的随机源：同一 (种子, 人数) 一侧、第三个域常量。
    /// 单独一条流是为了让布局流的消费顺序（平台落位 → 主题 → 平台内部 → 坡道 → 河道 → 岛缘）一步不动——同种子的平台清单与平台内地形因此与引入岸线之前逐格相同。
    /// </summary>
    internal static RandomStream ForLinkCoast(ulong mapSeed, int players) => ForLinkCoast(mapSeed, players, MapTier.Small);

    /// <summary>同上，按档位派生。</summary>
    internal static RandomStream ForLinkCoast(ulong mapSeed, int players, MapTier tier) => Create("map-link-coast", mapSeed, players, tier, LinkCoastDomain);

    /// <summary>
    /// 信物遗址落位（relic-sites 裁决 6）的随机源：同一 (种子, 人数, 档位) 一侧、第四个域常量。只在大图生成末尾派生并消费（小图不派生、不消费），
    /// 布局 / 岸线 / 新地表三条流的消费顺序与取值一步不动——大图同种子的地形与引入遗址之前逐格相同，只多出遗址清单。
    /// </summary>
    internal static RandomStream ForLinkRelicSites(ulong mapSeed, int players, MapTier tier) => Create("map-link-relic-sites", mapSeed, players, tier, LinkRelicSitesDomain);

    /// <summary>
    /// (种子, 人数) 一侧先混合，再与该流的域常量异或；人数经一次独立混合进入，与种子的任何取值都不在同一位置上相消。
    /// big-map D1：只有大图在 (种子, 人数) 一侧之后再混一次档位常量——小图的 <c>side</c> 与引入档位之前逐位相同。
    /// </summary>
    private static RandomStream Create(string name, ulong mapSeed, int players, MapTier tier, ulong domain)
    {
        ulong side = SplitMix64.Mix(SplitMix64.Mix(mapSeed ^ LinkDomain) ^ SplitMix64.Mix((ulong)players ^ PlayersDomain));
        if (tier == MapTier.Large)
        {
            side = SplitMix64.Mix(side ^ SplitMix64.Mix(TierDomain));
        }

        ulong root = side ^ SplitMix64.Mix(domain);
        ulong s0 = SplitMix64.Next(ref root);
        ulong s1 = SplitMix64.Next(ref root);
        ulong s2 = SplitMix64.Next(ref root);
        ulong s3 = SplitMix64.Next(ref root);
        return new RandomStream(name, s0, s1, s2, s3);
    }

    /// <summary>域分隔常量：链接档的地图种子一侧一个，布局一侧、新地表叠加一侧、岸线一侧（map-coastline）与信物遗址一侧（relic-sites）各一个。</summary>
    private const ulong LinkDomain = 0x17E3_B9A4_6C2D_F581UL;
    private const ulong LinkLayoutDomain = 0x8C4F_2A97_D5E6_031BUL;
    private const ulong LinkSurfacesDomain = 0x3B6D_E1F0_92A7_C458UL;
    private const ulong LinkCoastDomain = 0x6D91_4C2E_A7B3_58F0UL;
    private const ulong LinkRelicSitesDomain = 0x4A27_9E58_C1B6_D3F2UL;
    private const ulong PlayersDomain = 0x5A1C_7E93_B04D_26F7UL;

    /// <summary>大图档位常量（big-map D1）：只在 <see cref="MapTier.Large"/> 混入，小图不读它。</summary>
    private const ulong TierDomain = 0x2F8A_6D13_C9E4_7B05UL;
}
