using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>规格：map-definition —— Requirement: 出生区归属与共享</summary>
public class 出生区归属Tests
{
    private static readonly MapData Map = FourPlayerBaseMap.Create();

    [Fact]
    public void 共享出生区()
    {
        // 玩家 A 与玩家 B 都锁定出生区 1：两人的合法落子范围是"该区的全部空格"这同一个集合。
        // 本层没有"锁定"概念，可观察的等价命题是：范围只由格子的空/可落子状态决定，
        // 与落子者是谁无关——A 落一子会同时从 B 的范围里消耗掉同一格。
        const int lockedZone = 1;
        GameBoard board = GameBoard.Load(Map);
        Coord[] LegalFor(PlayerId _) =>
            [.. Map.BirthZones[lockedZone].Where(c => board[c].IsPlayableEmpty).Order()];

        Coord[] before = LegalFor(TestMaps.P0);
        Assert.Equal(before, LegalFor(TestMaps.P1));
        Assert.Equal(15, before.Length);

        board.Place(before[0], TestMaps.P0, PieceType.Basic);
        board.Place(before[1], TestMaps.P1, PieceType.Basic);

        Assert.Equal(before.Skip(2), LegalFor(TestMaps.P0));
        Assert.Equal(LegalFor(TestMaps.P0), LegalFor(TestMaps.P1));
        Assert.All(Map.BirthZones[lockedZone], c => Assert.Equal(lockedZone, Map.BirthZoneOf(c)));
    }

    [Fact]
    public void 出生区标记不自带限制()
    {
        // 本层没有大回合概念：某格是否属于某出生区，不影响它能否落子。
        // 限制的生效时机（前三大回合）由对局流程层决定。
        GameBoard board = GameBoard.Load(Map);
        Coord inZone = Map.BirthZones[0].Where(c => board[c].IsPlayableEmpty).Order().First();
        Coord outsideZone = board.AllCoords()
            .First(c => board[c].IsPlayableEmpty && Map.BirthZoneOf(c) is null);

        board.Place(inZone, TestMaps.P0, PieceType.Basic);
        board.Place(outsideZone, TestMaps.P0, PieceType.Basic);

        Assert.NotNull(board[inZone].Occupant);
        Assert.NotNull(board[outsideZone].Occupant);
    }

    [Fact]
    public void 出生区互不重叠()
    {
        for (int i = 0; i < Map.BirthZones.Length; i++)
        {
            for (int j = i + 1; j < Map.BirthZones.Length; j++)
            {
                Assert.Empty(Map.BirthZones[i].Intersect(Map.BirthZones[j]));
            }
        }
    }

    [Fact]
    public void 非出生区格归属为空()
    {
        Assert.Null(Map.BirthZoneOf(Map.CentralEntrance));
    }
}
