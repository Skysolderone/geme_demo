using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Core.Preview;
using Siege.Presentation.Visibility;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.VisualStyleBaseline;

/// <summary>
/// 规格：visual-style-baseline —— Requirement: 棋盘台面的可视表现（"哪些格属于哪块棋盘 MUST 取自视图模型中的棋盘清单"；"场景格 SHALL 只作装饰"）。
/// link-map-terrain 6.1：链接档棋盘的中间带可以有岩石（D4，每块 0–2 格）。棋盘内的岩石是本战场的一格地形，按棋盘格画（地砖 + 岩石部件），
/// 场景格（棋盘之外）仍按场景装饰画。"这一格是棋盘内岩石还是场景装饰"由视图模型 <see cref="DefaultBoardView.ObstacleArtAt"/> 按棋盘清单给出，Godot 不自己推断。
/// </summary>
/// <remarks>
/// 变异验证（link-map-terrain 6.1 实跑）：见各测试方法的注释。
/// terrain-bonus 段 B 阶段 2 实跑（过滤 VisualStyleBaseline，63 条）：MB-1 ObstacleArtAt 退回"棋盘清单外接矩形内"口径 → 「岩石地表取岩石造型」红 1；
/// MLT-G8 ObstacleArtAt 恒 Scenery（运行时恒假条件）→ 同条红 1；MLT-G2 BoardView 不经 ObstacleArtAt、自己判 cell.Surface → 「Godot障碍造型只经视图模型取用」红 1。
/// </remarks>
public class 棋盘台面的可视表现Tests
{
    /// <summary>扫描区间：地图种子 1–N（写死，不挑单个种子）。</summary>
    private const int SeedSpan = 20;

    [Fact]
    public void 岩石地表取岩石造型()
    {
        // 每张图让 P0 锁定 1 号棋盘、构筑期按座位过滤后的默认棋盘（open-map：锁定后 5 块都可见）。
        // terrain-bonus 段 B（裁决 1 / 4）：岩石是可落子地表——视图里的岩石地表格（平台上与低地都算）→ BoardRock；其余格，包括全部障碍格（虚空）→ Scenery。
        // 样本下界：1–20 号图里岩石地表格 ≥ 1，且平台上与低地上的岩石都出现。
        // 变异 MLT-G8（ObstacleArtAt 恒返回 Scenery）与段 B 的 MB-1（ObstacleArtAt 退回"棋盘清单外接矩形内"口径）见类注释。
        int rocks = 0, plateRocks = 0, obstacles = 0;
        for (int seed = 1; seed <= SeedSpan; seed++)
        {
            MapData map = MapCatalog.Resolve($"link:{seed}:4");
            MatchFlow match = MatchFlow.Create(map, TestMaps.Seed, All, Options);
            match.PlantSequentially(All.Select((p, i) => (p, i)));
            DefaultBoardView board = match.World(P0).Board();
            BoardPlate own = map.BirthBoard(0);

            foreach (BoardCellView cell in board.Cells)
            {
                bool rock = cell.Terrain == Terrain.Playable && cell.Surface == Surface.Rock;
                Assert.Equal(rock ? ObstacleArt.BoardRock : ObstacleArt.Scenery, board.ObstacleArtAt(cell.Coord));
                rocks += rock ? 1 : 0;
                plateRocks += rock && map.Boards.Any(b => b.Contains(cell.Coord)) ? 1 : 0;
                obstacles += cell.Terrain == Terrain.Obstacle ? 1 : 0;
            }

            // 画布留白的场景格（西南角 A1）：不属于任何可见棋盘 → 场景装饰。
            Assert.False(map.Boards.Any(b => b.Contains(own.Origin)) && own.Origin == new Coord(0, 0));
            Assert.Equal(ObstacleArt.Scenery, board.ObstacleArtAt(new Coord(0, 0)));
        }

        Assert.True(rocks >= 1, $"link:1–{SeedSpan} 的视图里一格岩石地表都没有，本测试无样本");
        Assert.True(plateRocks >= 1 && rocks > plateRocks, $"样本口径：平台上的岩石 {plateRocks} 格、低地岩石 {rocks - plateRocks} 格，两种都要有");
        Assert.True(obstacles >= 1, "样本口径：视图里要有障碍格（虚空），才证明它们取场景装饰");
    }

    /// <summary>链接图锁定后（P0 锁 1 号棋盘、四人依次插旗）的默认棋盘，与「岩石地表取岩石造型」同一取法。</summary>
    private static DefaultBoardView LockedView(int seed)
    {
        MatchFlow match = MatchFlow.Create(MapCatalog.Resolve($"link:{seed}:4"), TestMaps.Seed, All, Options);
        match.PlantSequentially(All.Select((p, i) => (p, i)));
        return match.World(P0).Board();
    }

