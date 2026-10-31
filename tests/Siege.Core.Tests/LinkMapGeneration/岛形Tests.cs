using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Xunit.Abstractions;

namespace Siege.Core.Tests.LinkMapGeneration;

/// <summary>
/// 规格：openspec/specs/link-map-generation —— Requirement: 岛形（任务 10-31-map-coastline 裁决 1–3、5）：矩形切四角（直角边 4–7，有平台的角封顶）再沿边咬海湾（深 1–4、长 3–9），
/// 不挖内部虚空、掩码 4 邻连通；座位 + 2、中立平台 + 3、坡道朝外格不被海湾咬；非可落子格占岛面 25%–35%；只读岸线子流、同标识同岛形。
/// 种子 1–20 × 人数 2 / 3 / 4。岛面有两个口径：生成器的掩码（<see cref="LinkMapGenerator.IslandGeneration.IslandCells"/>）与测试内独立求的留白泛洪（<see cref="浮空岛布局Tests.IslandOf"/>，
/// 与 Godot 段 G 同一定义）——两者只差贴岸的零星岩石（掩码内、但与留白连通），占比按留白泛洪口径算。湖泊 / 岩区的格数、内陆、河道宽度见 浮空岛布局Tests。
/// </summary>
/// <remarks>
/// 保护区只断言代码保证的部分（map-coastline 段 F 实测）：海湾不咬保护区、平台格与坡道朝外格不被切角 / 海湾 / 湖泊 / 岩区占用。
/// 湖泊 / 岩区目前会落进座位 + 2、中立平台 + 3 的边距环（种子 1–20 每张图都有），这条是否要禁待负责人裁决，本类不断言。
/// 变异验证（段 F 实跑，过滤 岛形Tests，共 16 条用例）：记录在各方法注释里。
/// </remarks>
public class 岛形Tests(ITestOutputHelper output)
{
    private const int M = 2;

    public static IEnumerable<object[]> Players() => [[2], [3], [4]];

    private static IEnumerable<(ulong Seed, LinkMapGenerator.IslandGeneration Gen)> Generations(int players)
    {
        for (ulong seed = 1; seed <= 20; seed++)
        {
            yield return (seed, LinkMapGenerator.GenerateDetailed(seed, players));
        }
    }

    /// <summary>格到第 <paramref name="corner"/> 角（西南 0、东南 1、西北 2、东北 3）两条边的距离之和（测试内独立算式；小于该角切角即在切角三角里）。</summary>
    private static int CornerDistance(Coord c, (int W, int H) size, int corner)
    {
        int dx = corner is 0 or 2 ? c.X - M : M + size.W - 1 - c.X;
        int dy = corner is 0 or 1 ? c.Y - M : M + size.H - 1 - c.Y;
        return dx + dy;
    }

    private static bool InCornerCut(Coord c, (int W, int H) size, IReadOnlyList<int> cuts) =>
        Enumerable.Range(0, 4).Any(k => CornerDistance(c, size, k) < cuts[k]);

    private static bool InRectangle(Coord c, (int W, int H) size) => c.X >= M && c.X < M + size.W && c.Y >= M && c.Y < M + size.H;

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
    public void 非可落子格占岛面25到35(int players)
    {
        // 裁决 2：非可落子格（障碍 + 未架桥深水，含平台自带的）占岛面 30% ± 5；岛面按留白泛洪独立求。
        // 变异：生成器 TargetNonPlayableMin / Max 改 20 / 22 → 本条 ×3 红（占比降到约 20–23%）。
        var permille = new List<int>();
        foreach ((ulong seed, LinkMapGenerator.IslandGeneration gen) in Generations(players))
        {
            MapData map = gen.Map;
            HashSet<Coord> face = 浮空岛布局Tests.IslandOf(map);
            int nonPlayable = face.Count(c => !map.IsPlayable(c));
            Assert.True(nonPlayable * 100 >= 25 * face.Count && nonPlayable * 100 <= 35 * face.Count,
                $"link:{seed}:{players}：非可落子 {nonPlayable} / 岛面 {face.Count}");
            Assert.All(map.AllCoords().Where(map.IsPlayable), c => Assert.Contains(c, face));   // 可落子格都在岛面上（分子分母同一片）
            permille.Add(nonPlayable * 1000 / face.Count);
        }

        permille.Sort();
        output.WriteLine($"{players} 人 种子 1–20：非可落子占岛面 最小 {permille[0]}‰ 中位 {permille[permille.Count / 2]}‰ 最大 {permille[^1]}‰");
    }

