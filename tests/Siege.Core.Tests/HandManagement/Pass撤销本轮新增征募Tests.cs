using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Recruit;

namespace Siege.Core.Tests.HandManagement;

/// <summary>规格：hand-management —— Requirement: Pass 撤销本轮新增征募（ROADMAP 最高危陷阱之一：手牌只存总数而不分两段账）</summary>
public class Pass撤销本轮新增征募Tests
{
    [Fact]
    public void 同类型只扣回新增数量()
    {
        // 设计文档 §5.5 / 规范强制回归「手牌两段账」：进入小回合时普通子×4，本轮征募新获得普通子×2，随后 Pass → 4，MUST NOT 是 0 或 2。
        // 变异验证 M-H12：两段账合并成单一总数（OnPass 清整个条目）→ 结果 0 → 红 12，含本测试；
        // M-H13：OnPass 写成 new HandEntry(e.Gained, 0)（扣基数留新增）→ 结果 2 → 红 12，含本测试；
        // M-X：DeductHand 改为先扣基数 → 红 0——design.md D1 已论证两种扣法在合法流程中等价（扣完即折叠），不可观测，不做假测试；
        // M-H14：BeginTurn 不折叠 → 红 0——每条结算路径（DeductHand 折叠 / OnPass 清零）结束时新增已为 0，BeginTurn 的折叠在合法流程中是防御性的恒等操作。
        GameSeed seed = HandFixtures.SeedWhere(p => p.CountOf(PieceType.Basic) >= 2);
        HandLedger ledger = HandFixtures.Ledger(seed);
        ledger.Debug.SeedHand(HandFixtures.P0, (PieceType.Basic, 4));
        PlayerHandAccess access = HandFixtures.Begin(ledger, HandFixtures.P0);
        RecruitPanelView panel = access.EnterRecruit();
        foreach (int index in panel.IndicesOf(PieceType.Basic).Take(2))
        {
            access.Pick(index);
        }

        Assert.Equal(new HandEntry(4, 2), access.PrivateView().EntryOf(PieceType.Basic));
        Assert.Equal(6, access.PrivateView().CountOf(PieceType.Basic));

        ledger.OnPass(HandFixtures.P0);

        Assert.Equal(4, access.PrivateView().CountOf(PieceType.Basic));
        Assert.Equal(new HandEntry(4, 0), access.PrivateView().EntryOf(PieceType.Basic));
        ledger.EndTurn(HandFixtures.P0);
        Assert.Equal(2, ledger.Records[0].RevokedCount);
    }

    [Fact]
    public void 撤销全部本轮新增()
    {
        // 设计文档 §5.5：本轮征募普通子×1、堡垒子×2 后 Pass → 三枚全部撤销，手牌恢复为进入小回合时的状态。
        GameSeed seed = HandFixtures.SeedWhere(p => p.CountOf(PieceType.Basic) >= 1 && p.CountOf(PieceType.Fortress) >= 2);
        HandLedger ledger = HandFixtures.Ledger(seed);
        PlayerHandAccess access = HandFixtures.Begin(ledger, HandFixtures.P0);
        HandPrivateView entering = access.PrivateView();
        RecruitPanelView panel = access.EnterRecruit();
        access.Pick(panel.IndicesOf(PieceType.Basic)[0]);
        access.Pick(panel.IndicesOf(PieceType.Fortress)[0]);
        access.Pick(panel.IndicesOf(PieceType.Fortress)[1]);
        Assert.Equal(8, access.PrivateView().TotalCount);

        ledger.OnPass(HandFixtures.P0);

        HandPrivateView after = access.PrivateView();
        Assert.Equal(entering.Entries, after.Entries);
        Assert.Equal(5, after.TotalCount);
        Assert.Equal(0, after.PendingGained);
    }

