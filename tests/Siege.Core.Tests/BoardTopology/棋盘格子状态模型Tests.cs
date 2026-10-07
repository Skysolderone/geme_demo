using Siege.Core.Board;

namespace Siege.Core.Tests.BoardTopology;

/// <summary>规格：board-topology —— Requirement: 棋盘格子状态模型</summary>
public class 棋盘格子状态模型Tests
{
    [Fact]
    public void 障碍格拒绝占用()
    {
        GameBoard board = TestMaps.Blank(size: 5, "C3");

        SiegeRuleException ex = Assert.Throws<SiegeRuleException>(
            () => board.Place(TestMaps.At("C3"), TestMaps.P0, PieceType.Basic));

        Assert.Contains("不可落子", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 信物格可正常落子()
    {
        GameBoard board = GameBoard.LoadUnvalidated(RelicMap());
        Coord relic = TestMaps.At("B2");
        Assert.True(board[relic].IsRelicCell);

        board.Place(relic, TestMaps.P0, PieceType.Fortress);

        Cell cell = board[relic];
        Assert.Equal(new Occupant(TestMaps.P0, PieceType.Fortress), cell.Occupant);
        Assert.True(cell.IsRelicCell, "信物标记不得因落子而改变。");
    }

    [Fact]
    public void 棋盘外沿等同封堵()
    {
        // A1 在角上：越界的左、下两个方向不贡献气，效果与相邻障碍完全一致
        GameBoard corner = TestMaps.Blank(size: 5).Place("A1", TestMaps.P0);
        GameBoard walled = TestMaps.Blank(size: 5, "A2", "B1").Place("A1", TestMaps.P0);

        Assert.Equal(2, corner.LibertiesOf(corner.GroupAt(TestMaps.At("A1"))!).Length);
        Assert.Empty(walled.LibertiesOf(walled.GroupAt(TestMaps.At("A1"))!));
    }

    [Fact]
    public void 越界坐标的地形就是障碍()
    {
        // "棋盘外沿与障碍具有相同的封堵语义"这句话在数据层的落点：
        // 越界格的地形 MUST 就是障碍，而不是靠每个调用方各自记得先判边界。
        MapData map = RelicMap();

        // 合成 9×9 图（retire-legacy-maps 段 A2，此前 v3 / v5 13×13）：(9, 0) / (0, 9) 刚好越界一格；G7 是盘内可落子格。
        Assert.False(map.Contains(new Coord(9, 0)));
        Assert.True(map.Contains(new Coord(8, 0)));
        Assert.Equal(Terrain.Obstacle, map.TerrainAt(new Coord(9, 0)));
        Assert.Equal(Terrain.Obstacle, map.TerrainAt(new Coord(0, 9)));
        Assert.Equal(Terrain.Obstacle, map.TerrainAt(new Coord(24, 99)));
        Assert.Equal(Terrain.Playable, map.TerrainAt(TestMaps.At("G7")));
    }

    /// <summary>B2 是信物格的合成 9×9 图（retire-legacy-maps 段 A2：此前用 v5，B2 是其出生区信物格）。</summary>
    private static MapData RelicMap() => MatchFixtures.Map("B2");
}
