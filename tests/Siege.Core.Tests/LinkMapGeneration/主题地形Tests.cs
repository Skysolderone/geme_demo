using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Tests.CaptureResolution;
using Xunit.Abstractions;

namespace Siege.Core.Tests.LinkMapGeneration;

/// <summary>
/// 规格：openspec/changes/link-map-terrain/specs/link-map-generation —— Requirement: 初始布局（中间带按主题放置地形）；design D3 / D4（tasks 2.2 / 2.3）。
/// 每个主题的生成函数单测其主题特征（抬升前的平台内部），各取 6×6 / 7×7 / 8×8 三种尺寸（map-expand-25 D2：平台边长 6–8，中央 8×8）、布局流种子 1–30；
/// 另统计种子 1–20 × 人数 2–4 的重抽次数分布与各主题的实际地形量（输出到测试日志，抄进任务汇报）。map-expand-25 R3：高台主题去掉，接缝自检与难到达格随紧贴布局删除。
/// </summary>
/// <remarks>变异验证见任务汇报（M-T*）。</remarks>
public class 主题地形Tests(ITestOutputHelper output)
{
    private const int StreamSeeds = 30;

    public static IEnumerable<object[]> Sizes() => [[6], [7], [8]];

    private static IEnumerable<(ulong Seed, LinkMapGenerator.BoardTerrain Board, MapData Map)> Draft(BoardTheme theme, int side)
    {
        var plate = new BoardPlate(new Coord(2, 2), side, side, BoardPlateKind.Birth, theme);
        for (ulong seed = 1; seed <= StreamSeeds; seed++)
        {
            LinkMapGenerator.BoardTerrain board = LinkMapGenerator.DraftBoard(plate, MapRandom.ForLinkLayout(seed, 4));
            yield return (seed, board, board.Preview());
        }
    }

    /// <summary>
    /// 每个主题都满足的公共约束（抬升前）：外圈 / 核心区平坦（河流的隔断线经过处除外，terrain-barriers 裁决 H4）、散布岩石只在中间带且 ≤ 2（6×6 为 0）、
    /// 湖泊 / 岩区一团（见「湖泊或岩区恰一团且不切断棋盘」）、棋盘内连通、可改造目标 ≥ 2；平台内全部可落子格从核心区沿气边走得到（map-expand-25：对全部主题查）。
    /// </summary>
    private static void AssertCommon(BoardPlate plate, MapData map, LinkMapGenerator.BoardTerrain board, ulong seed)
    {
        string at = $"{plate.Theme} {plate.Width}×{plate.Height} 流种子 {seed}";
        HashSet<Coord> barrier = BarrierRegion(plate, map);
        Assert.All(plate.Cells().Where(c => plate.OnRim(c) && !barrier.Contains(c)), c => Assert.True(map.IsPlayable(c) && map.HeightAt(c) == 0 && map.SurfaceAt(c) is Surface.Grass or Surface.Forest, $"{at} 外圈 {c}"));
        Assert.All(plate.CoreCells(), c => Assert.True(map.IsPlayable(c) && map.HeightAt(c) == 0 && map.SurfaceAt(c) == Surface.Grass, $"{at} 核心区 {c}"));
        Assert.All(map.TerrainData.Fences, e => Assert.False(plate.OnRim(e.A) || plate.OnRim(e.B) || (plate.InCore(e.A) && plate.InCore(e.B)), $"{at} 栅栏 {e}"));
        Coord[] scattered = [.. board.Rocks.Where(r => !board.Cluster.Contains(r))];
        Assert.InRange(scattered.Length, 0, plate.Width == 6 && plate.Height == 6 ? 0 : 2);
        Assert.All(board.Rocks, r => Assert.True(plate.InMiddleBand(r), $"{at} 岩石 {r}"));
        Assert.Equal(1, Battlefields.Of(map).Count);
        Assert.True(plate.Cells().Count(c => map.SurfaceAt(c) == Surface.Forest || map.TerrainData.IsUnbridgedDeepWater(c)) >= 2, $"{at} 改造目标不足");
        Assert.All(map.TerrainData.Heights.Keys.Concat(map.TerrainData.Surfaces.Where(kv => kv.Value != Surface.Forest).Select(kv => kv.Key)).Where(c => !barrier.Contains(c)), c => Assert.True(plate.InMiddleBand(c), $"{at} {c} 的高差 / 深水不在中间带"));
        Assert.InRange(board.Redraws, 0, LinkMapGenerator.MaxBoardRedraws);
        var reached = new HashSet<Coord>(plate.CoreCells());
        var queue = new Queue<Coord>(reached);
        while (queue.Count > 0)
        {
            foreach (Coord n in Adjacency.LibertyNeighbors(map, queue.Dequeue()).Where(plate.Contains))
            {
                if (reached.Add(n))
                {
                    queue.Enqueue(n);
                }
            }
        }

        Assert.All(plate.Cells().Where(map.IsPlayable), c => Assert.True(reached.Contains(c), $"{at}：{c} 从核心区沿气边走不到"));
    }

