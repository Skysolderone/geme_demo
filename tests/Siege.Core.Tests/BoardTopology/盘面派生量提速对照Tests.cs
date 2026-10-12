using System.Collections.Immutable;
using System.Text;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.BoardTopology;

/// <summary>
/// board-link-mode 段 C2（裁决 C18 / P2：只做决策不变的提速）的守门：新增的记忆与派生都 MUST 与"每次从零重算"逐项相同，且盘面一变即失效。
/// <list type="bullet">
/// <item>改造换图时地图派生表由旧表派生（<c>GameBoard.MapTables.Derive</c>：抄旧表、只作废改造目标周围 2 格）——与在新地图上从零建表逐格比对：随机地形小图上穷举每一个合法改造目标并逐格全比；
/// 链接图（首轮两次链接 + 决赛链接）、v5、board:1 上搭桥与烧林全取、立栅按步长抽样，比改造目标周围 6 格并定期全图比。</item>
/// <item><see cref="GameBoard.Serialize"/> 按突变版本号记忆，且改为一次成串——每种写入原语之后重算、与改动前的逐格 <c>Append</c> 写法逐字符相同；同形比对键同理。</item>
/// <item>空集合的 <see cref="GameBoard.RemoveStones"/> 什么都没写，不令记忆失效。</item>
/// <item>活形缓存指纹（<see cref="BatchEvaluator.Fingerprint"/>）改为一次成串——与改动前的逐格 <c>Append</c> 写法逐字符相同，且仍只看占用者 + 改造段、不看棋子类型。</item>
/// </list>
/// 参考实现只经 <see cref="Adjacency"/> / <see cref="MapData"/> 的查询（从零建表即 <see cref="GameBoard.LoadUnvalidated"/> 在新地图上新建的盘面）。
/// </summary>
/// <remarks>
/// 变异验证（实跑，记录见 <c>.trellis/tasks/10-10-board-link-mode-c2/implement.md</c>）：
/// MT-1 <c>MapTables.EditReach</c> 2 → 1 → 「改造派生表与从零建表逐项相同」红；
/// MT-2 <c>Derive</c> 对栅栏不作废两端 → 同上红；
/// MT-3 <c>Serialize</c> 的记忆不看版本号 → 「序列化按版本号记忆且与逐格写法相同」红；
/// MT-4 <c>RemoveStones</c> 对空集合仍推进版本号 → 「空集合移除不令记忆失效」红；
/// MT-10（check 阶段补）<c>BatchEvaluator.Fingerprint</c> 丢掉改造段 → 「活形缓存指纹与逐格写法相同」红 1 / 4。
/// check 阶段另抽 MT-1（红 1 / 4）与 MT-9（<c>LifeShapeReport.IndexChecked</c> 返回格下标 → <c>空区与封闭眼空间Tests.棋盘边缘与障碍算墙</c> 红 1 / 41）复现，记录见
/// <c>.trellis/tasks/10-10-board-link-mode-c2/check.md</c>。
/// </remarks>
public class 盘面派生量提速对照Tests
{
    [Fact]
    public void 改造派生表与从零建表逐项相同()
    {
        var counts = new Dictionary<TerrainEditKind, int>();
        int edits = 0;
        int farCoverageChanges = 0;
        int nearLibertyChanges = 0;
        foreach (MapData map in Maps())
        {
            // 旧表两种状态：逐格全部算过（对局中的常态），与一格都没算过（派生只能抄到"未算"）。
            GameBoard warm = GameBoard.LoadUnvalidated(map);
            Touch(warm);
            GameBoard cold = GameBoard.LoadUnvalidated(map);

            // 合法改造目标按坐标序去重穷举；大图（> 400 格）的立栅目标数以千计（每条可落子边一个），按出现次序每 FenceStride 个取一个，搭桥与烧林全取。
            bool small = map.Width * map.Height <= 400;
            var seen = new HashSet<TerrainEdit>();
            var all = new List<TerrainEdit>();
            int fences = 0;
            foreach (Coord c in map.AllCoords())
            {
                foreach (TerrainEdit edit in TerrainEditRules.LegalTargets(map, c, workshop: true))
                {
                    if (seen.Add(edit) && (small || edit.Kind != TerrainEditKind.Fence || fences++ % FenceStride == 0))
                    {
                        all.Add(edit);
                    }
                }
            }

            // 小图逐格全比；大图（链接图 73×73 等）每个改造比改造目标周围 Window 格内的全部格 + 全图可落子清单，每 25 个改造再全图比一次
            // （作废范围之外的格是从旧表原样抄来的，"远处不受改造影响"在小图上已逐格全比验证）。
            for (int k = 0; k < all.Count; k++)
            {
                TerrainEdit edit = all[k];
                edits++;
                counts[edit.Kind] = counts.GetValueOrDefault(edit.Kind) + 1;
                bool full = small || k % 25 == 0;
                GameBoard derived = warm.Clone();
                derived.ApplyTerrainEdits([edit]);
                AssertSameAsFresh(derived, $"{map.Id} {edit}", full);
                (int far, int near) = ChangesAtDistance(warm, derived);
                farCoverageChanges += far;
                nearLibertyChanges += near;

                if (small || k % 5 == 0)
                {
                    GameBoard fromCold = cold.Clone();
                    fromCold.ApplyTerrainEdits([edit]);
                    AssertSameAsFresh(fromCold, $"{map.Id} {edit}（旧表未算）", full);
                }
            }

            // 一批多个改造同时生效、以及连续两次改造（派生表再派生）。
            for (int k = 0; k + 1 < all.Count; k += small ? 3 : 29)
            {
                TerrainEdit a = all[k];
                TerrainEdit b = all[k + 1];
                if (!Compatible(map, a, b))
                {
                    continue;
                }

                GameBoard together = warm.Clone();
                together.ApplyTerrainEdits([a, b]);
                AssertSameAsFresh(together, $"{map.Id} {a}+{b}", small);

                GameBoard chained = warm.Clone();
                chained.ApplyTerrainEdits([a]);
                Touch(chained);
                chained.ApplyTerrainEdits([b]);
                AssertSameAsFresh(chained, $"{map.Id} {a}→{b}", small);
            }
        }

        // 样本口径下界：三类改造都穷举到；且确有"距离 2 处覆盖目标变化""距离 1 处气边变化"的样本——作废半径写小即红（MT-1）。
        Assert.True(edits >= 1000, $"只穷举了 {edits} 个改造");
        Assert.True(counts.GetValueOrDefault(TerrainEditKind.Bridge) >= 20, $"搭桥只有 {counts.GetValueOrDefault(TerrainEditKind.Bridge)} 个");
        Assert.True(counts.GetValueOrDefault(TerrainEditKind.Fence) >= 100, $"立栅只有 {counts.GetValueOrDefault(TerrainEditKind.Fence)} 个");
        Assert.True(counts.GetValueOrDefault(TerrainEditKind.Burn) >= 20, $"烧林只有 {counts.GetValueOrDefault(TerrainEditKind.Burn)} 个");
        Assert.True(farCoverageChanges >= 10, $"距离 2 处覆盖目标变化的样本只有 {farCoverageChanges} 个");
        Assert.True(nearLibertyChanges >= 10, $"距离 1 处气边变化的样本只有 {nearLibertyChanges} 个");
    }

