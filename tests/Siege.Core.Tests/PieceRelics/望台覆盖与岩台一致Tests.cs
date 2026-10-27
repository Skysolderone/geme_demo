using Siege.Core.Board;
using Siege.Core.PieceRelics;
using Siege.Core.Scoring;
using static Siege.Core.Tests.PieceRelics.PieceRelicFixtures;

namespace Siege.Core.Tests.PieceRelics;

/// <summary>
/// relic-redesign RQ3 / AC3（望台）：挂望台的子按岩台规则额外覆盖四方向距离 2（design.md D3）——覆盖目标与站在岩台上逐项相同；
/// 岩台上再挂望台不再扩大；沼泽上的子仍不提供覆盖（沼泽优先）。覆盖表、空格归属与领地分都经同一份覆盖目标继承。
/// 变异验证（加法段实跑）：M4 GameBoard.CoverageTargets 对望台传 farReach: false → 红 3。
/// </summary>
public class 望台覆盖与岩台一致Tests
{
    [Fact]
    public void 望台的覆盖目标与站在岩台上相同()
    {
        GameBoard tower = TestMaps.Blank(size: 9).Place("E5", TestMaps.P0).Attach("E5", Map(0, RelicKind.Watchtower));
        GameBoard crag = TestMaps.Blank(TestMaps.Terrain(surfaces: [("E5", Surface.Crag)]), size: 9).Place("E5", TestMaps.P0);

        // 期望按坐标字典序（先行后列）手写：E3、E4、C5、D5、F5、G5、E6、E7。
        Assert.Equal(["E3", "E4", "C5", "D5", "F5", "G5", "E6", "E7"], tower.CoverageTargets(TestMaps.At("E5")).Notations());
        Assert.Equal(crag.CoverageTargets(TestMaps.At("E5")), tower.CoverageTargets(TestMaps.At("E5")));
    }

    [Fact]
    public void 没有望台时只覆盖四邻()
    {
        GameBoard board = TestMaps.Blank(size: 9).Place("E5", TestMaps.P0).Attach("E5", Map(0, RelicKind.WarDrum));

        Assert.Equal(["E4", "D5", "F5", "E6"], board.CoverageTargets(TestMaps.At("E5")).Notations());
    }

    [Fact]
    public void 望台让远格成为独占空格并计领地分()
    {
        GameBoard plain = TestMaps.Blank(size: 9).Place("E5", TestMaps.P0);
        GameBoard tower = TestMaps.Blank(size: 9).Place("E5", TestMaps.P0).Attach("E5", Map(0, RelicKind.Watchtower));

        PowerSnapshot before = PowerCalculator.Compute(plain);
        PowerSnapshot after = PowerCalculator.Compute(tower);

        Assert.DoesNotContain(TestMaps.At("E7"), before.Of(TestMaps.P0).ExclusiveCells);
        Assert.Contains(TestMaps.At("E7"), after.Of(TestMaps.P0).ExclusiveCells);
        Assert.Equal(4, before.Of(TestMaps.P0).ScoredCells.Length);
        Assert.Equal(8, after.Of(TestMaps.P0).ScoredCells.Length);
        Assert.Equal(TestMaps.P0, after.Coverage.UniqueCoverer(TestMaps.At("G5")));
    }

    [Fact]
    public void 岩台上挂望台不再扩大()
    {
        TerrainData crag = TestMaps.Terrain(surfaces: [("E5", Surface.Crag)]);
        GameBoard plain = TestMaps.Blank(crag, size: 9).Place("E5", TestMaps.P0);
        GameBoard tower = TestMaps.Blank(crag, size: 9).Place("E5", TestMaps.P0).Attach("E5", Map(0, RelicKind.Watchtower));

        Assert.Equal(plain.CoverageTargets(TestMaps.At("E5")), tower.CoverageTargets(TestMaps.At("E5")));
    }

    [Fact]
    public void 沼泽上的子挂望台仍不提供覆盖()
    {
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("E5", Surface.Marsh)]), size: 9)
            .Place("E5", TestMaps.P0).Attach("E5", Map(0, RelicKind.Watchtower));

        Assert.Empty(board.CoverageTargets(TestMaps.At("E5")));
    }

    [Fact]
    public void 望台远格同样被障碍阻挡()
    {
        // 岩台远格规则：中间格是障碍时不越过（与岩台同一实现）。
        GameBoard board = TestMaps.Blank(9, "E6").Place("E5", TestMaps.P0).Attach("E5", Map(0, RelicKind.Watchtower));
        GameBoard crag = TestMaps.Blank(TestMaps.Terrain(surfaces: [("E5", Surface.Crag)]), 9, "E6").Place("E5", TestMaps.P0);

        Assert.DoesNotContain(TestMaps.At("E7"), board.CoverageTargets(TestMaps.At("E5")));
        Assert.Equal(crag.CoverageTargets(TestMaps.At("E5")), board.CoverageTargets(TestMaps.At("E5")));
    }

    [Fact]
    public void 持物棋子被提后望台不再覆盖()
    {
        GameBoard board = TestMaps.Blank(size: 9).Place("E5", TestMaps.P0).Attach("E5", Map(0, RelicKind.Watchtower));

        board.RemoveStones([TestMaps.At("E5")]);

        Assert.Empty(PowerCalculator.Compute(board).Coverage.SourcesOf(TestMaps.At("E7")));
    }
}