    /// <summary>
    /// 隔断线（terrain-barriers D1 / 实施修订 J1–J3，测试内独立判定，不读生成器）：棋盘内不在外圈的整列 / 整行中每格都是深水的那一条（河流）。
    /// 断言恰有一条，返回线上的格、不含核心区的一侧的格与通道格（桥）。核心区中心用独立算式。
    /// </summary>
    internal static (Coord[] Line, Coord[] Beyond, Coord Passage) Barrier(BoardPlate plate, MapData map)
    {
        var center = new Coord(plate.Origin.X + ((plate.Width - 3) / 2) + 1, plate.Origin.Y + ((plate.Height - 3) / 2) + 1);
        var lines = new List<(bool Column, int Index)>();
        for (int x = plate.Origin.X; x < plate.Origin.X + plate.Width; x++)
        {
            lines.Add((true, x));
        }

        for (int y = plate.Origin.Y; y < plate.Origin.Y + plate.Height; y++)
        {
            lines.Add((false, y));
        }

        Coord[] Cells((bool Column, int Index) l) => [.. plate.Cells().Where(c => (l.Column ? c.X : c.Y) == l.Index)];
        (bool Column, int Index) line = Assert.Single(lines, l => Cells(l).All(c => map.SurfaceAt(c) == Surface.DeepWater));
        Coord[] onLine = Cells(line);
        int side = Math.Sign((line.Column ? center.X : center.Y) - line.Index);
        Coord[] beyond = [.. plate.Cells().Where(c => Math.Sign((line.Column ? c.X : c.Y) - line.Index) == -side)];
        Coord passage = Assert.Single(onLine, map.HasBridge);
        return (onLine, beyond, passage);
    }

    /// <summary>外圈规则的放开范围（H4）：河流为隔断线的格（两端在外圈）；其余主题为空。</summary>
    private static HashSet<Coord> BarrierRegion(BoardPlate plate, MapData map) =>
        plate.Theme == BoardTheme.River ? [.. Barrier(plate, map).Line] : [];

