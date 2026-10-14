using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Xunit.Abstractions;

namespace Siege.Core.Tests.LinkMapGeneration;

/// <summary>
/// 规格：openspec/changes/link-map-terrain/specs/link-map-generation —— Requirement: 初始布局（中间带按主题放置地形）；design D3 / D4（tasks 2.2 / 2.3）。
/// 每个主题的生成函数单测其主题特征，各取 6×6 与 8×8 两种尺寸、布局流种子 1–30；另统计种子 1–20 的重抽次数分布与各主题的实际地形量（输出到测试日志，抄进任务汇报）。
/// </summary>
/// <remarks>变异验证见任务汇报（M-T*）。</remarks>
public class 主题地形Tests(ITestOutputHelper output)
{
    private const int StreamSeeds = 30;

    public static IEnumerable<object[]> Sizes() => [[6], [8]];

    private static IEnumerable<(ulong Seed, LinkMapGenerator.BoardTerrain Board, MapData Map)> Draft(BoardTheme theme, int side)
    {
        var plate = new BoardPlate(new Coord(2, 2), side, side, BoardPlateKind.Birth, theme);
        for (ulong seed = 1; seed <= StreamSeeds; seed++)
        {
            LinkMapGenerator.BoardTerrain board = LinkMapGenerator.DraftBoard(plate, MapRandom.ForLinkLayout(seed));
            yield return (seed, board, board.Preview());
        }
    }

