using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>规格：map-definition —— Requirement: 4 人基准地图</summary>
public class 四人基准地图Tests
{
    private static readonly MapData Map = FourPlayerBaseMap.Create();

    [Fact]
    public void 外接尺寸与可落子格()
    {
        Assert.Equal(11, Map.Width);
        Assert.Equal(11, Map.Height);
        Assert.InRange(Map.PlayableCount, 100, 115);
    }

    [Fact]
    public void 四个出生区各十二至十五格()
    {
        Assert.Equal(4, Map.BirthZones.Length);
        foreach (var zone in Map.BirthZones)
        {
            Assert.InRange(zone.Count(c => Map.TerrainAt(c) == Terrain.Playable), 12, 15);
        }
    }

    [Fact]
    public void 信物格分布()
    {
        // 每个出生区各 2 个，公共争夺区约 6 个，总数落在 13–15 区间
        for (int i = 0; i < Map.BirthZones.Length; i++)
        {
            int inZone = Map.RelicCells.Keys.Count(c => Map.BirthZoneOf(c) == i);
            Assert.Equal(2, inZone);
        }

        Assert.Equal(6, Map.RelicCells.Count(kv => kv.Value.Zone == RelicZone.Contested));
        Assert.InRange(Map.RelicCells.Count, 13, 15);
    }

    [Fact]
    public void 出生区容量()
    {
        // 单名玩家前三大回合各部署 3 枚且不发生提子，每一步都仍存在合法空格
        GameBoard board = GameBoard.Load(Map);
        var zone = Map.BirthZones[0];

        for (int round = 0; round < 3; round++)
        {
            for (int stone = 0; stone < 3; stone++)
            {
                Coord? free = zone.Where(c => board[c].IsPlayableEmpty).Order().Cast<Coord?>().FirstOrDefault();
                Assert.True(free is not null, $"第 {round + 1} 大回合第 {stone + 1} 枚部署时出生区已无合法空格。");
                board.Place(free!.Value, TestMaps.P0, PieceType.Basic);
            }
        }

        Assert.Equal(9, board.GroupsOf(TestMaps.P0).Sum(g => g.Size));
        Assert.Contains(zone, c => board[c].IsPlayableEmpty);
    }

    [Fact]
    public void 中央区预算严格高于出生区()
    {
        // 设计文档 §8.2：中央、咽喉与高风险边缘承担更高的信物强度预算
        BudgetTier maxBirth = Map.RelicCells.Values
            .Where(s => s.Zone == RelicZone.BirthZone).Max(s => s.Budget);
        BudgetTier minContested = Map.RelicCells.Values
            .Where(s => s.Zone == RelicZone.Contested).Min(s => s.Budget);

        Assert.True(minContested > maxBirth, "公共争夺区的预算档位必须严格高于出生区。");
        Assert.Contains(Map.RelicCells.Values, s => s.Budget == BudgetTier.High);
    }

    [Fact]
    public void 出生区不含高阶预算信物()
    {
        foreach (RelicCellSpec spec in Map.RelicCells
                     .Where(kv => Map.BirthZoneOf(kv.Key) is not null).Select(kv => kv.Value))
        {
            Assert.Equal(BudgetTier.Birth, spec.Budget);
        }
    }
}
