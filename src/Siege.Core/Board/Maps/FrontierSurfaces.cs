using Siege.Core.Determinism;

namespace Siege.Core.Board.Maps;

/// <summary>
/// 生成图的新地表投放（terrain-surfaces design D6）：在布局与校验全部完成之后，把平台之外、非桥、非信物的草地 / 土路格
/// 改写成荒漠 / 沼泽 / 岩台 / 浅滩。只改地表——可落子性、高度、障碍、桥、栅栏、出生区、信物格一概不动，
/// 因此同一种子下开 / 关新地表的两张图除被改写的地表外逐格相同。
/// </summary>
/// <remarks>
/// <para>随机源是由地图种子派生的<b>独立</b>子流（<see cref="MapRandom.ForSurfaces"/>），与布局的尝试子流互不消耗；开关为关时根本不派生。</para>
/// <para>全部遍历按坐标序；邻格只经 <see cref="Adjacency.Neighbors"/>（几何四邻的唯一实现）。</para>
/// <para>规格：terrain-surfaces · map-generation —— Requirement: 布局规则（新地表投放）</para>
/// </remarks>
internal static class FrontierSurfaces
{
    /// <summary>
    /// 投放次序：每一轮按此顺序各投一块，第 0 轮保证四种各至少一块。约束最紧的先投——浅滩只能挨着主河、岩台只能在 h ≤ 1，
    /// 若让荒漠 / 沼泽先占掉河岸，浅滩会无处可放（平台数 5 / 7 / 8 的种子里实测出现过）。
    /// </summary>
    private static readonly Surface[] Order = [Surface.Shallows, Surface.Crag, Surface.Desert, Surface.Marsh];

    /// <summary>每种地表至多几块。</summary>
    internal const int MaxBlocks = 3;

    /// <summary>一块的格数下限。</summary>
    internal const int MinBlockCells = 2;

    /// <summary>一块的格数上限。</summary>
    internal const int MaxBlockCells = 6;

    /// <summary>合计占平台外可落子格的下限（百分数）。</summary>
    internal const int MinPercent = 15;

    /// <summary>合计占平台外可落子格的上限（百分数）。</summary>
    internal const int MaxPercent = 25;

    /// <summary>一块找种子格失败时换种子格重试的次数。</summary>
    private const int SeedTries = 8;