    [Fact]
    public void 序列化按版本号记忆且与逐格写法相同()
    {
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("E4", Surface.Forest), ("F4", Surface.DeepWater)]), size: 9);
        string empty = board.Serialize();
        Assert.Same(empty, board.Serialize());   // 盘面未变：同一份
        Assert.Equal(RefSerialize(board), empty);

        // 每种写入原语之后：不再返回写入前那一份，内容与从零序列化（副本的记忆从零起）及逐格写法相同。
        void Check(string before, string what)
        {
            string now = board.Serialize();
            Assert.False(ReferenceEquals(before, now), $"{what} 之后仍返回写入前那一份");
            Assert.False(before == now, $"{what} 之后序列化内容未变");
            Assert.True(RefSerialize(board) == now, $"{what} 之后与逐格写法不同");
            Assert.True(board.Clone().Serialize() == now, $"{what} 之后与副本的从零序列化不同");
            Assert.Same(now, board.Serialize());
            Assert.True(RefSuperkoKey(now) == GameBoard.SuperkoKey(now), $"{what} 之后同形比对键与参考实现不同");
        }

        board.Place(TestMaps.At("D4"), TestMaps.P0, PieceType.Basic);
        Check(empty, "Place");
        string placed = board.Serialize();
        board.Place(TestMaps.At("D5"), new PlayerId(15), PieceType.Boundary);
        Check(placed, "Place 15 号玩家");
        string twice = board.Serialize();
        board.ApplyTerrainEdits([TerrainEdit.Burn(TestMaps.At("E4")), TerrainEdit.Bridge(TestMaps.At("F4"))]);
        Check(twice, "ApplyTerrainEdits");
        string edited = board.Serialize();
        board.ApplyTerrainEdits([TerrainEdit.Fence(new FenceEdge(TestMaps.At("D4"), TestMaps.At("D5")))]);
        Check(edited, "ApplyTerrainEdits 栅栏");
        string fenced = board.Serialize();
        board.Clear(TestMaps.At("D5"));
        Check(fenced, "Clear");
        string cleared = board.Serialize();
        board.RemoveStones([TestMaps.At("D4")]);
        Check(cleared, "RemoveStones");

        // 副本的记忆与原盘面互不可见
        GameBoard clone = board.Clone();
        string original = board.Serialize();
        clone.Place(TestMaps.At("A1"), TestMaps.P1, PieceType.Line);
        Assert.Same(original, board.Serialize());
        Assert.Equal(RefSerialize(clone), clone.Serialize());

        // 随机盘面（含 0–15 号玩家、十种类型、改造段）与逐格写法逐字符相同
        var random = new Random(7);
        PieceType[] types = Enum.GetValues<PieceType>();
        for (int seed = 0; seed < 40; seed++)
        {
            GameBoard sample = TestMaps.Blank(TestMaps.Terrain(surfaces: [("C3", Surface.Forest), ("G7", Surface.DeepWater)]), size: 7 + (seed % 6));
            foreach (Coord c in sample.AllCoords())
            {
                if (sample.Map.IsPlayable(c) && random.Next(100) < 40)
                {
                    sample.Place(c, new PlayerId(random.Next(16)), types[random.Next(types.Length)]);
                }
            }

            if (seed % 2 == 0)
            {
                sample.ApplyTerrainEdits([TerrainEdit.Burn(TestMaps.At("C3"))]);
            }

            string text = sample.Serialize();
            Assert.Equal(RefSerialize(sample), text);
            Assert.Equal(RefSuperkoKey(text), GameBoard.SuperkoKey(text));
        }
    }

    [Fact]
    public void 空集合移除不令记忆失效()
    {
        GameBoard board = TestMaps.Blank(size: 9);
        board.Place(TestMaps.At("D4"), TestMaps.P0, PieceType.Basic);
        ImmutableArray<Group> groups = board.AllGroups();
        LifeShapeReport life = LifeShapeReport.Analyze(board);
        string serialized = board.Serialize();

        board.RemoveStones([]);
        Assert.True(groups == board.AllGroups());   // 同一底层数组
        Assert.Same(life, LifeShapeReport.Analyze(board));
        Assert.Same(serialized, board.Serialize());

        // 非空即作废（与既有的写入原语口径相同）
        board.RemoveStones([TestMaps.At("D4")]);
        Assert.Empty(board.AllGroups());
        Assert.NotSame(life, LifeShapeReport.Analyze(board));
        Assert.NotEqual(serialized, board.Serialize());
    }

    [Fact]
    public void 活形缓存指纹与逐格写法相同()
    {
        // 随机盘面（含 0–15 号玩家、十种类型、烧林 / 搭桥改造段）与改动前的逐格 Append 写法逐字符相同。
        var random = new Random(11);
        PieceType[] types = Enum.GetValues<PieceType>();
        for (int seed = 0; seed < 40; seed++)
        {
            GameBoard sample = TestMaps.Blank(TestMaps.Terrain(surfaces: [("C3", Surface.Forest), ("G7", Surface.DeepWater)]), size: 7 + (seed % 6));
            foreach (Coord c in sample.AllCoords())
            {
                if (sample.Map.IsPlayable(c) && random.Next(100) < 40)
                {
                    sample.Place(c, new PlayerId(random.Next(16)), types[random.Next(types.Length)]);
                }
            }

            switch (seed % 4)
            {
                case 0:
                    sample.ApplyTerrainEdits([TerrainEdit.Burn(TestMaps.At("C3"))]);
                    break;
                case 1:
                    sample.ApplyTerrainEdits([TerrainEdit.Bridge(TestMaps.At("G7")), TerrainEdit.Burn(TestMaps.At("C3"))]);
                    break;
                default:
                    break;
            }

            Assert.True(RefFingerprint(sample) == BatchEvaluator.Fingerprint(sample), $"样本 {seed}：指纹与逐格写法不同");
        }

        // 语义不变：只看占用者与改造段——同一格换棋子类型指纹相同；换占用者、加子、加改造都不同。
        GameBoard a = TestMaps.Blank(TestMaps.Terrain(surfaces: [("E4", Surface.Forest)]), size: 9);
        GameBoard b = a.Clone();
        a.Place(TestMaps.At("D4"), TestMaps.P0, PieceType.Basic);
        b.Place(TestMaps.At("D4"), TestMaps.P0, PieceType.Fortress);
        Assert.Equal(BatchEvaluator.Fingerprint(a), BatchEvaluator.Fingerprint(b));

        GameBoard owner = a.Clone();
        owner.Clear(TestMaps.At("D4"));
        owner.Place(TestMaps.At("D4"), TestMaps.P1, PieceType.Basic);
        Assert.NotEqual(BatchEvaluator.Fingerprint(a), BatchEvaluator.Fingerprint(owner));

        GameBoard edited = a.Clone();
        edited.ApplyTerrainEdits([TerrainEdit.Burn(TestMaps.At("E4"))]);
        Assert.NotEqual(BatchEvaluator.Fingerprint(a), BatchEvaluator.Fingerprint(edited));
        Assert.Equal(RefFingerprint(edited), BatchEvaluator.Fingerprint(edited));
    }

    // ---------- 样本地图 ----------

    private static IEnumerable<MapData> Maps()
    {
        // link:1 首轮两次东贴后再接决赛北贴：拼接后的连体棋盘（段 F）。
        MapData link = LinkMapGenerator.Generate(1);
        link = LinkMapGenerator.Link(link, 0, 1, LinkSide.East).Map;
        link = LinkMapGenerator.Link(link, 2, 3, LinkSide.East).Map;
        yield return LinkMapGenerator.Link(link, 0, 2, LinkSide.North).Map;
        yield return MapCatalog.Legacy.Resolve("siege-4p-base-v5");
        yield return MapCatalog.Legacy.Resolve("board:1");
        for (int seed = 1; seed <= 12; seed++)
        {
            yield return RandomMap(seed);
        }
    }

    /// <summary>随机地形小图：障碍、深水（部分架桥）、林地、浅滩、沼泽、岩台、荒漠、高差与栅栏都有，供三类改造与隔水 / 岩台远格覆盖充分出现。</summary>
    private static MapData RandomMap(int seed)
    {
        var random = new Random(seed * 7919);
        int size = 9 + (seed % 4);
        var obstacles = new List<Coord>();
        var heights = new Dictionary<Coord, int>();
        var surfaces = new Dictionary<Coord, Surface>();
        var bridges = new List<Coord>();
        var fences = new HashSet<FenceEdge>();
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                var c = new Coord(x, y);
                int roll = random.Next(100);
                if (roll < 6)
                {
                    obstacles.Add(c);
                    continue;
                }

                if (roll < 22)
                {
                    surfaces[c] = Surface.DeepWater;
                    if (random.Next(4) == 0)
                    {
                        bridges.Add(c);
                    }
                }
                else if (roll < 34)
                {
                    surfaces[c] = Surface.Forest;
                }
                else if (roll < 40)
                {
                    surfaces[c] = Surface.Shallows;
                }
                else if (roll < 44)
                {
                    surfaces[c] = Surface.Marsh;
                }
                else if (roll < 52)
                {
                    surfaces[c] = Surface.Crag;
                }
                else if (roll < 56)
                {
                    surfaces[c] = Surface.Desert;
                }

                if (random.Next(100) < 30)
                {
                    heights[c] = random.Next(TerrainData.MaxHeight + 1);
                }

                if (x > 0 && random.Next(100) < 5)
                {
                    fences.Add(new FenceEdge(new Coord(x - 1, y), c));
                }

                if (y > 0 && random.Next(100) < 5)
                {
                    fences.Add(new FenceEdge(new Coord(x, y - 1), c));
                }
            }
        }

        var spec = new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard);
        return new MapData
        {
            Id = $"test-c2-random-{seed}",
            Width = size,
            Height = size,
            MaxPlayers = 4,
            Obstacles = [.. obstacles],
            BirthZones = [[new Coord(0, 0), new Coord(1, 0)], [new Coord(size - 1, size - 1)]],
            RelicCells = new[] { new Coord(size / 2, size / 2), new Coord(1, size - 2) }.ToImmutableDictionary(c => c, _ => spec),
            ChokePoints = [new Coord(size / 2, size / 2)],
            CentralEntrance = new Coord(size / 2, size / 2),
            TerrainData = new TerrainData(heights.ToImmutableDictionary(), surfaces.ToImmutableDictionary(), [.. bridges], [.. fences]),
        };
    }

    /// <summary>两个改造能否同批生效（目标不同、各自对原图合法即可；TerrainWriter 对整批原子校验，不合法的组合直接跳过）。</summary>
    private static bool Compatible(MapData map, TerrainEdit a, TerrainEdit b)
    {
        if (a == b)
        {
            return false;
        }

        try
        {
            _ = TerrainWriter.ApplyAll(map, [a, b]);
            return true;
        }
        catch (SiegeRuleException)
        {
            return false;
        }
    }

    // ---------- 比对 ----------

    /// <summary>逐格把全部派生项读一遍（令旧表"全部算过"）。</summary>
    private static void Touch(GameBoard board)
    {
        foreach (Coord c in board.AllCoords())
        {
            _ = board[c];
            _ = board.LibertyNeighbors(c);
            _ = board.CoverageTargets(c);
        }

        _ = board.PlayableIndices;
    }

    /// <summary>派生表的每一项与在同一张新地图上从零建表（新建盘面）逐格相同；同时与唯一实现（Adjacency / MapData）直接算的结果相同。</summary>
    private static void AssertSameAsFresh(GameBoard derived, string label, bool full)
    {
        GameBoard fresh = GameBoard.LoadUnvalidated(derived.Map);
        foreach (Coord c in full ? derived.AllCoords() : Around(derived, Window))
        {
            Assert.True(fresh[c] == derived[c], $"{label}：{c} 格视图不同");
            Assert.True(fresh.GivesLiberty(c) == derived.GivesLiberty(c), $"{label}：{c} 可作气不同");
            Assert.True(fresh.LibertyNeighbors(c).SequenceEqual(derived.LibertyNeighbors(c)), $"{label}：{c} 气边邻居不同");
            Assert.True(fresh.CoverageTargets(c).SequenceEqual(derived.CoverageTargets(c)), $"{label}：{c} 覆盖目标不同");
        }

        Assert.True(fresh.PlayableIndices.SequenceEqual(derived.PlayableIndices), $"{label}：可落子格清单不同");
        Assert.True(fresh.PlayableOrdinals.SequenceEqual(derived.PlayableOrdinals), $"{label}：可落子序号不同");
    }

    /// <summary>大图立栅目标的抽样步长（见「改造派生表与从零建表逐项相同」）。</summary>
    private const int FenceStride = 20;

    /// <summary>局部比对的窗口：改造目标周围切比雪夫距离不超过此值（大于作废半径 2，跨过作废边界两侧各数格）。</summary>
    private const int Window = 6;

    /// <summary>全部改造目标格（栅栏取两端）周围 <paramref name="radius"/> 格内的格，坐标序。</summary>
    private static IEnumerable<Coord> Around(GameBoard board, int radius)
    {
        var cells = new SortedSet<Coord>();
        foreach (TerrainEdit edit in board.TerrainEdits)
        {
            foreach (Coord e in edit.Kind == TerrainEditKind.Fence ? new[] { edit.Edge.A, edit.Edge.B } : [edit.Cell])
            {
                for (int y = Math.Max(0, e.Y - radius); y <= Math.Min(board.Height - 1, e.Y + radius); y++)
                {
                    for (int x = Math.Max(0, e.X - radius); x <= Math.Min(board.Width - 1, e.X + radius); x++)
                    {
                        cells.Add(new Coord(x, y));
                    }
                }
            }
        }

        return cells;
    }

    /// <summary>改造前后：恰在切比雪夫距离 2 处覆盖目标变化的格数，与恰在距离 1 处气边邻居变化的格数（证明作废半径不能再小）。</summary>
    private static (int Far, int Near) ChangesAtDistance(GameBoard before, GameBoard after)
    {
        ImmutableArray<TerrainEdit> edits = after.TerrainEdits;
        var centers = edits.SelectMany(e => e.Kind == TerrainEditKind.Fence ? new[] { e.Edge.A, e.Edge.B } : [e.Cell]).ToArray();
        int far = 0;
        int near = 0;
        var around = new HashSet<Coord>();
        foreach (Coord e in centers)
        {
            for (int y = Math.Max(0, e.Y - 2); y <= Math.Min(after.Height - 1, e.Y + 2); y++)
            {
                for (int x = Math.Max(0, e.X - 2); x <= Math.Min(after.Width - 1, e.X + 2); x++)
                {
                    around.Add(new Coord(x, y));
                }
            }
        }

        foreach (Coord c in around)
        {
            int d = centers.Min(e => Math.Max(Math.Abs(e.X - c.X), Math.Abs(e.Y - c.Y)));
            if (d == 2 && !Adjacency.CoverageTargets(before.Map, c).SequenceEqual(Adjacency.CoverageTargets(after.Map, c)))
            {
                far++;
            }

            if (d == 1 && !Adjacency.LibertyNeighbors(before.Map, c).SequenceEqual(Adjacency.LibertyNeighbors(after.Map, c)))
            {
                near++;
            }
        }

        return (far, near);
    }

    // ---------- 参考实现（段 C2 改动前的逐格写法） ----------

    private static string RefSerialize(GameBoard board)
    {
        var sb = new StringBuilder();
        for (int y = 0; y < board.Height; y++)
        {
            if (y > 0)
            {
                sb.Append('/');
            }

            for (int x = 0; x < board.Width; x++)
            {
                Occupant? occupant = board.OccupantAt(new Coord(x, y));
                if (occupant is null)
                {
                    sb.Append("--");
                }
                else
                {
                    sb.Append(occupant.Value.Owner.Value.ToString("X1"));
                    sb.Append(RefTypeCode(occupant.Value.Type));
                }
            }
        }

        ImmutableArray<TerrainEdit> edits = board.TerrainEdits;
        if (!edits.IsEmpty)
        {
            sb.Append('|');
            sb.Append(string.Join(",", edits.Select(e => e.ToString()).Order(StringComparer.Ordinal)));
        }

        return sb.ToString();
    }

    private static char RefTypeCode(PieceType type) => type switch
    {
        PieceType.Basic => 'B',
        PieceType.Fortress => 'F',
        PieceType.Line => 'L',
        PieceType.Multiplier => 'M',
        PieceType.Synergy => 'S',
        PieceType.Artisan => 'A',
        PieceType.Bannerman => 'N',
        PieceType.Chain => 'C',
        PieceType.Sentry => 'T',
        PieceType.Boundary => 'K',
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private static string RefFingerprint(GameBoard board)
    {
        var sb = new StringBuilder();
        foreach (Coord c in board.AllCoords())
        {
            sb.Append(board.OccupantAt(c) is { } occupant ? (char)('A' + occupant.Owner.Value) : '.');
        }

        ImmutableArray<TerrainEdit> edits = board.TerrainEdits;
        if (!edits.IsEmpty)
        {
            sb.Append('|').AppendJoin(',', edits.Select(e => e.ToString()).Order(StringComparer.Ordinal));
        }

        return sb.ToString();
    }

    private static string RefSuperkoKey(string serialized)
    {
        int bar = serialized.IndexOf('|', StringComparison.Ordinal);
        string grid = bar >= 0 ? serialized[..bar] : serialized;
        var sb = new StringBuilder();
        int column = 0;
        foreach (char ch in grid)
        {
            if (ch == '/')
            {
                column = 0;
                sb.Append(ch);
                continue;
            }

            if (column % 2 == 0)
            {
                sb.Append(ch);
            }

            column++;
        }

        if (bar >= 0)
        {
            sb.Append(serialized, bar, serialized.Length - bar);
        }

        return sb.ToString();
    }
}
