using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Recruit;
using Siege.Core.Relics;

namespace Siege.Core.Tests.HandManagement;

/// <summary>规格：hand-management —— Requirement: 手牌类型槽</summary>
public class 手牌类型槽Tests
{
    [Fact]
    public void 同类无限叠加()
    {
        // 设计文档 §5.2：14 枚普通子只占 1 个槽位；满槽（4 槽、4 种）时仍可继续叠加普通子，系统不因数量拒绝。
        // 变异验证 M-H1/H2：RejectReason 改为按手牌总数与槽位比较（类型槽当容量）→ 红 17，含本测试；M-R15（满槽一律拒）→ 红 2，含本测试。
        GameSeed seed = HandFixtures.SeedWhere(p => p.CountOf(PieceType.Basic) >= 2);
        HandLedger ledger = HandFixtures.Ledger(seed);
        ledger.Debug.SeedHand(HandFixtures.P0, (PieceType.Basic, 12), (PieceType.Fortress, 1), (PieceType.Line, 1), (PieceType.Multiplier, 1));
        PlayerHandAccess access = HandFixtures.Begin(ledger, HandFixtures.P0, slots: 4);
        RecruitPanelView panel = access.EnterRecruit();

        foreach (int index in panel.IndicesOf(PieceType.Basic).Take(2))
        {
            access.Pick(index);
        }

        HandPrivateView hand = access.PrivateView();
        Assert.Equal(14, hand.CountOf(PieceType.Basic));
        Assert.Equal(4, hand.OccupiedSlots);
        Assert.Equal(0, hand.Overflow);
    }

    [Fact]
    public void 兵站扩展槽位()
    {
        // 设计文档 §8.1：控制 2 枚普通兵站信物 → 快照类型槽 5 + 1 + 1 = 7；手牌层原样消费该快照。
        // 变异验证 M-H3：PrivateViewOf 把 TypeSlots 钉死为 EffectSnapshot.BaseTypeSlots → 红 2（本测试 + 兵站丢失触发强制弃牌）。
        (GameBoard board, RelicLedger relics) = RelicFixtures.Scene(("E7", RelicFixtures.Depot()), ("C3", RelicFixtures.Depot()));
        board.Place("E6", TestMaps.P0).Place("C2", TestMaps.P0);
        relics.Settle(board, 1);
        HandLedger ledger = HandFixtures.Ledger();
        EffectSnapshot snapshot = relics.SnapshotFor(TestMaps.P0, board, ledger.HeldTypeCount(TestMaps.P0), 1);
        Assert.Equal(7, snapshot.TypeSlots);

        ledger.BeginTurn(TestMaps.P0, snapshot);
        PlayerHandAccess access = ledger.AccessFor(TestMaps.P0);

        Assert.Equal(7, access.PrivateView().TypeSlots);
        Assert.Equal(7, access.EnterRecruit().TypeSlots);
    }

    [Fact]
    public void 槽位统计种类而非总数()
    {
        // 设计文档 §5.2：普通子×9 + 堡垒子×1 → 已占 2 槽、余 3 空槽（默认 5 槽）。
        HandLedger ledger = HandFixtures.Ledger();
        ledger.Debug.SeedHand(HandFixtures.P0, (PieceType.Basic, 9), (PieceType.Fortress, 1));
        PlayerHandAccess access = HandFixtures.Begin(ledger, HandFixtures.P0);

        HandPrivateView hand = access.PrivateView();
        Assert.Equal(10, hand.TotalCount);
        Assert.Equal(2, hand.OccupiedSlots);
        Assert.Equal(5, hand.TypeSlots);
        Assert.Equal(3, hand.TypeSlots - hand.OccupiedSlots);
        Assert.Equal(2, ledger.HeldTypeCount(HandFixtures.P0));
    }

    [Fact]
    public void 槽位由类型数派生不存在泄漏()
    {
        // design.md D2：任意操作序列后已占槽位始终等于数量 > 0 的类型数。跑一段脚本，每一步都核对 OccupiedSlots == Types.Count() == 公开类型数。
        // 变异验证 M-H4：SetEntry 在归零时保留 (0,0) 条目 → 红 7，含本测试、清零释放槽位、撤销释放槽位、手牌清空。
        HandLedger ledger = HandFixtures.Ledger();
        for (int round = 1; round <= 20; round++)
        {
            PlayerHandAccess access = HandFixtures.Begin(ledger, HandFixtures.P0, slots: 3, round: round);
            HandPrivateView hand = access.PrivateView();
            if (hand.Overflow > 0)
            {
                access.Discard(hand.Types.Last());
            }

            RecruitPanelView panel = access.EnterRecruit();
            foreach (RecruitCandidateView c in panel.Candidates)
            {
                if (access.Panel().Candidates[c.Index].IsSelectable)
                {
                    access.Pick(c.Index);
                }
            }

            Check(ledger);
            if (round % 2 == 0)
            {
                ledger.OnPass(HandFixtures.P0);
            }
            else
            {
                HandPrivateView h = access.PrivateView();
                ledger.DeductHand(HandFixtures.P0, HandFixtures.Deployed((h.Types.First(), h.CountOf(h.Types.First()))));
            }

            Check(ledger);
            ledger.EndTurn(HandFixtures.P0);
            Check(ledger);
        }

        static void Check(HandLedger ledger)
        {
            HandPrivateView hand = ledger.Debug.PrivateViewOf(HandFixtures.P0);
            Assert.Equal(hand.Types.Count(), hand.OccupiedSlots);
            Assert.Equal(hand.OccupiedSlots, ledger.PublicView(HandFixtures.P0).Types.Count);
            Assert.All(hand.Entries.Values, e => Assert.True(e.Total > 0));
            Assert.Equal(hand.OccupiedSlots, ledger.HeldTypeCount(HandFixtures.P0));
        }
    }
}