    /// <summary>
    /// 隔断线公共断言（规格 Scenario「河流横穿棋盘」）：整行 / 整列、不在外圈（两端各占外圈 1 格）、不经过核心区、恰 1 处通道；
    /// 从核心区沿气边在棋盘内走：去掉通道格时走不到线上与对侧的任何格，留着通道格时走得到线上与对侧的全部可落子格（两侧除通道外无气边）。
    /// </summary>
    private static (Coord[] Line, Coord[] Beyond, Coord Passage) AssertBarrier(BoardPlate plate, MapData map, ulong seed)
    {
        string at = $"{plate.Theme} {plate.Width}×{plate.Height} 流种子 {seed}";
        (Coord[] line, Coord[] beyond, Coord passage) = Barrier(plate, map);
        Assert.True(line.Length == plate.Width || line.Length == plate.Height, $"{at}：隔断线没有贯穿棋盘");
        Assert.True(line.Count(plate.OnRim) == 2, $"{at}：隔断线落在外圈行 / 列上，或两端不在外沿");
        int cx = plate.Origin.X + ((plate.Width - 3) / 2), cy = plate.Origin.Y + ((plate.Height - 3) / 2);
        Coord[] core = [.. Enumerable.Range(0, 9).Select(k => new Coord(cx + (k % 3), cy + (k / 3)))];
        Assert.All(line, c => Assert.DoesNotContain(c, core));
        Assert.NotEmpty(beyond);

        HashSet<Coord> far = [.. line, .. beyond];
        HashSet<Coord> Walk(bool withPassage)
        {
            var reached = new HashSet<Coord>(core);
            var queue = new Queue<Coord>(core);
            while (queue.Count > 0)
            {
                foreach (Coord n in Adjacency.LibertyNeighbors(map, queue.Dequeue()).Where(plate.Contains))
                {
                    if ((withPassage || n != passage) && reached.Add(n))
                    {
                        queue.Enqueue(n);
                    }
                }
            }

            return reached;
        }

        Assert.DoesNotContain(Walk(withPassage: false), c => far.Contains(c));
        HashSet<Coord> open = Walk(withPassage: true);
        Assert.All(far.Where(map.IsPlayable), c => Assert.Contains(c, open));
        return (line, beyond, passage);
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void 河流横穿棋盘且恰有一座桥(int side)
    {
        // terrain-barriers D1 / 实施修订 J1、J2：中间带整行 / 整列深水、恰 1 座桥。
        var chosen = new HashSet<string>(StringComparer.Ordinal);
        foreach ((ulong seed, LinkMapGenerator.BoardTerrain board, MapData map) in Draft(BoardTheme.River, side))
        {
            BoardPlate plate = board.Plate;
            AssertCommon(plate, map, board, seed);
            (Coord[] line, _, Coord bridge) = AssertBarrier(plate, map, seed);
            Assert.Equal([bridge], map.TerrainData.Bridges);
            Assert.All(line.Where(c => c != bridge), c => Assert.True(map.TerrainData.IsUnbridgedDeepWater(c)));
            chosen.Add(LineName(plate, line));
        }

        output.WriteLine($"河流 {side}×{side}：{StreamSeeds} 个流种子取到的隔断线 {string.Join(" ", chosen.Order(StringComparer.Ordinal))}");
        AssertCandidates(side, chosen);
    }

    /// <summary>隔断线的相对名称：列 c0..c7 / 行 r0..r7（相对棋盘左下角）。</summary>
    private static string LineName(BoardPlate plate, Coord[] line) =>
        line.Select(c => c.X).Distinct().Count() == 1 ? $"c{line[0].X - plate.Origin.X}" : $"r{line[0].Y - plate.Origin.Y}";

    /// <summary>
    /// 候选（实施修订 J1 / J2，独立列表）：中间带里不穿过核心区的整列 / 整行——边长 6 只有 c4 / r4，边长 7 为 c1 / c5 与 r1 / r5，边长 8（核心区偏西南、占 2–4）为 c1 / c5 / c6 与 r1 / r5 / r6；
    /// 30 个流种子取到的线都在候选里，且候选全部取到过（样本口径：均匀抽取不是只取一条）。
    /// </summary>
    private static void AssertCandidates(int side, HashSet<string> chosen)
    {
        string[] allowed = side switch
        {
            6 => ["c4", "r4"],
            7 => ["c1", "c5", "r1", "r5"],
            8 => ["c1", "c5", "c6", "r1", "r5", "r6"],
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, "平台边长只有 6–8"),
        };
        Assert.Equal(allowed.Order(StringComparer.Ordinal), chosen.Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void 湖泊或岩区恰一团且不切断棋盘(int side)
    {
        // terrain-barriers D3 / 裁决 H5 / 实施修订 J4：中间带多于 12 格的棋盘恰 1 团 3–5 格，全为未架桥深水（湖泊）或全为岩石（岩区），几何四邻连成一团；
        // 中间带 ≤ 12 格（6×6 的 7 格）不放。放完后棋盘内可落子格几何四邻连通、可改造目标 ≥ 2（AssertCommon）。四个主题都放。
        int lakes = 0, rockFields = 0;
        foreach (BoardTheme theme in LinkMapGenerator.IslandThemes)
        {
            foreach ((ulong seed, LinkMapGenerator.BoardTerrain board, MapData map) in Draft(theme, side))
            {
                BoardPlate plate = board.Plate;
                string at = $"{theme} {side}×{side} 流种子 {seed}";
                AssertCommon(plate, map, board, seed);
                Coord[] cluster = [.. board.Cluster];
                int middle = ((side - 2) * (side - 2)) - 9;
                if (middle <= 12)
                {
                    Assert.Empty(cluster);
                    continue;
                }

                Assert.InRange(cluster.Length, 3, 5);
                Assert.All(cluster, c => Assert.True(plate.InMiddleBand(c), $"{at}：{c} 不在中间带"));
                Assert.Single(Clumps([.. cluster]));
                bool lake = cluster.All(map.TerrainData.IsUnbridgedDeepWater);
                bool rock = cluster.All(c => board.Rocks.Contains(c) && !map.IsPlayable(c) && map.SurfaceAt(c) != Surface.DeepWater);
                Assert.True(lake ^ rock, $"{at}：湖泊 / 岩区不是同一种");
                lakes += lake ? 1 : 0;
                rockFields += rock ? 1 : 0;
            }
        }

        output.WriteLine($"{side}×{side}：湖泊 {lakes} 团、岩区 {rockFields} 团");
        Assert.True(side == 6 || (lakes > 0 && rockFields > 0), "样本口径：两种都要出现");
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void 林地有两到三团(int side)
    {
        foreach ((ulong seed, LinkMapGenerator.BoardTerrain board, MapData map) in Draft(BoardTheme.Forest, side))
        {
            BoardPlate plate = board.Plate;
            AssertCommon(plate, map, board, seed);
            List<List<Coord>> clumps = Clumps([.. plate.Cells().Where(c => map.SurfaceAt(c) == Surface.Forest)]);
            Assert.InRange(clumps.Count, 2, 3);
            Assert.All(clumps, k => Assert.InRange(k.Count, 2, 4));
            Assert.Empty(map.TerrainData.Fences);
            Assert.Empty(map.TerrainData.Heights);
        }
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void 栅栏阵有两到三段且不碰外圈(int side)
    {
        foreach ((ulong seed, LinkMapGenerator.BoardTerrain board, MapData map) in Draft(BoardTheme.Palisade, side))
        {
            BoardPlate plate = board.Plate;
            AssertCommon(plate, map, board, seed);
            List<int> runs = Runs(map.TerrainData.Fences);
            Assert.InRange(runs.Count, 2, 3);
            Assert.All(runs, n => Assert.InRange(n, 2, 3));
            Coord[] forest = [.. plate.Cells().Where(c => map.SurfaceAt(c) == Surface.Forest)];
            Assert.Equal(2, forest.Length);
            Assert.All(forest, f => Assert.True(plate.OnRim(f)));
        }
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void 混合四样各有(int side)
    {
        foreach ((ulong seed, LinkMapGenerator.BoardTerrain board, MapData map) in Draft(BoardTheme.Mixed, side))
        {
            BoardPlate plate = board.Plate;
            AssertCommon(plate, map, board, seed);
            Assert.Single(map.TerrainData.Heights);
            Assert.Equal(1, map.TerrainData.Heights.Values.Single());
            Coord[] water = [.. plate.Cells().Where(c => map.SurfaceAt(c) == Surface.DeepWater && !board.Cluster.Contains(c))];   // 湖泊（terrain-barriers D3）另算
            Assert.Equal(2, water.Length);
            Assert.True(Adjacency.AreAdjacent(water[0], water[1]));
            Assert.Single(map.TerrainData.Bridges);
            List<List<Coord>> clumps = Clumps([.. plate.Cells().Where(c => map.SurfaceAt(c) == Surface.Forest)]);
            Assert.Single(clumps);
            Assert.InRange(clumps[0].Count, 2, 3);
            List<int> runs = Runs(map.TerrainData.Fences);
            Assert.Single(runs);
            Assert.InRange(runs[0], 2, 3);
        }
    }

    [Fact]
    public void 种子1到20的重抽次数分布()
    {
        // tasks 2.3 / map-expand-25 D2–D4：种子 1–20 × 人数 2–4 全部在上限内成功（不抛 MapGenerationException），记录平台级、整张布局与低地三种重抽次数的分布。
        var histogram = new SortedDictionary<int, int>();
        var byTheme = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int plates = 0;
        foreach (int players in new[] { 2, 3, 4 })
        {
            var layout = new List<int>();
            var lowland = new List<int>();
            for (ulong seed = 1; seed <= 20; seed++)
            {
                LinkMapGenerator.IslandGeneration g = LinkMapGenerator.GenerateDetailed(seed, players);
                Assert.Equal(MapFile.ToJson(LinkMapGenerator.Generate(seed, players)), MapFile.ToJson(g.Map));   // 统计入口与缓存入口同图
                Assert.InRange(g.LayoutRedraws, 0, LinkMapGenerator.MaxLayoutRedraws);
                Assert.InRange(g.LowlandRedraws, 0, LinkMapGenerator.MaxLowlandRedraws);
                layout.Add(g.LayoutRedraws);
                lowland.Add(g.LowlandRedraws);
                foreach (LinkMapGenerator.BoardTerrain board in g.Boards)
                {
                    Assert.InRange(board.Redraws, 0, LinkMapGenerator.MaxBoardRedraws);
                    histogram[board.Redraws] = histogram.GetValueOrDefault(board.Redraws) + 1;
                    string key = $"{board.Plate.Theme!.Value.DisplayName()} {board.Plate.Width}×{board.Plate.Height}";
                    byTheme[key] = byTheme.GetValueOrDefault(key) + board.Redraws;
                    plates++;
                }
            }

            output.WriteLine($"{players} 人：整张布局重抽 均值 {layout.Average():0.00} 最多 {layout.Max()}；低地重抽 均值 {lowland.Average():0.00} 最多 {lowland.Max()}");
        }

        output.WriteLine($"平台级重抽次数分布（{plates} 块）：{string.Join(" ", histogram.Select(kv => $"{kv.Key}次:{kv.Value}块"))}");
        output.WriteLine($"按主题与尺寸累计重抽：{string.Join("；", byTheme.Where(kv => kv.Value > 0).Select(kv => $"{kv.Key} {kv.Value}"))}");
        Assert.Equal(plates, histogram.Values.Sum());
        Assert.True(plates >= 60 * 6, $"样本口径：60 张图只数到 {plates} 块平台");
    }

    [Fact]
    public void 种子1到20生成不抛异常()
    {
        // 逐种子逐人数生成（不经缓存），失败按失败码归类输出；另记可落子格与平台数区间（抄进任务汇报）。
        var failures = new List<string>();
        foreach (int players in new[] { 2, 3, 4 })
        {
            int minPlayable = int.MaxValue, maxPlayable = 0, minPlates = int.MaxValue, maxPlates = 0;
            var neutral = new SortedDictionary<int, int>();
            var redraws = new List<(int Layout, int Lowland)>();
            for (ulong seed = 1; seed <= 20; seed++)
            {
                try
                {
                    LinkMapGenerator.IslandGeneration g = LinkMapGenerator.GenerateDetailed(seed, players);
                    MapData map = g.Map;
                    (minPlayable, maxPlayable) = (Math.Min(minPlayable, map.PlayableCount), Math.Max(maxPlayable, map.PlayableCount));
                    (minPlates, maxPlates) = (Math.Min(minPlates, map.Boards.Length), Math.Max(maxPlates, map.Boards.Length));
                    int count = map.Boards.Count(b => b.Kind == BoardPlateKind.Public);
                    neutral[count] = neutral.GetValueOrDefault(count) + 1;
                    redraws.Add((g.LayoutRedraws, g.LowlandRedraws));
                }
                catch (MapGenerationException ex)
                {
                    failures.Add($"link:{seed}:{players} {ex.Message}");
                }
            }

            output.WriteLine($"{players} 人：可落子 {minPlayable}–{maxPlayable}，平台 {minPlates}–{maxPlates} 块；中立平台 {string.Join(" ", neutral.Select(kv => $"{kv.Key}块×{kv.Value}"))}；"
                + $"整张布局重抽 {string.Join(",", redraws.Select(r => r.Layout))}（合计 {redraws.Sum(r => r.Layout)}）；低地重抽 {string.Join(",", redraws.Select(r => r.Lowland))}（合计 {redraws.Sum(r => r.Lowland)}）");
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void 自检失败在同一条流上重抽_到上限即抛()
    {
        // D3：自检失败就在同一条随机流上重抽该块，至多 MaxBoardRedraws 次，仍失败抛 MapGenerationException（生成器缺陷，不静默）。
        // 这里用一块 5×5 的林地主题棋盘（没有中间带，一团林地都起不了）走到上限：
        // 每次尝试都消费随机数（同一条流在推进，不是同一次尝试重复 17 遍），17 次后抛出并指出棋盘、尺寸与主题。
        var plate = new BoardPlate(new Coord(2, 2), 5, 5, BoardPlateKind.Birth, BoardTheme.Forest);
        Siege.Core.Determinism.RandomStream rng = MapRandom.ForLinkLayout(1, 4);
        MapGenerationException ex = Assert.Throws<MapGenerationException>(() => LinkMapGenerator.DraftBoard(plate, rng));
        Assert.Contains($"重抽 {LinkMapGenerator.MaxBoardRedraws} 次", ex.Message, StringComparison.Ordinal);
        Assert.Contains("C3", ex.Message, StringComparison.Ordinal);
        Assert.Contains("林地", ex.Message, StringComparison.Ordinal);
        Assert.Equal(16, LinkMapGenerator.MaxBoardRedraws);
        Assert.True(rng.Consumed >= LinkMapGenerator.MaxBoardRedraws + 1, $"随机流只消费了 {rng.Consumed} 次");

        // 主题缺失的棋盘不生成（不静默取缺省主题）。
        Assert.Throws<ArgumentException>(() => LinkMapGenerator.DraftBoard(plate with { Theme = null }, MapRandom.ForLinkLayout(1, 4)));

        // map-expand-25 R3：高台主题不再生成（不静默换成别的主题）。
        Assert.Throws<ArgumentOutOfRangeException>(() => LinkMapGenerator.DraftBoard(plate with { Width = 7, Height = 7, Theme = BoardTheme.Plateau }, MapRandom.ForLinkLayout(1, 4)));
    }

    [Fact]
    public void 各主题在各边长上的实际地形量()
    {
        // 汇报用统计（tasks 2.2 / 5.4 回调的起点）：流种子 1–30 的平均量。断言只做样本口径下界。
        foreach (BoardTheme theme in LinkMapGenerator.IslandThemes)
        {
            foreach (int side in new[] { 6, 7, 8 })
            {
                var rows = Draft(theme, side).ToList();
                double Avg(Func<(ulong Seed, LinkMapGenerator.BoardTerrain Board, MapData Map), int> f) => rows.Average(r => (double)f(r));
                output.WriteLine(
                    $"{theme.DisplayName()} {side}×{side}：高2 {Avg(r => r.Board.Heights.Count(kv => kv.Value == 2)):0.0} 高1 {Avg(r => r.Board.Heights.Count(kv => kv.Value == 1)):0.0} "
                    + $"深水 {Avg(r => r.Board.Surfaces.Count(kv => kv.Value == Surface.DeepWater)):0.0} 桥 {Avg(r => r.Board.Bridges.Count):0.0} 林地 {Avg(r => r.Board.Surfaces.Count(kv => kv.Value == Surface.Forest)):0.0} "
                    + $"栅栏边 {Avg(r => r.Board.Fences.Count):0.0} 岩石 {Avg(r => r.Board.Rocks.Count):0.0} "
                    + $"可落子 {Avg(r => r.Map.PlayableCount):0.0} 重抽 {Avg(r => r.Board.Redraws):0.00}");
                Assert.Equal(StreamSeeds, rows.Count);
            }
        }
    }

    [Theory]
    [InlineData(6, 6, false)]
    [InlineData(6, 7, false)]
    [InlineData(7, 6, false)]
    [InlineData(7, 7, true)]
    [InlineData(8, 8, true)]
    public void 面积缩放端点随边长上限推算(int width, int height, bool top)
    {
        // terrain-density 实施后裁决 N1：插值终点由边长上限推算（7×7 的中间带 16 格），6×6 / 6×7 / 7×6（中间带 7 / 11 格）恒取下限，7×7 才取得到上限。
        // 栅栏阵取下限 = 2 段 × 2 条 = 4 条栅栏边、散布岩石 0 格（Draw(0, 2)）；7×7 在流种子 1–30 里至少一颗多于 4 条、至少一颗有散布岩石（样本口径）。
        // 判别力：终点钉回 8×8 的 27 格时 7×7 的插值上限退化为下限，本条 7×7 一行即红。map-expand-25：8×8（中央平台）按 7×7 封顶，同样取得到上限。
        var plate = new BoardPlate(new Coord(2, 2), width, height, BoardPlateKind.Birth, BoardTheme.Palisade);
        List<LinkMapGenerator.BoardTerrain> boards = [.. Enumerable.Range(1, StreamSeeds).Select(s => LinkMapGenerator.DraftBoard(plate, MapRandom.ForLinkLayout((ulong)s, 4)))];
        static int Scattered(LinkMapGenerator.BoardTerrain b) => b.Rocks.Count(r => !b.Cluster.Contains(r));
        if (top)
        {
            Assert.Contains(boards, b => b.Fences.Count > 4);
            Assert.Contains(boards, b => Scattered(b) > 0);
        }
        else
        {
            Assert.All(boards, b => Assert.Equal(4, b.Fences.Count));
            Assert.All(boards, b => Assert.Equal(0, Scattered(b)));
        }
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

    /// <summary>栅栏按"同一条墙线上紧挨着"分段，返回各段的边数（测试内独立实现）。</summary>
    internal static List<int> Runs(IEnumerable<FenceEdge> fences)
    {
        // 竖墙（东西相邻两格之间）按 (x, y) 排列，同 x 且 y 连续为一段；横墙按 (y, x)，同 y 且 x 连续为一段。
        var keys = fences.Select(e => e.A.Y == e.B.Y ? (Kind: 0, Line: e.A.X, Pos: e.A.Y) : (Kind: 1, Line: e.A.Y, Pos: e.A.X)).OrderBy(k => k).ToList();
        var runs = new List<int>();
        for (int i = 0; i < keys.Count; i++)
        {
            if (i > 0 && keys[i].Kind == keys[i - 1].Kind && keys[i].Line == keys[i - 1].Line && keys[i].Pos == keys[i - 1].Pos + 1)
            {
                runs[^1]++;
            }
            else
            {
                runs.Add(1);
            }
        }

        return runs;
    }
}
