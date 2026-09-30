using System.Diagnostics;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.MapGeneration;

/// <summary>
/// 规格：openspec/changes/board-map/specs/map-generation —— Requirement: 棋盘档布局规则。
/// 样本：地图种子 1–20 × 棋盘数 7–10 共 80 张（<see cref="BoardGenFixtures.Sample"/>）。断言一律从地图数据反推，不读生成器的工作态。
/// </summary>
/// <remarks>
/// 变异验证（board-map 段 B 实跑，每条只跑本类；红数按 Theory 行计）。生成器有校验闭环与布局自检，单改摆法只会换一次尝试、图仍合规，
/// 所以标"连同自检"的几条是把摆法与 BoardMapGenerator.CheckLayout 里对应的那一项一起改掉：
/// M-B18 公共棋盘"放 2 个信物"的边长上限 10 → 8 → 红 5：资源布点、信物格与中央入口；
/// M-B20 目标带上限 900 → 2000 → 红 2：规模落在目标带；
/// M-B21 出生棋盘也可以贴在出生棋盘上（连同自检）→ 红 6：出生棋盘直通公共棋盘、每块棋盘的通道数；
/// M-B22 每块棋盘的通道数上限 4 → 9（连同自检）→ 红 4：每块棋盘的通道数；
/// M-B23 信物格可以落在最外一圈（连同自检）→ 红 5：信物格与中央入口；
/// M-B24 离外缘的间隔 2 → 0 → 红 5：棋盘离地图外缘至少 2 格。
/// 段 B 修正二（2026-09-30 裁决：通道 3–4 格宽、出生棋盘两条出路、公共棋盘 9–11、目标带 300–800）：
/// M-2c-2 摆法不再给出生棋盘找第二条出路（BoardMapLayout：出生棋盘改走 Attach 而非 AttachBirth、修补阈值 BirthMinLinks → 1；
/// 连同 BoardMapGenerator.CheckLayout：出生棋盘通道下限 fewest → 1、不同邻盘判据 neighbors &lt; 2 → &lt; 1，共四处一起改）→ 红 8：出生棋盘直通公共棋盘 ×4、每块棋盘的通道数 ×4。
/// </remarks>
public class 棋盘档布局规则Tests
{
    public static TheoryData<int> BoardCounts => new() { 7, 8, 9, 10 };

    private static IEnumerable<(string Id, MapData Map)> Maps(int boards) =>
        Enumerable.Range(1, 20).Select(seed => ($"board:{seed}:n{boards}", BoardGenFixtures.Generated((ulong)seed, boards).Map));

    [Theory]
    [MemberData(nameof(BoardCounts))]
    public void 生成图通过校验(int boards)
    {
        // Scenario：种子 1 到 20、棋盘数 7–10 各生成一张 → 每一张都通过棋盘档静态校验。
        foreach ((string id, MapData map) in Maps(boards))
        {
            MapValidationResult result = MapValidator.Validate(map);
            Assert.True(result.IsValid, $"{id}：{result}\n{BoardGenFixtures.TextArt(map)}");
            Assert.Equal(3, result.Reports.Length);   // 到三类目标的距离只报告
            GameBoard.Load(map);
        }
    }

    [Theory]
    [MemberData(nameof(BoardCounts))]
    public void 规模落在目标带(int boards)
    {
        // Scenario：各生成一张并统计可落子格 → 每一张都在 300–800 之间。
        // 独立算式：Σ 棋盘面积 + 通道格数，与逐格数出来的可落子格相等。
        foreach ((string id, MapData map) in Maps(boards))
        {
            int byBoards = map.Boards.Sum(b => b.Width * b.Height) + BoardGenFixtures.Corridors(map).Sum(c => c.Cells.Length);
            Assert.Equal(byBoards, map.PlayableCount);
            Assert.True(map.PlayableCount is >= 300 and <= 800, $"{id} 的可落子格为 {map.PlayableCount}。");
        }
    }

    [Theory]
    [MemberData(nameof(BoardCounts))]
    public void 棋盘无障碍(int boards)
    {
        // Scenario：每块棋盘外接矩形内的可落子格数恰等于矩形面积，宽与高都不小于 5。
        // 连同「布局规则」的边长区间：出生棋盘 5–7；公共棋盘 9–11，且至少一块公共棋盘的宽与高都为 11。
        foreach ((string id, MapData map) in Maps(boards))
        {
            foreach (BoardPlate plate in map.Boards)
            {
                Assert.Equal(plate.Width * plate.Height, plate.Cells().Count(map.IsPlayable));
                Assert.True(plate.Width >= 5 && plate.Height >= 5, id);
                (int min, int max) = plate.Kind == BoardPlateKind.Birth ? (5, 7) : (9, 11);
                Assert.InRange(plate.Width, min, max);
                Assert.InRange(plate.Height, min, max);
            }

            Assert.Contains(map.Boards, b => b.Kind == BoardPlateKind.Public && b.Width == 11 && b.Height == 11);

            // 出生棋盘在清单里排在前面，第 i 块 = 出生区 i。
            Assert.All(map.Boards.Take(5), b => Assert.Equal(BoardPlateKind.Birth, b.Kind));
            for (int i = 0; i < 5; i++)
            {
                Assert.Equal(map.BirthZones[i].Order(), map.Boards[i].Cells().Order());
            }
        }
    }

