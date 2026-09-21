using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Match;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>
/// 规格：openspec/changes/frontier-map/specs/map-definition —— Requirement: 边疆档基准地图（tasks 3.1–3.4、3.6 的规则测试）。
/// 距离、连通、缓坡分段都在测试内用 <see cref="Adjacency.LibertyNeighbors"/> 独立推出，不调用校验器的距离表，也不抄生成器的坐标。
/// </summary>
public class 边疆档基准地图Tests
{
    private static readonly MapData Map = FrontierMapV2.Create();

    /// <summary>规格写死的平台边长，按平台编号 1–6（生成器注释里的"编号 → 边长 → 方位"表）。</summary>
    private static readonly int[] Sides = [9, 8, 7, 6, 5, 5];

    [Fact]
    public void 外接尺寸与校验通过()
    {
        // 规格：宽度 MUST NOT 超过 25 列，本图取 25 × 28–32；可落子格落在边疆档 4 人区间 300–420；边疆档全项校验通过，五项距离作为报告项给出。
        Assert.Equal("siege-frontier-v2", Map.Id);
        Assert.Equal(25, Map.Width);
        Assert.InRange(Map.Height, 28, 32);
        Assert.Equal(4, Map.MaxPlayers);
        Assert.Equal(MapProfile.Frontier, Map.Profile);
        Assert.InRange(Map.PlayableCount, 300, 420);

        MapValidationResult result = MapValidator.Validate(Map);
        Assert.True(result.IsValid, result.ToString());
        Assert.Equal(3, result.Reports.Count(r => r.Code == "BIRTH_ZONE_DISTANCE_REPORT"));   // restore-go-core-rules：距离报告项五项改三项
        Assert.Empty(Map.PocketExemptions);
    }

    [Fact]
    public void 平台规模()
    {
        // 规格 Scenario：共 6 个，外接边长 5、5、6、7、8、9，全部格子 h=2，每个平台恰是整块外接方块（格数 = 边长²，平台留白）。
        // 变异 M-B1（平台留白修订后）：字符画里把 5 号台一格 '5' 改成 '#' → 构造当场抛出、本类全红；把平台一格抬 / 降高度 → h=2 断言红。
        Assert.Equal(6, Map.BirthZones.Length);
        for (int z = 0; z < 6; z++)
        {
            ImmutableHashSet<Coord> zone = Map.BirthZones[z];
            int side = Sides[z];
            int width = zone.Max(c => c.X) - zone.Min(c => c.X) + 1;
            int height = zone.Max(c => c.Y) - zone.Min(c => c.Y) + 1;
            Assert.Equal((side, side), (width, height));
            Assert.All(zone, c => Assert.Equal(2, Map.HeightAt(c)));
            Assert.All(zone, c => Assert.True(Map.IsPlayable(c), $"{BirthZoneLabel.Of(z)} 的 {c} 不可落子。"));
            Assert.True(zone.Count == side * side, $"{BirthZoneLabel.Of(z)} 有 {zone.Count} 格，应为整块 {side}×{side}。");
        }

        Assert.Equal([5, 5, 6, 7, 8, 9], Sides.Order());
    }

    [Fact]
    public void 平台留白_平台内无障碍格()
    {
        // 规格：平台内 MUST NOT 有障碍格（map-generator 裁决 17，负责人 2026-09-21 试玩后要求）。外接方块按规格写死的边长从出生区推出，
        // 方块里每一格都属于本平台、可落子、不是障碍；留在平台上的只有 1–2 个信物格（边长 5–6 的 1 个、7–9 的 2 个），
        // 变异（frontier-map 段 B）M-B15：字符画解析里恢复"方块内允许 '#'"并把 5 号台 F15 改回 '#' → 本测试与「平台规模」红。
        for (int z = 0; z < 6; z++)
        {
            ImmutableHashSet<Coord> zone = Map.BirthZones[z];
            int side = Sides[z];
            Coord[] box = [.. FrontierFixtures.Rect(zone.Min(c => c.X), zone.Min(c => c.Y), side, side)];
            Assert.Empty(box.Where(Map.Obstacles.Contains));
            Assert.All(box, c => Assert.True(zone.Contains(c) && Map.IsPlayable(c), $"{BirthZoneLabel.Of(z)} 的外接方块里 {c} 不是平台格。"));
            Assert.Equal(side >= 7 ? 2 : 1, box.Count(Map.RelicCells.ContainsKey));
        }

        Assert.Equal([81, 64, 49, 36, 25, 25], Map.BirthZones.Select(z => z.Count));
        Assert.Equal(411, Map.PlayableCount);
    }

