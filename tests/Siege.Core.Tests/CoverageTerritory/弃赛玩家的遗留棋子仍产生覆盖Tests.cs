using System.Numerics;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Scoring;
using Siege.Core.Recruit;

namespace Siege.Core.Tests.CoverageTerritory;

/// <summary>规格：coverage-territory —— Requirement: 弃赛玩家的遗留棋子仍产生覆盖</summary>
public class 弃赛玩家的遗留棋子仍产生覆盖Tests
{
    [Fact]
    public void 弃赛者遗留棋子制造争议()
    {
        // 规格 Scenario（coverage-territory「弃赛玩家的遗留棋子仍产生覆盖」）：已弃赛 D（P3）的 D6 与参赛 A（P0）的 D4 同时覆盖空格 D5
        // → 该格判定为争议，玩家 A 不获得该格的领地分。
        // 段 A 重算：原期望 A / D 势力各 1（scoring-sites：独占空格不计分）→ 各 4 = 军势 1 + 领地 3（四邻中 D5 是争议格，不计）。
        // 段 B：D5 原先另标着一个据点（营帐），据点随本段整体摘除，摆法与全部数值不变。
        // 变异验证 M6：PowerCalculator.Compute 在算覆盖前把非 Active 玩家的棋子从副本上清掉 → 红，含本测试（D5 变为 P0 唯一覆盖、A 得 5 分）与「弃赛者遗留棋子可被围杀」。
        IReadOnlyDictionary<PlayerId, PlayerStatus> roster = ScoringFixtures.Roster(
            (TestMaps.P0, PlayerStatus.Active), (ScoringFixtures.P3, PlayerStatus.Resigned));
        GameBoard board = TestMaps.Blank(size: 7).Place("D4", TestMaps.P0).Place("D6", ScoringFixtures.P3);

        PowerSnapshot snapshot = PowerCalculator.Compute(board, roster);

        Assert.Equal(OwnershipKind.Contested, snapshot.Coverage.OwnershipOf(TestMaps.At("D5")).Kind);
        PlayerPower a = snapshot.Of(TestMaps.P0);
        Assert.DoesNotContain(TestMaps.At("D5"), a.ExclusiveCells);
        Assert.Equal(3, a.ExclusiveCells.Length);
        Assert.Equal((3, (BigInteger)4), (a.TerritoryScore, a.Total));
        // 弃赛者照常产生独占与势力，明细里标记为已弃赛
        PlayerPower d = snapshot.Of(ScoringFixtures.P3);
        Assert.Equal(PlayerStatus.Resigned, d.Status);
        Assert.Equal(3, d.ExclusiveCells.Length);
        Assert.Equal((3, (BigInteger)4), (d.TerritoryScore, d.Total));
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
        Assert.Equal(OwnershipKind.Occupied, PowerCalculator.Compute(board, roster).Coverage.OwnershipOf(TestMaps.At("D4")).Kind);

        SettlementDriver driver = BatchFixtures.Driver(board);
        Assert.True(driver.Confirm(BatchFixtures.Context(board, TestMaps.P0), [BatchFixtures.P("D5")]).Confirmed);

        PowerSnapshot after = PowerCalculator.Compute(board, roster);
        Assert.Equal(new CellOwnership(OwnershipKind.Exclusive, TestMaps.P0), after.Coverage.OwnershipOf(TestMaps.At("D4")));
        Assert.Equal(0, after.Of(ScoringFixtures.P3).Total);
        Assert.Empty(after.Of(ScoringFixtures.P3).Groups);
    }
}
