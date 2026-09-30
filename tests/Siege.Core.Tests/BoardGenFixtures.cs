using System.Collections.Concurrent;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests;

/// <summary>
/// 棋盘档生成图测试的共用设施（board-map 段 B）：按（种子, 棋盘数）缓存生成结果，以及<b>不读生成器内部状态</b>、只从地图数据反推的通道分组——
/// 布局断言以地图数据为准，不与生成器共用一份实现。
/// </summary>
internal static class BoardGenFixtures
{
    private static readonly ConcurrentDictionary<(ulong Seed, int Boards), GeneratedBoardMap> Cache = new();

    /// <summary>种子 1–20 × 棋盘数 7–10：规格「生成图通过校验」「规模落在目标带」的样本口径，共 80 张。</summary>
    public static IEnumerable<(ulong Seed, int Boards)> Sample()
    {
        for (int boards = BoardMapParameters.MinBoards; boards <= BoardMapParameters.MaxBoards; boards++)
        {
            for (ulong seed = 1; seed <= 20; seed++)
            {
                yield return (seed, boards);
            }
        }
    }

    public static GeneratedBoardMap Generated(ulong seed, int boards = BoardMapParameters.DefaultBoards) =>
        Cache.GetOrAdd((seed, boards), key =>
            BoardMapGenerator.GenerateDetailed(key.Seed, new BoardMapParameters { BoardCount = key.Boards }));

    /// <summary>该格所在棋盘在清单里的下标；不在任何棋盘内为 −1。</summary>
    public static int BoardOf(MapData map, Coord c)
    {
        for (int i = 0; i < map.Boards.Length; i++)
        {
            if (map.Boards[i].Contains(c))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>一条通道：格子（坐标序）与它沿四邻贴着的棋盘下标（升序去重）。</summary>
    public sealed record Corridor(Coord[] Cells, int[] Boards)
    {
        public int Columns => Cells.Max(c => c.X) - Cells.Min(c => c.X) + 1;

        public int Rows => Cells.Max(c => c.Y) - Cells.Min(c => c.Y) + 1;
    }

    /// <summary>不属于任何棋盘的可落子格，按几何四邻分组；按组内最小坐标的坐标序。</summary>
    public static List<Corridor> Corridors(MapData map)
    {
        var corridors = new List<Corridor>();
        var seen = new HashSet<Coord>();
        foreach (Coord start in map.AllCoords().Where(c => map.IsPlayable(c) && BoardOf(map, c) < 0))
        {
            if (!seen.Add(start))
            {
                continue;
            }

            var cells = new List<Coord>();
            var touched = new SortedSet<int>();
            var queue = new Queue<Coord>([start]);
            while (queue.Count > 0)
            {
                Coord current = queue.Dequeue();
                cells.Add(current);
                foreach (Coord n in Adjacency.Neighbors(map.Width, map.Height, current))
                {
                    int board = BoardOf(map, n);
                    if (board >= 0)
                    {
                        touched.Add(board);
                    }
                    else if (map.IsPlayable(n) && seen.Add(n))
                    {
                        queue.Enqueue(n);
                    }
                }
            }

            corridors.Add(new Corridor([.. cells.Order()], [.. touched]));
        }

        return corridors;
    }

    /// <summary>文本图：数字 = 出生棋盘格（编号）、<c>+</c> 公共棋盘格、<c>:</c> 通道、<c>r</c> / <c>o</c> / <c>R</c> 信物（出生 / 标准 / 高档）、<c>#</c> 障碍。上北下南。</summary>
    public static string TextArt(MapData map)
    {
        var text = new System.Text.StringBuilder();
        for (int y = map.Height - 1; y >= 0; y--)
        {
            for (int x = 0; x < map.Width; x++)
            {
                var c = new Coord(x, y);
                char glyph = !map.IsPlayable(c) ? '#'
                    : map.RelicCells.TryGetValue(c, out RelicCellSpec spec) ? (spec.Zone == RelicZone.BirthZone ? 'r' : spec.Budget == BudgetTier.High ? 'R' : 'o')
                    : map.BirthZoneOf(c) is { } zone ? (char)('1' + zone)
                    : BoardOf(map, c) >= 0 ? '+' : ':';
                text.Append(glyph);
            }

            text.Append("  ").Append(y + 1).Append('\n');
        }

        return text.ToString();
    }
}
