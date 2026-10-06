using System.Diagnostics;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.MapGeneration;

/// <summary>
/// 规格：openspec/changes/board-isolated-gen/specs/map-generation —— Requirement: 棋盘档布局规则。
/// 样本：地图种子 1–20 × 每个人数（2 / 3 / 4）的每个合法棋盘数，共 200 张（<see cref="BoardGenFixtures.Sample"/>）。断言一律从地图数据反推，不读生成器的工作态。
/// </summary>
/// <remarks>
/// 变异验证（board-isolated-gen 段 A 实跑，补改——人数进入随机流、边长整组重抽——之后 L1–L7 全部重跑，红数如下；生成器有校验闭环与布局自检，单改摆法只会换一次尝试、图仍合规，
/// 所以有几条是把摆法与校验器 / 自检里对应的那一项一起改掉；红数按 Theory 行计）：
/// （2 人收窄为 4–5 后共 10 组配置，下列为收窄后的重跑红数）
/// L1 目标带上限 200p → 260p（只跑 规模落在目标带）→ 红 5；
/// L2 棋盘最小间隔 2 → 1（工作态 MinGap 与校验器 MinGap 一起改；只跑该测试）→ 红 10：棋盘离地图外缘至少2格_两两至少间隔2格 全部 10 行；
/// L3 公共棋盘边长下限 7 → 6（工作态常量与校验器一起改，自检读同一常量）→ 红 9：棋盘无障碍 ×8、边长覆盖整个区间；
/// L4 主战场下限 11 → 9（抽样、自检同一常量）→ 红 10：有主战场 全部 10 行；
/// L5 较短边 ≤ 8 放 1 个改成 ≤ 6（7–8 放 2 个）→ 红 9：信物格与中央入口 ×8、资源布点；
/// L6 出生棋盘数 = 人数 + 2 → 红 94（本类除 边长整组重抽 外全部：校验预算对不上，生成作废到上限）；
/// L7 生成器在第 1 块棋盘东侧留一格可落子格、校验器的"棋盘外可落子格"判据改成运行时恒假 → 红 20：棋盘之外只有场景 ×10、棋盘互不连通 ×10；
/// T1 只改测试不改实现：规模落在目标带 里 3 人与 2 人的目标带对调 → 红 4（防测试照抄实现的算式 75p–200p）。
/// builtin-board-maps 段 A（出生棋盘同尺寸；跑 FullyQualifiedName~棋盘档，红数按 Theory 行计）：
/// B1 抽样改回每块出生棋盘各抽宽高、校验器的尺寸一致判据同时改成运行时恒放行（odd.Length >= 0 即返回）→ 红 17：出生棋盘同尺寸 ×10、边长整组重抽 ×3、黄金值，
///    外加 棋盘档预算与校验Tests 的 出生棋盘尺寸不一、出生棋盘越界 ×2（校验器那一半）；
/// B2 出生棋盘不转向（朝向数恒 1）→ 红 1：出生棋盘尺寸逐图抽取且可转向；
/// B3 出生尺寸恒 6×6（仍消耗两个随机数）→ 红 3：出生棋盘尺寸逐图抽取且可转向、边长覆盖整个区间、黄金值；
/// G1 去掉测试入口"给定出生尺寸不一即报参数错误"（条件加运行时恒假）→ 红 1：资源布点。
/// </remarks>
public class 棋盘档布局规则Tests
{
    public static TheoryData<int, int> Configurations()
    {
        var data = new TheoryData<int, int>();
        foreach ((int players, int boards) in BoardGenFixtures.Configurations())
        {
            data.Add(players, boards);
        }

        return data;
    }

    private static IEnumerable<(string Id, MapData Map)> Maps(int players, int boards) =>
        Enumerable.Range(1, 20).Select(seed => (BoardGenFixtures.Label((ulong)seed, players, boards), BoardGenFixtures.Generated((ulong)seed, boards, players).Map));

