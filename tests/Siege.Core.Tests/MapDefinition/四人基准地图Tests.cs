using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>规格：map-definition —— Requirement: 4 人基准地图</summary>
public class 四人基准地图Tests
{
    private static readonly MapData Map = FourPlayerBaseMap.Create();

    [Fact]
    public void 外接尺寸与可落子格()
    {
        // terrain-model 裁决 D18：v3 外接 13×13，可落子格 95–110（v2 是 11×11、80–95）。
        // scoring-sites 裁决 S-9：地形不变、加据点后 Id 升为 v4（同名不同口径被否决）。
        Assert.Equal("siege-4p-base-v4", Map.Id);
        Assert.Equal(13, Map.Width);
        Assert.Equal(13, Map.Height);
        Assert.InRange(Map.PlayableCount, 95, 110);
    }

    [Fact]
    public void 可落子格规模()
    {
        // 规格 Scenario：可落子格落在 95–110，其中四个出生区各 12–14 格且全部为 h=2。
        // design Open Question 1：每区经 2–3 格 h=1 缓坡下到 h=0，其余边缘直接落到 h=0（崖壁）——
        // 即"区外、与区内某格有气边"的格只能是 h=1 的缓坡，且恰有 2–3 格。
        Assert.InRange(Map.PlayableCount, 95, 110);
        Assert.Equal(4, Map.BirthZones.Length);
        foreach (ImmutableHashSet<Coord> zone in Map.BirthZones)
        {
            Assert.InRange(zone.Count(Map.IsPlayable), 12, 14);
            Assert.All(zone, c => Assert.Equal(2, Map.HeightAt(c)));

            Coord[] exits = [.. zone.SelectMany(c => Adjacency.LibertyNeighbors(Map, c)).Where(n => !zone.Contains(n)).Distinct()];
            Assert.InRange(exits.Length, 2, 3);
            Assert.All(exits, n => Assert.Equal(1, Map.HeightAt(n)));
        }
    }

    [Fact]
    public void 信物格分布()
    {
        // 规格 Scenario：每个出生区各 2 个，公共争夺区约 6 个，总数落在 13–15 区间。
        // C4 旋转下轨道大小只有 4（一般格）或 1（中心格），公共区 6 枚不可能：4 元轨道 + 中心格 = 5，总数 13（段 B 待决 B-1）。
        for (int i = 0; i < Map.BirthZones.Length; i++)
        {
            int inZone = Map.RelicCells.Keys.Count(c => Map.BirthZoneOf(c) == i);
            Assert.Equal(2, inZone);
        }

        Assert.Equal(5, Map.RelicCells.Count(kv => kv.Value.Zone == RelicZone.Contested));
        Assert.InRange(Map.RelicCells.Count, 13, 15);
        Assert.Equal(new RelicCellSpec(RelicZone.Contested, BudgetTier.High), Map.RelicCells[Map.CentralEntrance]);
    }

    [Fact]
    public void 出生区容量()
    {
        // 单名玩家前三大回合各部署 3 枚且不发生提子，每一步都仍存在合法空格
        GameBoard board = GameBoard.Load(Map);
        var zone = Map.BirthZones[0];

        for (int round = 0; round < 3; round++)
        {
            for (int stone = 0; stone < 3; stone++)
            {
                Coord? free = zone.Where(c => board[c].IsPlayableEmpty).Order().Cast<Coord?>().FirstOrDefault();
                Assert.True(free is not null, $"第 {round + 1} 大回合第 {stone + 1} 枚部署时出生区已无合法空格。");
                board.Place(free!.Value, TestMaps.P0, PieceType.Basic);
            }
        }

        Assert.Equal(9, board.GroupsOf(TestMaps.P0).Sum(g => g.Size));
        Assert.Contains(zone, c => board[c].IsPlayableEmpty);
    }

    [Fact]
    public void 旋转对称()
    {
        // 规格 Scenario：绕中心旋转 90° 后高度、地表、障碍、桥、栅栏、出生区（编号轮换）与信物格逐格一致。
        // MapSymmetry 是唯一的旋转比对实现；基准地图对称性Tests 另用测试内独立的旋转算式逐项复核，两边不共用代码。
        // 变异验证 M-B1：MapSymmetry.Rotate90 改成 180°（(w−1−x, w−1−y)）→ 红 2：本测试（v3 虽也 180° 对称，
        // 但 180° 下出生区 0 的像是出生区 2，编号轮换检查报出）与 基准地图对称性Tests.只满足D2的图被判不对称。
        Assert.Empty(MapSymmetry.RotationDefects(Map));
        Assert.True(MapSymmetry.IsC4Symmetric(Map));
    }