    [Theory]
    [MemberData(nameof(Players))]
    public void 岛面四邻连通且没有内部虚空(int players)
    {
        // 裁决 1：只啃岛缘、不挖内部虚空；掩码 4 邻连通。掩码外（岛外、切角、海湾）全是障碍，且全部与留白连通（留白泛洪求出的岛面 ⊆ 掩码）；
        // 掩码内被留白泛洪划成虚空的只能是贴岸的障碍格（四邻有掩码外的格）。
        // 变异：DraftCoast 返回前在掩码中间一行挖 2 格洞 → 本条 ×3 红（同批 保护区 / 切角海湾 / 占比 一起红，共 10）。掩码不连通这一半没有单独变异：掩码断开即可落子格断开，生成器自检先抛异常。
        (int W, int H) size = LinkMapGenerator.IslandSize(players);
        foreach ((ulong seed, LinkMapGenerator.IslandGeneration gen) in Generations(players))
        {
            string at = $"link:{seed}:{players}";
            MapData map = gen.Map;
            HashSet<Coord> mask = [.. gen.IslandCells];
            HashSet<Coord> face = 浮空岛布局Tests.IslandOf(map);
            Assert.All(mask, c => Assert.True(InRectangle(c, size), $"{at}：掩码格 {c.ToNotation()} 越出外接矩形"));
            Assert.All(map.AllCoords().Where(c => !mask.Contains(c)), c => Assert.True(map.Obstacles.Contains(c), $"{at}：掩码外 {c.ToNotation()} 不是障碍"));
            Assert.True(face.IsSubsetOf(mask), $"{at}：留白泛洪的岛面越出掩码（掩码外有不与留白连通的格，即内部虚空）");
            Assert.All(mask.Where(c => !face.Contains(c)), c =>
            {
                Assert.True(map.Obstacles.Contains(c), $"{at}：{c.ToNotation()}");
                Assert.Contains(Adjacency.Neighbors(map.Width, map.Height, c), n => !mask.Contains(n));
            });

            // 掩码 4 邻连通（测试内独立泛洪）。
            var reached = new HashSet<Coord> { gen.IslandCells[0] };
            var queue = new Queue<Coord>(reached);
            while (queue.Count > 0)
            {
                foreach (Coord n in Adjacency.Neighbors(map.Width, map.Height, queue.Dequeue()))
                {
                    if (mask.Contains(n) && reached.Add(n))
                    {
                        queue.Enqueue(n);
                    }
                }
            }

            Assert.True(reached.Count == mask.Count, $"{at}：掩码分成多片（首片 {reached.Count} / {mask.Count}）");
        }
    }

