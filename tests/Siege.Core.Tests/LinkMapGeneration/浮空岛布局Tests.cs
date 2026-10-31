using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Xunit.Abstractions;

namespace Siege.Core.Tests.LinkMapGeneration;

/// <summary>
/// 规格：任务 10-26-map-expand-25 design D2–D5（多平台浮空岛）/ prd 验收标准：岛尺寸、平台数与尺寸、平台高度与坡道、低地河道与桥、岛缘点缀、全图气边连通、不放静态信物格（段 A3）；
/// 任务 10-28-seat-gap-25 design E1–E3 / prd 验收标准：长方形岛 45×33 / 45×45、恰 n 个 8×8 出生座位且两两间距 ≥ 25、中立平台数 2–3 / 3–4 / 4–5、中央入口；
/// 任务 10-31-map-coastline 裁决 1–3：岛形改为不规则掩码（岛面 = 画布内不与留白连通的格，测试内用留白泛洪独立求出，与 Godot 段 G 同一定义）、河道 1–2 格宽、湖泊 4–9 格、岩区 3–6 格。
/// 种子 1–20 × 人数 2 / 3 / 4，全部判定在测试内独立算（岛范围、座位位置、间距、坡道朝外格、气边连通都不读生成器的私有实现）。岛形本身（切角、海湾、保护区、占比）见 岛形Tests。
/// </summary>
/// <remarks>
/// 变异验证（map-expand-25 段 A1 实跑，过滤 LinkMapGeneration 命名空间）：
/// M2 校验器 PlatesApart 间隔改 gap−1（生成器落位共用）→「平台数与尺寸」×3 红；M6 LibertySplit 恒空（生成器低地自检共用）→「全图可落子格沿气边连通」×3 红；
/// M10 每条河道只放 1 座桥 →「低地有N条河道各两座桥」×3 红。另 M3 MapRandom 派生去掉人数 → 链接档地图标识与确定性Tests「不同人数不同图」「新地表子流与布局子流互不扰动」红 2；
/// M4 LinkMapId.Parse 去掉旧两段格式分支 →「标识解析与规范化」「目录解析link标识」红 2。
/// </remarks>
public class 浮空岛布局Tests(ITestOutputHelper output)
{
    /// <summary>岛的宽 × 高（seat-gap-25 裁决 S3 / E1：2 人 45×33 东西对置，3 / 4 人 45×45；原 map-expand-25 的正方形 30 / 35 / 40 作废）。</summary>
    private static readonly IReadOnlyDictionary<int, (int W, int H)> Size = new Dictionary<int, (int, int)> { [2] = (45, 33), [3] = (45, 45), [4] = (45, 45) };

    /// <summary>中立平台数区间（裁决 S4：2–3 / 3–4 / 4–5；原 map-expand-25 R2 的"全部平台 6–8 / 8–10 / 10–12 块"作废）。</summary>
    private static readonly IReadOnlyDictionary<int, (int Min, int Max)> NeutralRange = new Dictionary<int, (int, int)> { [2] = (2, 3), [3] = (3, 4), [4] = (4, 5) };

    private const int M = 2;

    public static IEnumerable<object[]> Players() => [[2], [3], [4]];

    private static IEnumerable<(ulong Seed, MapData Map)> Maps(int players)
    {
        for (ulong seed = 1; seed <= 20; seed++)
        {
            yield return (seed, LinkMapGenerator.Generate(seed, players));
        }
    }

    /// <summary>
    /// 岛面（map-coastline 裁决 1 / 7，测试内独立实现）：从画布四边的障碍格出发沿几何四邻在障碍格里泛洪，泛到的是虚空（岛外、切角、海湾、贴岸的零星岩石）；其余格是岛上的格。
    /// 按地图实例记忆，避免每条用例重算。
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MapData, HashSet<Coord>> IslandMemo = new();

