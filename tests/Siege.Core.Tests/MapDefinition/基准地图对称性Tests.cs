using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>
/// 基准地图的独立验证：这里的每一项都**自己算**，不调用 <see cref="MapValidator"/> 与 <see cref="MapSymmetry"/>。
/// </summary>
/// <remarks>
/// 距离容差被定为 1（裁决记录第 1 条），v3 基准图靠 C4 旋转对称精确满足它（terrain-model 裁决 D19）。
/// 如果对称被后续改动破坏，MapValidator 会报 DISTANCE_IMBALANCE——但那是它自己的 BFS 说的。
/// 这里用独立实现的沿气边 BFS（|Δh| ≤ 1、非未架桥深水、无栅栏）复算一遍，避免"校验器和被校验对象一起错"。
/// </remarks>
public class 基准地图对称性Tests
{
    private static readonly MapData Map = FourPlayerBaseMap.Create();
    private const int Size = 13;

    /// <summary>测试内独立实现的四方向偏移，不复用 <see cref="Adjacency"/>。</summary>
    private static readonly (int Dx, int Dy)[] Directions = [(0, -1), (-1, 0), (1, 0), (0, 1)];

    /// <summary>测试内独立实现的绕中心 90° 旋转：<c>(x, y) → (Size − 1 − y, x)</c>，不复用 <see cref="MapSymmetry"/>。</summary>
    private static Coord Rotate90(Coord c) => new(Size - 1 - c.Y, c.X);

    [Fact]
    public void 障碍集合在C4旋转下不变()
    {
        Assert.Equal(Map.Obstacles.OrderBy(c => c), Map.Obstacles.Select(Rotate90).OrderBy(c => c));
    }

    [Fact]
    public void 地形在C4旋转下逐格不变()
    {
        // 高度、地表、桥逐格比；栅栏按边比（两端一起旋转）。
        foreach (Coord c in Map.AllCoords())
        {
            Coord image = Rotate90(c);
            Assert.Equal(Map.HeightAt(c), Map.HeightAt(image));
            Assert.Equal(Map.SurfaceAt(c), Map.SurfaceAt(image));
            Assert.Equal(Map.HasBridge(c), Map.HasBridge(image));
        }

        foreach (FenceEdge fence in Map.TerrainData.Fences)
        {
            Assert.True(Map.HasFence(Rotate90(fence.A), Rotate90(fence.B)), $"栅栏 {fence} 的像不是栅栏。");
        }

        Assert.Equal(4, Map.TerrainData.Fences.Count);
        Assert.Equal(4, Map.TerrainData.Bridges.Count);
    }

    [Fact]
    public void 信物格与分区在C4旋转下不变()
    {
        foreach ((Coord c, RelicCellSpec spec) in Map.RelicCells)
        {
            Coord image = Rotate90(c);
            Assert.True(Map.RelicCells.ContainsKey(image), $"{c} 的像 {image} 不是信物格。");
            Assert.Equal(spec, Map.RelicCells[image]);
        }
    }

    [Fact]
    public void 咽喉与中央入口在C4旋转下不变()
    {
        Assert.Equal(Map.ChokePoints.OrderBy(c => c), Map.ChokePoints.Select(Rotate90).OrderBy(c => c));
        Assert.Equal(Map.CentralEntrance, Rotate90(Map.CentralEntrance));
    }

    [Fact]
    public void 出生区在C4旋转下编号轮换()
    {
        // 规格："出生区（编号轮换）"：出生区 i 的像恰是出生区 (i + 1) mod 4，不只是"像是某个区"。
        var zones = Map.BirthZones.Select(z => z.OrderBy(c => c).ToImmutableArray()).ToList();

        for (int i = 0; i < zones.Count; i++)
        {
            var image = zones[i].Select(Rotate90).OrderBy(c => c).ToImmutableArray();
            Assert.Equal(zones[(i + 1) % zones.Count], image);
            Assert.False(zones[i].SequenceEqual(image), $"出生区 {i} 旋转后与自身重合。");
        }
    }

    [Fact]
    public void 四个出生区到三类地标的距离精确相等()
    {
        ImmutableArray<Coord> contestedRelics = Map.RelicCells
            .Where(kv => kv.Value.Zone == RelicZone.Contested)
            .Select(kv => kv.Key).ToImmutableArray();

        var metrics = new (string Name, ImmutableArray<Coord> Targets)[]
        {
            ("最近公共信物", contestedRelics),
            ("中央入口", [Map.CentralEntrance]),
            ("最近咽喉", Map.ChokePoints.ToImmutableArray()),
        };

        // 沿气边最短距离的实测值（terrain-model v3）：桥头公共信物 5、中央入口 7、咽喉（桥）4。
        // 只断言"四区相等"抓不到"四区一起变远"——改图最容易犯的正是这个错。
        int[] expected = [5, 7, 4];

        foreach (((string name, ImmutableArray<Coord> targets), int want) in metrics.Zip(expected))
        {
            int[] perZone = Map.BirthZones
                .Select(zone =>
                {
                    Dictionary<Coord, int> dist = Bfs(zone);
                    return targets.Where(dist.ContainsKey).Select(t => dist[t]).Min();
                })
                .ToArray();

            Assert.Equal(perZone.Min(), perZone.Max());
            Assert.True(perZone.Max() - perZone.Min() <= Map.DistanceTolerance,
                $"到{name}的距离极差 {perZone.Max() - perZone.Min()} 超出容差 {Map.DistanceTolerance}。");
            Assert.Equal([want, want, want, want], perZone);
        }
    }

