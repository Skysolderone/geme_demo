using System.Collections.Concurrent;
using System.Text;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests;

/// <summary>生成图测试的共用设施：按（种子, 平台数）缓存生成结果（同一张图被多条测试断言，不必重复生成），以及失败信息 / 布局速览用的文本图。</summary>
internal static class MapGenFixtures
{
    private static readonly ConcurrentDictionary<(ulong Seed, int Platforms), GeneratedMap> Cache = new();

    /// <summary>种子 1–50 × 平台数 5–8：规格「生成图通过校验」的样本口径。</summary>
    public static IEnumerable<(ulong Seed, int Platforms)> Sample()
    {
        for (int platforms = MapGenParameters.MinPlatforms; platforms <= MapGenParameters.MaxPlatforms; platforms++)
        {
            for (ulong seed = 1; seed <= 50; seed++)
            {
                yield return (seed, platforms);
            }
        }
    }

    public static GeneratedMap Generated(ulong seed, int platforms = MapGenParameters.DefaultPlatforms) =>
        Cache.GetOrAdd((seed, platforms), key =>
            FrontierMapGenerator.GenerateDetailed(key.Seed, new MapGenParameters { PlatformCount = key.Platforms }));

    /// <summary>从出生区的格子反推外接方块（不读生成器给的方块）：布局断言以地图数据为准。</summary>
    public static (int X0, int Y0, int X1, int Y1) BoundingBox(IEnumerable<Coord> zone)
    {
        Coord[] cells = [.. zone];
        return (cells.Min(c => c.X), cells.Min(c => c.Y), cells.Max(c => c.X), cells.Max(c => c.Y));
    }

    /// <summary>各座桥的格数：桥格按几何相邻连成的块，一块算一座。按块内最小坐标的坐标序。</summary>
    public static List<int> BridgeSpans(MapData map)
    {
        var spans = new List<int>();
        var seen = new HashSet<Coord>();
        foreach (Coord start in map.TerrainData.Bridges.Order())
        {
            if (!seen.Add(start))
            {
                continue;
            }

            int size = 0;
            var queue = new Queue<Coord>([start]);
            while (queue.Count > 0)
            {
                size++;
                foreach (Coord n in Adjacency.Neighbors(map.Width, map.Height, queue.Dequeue()))
                {
                    if (map.HasBridge(n) && seen.Add(n))
                    {
                        queue.Enqueue(n);
                    }
                }
            }

            spans.Add(size);
        }

        return spans;
    }

    /// <summary>过渡带格：可落子、不在任何平台内（h ≤ 1：走廊、广场、林地、桥、缓坡）。</summary>
    public static bool IsLowGround(MapData map, Coord c) => map.IsPlayable(c) && map.BirthZoneOf(c) is null;

    /// <summary>
    /// 几何口径的"窄处"：不属于任何一个"四格全是过渡带格"的 2×2 方块的过渡带格（按坐标序）。走廊全长 ≥ 2 格宽 ⇔ 此表为空。
    /// </summary>
    public static List<Coord> NarrowCells(MapData map)
    {
        bool Low(int x, int y) => x >= 0 && y >= 0 && x < map.Width && y < map.Height && IsLowGround(map, new Coord(x, y));

        var narrow = new List<Coord>();
        foreach (Coord c in map.AllCoords().Where(c => IsLowGround(map, c)).Order())
        {
            bool wide = false;
            for (int dx = -1; dx <= 0 && !wide; dx++)
            {
                for (int dy = -1; dy <= 0 && !wide; dy++)
                {
                    wide = Low(c.X + dx, c.Y + dy) && Low(c.X + dx + 1, c.Y + dy) && Low(c.X + dx, c.Y + dy + 1) && Low(c.X + dx + 1, c.Y + dy + 1);
                }
            }

            if (!wide)
            {
                narrow.Add(c);
            }
        }

        return narrow;
    }

    /// <summary>
    /// 功能口径的"一子堵死"：拿掉它（等于落一枚子）之后，某个平台与中央入口之间沿气边（含栅栏、崖壁的阻隔）不再连通的过渡带格（按坐标序）。
    /// 中央入口自身被拿掉时，改为要求全部平台仍两两连通。
    /// </summary>
    public static List<Coord> ChokingCells(MapData map)
    {
        var cuts = new List<Coord>();
        foreach (Coord removed in map.AllCoords().Where(c => IsLowGround(map, c)).Order())
        {
            Coord start = removed == map.CentralEntrance ? map.BirthZones[0].Order().First() : map.CentralEntrance;
            var seen = new HashSet<Coord> { start };
            var queue = new Queue<Coord>([start]);
            while (queue.Count > 0)
            {
                foreach (Coord n in Adjacency.LibertyNeighbors(map, queue.Dequeue()))
                {
                    if (n != removed && seen.Add(n))
                    {
                        queue.Enqueue(n);
                    }
                }
            }

            if (!map.BirthZones.All(zone => zone.Any(seen.Contains)))
            {
                cuts.Add(removed);
            }
        }

        return cuts;
    }

    /// <summary>
    /// 文本图，图例与手工边疆图的字符画一致：数字 = 平台格（编号）、<c>T</c> 营帐、<c>r</c> 平台内信物、<c>,</c> 缓坡、<c>.</c> 走廊 / 广场、<c>F</c> 林地、
    /// <c>C</c> 篝火、<c>S</c> 石碑、<c>o</c> 标准档公共信物、<c>R</c> 高档公共信物（中央入口）、<c>~</c> 深水、<c>=</c> 桥、<c>#</c> 岩石。上北下南，行号在右。
    /// </summary>
    public static string TextArt(MapData map)
    {
        var text = new StringBuilder();
        for (int y = map.Height - 1; y >= 0; y--)
        {
            for (int x = 0; x < map.Width; x++)
            {
                text.Append(Glyph(map, new Coord(x, y)));
            }

            text.Append("  ").Append(y + 1).Append('\n');
        }

        text.Append(Coord.ColumnLetters[..map.Width]).Append('\n');
        return text.ToString();
    }

    private static char Glyph(MapData map, Coord c)
    {
        if (map.Obstacles.Contains(c))
        {
            return '#';
        }

        if (map.HasBridge(c))
        {
            return '=';
        }

        if (map.SurfaceAt(c) == Surface.DeepWater)
        {
            return '~';
        }

        if (map.RelicCells.TryGetValue(c, out RelicCellSpec spec))
        {
            return spec.Zone == RelicZone.BirthZone ? 'r' : spec.Budget == BudgetTier.High ? 'R' : 'o';
        }

        if (map.BirthZoneOf(c) is { } zone)
        {
            return (char)('1' + zone);
        }

        if (map.SurfaceAt(c) == Surface.Forest)
        {
            return 'F';
        }

        return map.HeightAt(c) == 1 ? ',' : '.';
    }
}
