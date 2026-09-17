using System.Collections.Immutable;

namespace Siege.Core.Board;

/// <summary>
/// 据点的"主人出生区"：<b>只供遥测与平衡分析</b>（match-telemetry 平衡分析方向 10），不参与任何规则判定——据点控制不看主人。
/// 分析端只读日志、不重建地图（design.md D6），所以由跑局层在对局开始时按地图算好写进日志首部。
/// </summary>
/// <remarks>
/// <para>"区域"<c>R(z)</c> = 从出生区 z 的格出发、沿气边（<see cref="Adjacency.LibertyNeighbors"/>）可达、且<b>不经过桥格</b>的全部可落子格；
/// 即出生区本身 + 缓坡 + 河外低地。三档的主人：</para>
/// <list type="bullet">
/// <item><b>营帐</b>：据点格所在的出生区（<see cref="MapData.BirthZoneOf"/>）。</item>
/// <item><b>篝火</b>：包含据点格的唯一区域 <c>R(z)</c> 的 z（篝火所在河外低地的主人）。</item>
/// <item><b>石碑</b>（"相邻桥头那家"）：与石碑几何相邻的公共信物格（桥头信物，不属于任何出生区）→ 与该信物格有气边的桥格 →
/// 该桥格的气边邻居中落在某个 <c>R(z)</c> 里的那一侧 → z。即"桥头信物所在河外低地的主人"。</item>
/// </list>
/// <para>任何一步候选不唯一或不存在时，该据点的主人为 <c>null</c>（分析端对其跳过主人口径），不猜。v4 上四块石碑与四个篝火都能唯一推出。</para>
/// <para>几何相邻用 <see cref="Adjacency.AreAdjacent"/>，不直接枚举几何邻居（boundaries.md 守门名单）。</para>
/// </remarks>
public static class SiteAttribution
{
    /// <summary>地图上每个据点的主人出生区编号（0 起，与 <see cref="MapData.BirthZones"/> 下标一致）；推不出唯一主人为 <c>null</c>。</summary>
    public static ImmutableSortedDictionary<Coord, int?> HomeZones(MapData map)
    {
        ArgumentNullException.ThrowIfNull(map);
        HashSet<Coord>[] regions = [.. map.BirthZones.Select(zone => ReachableWithoutBridges(map, zone))];

        ImmutableSortedDictionary<Coord, int?>.Builder result = ImmutableSortedDictionary.CreateBuilder<Coord, int?>();
        foreach ((Coord site, SiteTier tier) in map.Sites)
        {
            result[site] = tier switch
            {
                SiteTier.Tent => map.BirthZoneOf(site),
                SiteTier.Campfire => UniqueRegionOf(regions, site),
                SiteTier.Stele => BridgeheadOwner(map, regions, site),
                _ => null,
            };
        }

        return result.ToImmutable();
    }

    private static int? BridgeheadOwner(MapData map, HashSet<Coord>[] regions, Coord stele)
    {
        var owners = new HashSet<int>();
        foreach (Coord relic in map.RelicCells.Keys.Where(r => map.BirthZoneOf(r) is null && Adjacency.AreAdjacent(r, stele)))
        {
            foreach (Coord bridge in Adjacency.LibertyNeighbors(map, relic).Where(map.HasBridge))
            {
                foreach (Coord shore in Adjacency.LibertyNeighbors(map, bridge).Where(c => !map.HasBridge(c)))
                {
                    if (UniqueRegionOf(regions, shore) is int zone)
                    {
                        owners.Add(zone);
                    }
                }
            }
        }

        return owners.Count == 1 ? owners.Single() : null;
    }

    private static int? UniqueRegionOf(HashSet<Coord>[] regions, Coord c)
    {
        int[] zones = [.. Enumerable.Range(0, regions.Length).Where(z => regions[z].Contains(c))];
        return zones.Length == 1 ? zones[0] : null;
    }

    private static HashSet<Coord> ReachableWithoutBridges(MapData map, IEnumerable<Coord> sources)
    {
        var seen = new HashSet<Coord>();
        var queue = new Queue<Coord>();
        foreach (Coord s in sources.Where(c => map.IsPlayable(c) && !map.HasBridge(c)))
        {
            if (seen.Add(s))
            {
                queue.Enqueue(s);
            }
        }

        while (queue.Count > 0)
        {
            Coord c = queue.Dequeue();
            foreach (Coord n in Adjacency.LibertyNeighbors(map, c))
            {
                if (!map.HasBridge(n) && seen.Add(n))
                {
                    queue.Enqueue(n);
                }
            }
        }

        return seen;
    }
}