    [Fact]
    public void 全盘可落子格连通且无小口袋()
    {
        // 沿气边连通：崖脚下若留出被高台包死的孤格（校验器不报——它不触及出生区），这里会红，逼着把它做成岩石或水。
        var playable = Map.AllCoords().Where(Playable).ToImmutableHashSet();
        Dictionary<Coord, int> reached = Bfs([playable.OrderBy(c => c).First()]);

        Assert.Equal(playable.Count, reached.Count);

        // terrain-model v3：13×13 = 169 格，岩石 36、深水 32（其中桥 4），可落子 105。
        Assert.Equal(105, playable.Count);
        Assert.Equal(36, Map.Obstacles.Count);
        Assert.Equal(28, Map.AllCoords().Count(Map.TerrainData.IsUnbridgedDeepWater));
    }

    [Fact]
    public void 校验输出跨次运行确定()
    {
        // ImmutableHashSet / ImmutableDictionary 的枚举顺序不保证稳定，
        // 任何直接枚举而未先 Order() 的地方都会在这里暴露。
        string first = Describe(MapValidator.Validate(Map with { ChokePoints = [Coord.Parse("G4")] }));

        for (int i = 0; i < 50; i++)
        {
            Assert.Equal(first, Describe(MapValidator.Validate(Map with { ChokePoints = [Coord.Parse("G4")] })));
        }
    }

    [Fact]
    public void 只满足D2的图被判不对称()
    {
        // 3.3 的验证：一条横穿中行的障碍条在水平镜像、垂直镜像、180° 旋转下都不变（D2），转 90° 变成竖条（非 C4）。
        // 变异验证 M-B1：MapSymmetry.Rotate90 改成 180° 旋转 → 这张图被放过，本测试红。
        Coord[] horizontal = [.. Enumerable.Range(1, 7).Select(x => new Coord(x, 4))];
        // 合成图的咽喉缺省在 A1，本身就不对称；把它放到中心，让障碍条成为这张图唯一的不对称来源。
        MapData bar = TestMaps.Synthetic(size: 9, maxPlayers: 4, obstacles: horizontal) with { ChokePoints = [new Coord(4, 4)] };

        static Coord MirrorX(Coord c) => new(8 - c.X, c.Y);
        static Coord MirrorY(Coord c) => new(c.X, 8 - c.Y);
        static Coord Rotate180(Coord c) => new(8 - c.X, 8 - c.Y);
        foreach (Func<Coord, Coord> t in new Func<Coord, Coord>[] { MirrorX, MirrorY, Rotate180 })
        {
            Assert.Equal(bar.Obstacles, bar.Obstacles.Select(t).ToImmutableHashSet());
        }

        Assert.NotEmpty(MapSymmetry.RotationDefects(bar));
        Assert.All(MapSymmetry.RotationDefects(bar), d => Assert.Contains("障碍", d, StringComparison.Ordinal));

        // 补上竖条变成十字，才是 C4 对称
        Coord[] vertical = [.. Enumerable.Range(1, 7).Select(y => new Coord(4, y))];
        MapData cross = bar with { Obstacles = bar.Obstacles.Union(vertical) };
        Assert.Empty(MapSymmetry.RotationDefects(cross));
    }

    [Fact]
    public void 距离报告项只剩三项且与独立BFS一致()
    {
        // restore-go-core-rules：距离均衡目标由五项改回三项（公共信物 / 中央入口 / 咽喉），另两项随其目标概念一并退役。
        // 校验器与 map 子命令共用的 DistanceTable 必须与独立 BFS 逐项一致，且取的是"最近"目标。
        // 只断言四区相等挡不住"最近"写成"最远"（C4 下最远也四区相等）——A1 检查变异 M-C2 在补这段前全绿。
        // 变异验证 M-B14（段 B，实跑红 7）：DistanceTable 把"最近咽喉"也删掉（只剩两项）→ 本测试红。
        (string Name, Coord[] Targets)[] expected =
        [
            ("最近公共信物", [.. Map.RelicCells.Where(kv => kv.Value.Zone == RelicZone.Contested).Select(kv => kv.Key)]),
            ("中央入口", [Map.CentralEntrance]),
            ("最近咽喉", [.. Map.ChokePoints]),
        ];
        ImmutableArray<BirthZoneDistance> table = MapValidator.DistanceTable(Map);
        Assert.Equal(3, table.Length);
        Assert.Equal(expected.Select(e => e.Name), table.Select(m => m.Name));
        for (int m = 0; m < expected.Length; m++)
        {
            int?[] independent = [.. Map.BirthZones.Select(zone =>
            {
                Dictionary<Coord, int> dist = Bfs(zone);
                return (int?)expected[m].Targets.Where(dist.ContainsKey).Select(t => dist[t]).Min();
            })];
            Assert.Equal(independent, table[m].Distances);
        }
    }

