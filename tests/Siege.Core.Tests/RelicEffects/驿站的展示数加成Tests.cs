using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Preview;
using Siege.Core.Relics;

namespace Siege.Core.Tests.RelicEffectsSpec;

/// <summary>
/// 规格：more-pieces-relics relic-effects —— Requirement: 驿站的展示数加成（D4）。
/// 每枚受控驿站令展示数增加「快照时受控信物总枚数 − 1」：含其他驿站与先锋，每枚按 1 计，争议 / 无人控制不计；与探勘直接相加，来源逐枚列出。
/// 用例一律让玩家 P0 直接占据信物格（占据即控制），经 <see cref="RelicLedger.SnapshotFor(PlayerId, GameBoard, int, int)"/> 读快照。
/// </summary>
public class 驿站的展示数加成Tests
{
    [Fact]
    public void 驿站计入其他受控信物()
    {
        // 规格 Scenario：控制 1 枚驿站、1 枚军令、1 枚先锋 → 展示数 5 + 2 = 7（先锋只作计数，其先手修正仍不进快照）。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(
            ("C3", RelicFixtures.Relay()), ("E5", RelicFixtures.Command()), ("G7", RelicFixtures.Vanguard()));
        board.Place("C3", TestMaps.P0).Place("E5", TestMaps.P0).Place("G7", TestMaps.P0);
        ledger.Settle(board, 1);

        EffectSnapshot snapshot = ledger.SnapshotFor(TestMaps.P0, board, 0, 1);

        Assert.Equal(7, snapshot.RevealCount);
        Assert.Equal(4, snapshot.DeployLimit);
        Assert.Equal([(TestMaps.At("C3"), 2)], snapshot.RelaySources.Select(kv => (kv.Key, kv.Value)));
        Assert.Equal(2, snapshot.RelayBonus);
    }

    [Fact]
    public void 只有驿站本身()
    {
        // 规格 Scenario：只控制 1 枚驿站 → 展示数 5（驿站不计自己）。来源里仍逐枚列出这枚驿站，加成 0。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("C3", RelicFixtures.Relay()));
        board.Place("C3", TestMaps.P0);
        ledger.Settle(board, 1);

        EffectSnapshot snapshot = ledger.SnapshotFor(TestMaps.P0, board, 0, 1);

        Assert.Equal(5, snapshot.RevealCount);
        Assert.Equal([(TestMaps.At("C3"), 0)], snapshot.RelaySources.Select(kv => (kv.Key, kv.Value)));
    }

    [Fact]
    public void 高阶信物按1枚计()
    {
        // 规格 Scenario：控制 1 枚驿站与 1 枚效果 +2 的兵站 → 驿站加成 1、展示数 6、手牌类型槽 7。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("C3", RelicFixtures.Relay()), ("E5", RelicFixtures.Depot(2)));
        board.Place("C3", TestMaps.P0).Place("E5", TestMaps.P0);
        ledger.Settle(board, 1);

        EffectSnapshot snapshot = ledger.SnapshotFor(TestMaps.P0, board, 0, 1);

        Assert.Equal(1, snapshot.RelayBonus);
        Assert.Equal(6, snapshot.RevealCount);
        Assert.Equal(7, snapshot.TypeSlots);
    }

    [Fact]
    public void 两枚驿站互相计入()
    {
        // 规格 Scenario：控制 2 枚驿站与 1 枚军令 → 每枚驿站各计 2 枚其他信物，展示数 5 + 2 + 2 = 9（k 枚驿站 + m 枚其他 → k × (k − 1 + m)）。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(
            ("C3", RelicFixtures.Relay()), ("E5", RelicFixtures.Relay()), ("G7", RelicFixtures.Command()));
        board.Place("C3", TestMaps.P0).Place("E5", TestMaps.P0).Place("G7", TestMaps.P0);
        ledger.Settle(board, 1);

        EffectSnapshot snapshot = ledger.SnapshotFor(TestMaps.P0, board, 0, 1);

        Assert.Equal(9, snapshot.RevealCount);
        Assert.Equal([(TestMaps.At("C3"), 2), (TestMaps.At("E5"), 2)], snapshot.RelaySources.Select(kv => (kv.Key, kv.Value)));
    }

    [Fact]
    public void 争议信物不计()
    {
        // 规格 Scenario：控制 1 枚驿站，另一枚探勘 E5 被 P0（E4）与 P1（E6）同时覆盖、处于争议状态 → 展示数 5。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("C3", RelicFixtures.Relay()), ("E5", RelicFixtures.Prospecting()));
        board.Place("C3", TestMaps.P0).Place("E4", TestMaps.P0).Place("E6", TestMaps.P1);
        ledger.Settle(board, 1);
        Assert.Equal(RelicControl.Contested, ledger.ControlOf(TestMaps.At("E5")));   // 前提

        EffectSnapshot snapshot = ledger.SnapshotFor(TestMaps.P0, board, 0, 1);

        Assert.Equal(5, snapshot.RevealCount);
        Assert.Equal(0, snapshot.RelayBonus);
    }

    [Fact]
    public void 与探勘相加()
    {
        // 规格 Scenario：控制 1 枚驿站与 1 枚探勘 → 展示数 5 + 1 + 1 = 7，来源拆分列出基础 5、探勘 +1、驿站 +1。
        // 快照一侧：驿站加成 1；公开结构参数一侧（MatchFlow.PublishSupplement）：P1 经正式结算占据 H4（驿站）与 J4（探勘），
        // 来源逐枚列出，且"基础 + 各来源 = 快照值"的一致性校验通过（驿站若不进来源，这里会抛）。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("C3", RelicFixtures.Relay()), ("E5", RelicFixtures.Prospecting()));
        board.Place("C3", TestMaps.P0).Place("E5", TestMaps.P0);
        ledger.Settle(board, 1);
        EffectSnapshot snapshot = ledger.SnapshotFor(TestMaps.P0, board, 0, 1);
        Assert.Equal(7, snapshot.RevealCount);
        Assert.Equal(1, snapshot.RelayBonus);

        MatchFlow match = MatchFixtures.Started(relics: [("H4", RelicFixtures.Relay()), ("J4", RelicFixtures.Prospecting())])
            .AtRound(7, MatchFixtures.All);
        match.PassTurn();
        match.PlayTurn("H4", "J4");

        StructureParameter reveal = match.PublishSupplement().Structures.Single(s => s.Player == MatchFixtures.P1).Parameters!.RevealCount;
        Assert.Equal((7, 5), (reveal.Value, reveal.Base));
        Assert.Equal(
            [(RelicType.Prospecting, 1, "J4"), (RelicType.Relay, 1, "H4")],
            reveal.Sources.Select(s => (s.Type, s.Magnitude, s.Coord.ToNotation())));
    }
}
