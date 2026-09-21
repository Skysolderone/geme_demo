using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.MapGeneration;

/// <summary>
/// 规格：openspec/changes/map-generator/specs/map-generation —— Requirement: 布局规则；specs/map-definition —— Scenario: 生成图同样经过校验。
/// 样本口径：地图种子 1–50 × 平台数 5–8，共 200 张（<see cref="MapGenFixtures.Sample"/>）。断言一律从 <see cref="MapData"/> 反推，
/// 不读生成器的工作态——生成器自报的外接方块只用来对照。
/// </summary>
public class 生成图布局规则Tests
{
    public static TheoryData<int> PlatformCounts() => new() { 5, 6, 7, 8 };

    private static IEnumerable<GeneratedMap> Maps(int platforms) =>
        MapGenFixtures.Sample().Where(s => s.Platforms == platforms).Select(s => MapGenFixtures.Generated(s.Seed, s.Platforms));

    [Theory]
    [MemberData(nameof(PlatformCounts))]
    public void 生成图通过边疆档静态校验(int platforms)
    {
        // Scenario: 生成图通过校验 / 生成图同样经过校验——与内置边疆图同一个入口（GameBoard.Load → MapValidator）。
        foreach (GeneratedMap g in Maps(platforms))
        {
            MapData map = g.Map;
            MapValidationResult result = MapValidator.Validate(map);
            Assert.True(result.IsValid, $"{map.Id}：{result}");
            Assert.NotNull(GameBoard.Load(map));

            Assert.Equal(MapProfile.Frontier, map.Profile);
            Assert.Equal((25, 30, 4), (map.Width, map.Height, map.MaxPlayers));
            Assert.Equal(platforms, map.BirthZones.Length);
            Assert.InRange(map.PlayableCount, 300, 420);
            Assert.NotEmpty(result.Reports);                                  // 边疆档：各平台的距离报告照出（裁决 7）
            Assert.Empty(map.PocketExemptions);                               // 不靠豁免过口袋规则
        }
    }

    [Theory]
    [MemberData(nameof(PlatformCounts))]
    public void 平台规整(int platforms)
    {
        // Scenario: 平台规整。
        // 变异验证 MG-8：把 FrontierMapLayout.PlatformGap 改成 1 → 本测试与缓坡测试红。
        foreach (GeneratedMap g in Maps(platforms))
        {
            MapData map = g.Map;
            var boxes = new List<(int X0, int Y0, int X1, int Y1)>();
            for (int z = 0; z < map.BirthZones.Length; z++)
            {
                (int X0, int Y0, int X1, int Y1) box = MapGenFixtures.BoundingBox(map.BirthZones[z]);
                boxes.Add(box);
                int side = box.X1 - box.X0 + 1;
                Assert.True(side == box.Y1 - box.Y0 + 1, $"{map.Id} 平台 {z + 1} 不是方块。");
                Assert.InRange(side, 5, 9);
                Assert.Equal(new PlatformRect(box.X0, box.Y0, side), g.Platforms[z]);
                Assert.All(map.BirthZones[z], c => Assert.Equal(2, map.HeightAt(c)));
                Assert.All(map.BirthZones[z], c => Assert.True(map.IsPlayable(c)));
                Assert.True(map.BirthZones[z].Count == side * side, $"{map.Id} 平台 {z + 1} 有 {map.BirthZones[z].Count} 格，不是整块 {side}×{side}。");

                // 离地图外缘至少 2 格（design D1）。
                Assert.True(box.X0 >= 2 && box.Y0 >= 2 && box.X1 <= map.Width - 3 && box.Y1 <= map.Height - 3, $"{map.Id} 平台 {z + 1} 贴边。");

                // 平台留白（design 裁决 17）：方块按生成器报告的外接方块取（不从出生区反推——反推看不见缺了一整条边的情形），
                // 方块内每一格都是本平台的可落子格，没有障碍格。变异验证 MG-18：摆完缓坡后把 1 号平台中心格改成岩石 → 本测试红。
                PlatformRect rect = g.Platforms[z];
                for (int y = rect.Y; y <= rect.Y1; y++)
                {
                    for (int x = rect.X; x <= rect.X1; x++)
                    {
                        var c = new Coord(x, y);
                        Assert.False(map.Obstacles.Contains(c), $"{map.Id} 平台 {z + 1} 方块内的 {c} 是障碍格。");
                        Assert.True(map.BirthZones[z].Contains(c) && map.IsPlayable(c), $"{map.Id} {c} 在平台 {z + 1} 方块内却不是平台格。");
                    }
                }
            }

            // 编号按边长从大到小。
            int[] sides = [.. boxes.Select(b => b.X1 - b.X0 + 1)];
            Assert.Equal(sides.OrderByDescending(s => s), sides);

            // 两两至少隔 2 格：任一方块向四周各扩 2 格后仍不与另一方块相交。
            for (int a = 0; a < boxes.Count; a++)
            {
                for (int b = a + 1; b < boxes.Count; b++)
                {
                    int dx = Math.Max(boxes[a].X0 - boxes[b].X1, boxes[b].X0 - boxes[a].X1) - 1;
                    int dy = Math.Max(boxes[a].Y0 - boxes[b].Y1, boxes[b].Y0 - boxes[a].Y1) - 1;
                    Assert.True(Math.Max(dx, dy) >= 2, $"{map.Id} 平台 {a + 1} 与 {b + 1} 只隔 {Math.Max(dx, dy)} 格。");
                }
            }

            // h=2 的格子全在出生区里：平台之外没有高台。
            Assert.All(map.TerrainData.Heights.Where(kv => kv.Value == 2), kv => Assert.NotNull(map.BirthZoneOf(kv.Key)));
        }
    }

