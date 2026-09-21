using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Tests;

/// <summary>
/// 测试用边疆档地图：在测试内用代码构造，不依赖内置的 <c>siege-frontier-v2</c>。
/// </summary>
internal static class FrontierFixtures
{
    /// <summary>中央入口 <c>L10</c>。</summary>
    internal static readonly Coord Entrance = new(10, 9);

    /// <summary>
    /// 一张<b>合法</b>的边疆档小图，数字取自规格算例「边疆档按自己的区间校验」：可落子 360、出生区（平台）6、信物 16。
    /// 20×20 全平地草地，最上两行（第 19、20 行）整行岩石 → 400 − 40 = 360；单连通、无口袋。
    /// 6 个 5×5 平台<b>故意不对称</b>摆放（1.4：边疆档不要求旋转对称），到中央入口的气边距离依次为
    /// 11 / 10 / 10 / 7 / 4 / 2（平地无障碍 → 平台最近格到 <c>L10</c> 的曼哈顿距离，规格算例「平台 A 为 4、平台 B 为 11」即 5 号与 1 号）。
    /// 信物：每平台 1 个出生区信物 + 10 个公共信物（入口 <c>L10</c> 为高档）。
    /// </summary>
    internal static MapData Map() =>
        new()
        {
            Id = "test-frontier-6",
            Width = 20,
            Height = 20,
            MaxPlayers = 4,
            Profile = MapProfile.Frontier,
            Obstacles = [.. Rect(0, 18, 20, 2)],
            BirthZones =
            [
                [.. Rect(0, 0, 5, 5)],
                [.. Rect(15, 0, 5, 5)],
                [.. Rect(0, 13, 5, 5)],
                [.. Rect(14, 12, 5, 5)],
                [.. Rect(7, 1, 5, 5)],
                [.. Rect(8, 11, 5, 5)],
            ],
            RelicCells = new (int X, int Y, RelicZone Zone, BudgetTier Budget)[]
            {
                (1, 1, RelicZone.BirthZone, BudgetTier.Birth),
                (18, 1, RelicZone.BirthZone, BudgetTier.Birth),
                (1, 16, RelicZone.BirthZone, BudgetTier.Birth),
                (17, 15, RelicZone.BirthZone, BudgetTier.Birth),
                (8, 2, RelicZone.BirthZone, BudgetTier.Birth),
                (11, 14, RelicZone.BirthZone, BudgetTier.Birth),
                (10, 9, RelicZone.Contested, BudgetTier.High),
                (6, 6, RelicZone.Contested, BudgetTier.Standard),
                (13, 10, RelicZone.Contested, BudgetTier.Standard),
                (5, 12, RelicZone.Contested, BudgetTier.Standard),
                (16, 7, RelicZone.Contested, BudgetTier.Standard),
                (12, 7, RelicZone.Contested, BudgetTier.Standard),
                (7, 10, RelicZone.Contested, BudgetTier.Standard),
                (3, 8, RelicZone.Contested, BudgetTier.Standard),
                (18, 10, RelicZone.Contested, BudgetTier.Standard),
                (6, 14, RelicZone.Contested, BudgetTier.Standard),
            }.ToImmutableDictionary(r => new Coord(r.X, r.Y), r => new RelicCellSpec(r.Zone, r.Budget)),
            ChokePoints = [new Coord(7, 9), new Coord(13, 9)],
            CentralEntrance = Entrance,
        };

    /// <summary>左下角 (<paramref name="x0"/>, <paramref name="y0"/>)、宽 <paramref name="w"/> 高 <paramref name="h"/> 的矩形格集。</summary>
    internal static IEnumerable<Coord> Rect(int x0, int y0, int w, int h) =>
        from y in Enumerable.Range(y0, h)
        from x in Enumerable.Range(x0, w)
        select new Coord(x, y);

    /// <summary>不在任何出生区、不是信物、可落子的空闲格（先行后列），供测试增补信物 / 障碍。</summary>
    internal static Coord[] FreeCells(MapData map) =>
        [.. map.AllCoords().Where(c =>
            map.IsPlayable(c) && map.BirthZoneOf(c) is null && !map.RelicCells.ContainsKey(c)
            && c != map.CentralEntrance && !map.ChokePoints.Contains(c))];

    /// <summary>仓库根目录（含 <c>siege.sln</c>）。</summary>
    internal static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "siege.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("找不到仓库根目录（siege.sln）。");
    }
}