    [Fact]
    public void 撤销释放槽位()
    {
        // 设计文档 §5.5：本轮首次获得倍增子×1（此前无该类型），Pass → 倍增子类型移除、槽位释放。
        // 变异验证 M-H4（SetEntry 归零不移除条目）→ 红 7，含本测试。
        GameSeed seed = HandFixtures.SeedWhere(p => p.Contains(PieceType.Multiplier));
        HandLedger ledger = HandFixtures.Ledger(seed);
        PlayerHandAccess access = HandFixtures.Begin(ledger, HandFixtures.P0);
        RecruitPanelView panel = access.EnterRecruit();
        access.Pick(panel.IndicesOf(PieceType.Multiplier)[0]);
        Assert.Equal(2, access.PrivateView().OccupiedSlots);
        Assert.Contains(PieceType.Multiplier, ledger.PublicView(HandFixtures.P0).Types);

        ledger.OnPass(HandFixtures.P0);

        HandPrivateView after = access.PrivateView();
        Assert.Equal(1, after.OccupiedSlots);
        Assert.DoesNotContain(PieceType.Multiplier, after.Types);
        Assert.DoesNotContain(PieceType.Multiplier, ledger.PublicView(HandFixtures.P0).Types);
    }

    [Fact]
    public void 落子1枚即全保留()
    {
        // 设计文档 §5.5：本轮征募 3 枚、只部署其中 1 枚并确认批次 → 本轮征募所得全部保留，未部署的 2 枚留在手牌；
        // 下一小回合它们已折进基数，即使下一回合 Pass 也不会被撤销。走真实结算驱动器。变异验证 M-H12 / M-H13 / M-R2 / M-H10 均让本测试红；
        // check M-C4：DeductHand 扣完不折叠（新增留在 Gained）→ 红 2（本测试 + 连续三个小回合的账目折叠）；M-C1：Pick 把新增记进 Carried → 红 10。
        GameSeed seed = HandFixtures.SeedWhere(p => p.CountOf(PieceType.Fortress) >= 3);
        HandLedger ledger = HandFixtures.Ledger(seed);
        GameBoard board = TestMaps.Blank(size: 7);
        var driver = new SettlementDriver(board, new BoardHistory(), new HandHooks(ledger));
        PlayerHandAccess access = HandFixtures.Begin(ledger, HandFixtures.P0);
        RecruitPanelView panel = access.EnterRecruit();
        foreach (int index in panel.IndicesOf(PieceType.Fortress).Take(3))
        {
            access.Pick(index);
        }

        Assert.Equal(new HandEntry(0, 3), access.PrivateView().EntryOf(PieceType.Fortress));

        BatchContext context = BatchFixtures.Context(board, HandFixtures.P0, 3, stock: BatchFixtures.Stock((PieceType.Basic, 5), (PieceType.Fortress, 3)));
        Assert.True(driver.Confirm(context, [BatchFixtures.P("D4", PieceType.Fortress)]).Confirmed);

        HandPrivateView after = access.PrivateView();
        Assert.Equal(2, after.CountOf(PieceType.Fortress));
        Assert.Equal(new HandEntry(2, 0), after.EntryOf(PieceType.Fortress));
        Assert.Equal(7, after.TotalCount);
        Assert.Equal(0, after.PendingGained);
        ledger.EndTurn(HandFixtures.P0);
        Assert.Equal(1, ledger.Records[0].DeployedCount);
        Assert.Equal(3, ledger.Records[0].RecruitedCount);

        // 第二小回合：不征募直接 Pass → 上轮留下的 2 枚堡垒子不受影响
        access = HandFixtures.Begin(ledger, HandFixtures.P0, round: 2);
        access.EnterRecruit();
        ledger.OnPass(HandFixtures.P0);
        Assert.Equal(2, access.PrivateView().CountOf(PieceType.Fortress));
        Assert.Equal(7, access.PrivateView().TotalCount);
    }

    [Fact]
    public void Pass不撤销本轮的整类弃牌()
    {
        // 裁决记录 4：本小回合主动弃掉整类协同子后 Pass → 协同子不恢复，其释放的槽位保持空闲。
        // 变异验证 M-H15：OnPass 把本轮 Discarded 的类型按弃牌前数量写回 → 红 1（本测试）。
        HandLedger ledger = HandFixtures.Ledger();
        ledger.Debug.SeedHand(HandFixtures.P0, (PieceType.Basic, 5), (PieceType.Synergy, 3));
        PlayerHandAccess access = HandFixtures.Begin(ledger, HandFixtures.P0);
        access.Discard(PieceType.Synergy);
        access.EnterRecruit();

        ledger.OnPass(HandFixtures.P0);

        HandPrivateView after = access.PrivateView();
        Assert.Equal(0, after.CountOf(PieceType.Synergy));
        Assert.Equal(1, after.OccupiedSlots);
        Assert.Equal(5, after.TotalCount);
        ledger.EndTurn(HandFixtures.P0);
        Assert.Equal([PieceType.Synergy], ledger.Records[0].Discarded);
    }