    internal static HashSet<Coord> IslandOf(MapData map) => IslandMemo.GetValue(map, static m =>
    {
        var voidCells = new HashSet<Coord>();
        var queue = new Queue<Coord>();
        foreach (Coord c in m.AllCoords().Where(c => (c.X == 0 || c.Y == 0 || c.X == m.Width - 1 || c.Y == m.Height - 1) && m.Obstacles.Contains(c)))
        {
            voidCells.Add(c);
            queue.Enqueue(c);
        }

        while (queue.Count > 0)
        {
            foreach (Coord n in Adjacency.Neighbors(m.Width, m.Height, queue.Dequeue()))
            {
                if (m.Obstacles.Contains(n) && voidCells.Add(n))
                {
                    queue.Enqueue(n);
                }
            }
        }

        return [.. m.AllCoords().Where(c => !voidCells.Contains(c))];
    });

    private static bool OnIsland(Coord c, MapData map) => IslandOf(map).Contains(c);

    /// <summary>岛缘一圈：岛上，且东南西北至少一个方向的下一格不在岛上。</summary>
    private static bool OnIslandEdge(Coord c, MapData map) =>
        OnIsland(c, map) && Adjacency.Neighbors(map.Width, map.Height, c).Any(n => !OnIsland(n, map));

    /// <summary>两矩形之间相隔的格数，取横纵较大者（裁决 S1 的间距口径，测试内独立算式；重叠方向为负）。</summary>
    private static int Gap(BoardPlate p, BoardPlate q) =>
        Math.Max(
            Math.Max(q.Origin.X - (p.Origin.X + p.Width), p.Origin.X - (q.Origin.X + q.Width)),
            Math.Max(q.Origin.Y - (p.Origin.Y + p.Height), p.Origin.Y - (q.Origin.Y + q.Height)));

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
    public void 岛外接尺寸按人数(int players)
    {
        // seat-gap-25 prd 验收：岛外接尺寸 2 人 45×33，3 / 4 人 45×45（不含留白）；design E1：画布 (W+2M)×(H+2M)。
        // map-coastline 裁决 1：岛形改为掩码，但画布与可落子格外接矩形不变——岛面全部落在 [M, M+W)×[M, M+H) 内，留白一圈仍是障碍。
        (int w, int h) = Size[players];
        Assert.Equal((w, h), LinkMapGenerator.IslandSize(players));
        foreach ((ulong seed, MapData map) in Maps(players))
        {
            string at = $"link:{seed}:{players}";
            Assert.Equal(w + (2 * M), map.Width);
            Assert.Equal(h + (2 * M), map.Height);
            Coord[] playable = [.. map.AllCoords().Where(map.IsPlayable)];
            Assert.Equal(w, playable.Max(c => c.X) - playable.Min(c => c.X) + 1);
            Assert.Equal(h, playable.Max(c => c.Y) - playable.Min(c => c.Y) + 1);
            Assert.All(IslandOf(map), c => Assert.True(c.X >= M && c.X < M + w && c.Y >= M && c.Y < M + h, $"{at}：岛面 {c.ToNotation()} 越出外接矩形"));
            Assert.All(map.AllCoords().Where(c => c.X < M || c.Y < M || c.X >= M + w || c.Y >= M + h), c => Assert.True(map.Obstacles.Contains(c), $"{at}：留白 {c.ToNotation()} 不是障碍"));
            Assert.Equal(players, map.MaxPlayers);
            Assert.Equal($"link:{seed}:{players}", map.Id);
        }
    }

