using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Xunit.Abstractions;

namespace Siege.Core.Tests.LinkMapGeneration;

/// <summary>
/// 规格：任务 10-26-map-expand-25 design D2–D5（多平台浮空岛）/ prd 验收标准：岛尺寸、平台数与尺寸、平台高度与坡道、低地河道与桥、岛缘点缀、全图气边连通、不放静态信物格（段 A3）。
/// 种子 1–20 × 人数 2 / 3 / 4，全部判定在测试内独立算（岛范围、切角、坡道朝外格、气边连通都不读生成器的私有实现）。
/// </summary>
/// <remarks>
/// 变异验证（map-expand-25 段 A1 实跑，过滤 LinkMapGeneration 命名空间）：
/// M2 校验器 PlatesApart 间隔改 gap−1（生成器落位共用）→「平台数与尺寸」×3 红；M6 LibertySplit 恒空（生成器低地自检共用）→「全图可落子格沿气边连通」×3 红；
/// M10 每条河道只放 1 座桥 →「低地有N条河道各两座桥」×3 红。另 M3 MapRandom 派生去掉人数 → 链接档地图标识与确定性Tests「不同人数不同图」「新地表子流与布局子流互不扰动」红 2；
/// M4 LinkMapId.Parse 去掉旧两段格式分支 →「标识解析与规范化」「目录解析link标识」红 2。
/// </remarks>
public class 浮空岛布局Tests(ITestOutputHelper output)
{
    /// <summary>岛边长表（实施修订：design D2 原 26 / 31 / 36 放不下 R2 的平台数，改为 30 / 35 / 40，见 <c>LinkMapGenerator.IslandBaseSide</c>）。</summary>
    private static readonly IReadOnlyDictionary<int, int> Side = new Dictionary<int, int> { [2] = 30, [3] = 35, [4] = 40 };

    /// <summary>平台数区间（裁决 R2）。</summary>
    private static readonly IReadOnlyDictionary<int, (int Min, int Max)> PlateRange = new Dictionary<int, (int, int)> { [2] = (6, 8), [3] = (8, 10), [4] = (10, 12) };

    private const int M = 2;

    public static IEnumerable<object[]> Players() => [[2], [3], [4]];

    private static IEnumerable<(ulong Seed, MapData Map)> Maps(int players)
    {
        for (ulong seed = 1; seed <= 20; seed++)
        {
            yield return (seed, LinkMapGenerator.Generate(seed, players));
        }
    }

    /// <summary>岛上的格（测试内独立算式）：在 [M, M+W)² 内，且到最近两条边的距离之和 ≥ 3（四角各切去直角边 3 的三角）。</summary>
    private static bool OnIsland(Coord c, int w)
    {
        int dx = Math.Min(c.X - M, M + w - 1 - c.X), dy = Math.Min(c.Y - M, M + w - 1 - c.Y);
        return dx >= 0 && dy >= 0 && dx + dy >= 3;
    }

    /// <summary>岛缘一圈：岛上，且东南西北至少一个方向的下一格不在岛上（坐标算术）。</summary>
    private static bool OnIslandEdge(Coord c, int w) =>
        OnIsland(c, w) && new[] { (0, -1), (0, 1), (-1, 0), (1, 0) }.Any(d => !OnIsland(new Coord(c.X + d.Item1, c.Y + d.Item2), w));

    private static bool InPlate(MapData map, Coord c) => map.Boards.Any(p => p.Contains(c));

    /// <summary>平台外圈非角格朝外的格（坐标算术）；角格或不在外圈为 <c>null</c>。</summary>
    private static Coord? Outward(BoardPlate p, Coord c)
    {
        int l = p.Origin.X, r = p.Origin.X + p.Width - 1, b = p.Origin.Y, t = p.Origin.Y + p.Height - 1;
        bool corner = (c.X == l || c.X == r) && (c.Y == b || c.Y == t);
        return corner ? null
            : c.X == l ? new Coord(c.X - 1, c.Y)
            : c.X == r ? new Coord(c.X + 1, c.Y)
            : c.Y == b ? new Coord(c.X, c.Y - 1)
            : c.Y == t ? new Coord(c.X, c.Y + 1)
            : null;
    }