    [Fact]
    public void 平台四周是崖壁只经缓坡下到过渡带()
    {
        // design D3 / tasks 3.1：每平台 1–2 处 2–3 格宽的 h=1 缓坡，两个 5×5 各开 2 处；平台外、与平台有气边的格只能是缓坡。
        // 缓坡再往外必须接到 h=0 的过渡带（否则是悬在半空的台阶）。
        for (int z = 0; z < 6; z++)
        {
            ImmutableHashSet<Coord> zone = Map.BirthZones[z];
            HashSet<Coord> exits = [.. zone.SelectMany(c => Adjacency.LibertyNeighbors(Map, c)).Where(n => !zone.Contains(n))];
            Assert.All(exits, n => Assert.Equal(1, Map.HeightAt(n)));

            List<HashSet<Coord>> ramps = GeometricClusters(exits);
            Assert.InRange(ramps.Count, 1, 2);
            Assert.All(ramps, ramp => Assert.InRange(ramp.Count, 2, 3));
            Assert.All(ramps, ramp => Assert.Contains(ramp.SelectMany(c => Adjacency.LibertyNeighbors(Map, c)), n => Map.HeightAt(n) == 0));
            if (Sides[z] == 5)
            {
                Assert.Equal(2, ramps.Count);
            }
        }

        // 全图的 h=1 格都是某个平台的缓坡，且都标成了咽喉；四座桥同样是咽喉。
        Coord[] h1 = [.. Map.AllCoords().Where(c => Map.IsPlayable(c) && Map.HeightAt(c) == 1)];
        Assert.All(h1, c => Assert.Contains(Adjacency.LibertyNeighbors(Map, c), n => Map.BirthZoneOf(n) is not null));
        Assert.Equal(h1.Concat(Map.TerrainData.Bridges).Order(), Map.ChokePoints.Order());
    }

    [Fact]
    public void 平台连成一片()
    {
        // 规格 Scenario：任取两个平台，沿气边求最短路 → 通路存在。过渡带（平台之外的可落子格）只有 h=0 / h=1，全图可落子格连成一块。
        for (int a = 0; a < 6; a++)
        {
            Dictionary<Coord, int> dist = Distances(Map.BirthZones[a]);
            for (int b = 0; b < 6; b++)
            {
                Assert.Contains(Map.BirthZones[b], dist.ContainsKey);
            }
        }

        Coord[] playable = [.. Map.AllCoords().Where(Map.IsPlayable)];
        Assert.Equal(playable.Length, Distances([Map.CentralEntrance]).Count);
        Assert.All(playable.Where(c => Map.BirthZoneOf(c) is null), c => Assert.InRange(Map.HeightAt(c), 0, 1));
    }

    [Fact]
    public void 小平台更靠中央()
    {
        // 规格 Scenario：两个边长 5 的平台到中央入口的沿气边距离都小于三个边长 7–9 的平台。
        // 规格正文更严——"比其余平台更靠近中央入口"，6×6 也算"其余"，一并比较。
        // 变异 M-B2：把 5 号台朝广场的缓坡 K14–K16 改成岩石（只剩北缓坡绕行）→ 5 号台距离变大，本测试红。
        int[] d = [.. Map.BirthZones.Select(z => Distances(z)[Map.CentralEntrance])];
        int[] small = [.. Enumerable.Range(0, 6).Where(z => Sides[z] == 5).Select(z => d[z])];
        int[] large = [.. Enumerable.Range(0, 6).Where(z => Sides[z] >= 7).Select(z => d[z])];
        int[] others = [.. Enumerable.Range(0, 6).Where(z => Sides[z] != 5).Select(z => d[z])];
        Assert.Equal(2, small.Length);
        Assert.Equal(3, large.Length);
        Assert.Equal(4, others.Length);
        Assert.True(small.Max() < large.Min(), $"各平台到中央入口的距离：{string.Join("/", d)}。");
        Assert.True(small.Max() < others.Min(), $"各平台到中央入口的距离：{string.Join("/", d)}。");
    }

    [Fact]
    public void 地形要素齐全()
    {
        // 规格：三种高度、至少一段一格宽的深水、至少两座预置桥、至少一段栅栏与至少一片林地；中央入口在 h=0 的广场上。
        Coord[] playable = [.. Map.AllCoords().Where(Map.IsPlayable)];
        Assert.Equal([0, 1, 2], playable.Select(Map.HeightAt).Distinct().Order());
        Assert.True(Map.TerrainData.Bridges.Count >= 2);
        Assert.All(Map.TerrainData.Bridges, b => Assert.Equal(Surface.DeepWater, Map.SurfaceAt(b)));
        Assert.NotEmpty(Map.TerrainData.Fences);
        Assert.Contains(playable, c => Map.SurfaceAt(c) == Surface.Forest);
        Assert.Equal(0, Map.HeightAt(Map.CentralEntrance));

        // "一格宽"沿用 v4 测试的可观察定义：某可落子格隔着一格未架桥深水覆盖到对岸。
        bool oneWide = playable.Any(s => Adjacency.Neighbors(Map.Width, Map.Height, s).Any(t =>
        {
            int ux = (2 * t.X) - s.X;
            int uy = (2 * t.Y) - s.Y;
            return Map.TerrainData.IsUnbridgedDeepWater(t)
                && ux >= 0 && uy >= 0 && ux < Map.Width && uy < Map.Height
                && Adjacency.CoverageTargets(Map, s).Contains(new Coord(ux, uy));
        }));
        Assert.True(oneWide, "地图上没有任何一段可隔岸覆盖的一格宽深水。");

        // 主河在中央广场处断开：北段、南段各至少一座桥（以中央入口所在行为界）。
        Assert.Contains(Map.TerrainData.Bridges, b => b.Y > Map.CentralEntrance.Y);
        Assert.Contains(Map.TerrainData.Bridges, b => b.Y < Map.CentralEntrance.Y);
    }

