using System.Collections.Immutable;
using Siege.Core.Determinism;
using Siege.Core.PieceRelics;

namespace Siege.Core.Board.Maps;

/// <summary>
/// 信物遗址落位（relic-sites 裁决 6 / 7）：大图生成末尾、成品校验之前，用独立子流 <see cref="MapRandom.ForLinkRelicSites"/> 抽稀有 ⌈n/2⌉ + 普通 n+2 处。
/// 小图不进这里（不派生、不消费），既有三条流的消费一步不动。
/// </summary>
public static partial class LinkMapGenerator
{
    /// <summary>
    /// 消费顺序（改它即换遗址清单，须重钉大图测试的期望）：候选 = 全图满足 <see cref="RelicSites.IsSiteGround"/> 的格，先行后列；先抽稀有 ⌈n/2⌉ 处、再抽普通 n+2 处，
    /// 每处一次 <c>NextInt(剩余候选数)</c>，抽中后把与它切比雪夫距离 &lt; <see cref="RelicSites.UncalibratedSiteGap"/> 的候选全部移出。候选在抽完之前耗尽即生成失败
    /// （<see cref="MapGenerationException"/>，与成品校验不过同一条失败路径；150×150 上候选上万、每处至多移出 39×39，实际不会发生）。
    /// 返回清单按抽取次序（稀有在前）。
    /// </summary>
    private static ImmutableArray<RelicSite> PlaceRelicSites(MapData map, int players, RandomStream sites)
    {
        int rare = RelicSites.RareCount(players);
        int total = rare + RelicSites.CommonCount(players);
        List<Coord> pool = [.. map.AllCoords().Where(c => RelicSites.IsSiteGround(map, c))];
        ImmutableArray<RelicSite>.Builder placed = ImmutableArray.CreateBuilder<RelicSite>(total);
        for (int k = 0; k < total; k++)
        {
            if (pool.Count == 0)
            {
                throw new MapGenerationException($"地图 {map.Id} 放了 {k} 处信物遗址后没有候选格可放第 {k + 1} 处（共需 {total} 处，两两相距 ≥ {RelicSites.UncalibratedSiteGap}）。");
            }

            Coord cell = pool[sites.NextInt(pool.Count)];
            placed.Add(new RelicSite(cell, k < rare ? RelicTier.Rare : RelicTier.Common));
            pool.RemoveAll(c => RelicSites.Distance(c, cell) < RelicSites.UncalibratedSiteGap);
        }

        return placed.MoveToImmutable();
    }
}
