using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.BoardTopology;

/// <summary>
/// ai-turn-speed D2 的守门：<see cref="GameBoard"/> 的棋串 / 气 / 格视图改成数组下标实现、按地图逐格记忆气边与覆盖目标、按突变版本号记忆全盘棋串与活形，
/// <see cref="CoverageMap.Compute"/> 不再逐格建集合——这些都是规则层的<b>内部</b>改写，输出 MUST 与改动前逐项相同。
/// 参考实现取自改动前的代码（HEAD d25110a：HashSet + Queue 的 BFS、逐格 <c>Map</c> 查询、逐格集合的覆盖表），只经 <see cref="Adjacency"/> 与 <see cref="MapData"/> 的查询取邻接与地形，
/// 不经被测的记忆表；随机局面含障碍、深水（含桥）、林地、浅滩、沼泽、岩台、荒漠、高差与栅栏，棋子来自 3 名玩家、类型随机。
/// </summary>
/// <remarks>
/// 变异验证（实跑）：M-S1 <c>GameBoard.CollectGroup</c> 去掉 <c>stones.Sort()</c> → 「随机局面下棋串气与格视图与参考实现逐项相同」红；
/// M-S2 <c>CoverageMap.Compute</c> 把 <c>else if (holder != occupant.Owner)</c> 改成 <c>else</c>（同一玩家重复覆盖也计第二名覆盖者）→ 「随机局面下覆盖表与参考实现逐项相同」红；
/// M-S3 <c>GameBoard.Clear</c> 去掉 <c>_version++</c> → 「每种写入原语之后棋串与活形随之重算」红。
/// </remarks>
public class 盘面派生量优化对照Tests
{
    private const int Seeds = 120;

    [Fact]
    public void 随机局面下棋串气与格视图与参考实现逐项相同()
    {
        int groups = 0;
        int stones = 0;
        for (int seed = 1; seed <= Seeds; seed++)
        {
            GameBoard board = RandomBoard(seed);
            ImmutableArray<Group> all = board.AllGroups();
            Assert.Equal(RefScan(board, static _ => true).Select(Text), all.Select(Text));
            Assert.True(SameArray(all, board.AllGroups()));   // 盘面未变：同一份结果
            foreach (PlayerId player in Players)
            {
                Assert.Equal(RefScan(board, o => o.Owner == player).Select(Text), board.GroupsOf(player).Select(Text));
            }

            foreach (Group group in all)
            {
                groups++;
                stones += group.Size;
                Assert.Equal(RefLiberties(board, group), board.LibertiesOf(group));
                Assert.Equal(RefLiberties(board, group).IsEmpty, board.IsCaptured(group));
            }

            foreach (Coord c in board.AllCoords())
            {
                Assert.Equal(RefCell(board, c), board[c]);
                Assert.Equal(RefGivesLiberty(board, c), board.GivesLiberty(c));
                Assert.Equal(Adjacency.LibertyNeighbors(board.Map, c), board.LibertyNeighbors(c));
                Assert.Equal(Adjacency.CoverageTargets(board.Map, c), board.CoverageTargets(c));
                Group? expected = RefGroupAt(board, c);
                Group? actual = board.GroupAt(c);
                Assert.Equal(expected is null ? null : Text(expected), actual is null ? null : Text(actual));
            }
        }

        // 样本口径下界：随机参数写错时上面的断言会在空盘上恒真
        Assert.True(groups >= 2000, $"只有 {groups} 条棋串");
        Assert.True(stones > groups, $"棋串 {groups} 条、棋子 {stones} 枚——没有多子棋串");
    }

