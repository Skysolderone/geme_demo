using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Recruit;
using Siege.Core.Relics;

namespace Siege.Core.Tests.Recruitment;

/// <summary>规格：recruitment —— Requirement: 流派徽记调整征募权重</summary>
public class 流派徽记调整征募权重Tests
{
    // 权重全部以整数形式 基础 × (4 + 3n) 表达（分母 4 对全部类型相同，归一化后消去）。
    // 变异验证 M-R5：RecruitWeights.AdjustedTable 改为 BaseTable × 4（忽略快照）→ 红 2（单枚徽记调权 + 调权后重新归一化）；
    // M-R6：AdjustedTable 改 4 + 2n → 红 2（同上）；M-R6b：AdjustedWeightOf 改 4 + 2n → 红 3（三个调权用例全红）。

    [Fact]
    public void 单枚徽记调权()
    {
        // 设计文档 §9.1：1 枚倍增子徽记 → 12 × (1 + 0.75) = 21；整数形式 12 × 7 = 84 = 21 × 4；其余类型不变。
        HandLedger ledger = HandFixtures.Ledger();
        EffectSnapshot snapshot = HandFixtures.Snapshot(ledger, HandFixtures.P0, emblems: [(PieceType.Multiplier, 1)]);

        Assert.Equal(21 * 4, RecruitWeights.AdjustedWeightOf(snapshot, PieceType.Multiplier));
        Assert.Equal([40 * 4, 20 * 4, 18 * 4, 21 * 4, 10 * 4], RecruitWeights.AdjustedTable(snapshot));
    }

    [Fact]
    public void 高阶徽记按数量2()
    {
        // 设计文档 §9.1：1 枚高阶连珠子徽记（数量 2）→ 18 × (1 + 0.75 × 2) = 45；整数形式 18 × 10 = 180 = 45 × 4。
        HandLedger ledger = HandFixtures.Ledger();
        EffectSnapshot snapshot = HandFixtures.Snapshot(ledger, HandFixtures.P0, emblems: [(PieceType.Line, 2)]);

        Assert.Equal(45 * 4, RecruitWeights.AdjustedWeightOf(snapshot, PieceType.Line));
    }

    [Fact]
    public void 多枚同类徽记叠加()
    {
        // 设计文档 §9.1：2 枚普通堡垒子徽记 + 1 枚高阶（合计数量 4）→ 20 × (1 + 0.75 × 4) = 80；整数形式 20 × 16 = 320 = 80 × 4。
        HandLedger ledger = HandFixtures.Ledger();
        EffectSnapshot snapshot = HandFixtures.Snapshot(ledger, HandFixtures.P0, emblems: [(PieceType.Fortress, 4)]);

        Assert.Equal(80 * 4, RecruitWeights.AdjustedWeightOf(snapshot, PieceType.Fortress));
    }

    [Fact]
    public void 调权后重新归一化()
    {
        // 实现清单 3.3：归一化在抽取时由整数累积权重完成。堡垒子徽记数量 4 → 权重 80，总和 40+80+18+12+10 = 160，堡垒子占 50%（默认时 20%）。
        // 5000 个候选位，±3 个百分点。
        // 变异验证 M-R7：EnterRecruit 用 RecruitWeights.BaseWeights 抽样而不是 AdjustedTable → 红 1（本测试：堡垒子回到 20%）。
        HandLedger ledger = HandFixtures.Ledger();
        int fortress = 0;
        for (int turn = 0; turn < 500; turn++)
        {
            PlayerHandAccess access = HandFixtures.Begin(ledger, HandFixtures.P0, reveal: 10, emblems: [(PieceType.Fortress, 4)]);
            fortress += access.EnterRecruit().CandidateTypes.CountOf(PieceType.Fortress);
            ledger.OnPass(HandFixtures.P0);
            ledger.EndTurn(HandFixtures.P0);
        }

        Assert.InRange(fortress, 2500 - 150, 2500 + 150);
    }

    [Fact]
    public void 候选位独立且可重复()
    {
        // 设计文档 §9.1：每个候选位独立抽取，允许同一面板出现多枚同类型。
        // 展示 5 枚的面板：在前 200 个种子中至少有一个面板含 ≥3 枚普通子（独立抽取下概率约 32%），
        // 且面板类型序列 == 对 recruit 子流连续 5 次 WeightedPick 的结果（逐位独立，不做去重/不放回）。
        // 变异验证 M-R8：EnterRecruit 改为跳过与前一位相同的类型（去重）→ 红 1（本测试）。
        bool sawTriple = false;
        for (ulong v = 1; v <= 200; v++)
        {
            var seed = new GameSeed(v);
            HandLedger ledger = HandFixtures.Ledger(seed);
            PlayerHandAccess access = HandFixtures.Begin(ledger, HandFixtures.P0);
            RecruitPanelView panel = access.EnterRecruit();
            Assert.Equal(5, panel.ShowCount);

            RandomStream expected = seed.Stream(GameSeed.Recruit);
            int[] table = [160, 80, 72, 48, 40];
            PieceType[] direct = [.. Enumerable.Range(0, 5).Select(_ => RecruitWeights.Order[expected.WeightedPick(table)])];
            Assert.Equal(direct, panel.CandidateTypes);

            sawTriple |= panel.CandidateTypes.CountOf(PieceType.Basic) >= 3;
        }

        Assert.True(sawTriple, "200 个种子里没有一个面板出现 ≥3 枚普通子：候选位不是独立抽取。");
    }
}