    [Fact]
    public void 场景装饰落在岛外浮石上()
    {
        // shore-islets 方案 A：浮石只在紧贴岛缘的虚空格上；瀑布口正前方、两侧岸线对夹的格不放；任意两块不八邻相接；七种装饰在种子 1–20 里都出现。
        // 变异实跑（shore-islets，过滤本类）：去掉瀑布口判据 → 本测试红；去掉八邻间隔 → 本测试红；去掉对夹判据 → 本测试红。
        var kinds = new HashSet<SceneryKind>();
        int total = 0;
        for (int seed = 1; seed <= SeedSpan; seed++)
        {
            DefaultBoardView board = LockedView(seed);
            HashSet<Coord> island = ShoreScenery.IslandCells(board);
            Dictionary<Coord, BoardCellView> cellAt = board.Cells.ToDictionary(c => c.Coord);
            ImmutableArray<SceneryIslet> islets = board.SceneryIslets();
            Assert.NotEmpty(islets);
            foreach (SceneryIslet islet in islets)
            {
                Coord at = islet.Cell;
                Assert.Equal(Terrain.Obstacle, cellAt[at].Terrain);
                Assert.DoesNotContain(at, island);
                // 先判界再建 Coord（Coord 拒绝负下标）。
                bool IslandAt((int Dx, int Dy) d) => at.X + d.Dx >= 0 && at.Y + d.Dy >= 0 && island.Contains(new Coord(at.X + d.Dx, at.Y + d.Dy));
                (int Dx, int Dy)[] shore = [.. new (int Dx, int Dy)[] { (1, 0), (-1, 0), (0, 1), (0, -1) }.Where(IslandAt)];
                Assert.NotEmpty(shore);
                Assert.All(shore, d => Assert.NotEqual(Surface.DeepWater, cellAt[new Coord(at.X + d.Dx, at.Y + d.Dy)].Surface));
                Assert.False(shore.Any(d => shore.Contains((-d.Dx, -d.Dy))), $"link:{seed}:4 的浮石 {at.ToNotation()} 夹在两侧岸线之间");

                // 背离岛面的方向与岸线方向相反。
                Assert.Equal(-Math.Sign(shore.Sum(d => d.Dx)), islet.OutX);
                Assert.Equal(-Math.Sign(shore.Sum(d => d.Dy)), islet.OutY);
                Assert.True(islet.Variant >= 0);
                kinds.Add(islet.Kind);
            }

            foreach (SceneryIslet a in islets)
            {
                Assert.DoesNotContain(islets, b => b.Cell != a.Cell && Math.Abs(b.Cell.X - a.Cell.X) <= 1 && Math.Abs(b.Cell.Y - a.Cell.Y) <= 1);
            }

            total += islets.Length;
        }

        Assert.Equal(Enum.GetValues<SceneryKind>().Length, kinds.Count);
        Assert.True(total >= SeedSpan * 20, $"样本口径：link:1–{SeedSpan}:4 一共只有 {total} 块浮石");
    }

    [Fact]
    public void 浮石装饰由地图确定()
    {
        // 同一张图两次建视图（两次独立建局）逐项相同；换一张图不同。散列只用整数算术，不依赖进程随机种子。
        Assert.Equal(LockedView(1).SceneryIslets(), LockedView(1).SceneryIslets());
        Assert.NotEqual(LockedView(1).SceneryIslets(), LockedView(2).SceneryIslets());
    }

    [Fact]
    public void 岛形不完整时不放浮石()
    {
        // 插旗阶段：P0 还没插旗，视图里只有没展开的座位占位，岛形不完整。
        MatchFlow match = MatchFlow.Create(MapCatalog.Resolve("link:1:4"), TestMaps.Seed, All, Options);
        DefaultBoardView board = match.World(P0).Board();
        Assert.Contains(board.Placeholders, p => !p.Expanded);   // 样本口径：确有未展开的占位
        Assert.Empty(board.SceneryIslets());

        // 判据本身：同一份锁定后的全图格子，只要还挂着一块没展开的占位（或中立平台轮廓）就不放——不靠"插旗阶段恰好看不到虚空格"。
        // 变异实跑（shore-islets）：去掉"有未展开占位即返回空" → 本测试红（只靠上面的插旗阶段断言时该变异存活）；去掉"有中立平台轮廓即返回空" → 本测试红。
        DefaultBoardView locked = LockedView(1);
        Assert.NotEmpty(locked.SceneryIslets());   // 样本口径：不挂占位时确有浮石
        BoardPlate seat = locked.Boards.First(b => b.Kind == BoardPlateKind.Birth);
        Assert.Empty((locked with { Placeholders = [new BoardPlaceholderView(1, seat.Origin, seat.Width, seat.Height, Expanded: false, Own: false, Theme: null)] }).SceneryIslets());
        BoardPlate neutral = locked.Boards.First(b => b.Kind == BoardPlateKind.Public);
        Assert.Empty((locked with { NeutralPlates = [neutral] }).SceneryIslets());
    }