    [Theory]
    [MemberData(nameof(BoardCounts))]
    public void 棋盘离地图外缘至少2格_两两至少间隔2格(int boards)
    {
        foreach ((string id, MapData map) in Maps(boards))
        {
            foreach (BoardPlate b in map.Boards)
            {
                Assert.True(
                    b.Origin.X >= 2 && b.Origin.Y >= 2 && b.Origin.X + b.Width <= map.Width - 2 && b.Origin.Y + b.Height <= map.Height - 2,
                    $"{id}：{b} 离外缘不足 2 格。");
            }

            // 把一块棋盘向四周各扩 2 格，仍不与任何别的棋盘相交。
            for (int i = 0; i < map.Boards.Length; i++)
            {
                BoardPlate a = map.Boards[i];
                var grown = new BoardPlate(new Coord(a.Origin.X - 2, a.Origin.Y - 2), a.Width + 4, a.Height + 4, a.Kind);
                for (int j = 0; j < map.Boards.Length; j++)
                {
                    if (i != j)
                    {
                        Assert.DoesNotContain(map.Boards[j].Cells(), grown.Contains);
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(BoardCounts))]
    public void 棋盘之外只有通道与场景(int boards)
    {
        // Scenario：不属于任何棋盘的格子，每一格要么是 3–4 格宽的通道格，要么是障碍格。
        // 通道：直条（填满外接矩形）、恰贴两块棋盘、窄边 3–4、沿两块棋盘连线方向长 2–4。
        foreach ((string id, MapData map) in Maps(boards))
        {
            List<BoardGenFixtures.Corridor> corridors = BoardGenFixtures.Corridors(map);
            Coord[] corridorCells = [.. corridors.SelectMany(c => c.Cells).Order()];
            foreach (Coord c in map.AllCoords().Where(c => BoardGenFixtures.BoardOf(map, c) < 0))
            {
                Assert.True(map.Obstacles.Contains(c) || corridorCells.Contains(c), $"{id}：{c} 既不是障碍也不是通道。");
            }

            Assert.True(corridors.Count >= boards - 1, id);   // 连成一片至少要 棋盘数 − 1 条
            foreach (BoardGenFixtures.Corridor corridor in corridors)
            {
                Assert.Equal(corridor.Columns * corridor.Rows, corridor.Cells.Length);
                Assert.Equal(2, corridor.Boards.Length);

                // 方向：两块棋盘在通道的东西两端（通道横走）还是南北两端（竖走），从棋盘与通道的相对位置判断。
                bool horizontal = Horizontal(map, corridor);
                int length = horizontal ? corridor.Columns : corridor.Rows;
                int width = horizontal ? corridor.Rows : corridor.Columns;
                Assert.True(width is 3 or 4, $"{id}：通道 {corridor.Cells[0]} 宽 {width}。");
                Assert.True(length is >= 2 and <= 4, $"{id}：通道 {corridor.Cells[0]} 长 {length}。");
            }

            // 通道格全部标为咽喉，咽喉也只有通道格。
            Assert.Equal(corridorCells, map.ChokePoints.Order());
        }

        // 样本口径：80 张里 3 格宽与 4 格宽的通道都出现过（宽取垂直于通道走向的那一边——长 2 的通道比它的宽还短，不能取较小者）。
        int[] widths = [.. BoardGenFixtures.Sample()
            .Select(s => BoardGenFixtures.Generated(s.Seed, s.Boards).Map)
            .SelectMany(map => BoardGenFixtures.Corridors(map).Select(c => Horizontal(map, c) ? c.Rows : c.Columns)).Distinct().Order()];
        Assert.Equal([3, 4], widths);
    }

    /// <summary>通道横走（两块棋盘在它的东西两端）还是竖走，按它所贴的第一块棋盘与通道的相对位置判断。</summary>
    private static bool Horizontal(MapData map, BoardGenFixtures.Corridor corridor)
    {
        BoardPlate first = map.Boards[corridor.Boards[0]];
        return first.Origin.X + first.Width <= corridor.Cells.Min(c => c.X) || first.Origin.X > corridor.Cells.Max(c => c.X);
    }

    [Theory]
    [MemberData(nameof(BoardCounts))]
    public void 出生棋盘直通公共棋盘(int boards)
    {
        // Scenario：每块出生棋盘有 2–4 条通道，通向至少两块不同的棋盘，且至少一条的另一端是公共棋盘（2026-09-30 裁决）。
        foreach ((string id, MapData map) in Maps(boards))
        {
            List<BoardGenFixtures.Corridor> corridors = BoardGenFixtures.Corridors(map);
            for (int i = 0; i < map.Boards.Length; i++)
            {
                if (map.Boards[i].Kind != BoardPlateKind.Birth)
                {
                    continue;
                }

                int[] others = [.. corridors.Where(c => c.Boards.Contains(i)).Select(c => c.Boards.Single(other => other != i))];
                Assert.True(others.Length is >= 2 and <= 4, $"{id}：第 {i + 1} 块出生棋盘有 {others.Length} 条通道，应为 2–4。");
                Assert.True(others.Distinct().Count() >= 2, $"{id}：第 {i + 1} 块出生棋盘的通道全部通向同一块棋盘。");
                Assert.True(
                    others.Any(other => map.Boards[other].Kind == BoardPlateKind.Public),
                    $"{id}：第 {i + 1} 块出生棋盘没有直通公共棋盘的通道。");
            }
        }
    }

    [Theory]
    [MemberData(nameof(BoardCounts))]
    public void 每块棋盘的通道数(int boards)
    {
        // 「出生棋盘 2–4 条通道；公共棋盘 1–6 条」：全部样本严格成立。样本口径：确有出生棋盘接到 3 条以上、公共棋盘接到 4 条以上的图。
        int birthAbove2 = 0;
        int publicAbove3 = 0;
        foreach ((string id, MapData map) in Maps(boards))
        {
            List<BoardGenFixtures.Corridor> corridors = BoardGenFixtures.Corridors(map);
            for (int i = 0; i < map.Boards.Length; i++)
            {
                int count = corridors.Count(c => c.Boards.Contains(i));
                (int min, int max) = map.Boards[i].Kind == BoardPlateKind.Birth ? (2, 4) : (1, 6);
                Assert.True(count >= min && count <= max, $"{id}：第 {i + 1} 块棋盘有 {count} 条通道，应为 {min}–{max}。");
                birthAbove2 += map.Boards[i].Kind == BoardPlateKind.Birth && count > 2 ? 1 : 0;
                publicAbove3 += map.Boards[i].Kind == BoardPlateKind.Public && count > 3 ? 1 : 0;
            }
        }

        Assert.True(birthAbove2 > 0 && publicAbove3 > 0, $"样本口径：出生棋盘 >2 条 {birthAbove2} 块、公共棋盘 >3 条 {publicAbove3} 块。");
    }

    [Theory]
    [MemberData(nameof(BoardCounts))]
    public void 全部棋盘连通(int boards)
    {
        // Scenario：任取两块棋盘沿气边求最短路 → 通路存在。从第 1 块棋盘的一格出发沿气边泛洪，应到达全部可落子格。
        foreach ((string id, MapData map) in Maps(boards))
        {
            Coord start = map.Boards[0].Origin;
            var seen = new HashSet<Coord> { start };
            var queue = new Queue<Coord>([start]);
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

            Assert.All(map.Boards, b => Assert.All(b.Cells(), c => Assert.Contains(c, seen)));
            Assert.True(seen.Count == map.PlayableCount, id);
        }
    }

    [Fact]
    public void 资源布点()
    {
        // Scenario：棋盘数 7、两块公共棋盘分别为 9×10 与 11×11 → 信物格共 10 个：出生棋盘 5 个、公共棋盘 2 + 3 个，11×11 棋盘上有 1 个高档。
        // 边长不靠找种子：测试入口直接给定各棋盘的宽高，其余（摆放、通道、布点、校验闭环）与公开入口同一条路。
        BoardSize[] births = [new(5, 5), new(6, 5), new(7, 7), new(5, 7), new(6, 6)];
        BoardSize[] publics = [new(9, 10), new(11, 11)];
        MapData map = BoardMapGenerator.GenerateDetailed(
            4242, new BoardMapParameters { BoardCount = 7 }, BoardMapGenerator.DefaultMaxAttempts, births, publics).Map;

        Assert.True(MapValidator.Validate(map).IsValid);
        BoardPlate small = Assert.Single(map.Boards, b => b.Kind == BoardPlateKind.Public && b.Width * b.Height == 90);
        BoardPlate large = Assert.Single(map.Boards, b => b.Kind == BoardPlateKind.Public && b.Width == 11 && b.Height == 11);
        Assert.Equal(births.Select(b => (b.Width, b.Height)), map.Boards.Take(5).Select(b => (b.Width, b.Height)));

        Assert.Equal(10, map.RelicCells.Count);
        Assert.All(map.Boards.Take(5), b => Assert.Single(map.RelicCells.Keys, b.Contains));
        Assert.Equal(2, map.RelicCells.Keys.Count(small.Contains));
        Assert.Equal(3, map.RelicCells.Keys.Count(large.Contains));
        KeyValuePair<Coord, RelicCellSpec> high = Assert.Single(map.RelicCells, kv => kv.Value.Budget == BudgetTier.High);
        Assert.True(large.Contains(high.Key));
        Assert.Equal(new Coord(large.Origin.X + 5, large.Origin.Y + 5), map.CentralEntrance);   // 11×11 的中心格
    }

    [Theory]
    [MemberData(nameof(BoardCounts))]
    public void 信物格与中央入口(int boards)
    {
        // 布局规则：出生棋盘各 1 个；公共棋盘边长（宽高较小者）9–10 各 2 个、11–15 各 3 个，归公共争夺区；
        // 面积最大的公共棋盘上有 1 个高档；信物格不在棋盘最外一圈；中央入口 = 面积最大的公共棋盘的中心格（并列取清单靠前者）。
        foreach ((string id, MapData map) in Maps(boards))
        {
            int expected = 0;
            foreach (BoardPlate plate in map.Boards)
            {
                KeyValuePair<Coord, RelicCellSpec>[] relics = [.. map.RelicCells.Where(kv => plate.Contains(kv.Key)).OrderBy(kv => kv.Key)];
                int want = plate.Kind == BoardPlateKind.Birth ? 1 : Math.Min(plate.Width, plate.Height) <= 10 ? 2 : 3;
                expected += want;
                Assert.True(relics.Length == want, $"{id}：{plate} 上有 {relics.Length} 个信物格，应为 {want}。");
                Assert.All(relics, kv => Assert.Equal(
                    plate.Kind == BoardPlateKind.Birth ? RelicZone.BirthZone : RelicZone.Contested, kv.Value.Zone));
                Assert.All(relics, kv => Assert.True(
                    kv.Key.X > plate.Origin.X && kv.Key.X < plate.Origin.X + plate.Width - 1
                    && kv.Key.Y > plate.Origin.Y && kv.Key.Y < plate.Origin.Y + plate.Height - 1,
                    $"{id}：信物格 {kv.Key} 在 {plate} 的最外一圈。"));
            }

            Assert.Equal(expected, map.RelicCells.Count);

            BoardPlate largest = map.Boards.Where(b => b.Kind == BoardPlateKind.Public).MaxBy(b => b.Width * b.Height);   // MaxBy 并列取先出现者
            Assert.Equal(
                new Coord(largest.Origin.X + ((largest.Width - 1) / 2), largest.Origin.Y + ((largest.Height - 1) / 2)), map.CentralEntrance);
            KeyValuePair<Coord, RelicCellSpec> high = Assert.Single(map.RelicCells, kv => kv.Value.Budget == BudgetTier.High);
            Assert.True(largest.Contains(high.Key), id);
        }
    }

    [Theory]
    [MemberData(nameof(BoardCounts))]
    public void 不投放地表_高度_栅栏_深水与桥(int boards)
    {
        // 「生成器 MUST NOT 在棋盘档地图上投放林地、荒漠、沼泽、岩台、浅滩、栅栏、深水与桥」；全图同一高度（design D3）。
        foreach ((_, MapData map) in Maps(boards))
        {
            Assert.Empty(map.TerrainData.Surfaces);
            Assert.Empty(map.TerrainData.Heights);
            Assert.Empty(map.TerrainData.Bridges);
            Assert.Empty(map.TerrainData.Fences);
        }
    }

    [Fact]
    public void 生成耗时()
    {
        // Scenario：生成任一棋盘档地图 → 耗时不超过 1 秒（含校验与重试）。不走缓存；先预热 JIT。
        // 取棋盘数 7–10 各 8 颗种子共 32 张，逐张断言；实测见段 B 修正的实施报告。
        BoardMapGenerator.Generate(1000);
        var slow = new List<string>();
        for (int boards = BoardMapParameters.MinBoards; boards <= BoardMapParameters.MaxBoards; boards++)
        {
            for (ulong seed = 101; seed <= 108; seed++)
            {
                var watch = Stopwatch.StartNew();
                BoardMapGenerator.Generate(seed, new BoardMapParameters { BoardCount = boards });
                watch.Stop();
                if (watch.ElapsedMilliseconds > 1000)
                {
                    slow.Add($"board:{seed}:n{boards} {watch.ElapsedMilliseconds} ms");
                }
            }
        }

        Assert.True(slow.Count == 0, "生成耗时超过 1 秒：" + string.Join("；", slow));
    }
}