    [Fact]
    public void 资源布点()
    {
        // 规格 Scenario「资源布点」：信物格 16 个（平台内 9、公共 7），地图数据不含据点。
        // 变异 M-B15b（restore-go-core-rules 段 B，实跑红 71）：FrontierMapV2 的解析把 'o'（标准档公共信物）这一分支改掉 → 本测试红。
        Assert.Equal(16, Map.RelicCells.Count);
        Assert.DoesNotContain("\"Sites\"", MapFile.ToJson(Map), StringComparison.Ordinal);

        Dictionary<Coord, int> fromEntrance = Distances([Map.CentralEntrance]);

        // 信物：边长 5–6 的平台各 1、7–9 的各 2（全部出生区分区 / 出生区预算，含没人选的中立平台——裁决 9）；公共 7，其中至少 1 个高档在中央入口附近。
        for (int z = 0; z < 6; z++)
        {
            KeyValuePair<Coord, RelicCellSpec>[] inZone = [.. Map.RelicCells.Where(kv => Map.BirthZoneOf(kv.Key) == z)];
            Assert.Equal(Sides[z] <= 6 ? 1 : 2, inZone.Length);
            Assert.All(inZone, kv => Assert.Equal(new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth), kv.Value));
        }

        KeyValuePair<Coord, RelicCellSpec>[] contested = [.. Map.RelicCells.Where(kv => Map.BirthZoneOf(kv.Key) is null)];
        Assert.Equal(7, contested.Length);
        Assert.All(contested, kv => Assert.Equal(RelicZone.Contested, kv.Value.Zone));
        Assert.Contains(contested, kv => kv.Value.Budget == BudgetTier.High && fromEntrance[kv.Key] <= 2);
        Assert.All(contested, kv => Assert.True(kv.Value.Budget > BudgetTier.Birth));

        // 桥头放公共信物：每座桥至少有一个气边邻格是公共信物格。
        Assert.All(Map.TerrainData.Bridges, b =>
            Assert.Contains(Adjacency.LibertyNeighbors(Map, b), n => Map.RelicCells.TryGetValue(n, out RelicCellSpec s) && s.Zone == RelicZone.Contested));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void 保护期容量(int zoneIndex)
    {
        // 规格 Scenario：一名玩家在平台上连续三个大回合各部署 3 枚且不发生提子，每一步都仍存在合法空格（沿用 v4 的容量测试，每个平台各跑一遍）。
        GameBoard board = GameBoard.Load(Map);
        ImmutableHashSet<Coord> zone = Map.BirthZones[zoneIndex];
        for (int round = 0; round < 3; round++)
        {
            for (int stone = 0; stone < 3; stone++)
            {
                Coord? free = zone.Where(c => board[c].IsPlayableEmpty).Order().Cast<Coord?>().FirstOrDefault();
                Assert.True(free is not null, $"第 {round + 1} 大回合第 {stone + 1} 枚部署时平台已无合法空格。");
                board.Place(free!.Value, TestMaps.P0, PieceType.Basic);
            }
        }

        Assert.Equal(9, board.GroupsOf(TestMaps.P0).Sum(g => g.Size));
        Assert.Contains(zone, c => board[c].IsPlayableEmpty);
    }

    [Fact]
    public void 生成器确定且与磁盘文件一致()
    {
        // tasks 3.4：同一代码两次导出字节相同；maps/siege-frontier-v2.json 与代码一致、再读入逐字段相等（规格档仍为边疆）。
        string json = MapFile.ToJson(FrontierMapV2.Create());
        Assert.Equal(json, MapFile.ToJson(FrontierMapV2.Create()));

        string path = Path.Combine(FrontierFixtures.RepoRoot(), "maps", $"{FrontierMapV2.Id}.json");
        MapData onDisk = MapFile.FromJson(File.ReadAllText(path));
        Assert.Equal(json, MapFile.ToJson(onDisk));
        Assert.Equal(MapProfile.Frontier, onDisk.Profile);
        Assert.True(MapValidator.Validate(onDisk).IsValid, MapValidator.Validate(onDisk).ToString());
    }

