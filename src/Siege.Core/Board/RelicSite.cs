using System.Collections.Immutable;
using Siege.Core.Board.Maps;
using Siege.Core.PieceRelics;

namespace Siege.Core.Board;

/// <summary>
/// 信物遗址（relic-sites 裁决 6 / 7）：大图生成时固定下来的一格，带档位（稀有 / 普通）。大图上每批地图信物只落在空着的遗址格上（裁决 8，<c>PieceRelics.RelicSpawner</c>），
/// 旗心与任一遗址切比雪夫距离 ≥ <see cref="RelicSites.FlagClearance"/>（裁决 9，<c>Match.FlagRules</c>）。小图没有遗址。
/// </summary>
/// <param name="Cell">遗址格（可落子、高度 0 的低地，且八邻都是可落子格——不贴岸线、不贴障碍与深水；追加裁决 13）。</param>
/// <param name="Tier">档位：稀有遗址只出稀有件，普通遗址只出普通件。</param>
public readonly record struct RelicSite(Coord Cell, RelicTier Tier)
{
    public override string ToString() => $"{Cell.ToNotation()}:{Tier}";
}

/// <summary>
/// 稀有信物遗址区域（relic-zones 裁决 2）：画布按 <see cref="RelicSites.UncalibratedRareZoneSide"/> 边长从左下角起切成方块（贴画布边的块裁到画布内），稀有遗址落在哪块，那块就是它的区域。
/// 界面（新局缩略、对局小地图）只画区域、不画稀有遗址的精确格，让玩家进图自己找；3D 石碑仍在精确格上。区域只由遗址格与画布尺寸决定，不消费随机。
/// </summary>
/// <param name="Origin">区域左下角格。</param>
/// <param name="Width">横向格数。</param>
/// <param name="Height">纵向格数。</param>
public readonly record struct RareSiteZone(Coord Origin, int Width, int Height)
{
    /// <summary>该格在不在区域内。</summary>
    public bool Contains(Coord c) => c.X >= Origin.X && c.X < Origin.X + Width && c.Y >= Origin.Y && c.Y < Origin.Y + Height;

    /// <summary>该格到区域的切比雪夫距离（区域内为 0）：与 <see cref="RelicSites.Distance"/> 同一口径。</summary>
    public int DistanceTo(Coord c)
    {
        int dx = Math.Max(0, Math.Max(Origin.X - c.X, c.X - (Origin.X + Width - 1)));
        int dy = Math.Max(0, Math.Max(Origin.Y - c.Y, c.Y - (Origin.Y + Height - 1)));
        return Math.Max(dx, dy);
    }
}

/// <summary>
/// 信物遗址的数量、间距与位置口径（relic-sites 裁决 6 / 7 / 9）。名字带 <c>Uncalibrated</c> 的常量是<b>未校准的初值</b>，不是基线。
/// 生成器（<c>LinkMapGenerator</c>）、校验器（<see cref="MapValidator"/>）与插旗规则共用这一份，MUST NOT 各写一遍。
/// </summary>
public static class RelicSites
{
    /// <summary>稀有遗址数 = ⌈开局人数 / 本常量⌉，<b>未校准的初值 2</b>（裁决 7：4 人 2 处）。</summary>
    public const int UncalibratedRareDivisor = 2;

    /// <summary>普通遗址数 = 开局人数 + 本常量，<b>未校准的初值 2</b>（裁决 7：4 人 6 处）。</summary>
    public const int UncalibratedCommonOverPlayers = 2;

    /// <summary>任意两处遗址的最小切比雪夫距离，<b>未校准的初值 20</b>（裁决 6）。</summary>
    public const int UncalibratedSiteGap = 20;

    /// <summary>
    /// 旗心与任一遗址的最小切比雪夫距离（裁决 9）：= 出生平台半径 6 + 1 = 7，锁定时物化的 13×13 出生平台因此永远盖不住遗址。几何推出的规则值，不是校准量。
    /// </summary>
    public const int FlagClearance = ((LinkMapGenerator.LargeBirthPlateSide - 1) / 2) + 1;

    /// <summary>
    /// 稀有遗址区域的边长（relic-zones 裁决 2），<b>未校准的初值 20</b>。MUST NOT 大于 <see cref="UncalibratedSiteGap"/>：块内任意两格相距 ≤ 边长 − 1 &lt; 遗址间距，一块里因此至多一处稀有遗址。
    /// </summary>
    public const int UncalibratedRareZoneSide = 20;

    /// <summary>稀有遗址 <paramref name="site"/> 所在的区域：边长 <see cref="UncalibratedRareZoneSide"/> 的网格块，裁到 <paramref name="width"/> × <paramref name="height"/> 画布内。</summary>
    public static RareSiteZone RareZoneOf(Coord site, int width, int height)
    {
        int side = UncalibratedRareZoneSide;
        int x0 = site.X / side * side, y0 = site.Y / side * side;
        return new RareSiteZone(new Coord(x0, y0), Math.Min(side, width - x0), Math.Min(side, height - y0));
    }

    /// <summary>清单里全部稀有遗址的区域，按清单次序（普通遗址不进）。</summary>
    public static ImmutableArray<RareSiteZone> RareZones(ImmutableArray<RelicSite> sites, int width, int height) =>
        sites.IsDefaultOrEmpty ? [] : [.. sites.Where(s => s.Tier == RelicTier.Rare).Select(s => RareZoneOf(s.Cell, width, height))];

    /// <summary>地图上全部稀有遗址的区域（见 <see cref="RareZones(ImmutableArray{RelicSite}, int, int)"/>）。</summary>
    public static ImmutableArray<RareSiteZone> RareZones(MapData map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return RareZones(map.RelicSites, map.Width, map.Height);
    }

    /// <summary>人数为 <paramref name="players"/> 时的稀有遗址数 ⌈n / 2⌉。</summary>
    public static int RareCount(int players) => (players + UncalibratedRareDivisor - 1) / UncalibratedRareDivisor;

    /// <summary>人数为 <paramref name="players"/> 时的普通遗址数 n + 2。</summary>
    public static int CommonCount(int players) => players + UncalibratedCommonOverPlayers;

    /// <summary>切比雪夫距离（横纵相隔格数取大者）：遗址间距与遗址到旗心距离的口径。只做坐标算术，不经邻接枚举。</summary>
    public static int Distance(Coord a, Coord b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    /// <summary>
    /// 该格能否放遗址（裁决 6「只在可落子格、避开岸线与障碍」+ 追加裁决 13「只落低地」的唯一判定）：本格可落子且高度 0，且八邻都在画布内且可落子——
    /// 岛缘、岩石、湖泊、未架桥河道的旁边一圈都不放；平台顶（h ≥ 1）与坡道不放，八邻的高度不限。
    /// </summary>
    public static bool IsSiteGround(MapData map, Coord c)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (!map.IsPlayable(c) || map.HeightAt(c) != 0)
        {
            return false;
        }

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = c.X + dx, y = c.Y + dy;
                if (x < 0 || y < 0 || x >= map.Width || y >= map.Height || !map.IsPlayable(new Coord(x, y)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>清单里按档位的处数。</summary>
    public static int CountOf(ImmutableArray<RelicSite> sites, RelicTier tier) => sites.IsDefaultOrEmpty ? 0 : sites.Count(s => s.Tier == tier);
}