    [Fact]
    public void Godot浮石只经视图模型取用()
    {
        // BoardView 只按视图模型的浮石清单放浮石与装饰件，档号取清单里的散列（不走常驻路径的递增计数器 Variant()）。
        string boardView = File.ReadAllText(Path.Combine(TestPaths.RepoRoot(), "src", "godot", "scripts", "BoardView.cs"));
        Assert.Contains("board.SceneryIslets()", boardView, StringComparison.Ordinal);
        Assert.Contains("sink.Parts.Add(Decor(TerrainParts.Islet, islet.Cell), islet.Variant,", boardView, StringComparison.Ordinal);
        Assert.Contains("sink.Parts.Add(Decor(SceneryPartOf(islet.Kind), islet.Cell), islet.Variant >> 3,", boardView, StringComparison.Ordinal);

        // 岛面判定只有一份：BoardView.IslandCells 转调视图模型。
        Assert.Contains("IslandCells(DefaultBoardView board) => ShoreScenery.IslandCells(board);", boardView, StringComparison.Ordinal);
    }

    [Fact]
    public void 棋盘清单为空时一律是场景装饰()
    {
        // 手写 9×9 夹具没有棋盘清单：任何格都按场景装饰画，与引入本方法之前相同。
        // retire-legacy-rules 1.3（清单 P4，(a) 规则层）：对局只能建在链接档图上（必有清单），这里不经对局——在 9×9 地图数据的 GameBoard 上
        // 直接组装同一时刻的公开快照与补充载荷，再经 DefaultBoardView.From 取默认棋盘（与对局发布走同一构建函数），断言不变。
        GameBoard nine = GameBoard.LoadUnvalidated(TestMaps.NineByNine());
        var view = new MatchPublicView(
            nine.Map.Id, TestMaps.Seed.ToString(), MatchPhase.FlagPlanting, 0, MatchOptions.DefaultArtisanWeight, ContentSets.Default, TurnStage.Idle,
            null, [], [], nine, nine.Serialize(), null, null, [], 0, null, LifeShapeReport.Analyze(nine), false);
        DefaultBoardView board = DefaultBoardView.From(PublicWorld.From(view, new PublicSupplement(nine.Serialize(), 0, [], [], null, null)));
        Assert.Equal(81, board.Cells.Length);   // 样本口径：确是整张 9×9

        Assert.Empty(board.Boards);
        Assert.All(board.Cells, c => Assert.Equal(ObstacleArt.Scenery, board.ObstacleArtAt(c.Coord)));
    }

    [Fact]
    public void Godot障碍造型只经视图模型取用()
    {
        // BoardView 搭建地砖时按 ObstacleArtAt 分流：岩石地表走平板岩片点缀（terrain-bonus 段 B），场景格走场景面砖与散列装饰。
        // 变异 MLT-G2（BoardView 去掉 ObstacleArtAt 分流、退回"plated && !playable 一律场景面砖"，实跑）→ 本测试红 1。
        string boardView = File.ReadAllText(Path.Combine(TestPaths.RepoRoot(), "src", "godot", "scripts", "BoardView.cs"));
        Assert.Contains("board.ObstacleArtAt(cell.Coord)", boardView, StringComparison.Ordinal);
        Assert.Contains("ObstacleArt.BoardRock", boardView, StringComparison.Ordinal);

        // 棋盘上的逐格装饰（林地的树、岩石平板）挂在装饰层（open-map 段 B：拼接滑入与滑动父节点已删除，BoardView 不再有 ApplySlide）。
        // big-map 段 B（规格 visual-style-baseline「大图的分块渲染」：装饰"按区块各一套"）：装饰走格所属 CellSink——小图常驻 sink 的装饰节点就是 _decoration、大图为区块自己的 Decoration。
        // terrain-bonus 段 B：岩石平板与其他地表点缀同走 sink.Ornament 合批（原棋盘内巨石的逐格节点 sink.Decoration.AddChild(rock) 随障碍岩石删除）。
        Assert.Contains("sink.Ornament(TerrainParts.RockSlab,", boardView, StringComparison.Ordinal);
        Assert.Contains("new CellSink(tiles, _decoration,", boardView, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplySlide", boardView, StringComparison.Ordinal);
    }
}