    [Fact]
    public void 地形要素齐全()
    {
        // 规格 Scenario：三种高度、一格宽深水、预置桥、栅栏与林地各至少出现一次。
        Coord[] playable = [.. Map.AllCoords().Where(Map.IsPlayable)];
        Assert.Equal([0, 1, 2], playable.Select(Map.HeightAt).Distinct().Order());
        Assert.Contains(Map.AllCoords(), Map.TerrainData.IsUnbridgedDeepWater);
        Assert.NotEmpty(Map.TerrainData.Bridges);
        Assert.NotEmpty(Map.TerrainData.Fences);
        Assert.Contains(playable, c => Map.SurfaceAt(c) == Surface.Forest);

        // "一格宽"按 terrain 规格 E4 的可观察定义：某可落子格 s 的几何邻居 t 是未架桥深水，且 s 的覆盖关系落到对岸 u = 2t − s。
        // 宽河（u 仍是深水）不满足；所以只要有一处成立，就存在一段一格宽的深水。
        bool oneWide = false;
        foreach (Coord s in playable)
        {
            foreach (Coord t in Adjacency.Neighbors(Map.Width, Map.Height, s))
            {
                int ux = (2 * t.X) - s.X;
                int uy = (2 * t.Y) - s.Y;
                if (Map.TerrainData.IsUnbridgedDeepWater(t)
                    && ux >= 0 && uy >= 0 && ux < Map.Width && uy < Map.Height
                    && Adjacency.CoverageTargets(Map, s).Contains(new Coord(ux, uy)))
                {
                    oneWide = true;
                }
            }
        }

        Assert.True(oneWide, "地图上没有任何一段可隔岸覆盖的一格宽深水。");

        // 规格 Requirement："中央区域为 h=0 低地并含深水"。中央入口在 h=0；以中心为界、切比雪夫距离 ≤ 2 的岛内可落子格全 h=0；
        // 距离 ≤ 3 处存在未架桥深水（护城河）。check 变异 N-4：生成器把 G7 抬到 h=1 → 只有 `磁盘上的基准地图文件与代码一致` 红
        // （通用漂移守门，说不出违反了哪条约束），补本段后这里也红。
        Coord center = new(Map.Width / 2, Map.Height / 2);
        static int Chebyshev(Coord a, Coord b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        Assert.Equal(center, Map.CentralEntrance);
        Assert.Equal(0, Map.HeightAt(Map.CentralEntrance));
        Coord[] island = [.. playable.Where(c => Chebyshev(c, center) <= 2)];
        Assert.NotEmpty(island);
        Assert.All(island, c => Assert.Equal(0, Map.HeightAt(c)));
        Assert.Contains(Map.AllCoords(), c => Chebyshev(c, center) <= 3 && Map.TerrainData.IsUnbridgedDeepWater(c));
    }

    [Fact]
    public void 据点布点()
    {
        // 规格 Scenario（scoring-sites）：共 12 个——每个出生区内 1 个营帐；每个出生区的河外低地里 1 个篝火，且与另一出生区 h=2 格几何相邻；
        // 岛上 4 个非林地石碑；无一与信物格重合。"河外低地""岛"都按规格定义在测试里从地图推出来，不抄坐标。
        Assert.Equal(12, Map.Sites.Count);
        Assert.DoesNotContain(Map.Sites.Keys, Map.RelicCells.ContainsKey);
        Assert.All(Map.Sites.Keys, c => Assert.True(Map.IsPlayable(c), $"据点 {c} 不可落子。"));

        Coord[] Tier(SiteTier t) => [.. Map.Sites.Where(kv => kv.Value == t).Select(kv => kv.Key).Order()];

        // 营帐：每区恰 1 个，h=2、非信物。
        Coord[] tents = Tier(SiteTier.Tent);
        Assert.Equal(4, tents.Length);
        for (int z = 0; z < 4; z++)
        {
            Coord tent = Assert.Single(tents, c => Map.BirthZones[z].Contains(c));
            Assert.Equal(2, Map.HeightAt(tent));
        }

        // 篝火：出生区 z 的河外低地 = 从该区出发、不经桥格、沿气边可达的 h=0 格。
        // 满足"与另一出生区 h=2 格几何相邻"的候选集合在 v3 地形上恰是 J2 轨道（design D-C），篝火必须正好是它。
        Coord[] campfires = Tier(SiteTier.Campfire);
        var candidates = new HashSet<Coord>();
        for (int z = 0; z < 4; z++)
        {
            HashSet<Coord> lowland = [.. ReachableWithoutBridges(Map.BirthZones[z]).Where(c => Map.HeightAt(c) == 0)];
            Coord[] mine = [.. lowland.Where(c => Adjacency.Neighbors(Map.Width, Map.Height, c)
                .Any(n => Map.HeightAt(n) == 2 && Map.BirthZoneOf(n) is { } other && other != z))];
            candidates.UnionWith(mine);

            Coord fire = Assert.Single(campfires, lowland.Contains);
            Assert.Contains(fire, mine);
        }

        string[] campfireOrbit = ["B5", "J2", "E12", "M9"];
        Assert.Equal(campfireOrbit.Select(Coord.Parse).Order(), candidates.Order());
        Assert.Equal(campfireOrbit.Select(Coord.Parse).Order(), campfires);

        // 石碑：岛 = 从中央入口出发、不经桥格沿气边可达的格，且任何出生区不经桥到不了。
        HashSet<Coord> island = [.. ReachableWithoutBridges([Map.CentralEntrance])];
        Assert.All(Map.BirthZones, zone => Assert.Empty(ReachableWithoutBridges(zone).Intersect(island)));
        Assert.All(island, c => Assert.Equal(0, Map.HeightAt(c)));

        Coord[] steles = Tier(SiteTier.Stele);
        Assert.Equal(4, steles.Length);
        Assert.All(steles, c =>
        {
            Assert.Contains(c, island);
            Assert.NotEqual(Surface.Forest, Map.SurfaceAt(c));
        });
    }

    [Fact]
    public void 营帐只有本区高台能覆盖()
    {
        // design D-C："邻家在崖下仰视覆盖不到，事实上只有主人能控制"——营帐不选缓坡旁的 D2 / D3（h=1 缓坡能覆盖）。
        foreach (Coord tent in Map.Sites.Where(kv => kv.Value == SiteTier.Tent).Select(kv => kv.Key))
        {
            int zone = Map.BirthZoneOf(tent)!.Value;
            Coord[] coverers = [.. Map.AllCoords().Where(s => Adjacency.CoverageTargets(Map, s).Contains(tent))];
            Assert.NotEmpty(coverers);
            Assert.All(coverers, s => Assert.Equal(zone, Map.BirthZoneOf(s)));
        }
    }

    [Fact]
    public void 篝火可被邻家居高覆盖()
    {
        // 规格 Scenario：在篝火旁、与之几何相邻的邻家 h=2 高台格上放一枚邻家棋子 → 篝火在其覆盖目标中；
        // 而篝火所在低地主人的任一 h=0 棋子都不能覆盖那格高台。用真实 CoverageTargets，不手算。
        foreach (Coord fire in Map.Sites.Where(kv => kv.Value == SiteTier.Campfire).Select(kv => kv.Key))
        {
            Coord[] cliffs = [.. Adjacency.Neighbors(Map.Width, Map.Height, fire)
                .Where(n => Map.HeightAt(n) == 2 && Map.BirthZoneOf(n) is not null)];
            Coord cliff = Assert.Single(cliffs);

            Assert.Contains(fire, Adjacency.CoverageTargets(Map, cliff));
            Assert.All(
                Map.AllCoords().Where(s => Map.IsPlayable(s) && Map.HeightAt(s) == 0),
                s => Assert.DoesNotContain(cliff, Adjacency.CoverageTargets(Map, s)));
        }

        // 钉住设计文档点名的那一对：K2（出生区 1）→ J2（出生区 0 的尾巷尽头）。
        Assert.Contains(Coord.Parse("J2"), Adjacency.CoverageTargets(Map, Coord.Parse("K2")));
        Assert.Equal(1, Map.BirthZoneOf(Coord.Parse("K2")));
    }

    [Fact]
    public void 保护期内篝火归邻家()
    {
        // 规格 Scenario：第 1 大回合邻家在 K2 落子，J2 的主人（出生区 0）只有区内棋子、低地无子 → J2 被邻家唯一覆盖。
        // 本段只断言覆盖归属（CoverageMap 唯一覆盖），据点分值属于段 A2。
        GameBoard board = GameBoard.Load(Map);
        board.Place(Coord.Parse("D2"), TestMaps.P0, PieceType.Basic);
        board.Place(Coord.Parse("D3"), TestMaps.P0, PieceType.Basic);
        board.Place(Coord.Parse("K2"), TestMaps.P1, PieceType.Basic);

        var coverage = Siege.Core.Scoring.CoverageMap.Compute(board);
        Coord j2 = Coord.Parse("J2");
        Assert.Equal(SiteTier.Campfire, Map.Sites[j2]);

        Assert.Equal(TestMaps.P1, coverage.UniqueCoverer(j2));
        Assert.Equal(new Siege.Core.Scoring.CellOwnership(Siege.Core.Scoring.OwnershipKind.Exclusive, TestMaps.P1), coverage.OwnershipOf(j2));
        Assert.Equal(1, coverage.CoverageOf(j2).CovererCount);
        Assert.Equal([new Siege.Core.Scoring.CoverageSource(Coord.Parse("K2"), Adjacent: true)], coverage.SourcesOf(j2));

        // 主人的区内棋子确实都在高台、低地无子；对照：没有 K2 这枚邻家棋子时 J2 无人覆盖——唯一覆盖来自 K2，不是别的棋子顺带。
        Assert.All(new[] { "D2", "D3" }, s => Assert.Equal((0, 2), (Map.BirthZoneOf(Coord.Parse(s))!.Value, Map.HeightAt(Coord.Parse(s)))));
        GameBoard ownerOnly = GameBoard.Load(Map);
        ownerOnly.Place(Coord.Parse("D2"), TestMaps.P0, PieceType.Basic);
        ownerOnly.Place(Coord.Parse("D3"), TestMaps.P0, PieceType.Basic);
        Assert.Equal(0, Siege.Core.Scoring.CoverageMap.Compute(ownerOnly).CoverageOf(j2).CovererCount);
    }

    [Fact]
    public void v4地形与v3文件逐格相同()
    {
        // scoring-sites 裁决 S-9：v4 = v3 地形 + 据点。拿磁盘上的 v3 历史文件补上 v4 的 Id 与据点，序列化后必须与 v4 逐字节相同。
        string path = Path.Combine(RepoRoot(), "maps", "siege-4p-base-v3.json");
        MapData v3 = MapFile.FromJson(File.ReadAllText(path));

        // 第一条腿：逐格比对活对象的地形查询。只比序列化文本时，ToJson 漏写某个地形字段会让两边同漏、断言恒真（testing.md「逐字节相等抓不到漏字段」）。
        Assert.Equal((Map.Width, Map.Height), (v3.Width, v3.Height));
        int cells = 0;
        foreach (Coord c in Map.AllCoords())
        {
            Assert.Equal(Map.HeightAt(c), v3.HeightAt(c));
            Assert.Equal(Map.SurfaceAt(c), v3.SurfaceAt(c));
            Assert.Equal(Map.TerrainAt(c), v3.TerrainAt(c));
            Assert.Equal(Map.HasBridge(c), v3.HasBridge(c));
            Assert.Equal(Map.BirthZoneOf(c), v3.BirthZoneOf(c));
            cells++;
        }

        Assert.Equal(13 * 13, cells);
        Assert.Equal(Map.Obstacles.Order(), v3.Obstacles.Order());
        Assert.Equal(Map.TerrainData.Fences.OrderBy(e => e.A).ThenBy(e => e.B), v3.TerrainData.Fences.OrderBy(e => e.A).ThenBy(e => e.B));
        Assert.Equal(Map.RelicCells.OrderBy(kv => kv.Key), v3.RelicCells.OrderBy(kv => kv.Key));
        Assert.Equal(Map.ChokePoints.Order(), v3.ChokePoints.Order());
        Assert.Equal(Map.CentralEntrance, v3.CentralEntrance);
        Assert.Empty(v3.Sites);

        // 第二条腿：补上 v4 的 Id 与据点后序列化逐字节相同，兜住逐格断言未列出的字段（容差、口袋豁免等）。
        Assert.Equal(MapFile.ToJson(Map), MapFile.ToJson(v3 with { Id = Map.Id, Sites = Map.Sites }));
    }

    /// <summary>从 <paramref name="sources"/> 出发、沿气边、不踏上桥格可达的格（含起点）。</summary>
    private static HashSet<Coord> ReachableWithoutBridges(IEnumerable<Coord> sources)
    {
        var seen = new HashSet<Coord>(sources);
        var queue = new Queue<Coord>(seen);
        while (queue.Count > 0)
        {
            Coord c = queue.Dequeue();
            foreach (Coord n in Adjacency.LibertyNeighbors(Map, c))
            {
                if (!Map.HasBridge(n) && seen.Add(n))
                {
                    queue.Enqueue(n);
                }
            }
        }

        return seen;
    }

    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "siege.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("找不到仓库根目录（siege.sln）。");
    }

    [Fact]
    public void 中央区预算严格高于出生区()
    {
        // 设计文档 §8.2：中央、咽喉与高风险边缘承担更高的信物强度预算
        BudgetTier maxBirth = Map.RelicCells.Values
            .Where(s => s.Zone == RelicZone.BirthZone).Max(s => s.Budget);
        BudgetTier minContested = Map.RelicCells.Values
            .Where(s => s.Zone == RelicZone.Contested).Min(s => s.Budget);

        Assert.True(minContested > maxBirth, "公共争夺区的预算档位必须严格高于出生区。");
        Assert.Contains(Map.RelicCells.Values, s => s.Budget == BudgetTier.High);
    }

    [Fact]
    public void 出生区不含高阶预算信物()
    {
        foreach (RelicCellSpec spec in Map.RelicCells
                     .Where(kv => Map.BirthZoneOf(kv.Key) is not null).Select(kv => kv.Value))
        {
            Assert.Equal(BudgetTier.Birth, spec.Budget);
        }
    }
}