    [Theory]
    [MemberData(nameof(Players))]
    public void 岛尺寸不小于WN(int players)
    {
        // prd 验收：n=2 时可落子区（不含留白）外接矩形 ≥ 26×26，每多一人再大一圈；design D2：画布 (W+2M)²。
        int w = Side[players];
        Assert.True(w >= 26 + (5 * (players - 2)), $"岛边长 {w} 低于 prd 的下限");
        Assert.Equal(w, LinkMapGenerator.IslandSide(players));
        foreach ((ulong seed, MapData map) in Maps(players))
        {
            string at = $"link:{seed}:{players}";
            Assert.Equal(w + (2 * M), map.Width);
            Assert.Equal(w + (2 * M), map.Height);
            Coord[] playable = [.. map.AllCoords().Where(map.IsPlayable)];
            Assert.Equal(w, playable.Max(c => c.X) - playable.Min(c => c.X) + 1);
            Assert.Equal(w, playable.Max(c => c.Y) - playable.Min(c => c.Y) + 1);
            Assert.All(map.AllCoords().Where(c => !OnIsland(c, w)), c => Assert.True(map.Obstacles.Contains(c), $"{at}：岛外 / 切角 {c.ToNotation()} 不是障碍"));
            Assert.Equal(players, map.MaxPlayers);
            Assert.Equal($"link:{seed}:{players}", map.Id);
        }
    }

    [Theory]
    [MemberData(nameof(Players))]
    public void 平台数与尺寸(int players)
    {
        // 裁决 R2 / design D2：平台数 6–8 / 8–10 / 10–12；中央 8×8 居中；其余宽高 6–8；距岛边 ≥ 2；两两外接矩形间隔 ≥ 3；全是出生棋盘、主题四选一（可重复）。
        int w = Side[players];
        (int min, int max) = PlateRange[players];
        int centre = M + ((w - 8) / 2);
        var counts = new HashSet<int>();
        var themes = new HashSet<BoardTheme>();
        bool repeated = false;
        foreach ((ulong seed, MapData map) in Maps(players))
        {
            string at = $"link:{seed}:{players}";
            Assert.InRange(map.Boards.Length, min, max);
            Assert.True(map.Boards.Length > players, $"{at}：平台数不多于人数");
            counts.Add(map.Boards.Length);
            Assert.Single(map.Boards, p => p.Origin == new Coord(centre, centre) && p.Width == 8 && p.Height == 8);
            for (int i = 0; i < map.Boards.Length; i++)
            {
                BoardPlate p = map.Boards[i];
                Assert.Equal(BoardPlateKind.Birth, p.Kind);
                Assert.InRange(p.Width, 6, 8);
                Assert.InRange(p.Height, 6, 8);
                Assert.True(p.Origin.X >= M + 2 && p.Origin.Y >= M + 2 && p.Origin.X + p.Width <= M + w - 2 && p.Origin.Y + p.Height <= M + w - 2, $"{at}：平台 {i} 距岛边不足 2");
                Assert.Contains(p.Theme!.Value, new[] { BoardTheme.River, BoardTheme.Forest, BoardTheme.Palisade, BoardTheme.Mixed });
                themes.Add(p.Theme!.Value);

                // 出生区 i = 平台 i 的核心 3×3（独立算式：偏西南）。
                var core = new Coord(p.Origin.X + ((p.Width - 3) / 2), p.Origin.Y + ((p.Height - 3) / 2));
                Assert.Equal(TestMaps.Rect(core.X, core.Y, 3, 3).Order(), map.BirthZones[i].Order());
                for (int j = i + 1; j < map.Boards.Length; j++)
                {
                    BoardPlate q = map.Boards[j];
                    int gapX = Math.Max(q.Origin.X - (p.Origin.X + p.Width), p.Origin.X - (q.Origin.X + q.Width));
                    int gapY = Math.Max(q.Origin.Y - (p.Origin.Y + p.Height), p.Origin.Y - (q.Origin.Y + q.Height));
                    Assert.True(Math.Max(gapX, gapY) >= 3, $"{at}：平台 {i} 与 {j} 间隔 {Math.Max(gapX, gapY)}");
                }
            }

            // 编号 = 左下角先行后列。
            Assert.Equal(map.Boards.OrderBy(p => p.Origin.Y).ThenBy(p => p.Origin.X), map.Boards);
            repeated |= map.Boards.GroupBy(p => p.Theme).Any(g => g.Count() > 1);
        }

        output.WriteLine($"{players} 人：平台数取到 {string.Join("/", counts.Order())}");
        Assert.Equal(4, themes.Count);   // 样本口径：四个主题都出现
        Assert.True(repeated, "样本口径：主题可重复（R3），20 张图里应当出现过");
        Assert.True(counts.Count >= 2, "样本口径：平台数不是恒定值");
    }