    [Fact]
    public void 出生区编号不轮换的图被判不对称()
    {
        // 四个区的形状都对，但编号顺序 0 → 2 → 1 → 3 不是旋转顺序，MUST 判不对称。
        MapData swapped = Map with { BirthZones = [Map.BirthZones[0], Map.BirthZones[2], Map.BirthZones[1], Map.BirthZones[3]] };

        ImmutableArray<string> defects = MapSymmetry.RotationDefects(swapped);

        Assert.NotEmpty(defects);
        Assert.All(defects, d => Assert.Contains("出生区", d, StringComparison.Ordinal));
        // S-14：报文里的出生区编号对人显示 1–4（内部索引 1 → 显示 2 等），与 map 文本图一致；不得出现 0 起编号。
        Assert.Contains(defects, d => d.Contains("出生区 4", StringComparison.Ordinal));
        Assert.DoesNotContain(defects, d => d.Contains("出生区 0", StringComparison.Ordinal));
    }

    [Fact]
    public void 每项属性都参与旋转比对()
    {
        // 逐项破坏 G3（h=0 土路，像是 L7）与 G4（桥），每一项都必须被指名报出——少比一项就是假守门。
        TerrainData t = Map.TerrainData;
        Coord g3 = Coord.Parse("G3");
        var cases = new (string Name, MapData Broken)[]
        {
            ("障碍", Map with { Obstacles = Map.Obstacles.Add(g3) }),
            ("高度", Map with { TerrainData = new TerrainData(t.Heights.SetItem(g3, 1), t.Surfaces, t.Bridges, t.Fences) }),
            ("地表", Map with { TerrainData = new TerrainData(t.Heights, t.Surfaces.SetItem(g3, Surface.Forest), t.Bridges, t.Fences) }),
            ("桥", Map with { TerrainData = new TerrainData(t.Heights, t.Surfaces, t.Bridges.Remove(Coord.Parse("G4")), t.Fences) }),
            ("栅栏", Map with { TerrainData = new TerrainData(t.Heights, t.Surfaces, t.Bridges, t.Fences.Add(new FenceEdge(g3, Coord.Parse("G2")))) }),
            ("信物", Map with { RelicCells = Map.RelicCells.Add(g3, new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard)) }),
            ("咽喉", Map with { ChokePoints = Map.ChokePoints.Add(g3) }),
            ("中央入口", Map with { CentralEntrance = g3 }),
        };

        foreach ((string name, MapData broken) in cases)
        {
            ImmutableArray<string> defects = MapSymmetry.RotationDefects(broken);
            Assert.True(defects.Length > 0, $"破坏{name}后未被判不对称。");
            Assert.Contains(defects, d => d.Contains(name, StringComparison.Ordinal));
        }
    }

    private static string Describe(MapValidationResult result) =>
        string.Join("|", result.Failures.Select(f => f.ToString()));

    /// <summary>测试内独立实现的可落子判定：盘内、非障碍、非未架桥深水。</summary>
    private static bool Playable(Coord c) =>
        c.X >= 0 && c.X < Size && c.Y >= 0 && c.Y < Size
        && !Map.Obstacles.Contains(c)
        && !(Map.SurfaceAt(c) == Surface.DeepWater && !Map.HasBridge(c));

    /// <summary>测试内独立实现的多源 BFS，沿气边：四邻接、两端可落子、|Δh| ≤ 1、无栅栏。</summary>
    private static Dictionary<Coord, int> Bfs(IEnumerable<Coord> sources)
    {
        var dist = new Dictionary<Coord, int>();
        var queue = new Queue<Coord>();
        foreach (Coord s in sources.OrderBy(c => c))
        {
            if (Playable(s))
            {
                dist[s] = 0;
                queue.Enqueue(s);
            }
        }

        while (queue.Count > 0)
        {
            Coord current = queue.Dequeue();
            foreach ((int dx, int dy) in Directions)
            {
                int nx = current.X + dx;
                int ny = current.Y + dy;
                if (nx < 0 || nx >= Size || ny < 0 || ny >= Size)
                {
                    continue;
                }

                Coord n = new(nx, ny);
                if (!Playable(n) || Math.Abs(Map.HeightAt(n) - Map.HeightAt(current)) > 1 || Map.HasFence(current, n))
                {
                    continue;
                }

                if (!dist.ContainsKey(n))
                {
                    dist[n] = dist[current] + 1;
                    queue.Enqueue(n);
                }
            }
        }

        return dist;
    }
}
