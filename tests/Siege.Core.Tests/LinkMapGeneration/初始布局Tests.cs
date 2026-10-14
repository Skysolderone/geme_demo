using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Relics;

namespace Siege.Core.Tests.LinkMapGeneration;

/// <summary>
/// 规格：openspec/changes/link-map-terrain/specs/link-map-generation —— Requirement: 初始布局（link-map-terrain 第 2 组：边长 6–8、主题、外圈 / 核心区 / 中间带）。
/// </summary>
/// <remarks>
/// 变异验证（段 F 实跑，只跑本类）：M-F2 <c>Layout</c> 的横向间距 <c>Gap</c> 改为 2 → 「出生棋盘互不相邻」全红；M-F3 <c>Assemble</c> 的东侧留白改为 1 → 「画布按布局裁出」全红。
/// link-map-terrain 第 2 组的变异记录见任务汇报（主题排列、核心区偏移、出生区信物相邻、公共信物档位）。
/// </remarks>
public class 初始布局Tests
{
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
    public void 出生棋盘互不相邻(ulong seed)
    {
        // Scenario：任意两块出生棋盘的外接矩形之间至少隔 3 格，沿几何邻接不连通（5 个战场）。
        MapData map = LinkMapGenerator.Generate(seed);
        for (int i = 0; i < map.Boards.Length; i++)
        {
            for (int j = i + 1; j < map.Boards.Length; j++)
            {
                Assert.True(LinkMapFixtures.Gap(map.Boards[i], map.Boards[j]) >= LinkMapGenerator.Gap, $"{seed}: 棋盘 {i} 与 {j} 只隔 {LinkMapFixtures.Gap(map.Boards[i], map.Boards[j])}");
            }
        }

        Assert.Equal(5, Battlefields.Of(map).Count);
        Assert.Equal(3, LinkMapGenerator.Gap);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void 画布按布局裁出(ulong seed)
    {
        // 裁决 J4 / J7：5 块棋盘底边对齐在第 2 行、自第 2 列起一字排开、相邻两块恰隔 3 格；画布四周各留 2 格裁出一次（之后原点不再变）。
        MapData map = LinkMapGenerator.Generate(seed);
        BoardPlate[] byX = [.. map.Boards.OrderBy(b => b.Origin.X)];
        Assert.Equal(LinkMapGenerator.Margin, byX[0].Origin.X);
        Assert.All(byX, b => Assert.Equal(LinkMapGenerator.Margin, b.Origin.Y));
        for (int k = 1; k < byX.Length; k++)
        {
            Assert.Equal(byX[k - 1].Origin.X + byX[k - 1].Width + LinkMapGenerator.Gap, byX[k].Origin.X);
        }

        Assert.Equal(byX[^1].Origin.X + byX[^1].Width + LinkMapGenerator.Margin, map.Width);
        Assert.Equal(map.Boards.Max(b => b.Origin.Y + b.Height) + LinkMapGenerator.Margin, map.Height);
        Assert.InRange(map.Width, 46, 56);   // 5×6 + 4×3 + 4 … 5×8 + 4×3 + 4
        Assert.InRange(map.Height, 10, 12);

        // 次序随种子变化（样本口径：20 个种子里不止一种排列）。
        Assert.True(Seeds().Select(s => string.Join(",", LinkMapGenerator.Generate((ulong)s[0]).Boards.OrderBy(b => b.Origin.X).Select(b => Array.IndexOf([.. LinkMapGenerator.Generate((ulong)s[0]).Boards], b)))).Distinct().Count() > 1);
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

            foreach (Coord c in b.Cells().Where(c => c.X == b.Origin.X || c.X == b.Origin.X + b.Width - 1 || c.Y == b.Origin.Y || c.Y == b.Origin.Y + b.Height - 1))
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
        // Scenario：每块出生棋盘内可落子格沿几何四邻接连成一片，林地格与未架桥深水格合计不少于 2 个，没有荒漠、沼泽、岩台与浅滩。
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
            Assert.DoesNotContain(b.Cells(), c => map.SurfaceAt(c) is Surface.Desert or Surface.Marsh or Surface.Crag or Surface.Shallows);
            // 岩石只在中间带、每块至多 2 格；高差只有 0 / 1 / 2（TerrainData 构造期保证上限）。
            Coord[] rocks = [.. b.Cells().Where(c => !map.IsPlayable(c) && map.SurfaceAt(c) != Surface.DeepWater)];
            Assert.InRange(rocks.Length, 0, 2);
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
