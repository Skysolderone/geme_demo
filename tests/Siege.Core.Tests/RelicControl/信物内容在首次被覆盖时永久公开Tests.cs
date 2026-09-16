using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Relics;

namespace Siege.Core.Tests.RelicControlSpec;

/// <summary>规格：relic-control —— Requirement: 信物内容在首次被覆盖时永久公开</summary>
public class 信物内容在首次被覆盖时永久公开Tests
{
    [Fact]
    public void 首次覆盖即揭示()
    {
        // 玩家 A 的棋子（E6）首次覆盖未知信物格 E7 → 类型与强度对全部玩家公开，揭示事件记下所在大回合。
        // 变异验证 M-C1：Reveal 把 `Kind == Neutral` 的 continue 改为 `Kind != Exclusive`（只在独占时揭示）→ 红 2（本测试不红，「争议不阻止揭示」红）；
        // M-C2：Reveal 不调用 MarkRevealed → 红 6。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Command(2)));
        Assert.False(ledger.IsRevealed(TestMaps.At("E7")));
        Assert.Null(ledger.PublicStateOf(TestMaps.At("E7")).Content);

        board.Place("E6", TestMaps.P0);
        RelicRevealEvent evt = Assert.Single(ledger.Settle(board, majorRound: 3));

        Assert.True(ledger.IsRevealed(TestMaps.At("E7")));
        RelicPublicState state = ledger.PublicStateOf(TestMaps.At("E7"));
        Assert.Equal(RelicFixtures.Command(2), state.Content);
        Assert.Equal(3, state.RevealedInMajorRound);
        Assert.Equal(new RelicRevealEvent(TestMaps.At("E7"), RelicFixtures.Command(2), 3), evt);
        Assert.Equal([evt], ledger.RevealEvents);
        // 公开状态不区分「谁看」——没有按玩家过滤的入口
        Assert.All(ledger.PublicStates(), s => Assert.Equal(s.IsRevealed, s.Content is not null));
    }

    [Fact]
    public void 直接占据也揭示()
    {
        // 「进入覆盖范围」含被直接占据：P0 把棋子落在未知信物格 E7 上、四周无任何己方棋子 → 揭示。
        // 占据即控制（§7.3），控制而不揭示会让公开面板（§14.3）出现来源不明的部署上限加成，与 §13.1「已发现信物的类型、强度和当前控制者」矛盾。
        // 变异验证 K-5（trellis-check）：Reveal 对 Occupied 也 continue → 红 1（本测试）。此前无测试守这条。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Command()));
        board.Place("E7", TestMaps.P0);

        Assert.Single(ledger.Settle(board, majorRound: 1));

        Assert.True(ledger.IsRevealed(TestMaps.At("E7")));
        Assert.Equal(new RelicControl(RelicControlKind.Controlled, TestMaps.P0), ledger.ControlOf(TestMaps.At("E7")));
    }

    [Fact]
    public void 占据即揭示()
    {
        // 林地格不接收覆盖：P0 的敌邻子（E6）先与林地信物格 E7 相邻 → 无覆盖、不揭示；
        // P0 落子占据 E7 → 类型与强度公开，且由 P0 控制。这是林地上的信物唯一的揭示途径。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(
            TestMaps.Terrain(surfaces: [("E7", Surface.Forest)]),
            ("E7", RelicFixtures.Depot(2)));
        board.Place("E6", TestMaps.P0);
        Assert.Empty(ledger.Settle(board, majorRound: 1));
        Assert.False(ledger.IsRevealed(TestMaps.At("E7")));
        Assert.Equal(RelicControl.Uncontrolled, ledger.ControlOf(TestMaps.At("E7")));

        board.Place("E7", TestMaps.P0);
        RelicRevealEvent evt = Assert.Single(ledger.Settle(board, majorRound: 2));

