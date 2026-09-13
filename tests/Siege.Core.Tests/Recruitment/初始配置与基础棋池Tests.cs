using Siege.Core.Board;
using Siege.Core.Recruit;
using Siege.Core.Relics;

namespace Siege.Core.Tests.Recruitment;

/// <summary>规格：recruitment —— Requirement: 初始配置与基础棋池</summary>
public class 初始配置与基础棋池Tests
{
    [Fact]
    public void 开局手牌()
    {
        // 设计文档 §9.1：每名玩家开局 5 枚普通子，占 1 个类型槽；裁决记录 5：计入回合前基数。
        // 变异验证 M-R1：HandLedger 构造把初始手牌写成 new HandEntry(0, 5)（计入本轮新增）→ 红 1（本测试；「已有手牌不受 Pass 影响」的开局变体不红，因为其 Pass 前先进入了新回合，BeginTurn 已折叠）；
        // M-R2：InitialBasicCount 改 4 → 红 8（本测试 + 依赖开局 5 枚的 7 个用例）。
        HandLedger ledger = HandFixtures.Ledger();

        foreach (PlayerId player in ledger.Players)
        {
            HandPrivateView hand = ledger.Debug.PrivateViewOf(player);
            Assert.Equal(new HandEntry(5, 0), hand.EntryOf(PieceType.Basic));
            Assert.Equal(5, hand.TotalCount);
            Assert.Equal(1, hand.OccupiedSlots);
            Assert.Equal([PieceType.Basic], hand.Types);
            Assert.Equal(1, ledger.HeldTypeCount(player));
        }

        Assert.Equal(4, ledger.Players.Count());
    }

    [Fact]
    public void 棋池全局一致()
    {
        // 设计文档 §9.1：基础棋池权重 普通 40 / 堡垒 20 / 连珠 18 / 倍增 12 / 协同 10；无徽记时任意两名玩家的调整后权重表完全相同。
        // 变异验证 M-R3：RecruitWeights.BaseTable 改 [40,20,18,12,11] → 红 1（本测试）。
        Assert.Equal([40, 20, 18, 12, 10], RecruitWeights.BaseWeights.ToArray());
        Assert.Equal([PieceType.Basic, PieceType.Fortress, PieceType.Line, PieceType.Multiplier, PieceType.Synergy], RecruitWeights.Order);
        Assert.Equal(18, RecruitWeights.BaseWeightOf(PieceType.Line));

        HandLedger ledger = HandFixtures.Ledger();
        EffectSnapshot a = HandFixtures.Snapshot(ledger, HandFixtures.P0);
        EffectSnapshot b = HandFixtures.Snapshot(ledger, HandFixtures.P3);
        Assert.Equal(RecruitWeights.AdjustedTable(a), RecruitWeights.AdjustedTable(b));
        // 无徽记：分子倍数 4，表为基础 × 4
        Assert.Equal([160, 80, 72, 48, 40], RecruitWeights.AdjustedTable(a));
    }

    [Fact]
    public void 无徽记大样本分布贴合权重表()
    {
        // 实现清单 3.1：无徽记时大样本分布与权重表偏差在容差内（10000 个候选位，每类 ±2.5 个百分点）。
        // 变异验证 M-R4：EnterRecruit 抽样改为 _recruit.NextInt(Order.Length)（等概率）→ 红 1（本测试：普通子 40% 变 20%）。
        HandLedger ledger = HandFixtures.Ledger();
        int[] counts = new int[RecruitWeights.Order.Length];
        for (int turn = 0; turn < 1000; turn++)
        {
            PlayerHandAccess access = HandFixtures.Begin(ledger, HandFixtures.P0, reveal: 10);
            foreach (PieceType type in access.EnterRecruit().CandidateTypes)
            {
                counts[RecruitWeights.IndexOf(type)]++;
            }

            ledger.OnPass(HandFixtures.P0);
            ledger.EndTurn(HandFixtures.P0);
        }

        int[] expected = [4000, 2000, 1800, 1200, 1000];
        for (int i = 0; i < counts.Length; i++)
        {
            Assert.InRange(counts[i], expected[i] - 250, expected[i] + 250);
        }
    }
}
