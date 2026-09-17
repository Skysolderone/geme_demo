using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.CoverageTerritory;

/// <summary>规格：coverage-territory —— Requirement: 弃赛玩家的遗留棋子仍产生覆盖</summary>
public class 弃赛玩家的遗留棋子仍产生覆盖Tests
{
    [Fact]
    public void 弃赛者遗留棋子制造争议()
    {
        // 规格 Scenario（scoring-sites 改写）：已弃赛 D（P3）的 D6 与参赛 A（P0）的 D4 同时覆盖空的据点格 D5（营帐）→ 争议，A 不获得该据点分。
        // scoring-sites 2.7 改写：旧期望 A 领地 3、D 领地 3、D 势力 4（军势 1 + 领地 3）→ 新期望独占空格仍各 3 个（只展示），D 势力 1（独占空格不计分）。
        // 变异验证 M6：PowerCalculator.Compute 在算覆盖前把非 Active 玩家的棋子从副本上清掉 → 红，含本测试（D5 变为 P0 唯一覆盖、A 得 5 分）与「弃赛者遗留棋子可被围杀」。
        IReadOnlyDictionary<PlayerId, PlayerStatus> roster = ScoringFixtures.Roster(
            (TestMaps.P0, PlayerStatus.Active), (ScoringFixtures.P3, PlayerStatus.Resigned));
        GameBoard board = TestMaps.Blank(size: 7).WithSites(("D5", SiteTier.Tent)).Place("D4", TestMaps.P0).Place("D6", ScoringFixtures.P3);

        PowerSnapshot snapshot = PowerCalculator.Compute(board, roster, SiteValues.Standard);

        Assert.Equal(OwnershipKind.Contested, snapshot.Coverage.OwnershipOf(TestMaps.At("D5")).Kind);
        Assert.Equal(new SiteState(TestMaps.At("D5"), SiteTier.Tent, SiteControlKind.Contested, null), Assert.Single(snapshot.SiteStates));
        PlayerPower a = snapshot.Of(TestMaps.P0);
        Assert.DoesNotContain(TestMaps.At("D5"), a.ExclusiveCells);
        Assert.Equal(3, a.ExclusiveCells.Length);
        Assert.Empty(a.Sites);
        Assert.Equal(0, a.SiteScore);
        Assert.Equal(1, a.Total);
        // 弃赛者照常产生独占与势力，明细里标记为已弃赛
        PlayerPower d = snapshot.Of(ScoringFixtures.P3);
        Assert.Equal(PlayerStatus.Resigned, d.Status);
        Assert.Equal(3, d.ExclusiveCells.Length);
        Assert.Equal(1, d.Total);
        Assert.Equal(["C6", "E6", "D7"], d.ExclusiveCells.Notations());
    }

    [Fact]
    public void 弃赛者遗留棋子可被围杀()
    {
        // §12.2：弃赛者的棋子可以被其他玩家围杀。P3 的 D4 被 P0 提走后，D4 及周边归 P0。
        IReadOnlyDictionary<PlayerId, PlayerStatus> roster = ScoringFixtures.Roster(
            (TestMaps.P0, PlayerStatus.Active), (ScoringFixtures.P3, PlayerStatus.Resigned));
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("D4", ScoringFixtures.P3)
            .Place("C4", TestMaps.P0).Place("E4", TestMaps.P0).Place("D3", TestMaps.P0);
        Assert.Equal(OwnershipKind.Occupied, PowerCalculator.Compute(board, roster, SiteValues.Standard).Coverage.OwnershipOf(TestMaps.At("D4")).Kind);

        SettlementDriver driver = BatchFixtures.Driver(board);
        Assert.True(driver.Confirm(BatchFixtures.Context(board, TestMaps.P0), [BatchFixtures.P("D5")]).Confirmed);

        PowerSnapshot after = PowerCalculator.Compute(board, roster, SiteValues.Standard);
        Assert.Equal(new CellOwnership(OwnershipKind.Exclusive, TestMaps.P0), after.Coverage.OwnershipOf(TestMaps.At("D4")));
        Assert.Equal(0, after.Of(ScoringFixtures.P3).Total);
        Assert.Empty(after.Of(ScoringFixtures.P3).Groups);
    }
}