    [Theory]
    [MemberData(nameof(PlatformCounts))]
    public void 缓坡每平台一到两处_每处两到三格宽_其余边缘为崖壁(int platforms)
    {
        foreach (GeneratedMap g in Maps(platforms))
        {
            MapData map = g.Map;
            var ramps = map.TerrainData.Heights.Where(kv => kv.Value == 1).Select(kv => kv.Key).ToHashSet();
            var claimed = new HashSet<Coord>();
            for (int z = 0; z < g.Platforms.Length; z++)
            {
                PlatformRect r = g.Platforms[z];

                // 四条边外侧一格上的缓坡格，按边分组。
                List<List<Coord>> runs =
                [
                    [.. Enumerable.Range(r.Y, r.Side).Select(y => new Coord(r.X - 1, y)).Where(ramps.Contains)],
                    [.. Enumerable.Range(r.Y, r.Side).Select(y => new Coord(r.X1 + 1, y)).Where(ramps.Contains)],
                    [.. Enumerable.Range(r.X, r.Side).Select(x => new Coord(x, r.Y - 1)).Where(ramps.Contains)],
                    [.. Enumerable.Range(r.X, r.Side).Select(x => new Coord(x, r.Y1 + 1)).Where(ramps.Contains)],
                ];
                List<List<Coord>> used = [.. runs.Where(run => run.Count > 0)];
                Assert.InRange(used.Count, 1, 2);
                foreach (List<Coord> run in used)
                {
                    Assert.InRange(run.Count, 2, 3);
                    int span = Math.Max(run.Max(c => c.X) - run.Min(c => c.X), run.Max(c => c.Y) - run.Min(c => c.Y));
                    Assert.True(span == run.Count - 1, $"{map.Id} 平台 {z + 1} 的缓坡不连续。");
                    Assert.All(run, c => Assert.True(map.IsPlayable(c)));
                    claimed.UnionWith(run);
                }
            }

            // 没有无主的缓坡格；缓坡都标成了咽喉。
            Assert.True(ramps.SetEquals(claimed), $"{map.Id} 有不贴任何平台的 h=1 格。");
            Assert.All(ramps, c => Assert.Contains(c, map.ChokePoints));
            Assert.All(map.TerrainData.Bridges, c => Assert.Contains(c, map.ChokePoints));
        }
    }

    [Theory]
    [MemberData(nameof(PlatformCounts))]
    public void 全部平台与中央区域沿气边连成一片(int platforms)
    {
        // Scenario: 全部平台连通——比"任意两个平台有通路"更强：全部可落子格从中央入口出发都走得到。
        foreach (GeneratedMap g in Maps(platforms))
        {
            MapData map = g.Map;
            var seen = new HashSet<Coord> { map.CentralEntrance };
            var queue = new Queue<Coord>([map.CentralEntrance]);
            while (queue.Count > 0)
            {
                foreach (Coord n in Adjacency.LibertyNeighbors(map, queue.Dequeue()))
                {
                    if (seen.Add(n))
                    {
                        queue.Enqueue(n);
                    }
                }
            }

            Assert.Equal(map.PlayableCount, seen.Count);
            Assert.All(map.BirthZones, zone => Assert.Contains(zone, seen.Contains));
        }
    }