    /// <summary>每个主题都满足的公共约束：外圈 / 核心区平坦、岩石只在中间带且 ≤ 2（6×6 为 0）、棋盘内连通、可改造目标 ≥ 2。</summary>
    private static void AssertCommon(BoardPlate plate, MapData map, LinkMapGenerator.BoardTerrain board, ulong seed)
    {
        string at = $"{plate.Theme} {plate.Width}×{plate.Height} 流种子 {seed}";
        Assert.All(plate.Cells().Where(plate.OnRim), c => Assert.True(map.IsPlayable(c) && map.HeightAt(c) == 0 && map.SurfaceAt(c) is Surface.Grass or Surface.Forest, $"{at} 外圈 {c}"));
        Assert.All(plate.CoreCells(), c => Assert.True(map.IsPlayable(c) && map.HeightAt(c) == 0 && map.SurfaceAt(c) == Surface.Grass, $"{at} 核心区 {c}"));
        Assert.All(map.TerrainData.Fences, e => Assert.False(plate.OnRim(e.A) || plate.OnRim(e.B) || (plate.InCore(e.A) && plate.InCore(e.B)), $"{at} 栅栏 {e}"));
        Assert.InRange(board.Rocks.Count, 0, plate.Width == 6 && plate.Height == 6 ? 0 : 2);
        Assert.All(board.Rocks, r => Assert.True(plate.InMiddleBand(r), $"{at} 岩石 {r}"));
        Assert.Equal(1, Battlefields.Of(map).Count);
        Assert.True(plate.Cells().Count(c => map.SurfaceAt(c) == Surface.Forest || map.TerrainData.IsUnbridgedDeepWater(c)) >= 2, $"{at} 改造目标不足");
        Assert.All(map.TerrainData.Heights.Keys.Concat(map.TerrainData.Surfaces.Where(kv => kv.Value != Surface.Forest).Select(kv => kv.Key)), c => Assert.True(plate.InMiddleBand(c), $"{at} {c} 的高差 / 深水不在中间带"));
        Assert.All(board.HardCells, c => Assert.True(plate.Contains(c) && !plate.InCore(c) && map.IsPlayable(c), $"{at} 难到达格 {c}"));
        Assert.InRange(board.Redraws, 0, LinkMapGenerator.MaxBoardRedraws);
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void 高台有高度2的格且可以走上去(int side)
    {
        foreach ((ulong seed, LinkMapGenerator.BoardTerrain board, MapData map) in Draft(BoardTheme.Plateau, side))
        {
            BoardPlate plate = board.Plate;
            AssertCommon(plate, map, board, seed);
            Coord[] tops = [.. plate.Cells().Where(c => map.HeightAt(c) == 2)];
            Assert.InRange(tops.Length, 2, 4);

            // 从高度 0（核心区）沿气边走上去：每个高度 2 的格都可达。
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

            Assert.All(tops, t => Assert.Contains(t, reached));
            // 平台在中间带里的四邻全是高度 1 的坡；8×8 有不邻外圈的中间带格，平台不与外圈相邻（6×6 没有这样的格，按待裁决的放宽处理）。
            Assert.All(plate.Cells().Where(c => plate.InMiddleBand(c) && map.HeightAt(c) != 2 && tops.Any(t => Adjacency.AreAdjacent(t, c))), c => Assert.Equal(1, map.HeightAt(c)));
            if (side == 8)
            {
                Assert.DoesNotContain(tops, t => plate.Cells().Where(plate.OnRim).Any(r => Adjacency.AreAdjacent(r, t)));
            }

            Assert.Equal(tops.Order(), board.HardCells.Order());
            Assert.InRange(plate.Cells().Count(c => map.SurfaceAt(c) == Surface.Forest), 2, 3);
        }
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void 河流贯穿中间带且恰有一座桥(int side)
    {
        int bends = 0;
        foreach ((ulong seed, LinkMapGenerator.BoardTerrain board, MapData map) in Draft(BoardTheme.River, side))
        {
            BoardPlate plate = board.Plate;
            AssertCommon(plate, map, board, seed);
            Coord[] water = [.. plate.Cells().Where(c => map.SurfaceAt(c) == Surface.DeepWater)];
            Assert.All(water, w => Assert.True(plate.InMiddleBand(w)));
            Assert.Single(map.TerrainData.Bridges);
            Assert.Contains(map.TerrainData.Bridges.Single(), water);

            // 宽 1 的一条线：每格至多 2 个深水邻居、恰 2 个端点、连成一片；端点各挨着外圈；直线或一次折弯。
            int Degree(Coord c) => water.Count(w => Adjacency.AreAdjacent(w, c));
            Assert.All(water, w => Assert.InRange(Degree(w), 1, 2));
            Coord[] ends = [.. water.Where(w => Degree(w) == 1)];
            Assert.Equal(2, ends.Length);
            Assert.All(ends, e => Assert.Contains(plate.Cells().Where(plate.OnRim), r => Adjacency.AreAdjacent(r, e)));
            int columns = water.Select(w => w.X).Distinct().Count();
            int rows = water.Select(w => w.Y).Distinct().Count();
            bool straight = columns == 1 || rows == 1;
            Assert.True(straight || columns + rows - 1 == water.Length, $"流种子 {seed}：河道不是直线或一次折弯");
            bends += straight ? 0 : 1;
            Assert.True(water.Length >= side - 2, "直线贯穿内圈全长；折弯的两臂合计更长");
        }

        output.WriteLine($"河流 {side}×{side}：{StreamSeeds} 个流种子里折弯 {bends} 次");
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

            // 难到达格（负责人 2026-10-14 裁决 T2 = A，原为三面）：核心区之外、可落子、非林地、至少两面挨着林地的格——测试内独立算式。
            Coord[] hard = [.. plate.Cells().Where(c => !plate.InCore(c) && map.IsPlayable(c) && map.SurfaceAt(c) != Surface.Forest
                && plate.Cells().Count(n => map.SurfaceAt(n) == Surface.Forest && Adjacency.AreAdjacent(n, c)) >= 2).Order()];
            Assert.Equal(hard, board.HardCells);
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

            // 难到达格（裁决 T2 = A）：核心区之外、可落子、至少两条边有栅栏的格——测试内独立算式。
            Coord[] hard = [.. plate.Cells().Where(c => !plate.InCore(c) && map.IsPlayable(c) && map.TerrainData.Fences.Count(e => e.A == c || e.B == c) >= 2).Order()];
            Assert.Equal(hard, board.HardCells);
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
            Coord[] water = [.. plate.Cells().Where(c => map.SurfaceAt(c) == Surface.DeepWater)];
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
        // tasks 2.3：种子 1–20 全部一次生成成功或在上限内成功（不抛 MapGenerationException），记录各种子的重抽次数分布。
        var histogram = new SortedDictionary<int, int>();
        var byTheme = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (ulong seed = 1; seed <= 20; seed++)
        {
            (MapData map, ImmutableArray<int> redraws) = LinkMapGenerator.GenerateWithRedraws(seed);
            Assert.Equal(MapFile.ToJson(LinkMapGenerator.Generate(seed)), MapFile.ToJson(map));   // 统计入口与缓存入口同图
            Assert.All(redraws, r => Assert.InRange(r, 0, LinkMapGenerator.MaxBoardRedraws));
            for (int z = 0; z < redraws.Length; z++)
            {
                histogram[redraws[z]] = histogram.GetValueOrDefault(redraws[z]) + 1;
                string key = $"{map.Boards[z].Theme!.Value.DisplayName()} {map.Boards[z].Width}×{map.Boards[z].Height}";
                byTheme[key] = byTheme.GetValueOrDefault(key) + redraws[z];
            }

            output.WriteLine($"link:{seed} 重抽 {string.Join(" ", redraws)}");
        }

        output.WriteLine($"重抽次数分布（100 块棋盘）：{string.Join(" ", histogram.Select(kv => $"{kv.Key}次:{kv.Value}块"))}");
        output.WriteLine($"按主题与尺寸累计重抽：{string.Join("；", byTheme.Where(kv => kv.Value > 0).Select(kv => $"{kv.Key} {kv.Value}"))}");
        Assert.Equal(100, histogram.Values.Sum());
    }

    [Fact]
    public void 自检失败在同一条流上重抽_到上限即抛()
    {
        // D3：自检失败就在同一条随机流上重抽该块，至多 MaxBoardRedraws 次，仍失败抛 MapGenerationException（生成器缺陷，不静默）。
        // 种子 1–20 实测从不重抽，这里用一块 5×5 的林地主题棋盘（没有中间带，一团林地都起不了）走到上限：
        // 每次尝试都消费随机数（同一条流在推进，不是同一次尝试重复 17 遍），17 次后抛出并指出棋盘、尺寸与主题。
        var plate = new BoardPlate(new Coord(2, 2), 5, 5, BoardPlateKind.Birth, BoardTheme.Forest);
        Siege.Core.Determinism.RandomStream rng = MapRandom.ForLinkLayout(1);
        MapGenerationException ex = Assert.Throws<MapGenerationException>(() => LinkMapGenerator.DraftBoard(plate, rng));
        Assert.Contains($"重抽 {LinkMapGenerator.MaxBoardRedraws} 次", ex.Message, StringComparison.Ordinal);
        Assert.Contains("C3", ex.Message, StringComparison.Ordinal);
        Assert.Contains("林地", ex.Message, StringComparison.Ordinal);
        Assert.Equal(16, LinkMapGenerator.MaxBoardRedraws);
        Assert.True(rng.Consumed >= LinkMapGenerator.MaxBoardRedraws + 1, $"随机流只消费了 {rng.Consumed} 次");

        // 主题缺失的棋盘不生成（不静默取缺省主题）。
        Assert.Throws<ArgumentException>(() => LinkMapGenerator.DraftBoard(plate with { Theme = null }, MapRandom.ForLinkLayout(1)));
    }

    [Fact]
    public void 各主题在六乘六与八乘八上的实际地形量()
    {
        // 汇报用统计（tasks 2.2 / 5.4 回调的起点）：流种子 1–30 的平均量。断言只做样本口径下界。
        foreach (BoardTheme theme in Enum.GetValues<BoardTheme>())
        {
            foreach (int side in new[] { 6, 8 })
            {
                var rows = Draft(theme, side).ToList();
                double Avg(Func<(ulong Seed, LinkMapGenerator.BoardTerrain Board, MapData Map), int> f) => rows.Average(r => (double)f(r));
                output.WriteLine(
                    $"{theme.DisplayName()} {side}×{side}：高2 {Avg(r => r.Board.Heights.Count(kv => kv.Value == 2)):0.0} 高1 {Avg(r => r.Board.Heights.Count(kv => kv.Value == 1)):0.0} "
                    + $"深水 {Avg(r => r.Board.Surfaces.Count(kv => kv.Value == Surface.DeepWater)):0.0} 桥 {Avg(r => r.Board.Bridges.Count):0.0} 林地 {Avg(r => r.Board.Surfaces.Count(kv => kv.Value == Surface.Forest)):0.0} "
                    + $"栅栏边 {Avg(r => r.Board.Fences.Count):0.0} 岩石 {Avg(r => r.Board.Rocks.Count):0.0} 难到达格 {Avg(r => r.Board.HardCells.Length):0.0}（有候选 {rows.Count(r => r.Board.HardCells.Length > 0)}/{rows.Count}） "
                    + $"可落子 {Avg(r => r.Map.PlayableCount):0.0} 重抽 {Avg(r => r.Board.Redraws):0.00}");
                Assert.Equal(StreamSeeds, rows.Count);
            }
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