    [Fact]
    public void 已收录但不是缺省地图()
    {
        // 规格 Scenario「不是缺省地图」：不带地图选项 → siege-4p-base-v5；边疆图只能按标识显式选到。
        // 变异 M-B3：MapCatalog.DefaultId 改成 FrontierMapV2.Id → 本测试红。
        Assert.Contains(FrontierMapV2.Id, MapCatalog.BuiltinIds);
        Assert.Equal(FrontierMapV2.Id, MapCatalog.Resolve("siege-frontier-v2").Id);
        Assert.NotEqual(FrontierMapV2.Id, MapCatalog.DefaultId);
        Assert.Equal(FourPlayerBaseMap.Id, MapCatalog.Resolve(null).Id);
        Assert.Equal(FourPlayerBaseMap.Id, new Siege.Sim.Config.RunConfig().MapId);
        Assert.Equal(MapFile.ToJson(FrontierMapV2.Create()), MapFile.ToJson(MapCatalog.Resolve(FrontierMapV2.Id)));
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(4, true)]
    public void 保护期边界在边疆图上照常(int majorRound, bool openMap)
    {
        // tasks 3.6：第 3 大回合落在他人平台 / 过渡带 / 中立平台被拒；第 4 大回合同一落点合法。落子规则不读规格档（裁决 4）。
        // 玩家 0–3 依次占 5、6、1、2 号台；3、4 号台没人选，是中立争夺区。
        // 变异 M-B4：MatchFlow.LegalRangeFor 的 `<=` 改 `<` → 第 3 大回合那一行红。
        MatchFlow match = MatchFlow.Create(Map, new GameSeed(5), MatchFixtures.All, MatchOptions.Immediate);
        foreach (PlayerId p in MatchFixtures.All)
        {
            match.Debug.SeedHand(p, (PieceType.Basic, 50));
        }

        int[] zones = [4, 5, 0, 1];
        match.PlantSequentially(MatchFixtures.All.Select((p, i) => (p, zones[i])));
        match.AtRound(majorRound);
        match.BeginTurn();
        match.EnterRecruit();
        var batch = match.EnterDeploy();

        Coord own = Map.BirthZones[4].Order().First();
        Coord others = Map.BirthZones[5].Order().First();      // 6 号台：别的玩家的平台
        Coord neutral = Map.BirthZones[2].Order().First();     // 3 号台：中立平台
        Coord transition = Map.CentralEntrance;                // 过渡带（中央广场）

        foreach (Coord outside in new[] { others, neutral, transition })
        {
            Siege.Core.Batch.BatchFailure? failure = batch.Stage(outside, PieceType.Basic);
            if (openMap)
            {
                Assert.Null(failure);
            }
            else
            {
                Assert.NotNull(failure);
                Assert.Equal(Siege.Core.Batch.BatchFailureKind.OutOfLegalRange, failure.Kind);
            }

            batch.Clear();
        }

        Assert.Null(batch.Stage(own, PieceType.Basic));
    }

    // ---------- 测试内独立的气边工具 ----------

    private static Dictionary<Coord, int> Distances(IEnumerable<Coord> sources)
    {
        var dist = new Dictionary<Coord, int>();
        var queue = new Queue<Coord>();
        foreach (Coord s in sources.Where(Map.IsPlayable))
        {
            dist[s] = 0;
            queue.Enqueue(s);
        }

        while (queue.Count > 0)
        {
            Coord c = queue.Dequeue();
            foreach (Coord n in Adjacency.LibertyNeighbors(Map, c))
            {
                if (dist.TryAdd(n, dist[c] + 1))
                {
                    queue.Enqueue(n);
                }
            }
        }

        return dist;
    }

    /// <summary>把一组格子按几何四邻接分成若干簇（一簇 = 一处缓坡）。</summary>
    private static List<HashSet<Coord>> GeometricClusters(HashSet<Coord> cells)
    {
        var clusters = new List<HashSet<Coord>>();
        var rest = new HashSet<Coord>(cells);
        while (rest.Count > 0)
        {
            Coord start = rest.Order().First();
            var cluster = new HashSet<Coord> { start };
            var queue = new Queue<Coord>([start]);
            rest.Remove(start);
            while (queue.Count > 0)
            {
                Coord c = queue.Dequeue();
                foreach (Coord n in rest.Where(r => Adjacency.AreAdjacent(r, c)).ToList())
                {
                    rest.Remove(n);
                    cluster.Add(n);
                    queue.Enqueue(n);
                }
            }

            clusters.Add(cluster);
        }

        return clusters;
    }
}