    [Fact]
    public void 已有手牌不受Pass影响()
    {
        // 设计文档 §5.5：进入小回合时 7 枚、本轮未征募 → Pass 后仍 7 枚。开局变体（裁决记录 5）：第一小回合直接 Pass，5 枚初始普通子不被撤销。
        // 变异验证 M-H12（单一总数）→ 红。
        HandLedger ledger = HandFixtures.Ledger();
        PlayerHandAccess first = HandFixtures.Begin(ledger, HandFixtures.P0);
        first.EnterRecruit();
        ledger.OnPass(HandFixtures.P0);
        Assert.Equal(5, first.PrivateView().CountOf(PieceType.Basic));
        ledger.EndTurn(HandFixtures.P0);

        ledger.Debug.SeedHand(HandFixtures.P1, (PieceType.Basic, 4), (PieceType.Line, 3));
        PlayerHandAccess access = HandFixtures.Begin(ledger, HandFixtures.P1);
        access.EnterRecruit();
        Assert.Equal(7, access.PrivateView().TotalCount);

        ledger.OnPass(HandFixtures.P1);

        Assert.Equal(7, access.PrivateView().TotalCount);
        Assert.Equal(4, access.PrivateView().CountOf(PieceType.Basic));
        Assert.Equal(3, access.PrivateView().CountOf(PieceType.Line));
        ledger.EndTurn(HandFixtures.P1);
        Assert.Equal(0, ledger.Records[1].RevokedCount);
    }

    [Fact]
    public void 连续三个小回合的账目折叠()
    {
        // 实现清单 1.2：回合 1 征募 n1 枚并落子 → 全部折进基数；回合 2 征募 n2 枚后 Pass → 只退 n2；回合 3 开始时基数 = 5 − 1 + n1，新增 0。
        // 变异验证 M-H12（单一总数）/ M-H13（Pass 扣基数）/ M-H4（归零不移除）/ M-R22（不写记录）均让本测试红；
        // M-H14（BeginTurn 不折叠）红 0——见「同类型只扣回新增数量」的说明，折叠已由结算路径保证。
        HandLedger ledger = HandFixtures.Ledger();
        PlayerHandAccess access = HandFixtures.Begin(ledger, HandFixtures.P0, slots: 7, round: 1);
        RecruitPanelView p1 = access.EnterRecruit();
        access.Pick(0);
        access.Pick(1);
        ledger.DeductHand(HandFixtures.P0, HandFixtures.Deployed((PieceType.Basic, 1)));
        ledger.EndTurn(HandFixtures.P0);
        HandPrivateView afterRound1 = access.PrivateView();
        Assert.Equal(6, afterRound1.TotalCount);
        Assert.Equal(0, afterRound1.PendingGained);

        access = HandFixtures.Begin(ledger, HandFixtures.P0, slots: 7, round: 2);
        access.EnterRecruit();
        access.Pick(0);
        access.Pick(1);
        access.Pick(2);
        Assert.Equal(3, access.PrivateView().PendingGained);
        Assert.Equal(9, access.PrivateView().TotalCount);
        ledger.OnPass(HandFixtures.P0);
        ledger.EndTurn(HandFixtures.P0);
        Assert.Equal(afterRound1.Entries, access.PrivateView().Entries);

        access = HandFixtures.Begin(ledger, HandFixtures.P0, slots: 7, round: 3);
        HandPrivateView round3 = access.PrivateView();
        Assert.Equal(6, round3.TotalCount);
        Assert.All(round3.Entries.Values, e => Assert.Equal(0, e.Gained));
        Assert.Equal(4 + p1.CandidateTypes.Take(2).CountOf(PieceType.Basic), round3.EntryOf(PieceType.Basic).Carried);
    }
}
