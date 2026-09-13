using Siege.Core.Board;
using Siege.Core.Relics;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.RelicControlSpec;

/// <summary>规格：relic-control —— Requirement: 弃赛玩家的遗留棋子仍影响信物</summary>
public class 弃赛玩家的遗留棋子仍影响信物Tests
{
    private static readonly PlayerId D = RelicFixtures.P3;

    [Fact]
    public void 弃赛者棋子制造信物争议()
    {
        // 已弃赛的 D（F7）与参赛的 P0（D7）同时覆盖 E7 → 争议，P0 不获得效果。
        // 变异验证 M-C10：RecalculateCore 在算覆盖前把弃赛者的棋子过滤掉（用 roster 过滤后的副本盘面）→ 红 2，含本测试。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Command()));
        board.Place("D7", TestMaps.P0).Place("F7", D);
        IReadOnlyDictionary<PlayerId, PlayerStatus> roster = RelicFixtures.Roster((TestMaps.P0, PlayerStatus.Active), (D, PlayerStatus.Resigned));

        ledger.Settle(board, 1, roster);

        Assert.Equal(RelicControl.Contested, ledger.ControlOf(TestMaps.At("E7")));
        Assert.Equal(3, ledger.SnapshotFor(TestMaps.P0, board, roster, 0, 1).DeployLimit);
    }

    [Fact]
    public void 弃赛者不消费信物效果()
    {
        // 裁决记录 5：D 的棋子唯一覆盖军令 E7 → 显示为「由已弃赛玩家控制（封锁）」，与无人控制、争议三态区分；
        // D 没有小回合（为其生成快照是接线错误）、不在先手修正输出中；先锋同理。
        // 变异验证 M-C11：Resolve 对非 Active 也返回 Controlled → 红 1（本测试）。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Command()), ("B2", RelicFixtures.Vanguard()));
        board.Place("F7", D).Place("B3", D).Place("H8", TestMaps.P0);
        IReadOnlyDictionary<PlayerId, PlayerStatus> roster = RelicFixtures.Roster((TestMaps.P0, PlayerStatus.Active), (D, PlayerStatus.Resigned));

        ledger.Settle(board, 1, roster);

        RelicControl control = ledger.ControlOf(TestMaps.At("E7"));
        Assert.Equal(new RelicControl(RelicControlKind.Blocked, D), control);
        Assert.NotEqual(RelicControlKind.Uncontrolled, control.Kind);
        Assert.NotEqual(RelicControlKind.Contested, control.Kind);
        Assert.False(control.GrantsEffectTo(D));
        Assert.Throws<SiegeRuleException>(() => ledger.SnapshotFor(D, board, roster, 0, 1));
        Assert.Equal(new Dictionary<PlayerId, int> { [TestMaps.P0] = 0 }, ledger.ReadInitiativeBonuses(board, roster));

        // 已出局者同样是封锁
        IReadOnlyDictionary<PlayerId, PlayerStatus> eliminated = RelicFixtures.Roster((TestMaps.P0, PlayerStatus.Active), (D, PlayerStatus.Eliminated));
        ledger.RecalculateControl(board, eliminated);
        Assert.Equal(RelicControlKind.Blocked, ledger.ControlOf(TestMaps.At("E7")).Kind);
    }
}