    [Theory]
    [MemberData(nameof(Players))]
    public void 恰N个出生座位且两两间距不小于25(int players)
    {
        // 裁决 S1 / S2 / design E1：恰 n 个 8×8 出生座位，距岛边 2，位置只由人数决定（2 人西东、纵向居中偏南；3 人西南、东南 + 北边横向居中偏西；4 人四角）；
        // 任意两座位外接矩形相隔的格数（横纵较大者）≥ 25；座位在棋盘清单最前、按左下角先行后列，出生区 i = 座位 i 的核心 3×3。
        (int w, int h) = Size[players];
        int west = M + 2, east = M + w - 2 - 8, south = M + 2, north = M + h - 2 - 8;
        Coord[] expected = players switch
        {
            2 => [new(west, M + ((h - 8) / 2)), new(east, M + ((h - 8) / 2))],
            3 => [new(west, south), new(east, south), new(M + ((w - 8) / 2), north)],
            _ => [new(west, south), new(east, south), new(west, north), new(east, north)],
        };
        foreach ((ulong seed, MapData map) in Maps(players))
        {
            string at = $"link:{seed}:{players}";
            BoardPlate[] seats = [.. map.Boards.Where(p => p.Kind == BoardPlateKind.Birth)];
            Assert.Equal(players, seats.Length);
            Assert.Equal(players, map.BirthZones.Length);
            Assert.Equal(seats, map.Boards.Take(players));   // 座位在前
            Assert.Equal(expected, seats.Select(p => p.Origin));
            for (int i = 0; i < seats.Length; i++)
            {
                Assert.Equal((8, 8), (seats[i].Width, seats[i].Height));
                var core = new Coord(seats[i].Origin.X + 2, seats[i].Origin.Y + 2);   // 8×8 的核心区偏西南：(8−3)/2 = 2
                Assert.Equal(TestMaps.Rect(core.X, core.Y, 3, 3).Order(), map.BirthZones[i].Order());
                Assert.Equal(i, map.BirthBoardAt(seats[i].Origin));
                for (int j = i + 1; j < seats.Length; j++)
                {
                    Assert.True(Gap(seats[i], seats[j]) >= 25, $"{at}：座位 {i} 与 {j} 间距 {Gap(seats[i], seats[j])}");
                }
            }

            // 25×25 战场：东西两座位之间夹着的 25 列（3 / 4 人另有南北两座位之间的 25 行）里没有任何座位格。
            Assert.Equal(25, Gap(seats[0], seats[1]));
        }
    }

    [Theory]
    [MemberData(nameof(Players))]
    public void 中立平台数与尺寸(int players)
    {
        // 裁决 S4 / design E2：中立平台（公共棋盘）数 2–3 / 3–4 / 4–5，宽高 6–8，带主题（四选一、可重复），不可出生（不对应出生区）；
        // 全部平台（座位与中立平台）距岛边 ≥ 2、两两外接矩形间隔 ≥ 3；中立平台在清单里排在座位之后、按左下角先行后列。
        (int w, int h) = Size[players];
        (int min, int max) = NeutralRange[players];
        var counts = new HashSet<int>();
        var themes = new HashSet<BoardTheme>();
        bool repeated = false;
        foreach ((ulong seed, MapData map) in Maps(players))
        {
            string at = $"link:{seed}:{players}";
            BoardPlate[] neutral = [.. map.Boards.Skip(players)];
            Assert.All(neutral, p => Assert.Equal(BoardPlateKind.Public, p.Kind));
            Assert.InRange(neutral.Length, min, max);
            counts.Add(neutral.Length);
            Assert.Equal(neutral.OrderBy(p => p.Origin.Y).ThenBy(p => p.Origin.X), neutral);
            Assert.All(neutral, p => Assert.Null(map.BirthBoardAt(p.Origin)));
            for (int i = 0; i < map.Boards.Length; i++)
            {
                BoardPlate p = map.Boards[i];
                Assert.InRange(p.Width, 6, 8);
                Assert.InRange(p.Height, 6, 8);
                Assert.True(p.Origin.X >= M + 2 && p.Origin.Y >= M + 2 && p.Origin.X + p.Width <= M + w - 2 && p.Origin.Y + p.Height <= M + h - 2, $"{at}：平台 {i} 距岛边不足 2");
                Assert.Contains(p.Theme!.Value, new[] { BoardTheme.River, BoardTheme.Forest, BoardTheme.Palisade, BoardTheme.Mixed });
                themes.Add(p.Theme!.Value);
                for (int j = i + 1; j < map.Boards.Length; j++)
                {
                    Assert.True(Gap(p, map.Boards[j]) >= 3, $"{at}：平台 {i} 与 {j} 间隔 {Gap(p, map.Boards[j])}");
                }
            }

            repeated |= map.Boards.GroupBy(p => p.Theme).Any(g => g.Count() > 1);
        }

        output.WriteLine($"{players} 人：中立平台数取到 {string.Join("/", counts.Order())}");
        Assert.Equal(4, themes.Count);   // 样本口径：四个主题都出现
        Assert.True(repeated, "样本口径：主题可重复（R3），20 张图里应当出现过");
        Assert.True(counts.Count >= 2, "样本口径：中立平台数不是恒定值");
    }