    [Theory]
    [MemberData(nameof(Players))]
    public void 平台高二且坡道二到三朝外是低地(int players)
    {
        // design D3：平台内可落子格除坡道外 h=2；坡道 h=1、草地、2–3 个、在外圈非角格、两两不在同一条边、朝外的格是 h=0 可落子低地；
        // 坡道之外平台与低地之间没有气边（崖壁）——平台内可落子格沿气边走出平台只能经坡道。
        int w = Side[players];
        var rampCounts = new HashSet<int>();
        foreach ((ulong seed, MapData map) in Maps(players))
        {
            string at = $"link:{seed}:{players}";
            foreach (BoardPlate p in map.Boards)
            {
                Coord[] playable = [.. p.Cells().Where(map.IsPlayable)];
                Coord[] ramps = [.. playable.Where(c => map.HeightAt(c) == 1)];
                Assert.All(playable.Except(ramps), c => Assert.True(map.HeightAt(c) == 2, $"{at}：{c.ToNotation()} 高 {map.HeightAt(c)}"));
                Assert.InRange(ramps.Length, 2, 3);
                rampCounts.Add(ramps.Length);
                var sides = new HashSet<(int, int)>();
                foreach (Coord ramp in ramps)
                {
                    Coord o = Assert.IsType<Coord>(Outward(p, ramp));
                    Assert.True(OnIsland(o, w) && map.IsPlayable(o) && map.HeightAt(o) == 0 && !InPlate(map, o), $"{at}：坡道 {ramp.ToNotation()} 朝外 {o.ToNotation()} 不是低地");
                    Assert.Equal(Surface.Grass, map.SurfaceAt(ramp));
                    sides.Add((o.X - ramp.X, o.Y - ramp.Y));
                    Assert.Contains(o, Adjacency.LibertyNeighbors(map, ramp));   // 坡道两头都通（h0 → h1 → h2）
                }

                Assert.Equal(ramps.Length, sides.Count);   // 两两不在同一条边
                Assert.All(p.CoreCells(), c => Assert.True(map.IsPlayable(c) && map.HeightAt(c) == 2 && map.SurfaceAt(c) == Surface.Grass, $"{at}：核心区 {c.ToNotation()}"));
                foreach (Coord c in playable.Except(ramps))
                {
                    Assert.All(Adjacency.LibertyNeighbors(map, c), n => Assert.True(p.Contains(n), $"{at}：{c.ToNotation()} 不是坡道却与平台外 {n.ToNotation()} 有气边"));
                }
            }
        }

        Assert.Equal([2, 3], rampCounts.Order());   // 样本口径：两种数量都出现
    }

    [Theory]
    [MemberData(nameof(Players))]
    public void 全图可落子格沿气边连通(int players)
    {
        // design D4 连通硬约束：全部可落子格沿气边（Adjacency.LibertyNeighbors）连成一片；几何战场恰 1 个。测试内独立 BFS。
        foreach ((ulong seed, MapData map) in Maps(players))
        {
            Coord[] playable = [.. map.AllCoords().Where(map.IsPlayable)];
            var reached = new HashSet<Coord> { playable[0] };
            var queue = new Queue<Coord>(reached);
            while (queue.Count > 0)
            {
                foreach (Coord n in Adjacency.LibertyNeighbors(map, queue.Dequeue()))
                {
                    if (reached.Add(n))
                    {
                        queue.Enqueue(n);
                    }
                }
            }

            Assert.True(reached.Count == playable.Length, $"link:{seed}:{players}：气边只连到 {reached.Count}/{playable.Length} 格");
            Assert.Equal(1, Battlefields.Of(map).Count);
        }
    }

    [Theory]
    [MemberData(nameof(Players))]
    public void 低地有N条河道各两座桥(int players)
    {
        // design D4：平台外、岛内、非切角为低地（h=0）；河道 n 条，各是 1 格宽的深水折线、恰 2 座不相邻的预置桥；不占坡道朝外格。
        int w = Side[players];
        foreach ((ulong seed, MapData map) in Maps(players))
        {
            string at = $"link:{seed}:{players}";
            Coord[] lowland = [.. map.AllCoords().Where(c => OnIsland(c, w) && !InPlate(map, c))];
            Assert.All(lowland, c => Assert.Equal(0, map.HeightAt(c)));
            List<List<Coord>> rivers = Clumps([.. lowland.Where(c => map.SurfaceAt(c) == Surface.DeepWater)]);
            Assert.True(rivers.Count == players, $"{at}：低地河道 {rivers.Count} 条");
            HashSet<Coord> outlets = [.. map.Boards.SelectMany(p => p.Cells().Where(c => map.IsPlayable(c) && map.HeightAt(c) == 1).Select(c => Outward(p, c)!.Value))];
            foreach (List<Coord> river in rivers)
            {
                Assert.True(river.Count >= 6, $"{at}：河道只有 {river.Count} 格");
                Assert.All(river, c => Assert.True(river.Count(o => Adjacency.AreAdjacent(o, c)) <= 2, $"{at}：河道在 {c.ToNotation()} 不是 1 格宽"));
                Coord[] bridges = [.. river.Where(map.HasBridge)];
                Assert.Equal(2, bridges.Length);
                Assert.False(Adjacency.AreAdjacent(bridges[0], bridges[1]), $"{at}：两座桥相邻");
                Assert.DoesNotContain(river, outlets.Contains);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Players))]
    public void 岛缘林地与少量岩石(int players)
    {
        // design D4：低地的林地只在岛缘一圈；低地岩石（障碍）2 –(n+1) 格、都在岛缘、不在坡道朝外格。
        int w = Side[players];
        int forestTotal = 0;
        foreach ((ulong seed, MapData map) in Maps(players))
        {
            string at = $"link:{seed}:{players}";
            Coord[] lowland = [.. map.AllCoords().Where(c => OnIsland(c, w) && !InPlate(map, c))];
            Coord[] forest = [.. lowland.Where(c => map.SurfaceAt(c) == Surface.Forest)];
            Coord[] rocks = [.. lowland.Where(map.Obstacles.Contains)];
            Assert.All(forest, c => Assert.True(OnIslandEdge(c, w), $"{at}：林地 {c.ToNotation()} 不在岛缘"));
            Assert.All(rocks, c => Assert.True(OnIslandEdge(c, w), $"{at}：岩石 {c.ToNotation()} 不在岛缘"));
            Assert.InRange(rocks.Length, 2, players + 1);
            forestTotal += forest.Length;
        }

        Assert.True(forestTotal > 20 * players, $"样本口径：20 张图的岛缘林地合计只有 {forestTotal} 格");
    }

