using System.Collections.Concurrent;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests;

/// <summary>
/// 棋盘档生成图测试的共用设施（board-map 段 B、board-isolated-gen 段 A）：按（种子, 人数, 棋盘数）缓存生成结果，
/// 以及<b>不读生成器内部状态</b>、只从地图数据反推的查询——布局断言以地图数据为准，不与生成器共用一份实现。
/// </summary>
internal static class BoardGenFixtures
{
    private static readonly ConcurrentDictionary<(ulong Seed, int Players, int Boards), GeneratedBoardMap> Cache = new();

    /// <summary>各人数的合法棋盘数（规格「棋盘档生成参数」：4 人 7–10、3 人 5–8、2 人 4–5；测试内独立写出，不读参数类）。</summary>
    public static readonly (int Players, int[] Boards)[] Ranges =
    [
        (4, [7, 8, 9, 10]),
        (3, [5, 6, 7, 8]),
        (2, [4, 5]),
    ];

    /// <summary>（人数, 棋盘数）的全部合法组合：规格「生成图通过校验」「规模落在目标带」的参数口径，共 10 组。</summary>
    public static IEnumerable<(int Players, int Boards)> Configurations() =>
        Ranges.SelectMany(r => r.Boards.Select(n => (r.Players, n)));

    /// <summary>种子 1–20 × 全部合法（人数, 棋盘数）：共 200 张。</summary>
    public static IEnumerable<(ulong Seed, int Players, int Boards)> Sample() =>
        Configurations().SelectMany(c => Enumerable.Range(1, 20).Select(seed => ((ulong)seed, c.Players, c.Boards)));

    /// <summary>标识（测试内拼写，不经 <see cref="BoardMapId.Format"/>），只用于失败消息。</summary>
    public static string Label(ulong seed, int players, int boards) => $"board:{seed}:p{players}:n{boards}";

    public static GeneratedBoardMap Generated(ulong seed, int boards = 7, int players = 4) =>
        Cache.GetOrAdd((seed, players, boards), key =>
            BoardMapGenerator.GenerateDetailed(key.Seed, new BoardMapParameters { Players = key.Players, BoardCount = key.Boards }));

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

    /// <summary>文本图：数字 = 出生棋盘格（编号）、<c>+</c> 公共棋盘格、<c>:</c> 棋盘外的可落子格（合规图上不应出现）、<c>r</c> / <c>o</c> / <c>R</c> 信物（出生 / 标准 / 高档）、<c>#</c> 障碍。上北下南。</summary>
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