    [Theory]
    [MemberData(nameof(Players))]
    public void 中央入口取岛中心或最近的可落子低地(int players)
    {
        // design E3：中央入口取岛几何中心格（偏西南）；该格不可落子时取离它最近（曼哈顿距离）的可落子 h=0 格，并列先行后列。测试内独立算式。
        (int w, int h) = Size[players];
        var centre = new Coord(M + ((w - 1) / 2), M + ((h - 1) / 2));
        foreach ((ulong seed, MapData map) in Maps(players))
        {
            Coord expected = map.IsPlayable(centre)
                ? centre
                : map.AllCoords().Where(c => OnIsland(c, map) && map.IsPlayable(c) && map.HeightAt(c) == 0)
                    .OrderBy(c => Math.Abs(c.X - centre.X) + Math.Abs(c.Y - centre.Y)).ThenBy(c => c.Y).ThenBy(c => c.X).First();
            Assert.Equal(expected, map.CentralEntrance);
        }
    }

    [Theory]
    [MemberData(nameof(Players))]
    public void 平台高二且坡道二到三朝外是低地(int players)
    {
        // design D3：平台内可落子格除坡道外 h=2；坡道 h=1、草地、2–3 个、在外圈非角格、两两不在同一条边、朝外的格是 h=0 可落子低地；
        // 坡道之外平台与低地之间没有气边（崖壁）——平台内可落子格沿气边走出平台只能经坡道。
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
                    Assert.True(OnIsland(o, map) && map.IsPlayable(o) && map.HeightAt(o) == 0 && !InPlate(map, o), $"{at}：坡道 {ramp.ToNotation()} 朝外 {o.ToNotation()} 不是低地");
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
        // design D4：平台外、岛内为低地（h=0）；河道 n 条，各是深水折线、恰 2 座不相邻的预置桥；不占坡道朝外格。
        // map-coastline 裁决 3：河道 1–2 格宽（没有 3×3 全是深水的块）；低地深水团里带桥的是河道、不带桥的是湖泊（4–9 格），两类都不相接（各自按几何四邻分团）；
        // 桥的两岸都通（桥格至少两个气边邻格）。
        int widened = 0;
        int lakesTotal = 0;
        foreach ((ulong seed, MapData map) in Maps(players))
        {
            string at = $"link:{seed}:{players}";
            Coord[] lowland = [.. map.AllCoords().Where(c => OnIsland(c, map) && !InPlate(map, c))];
            Assert.All(lowland, c => Assert.Equal(0, map.HeightAt(c)));
            List<List<Coord>> water = Clumps([.. lowland.Where(c => map.SurfaceAt(c) == Surface.DeepWater)]);
            List<List<Coord>> rivers = [.. water.Where(w => w.Any(map.HasBridge))];
            List<List<Coord>> lakes = [.. water.Where(w => !w.Any(map.HasBridge))];
            Assert.True(rivers.Count == players, $"{at}：低地河道 {rivers.Count} 条");
            HashSet<Coord> outlets = [.. map.Boards.SelectMany(p => p.Cells().Where(c => map.IsPlayable(c) && map.HeightAt(c) == 1).Select(c => Outward(p, c)!.Value))];
            foreach (List<Coord> river in rivers)
            {
                Assert.True(river.Count >= 6, $"{at}：河道只有 {river.Count} 格");
                HashSet<Coord> cells = [.. river];
                Assert.DoesNotContain(river, c => TestMaps.Rect(c.X, c.Y, 3, 3).All(cells.Contains));   // 宽 ≤ 2
                widened += river.Count(c => river.Count(o => Adjacency.AreAdjacent(o, c)) >= 3);
                Coord[] bridges = [.. river.Where(map.HasBridge)];
                Assert.Equal(2, bridges.Length);
                Assert.False(Adjacency.AreAdjacent(bridges[0], bridges[1]), $"{at}：两座桥相邻");
                Assert.DoesNotContain(river, outlets.Contains);
                Assert.All(bridges, b => Assert.True(Adjacency.LibertyNeighbors(map, b).Length >= 2, $"{at}：桥 {b.ToNotation()} 两岸不通"));
            }

            foreach (List<Coord> lake in lakes)
            {
                Assert.InRange(lake.Count, LinkMapGenerator.LakeMin, LinkMapGenerator.LakeMax);
                Assert.DoesNotContain(lake, c => OnIslandEdge(c, map));   // 内陆
                Assert.DoesNotContain(lake, outlets.Contains);
            }

            lakesTotal += lakes.Count;
        }

        Assert.True(widened > 20 * players, $"样本口径：20 张图里加宽到 2 格的河道格只有 {widened} 个");
        Assert.True(lakesTotal >= 20, $"样本口径：20 张图里只有 {lakesTotal} 个湖泊");
    }

