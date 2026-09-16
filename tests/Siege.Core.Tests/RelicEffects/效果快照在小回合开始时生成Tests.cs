using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Relics;

namespace Siege.Core.Tests.RelicEffectsSpec;

/// <summary>规格：relic-effects —— Requirement: 效果快照在小回合开始时生成</summary>
public class 效果快照在小回合开始时生成Tests
{
    [Fact]
    public void 分阶段基础值只在生成快照时读取一次()
    {
        // growth-pass-1 D3 / 裁决 2：基础部署上限按"生成快照时的当前大回合"读一次，小回合内不变、跨大回合不追溯。
        // 流程上无法在小回合内跨过大回合边界：大回合只在全员行动完后推进，Debug.SetMajorRound 也要求 Idle（小回合边界）。
        // 所以用快照对象本身证明：第 3 大回合最后一位玩家的快照（基础 3）在大回合推进到 4、为下一位生成基础 4 的快照之后，仍持有 3 / 第 3 大回合；
        // 本小回合的批次上下文取的也是这份快照的值。
        // 变异验证 M-GP7（快照重读大回合：BuildSnapshot 写静态 EffectSnapshot.LastRound，DeployLimit 改为 `BaseDeployLimitFor(LastRound) + 军令加成` 现算）
        // → 全套红 14，含本测试（round3 读成 4）；该变异引入进程级静态状态，并行测试互相污染，红数不稳定（Sim 批量跑局等也会红），以"本测试红"为准。
        MatchFlow match = MatchFixtures.Started(options: MatchFixtures.DominanceOff).AtRound(3, MatchFixtures.All);
        match.PassTurn();
        match.PassTurn();
        match.PassTurn();
        Assert.Equal((3, MatchFixtures.P3), (match.MajorRound, match.CurrentPlayer!.Value));

        match.BeginTurn();
        EffectSnapshot round3 = match.CurrentSnapshot!;
        Assert.Equal((3, 3), (round3.MajorRound, round3.DeployLimit));
        match.EnterRecruit();
        StagedBatch batch = match.EnterDeploy();
        Assert.Equal(3, batch.Context.DeployLimit);
        Assert.Null(batch.Stage(TestMaps.At("H8"), PieceType.Basic));
        Assert.True(match.Confirm().Confirmed);

        Assert.Equal(4, match.MajorRound);
        match.BeginTurn();
        EffectSnapshot round4 = match.CurrentSnapshot!;
        Assert.Equal((4, 4), (round4.MajorRound, round4.DeployLimit));
        Assert.Equal((3, 3), (round3.MajorRound, round3.DeployLimit));

        // 账本层：同一账本先后为第 3、第 4 大回合生成快照，先生成的那份不被后者改写
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Command()));
        board.Place("A1", TestMaps.P0);
        ledger.Settle(board, 3);
        EffectSnapshot early = ledger.SnapshotFor(TestMaps.P0, board, 0, 3);
        ledger.Settle(board, 7);
        EffectSnapshot late = ledger.SnapshotFor(TestMaps.P0, board, 0, 7);
        Assert.Equal((3, 5), (early.DeployLimit, late.DeployLimit));
    }

    [Fact]
    public void 快照读取开始时的名次()
    {
        // 规格 Scenario「快照读取开始时的名次」（catch-up-recruit 裁决 2）：小回合开始时为最后一名 → 快照的展示数与选取数含补偿；
        // 本小回合内名次上升也不回收。与「新占信物本回合不生效」同构：快照是不可变值对象，生成后不再回读任何活状态。
        // 势力独立复算（四邻接）：P0 A1-D1 → 4 + 5 = 9；P3 J9 → 1 + 2 = 3。
        // 变异验证 M-CU4：把 MatchFlow.CurrentSnapshot 改成回读账本与此刻名次的活视图 → 红 4，含本用例。
        MatchFlow match = MatchFixtures.Started(options: MatchFixtures.DominanceOff)
            .AtRound(5, [MatchFixtures.P3, MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2])
            .Stones(MatchFixtures.P0, "A1", "B1", "C1", "D1")
            .Stones(MatchFixtures.P1, "G1", "H1", "J1")
            .Stones(MatchFixtures.P2, "A9", "B9")
            .Stones(MatchFixtures.P3, "J9");
        Assert.Equal(4, match.Scoreboard.Latest!.RankOf(MatchFixtures.P3));

        match.BeginTurn();
        EffectSnapshot snapshot = match.CurrentSnapshot!;
        Assert.Equal((6, 4), (snapshot.RevealCount, snapshot.FreePickCount));

        match.Stones(MatchFixtures.P3, "D4", "E4", "F4", "D5", "E5", "F5", "D6", "E6", "F6");
        Assert.Equal(1, match.Scoreboard.Latest!.RankOf(MatchFixtures.P3));
        Assert.Equal((6, 4), (snapshot.RevealCount, snapshot.FreePickCount));
        Assert.Equal((6, 4), (match.CurrentSnapshot!.RevealCount, match.CurrentSnapshot!.FreePickCount));
        Assert.Equal((6, 4), (match.EnterRecruit().ShowCount, match.CurrentHand().Panel().FreePickCount));
    }

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