    [Fact]
    public void 随机局面下覆盖表与参考实现逐项相同()
    {
        int contested = 0;
        int repeated = 0;
        for (int seed = 1; seed <= Seeds; seed++)
        {
            GameBoard board = RandomBoard(seed);
            CoverageMap map = CoverageMap.Compute(board);
            (CellCoverage[] coverage, CellOwnership[] ownership, ImmutableArray<CoverageSource>[] sources) = RefCoverage(board);
            foreach (Coord c in board.AllCoords())
            {
                int i = (c.Y * board.Width) + c.X;
                Assert.Equal(coverage[i], map.CoverageOf(c));
                Assert.Equal(ownership[i], map.OwnershipOf(c));
                Assert.Equal(sources[i], map.SourcesOf(c));
                Assert.Equal(coverage[i].SoleCoverer, map.UniqueCoverer(c));
                contested += coverage[i].CovererCount >= 2 ? 1 : 0;
                repeated += coverage[i].CovererCount == 1 && sources[i].Length >= 2 ? 1 : 0;
            }

            foreach (PlayerId player in Players)
            {
                Assert.Equal(
                    board.AllCoords().Where(c => ownership[(c.Y * board.Width) + c.X] is { Kind: OwnershipKind.Exclusive } o && o.Owner == player),
                    map.ExclusiveCellsOf(player));
            }
        }

        Assert.True(contested >= 200, $"争议格只有 {contested} 个");
        Assert.True(repeated >= 200, $"同一玩家多枚棋子覆盖的格只有 {repeated} 个——钉不住\"同一玩家只计一名覆盖者\"");
    }