    [Theory]
    [MemberData(nameof(Configurations))]
    public void 生成图通过校验(int players, int boards)
    {
        // Scenario：对 2、3、4 人，用地图种子 1 到 20 与该人数的每个合法棋盘数各生成一张 → 每一张都通过棋盘档静态校验。
        foreach ((string id, MapData map) in Maps(players, boards))
        {
            MapValidationResult result = MapValidator.Validate(map);
            Assert.True(result.IsValid, $"{id}：{result}\n{BoardGenFixtures.TextArt(map)}");
            Assert.Equal(3, result.Reports.Length);   // 到三类目标的距离只报告（不可达）
            Assert.Equal((players, boards, players + 1), (map.MaxPlayers, map.Boards.Length, map.BirthZones.Length));
            GameBoard.Load(map);
        }
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void 规模落在目标带(int players, int boards)
    {
        // Scenario：各生成一张并统计可落子格 → 4 人 300–800、3 人 225–600、2 人 150–400（区间取自规格，测试内独立写出）。
        // 独立算式：可落子格 = Σ 棋盘面积（棋盘之外一格可落子格都没有）。
        (int min, int max) = players switch { 4 => (300, 800), 3 => (225, 600), _ => (150, 400) };
        foreach ((string id, MapData map) in Maps(players, boards))
        {
            Assert.Equal(map.Boards.Sum(b => b.Width * b.Height), map.PlayableCount);
            Assert.True(map.PlayableCount >= min && map.PlayableCount <= max, $"{id} 的可落子格为 {map.PlayableCount}，应在 {min}–{max}。");
        }
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void 棋盘无障碍(int players, int boards)
    {
        // Scenario：每块棋盘外接矩形内的可落子格数恰等于矩形面积；出生棋盘宽高在 5–7，公共棋盘宽高在 7–15。
        // 连同「出生棋盘数 = 人数 + 1」：出生棋盘排在清单最前，第 i 块 = 出生区 i。
        foreach ((string id, MapData map) in Maps(players, boards))
        {
            foreach (BoardPlate plate in map.Boards)
            {
                Assert.Equal(plate.Width * plate.Height, plate.Cells().Count(map.IsPlayable));
                (int min, int max) = plate.Kind == BoardPlateKind.Birth ? (5, 7) : (7, 15);
                Assert.True(plate.Width >= min && plate.Width <= max && plate.Height >= min && plate.Height <= max, $"{id}：{plate} 的边长应在 {min}–{max}。");
            }

            Assert.Equal(players + 1, map.Boards.Count(b => b.Kind == BoardPlateKind.Birth));
            Assert.All(map.Boards.Take(players + 1), b => Assert.Equal(BoardPlateKind.Birth, b.Kind));
            for (int i = 0; i <= players; i++)
            {
                Assert.Equal(map.BirthZones[i].Order(), map.Boards[i].Cells().Order());
            }
        }
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void 出生棋盘同尺寸(int players, int boards)
    {
        // Scenario（builtin-board-maps）：对 2、3、4 人，用地图种子 1 到 20 与该人数的每个合法棋盘数各生成一张 → 每张图的全部出生棋盘的 {宽, 高} 无序对相同。
        // 变异记录见类注释 B1–B3。
        foreach ((string id, MapData map) in Maps(players, boards))
        {
            (int, int)[] sizes = [.. map.Boards.Where(b => b.Kind == BoardPlateKind.Birth).Select(b => (Math.Min(b.Width, b.Height), Math.Max(b.Width, b.Height)))];
            Assert.Equal(players + 1, sizes.Length);
            Assert.True(sizes.All(s => s == sizes[0]), $"{id}：出生棋盘尺寸为 {string.Join("、", map.Boards.Where(b => b.Kind == BoardPlateKind.Birth).Select(b => $"{b.Width}×{b.Height}"))}。");
        }
    }

    [Fact]
    public void 出生棋盘尺寸逐图抽取且可转向()
    {
        // 样本口径（防"同尺寸"靠写死一个尺寸做到、或转向这条路从没走到）：200 张里出生棋盘的无序尺寸不止一种，
        // 且确有图的出生棋盘同时出现 w×h 与 h×w 两种朝向（w ≠ h）。
        var kinds = new SortedSet<(int, int)>();
        int rotated = 0;
        foreach ((ulong seed, int players, int boards) in BoardGenFixtures.Sample())
        {
            BoardPlate[] births = [.. BoardGenFixtures.Generated(seed, boards, players).Map.Boards.Where(b => b.Kind == BoardPlateKind.Birth)];
            kinds.Add((Math.Min(births[0].Width, births[0].Height), Math.Max(births[0].Width, births[0].Height)));
            rotated += births.Any(b => b.Width != b.Height) && births.Select(b => (b.Width, b.Height)).Distinct().Count() > 1 ? 1 : 0;
        }

        Assert.True(kinds.Count >= 4, $"样本里出生棋盘只有 {kinds.Count} 种尺寸。");
        Assert.True(rotated > 0, "样本里没有一张图的出生棋盘出现两种朝向。");
    }

    [Fact]
    public void 边长覆盖整个区间()
    {
        // 样本口径：200 张里出生棋盘边长 5 与 7、公共棋盘边长 7 与 15 都出现过——区间两端确实被抽到，不是只在中间取值。
        int[] birthSides = [.. BoardGenFixtures.Sample()
            .SelectMany(s => BoardGenFixtures.Generated(s.Seed, s.Boards, s.Players).Map.Boards.Where(b => b.Kind == BoardPlateKind.Birth))
            .SelectMany(b => new[] { b.Width, b.Height }).Distinct().Order()];
        int[] publicSides = [.. BoardGenFixtures.Sample()
            .SelectMany(s => BoardGenFixtures.Generated(s.Seed, s.Boards, s.Players).Map.Boards.Where(b => b.Kind == BoardPlateKind.Public))
            .SelectMany(b => new[] { b.Width, b.Height }).Distinct().Order()];

        Assert.Equal([5, 6, 7], birthSides);
        Assert.Equal(7, publicSides[0]);
        Assert.Equal(15, publicSides[^1]);
    }

    [Theory]
    [InlineData(4, 10)]
    [InlineData(3, 8)]
    [InlineData(2, 5)]
    public void 边长整组重抽(int players, int boards)
    {
        // design D2「预算抽样」（主会话裁决：重抽，不缩边收敛）：一次尝试内按原分布整组重抽，直到总格数落进目标带且主战场合格，上限 200 组。
        // 取每个人数最紧的一档、种子 1–20 的第 0 次尝试：都在尝试内抽成，被接受的一组落在目标带内、边长都在原区间内；
        // 样本口径：确有种子需要重抽（抽了不止一组），否则"重抽"这条路径从没被走到。
        // 变异 R1（重抽上限 200 → 1）→ 本测试红 3（三行全红）。
        (int min, int max) = players switch { 4 => (300, 800), 3 => (225, 600), _ => (150, 400) };
        int redrawn = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            bool ok = BoardMapLayout.TrySampleSizes(
                MapRandom.ForBoardAttempt(seed, players, 0), players + 1, boards - players - 1, min, max,
                out BoardSize[] births, out BoardSize[] publics, out int draws, out string reason);
            Assert.True(ok, $"{BoardGenFixtures.Label(seed, players, boards)}：{reason}");
            Assert.InRange(draws, 1, 200);
            int area = births.Sum(b => b.Width * b.Height) + publics.Sum(b => b.Width * b.Height);
            Assert.InRange(area, min, max);
            Assert.All(births, b => Assert.True(b.Width is >= 5 and <= 7 && b.Height is >= 5 and <= 7));
            Assert.All(births, b => Assert.Equal(births[0], b));   // builtin-board-maps D1：每组只抽一次出生尺寸
            Assert.True(publics[0].Width >= 11 && publics[0].Height >= 11 && publics[0].Width <= 15 && publics[0].Height <= 15);
            Assert.All(publics, b => Assert.True(b.Width is >= 7 and <= 15 && b.Height is >= 7 and <= 15));
            redrawn += draws > 1 ? 1 : 0;
        }

        Assert.True(redrawn > 0, "样本口径：种子 1–20 都是第一组就合格，重抽路径没被走到。");
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void 有主战场(int players, int boards)
    {
        // Scenario：面积最大的公共棋盘（并列取清单靠前者）宽与高都不小于 11，且其上有 1 个高档信物。
        foreach ((string id, MapData map) in Maps(players, boards))
        {
            BoardPlate largest = map.Boards.Where(b => b.Kind == BoardPlateKind.Public).MaxBy(b => b.Width * b.Height);   // MaxBy 并列取先出现者
            Assert.True(largest.Width >= 11 && largest.Height >= 11, $"{id}：面积最大的公共棋盘为 {largest.Width}×{largest.Height}。");
            Assert.Single(map.RelicCells, kv => kv.Value.Budget == BudgetTier.High && largest.Contains(kv.Key));
        }
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void 棋盘离地图外缘至少2格_两两至少间隔2格(int players, int boards)
    {
        foreach ((string id, MapData map) in Maps(players, boards))
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
    [MemberData(nameof(Configurations))]
    public void 棋盘之外只有场景(int players, int boards)
    {
        // Scenario：不属于任何棋盘的格子，每一格都是障碍格；棋盘档地图不标咽喉。
        foreach ((string id, MapData map) in Maps(players, boards))
        {
            Coord[] outside = [.. map.AllCoords().Where(c => BoardGenFixtures.BoardOf(map, c) < 0)];
            Assert.True(outside.Length > 0, id);
            Assert.All(outside, c => Assert.True(map.Obstacles.Contains(c) && !map.IsPlayable(c), $"{id}：{c} 不在任何棋盘内却不是障碍。"));
            Assert.Empty(map.ChokePoints);
        }
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void 棋盘互不连通(int players, int boards)
    {
        // Scenario：任取分属两块不同棋盘的两格 → 两格之间不存在沿四邻接的可落子格通路。
        // 判据一：从每块棋盘的一格出发沿气边泛洪，到达的格子恰是这块棋盘的全部格子。
        // 判据二（与气边无关的几何口径）：不同棋盘的两格不存在几何四邻接。
        foreach ((string id, MapData map) in Maps(players, boards))
        {
            foreach (BoardPlate plate in map.Boards)
            {
                var seen = new HashSet<Coord> { plate.Origin };
                var queue = new Queue<Coord>([plate.Origin]);
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

                Assert.True(seen.SetEquals(plate.Cells()), $"{id}：从 {plate} 出发沿气边到达 {seen.Count} 格，应恰为本棋盘的 {plate.Width * plate.Height} 格。");
            }

            foreach (Coord c in map.AllCoords())
            {
                int board = BoardGenFixtures.BoardOf(map, c);
                if (board < 0)
                {
                    continue;
                }

                foreach (Coord n in new[] { (c.X + 1, c.Y), (c.X, c.Y + 1) }.Where(p => p.Item1 < map.Width && p.Item2 < map.Height).Select(p => new Coord(p.Item1, p.Item2)))
                {
                    int other = BoardGenFixtures.BoardOf(map, n);
                    Assert.True(other < 0 || other == board, $"{id}：{c} 与 {n} 分属第 {board + 1} 与第 {other + 1} 块棋盘却四邻接。");
                }
            }
        }
    }

    [Fact]
    public void 资源布点()
    {
        // Scenario：4 人、棋盘数 8、三块公共棋盘分别为 7×8、9×10 与 11×11 → 信物格共 11 个：出生棋盘 5 个、公共棋盘 1 + 2 + 3 个，11×11 棋盘上有 1 个高档。
        // 边长不靠找种子：测试入口直接给定各棋盘的宽高（第一块公共棋盘放在中央），其余（摆放、布点、校验闭环）与公开入口同一条路。
        // builtin-board-maps D1：出生棋盘同尺寸，给定 5 块 6×5（摆放时可转成 5×6）；给定尺寸不一的出生棋盘是参数错误。
        BoardSize[] births = [new(6, 5), new(6, 5), new(6, 5), new(6, 5), new(6, 5)];
        BoardSize[] publics = [new(11, 11), new(9, 10), new(7, 8)];
        MapData map = BoardMapGenerator.GenerateDetailed(
            4242, new BoardMapParameters { BoardCount = 8 }, BoardMapGenerator.DefaultMaxAttempts, births, publics).Map;
        Assert.Throws<ArgumentException>(() => BoardMapGenerator.GenerateDetailed(
            4242, new BoardMapParameters { BoardCount = 8 }, BoardMapGenerator.DefaultMaxAttempts, [new(6, 5), new(5, 6), new(6, 6), new(6, 5), new(6, 5)], publics));

        Assert.True(MapValidator.Validate(map).IsValid);
        BoardPlate small = Assert.Single(map.Boards, b => b.Kind == BoardPlateKind.Public && b.Width * b.Height == 56);
        BoardPlate middle = Assert.Single(map.Boards, b => b.Kind == BoardPlateKind.Public && b.Width * b.Height == 90);
        BoardPlate large = Assert.Single(map.Boards, b => b.Kind == BoardPlateKind.Public && b.Width == 11 && b.Height == 11);
        Assert.All(map.Boards.Take(5), b => Assert.Equal((5, 6), (Math.Min(b.Width, b.Height), Math.Max(b.Width, b.Height))));

        Assert.Equal(11, map.RelicCells.Count);
        Assert.All(map.Boards.Take(5), b => Assert.Single(map.RelicCells.Keys, b.Contains));
        Assert.Equal(1, map.RelicCells.Keys.Count(small.Contains));
        Assert.Equal(2, map.RelicCells.Keys.Count(middle.Contains));
        Assert.Equal(3, map.RelicCells.Keys.Count(large.Contains));
        KeyValuePair<Coord, RelicCellSpec> high = Assert.Single(map.RelicCells, kv => kv.Value.Budget == BudgetTier.High);
        Assert.True(large.Contains(high.Key));
        Assert.Equal(new Coord(large.Origin.X + 5, large.Origin.Y + 5), map.CentralEntrance);   // 11×11 的中心格
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void 信物格与中央入口(int players, int boards)
    {
        // 布局规则：出生棋盘各 1 个；公共棋盘按较短边 7–8 各 1 个、9–10 各 2 个、11–15 各 3 个，归公共争夺区；
        // 面积最大的公共棋盘上有 1 个高档；信物格不在棋盘最外一圈；中央入口 = 面积最大的公共棋盘的中心格（并列取清单靠前者）。
        foreach ((string id, MapData map) in Maps(players, boards))
        {
            int expected = 0;
            foreach (BoardPlate plate in map.Boards)
            {
                KeyValuePair<Coord, RelicCellSpec>[] relics = [.. map.RelicCells.Where(kv => plate.Contains(kv.Key)).OrderBy(kv => kv.Key)];
                int shorter = Math.Min(plate.Width, plate.Height);
                int want = plate.Kind == BoardPlateKind.Birth ? 1 : shorter <= 8 ? 1 : shorter <= 10 ? 2 : 3;
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

    [Fact]
    public void 信物三档都在样本里出现()
    {
        // 样本口径：200 张里较短边 7–8、9–10、11–15 的公共棋盘都出现过，三档信物数确实都被上面的断言检查到。
        int[] shorter = [.. BoardGenFixtures.Sample()
            .SelectMany(s => BoardGenFixtures.Generated(s.Seed, s.Boards, s.Players).Map.Boards.Where(b => b.Kind == BoardPlateKind.Public))
            .Select(b => Math.Min(b.Width, b.Height))];
        Assert.Contains(shorter, s => s <= 8);
        Assert.Contains(shorter, s => s is 9 or 10);
        Assert.Contains(shorter, s => s >= 11);
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void 不投放地表_高度_栅栏_深水与桥(int players, int boards)
    {
        // 「生成器 MUST NOT 在棋盘档地图上投放林地、荒漠、沼泽、岩台、浅滩、栅栏、深水与桥」；全图同一高度。
        foreach ((_, MapData map) in Maps(players, boards))
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
        // 取每个（人数, 棋盘数）各 4 颗种子共 44 张，逐张断言。
        BoardMapGenerator.Generate(1000);
        var slow = new List<string>();
        foreach ((int players, int boards) in BoardGenFixtures.Configurations())
        {
            for (ulong seed = 101; seed <= 104; seed++)
            {
                var watch = Stopwatch.StartNew();
                BoardMapGenerator.Generate(seed, new BoardMapParameters { Players = players, BoardCount = boards });
                watch.Stop();
                if (watch.ElapsedMilliseconds > 1000)
                {
                    slow.Add($"{BoardGenFixtures.Label(seed, players, boards)} {watch.ElapsedMilliseconds} ms");
                }
            }
        }

        Assert.True(slow.Count == 0, "生成耗时超过 1 秒：" + string.Join("；", slow));
    }
}
