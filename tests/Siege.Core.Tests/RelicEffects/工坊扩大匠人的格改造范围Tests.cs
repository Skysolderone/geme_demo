using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Relics;

namespace Siege.Core.Tests.RelicEffectsSpec;

/// <summary>
/// 规格：more-pieces-relics relic-effects —— Requirement: 工坊扩大匠人的格改造范围（D5）。
/// 快照时控制至少一枚工坊 → 快照标记工坊生效；不叠加；本小回合新占领的下一小回合起生效。
/// 标记只经批次上下文（<see cref="BatchContext.WorkshopActive"/>）进入改造合法性的唯一实现。
/// </summary>
public class 工坊扩大匠人的格改造范围Tests
{
    [Fact]
    public void 控制工坊即标记生效()
    {
        // 规格 Scenario：小回合开始时控制 1 枚工坊 → 该小回合快照标记工坊生效；不控制工坊的玩家不标记。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("C3", RelicFixtures.Workshop()));
        board.Place("C3", TestMaps.P0).Place("G7", TestMaps.P1);
        ledger.Settle(board, 1);

        Assert.True(ledger.SnapshotFor(TestMaps.P0, board, 0, 1).WorkshopActive);
        Assert.False(ledger.SnapshotFor(TestMaps.P1, board, 0, 1).WorkshopActive);

        // 对局流程：小回合开始的快照标记工坊生效 → 进入部署时批次上下文带上这个标记 → 匠人可隔一格改造（F6 → 烧林 H6）。
        // 同一局面另一名不控制工坊的玩家，同样的暂放被拒。
        MatchFlow match = MatchFixtures.Started(
                TestMaps.Terrain(surfaces: [("H6", Surface.Forest), ("F4", Surface.Forest)]), relics: [("G4", RelicFixtures.Workshop())])
            .AtRound(7, MatchFixtures.All)
            .Stones(MatchFixtures.P0, "G4");
        match.Debug.SeedHand(MatchFixtures.P0, (PieceType.Artisan, 2));
        match.Debug.SeedHand(MatchFixtures.P1, (PieceType.Artisan, 2));

        match.BeginTurn();
        Assert.True(match.CurrentSnapshot!.WorkshopActive);
        match.EnterRecruit();
        StagedBatch batch = match.EnterDeploy();
        Assert.True(batch.Context.WorkshopActive);
        Assert.Null(batch.Stage(TestMaps.At("F6"), PieceType.Artisan, TerrainEdit.Burn(TestMaps.At("H6"))));
        batch.Unstage(TestMaps.At("F6"));
        Assert.True(match.Confirm().Confirmed);

        match.BeginTurn();
        Assert.Equal(MatchFixtures.P1, match.CurrentPlayer);
        Assert.False(match.CurrentSnapshot!.WorkshopActive);
        match.EnterRecruit();
        BatchFailure? failure = match.EnterDeploy().Stage(TestMaps.At("F6"), PieceType.Artisan, TerrainEdit.Burn(TestMaps.At("H6")));
        Assert.Equal(BatchFailureKind.TerrainEditIllegal, failure!.Kind);
    }

    [Fact]
    public void 多枚工坊不叠加()
    {
        // 规格 Scenario：控制 2 枚工坊 → 快照与只控制 1 枚时相同，匠人格目标仍只扩大一格（F6 落点：H6 可，J6 不可）。
        (GameBoard two, RelicLedger twoLedger) = RelicFixtures.Scene(("C3", RelicFixtures.Workshop()), ("C5", RelicFixtures.Workshop()));
        two.Place("C3", TestMaps.P0).Place("C5", TestMaps.P0);
        (GameBoard one, RelicLedger oneLedger) = RelicFixtures.Scene(("C3", RelicFixtures.Workshop()));
        one.Place("C3", TestMaps.P0);

        EffectSnapshot withTwo = twoLedger.SnapshotFor(TestMaps.P0, two, 0, 1);
        Assert.Equal(oneLedger.SnapshotFor(TestMaps.P0, one, 0, 1), withTwo);

        GameBoard forest = TestMaps.Blank(TestMaps.Terrain(surfaces: [("H6", Surface.Forest), ("J6", Surface.Forest)]), size: 11);
        var context = BatchFixtures.Context(forest, TestMaps.P0) with { WorkshopActive = withTwo.WorkshopActive };
        Assert.Null(BatchRehearsal.ValidateShape(forest, context, [BatchFixtures.Artisan("F6", TerrainEdit.Burn(TestMaps.At("H6")))]));
        Assert.Equal(
            BatchFailureKind.TerrainEditIllegal,
            BatchRehearsal.ValidateShape(forest, context, [BatchFixtures.Artisan("F6", TerrainEdit.Burn(TestMaps.At("J6")))])!.Kind);
    }

    [Fact]
    public void 新占工坊下一小回合生效()
    {
        // 规格 Scenario：玩家在本小回合的批次中首次占领一枚工坊 → 本小回合快照不标记工坊生效，其匠人本小回合不能隔一格改造；下一小回合起可以。
        // 走真实结算驱动器占领工坊 C3；已生成的快照不变（快照是不可变值对象），按它建立的上下文仍拒绝隔一格；下一次快照标记生效，隔一格合法。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(
            TestMaps.Terrain(surfaces: [("H6", Surface.DeepWater)]), ("C3", RelicFixtures.Workshop()));
        var driver = new SettlementDriver(board, new BoardHistory(), new RelicHooks(ledger) { MajorRound = 1 });

        EffectSnapshot before = ledger.SnapshotFor(TestMaps.P0, board, 0, 1);
        Assert.False(before.WorkshopActive);
        Assert.True(driver.Confirm(BatchFixtures.Context(board, TestMaps.P0) with { WorkshopActive = before.WorkshopActive }, [BatchFixtures.P("C3")]).Confirmed);
        Assert.Equal(new RelicControl(RelicControlKind.Controlled, TestMaps.P0), ledger.ControlOf(TestMaps.At("C3")));

        Assert.False(before.WorkshopActive);
        BatchFailure? sameTurn = BatchRehearsal.ValidateShape(
            board, BatchFixtures.Context(board, TestMaps.P0) with { WorkshopActive = before.WorkshopActive },
            [BatchFixtures.Artisan("F6", TerrainEdit.Bridge(TestMaps.At("H6")))]);
        Assert.Equal(BatchFailureKind.TerrainEditIllegal, sameTurn!.Kind);
        Assert.Contains("不是几何四邻", sameTurn.Message);

        EffectSnapshot next = ledger.SnapshotFor(TestMaps.P0, board, 0, 2);
        Assert.True(next.WorkshopActive);
        Assert.Null(BatchRehearsal.ValidateShape(
            board, BatchFixtures.Context(board, TestMaps.P0) with { WorkshopActive = next.WorkshopActive },
            [BatchFixtures.Artisan("F6", TerrainEdit.Bridge(TestMaps.At("H6")))]));
    }
}
