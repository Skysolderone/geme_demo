using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.PieceEffects;

/// <summary>规格：more-pieces-relics piece-effects —— Requirement: 旗手子的位置加值</summary>
/// <remarks>
/// 旗手子：自身格 ∪ 气边邻格中每个信物格 +3；信物格按地图静态位置，不读揭示、内容与控制；气边只经 <see cref="GameBoard.LibertyNeighbors"/>。
/// 变异记录见 <c>.trellis/tasks/09-25-more-pieces-relics/implement.md</c> 段 A（1.3）。
/// </remarks>
public class 旗手子的位置加值Tests
{
    [Fact]
    public void 站在信物格上并邻接另一信物格()
    {
        // 规格 Scenario：旗手子位于信物格 F6，G6 也是信物格且二者有气边，其余气边邻格都不是信物格 → 3 × 2 = 6。
        GameBoard board = TestMaps.WithRelicCells(["F6", "G6"]).Place("F6", TestMaps.P0, PieceType.Bannerman);

        GroupPower group = PowerCalculator.Compute(board).GroupContaining(TestMaps.P0, "F6");

        Assert.Equal(6, group.BannerBonus);
        Assert.Equal(6, group.PositionBonus);
        Assert.Equal(1 + 6, group.Power);
    }

    [Fact]
    public void 栅栏隔开的信物格不计()
    {
        // 规格 Scenario：旗手子位于非信物格 F6，唯一相邻的信物格 E6 与 F6 之间有栅栏 → 0。
        GameBoard board = TestMaps.WithRelicCells(["E6"], TestMaps.Terrain(fences: [("E6", "F6")])).Place("F6", TestMaps.P0, PieceType.Bannerman);

        Assert.DoesNotContain(TestMaps.At("E6"), board.LibertyNeighbors(TestMaps.At("F6")));   // 前提：几何相邻、无气边
        Assert.Equal(0, PowerCalculator.Compute(board).GroupContaining(TestMaps.P0, "F6").BannerBonus);
    }

    [Fact]
    public void 崖壁隔开的信物格不计()
    {
        // 规格 Scenario：旗手子位于 h=2 的 F6，几何相邻的信物格 G6 高度 0，二者之间没有气边 → G6 不计。
        GameBoard board = TestMaps.WithRelicCells(["G6"], TestMaps.Terrain(heights: [("F6", 2)])).Place("F6", TestMaps.P0, PieceType.Bannerman);

        Assert.DoesNotContain(TestMaps.At("G6"), board.LibertyNeighbors(TestMaps.At("F6")));   // 前提：崖壁切断气边
        Assert.Equal(0, PowerCalculator.Compute(board).GroupContaining(TestMaps.P0, "F6").BannerBonus);
    }

    [Fact]
    public void 不看揭示与控制()
    {
        // 规格 Scenario：旗手子位于 F6；经气边相邻的 G6 是尚未揭示的空信物格，经气边相邻的 F7 被玩家 B 的棋子占据、由 B 控制 → 3 × 2 = 6。
        // 计分层的输入只有盘面：揭示状态不在盘面上（本层读不到），控制由占据体现——F7 被 B 占据而仍计入，证明不看控制。
        GameBoard board = TestMaps.WithRelicCells(["G6", "F7"]).Place("F6", TestMaps.P0, PieceType.Bannerman).Place("F7", TestMaps.P1);

        PowerSnapshot snapshot = PowerCalculator.Compute(board);
        Assert.True(snapshot.Coverage.OwnershipOf(TestMaps.At("F7")).IsControlledBy(TestMaps.P1));   // 前提：F7 由 B 控制
        Assert.Equal(6, snapshot.GroupContaining(TestMaps.P0, "F6").BannerBonus);
    }

    [Fact]
    public void 同一信物格分别为两枚旗手子计分()
    {
        // 规格 Scenario：A 的两枚旗手子分别位于 F6 与 H6，二者都与空信物格 G6 有气边、各自没有其他相邻信物格 → 各 3 点，合计 6；分属两条棋串各计入自己的棋串。
        GameBoard board = TestMaps.WithRelicCells(["G6"])
            .Place("F6", TestMaps.P0, PieceType.Bannerman).Place("H6", TestMaps.P0, PieceType.Bannerman);

        PlayerPower player = PowerCalculator.Compute(board).Of(TestMaps.P0);

        Assert.Equal(2, player.Groups.Length);   // 前提：G6 空着，两枚分属两条棋串
        Assert.All(player.Groups, g => Assert.Equal(3, g.BannerBonus));
        Assert.Equal(6, player.Groups.Sum(g => g.BannerBonus));
    }
}