    [Theory]
    [MemberData(nameof(Players))]
    public void 切角4到7且海湾从岸线向内咬(int players)
    {
        // 裁决 1：四角切角直角边 4–7；外接矩形内、切角三角之外不在掩码里的格就是海湾格——测试内按切角独立数出来，须与生成器报的海湾格数相等，
        // 且每张图至少咬掉一格（Carve 的"重咬仍不通 → 只切角"兜底在种子 1–20 上没有触发；哪天触发了本条就红，届时查岸线或换样本）；
        // 海湾格离最近的矩形边 < 4 格（深 1–4），且朝那条边一路都不在掩码里（从岸线向内连续咬，不是岛内的洞）。
        // 变异：DraftCoast 的 bays 恒 false（只切角）→ 本条 ×3 红，外加「同标识两次生成岛形逐格相同」×1 红（推断是 4 人：切角恒 4，没有海湾时种子 1 与 2 岛形相同；变异输出里用例参数被编码吃掉，未亲见）；CornerCutMin 改 3 → 本条 ×3 红。
        (int W, int H) size = LinkMapGenerator.IslandSize(players);
        var cutsSeen = new SortedSet<int>();
        foreach ((ulong seed, LinkMapGenerator.IslandGeneration gen) in Generations(players))
        {
            string at = $"link:{seed}:{players}";
            Assert.Equal(4, gen.CornerCuts.Length);
            Assert.All(gen.CornerCuts, k => Assert.InRange(k, 4, 7));
            cutsSeen.UnionWith(gen.CornerCuts);

            HashSet<Coord> mask = [.. gen.IslandCells];
            Coord[] bays = [.. Enumerable.Range(M, size.H).SelectMany(y => Enumerable.Range(M, size.W).Select(x => new Coord(x, y)))
                .Where(c => !mask.Contains(c) && !InCornerCut(c, size, gen.CornerCuts))];
            Assert.Equal(gen.BayCells, bays.Length);
            Assert.True(bays.Length > 0, $"{at}：没有海湾");
            foreach (Coord c in bays)
            {
                int[] distance = [c.Y - M, M + size.H - 1 - c.Y, c.X - M, M + size.W - 1 - c.X];   // 南、北、西、东；0 = 岸格
                int edge = Array.IndexOf(distance, distance.Min());
                Assert.True(distance[edge] < 4, $"{at}：海湾格 {c.ToNotation()} 深 {distance[edge] + 1}");
                (int dx, int dy) = edge switch { 0 => (0, -1), 1 => (0, 1), 2 => (-1, 0), _ => (1, 0) };
                for (int j = 1; j <= distance[edge]; j++)
                {
                    Assert.DoesNotContain(new Coord(c.X + (dx * j), c.Y + (dy * j)), mask);
                }
            }
        }

        output.WriteLine($"{players} 人切角取到：{string.Join("/", cutsSeen)}");
        if (players < 4)
        {
            Assert.True(cutsSeen.Max > 4, $"样本口径：{players} 人 20 张图切角只取到 {string.Join("/", cutsSeen)}");   // 没有座位的角确实在 4–7 里抽
        }
    }

    [Theory]
    [MemberData(nameof(Players))]
    public void 保护区不被海湾咬且平台与坡道口不被内陆要素占用(int players)
    {
        // 裁决 1：座位外接矩形 + 外 2 格（EdgeGap）、中立平台 + 外 3 格（PlateGap）、坡道朝外格——海湾一格不咬；切角只切到边距环的角块（座位贴边距落在矩形角，
        // 座位角格到矩形角的距离恰为 4；原固定切角 3 也切进了边距环的角块），不切平台格与坡道朝外格。平台格与坡道朝外格不被湖泊 / 岩区占用，坡道朝外格是可落子低地。
        // 变异：GuardedCells 的边距 / 间隔恒 0 → 共红 10（本条 ×3 等）；中立平台间隔环改 1 格 → 本条红（link:1:3 的保护区格 AW30–AW32 被海湾咬掉）。
        (int W, int H) size = LinkMapGenerator.IslandSize(players);
        foreach ((ulong seed, LinkMapGenerator.IslandGeneration gen) in Generations(players))
        {
            string at = $"link:{seed}:{players}";
            MapData map = gen.Map;
            HashSet<Coord> mask = [.. gen.IslandCells];
            var guarded = new HashSet<Coord>();
            foreach (BoardPlate p in map.Boards)
            {
                int gap = p.Kind == BoardPlateKind.Birth ? 2 : 3;
                for (int y = p.Origin.Y - gap; y < p.Origin.Y + p.Height + gap; y++)
                {
                    for (int x = p.Origin.X - gap; x < p.Origin.X + p.Width + gap; x++)
                    {
                        if (InRectangle(new Coord(x, y), size))
                        {
                            guarded.Add(new Coord(x, y));
                        }
                    }
                }
            }

            Coord[] outlets = [.. map.Boards.SelectMany(p => p.Cells().Where(c => map.IsPlayable(c) && map.HeightAt(c) == 1).Select(c => Outward(p, c)!.Value))];
            Assert.NotEmpty(outlets);
            Assert.All(guarded, c => Assert.True(mask.Contains(c) || InCornerCut(c, size, gen.CornerCuts), $"{at}：保护区格 {c.ToNotation()} 被海湾咬掉"));
            Coord[] kept = [.. map.Boards.SelectMany(p => p.Cells()), .. outlets];
            Assert.All(kept, c => Assert.True(mask.Contains(c), $"{at}：平台 / 坡道朝外格 {c.ToNotation()} 不在岛上"));
            Assert.All(kept, c => Assert.False(gen.Lakes.Contains(c) || gen.RockClusters.Contains(c), $"{at}：平台 / 坡道朝外格 {c.ToNotation()} 被湖泊 / 岩区占用"));
            Assert.All(outlets, c => Assert.True(map.IsPlayable(c) && map.HeightAt(c) == 0, $"{at}：坡道朝外格 {c.ToNotation()} 不是可落子低地"));
        }
    }

