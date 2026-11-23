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