    [Theory]
    [MemberData(nameof(Players))]
    public void 岛缘林地与少量岩石(int players)
    {
        // design D4：低地的林地只在岛缘一圈。map-coastline 裁决 3 / 7：低地障碍只有岩区——3–6 格一团、不在岛缘（贴岸的障碍在数据上就是岸线凹口，表现层也当虚空）；
        // 生成器仍在岛缘放 2–(n+1) 格零星岩石，但它们贴岸、四邻有掩码外的格，按留白泛洪口径与岸外连通而算虚空（贴岸的障碍与海湾在数据上无法区分，表现层同样不铺岛面），
        // 所以泛洪岛面上的低地障碍只剩岩区；零星岩石落在掩码内、泛洪岛面外这一点由 岛形Tests「岛面四邻连通且没有内部虚空」断言。
        int forestTotal = 0;
        int clustersTotal = 0;
        foreach ((ulong seed, MapData map) in Maps(players))
        {
            string at = $"link:{seed}:{players}";
            Coord[] lowland = [.. map.AllCoords().Where(c => OnIsland(c, map) && !InPlate(map, c))];
            Coord[] forest = [.. lowland.Where(c => map.SurfaceAt(c) == Surface.Forest)];
            Assert.All(forest, c => Assert.True(OnIslandEdge(c, map), $"{at}：林地 {c.ToNotation()} 不在岛缘"));
            forestTotal += forest.Length;

            List<List<Coord>> clusters = Clumps([.. lowland.Where(map.Obstacles.Contains)]);
            Assert.All(clusters, k => Assert.InRange(k.Count, LinkMapGenerator.RockClusterMin, LinkMapGenerator.RockClusterMax));
            Assert.All(clusters, k => Assert.DoesNotContain(k, c => OnIslandEdge(c, map)));
            clustersTotal += clusters.Count;
        }

        Assert.True(forestTotal > 20 * players, $"样本口径：20 张图的岛缘林地合计只有 {forestTotal} 格");
        Assert.True(clustersTotal >= 20 * players, $"样本口径：20 张图的岩区合计只有 {clustersTotal} 团");
    }

    [Theory]
    [MemberData(nameof(Players))]
    public void 不放静态信物格(int players)
    {
        // design D5（段 A3 改写，依据 relic-redesign R29，2026-10-26 用户裁决）：浮空岛不放静态信物格——出生区信物与低地公共信物都没有。
        // relic-redesign 接线段 W1 起地图数据连"信物格"字段都没有：导出文件里不出现 RelicCells（信物改由对局中途刷新）。
        foreach ((ulong seed, MapData map) in Maps(players))
        {
            Assert.DoesNotContain("RelicCells", MapFile.ToJson(map), StringComparison.Ordinal);
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
