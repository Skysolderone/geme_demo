using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.CoverageTerritory;

/// <summary>规格：coverage-territory —— Requirement: 唯一覆盖查询</summary>
public class 唯一覆盖查询Tests
{
    private static GameBoard RelicBoard() =>
        GameBoard.LoadUnvalidated(TestMaps.Synthetic(
            size: 9, maxPlayers: 4,
            relics: [KeyValuePair.Create(TestMaps.At("E7"), new RelicCellSpec(RelicZone.Contested, BudgetTier.High))]));

    [Fact]
    public void 唯一覆盖者()
    {
        // 信物格 E7 只被 P1（E6）覆盖 → 唯一覆盖，覆盖者为 P1。
        GameBoard board = RelicBoard().Place("E6", TestMaps.P1);
        Assert.True(board[TestMaps.At("E7")].IsRelicCell);

        CoverageMap coverage = CoverageMap.Compute(board);

        Assert.Equal(TestMaps.P1, coverage.UniqueCoverer(TestMaps.At("E7")));
        Assert.True(coverage.CoverageOf(TestMaps.At("E7")).IsUnique);
        Assert.True(coverage.OwnershipOf(TestMaps.At("E7")).IsControlledBy(TestMaps.P1));
    }

    [Fact]
    public void 多人覆盖()
    {
        // 信物格 E7 同时被 P0（D7）与 P2（F7）覆盖 → 非唯一覆盖。
        // 变异验证 M18：Compute 把 SoleCoverer 改为 set.First()（不判断 Count == 1）→ 红 3，含本测试。
        GameBoard board = RelicBoard().Place("D7", TestMaps.P0).Place("F7", ScoringFixtures.P2);

        CoverageMap coverage = CoverageMap.Compute(board);

        Assert.Null(coverage.UniqueCoverer(TestMaps.At("E7")));
        Assert.Equal(new CellCoverage(2, null), coverage.CoverageOf(TestMaps.At("E7")));
        Assert.False(coverage.OwnershipOf(TestMaps.At("E7")).IsControlledBy(TestMaps.P0));
        Assert.False(coverage.OwnershipOf(TestMaps.At("E7")).IsControlledBy(ScoringFixtures.P2));
    }

    [Fact]
    public void 林地信物只能占据()
    {
        // 玩家 B 的棋子（E6）与林地信物格 E7 几何相邻 → E7 无人覆盖、中立；B 落子占据 E7 后才控制
        GameBoard board = GameBoard.LoadUnvalidated(TestMaps.Synthetic(
            size: 9, maxPlayers: 4,
            relics: [KeyValuePair.Create(TestMaps.At("E7"), new RelicCellSpec(RelicZone.Contested, BudgetTier.High))],
            terrain: TestMaps.Terrain(surfaces: [("E7", Surface.Forest)])));
        board.Place("E6", TestMaps.P1);

        CoverageMap before = CoverageMap.Compute(board);
        Assert.Equal(CellCoverage.None, before.CoverageOf(TestMaps.At("E7")));
        Assert.Null(before.UniqueCoverer(TestMaps.At("E7")));
        Assert.Equal(new CellOwnership(OwnershipKind.Neutral, null), before.OwnershipOf(TestMaps.At("E7")));

        board.Place("E7", TestMaps.P1);
        CoverageMap after = CoverageMap.Compute(board);
        Assert.Equal(new CellOwnership(OwnershipKind.Occupied, TestMaps.P1), after.OwnershipOf(TestMaps.At("E7")));
        Assert.True(after.OwnershipOf(TestMaps.At("E7")).IsControlledBy(TestMaps.P1));
    }

    [Fact]
    public void 唯一覆盖查询与空格归属交叉一致()
    {
        // 两套判定 MUST 读同一份覆盖表：对全盘每一格断言 OwnershipOf 与 UniqueCoverer / CoverageOf 不矛盾。
        // 盘面由固定种子随机生成（4 人、含障碍），覆盖独占 / 争议 / 中立 / 占据 / 障碍五种情况。
        // 变异验证：M7（占据优先失效）、M15（障碍传播覆盖）、M18（SoleCoverer 取 First）、M22（去掉 Obstacle 分支）四条都让本测试红。
        var rng = new Random(20260913);
        GameBoard board = TestMaps.Blank(size: 11, "C3", "H8", "F6", "B9");
        PlayerId[] players = [TestMaps.P0, TestMaps.P1, ScoringFixtures.P2, ScoringFixtures.P3];
        foreach (Coord c in board.AllCoords())
        {
            if (board[c].Terrain == Terrain.Playable && rng.Next(3) == 0)
            {
                board.Place(c, players[rng.Next(players.Length)], PieceType.Basic);
            }
        }

        PowerSnapshot snapshot = PowerCalculator.Compute(board);
        CoverageMap coverage = snapshot.Coverage;
        var seen = new HashSet<OwnershipKind>();

        foreach (Coord c in board.AllCoords())
        {
            Cell cell = board[c];
            CellOwnership ownership = coverage.OwnershipOf(c);
            CellCoverage cover = coverage.CoverageOf(c);
            seen.Add(ownership.Kind);

            if (cell.Terrain == Terrain.Obstacle)
            {
                Assert.Equal(new CellOwnership(OwnershipKind.Obstacle, null), ownership);
                Assert.Equal(CellCoverage.None, cover);
                continue;
            }

            if (cell.Occupant is { } occupant)
            {
                Assert.Equal(new CellOwnership(OwnershipKind.Occupied, occupant.Owner), ownership);
                continue;
            }

            switch (ownership.Kind)
            {
                case OwnershipKind.Exclusive:
                    Assert.True(cover.IsUnique);
                    Assert.Equal(coverage.UniqueCoverer(c), ownership.Owner);
                    Assert.Contains(c, coverage.ExclusiveCellsOf(ownership.Owner!.Value));
                    Assert.Contains(c, snapshot.Of(ownership.Owner.Value).ExclusiveCells);
                    break;
                case OwnershipKind.Contested:
                    Assert.True(cover.CovererCount >= 2);
                    Assert.Null(coverage.UniqueCoverer(c));
                    break;
                case OwnershipKind.Neutral:
                    Assert.Equal(CellCoverage.None, cover);
                    break;
                default:
                    Assert.Fail($"空的可落子格 {c} 不应判为 {ownership.Kind}");
                    break;
            }

            foreach (PlayerId p in players)
            {
                Assert.Equal(ownership.Kind == OwnershipKind.Exclusive && ownership.Owner == p, coverage.ExclusiveCellsOf(p).Contains(c));
            }
        }

        // 随机盘面必须真的触发了全部五种归属，否则上面的分支未被验证
        Assert.Equal(5, seen.Count);
    }
}