    [Theory]
    [MemberData(nameof(PlatformCounts))]
    public void 桥的长度上限_河垂直穿过走廊(int platforms)
    {
        // Scenario: 桥的长度上限。一座桥 = 相邻桥格连成的一块；河一格宽，所以一座桥的格数就是它沿河的长度。
        // 河垂直穿过走廊 ⇒ 桥长 = 走廊宽 ≤ 3；河顺着走廊流才会出 4 格以上的"长桥"。
        // 变异验证 MG-12：FrontierMapLayout.MaxBridgeSpan 3 → 6 → 本测试红（3 格宽走廊并排过河时会并成 4–6 格的桥）。
        foreach (GeneratedMap g in Maps(platforms))
        {
            MapData map = g.Map;
            List<int> spans = MapGenFixtures.BridgeSpans(map);
            Assert.True(spans.Count >= 2, $"{map.Id} 只有 {spans.Count} 座桥。");
            Assert.True(spans.Max() <= 3, $"{map.Id} 有一座 {spans.Max()} 格长的桥。\n{MapGenFixtures.TextArt(map)}");
            Assert.True(spans.Sum() <= 10, $"{map.Id} 桥格共 {spans.Sum()} 个。");

            // 每个桥格都是"过河"的：桥面方向（相邻桥格的连线）上的两端是水，垂直方向的两侧是可落子的岸。
            foreach (Coord b in map.TerrainData.Bridges)
            {
                bool Shore(int dx, int dy) => map.IsPlayable(new Coord(b.X + dx, b.Y + dy)) && !map.HasBridge(new Coord(b.X + dx, b.Y + dy));
                Assert.True((Shore(1, 0) && Shore(-1, 0)) || (Shore(0, 1) && Shore(0, -1)), $"{map.Id} 桥格 {b} 两岸不通。");
            }
        }
    }

    [Theory]
    [MemberData(nameof(PlatformCounts))]
    public void 走廊全长至少两格宽_没有一枚子就能堵死的口子(int platforms)
    {
        // Scenario: 走廊宽度。两条口径同时成立，缓坡口与桥头不设豁免（样本 200 张实测无需豁免）：
        // ① 几何：每个过渡带格（平台之外的可落子格：走廊、广场、林地、桥、缓坡）都属于某个四格全是过渡带格的 2×2 方块；
        // ② 功能：拿掉任何一个过渡带格，每个平台仍能沿气边（算上栅栏与崖壁）走到中央入口——2×2 口径管不到"两个方块只搭一个角"和"栅栏拦掉一条道"。
        // 变异验证 MG-13：去掉 TryBuild 里的 WidenNarrowCorridors 与 CheckCorridorWidth → 本测试红。
        foreach (GeneratedMap g in Maps(platforms))
        {
            MapData map = g.Map;
            List<Coord> narrow = MapGenFixtures.NarrowCells(map);
            Assert.True(narrow.Count == 0, $"{map.Id} 在 {string.Join(" ", narrow)} 只有 1 格宽。\n{MapGenFixtures.TextArt(map)}");
            List<Coord> chokes = MapGenFixtures.ChokingCells(map);
            Assert.True(chokes.Count == 0, $"{map.Id} 在 {string.Join(" ", chokes)} 落一枚子就能隔开某个平台。\n{MapGenFixtures.TextArt(map)}");
        }
    }

