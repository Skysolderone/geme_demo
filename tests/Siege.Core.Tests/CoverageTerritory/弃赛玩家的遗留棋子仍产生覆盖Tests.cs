using System.Numerics;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Relics;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.CoverageTerritory;

/// <summary>规格：coverage-territory —— Requirement: 弃赛玩家的遗留棋子仍产生覆盖</summary>
public class 弃赛玩家的遗留棋子仍产生覆盖Tests
{
    [Fact]
    public void 弃赛者遗留棋子制造争议()
    {
        // 规格 Scenario（scoring-sites 改写）：已弃赛 D（P3）的 D6 与参赛 A（P0）的 D4 同时覆盖空的据点格 D5（营帐）→ 争议，A 不获得该据点分。
        // restore-go-core-rules coverage-territory 规格：该格判定为争议，玩家 A 不获得该格的领地分。
        // 段 A 重算：原期望 A / D 势力各 1（scoring-sites：独占空格不计分）→ 各 4 = 军势 1 + 领地 3（四邻中 D5 是争议格，不计）。据点格 D5 的据点断言到段 B 随据点一并删除。
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
        Assert.Equal((3, (BigInteger)4), (a.TerritoryScore, a.Total));
        // 弃赛者照常产生独占与势力，明细里标记为已弃赛
        PlayerPower d = snapshot.Of(ScoringFixtures.P3);
        Assert.Equal(PlayerStatus.Resigned, d.Status);
        Assert.Equal(3, d.ExclusiveCells.Length);
        Assert.Equal((3, (BigInteger)4), (d.TerritoryScore, d.Total));
        Assert.Equal(["C6", "E6", "D7"], d.ExclusiveCells.Notations());
    }

    [Fact]
    public void 弃赛者遗留棋子封锁信物()
    {
        // restore-go-core-rules coverage-territory 规格（段 A 新增的 Scenario，原文只有「据点」一条）：
        // 已弃赛玩家 D（F7）与参赛玩家 A（D7）同时覆盖空的信物格 E7 → 该信物判定为争议，A 不获得其效果。
        // 与「弃赛者遗留棋子制造争议」的区别在于落点是信物格：归属三态与信物控制读同一份覆盖表，弃赛者的覆盖照常参与。
        // A 的效果快照取缺省值（部署上限 3）即"没拿到军令"；同时钉住 A 的领地分不含 E7。
        // 变异验证 M-AC8（本次 check 实跑）：RelicLedger.RecalculateCore 的 OwnershipKind.Contested 分支改为
        // Resolve(最小编号的覆盖者, …)（争议也判给人）→ 红 12，含本测试。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Command()));
        board.Place("D7", TestMaps.P0).Place("F7", ScoringFixtures.P3);
        IReadOnlyDictionary<PlayerId, PlayerStatus> roster = ScoringFixtures.Roster(
            (TestMaps.P0, PlayerStatus.Active), (ScoringFixtures.P3, PlayerStatus.Resigned));

        ledger.Settle(board, 1, roster);
        PowerSnapshot snapshot = PowerCalculator.Compute(board, roster, SiteValues.Standard);

        Assert.True(board[TestMaps.At("E7")].IsRelicCell);
        Assert.Equal(OwnershipKind.Contested, snapshot.Coverage.OwnershipOf(TestMaps.At("E7")).Kind);
        Assert.Equal(RelicControl.Contested, ledger.ControlOf(TestMaps.At("E7")));
        Assert.False(ledger.ControlOf(TestMaps.At("E7")).GrantsEffectTo(TestMaps.P0));
        Assert.Equal(EffectSnapshot.BaseDeployLimitFor(1), ledger.SnapshotFor(TestMaps.P0, board, roster, 0, 1).DeployLimit);
        Assert.DoesNotContain(TestMaps.At("E7"), snapshot.Of(TestMaps.P0).ExclusiveCells);
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