    /// <summary>
    /// 在 <paramref name="map"/> 上投放新地表，按坐标序返回新投放的格及其地表；由生成器在灌成地图数据的那一处改写地形。
    /// <paramref name="river"/> 按 <c>[x, y]</c> 标出主河的深水格（含桥格；浅滩块的种子格必须挨着它）。
    /// 候选不足以让四种各投一块、或合计达不到下限时抛 <see cref="MapGenerationException"/>——MUST NOT 静默少投。
    /// </summary>
    /// <remarks>
    /// 工作态只用数组与列表（与 <c>FrontierMapLayout</c> 同一条源码扫描守门）：候选按坐标序排好一次，此后种子格、边沿格都从它按序筛出，
    /// 不遍历任何散列容器。
    /// </remarks>
    internal static List<(Coord Cell, Surface Surface)> Place(MapData map, bool[,] river, RandomStream rng)
    {
        Coord[] candidates =
        [
            .. map.AllCoords()
                .Where(c => map.IsPlayable(c)
                    && map.BirthZoneOf(c) is null
                    && !map.HasBridge(c)
                    && !map.RelicCells.ContainsKey(c)
                    && map.SurfaceAt(c) is Surface.Grass or Surface.Road)
                .Order(),
        ];
        int outside = map.AllCoords().Count(c => map.IsPlayable(c) && map.BirthZoneOf(c) is null);
        int min = ((outside * MinPercent) + 99) / 100;
        int max = outside * MaxPercent / 100;

        // 目标的下界还要容得下"四种各一块最小块"，否则第 0 轮的余量不够、会有种类整块落空。
        int floor = Math.Max(min + 1, Order.Length * MinBlockCells);
        if (max < floor)
        {
            throw new MapGenerationException($"地图 {map.Id} 平台外可落子格只有 {outside} 个，容不下新地表投放。");
        }

        // 目标取在 [floor, max]：最后一块可能因剩余不足 2 格而放弃，至多少 1 格，仍不低于下限。
        int target = floor + rng.NextInt(max - floor + 1);
        var free = new bool[map.Width, map.Height];
        foreach (Coord c in candidates)
        {
            free[c.X, c.Y] = true;
        }

        var placed = new Surface?[map.Width, map.Height];
        var painted = new List<(Coord Cell, Surface Surface)>();

        for (int round = 0; round < MaxBlocks; round++)
        {
            for (int k = 0; k < Order.Length; k++)
            {
                Surface kind = Order[k];

                // 第 0 轮给后面还没投的种类各留出一块最小块的余量，保证四种各至少一块。
                int reserve = round == 0 ? (Order.Length - 1 - k) * MinBlockCells : 0;
                int remaining = target - painted.Count - reserve;
                if (remaining < MinBlockCells)
                {
                    // 第 0 轮有余量保证，走到这里说明目标算错了：MUST NOT 静默让某种地表落空。
                    if (round == 0)
                    {
                        throw new MapGenerationException($"地图 {map.Id} 投放目标 {target} 容不下四种各一块。");
                    }

                    break;
                }

                int size = Math.Min(MinBlockCells + rng.NextInt(MaxBlockCells - MinBlockCells + 1), remaining);
                List<Coord>? block = TryGrow(map, kind, size, candidates, free, placed, river, rng);
                if (block is null)
                {
                    if (round == 0)
                    {
                        throw new MapGenerationException($"地图 {map.Id} 找不到放{kind.DisplayName()}的位置：候选格不足。");
                    }

                    continue;
                }

                foreach (Coord c in block)
                {
                    free[c.X, c.Y] = false;
                    placed[c.X, c.Y] = kind;
                    painted.Add((c, kind));
                }
            }
        }

        if (painted.Count < min)
        {
            throw new MapGenerationException($"地图 {map.Id} 新地表只投放了 {painted.Count} 格，低于下限 {min}。");
        }

        painted.Sort((a, b) => a.Cell.CompareTo(b.Cell));
        return painted;
    }

    /// <summary>
    /// 长一块：先按坐标序列出合格的种子格、随机挑一个，再从边沿格（按坐标序）里随机挑着生长到 <paramref name="size"/> 格。
    /// 块内每一格都不得与已投放的<b>同种</b>地表几何相邻（否则两块会连成一块，块数与块大小的约束就失效）。
    /// 长不到 <see cref="MinBlockCells"/> 格就换种子格重来，重试用尽返回 <c>null</c>。
    /// </summary>
    private static List<Coord>? TryGrow(
        MapData map, Surface kind, int size, Coord[] candidates, bool[,] free, Surface?[,] placed, bool[,] river, RandomStream rng)
    {
        IEnumerable<Coord> Around(Coord c) => Adjacency.Neighbors(map.Width, map.Height, c);

        bool Fits(Coord c) =>
            free[c.X, c.Y]
            && (kind != Surface.Crag || map.HeightAt(c) <= 1)
            && !Around(c).Any(n => placed[n.X, n.Y] == kind);

        // candidates 已按坐标序排好，从它筛出的序列同样按坐标序——随机挑选只依赖这个次序。
        Coord[] seeds =
        [
            .. candidates.Where(c => Fits(c) && (kind != Surface.Shallows || Around(c).Any(n => river[n.X, n.Y]))),
        ];

        for (int attempt = 0; attempt < SeedTries && seeds.Length > 0; attempt++)
        {
            Coord first = seeds[rng.NextInt(seeds.Length)];
            var block = new List<Coord> { first };
            var inBlock = new bool[map.Width, map.Height];
            inBlock[first.X, first.Y] = true;
            while (block.Count < size)
            {
                Coord[] frontier =
                [
                    .. candidates.Where(c => !inBlock[c.X, c.Y] && Fits(c) && Around(c).Any(n => inBlock[n.X, n.Y])),
                ];
                if (frontier.Length == 0)
                {
                    break;
                }

                Coord next = frontier[rng.NextInt(frontier.Length)];
                block.Add(next);
                inBlock[next.X, next.Y] = true;
            }

            if (block.Count >= MinBlockCells)
            {
                block.Sort();
                return block;
            }
        }

        return null;
    }
}