    [Theory]
    [MemberData(nameof(PlatformCounts))]
    public void 主河从一边贯穿到对边(int platforms)
    {
        // 布局规则：一条从地图一边贯穿到对边的主河。口径：只走内陆（不含最外一圈外海）的深水格与桥格，能从左数第二列走到右数第二列，
        // 或从下数第二行走到上数第二行。
        foreach (GeneratedMap g in Maps(platforms))
        {
            MapData map = g.Map;
            bool Wet(Coord c) => map.SurfaceAt(c) == Surface.DeepWater;
            bool Inland(Coord c) => c.X >= 1 && c.Y >= 1 && c.X <= map.Width - 2 && c.Y <= map.Height - 2;

            bool Crosses(Func<Coord, bool> isStart, Func<Coord, bool> isEnd)
            {
                var seen = new HashSet<Coord>(map.AllCoords().Where(c => Inland(c) && Wet(c) && isStart(c)));
                var queue = new Queue<Coord>(seen.Order());
                while (queue.Count > 0)
                {
                    Coord cur = queue.Dequeue();
                    if (isEnd(cur))
                    {
                        return true;
                    }

                    foreach (Coord n in Adjacency.Neighbors(map.Width, map.Height, cur))
                    {
                        if (Inland(n) && Wet(n) && seen.Add(n))
                        {
                            queue.Enqueue(n);
                        }
                    }
                }

                return false;
            }

            Assert.True(
                Crosses(c => c.X == 1, c => c.X == map.Width - 2) || Crosses(c => c.Y == 1, c => c.Y == map.Height - 2),
                $"{map.Id} 没有贯穿的主河。\n{MapGenFixtures.TextArt(map)}");
        }
    }

    [Theory]
    [MemberData(nameof(PlatformCounts))]
    public void 小平台靠中央(int platforms)
    {
        // 可测口径：边长最小的两个平台（编号最后两个）到中央入口的沿气边距离，不大于任何一个边长最大的平台的距离。
        foreach (GeneratedMap g in Maps(platforms))
        {
            BirthZoneDistance toEntrance = MapValidator.DistanceTable(g.Map).Single(m => m.Name == "中央入口");
            int n = g.Platforms.Length;
            int smallFar = Math.Max(toEntrance.Distances[n - 1]!.Value, toEntrance.Distances[n - 2]!.Value);
            int largeNear = Enumerable.Range(0, n)
                .Where(i => g.Platforms[i].Side == g.Platforms[0].Side)
                .Min(i => toEntrance.Distances[i]!.Value);
            Assert.True(smallFar <= largeNear, $"{g.Map.Id}：小平台 {smallFar} 步，大平台 {largeNear} 步。");
        }
    }

    [Theory]
    [MemberData(nameof(PlatformCounts))]
    public void 资源布点(int platforms)
    {
        // Scenario: 资源布点（平台内按边长 1 或 2 个信物、公共区 7 个、地图数据不含据点）；各平台数同理。
        // 变异验证 MG-10：把中央入口的信物档位从高档改成标准档 → 本测试红。
        // 变异验证 M-B17（restore-go-core-rules 段 B，实跑红 29）：MapFile.ToJson 写出一个 "Sites" 字段 → 本测试红。
        foreach (GeneratedMap g in Maps(platforms))
        {
            MapData map = g.Map;
            Coord e = map.CentralEntrance;

            Assert.DoesNotContain("\"Sites\"", MapFile.ToJson(map), StringComparison.Ordinal);   // 生成器 MUST NOT 布置据点

            // 信物：平台内按边长 1 或 2 个；公共区 7 个 = 中央入口 1 个高档 + 6 个标准档。
            for (int z = 0; z < platforms; z++)
            {
                int expected = g.Platforms[z].Side >= 7 ? 2 : 1;
                Assert.Equal(expected, map.RelicCells.Keys.Count(c => map.BirthZoneOf(c) == z));
            }

            KeyValuePair<Coord, RelicCellSpec>[] contested = [.. map.RelicCells.Where(kv => kv.Value.Zone == RelicZone.Contested)];
            Assert.Equal(7, contested.Length);
            Assert.Equal([e], contested.Where(kv => kv.Value.Budget == BudgetTier.High).Select(kv => kv.Key));
            Assert.Equal(6, contested.Count(kv => kv.Value.Budget == BudgetTier.Standard));

            // 河、桥、栅栏、林地。
            Assert.True(map.TerrainData.Bridges.Count >= 2, $"{map.Id} 桥少于 2 座。");
            Assert.NotEmpty(map.TerrainData.Fences);
            Assert.Contains(Surface.Forest, map.TerrainData.Surfaces.Values);

            // 桥头优先放标准档：只要有桥头可放，就至少有一个标准档公共信物挨着桥。
            Assert.Contains(contested, kv => Adjacency.Neighbors(map.Width, map.Height, kv.Key).Any(map.HasBridge));
        }
    }
}
