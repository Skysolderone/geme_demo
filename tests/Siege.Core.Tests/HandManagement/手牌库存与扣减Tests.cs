using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Recruit;

namespace Siege.Core.Tests.HandManagement;

/// <summary>规格：hand-management —— Requirement: 手牌库存与扣减（走真实结算驱动器，扣减来自第 1 步回调）</summary>
public class 手牌库存与扣减Tests
{
    private static (HandLedger Ledger, SettlementDriver Driver, GameBoard Board) Scene(params (PieceType Type, int Count)[] hand)
    {
        HandLedger ledger = HandFixtures.Ledger();
        ledger.Debug.SeedHand(HandFixtures.P0, hand);
        GameBoard board = TestMaps.Blank(size: 7);
        var driver = new SettlementDriver(board, new BoardHistory(), new HandHooks(ledger));
        return (ledger, driver, board);
    }

    private static BatchContext Context(GameBoard board, HandLedger ledger, int limit = 3) =>
        BatchFixtures.Context(board, HandFixtures.P0, limit,
            stock: ledger.Debug.PrivateViewOf(HandFixtures.P0).Entries.ToDictionary(kv => kv.Key, kv => kv.Value.Total));

    [Fact]
    public void 部署扣减库存()
    {
        // 设计文档 §6.3 第 1 步：连珠子×3，确认批次部署 2 枚 → 结算后库存 1，槽位仍被占用。
        // 变异验证 M-H10：DeductHand 的扣减循环改为每类只扣 1 枚 → 红 3（本测试 + 暂放不扣减 + 手牌清空）。
        (HandLedger ledger, SettlementDriver driver, GameBoard board) = Scene((PieceType.Basic, 5), (PieceType.Line, 3));
        PlayerHandAccess access = HandFixtures.Begin(ledger, HandFixtures.P0);
        access.EnterRecruit();

        SettlementOutcome outcome = driver.Confirm(Context(board, ledger), [BatchFixtures.P("A1", PieceType.Line), BatchFixtures.P("C3", PieceType.Line)]);

        Assert.True(outcome.Confirmed);
        HandPrivateView hand = access.PrivateView();
        Assert.Equal(1, hand.CountOf(PieceType.Line));
        Assert.Equal(2, hand.OccupiedSlots);
        Assert.Contains(PieceType.Line, ledger.PublicView(HandFixtures.P0).Types);
        Assert.Equal(TurnPhase.Settled, ledger.PhaseOf(HandFixtures.P0));
    }

    [Fact]
    public void 清零释放槽位()
    {
        // 设计文档 §5.2：协同子×1 全部部署 → 类型移除、槽位释放。
        (HandLedger ledger, SettlementDriver driver, GameBoard board) = Scene((PieceType.Basic, 5), (PieceType.Synergy, 1));
        PlayerHandAccess access = HandFixtures.Begin(ledger, HandFixtures.P0);
        access.EnterRecruit();

        Assert.True(driver.Confirm(Context(board, ledger), [BatchFixtures.P("D4", PieceType.Synergy)]).Confirmed);

        HandPrivateView hand = access.PrivateView();
        Assert.Equal(0, hand.CountOf(PieceType.Synergy));
        Assert.DoesNotContain(PieceType.Synergy, hand.Types);
        Assert.Equal(1, hand.OccupiedSlots);
        Assert.Equal([PieceType.Basic], ledger.PublicView(HandFixtures.P0).Types);
    }

    [Fact]
    public void 暂放不扣减()
    {
        // 设计文档 §5.4：暂放 3 枚未确认 → 手牌库存不变；只有确认批次触发第 1 步扣减。
        // 契约测试：暂放在批次层（StagedBatch）完成，不经过任何手牌回调；能让它红的变异是"StagedBatch.Stage 调 DeductHand"，属批次层，不做变异记录。
        (HandLedger ledger, SettlementDriver driver, GameBoard board) = Scene((PieceType.Basic, 5), (PieceType.Fortress, 2));
        PlayerHandAccess access = HandFixtures.Begin(ledger, HandFixtures.P0);
        access.EnterRecruit();
        HandPrivateView before = access.PrivateView();

        var batch = new StagedBatch(board, Context(board, ledger));
        Assert.Null(batch.Stage(TestMaps.At("A1"), PieceType.Basic));
        Assert.Null(batch.Stage(TestMaps.At("B2"), PieceType.Fortress));
        Assert.Null(batch.Stage(TestMaps.At("C3"), PieceType.Fortress));
        Assert.Equal(3, batch.Count);

        Assert.Equal(before, access.PrivateView());
        Assert.Equal(7, access.PrivateView().TotalCount);
        Assert.Equal(TurnPhase.Recruit, ledger.PhaseOf(HandFixtures.P0));

        Assert.True(driver.Confirm(Context(board, ledger), [.. batch.Placements]).Confirmed);
        Assert.Equal(4, access.PrivateView().TotalCount);
        Assert.Equal(0, access.PrivateView().CountOf(PieceType.Fortress));
    }

    [Fact]
    public void 库存不足的扣减响亮失败()
    {
        // 批次层已按库存预演，扣减仍超库存只可能是接线错误 → 抛出而不是静默截到 0。
        // 变异验证 M-H11：DeductHand 去掉 `count > stock` 检查、改为 Math.Max(0, …) → 红 1（本测试）。
        HandLedger ledger = HandFixtures.Ledger();
        HandFixtures.Begin(ledger, HandFixtures.P0).EnterRecruit();

        Assert.Throws<SiegeRuleException>(() => ledger.DeductHand(HandFixtures.P0, HandFixtures.Deployed((PieceType.Basic, 6))));
        Assert.Throws<SiegeRuleException>(() => ledger.DeductHand(HandFixtures.P0, HandFixtures.Deployed((PieceType.Line, 1))));
        Assert.Throws<SiegeRuleException>(() => ledger.DeductHand(HandFixtures.P0, HandFixtures.Deployed()));
        Assert.Equal(5, ledger.Debug.PrivateViewOf(HandFixtures.P0).CountOf(PieceType.Basic));
    }
}