    [Fact]
    public void 每种写入原语之后棋串与活形随之重算()
    {
        // 四种写入原语各一次：全盘棋串与活形报告都不得再返回写入前的那一份，且内容按新盘面重算。
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("E4", Surface.Forest)]), size: 9);
        board.Place(TestMaps.At("D4"), TestMaps.P0, PieceType.Basic);
        board.Place(TestMaps.At("D5"), TestMaps.P0, PieceType.Basic);
        ImmutableArray<Group> before = board.AllGroups();
        LifeShapeReport lifeBefore = LifeShapeReport.Analyze(board);
        Assert.True(SameArray(before, board.AllGroups()));
        Assert.Same(lifeBefore, LifeShapeReport.Analyze(board));

        // Place：D6 并入同一串
        board.Place(TestMaps.At("D6"), TestMaps.P0, PieceType.Basic);
        Assert.False(SameArray(before, board.AllGroups()));
        Assert.NotSame(lifeBefore, LifeShapeReport.Analyze(board));
        Assert.Equal(["D4", "D5", "D6"], board.AllGroups().Single().Stones.Notations());
        Assert.Equal(3, LifeShapeReport.Analyze(board).Groups.Single().Group.Size);

        // ApplyTerrainEdits：D5–D6 之间立栅，串被切成两条
        ImmutableArray<Group> joined = board.AllGroups();
        LifeShapeReport lifeJoined = LifeShapeReport.Analyze(board);
        board.ApplyTerrainEdits([TerrainEdit.Fence(TestMaps.At("D5"), TestMaps.At("D6"))]);
        Assert.False(SameArray(joined, board.AllGroups()));
        Assert.NotSame(lifeJoined, LifeShapeReport.Analyze(board));
        Assert.Equal(2, board.AllGroups().Length);
        Assert.Equal(2, LifeShapeReport.Analyze(board).Groups.Length);

        // Clear：清掉 D6
        ImmutableArray<Group> split = board.AllGroups();
        LifeShapeReport lifeSplit = LifeShapeReport.Analyze(board);
        board.Clear(TestMaps.At("D6"));
        Assert.False(SameArray(split, board.AllGroups()));
        Assert.NotSame(lifeSplit, LifeShapeReport.Analyze(board));
        Assert.Equal(["D4", "D5"], board.AllGroups().Single().Stones.Notations());

        // RemoveStones：一次移除两枚，盘面清空
        ImmutableArray<Group> pair = board.AllGroups();
        LifeShapeReport lifePair = LifeShapeReport.Analyze(board);
        board.RemoveStones([TestMaps.At("D4"), TestMaps.At("D5")]);
        Assert.False(SameArray(pair, board.AllGroups()));
        Assert.NotSame(lifePair, LifeShapeReport.Analyze(board));
        Assert.Empty(board.AllGroups());
        Assert.Empty(LifeShapeReport.Analyze(board).Groups);
    }

    [Fact]
    public void 副本的记忆与原盘面互不可见()
    {
        GameBoard board = TestMaps.Blank(size: 9);
        board.Place(TestMaps.At("D4"), TestMaps.P0, PieceType.Basic);
        ImmutableArray<Group> original = board.AllGroups();
        LifeShapeReport life = LifeShapeReport.Analyze(board);

        GameBoard clone = board.Clone();
        Assert.Equal(original.Select(Text), clone.AllGroups().Select(Text));
        clone.Place(TestMaps.At("D5"), TestMaps.P0, PieceType.Basic);
        Assert.Equal(["D4", "D5"], clone.AllGroups().Single().Stones.Notations());
        Assert.Equal(2, LifeShapeReport.Analyze(clone).Groups.Single().Group.Size);

        // 原盘面未变：仍是同一份结果、内容不变
        Assert.True(SameArray(original, board.AllGroups()));
        Assert.Same(life, LifeShapeReport.Analyze(board));
        Assert.Equal(["D4"], board.AllGroups().Single().Stones.Notations());

        // 原盘面改造换图：副本共用的旧地图派生表不受影响
        board.ApplyTerrainEdits([TerrainEdit.Fence(TestMaps.At("D4"), TestMaps.At("D5"))]);
        Assert.DoesNotContain(TestMaps.At("D5"), board.LibertyNeighbors(TestMaps.At("D4")));
        Assert.Contains(TestMaps.At("D5"), clone.LibertyNeighbors(TestMaps.At("D4")));
        Assert.Equal(["D4", "D5"], clone.AllGroups().Single().Stones.Notations());
    }

    // ---------- 随机局面 ----------

    private static readonly PlayerId[] Players = [new(0), new(1), new(2)];

    private static readonly PieceType[] Types = Enum.GetValues<PieceType>();

    private static GameBoard RandomBoard(int seed)
    {
        var random = new Random(seed);
        int size = 7 + (seed % 5);
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
                if (roll < 8)
                {
                    obstacles.Add(c);
                    continue;
                }

                if (roll < 16)
                {
                    surfaces[c] = Surface.DeepWater;
                    if (random.Next(2) == 0)
                    {
                        bridges.Add(c);
                    }
                }
                else if (roll < 22)
                {
                    surfaces[c] = Surface.Forest;
                }
                else if (roll < 28)
                {
                    surfaces[c] = Surface.Shallows;
                }
                else if (roll < 32)
                {
                    surfaces[c] = Surface.Marsh;
                }
                else if (roll < 36)
                {
                    surfaces[c] = Surface.Crag;
                }
                else if (roll < 40)
                {
                    surfaces[c] = Surface.Desert;
                }

                if (random.Next(100) < 30)
                {
                    heights[c] = random.Next(TerrainData.MaxHeight + 1);
                }

                if (x > 0 && random.Next(100) < 6)
                {
                    fences.Add(new FenceEdge(new Coord(x - 1, y), c));
                }

                if (y > 0 && random.Next(100) < 6)
                {
                    fences.Add(new FenceEdge(new Coord(x, y - 1), c));
                }
            }
        }

        var terrain = new TerrainData(
            heights.ToImmutableDictionary(), surfaces.ToImmutableDictionary(), [.. bridges], [.. fences]);
        var spec = new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard);
        var map = new MapData
        {
            Id = $"test-random-{seed}",
            Width = size,
            Height = size,
            MaxPlayers = 4,
            Obstacles = [.. obstacles],
            BirthZones = [Zone(0, 0), Zone(size - 2, size - 2)],
            RelicCells = Enumerable.Range(0, 3).Select(_ => new Coord(random.Next(size), random.Next(size))).Distinct().ToImmutableDictionary(c => c, _ => spec),
            ChokePoints = [new Coord(size / 2, size / 2)],
            CentralEntrance = new Coord(size / 2, size / 2),
            TerrainData = terrain,
        };
        GameBoard board = GameBoard.LoadUnvalidated(map);
        foreach (Coord c in board.AllCoords())
        {
            if (board.Map.IsPlayable(c) && random.Next(100) < 45)
            {
                board.Place(c, Players[random.Next(Players.Length)], Types[random.Next(Types.Length)]);
            }
        }

        return board;
    }

    private static ImmutableHashSet<Coord> Zone(int x0, int y0) =>
        [new Coord(x0, y0), new Coord(x0 + 1, y0), new Coord(x0, y0 + 1), new Coord(x0 + 1, y0 + 1)];

    private static string Text(Group group) => group.ToString();

    /// <summary>两个 ImmutableArray 是否共用同一底层数组（记忆命中即同一份）。</summary>
    private static bool SameArray(ImmutableArray<Group> a, ImmutableArray<Group> b) =>
        ReferenceEquals(ImmutableCollectionsMarshal.AsArray(a), ImmutableCollectionsMarshal.AsArray(b));

    // ---------- 参考实现（改动前的代码，只经 Adjacency / MapData 取邻接与地形） ----------

    private static Cell RefCell(GameBoard board, Coord c) =>
        new(c, board.Map.TerrainAt(c), board.Map.BirthZoneOf(c), board.Map.RelicCells.ContainsKey(c), board.OccupantAt(c));

    private static bool RefGivesLiberty(GameBoard board, Coord c) =>
        RefCell(board, c).IsPlayableEmpty && board.Map.SurfaceAt(c) != Surface.Shallows;

    private static Group? RefGroupAt(GameBoard board, Coord c)
    {
        if (board.OccupantAt(c) is not { } occupant)
        {
            return null;
        }

        PlayerId owner = occupant.Owner;
        var visited = new HashSet<Coord> { c };
        var queue = new Queue<Coord>();
        queue.Enqueue(c);
        while (queue.Count > 0)
        {
            Coord current = queue.Dequeue();
            foreach (Coord n in Adjacency.LibertyNeighbors(board.Map, current))
            {
                if (!visited.Contains(n) && board.OccupantAt(n) is { } other && other.Owner == owner)
                {
                    visited.Add(n);
                    queue.Enqueue(n);
                }
            }
        }

        return new Group(owner, visited.Order().ToImmutableArray());
    }

    private static ImmutableArray<Group> RefScan(GameBoard board, Func<Occupant, bool> include)
    {
        var seen = new HashSet<Coord>();
        ImmutableArray<Group>.Builder groups = ImmutableArray.CreateBuilder<Group>();
        foreach (Coord c in board.AllCoords())
        {
            if (seen.Contains(c) || board.OccupantAt(c) is not { } occupant || !include(occupant))
            {
                continue;
            }

            Group group = RefGroupAt(board, c)!;
            seen.UnionWith(group.Stones);
            groups.Add(group);
        }

        return groups.ToImmutable();
    }

    private static ImmutableArray<Coord> RefLiberties(GameBoard board, Group group)
    {
        var liberties = new HashSet<Coord>();
        foreach (Coord stone in group.Stones)
        {
            foreach (Coord n in Adjacency.LibertyNeighbors(board.Map, stone))
            {
                if (RefGivesLiberty(board, n))
                {
                    liberties.Add(n);
                }
            }
        }

        return liberties.Order().ToImmutableArray();
    }

    private static (CellCoverage[] Coverage, CellOwnership[] Ownership, ImmutableArray<CoverageSource>[] Sources) RefCoverage(GameBoard board)
    {
        int width = board.Width;
        int height = board.Height;
        var coverers = new HashSet<PlayerId>?[width * height];
        var sources = new ImmutableArray<CoverageSource>.Builder?[width * height];
        foreach (Coord c in board.AllCoords())
        {
            if (board.OccupantAt(c) is not { } occupant)
            {
                continue;
            }

            foreach (Coord target in Adjacency.CoverageTargets(board.Map, c))
            {
                int index = (target.Y * width) + target.X;
                (coverers[index] ??= []).Add(occupant.Owner);
                (sources[index] ??= ImmutableArray.CreateBuilder<CoverageSource>()).Add(new CoverageSource(c, Adjacency.AreAdjacent(c, target)));
            }
        }

        var coverage = new CellCoverage[width * height];
        var ownership = new CellOwnership[width * height];
        foreach (Coord c in board.AllCoords())
        {
            int index = (c.Y * width) + c.X;
            HashSet<PlayerId>? set = coverers[index];
            CellCoverage cell = set is null ? CellCoverage.None : new CellCoverage(set.Count, set.Count == 1 ? set.Single() : null);
            coverage[index] = cell;
            Cell view = RefCell(board, c);
            ownership[index] = view.Terrain == Terrain.Obstacle
                ? new CellOwnership(OwnershipKind.Obstacle, null)
                : view.Occupant is { } occupant
                    ? new CellOwnership(OwnershipKind.Occupied, occupant.Owner)
                    : cell.CovererCount switch
                    {
                        0 => new CellOwnership(OwnershipKind.Neutral, null),
                        1 => new CellOwnership(OwnershipKind.Exclusive, cell.SoleCoverer),
                        _ => new CellOwnership(OwnershipKind.Contested, null),
                    };
        }

        return (coverage, ownership, [.. sources.Select(b => b is null ? ImmutableArray<CoverageSource>.Empty : b.ToImmutable())]);
    }
}