    [Theory]
    [MemberData(nameof(Players))]
    public void 不放静态信物格(int players)
    {
        // design D5（段 A3 改写，依据 relic-redesign R29，2026-10-26 用户裁决）：浮空岛不放静态信物格——出生区信物与低地公共信物都没有。
        // 原「信物数量与位置」（每平台核心区 2 个出生区信物、低地公共信物 ≤ n+2）随之改为本条。
        foreach ((ulong seed, MapData map) in Maps(players))
        {
            Assert.True(map.RelicCells.IsEmpty, $"link:{seed}:{players} 有 {map.RelicCells.Count} 个信物格");
        }
    }

    [Theory]
    [MemberData(nameof(Players))]
    public void 新地表不改其余地形(int players)
    {
        // link-map-surfaces D1 / D5（map-expand-25 D3：叠加在抬升前、读主题原始高度）：同一标识有无新地表叠加，只差平台中间带若干格的地表（荒漠 / 沼泽 / 岩台 / 浅滩），每块至多 4 格，低地没有新地表。
        Surface[] special = [Surface.Desert, Surface.Marsh, Surface.Crag, Surface.Shallows];
        int total = 0;
        for (ulong seed = 1; seed <= 5; seed++)
        {
            MapData with = LinkMapGenerator.Generate(seed, players);
            MapData without = LinkMapGenerator.GenerateWithoutSurfaces(seed, players);
            Coord[] changed = [.. with.AllCoords().Where(c => with.SurfaceAt(c) != without.SurfaceAt(c))];
            Assert.All(changed, c => Assert.Contains(with.SurfaceAt(c), special));
            Assert.All(changed, c => Assert.Equal(Surface.Grass, without.SurfaceAt(c)));
            Assert.All(changed, c => Assert.Contains(with.Boards, p => p.InMiddleBand(c)));
            Assert.All(with.Boards, p => Assert.InRange(changed.Count(p.Contains), 0, 4));
            total += changed.Length;

            // 其余逐项相同。
            Assert.Equal(without.Obstacles.Order(), with.Obstacles.Order());
            Assert.Equal(without.TerrainData.Heights.OrderBy(kv => kv.Key), with.TerrainData.Heights.OrderBy(kv => kv.Key));
            Assert.Equal(without.TerrainData.Bridges.Order(), with.TerrainData.Bridges.Order());
            Assert.Equal(without.TerrainData.Fences.OrderBy(f => f.A).ThenBy(f => f.B), with.TerrainData.Fences.OrderBy(f => f.A).ThenBy(f => f.B));
            Assert.Equal(without.RelicCells.OrderBy(kv => kv.Key), with.RelicCells.OrderBy(kv => kv.Key));
            Assert.Equal(without.Boards, with.Boards);
        }

        Assert.True(total > 0, "样本口径：确实放了新地表");
    }

    /// <summary>格集合按几何四邻分团（测试内独立实现）。</summary>
    private static List<List<Coord>> Clumps(List<Coord> cells)
    {
        var left = new HashSet<Coord>(cells);
        var result = new List<List<Coord>>();
        while (left.Count > 0)
        {
            Coord start = left.Order().First();
            var clump = new List<Coord> { start };
            left.Remove(start);
            for (int i = 0; i < clump.Count; i++)
            {
                foreach (Coord n in left.Where(c => Adjacency.AreAdjacent(c, clump[i])).ToList())
                {
                    left.Remove(n);
                    clump.Add(n);
                }
            }

            result.Add(clump);
        }

        return result;
    }
}
