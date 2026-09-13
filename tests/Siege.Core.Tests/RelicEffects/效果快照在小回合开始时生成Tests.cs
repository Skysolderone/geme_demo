using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Relics;

namespace Siege.Core.Tests.RelicEffectsSpec;

/// <summary>规格：relic-effects —— Requirement: 效果快照在小回合开始时生成</summary>
public class 效果快照在小回合开始时生成Tests
{
    [Fact]
    public void 新占信物本回合不生效()
    {
        // 设计文档 §5.1：小回合开始时生成快照（部署上限 3）；本小回合的批次占领军令 E7（走真实结算驱动器）→ 已生成的快照仍为 3；下一小回合的快照为 4。
        // 变异验证 M-E6：EffectSnapshot.DeployLimit 改为持有 Func<int> 回读账本（活视图）→ 红 3，含本测试；
        // M-E7：SnapshotFor 不重算控制、直接读上次结算的 Control → 「先手玩家夺走后手信物」红（本测试不红，因为结算钩子已重算）。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Command()));
        var driver = new SettlementDriver(board, new BoardHistory(), new RelicHooks(ledger) { MajorRound = 1 });

        EffectSnapshot before = ledger.SnapshotFor(TestMaps.P0, board, 0, 1);
        Assert.Equal(3, before.DeployLimit);

        Assert.True(driver.Confirm(BatchFixtures.Context(board, TestMaps.P0, before.DeployLimit), [BatchFixtures.P("E7")]).Confirmed);

        Assert.Equal(new RelicControl(RelicControlKind.Controlled, TestMaps.P0), ledger.ControlOf(TestMaps.At("E7")));
        Assert.Equal(3, before.DeployLimit);
        Assert.Equal(4, ledger.SnapshotFor(TestMaps.P0, board, 0, 2).DeployLimit);
    }

    [Fact]
    public void 先手玩家夺走后手信物()
    {
        // 设计文档 §5.1：后手 P1 上一大回合控制探勘 E7（E8 唯一覆盖）。本大回合先手 P0 先行动：落 E6 使 E7 争议。
        // 轮到 P1 时生成快照 → 不含该探勘的展示数加成（5 而非 6）。
        // 这里刻意不在 P0 行动后调用 RecalculateControl，以验证 SnapshotFor 读的是「此刻盘面」而不是上次结算的缓存（M-E7 让本测试红）。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Prospecting()));
        board.Place("E8", TestMaps.P1);
        ledger.Settle(board, 4);
        Assert.Equal(6, ledger.SnapshotFor(TestMaps.P1, board, 0, 4).RevealCount);

        board.Place("E6", TestMaps.P0);

        Assert.Equal(5, ledger.SnapshotFor(TestMaps.P1, board, 0, 5).RevealCount);
        Assert.Equal(5, ledger.SnapshotFor(TestMaps.P0, board, 0, 5).RevealCount);
    }

    [Fact]
    public void 快照期间丢失不影响本回合()
    {
        // P0 小回合开始时控制征召 E7（E6 唯一覆盖）→ 快照免费选取数 4；本回合结算中 P1 落 E8 使其争议 → 本回合快照仍 4；下一小回合恢复为 3。
        // 变异验证 M-E6（活视图）→ 本测试红。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Conscription()));
        board.Place("E6", TestMaps.P0);
        ledger.Settle(board, 1);

        EffectSnapshot snapshot = ledger.SnapshotFor(TestMaps.P0, board, 0, 1);
        Assert.Equal(4, snapshot.FreePickCount);

        board.Place("E8", TestMaps.P1);
        ledger.Settle(board, 1);
        Assert.Equal(RelicControl.Contested, ledger.ControlOf(TestMaps.At("E7")));

        Assert.Equal(4, snapshot.FreePickCount);
        Assert.Equal(3, ledger.SnapshotFor(TestMaps.P0, board, 0, 2).FreePickCount);
    }

    [Fact]
    public void 快照是不可变值对象()
    {
        // design.md D5：快照全部属性只读，且不持有账本或盘面引用；两次读取同一快照的值恒等。
        // 变异验证 M-E6（活视图）→ 本测试红（出现可写或委托类型的属性）。
        foreach (System.Reflection.PropertyInfo p in typeof(EffectSnapshot).GetProperties())
        {
            Assert.False(p.CanWrite, $"EffectSnapshot.{p.Name} 可写");
            Assert.False(typeof(Delegate).IsAssignableFrom(p.PropertyType), $"EffectSnapshot.{p.Name} 是委托");
            Assert.NotEqual(typeof(RelicLedger), p.PropertyType);
            Assert.NotEqual(typeof(GameBoard), p.PropertyType);
        }

        Assert.All(typeof(EffectSnapshot).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public),
            f => Assert.True(f.IsInitOnly, $"字段 {f.Name} 非只读"));
    }
}
