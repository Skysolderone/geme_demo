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
        GameBoard board = GameBoard.LoadUnvalidated(FourPlayerBaseMapData());
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
        MapData map = FourPlayerBaseMapData();

        // v3 基准图 13×13：(13, 0) / (0, 13) 刚好越界一格；G7 是盘内可落子格（中央入口）。
        Assert.False(map.Contains(new Coord(13, 0)));
        Assert.Equal(Terrain.Obstacle, map.TerrainAt(new Coord(13, 0)));
        Assert.Equal(Terrain.Obstacle, map.TerrainAt(new Coord(0, 13)));
        Assert.Equal(Terrain.Obstacle, map.TerrainAt(new Coord(24, 99)));
        Assert.Equal(Terrain.Playable, map.TerrainAt(TestMaps.At("G7")));
    }

    private static MapData FourPlayerBaseMapData() => Siege.Core.Board.Maps.FourPlayerBaseMap.Create();
}