    [Theory]
    [MemberData(nameof(Players))]
    public void 同标识两次生成岛形逐格相同(int players)
    {
        // 裁决 5 / 规格 Requirement 岛形「确定性」：同一 (种子, 人数) 两次生成，掩码、切角、海湾格数、湖泊、岩区与整张地图逐格相同（GenerateDetailed 不缓存，两次是真生成）。
        foreach (ulong seed in new ulong[] { 1, 7, 12345 })
        {
            LinkMapGenerator.IslandGeneration a = LinkMapGenerator.GenerateDetailed(seed, players);
            LinkMapGenerator.IslandGeneration b = LinkMapGenerator.GenerateDetailed(seed, players);
            Assert.NotSame(a.Map, b.Map);
            Assert.Equal(a.IslandCells, b.IslandCells);
            Assert.Equal(a.CornerCuts, b.CornerCuts);
            Assert.Equal(a.BayCells, b.BayCells);
            Assert.True(a.Lakes.SetEquals(b.Lakes));
            Assert.True(a.RockClusters.SetEquals(b.RockClusters));
            Assert.Equal(MapFile.ToJson(a.Map), MapFile.ToJson(b.Map));
            Assert.Equal(MapFile.ToJson(LinkMapGenerator.Generate(seed, players)), MapFile.ToJson(a.Map));
        }

        // 样本口径：不同种子的岛形不同（上面的"相同"不是因为岛形恒定）。
        Assert.NotEqual(LinkMapGenerator.GenerateDetailed(1, players).IslandCells, LinkMapGenerator.GenerateDetailed(2, players).IslandCells);
    }

    [Fact]
    public void 岸线子流与布局子流新地表子流互不扰动()
    {
        // map-coastline：岸线与内陆要素只读独立子流 MapRandom.ForLinkCoast（只由地图种子与人数派生、域常量不同）——同种子可复现；与布局流、新地表流、对局各条命名子流都不同构。
        foreach (ulong seed in new ulong[] { 1, 7, 12345 })
        {
            ulong[] coast = Take(MapRandom.ForLinkCoast(seed, 4), 8);
            Assert.Equal(coast, Take(MapRandom.ForLinkCoast(seed, 4), 8));
            Assert.NotEqual(Take(MapRandom.ForLinkLayout(seed, 4), 8), coast);
            Assert.NotEqual(Take(MapRandom.ForLinkSurfaces(seed, 4), 8), coast);
            Assert.NotEqual(Take(MapRandom.ForLinkCoast(seed + 1, 4), 8), coast);
            Assert.NotEqual(Take(MapRandom.ForLinkCoast(seed, 2), 8), coast);
            var game = new GameSeed(seed);
            foreach (string name in new[] { GameSeed.RelicSpawn(4), GameSeed.Recruit, GameSeed.Setup, GameSeed.FlagPick, GameSeed.CarryAi })
            {
                Assert.NotEqual(Take(game.Stream(name), 8), coast);
            }
        }

        Assert.Equal("map-link-coast", MapRandom.ForLinkCoast(1, 4).Name);
        Assert.Contains("MapRandom.ForLinkCoast(", File.ReadAllText(Path.Combine(TestPaths.RepoRoot(), "src", "Siege.Core", "Board", "Maps", "LinkMapGenerator.cs")), StringComparison.Ordinal);
    }

    private static ulong[] Take(RandomStream stream, int count) => [.. Enumerable.Range(0, count).Select(_ => stream.NextUInt64())];
}
