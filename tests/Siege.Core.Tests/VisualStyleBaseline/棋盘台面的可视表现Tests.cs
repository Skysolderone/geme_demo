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
/// </remarks>
public class 棋盘台面的可视表现Tests
{
    /// <summary>扫描区间：地图种子 1–N（写死，不挑单个种子）。</summary>
    private const int SeedSpan = 20;

    [Fact]
    public void 棋盘内岩石取岩石造型_场景格取场景装饰()
    {
        // 每张图让 P0 锁定 1 号棋盘、构筑期按座位过滤后的默认棋盘（open-map：锁定后 5 块都可见）：棋盘内的不可落子非深水格（岩石）→ BoardRock；
        // 棋盘外接矩形之外的格（场景格，不在视图里）→ Scenery。样本下界：1–20 号图里至少出现一格岩石。
        // 变异 MLT-G8（ObstacleArtAt 恒返回 Scenery，实跑）→ 红 1（本测试）。
        int rocks = 0;
        for (int seed = 1; seed <= SeedSpan; seed++)
        {
            MapData map = MapCatalog.Resolve($"link:{seed}:4");
            MatchFlow match = MatchFlow.Create(map, TestMaps.Seed, All, Options);
            match.PlantSequentially(All.Select((p, i) => (p, i)));
            DefaultBoardView board = match.World(P0).Board();
            BoardPlate own = map.BirthBoard(0);

            // map-expand-25 段 B：插旗阶段之后座位可见整张图——视图里的岩石既有平台内的，也有平台之间低地 / 岛缘的与画布留白；
            // 平台外接矩形内的取岩石造型，之外的（低地岩石、留白）一律场景装饰。
            foreach (BoardCellView cell in board.Cells.Where(c => c.Terrain == Terrain.Obstacle && c.Surface != Surface.DeepWater))
            {
                bool inBoard = map.Boards.Any(b => b.Contains(cell.Coord));
                Assert.Equal(inBoard ? ObstacleArt.BoardRock : ObstacleArt.Scenery, board.ObstacleArtAt(cell.Coord));
                rocks += inBoard ? 1 : 0;
            }

            // 画布留白的场景格（西南角 A1）：不属于任何可见棋盘 → 场景装饰。
            Assert.False(map.Boards.Any(b => b.Contains(own.Origin)) && own.Origin == new Coord(0, 0));
            Assert.Equal(ObstacleArt.Scenery, board.ObstacleArtAt(new Coord(0, 0)));
        }

        Assert.True(rocks >= 1, $"link:1–{SeedSpan} 的棋盘里一格岩石都没有，本测试无样本");
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
        // BoardView 搭建地砖时按 ObstacleArtAt 分流：棋盘内岩石走棋盘格画法（逐格节点、随拼接滑动），场景格走场景面砖与散列装饰。
        // 变异 MLT-G2（BoardView 去掉 ObstacleArtAt 分流、退回"plated && !playable 一律场景面砖"，实跑）→ 本测试红 1。
        string boardView = File.ReadAllText(Path.Combine(TestPaths.RepoRoot(), "src", "godot", "scripts", "BoardView.cs"));
        Assert.Contains("board.ObstacleArtAt(cell.Coord)", boardView, StringComparison.Ordinal);
        Assert.Contains("ObstacleArt.BoardRock", boardView, StringComparison.Ordinal);

        // 棋盘上的逐格装饰（林地的树、棋盘内岩石）挂在装饰层 _decoration（open-map 段 B：拼接滑入与滑动父节点已删除，BoardView 不再有 ApplySlide）。
        Assert.Contains("_decoration.AddChild(rock)", boardView, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplySlide", boardView, StringComparison.Ordinal);
    }
}
