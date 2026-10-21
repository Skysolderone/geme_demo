using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Relics;
using Xunit.Abstractions;

namespace Siege.Core.Tests.LinkMapGeneration;

/// <summary>
/// 规格：openspec/changes/open-map/specs/link-map-generation —— Requirement: 初始布局（link-map-terrain 第 2 组：边长 6–8、主题、外圈 / 核心区 / 中间带；open-map：3+2 紧贴连通）。
/// </summary>
/// <remarks>
/// 变异验证（段 F 实跑，只跑本类）：M-F3 <c>Assemble</c> 的东侧留白改为 1 → 「画布按布局裁出」全红。
/// open-map 1.1 变异（只跑本类）：M-O1 <c>InitialLayout.Arrange</c> 第一行间距改为 1 → 「三加二布局」「每条接缝至少一条气边」「地图只有出生棋盘且连成一片」全红。
/// link-map-terrain 第 2 组的变异记录见任务汇报（主题排列、核心区偏移、出生区信物相邻、公共信物档位）。
/// link-map-surfaces 2.1：加「新地表按主题投放」「新地表不改其余地形」「种子1到20每块新地表格数」（变异记录见 .trellis/tasks/10-20-link-map-surfaces/notes.md）。
/// </remarks>
public class 初始布局Tests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    public static IEnumerable<object[]> Seeds() => Enumerable.Range(1, 20).Select(s => new object[] { (ulong)s });

    [Theory]
    [MemberData(nameof(Seeds))]
    public void 初始图只有出生棋盘(ulong seed)
    {
        // Scenario：link:1–link:20 每张恰有 5 块出生棋盘，宽与高都在 6–8 之间，棋盘清单中没有其他棋盘，棋盘之外没有可落子格。
        MapData map = LinkMapGenerator.Generate(seed);

        Assert.Equal(MapProfile.Link, map.Profile);
        Assert.Equal(0, map.MapVersion);
        Assert.Equal(4, map.MaxPlayers);
        Assert.Equal(LinkMapGenerator.BirthBoards, map.Boards.Length);
        Assert.All(map.Boards, b => Assert.Equal(BoardPlateKind.Birth, b.Kind));
        Assert.Equal(5, map.BirthZones.Length);
        Assert.All(map.Boards, b => Assert.InRange(b.Width, 6, 8));
        Assert.All(map.Boards, b => Assert.InRange(b.Height, 6, 8));
        Assert.Equal((6, 8), (LinkMapGenerator.BirthMinSide, LinkMapGenerator.BirthMaxSide));

        HashSet<Coord> boardCells = [.. map.Boards.SelectMany(b => b.Cells())];
        Assert.All(map.AllCoords().Where(map.IsPlayable), c => Assert.Contains(c, boardCells));
        // 地形与信物都在棋盘内。
        Assert.All(map.TerrainData.Heights.Keys.Concat(map.TerrainData.Surfaces.Keys).Concat(map.TerrainData.Bridges).Concat(map.RelicCells.Keys), c => Assert.Contains(c, boardCells));
        Assert.All(map.TerrainData.Fences, e => Assert.True(boardCells.Contains(e.A) && boardCells.Contains(e.B), e.ToString()));
        Assert.True(MapValidator.Validate(map).IsValid, MapValidator.Validate(map).ToString());
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void 地图只有出生棋盘且连成一片(ulong seed)
    {
        // Scenario（open-map）：link:1–link:20 每张恰有 5 块出生棋盘，棋盘之外没有可落子格；全部可落子格沿几何四邻接恰成 1 个连通分量；5 块两两不重叠。
        // 连通分量用测试内独立 BFS（只走 Adjacency.Neighbors 与 IsPlayable），不读 Battlefields。
        MapData map = LinkMapGenerator.Generate(seed);
        HashSet<Coord> playable = [.. map.AllCoords().Where(map.IsPlayable)];
        var reached = new HashSet<Coord> { playable.Order().First() };
        var queue = new Queue<Coord>(reached);
        while (queue.Count > 0)
        {
            foreach (Coord n in Adjacency.Neighbors(map.Width, map.Height, queue.Dequeue()).Where(playable.Contains))
            {
                if (reached.Add(n))
                {
                    queue.Enqueue(n);
                }
            }
        }

        Assert.Equal(playable.Count, reached.Count);
        Assert.Equal(1, Battlefields.Of(map).Count);
        Assert.Equal(Enumerable.Range(0, 5), Battlefields.Of(map).All[0].BirthZones);
        for (int i = 0; i < map.Boards.Length; i++)
        {
            for (int j = i + 1; j < map.Boards.Length; j++)
            {
                Assert.False(InitialLayout.Overlaps(map.Boards[i], map.Boards[j]), $"link:{seed}: 棋盘 {i} 与 {j} 重叠");
            }
        }

        Assert.InRange(map.PlayableCount, 202, 258);   // 校验器链接行回写：种子 1–20 实测 202–258（terrain-barriers 2.2；原 212–280）
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void 每条接缝至少一条气边(ulong seed)
    {
        // Scenario（open-map）：任意两块外接矩形相邻的出生棋盘，在接缝上至少有 1 对互为几何邻居的可落子格之间存在气边。
        // 接缝格对由测试内独立算出（沿共享边逐格配对），气边读唯一实现 Adjacency.LibertyNeighbors。
        MapData map = LinkMapGenerator.Generate(seed);
        int seams = 0;
        for (int i = 0; i < map.Boards.Length; i++)
        {
            for (int j = i + 1; j < map.Boards.Length; j++)
            {
                BoardPlate a = map.Boards[i];
                BoardPlate b = map.Boards[j];
                if (InitialLayout.SharedEdge(a, b) == 0)
                {
                    continue;
                }

                seams++;
                var pairs = new List<(Coord P, Coord Q)>();
                foreach (Coord p in a.Cells())
                {
                    foreach (Coord q in Adjacency.Neighbors(map.Width, map.Height, p).Where(b.Contains))
                    {
                        pairs.Add((p, q));
                    }
                }

                Assert.NotEmpty(pairs);
                Assert.True(pairs.Any(x => Adjacency.LibertyNeighbors(map, x.P).Contains(x.Q)), $"link:{seed}: 棋盘 {i} 与 {j} 的接缝没有气边");
            }
        }

        Assert.True(seams >= 4, $"link:{seed}: 只有 {seams} 条接缝");   // 3+2 布局至少 4 条接缝（第一行两条 + 第二行两块各贴一条）
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void 三加二布局(ulong seed)
    {
        // Scenario（open-map D1）：次序 o0、o1、o2 底边对齐、自西向东依次紧贴（间距 0）；o3 的最南行紧贴 o0 的最北行且西边与 o0 对齐；
        // o4 的最南行紧贴 o1 的最北行、与 o1 共享边 ≥ 4、与 o3 不重叠。次序本身不公开，这里按几何反推：第一行 = 底边在留白行的三块（自西向东即 o0 / o1 / o2）。
        // 实施记录 R-1：锚的北侧被挡死（o3 比 o0 宽且 o1 比 o0 高 / o4 比 o1 宽且 o2 比 o1 高）时换锚第一行共享边最长的一块；种子 1–20 里只有 link:7 的 o4 改锚 o2。
        MapData map = LinkMapGenerator.Generate(seed);
        BoardPlate[] row1 = [.. map.Boards.Where(b => b.Origin.Y == LinkMapGenerator.Margin).OrderBy(b => b.Origin.X)];
        BoardPlate[] row2 = [.. map.Boards.Where(b => b.Origin.Y != LinkMapGenerator.Margin).OrderBy(b => b.Origin.X)];
        Assert.Equal(3, row1.Length);
        Assert.Equal(2, row2.Length);
        Assert.Equal(LinkMapGenerator.Margin, row1[0].Origin.X);
        Assert.Equal(row1[0].Origin.X + row1[0].Width, row1[1].Origin.X);
        Assert.Equal(row1[1].Origin.X + row1[1].Width, row1[2].Origin.X);

        // 每块第二行棋盘都北贴第一行的一块，与锚的共享边 ≥ 4（规格「初始布局」）。
        foreach (BoardPlate top in row2)
        {
            int best = row1.Max(a => top.Origin.Y == a.Origin.Y + a.Height ? InitialLayout.SharedEdge(a, top) : 0);
            Assert.True(best >= InitialLayout.MinSharedEdge, $"link:{seed}: 第二行棋盘 {top.Origin} 与锚的共享边只有 {best}");
        }

        // 设计规则本身（o3 锚 o0 西对齐；o4 锚 o1）：除 R-1 的换锚种子外逐条成立。
        BoardPlate o3 = row2[0];
        Assert.Equal(new Coord(LinkMapGenerator.Margin, row1[0].Origin.Y + row1[0].Height), o3.Origin);
        Assert.Equal(Math.Min(row1[0].Width, o3.Width), InitialLayout.SharedEdge(row1[0], o3));
        if (seed != 7)
        {
            BoardPlate o4 = row2[1];
            Assert.Equal(row1[1].Origin.Y + row1[1].Height, o4.Origin.Y);
            Assert.True(InitialLayout.SharedEdge(row1[1], o4) >= InitialLayout.MinSharedEdge);
            Assert.True(o4.Origin.X >= o3.Origin.X + o3.Width);
        }
        else
        {
            Assert.Equal(row1[2].Origin.Y + row1[2].Height, row2[1].Origin.Y);   // link:7：o4（8×8）比 o1（6×6）宽、o2（7×7）比 o1 高 → 换锚 o2
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void 画布按布局裁出(ulong seed)
    {
        // 裁决 J4 / J7 + open-map D1：画布原点固定，按 5 块的包围盒四周各留 2 格裁出一次（之后原点与画布都不再变，MapVersion 恒 0）。
        MapData map = LinkMapGenerator.Generate(seed);
        Assert.Equal(LinkMapGenerator.Margin, map.Boards.Min(b => b.Origin.X));
        Assert.Equal(LinkMapGenerator.Margin, map.Boards.Min(b => b.Origin.Y));
        Assert.Equal(map.Boards.Max(b => b.Origin.X + b.Width) + LinkMapGenerator.Margin, map.Width);
        Assert.Equal(map.Boards.Max(b => b.Origin.Y + b.Height) + LinkMapGenerator.Margin, map.Height);
        Assert.InRange(map.Width, 23, 27);    // 种子 1–20 实测（校验器链接行取理论边界 22–30）
        Assert.InRange(map.Height, 17, 20);   // 种子 1–20 实测（理论边界 16–20）

        // 次序随种子变化（样本口径：20 个种子里不止一种排列）。
        Assert.True(Seeds().Select(s => string.Join(",", LinkMapGenerator.Generate((ulong)s[0]).Boards.OrderBy(b => b.Origin.Y).ThenBy(b => b.Origin.X).Select(b => Array.IndexOf([.. LinkMapGenerator.Generate((ulong)s[0]).Boards], b)))).Distinct().Count() > 1);
    }

    [Fact]
    public void 主题两两不同()
    {
        // Scenario：link:1–link:20 每张的 5 块出生棋盘恰好各取高台、河流、林地、栅栏阵、混合之一，且至少有两张图的主题排列不同。
        var permutations = new HashSet<string>(StringComparer.Ordinal);
        for (ulong seed = 1; seed <= 20; seed++)
        {
            MapData map = LinkMapGenerator.Generate(seed);
            BoardTheme[] themes = [.. map.Boards.Select(b => b.Theme!.Value)];
            Assert.Equal(Enum.GetValues<BoardTheme>().Order(), themes.Order());
            permutations.Add(string.Join(",", themes));
        }

        Assert.True(permutations.Count >= 2, $"20 张图只有 {permutations.Count} 种主题排列");
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void 外圈与核心区平坦(ulong seed)
    {
        // Scenario：外圈每格高度 0、可落子、地表为草地或林地、不带栅栏；核心区 9 格都是高度 0 的草地，含恰 2 个互不相邻的出生区档信物格。
        // 出生区 = 核心区（棋盘中央 3×3，宽或高为偶数时偏西 / 偏南）——这里用测试内独立算式算核心区，不读 BoardPlate.CoreCells。
        MapData map = LinkMapGenerator.Generate(seed);
        for (int i = 0; i < 5; i++)
        {
            BoardPlate b = map.Boards[i];
            int cx = b.Origin.X + ((b.Width - 3) / 2);
            int cy = b.Origin.Y + ((b.Height - 3) / 2);
            HashSet<Coord> core = [.. Enumerable.Range(0, 9).Select(k => new Coord(cx + (k % 3), cy + (k / 3)))];
            Assert.True(map.BirthZones[i].SetEquals(core), $"link:{seed} 棋盘 {i}：出生区不是核心区");

            // terrain-barriers 裁决 H4：河流 / 高台的隔断线两端占外圈各 1 格，这两格除外（隔断线本身见「河流与高台横穿棋盘」）。
            HashSet<Coord> ends = (b.Theme is BoardTheme.River or BoardTheme.Plateau) ? [.. 主题地形Tests.Barrier(b, map).Line] : [];
            foreach (Coord c in b.Cells().Where(c => c.X == b.Origin.X || c.X == b.Origin.X + b.Width - 1 || c.Y == b.Origin.Y || c.Y == b.Origin.Y + b.Height - 1).Where(c => !ends.Contains(c)))
            {
                Assert.True(map.IsPlayable(c) && map.HeightAt(c) == 0 && map.SurfaceAt(c) is Surface.Grass or Surface.Forest, $"link:{seed} 外圈 {c}");
                Assert.DoesNotContain(map.TerrainData.Fences, e => e.A == c || e.B == c);
            }

            foreach (Coord c in core)
            {
                Assert.True(map.IsPlayable(c) && map.HeightAt(c) == 0 && map.SurfaceAt(c) == Surface.Grass && !map.HasBridge(c), $"link:{seed} 核心区 {c}");
            }

            Assert.DoesNotContain(map.TerrainData.Fences, e => core.Contains(e.A) && core.Contains(e.B));
            Coord[] birthRelics = [.. map.RelicCells.Where(kv => b.Contains(kv.Key) && kv.Value == new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth)).Select(kv => kv.Key).Order()];
            Assert.Equal(2, birthRelics.Length);
            Assert.All(birthRelics, r => Assert.Contains(r, core));
            Assert.False(Adjacency.AreAdjacent(birthRelics[0], birthRelics[1]), $"link:{seed} 棋盘 {i} 的两个出生区信物格 {birthRelics[0]} / {birthRelics[1]} 相邻");
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void 棋盘连通且有改造目标(ulong seed)
    {
        // Scenario：每块出生棋盘内可落子格沿几何四邻接连成一片，林地格与未架桥深水格合计不少于 2 个。
        // link-map-surfaces 2.1：去掉"没有荒漠、沼泽、岩台与浅滩"一条（新地表按主题投放进中间带，见「新地表按主题投放」）。
        MapData map = LinkMapGenerator.Generate(seed);
        foreach (BoardPlate b in map.Boards)
        {
            HashSet<Coord> playable = [.. b.Cells().Where(map.IsPlayable)];
            var reached = new HashSet<Coord> { playable.Order().First() };
            var queue = new Queue<Coord>(reached);
            while (queue.Count > 0)
            {
                foreach (Coord n in Adjacency.Neighbors(map.Width, map.Height, queue.Dequeue()).Where(playable.Contains))
                {
                    if (reached.Add(n))
                    {
                        queue.Enqueue(n);
                    }
                }
            }

            Assert.Equal(playable.Count, reached.Count);
            Assert.True(b.Cells().Count(c => map.SurfaceAt(c) == Surface.Forest || (map.SurfaceAt(c) == Surface.DeepWater && !map.HasBridge(c))) >= 2, $"link:{seed} {b.Origin}");
            // 岩石只在中间带、每块至多 2 格散布 + 至多 5 格岩区（terrain-barriers D3）；高差只有 0 / 1 / 2（TerrainData 构造期保证上限）。
            Coord[] rocks = [.. b.Cells().Where(c => !map.IsPlayable(c) && map.SurfaceAt(c) != Surface.DeepWater)];
            Assert.InRange(rocks.Length, 0, 2 + LinkMapGenerator.MaxCluster);
            Assert.All(rocks, r => Assert.True(b.InMiddleBand(r), $"link:{seed} 岩石 {r} 不在中间带"));
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void 公共区信物位置(ulong seed)
    {
        // Scenario：每块至多 1 个公共区档信物格，位于核心区之外，且是该主题定义的难到达格（高台主题为高度 2 的格，河流主题为与核心区隔着深水的一侧）。
        // 档位 (Contested, Standard)（D5）。各主题的难到达格按 design D4 的定义在测试内独立判定。
        MapData map = LinkMapGenerator.Generate(seed);
        foreach (BoardPlate b in map.Boards)
        {
            Coord[] contested = [.. map.RelicCells.Where(kv => b.Contains(kv.Key) && kv.Value.Zone == RelicZone.Contested).Select(kv => kv.Key)];
            Assert.InRange(contested.Length, 0, 1);
            if (contested.Length == 0)
            {
                continue;
            }

            Coord r = contested[0];
            Assert.Equal(new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard), map.RelicCells[r]);
            Assert.Null(map.BirthZoneOf(r));
            Assert.True(map.IsPlayable(r));
            int forestAround = Adjacency.Neighbors(map.Width, map.Height, r).Count(n => map.SurfaceAt(n) == Surface.Forest);
            int fencedSides = map.TerrainData.Fences.Count(e => e.A == r || e.B == r);
            bool hard = b.Theme switch
            {
                BoardTheme.Plateau => map.HeightAt(r) == 2,
                BoardTheme.River => AcrossWater(map, b, r),
                BoardTheme.Forest => forestAround >= 2 && map.SurfaceAt(r) != Surface.Forest,   // 裁决 T2 = A：至少两面挨着林地（原三面）
                BoardTheme.Palisade => fencedSides >= 2,
                BoardTheme.Mixed => map.HeightAt(r) == 1 || Adjacency.Neighbors(map.Width, map.Height, r).Any(w => map.TerrainData.IsUnbridgedDeepWater(w) && b.InCore(new Coord((2 * w.X) - r.X, (2 * w.Y) - r.Y))),
                _ => false,
            };
            Assert.True(hard, $"link:{seed} 主题{b.Theme} 的公共信物 {r.ToNotation()} 不是难到达格");
        }

        // 样本口径（整组种子）：高台与河流两个主题都确有公共信物（上面的 switch 分支不是空跑）。
        if (seed == 20)
        {
            var themes = new HashSet<BoardTheme>();
            for (ulong s = 1; s <= 20; s++)
            {
                MapData m = LinkMapGenerator.Generate(s);
                themes.UnionWith(m.Boards.Where(b => m.RelicCells.Any(kv => b.Contains(kv.Key) && kv.Value.Zone == RelicZone.Contested)).Select(b => b.Theme!.Value));
            }

            Assert.Contains(BoardTheme.Plateau, themes);
            Assert.Contains(BoardTheme.River, themes);
        }
    }

    [Fact]
    public void 公共信物走公共区权重表()
    {
        // tasks 2.4 / design D5：link:1 用对局种子 1 生成信物——公共区档信物格 (Contested, Standard) 复用 relic-generation 公共区的权重与高阶升级，出生区档不变。
        // 权重表的判别：公共区表（v2）流派徽记 216‰、出生区表 360‰；种子 1–1000 的 link:1 公共信物（3 格 × 1000 局）里流派徽记占比落在 18%–25%（≈ 21.6%，±4σ 以上），
        // 走出生区表会是约 36%。高阶升级只对公共区档发生：样本里确有 2 级的公共信物，出生区档恒为 1 级。
        MapData map = LinkMapGenerator.Generate(1);
        Coord[] contested = [.. map.RelicCells.Where(kv => kv.Value.Zone == RelicZone.Contested).Select(kv => kv.Key).Order()];
        Assert.Equal(3, contested.Length);   // 样本口径：link:1 有 3 个公共区档信物格
        RelicGenerationRecord first = RelicGenerator.Generate(map, new GameSeed(1));
        Assert.Equal(map.RelicCells.Count, first.Placements.Length);
        Assert.All(first.Placements.Where(p => contested.Contains(p.Coord)), p => Assert.Equal(new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard), p.Spec));

        int emblems = 0;
        int samples = 0;
        int upgraded = 0;
        for (ulong seed = 1; seed <= 1000; seed++)
        {
            RelicGenerationRecord record = RelicGenerator.Generate(map, new GameSeed(seed));
            foreach (RelicPlacement p in record.Placements)
            {
                if (p.Spec.Zone == RelicZone.Contested)
                {
                    samples++;
                    emblems += p.Content.Type == RelicType.SchoolEmblem ? 1 : 0;
                    upgraded += p.Content.Magnitude == 2 ? 1 : 0;
                }
                else
                {
                    Assert.Equal(1, p.Content.Magnitude);
                }
            }
        }

        Assert.Equal(3000, samples);
        Assert.InRange(emblems * 1000 / samples, 180, 250);
        Assert.True(upgraded > 0, "样本里没有升级的公共信物");
    }

    // ---------- link-map-surfaces：新地表投放（design D1–D3、实施裁决 S1–S4） ----------

    private static readonly Surface[] SpecialSurfaces = [Surface.Desert, Surface.Marsh, Surface.Crag, Surface.Shallows];

    /// <summary>混合主题的尝试次序（S4，测试内独立写一份）。</summary>
    private static readonly Surface[] MixedOrder = [Surface.Shallows, Surface.Crag, Surface.Marsh, Surface.Desert];

    /// <summary>主题配对（规格「初始布局」新地表投放表，测试内独立写一份，不读生成器）。</summary>
    private static Surface[] PairedWith(BoardTheme theme) => theme switch
    {
        BoardTheme.River => [Surface.Shallows],
        BoardTheme.Plateau => [Surface.Crag],
        BoardTheme.Forest => [Surface.Marsh],
        BoardTheme.Palisade => [Surface.Desert],
        _ => SpecialSurfaces,
    };

    /// <summary>主题候选条件（规格表，测试内独立判定；读"不做叠加"的同种子图，只走 Adjacency.Neighbors）。</summary>
    private static bool ThemeFits(MapData plain, Surface surface, Coord c)
    {
        IEnumerable<Coord> around = Adjacency.Neighbors(plain.Width, plain.Height, c);
        bool Fenced(Coord x) => plain.TerrainData.Fences.Any(e => e.A == x || e.B == x);
        return surface switch
        {
            Surface.Shallows => around.Any(n => plain.SurfaceAt(n) == Surface.DeepWater),   // 未架桥深水与桥格同为深水地表
            Surface.Crag => around.Any(n => plain.HeightAt(n) >= 1),
            Surface.Marsh => around.Any(n => plain.SurfaceAt(n) == Surface.Forest),
            Surface.Desert => Fenced(c) || around.Any(Fenced),
            _ => throw new ArgumentOutOfRangeException(nameof(surface)),
        };
    }

    /// <summary>该块的中间带与核心区（测试内独立算式，不读 BoardPlate.InMiddleBand / CoreCells）。</summary>
    private static (Coord[] Middle, HashSet<Coord> Core) Bands(BoardPlate b)
    {
        int cx = b.Origin.X + ((b.Width - 3) / 2);
        int cy = b.Origin.Y + ((b.Height - 3) / 2);
        HashSet<Coord> core = [.. Enumerable.Range(0, 9).Select(k => new Coord(cx + (k % 3), cy + (k / 3)))];
        Coord[] middle = [.. b.Cells().Where(c => c.X > b.Origin.X && c.X < b.Origin.X + b.Width - 1 && c.Y > b.Origin.Y && c.Y < b.Origin.Y + b.Height - 1 && !core.Contains(c))];
        return (middle, core);
    }

    /// <summary>
    /// 基础候选（D2 / S1，测试内独立判定）：中间带、高度 0、草地、非岩石、非信物格、不与核心区任一格几何相邻；
    /// 中间带 ≤ 12 格的小棋盘免除"不与核心区相邻"（实施后裁决 F1）。
    /// </summary>
    private static Coord[] Basis(MapData plain, BoardPlate b)
    {
        (Coord[] middle, HashSet<Coord> core) = Bands(b);
        bool small = middle.Length <= 12;
        return [.. middle.Where(c => plain.IsPlayable(c) && plain.HeightAt(c) == 0 && plain.SurfaceAt(c) == Surface.Grass && !plain.RelicCells.ContainsKey(c)
            && (small || !Adjacency.Neighbors(plain.Width, plain.Height, c).Any(core.Contains))).Order()];
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void 新地表按主题投放(ulong seed)
    {
        // Scenario（link-map-surfaces，实施后裁决 F1 / F2 改写）：河流块只有浅滩、高台块只有岩台、林地块只有沼泽、栅栏阵块只有荒漠，混合块每种至多 1 格；
        // 每块至多 4 格，全部在中间带、高度 0、不是信物格；格数少于目标的块，其候选格已全部用完。
        // "少于目标即候选用完"：实际格数在 [min(2, 基础候选), min(目标上限, 基础候选)]；中间带 ≤ 12 格的小棋盘目标恒为 2（S3），实际格数恰为 min(2, 基础候选)（M-LS5 的守门）。
        // 单一地表的主题候选抽完才退基础候选（S2）；混合主题按 S4 次序逐种放、每种至多 1 格，某种按条件找不到时从基础候选补该种（F2），所以放下的种类恰是次序的前缀。
        // 基础候选与主题条件读"不做叠加"的同种子图，全部用测试内独立算式。
        (MapData map, ImmutableArray<int> counts) = LinkMapGenerator.GenerateWithSurfaceCounts(seed);
        MapData plain = LinkMapGenerator.GenerateWithoutSurfaces(seed);
        Assert.Equal(MapFile.ToJson(LinkMapGenerator.Generate(seed)), MapFile.ToJson(map));   // 统计入口与缓存入口同图
        for (int i = 0; i < map.Boards.Length; i++)
        {
            BoardPlate b = map.Boards[i];
            BoardTheme theme = b.Theme!.Value;
            string at = $"link:{seed} 棋盘 {i}（{theme.DisplayName()} {b.Width}×{b.Height}）";
            Coord[] special = [.. b.Cells().Where(c => SpecialSurfaces.Contains(map.SurfaceAt(c))).Order()];
            Surface[] kinds = [.. special.Select(map.SurfaceAt)];
            Assert.Equal(counts[i], special.Length);   // BoardTerrain 记下的实际格数与图上一致
            Assert.InRange(special.Length, 0, 4);
            Assert.All(kinds, k => Assert.Contains(k, PairedWith(theme)));

            Coord[] basis = Basis(plain, b);
            Assert.All(special, c => Assert.Contains(c, basis));   // 中间带、高度 0、叠加前是草地、非岩石、非信物格、大棋盘不在核心区四邻
            bool small = Bands(b).Middle.Length <= 12;
            int cap = small ? 2 : (theme == BoardTheme.Mixed ? 4 : 3);
            Assert.InRange(special.Length, Math.Min(2, basis.Length), Math.Min(cap, basis.Length));
            if (small)
            {
                Assert.True(Math.Min(2, basis.Length) == special.Length, $"{at}：小棋盘目标 2，基础候选 {basis.Length}，实际 {special.Length}");
            }

            if (theme != BoardTheme.Mixed)
            {
                Surface s = PairedWith(theme)[0];
                Coord[] fits = [.. basis.Where(c => ThemeFits(plain, s, c))];
                Assert.True(Math.Min(special.Length, fits.Length) == special.Count(c => ThemeFits(plain, s, c)), $"{at}：主题候选没抽完就退了基础候选");
            }
            else
            {
                Assert.Equal(MixedOrder.Take(kinds.Length).Order(), kinds.Order());   // 每种至多 1 格，放下的种类是 S4 次序的前缀
                foreach (Coord c in special.Where(c => !ThemeFits(plain, map.SurfaceAt(c), c)))
                {
                    // F2 的补位：只在该种的主题候选此时都已被次序更靠前的种类占了时才发生。
                    Surface k = map.SurfaceAt(c);
                    Assert.All(basis.Where(x => ThemeFits(plain, k, x)), x => Assert.True(
                        special.Contains(x) && Array.IndexOf(MixedOrder, map.SurfaceAt(x)) < Array.IndexOf(MixedOrder, k), $"{at}：{k.DisplayName()}还有主题候选 {x.ToNotation()} 却补了 {c.ToNotation()}"));
                }
            }
        }

        // 算例（tasks 1.1）：link:1 的河流块只有浅滩、林地块只有沼泽，且各有与深水 / 林地相邻的格（样本口径：两块都确有新地表，主题候选分支不是空跑）。
        // terrain-barriers 3.1（地图变了）：link:1 的河流块河道改到第 4 行、湖泊在 D8–F9，大棋盘不贴核心区的主题候选只剩 G9，第二格 G8 按 S2 退到基础候选——
        // 原"每格都与深水相邻"改为"至少一格"，退基础候选的口径由上面的通用断言守住。
        if (seed == 1)
        {
            foreach ((BoardTheme theme, Surface surface) in new[] { (BoardTheme.River, Surface.Shallows), (BoardTheme.Forest, Surface.Marsh) })
            {
                BoardPlate b = map.Boards.Single(p => p.Theme == theme);
                Coord[] cells = [.. b.Cells().Where(c => SpecialSurfaces.Contains(map.SurfaceAt(c)))];
                Assert.NotEmpty(cells);
                Assert.All(cells, c => Assert.Equal(surface, map.SurfaceAt(c)));
                Assert.Contains(cells, c => ThemeFits(plain, surface, c));
            }
        }
    }

    [Fact]
    public void 种子1到20每块新地表格数()
    {
        // design D3 记录项：种子 1–20 每块实际放下的格数（输出到测试日志，抄进任务目录）。样本口径：大多数块 ≥ 2、四种地表都出现过、确有混合块放满 2 格以上。
        // 实施后裁决 F1 / F2 之后只剩候选格真不够的块（6×6 河流的中间带被河道占满等）不足 2 格；"少于目标即候选用完"由「新地表按主题投放」逐块守。
        // 另守 F1 确实生效：小棋盘（中间带 ≤ 12）里确有放到 2 格的块，且确有新地表格贴着核心区（S1 下不可能出现）。
        var histogram = new SortedDictionary<int, int>();
        var kinds = new HashSet<Surface>();
        var low = new List<string>();
        int mixedFull = 0;
        int smallFull = 0;
        int smallBesideCore = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            (MapData map, ImmutableArray<int> counts) = LinkMapGenerator.GenerateWithSurfaceCounts(seed);
            for (int i = 0; i < counts.Length; i++)
            {
                histogram[counts[i]] = histogram.GetValueOrDefault(counts[i]) + 1;
                BoardPlate b = map.Boards[i];
                kinds.UnionWith(b.Cells().Select(map.SurfaceAt).Where(SpecialSurfaces.Contains));
                mixedFull += b.Theme == BoardTheme.Mixed && counts[i] >= 2 ? 1 : 0;
                (Coord[] middle, HashSet<Coord> core) = Bands(b);
                if (middle.Length <= 12)
                {
                    smallFull += counts[i] == 2 ? 1 : 0;
                    smallBesideCore += middle.Count(c => SpecialSurfaces.Contains(map.SurfaceAt(c)) && Adjacency.Neighbors(map.Width, map.Height, c).Any(core.Contains));
                }
                if (counts[i] < 2)
                {
                    low.Add($"link:{seed}#{i} {b.Theme!.Value.DisplayName()}{b.Width}×{b.Height}={counts[i]}");
                }
            }

            _output.WriteLine($"link:{seed} 新地表 {string.Join(" ", counts)}");
        }

        _output.WriteLine($"每块格数分布（100 块）：{string.Join(" ", histogram.Select(kv => $"{kv.Key}格:{kv.Value}块"))}");
        _output.WriteLine($"不足 2 格的块（{low.Count}）：{string.Join("；", low)}");
        Assert.Equal(100, histogram.Values.Sum());
        _output.WriteLine($"小棋盘（中间带 ≤ 12）放满 2 格 {smallFull} 块，贴核心区的新地表格 {smallBesideCore} 格");
        Assert.True(histogram.Where(kv => kv.Key >= 2).Sum(kv => kv.Value) >= 90, "不足 90 块放下 ≥ 2 格");
        Assert.True(smallFull >= 20, $"小棋盘放满 2 格的只有 {smallFull} 块");
        Assert.True(smallBesideCore > 0, "小棋盘没有任何贴核心区的新地表格：F1 未生效");
        Assert.Equal(SpecialSurfaces.Order(), kinds.Order());
        Assert.True(mixedFull >= 5, $"混合块放下 ≥ 2 格的只有 {mixedFull} 块");
        Assert.DoesNotContain(histogram.Keys, k => k > 4);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void 新地表不改其余地形(ulong seed)
    {
        // Scenario（link-map-surfaces E4 / D1 / D5）：link:1–20 与"不做新地表投放"的同种子地图逐格对照——只在新地表格的地表取值上不同；
        // 高度、林地、深水、桥、栅栏、岩石、信物格与布局逐项相同。把新图的地表表换回对照图的地表表后，地图文件逐字节相同。
        MapData map = LinkMapGenerator.Generate(seed);
        MapData plain = LinkMapGenerator.GenerateWithoutSurfaces(seed);
        Assert.Equal((plain.Id, plain.Width, plain.Height, plain.CentralEntrance), (map.Id, map.Width, map.Height, map.CentralEntrance));
        Assert.Equal(plain.Boards, map.Boards);
        Assert.True(plain.Obstacles.SetEquals(map.Obstacles));
        Assert.Equal(plain.RelicCells.OrderBy(kv => kv.Key), map.RelicCells.OrderBy(kv => kv.Key));
        Assert.Equal(plain.BirthZones.Length, map.BirthZones.Length);
        Assert.All(Enumerable.Range(0, map.BirthZones.Length), z => Assert.True(plain.BirthZones[z].SetEquals(map.BirthZones[z])));
        Assert.Equal(plain.TerrainData.Heights.OrderBy(kv => kv.Key), map.TerrainData.Heights.OrderBy(kv => kv.Key));
        Assert.True(plain.TerrainData.Bridges.SetEquals(map.TerrainData.Bridges));
        Assert.True(plain.TerrainData.Fences.SetEquals(map.TerrainData.Fences));

        Coord[] changed = [.. map.AllCoords().Where(c => map.SurfaceAt(c) != plain.SurfaceAt(c))];
        Coord[] special = [.. map.AllCoords().Where(c => SpecialSurfaces.Contains(map.SurfaceAt(c)))];
        Assert.NotEmpty(changed);   // 样本口径：每张图都确有新地表（种子 1–20 实测 5–13 格）
        Assert.Equal(special, changed);
        Assert.All(changed, c => Assert.Equal(Surface.Grass, plain.SurfaceAt(c)));
        Assert.All(changed, c => Assert.Contains(map.Boards, b => Bands(b).Middle.Contains(c)));
        Assert.DoesNotContain(plain.AllCoords(), c => SpecialSurfaces.Contains(plain.SurfaceAt(c)));

        MapData reverted = map with { TerrainData = new TerrainData(map.TerrainData.Heights, plain.TerrainData.Surfaces, map.TerrainData.Bridges, map.TerrainData.Fences) };
        Assert.Equal(MapFile.ToJson(plain), MapFile.ToJson(reverted));
        Assert.NotEqual(MapFile.ToJson(plain), MapFile.ToJson(map));
        Assert.True(MapValidator.Validate(plain).IsValid, MapValidator.Validate(plain).ToString());
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void 河流与高台横穿棋盘(ulong seed)
    {
        // Scenario（terrain-barriers，实施修订 J1–J3）：河道 / 山脊是一整列或一整行、取自中间带、两端到达棋盘外沿、不经过核心区；河道恰有 1 座桥、山脊恰有 1 处坡口；
        // 两侧之间除桥 / 坡口外没有气边。逐块判定用 主题地形Tests 的测试内独立算式（不读生成器），在整张图上（含新地表叠加）复核。
        MapData map = LinkMapGenerator.Generate(seed);
        foreach (BoardPlate b in map.Boards.Where(p => p.Theme is BoardTheme.River or BoardTheme.Plateau))
        {
            (Coord[] line, Coord[] beyond, Coord passage) = 主题地形Tests.Barrier(b, map);
            string at = $"link:{seed} {b.Theme} {b.Origin.ToNotation()}";
            Assert.True(line.Length == b.Width || line.Length == b.Height, at);
            Assert.Equal(2, line.Count(b.OnRim));
            Assert.All(line, c => Assert.False(map.BirthZones.Any(z => z.Contains(c)), $"{at}：{c} 在核心区"));

            // 去掉通道：从核心区沿气边（只在本块内）走不到线上与对侧任何格；留着通道：走得到全部。
            HashSet<Coord> far = [.. line, .. beyond];
            HashSet<Coord> Walk(bool open)
            {
                var reached = new HashSet<Coord>(b.CoreCells());
                var queue = new Queue<Coord>(reached);
                while (queue.Count > 0)
                {
                    foreach (Coord n in Adjacency.LibertyNeighbors(map, queue.Dequeue()).Where(b.Contains))
                    {
                        if ((open || n != passage) && reached.Add(n))
                        {
                            queue.Enqueue(n);
                        }
                    }
                }

                return reached;
            }

            Assert.DoesNotContain(Walk(open: false), far.Contains);
            HashSet<Coord> all = Walk(open: true);
            Assert.All(far.Where(map.IsPlayable), c => Assert.Contains(c, all));
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void 湖泊或岩区不切断棋盘(ulong seed)
    {
        // Scenario（terrain-barriers，裁决 H5 / 实施修订 J4）：中间带多于 12 格的棋盘恰有 1 团 3–5 格的未架桥深水或岩石（≤ 12 格的不放），
        // 棋盘内可落子格仍几何四邻连通（见「棋盘连通且有改造目标」），全图可落子格落在校验器声明的区间内。
        (MapData map, ImmutableArray<LinkMapGenerator.BoardTerrain> boards) = LinkMapGenerator.GenerateWithBoards(seed);
        Assert.Equal(MapFile.ToJson(LinkMapGenerator.Generate(seed)), MapFile.ToJson(map));
        for (int i = 0; i < boards.Length; i++)
        {
            BoardPlate b = map.Boards[i];
            Coord[] cluster = [.. boards[i].Cluster];
            if (Bands(b).Middle.Length <= 12)
            {
                Assert.Empty(cluster);
                continue;
            }

            Assert.InRange(cluster.Length, 3, 5);
            Assert.All(cluster, c => Assert.Contains(c, Bands(b).Middle));
            Assert.True(cluster.All(map.TerrainData.IsUnbridgedDeepWater) || cluster.All(c => map.Obstacles.Contains(c)), $"link:{seed} 棋盘 {i}");
            var reached = new HashSet<Coord> { cluster[0] };
            var queue = new Queue<Coord>(reached);
            while (queue.Count > 0)
            {
                foreach (Coord n in Adjacency.Neighbors(map.Width, map.Height, queue.Dequeue()).Where(cluster.Contains))
                {
                    if (reached.Add(n))
                    {
                        queue.Enqueue(n);
                    }
                }
            }

            Assert.Equal(cluster.Length, reached.Count);
        }

        Assert.True(MapValidator.Validate(map).IsValid);   // 可落子格总数落在声明区间内（PLAYABLE_COUNT_OUT_OF_RANGE 是其中一条）
        if (seed == 20)
        {
            // 样本口径：种子 1–20 里大棋盘确有湖泊与岩区两种、也确有小棋盘不放。
            var kinds = new HashSet<string>(StringComparer.Ordinal);
            for (ulong s = 1; s <= 20; s++)
            {
                (MapData m, ImmutableArray<LinkMapGenerator.BoardTerrain> bs) = LinkMapGenerator.GenerateWithBoards(s);
                foreach (LinkMapGenerator.BoardTerrain t in bs)
                {
                    kinds.Add(t.Cluster.IsEmpty ? "无" : m.TerrainData.IsUnbridgedDeepWater(t.Cluster[0]) ? "湖泊" : "岩区");
                }
            }

            Assert.Equal(["岩区", "无", "湖泊"], kinds.Order(StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// 河流主题的"与核心区隔着深水"（测试内独立判定）：从核心区出发、只走棋盘内不在外圈的非深水格（桥格也当作河道）能到的格记为 S；
    /// 该格不在 S 里、也没有几何邻居在 S 里，即与核心区之间隔着深水（外圈绕行不算）。
    /// </summary>
    private static bool AcrossWater(MapData map, BoardPlate b, Coord r)
    {
        bool Inner(Coord c) => b.Contains(c) && !b.OnRim(c) && map.SurfaceAt(c) != Surface.DeepWater && map.IsPlayable(c);
        var reached = new HashSet<Coord>(b.CoreCells());
        var queue = new Queue<Coord>(reached);
        while (queue.Count > 0)
        {
            foreach (Coord n in Adjacency.Neighbors(map.Width, map.Height, queue.Dequeue()).Where(Inner))
            {
                if (reached.Add(n))
                {
                    queue.Enqueue(n);
                }
            }
        }

        return !reached.Contains(r) && !Adjacency.Neighbors(map.Width, map.Height, r).Any(reached.Contains);
    }
}