        Assert.Equal(new RelicRevealEvent(TestMaps.At("E7"), RelicFixtures.Depot(2), 2), evt);
        Assert.Equal(RelicFixtures.Depot(2), ledger.PublicStateOf(TestMaps.At("E7")).Content);
        Assert.Equal(new RelicControl(RelicControlKind.Controlled, TestMaps.P0), ledger.ControlOf(TestMaps.At("E7")));
    }

    [Fact]
    public void 争议不阻止揭示()
    {
        // 结算后 E7 同时被 P0（E6）与 P1（E8）覆盖 → 内容仍揭示，且信物为争议状态、不向任何人提供效果。
        // 变异验证 M-C1（只在独占时揭示）→ 本测试红。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Prospecting()));
        board.Place("E6", TestMaps.P0).Place("E8", TestMaps.P1);

        Assert.Single(ledger.Settle(board, majorRound: 1));

        Assert.True(ledger.IsRevealed(TestMaps.At("E7")));
        Assert.Equal(RelicControl.Contested, ledger.ControlOf(TestMaps.At("E7")));
        Assert.Equal(EffectSnapshot.BaseRevealCount, ledger.SnapshotFor(TestMaps.P0, board, 0, 1).RevealCount);
        Assert.Equal(EffectSnapshot.BaseRevealCount, ledger.SnapshotFor(TestMaps.P1, board, 0, 1).RevealCount);
    }

    [Fact]
    public void 揭示不可撤销()
    {
        // 揭示者 P0（E6）随后被 P1 围杀，E7 不再被任何玩家覆盖 → 内容仍公开，揭示轮次不变，且不再产生第二次揭示事件。
        // 变异验证 M-C3：Reveal 对 Neutral 的信物执行 `IsRevealed = false`（揭示随覆盖消失而回退）→ 红 1（本测试）。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Depot()));
        board.Place("E6", TestMaps.P0);
        ledger.Settle(board, majorRound: 1);
        Assert.True(ledger.IsRevealed(TestMaps.At("E7")));

        // P1 围杀 E6：D6/F6/E5 三面 + E7 是信物空格，P1 不落 E7，改由提子逻辑外的最小写入模拟：直接清除 E6，四周无人。
        board.Clear(TestMaps.At("E6"));
        ledger.Settle(board, majorRound: 2);

        Assert.Equal(RelicControl.Uncontrolled, ledger.ControlOf(TestMaps.At("E7")));
        RelicPublicState state = ledger.PublicStateOf(TestMaps.At("E7"));
        Assert.True(state.IsRevealed);
        Assert.Equal(RelicFixtures.Depot(), state.Content);
        Assert.Equal(1, state.RevealedInMajorRound);
        Assert.Single(ledger.RevealEvents);
    }

    [Fact]
    public void 提子先于揭示()
    {
        // 走真实的结算驱动器：P0 落 E6 既提走 P1 的 E5（D5/F5/E4 已围三面）又使己方覆盖信物格 E7。
        // 揭示本身只看「是否有覆盖」，提子只会减少覆盖，所以揭示与否在本层无法区分先后；能观察到的是：
        // 第 4 步回调拿到的盘面 MUST 已完成提子（E5 为空、Captures 含 E5），账本据此揭示并由第 5 步基于提子后的覆盖关系判定控制。
        // 变异验证 M-C4：SettlementDriver 把 OnRevealRelics 移到 RemoveStones 之前 → 本测试红（钩子内 E5 仍有 P1 棋子）。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Command()));
        board.Place("E5", TestMaps.P1)
             .Place("D5", TestMaps.P0).Place("F5", TestMaps.P0).Place("E4", TestMaps.P0);
        bool e5EmptyAtReveal = false;
        int capturesAtReveal = -1;
        var hooks = new RelicHooks(ledger)
        {
            MajorRound = 2,
            RevealProbe = ctx =>
            {
                e5EmptyAtReveal = ctx.Board[TestMaps.At("E5")].Occupant is null;
                capturesAtReveal = ctx.Captures.Length;
            },
        };
        var driver = new SettlementDriver(board, new BoardHistory(), hooks);

        SettlementOutcome outcome = driver.Confirm(BatchFixtures.Context(board, TestMaps.P0), [BatchFixtures.P("E6")]);

        Assert.True(outcome.Confirmed);
        Assert.Equal(["DeductHand", "OnRevealRelics", "OnRecalculatePower", "OnCheckEndConditions"], hooks.Steps);
        Assert.True(e5EmptyAtReveal, "第 4 步回调时 E5 仍有棋子：揭示发生在提子之前");
        Assert.Equal(1, capturesAtReveal);
        Assert.Null(board[TestMaps.At("E5")].Occupant);
        RelicRevealEvent evt = Assert.Single(hooks.Revealed);
        Assert.Equal((TestMaps.At("E7"), 2), (evt.Coord, evt.MajorRound));
        Assert.Equal(new RelicControl(RelicControlKind.Controlled, TestMaps.P0), ledger.ControlOf(TestMaps.At("E7")));
    }
}
