using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Core.Relics;
using Siege.Sim.Running;

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
        // 设计文档 §9.1 + artisan-terrain-edit 裁决 T-1：基础棋池权重 普通 40 / 堡垒 20 / 连珠 18 / 倍增 12 / 协同 10 / 匠人 10；无徽记时任意两名玩家的调整后权重表完全相同。
        // 变异验证 M-R3：RecruitWeights.BaseTable 改 [40,20,18,12,11,10] → 红 1（本测试）。
        // more-pieces-relics 段 A 改写：缺省内容集 v2 的棋池为十档（新四种各 8，裁决记录），类型次序 = 枚举次序（D9）；六档表改为十档，Scenario 本身不变。
        Assert.Equal([40, 20, 18, 12, 10, 10, 8, 8, 8, 8], RecruitWeights.BaseWeights.ToArray());
        Assert.Equal(
            [PieceType.Basic, PieceType.Fortress, PieceType.Line, PieceType.Multiplier, PieceType.Synergy, PieceType.Artisan,
             PieceType.Bannerman, PieceType.Chain, PieceType.Sentry, PieceType.Boundary],
            RecruitWeights.Order);
        Assert.Equal(18, RecruitWeights.BaseWeightOf(PieceType.Line));
        Assert.Equal(10, RecruitWeights.BaseWeightOf(PieceType.Artisan));

        HandLedger ledger = HandFixtures.Ledger();
        EffectSnapshot a = HandFixtures.Snapshot(ledger, HandFixtures.P0);
        EffectSnapshot b = HandFixtures.Snapshot(ledger, HandFixtures.P3);
        Assert.Equal(RecruitWeights.AdjustedTable(a), RecruitWeights.AdjustedTable(b));
        // 无徽记：分子倍数 4，表为基础 × 4
        Assert.Equal([160, 80, 72, 48, 40, 40, 32, 32, 32, 32], RecruitWeights.AdjustedTable(a));
    }

    [Fact]
    public void 无徽记大样本分布贴合权重表()
    {
        // 实现清单 3.1：无徽记时大样本分布与权重表偏差在容差内（10000 个候选位，每类 ±2.5 个百分点）。
        // 六种类型后总权重 110（artisan-terrain-edit「匠人在池中」：匠人概率 10/110）；期望值 = 10000 × 权重 / 110。
        // 变异验证 M-R4：EnterRecruit 抽样改为 _recruit.NextInt(Order.Length)（等概率）→ 红 1（本测试：普通子 36.4% 变 16.7%）。
        // more-pieces-relics 段 A 改写：缺省内容集 v2 十档、总权重 142，期望值 = 10000 × 权重 / 142（四舍五入）；v1 六档的原期望移到「内容集v1的棋池」。
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

        int[] expected = [2817, 1408, 1268, 845, 704, 704, 563, 563, 563, 563];
        for (int i = 0; i < counts.Length; i++)
        {
            Assert.InRange(counts[i], expected[i] - 250, expected[i] + 250);
        }
    }

    [Fact]
    public void 匠人在池中()
    {
        // 规格 recruitment「匠人在池中」：默认配置下十种类型的权重为 40 / 20 / 18 / 12 / 10 / 10 / 8 / 8 / 8 / 8，匠人出现概率 10 / 142。
        // 期望值不照抄实现表：总权重与匠人占比用独立算式表达，再用 5000 个候选位的实抽样证明匠人真的会被抽到。
        // 变异验证见测试报告 M-A3（权重表漏掉匠人）。
        // more-pieces-relics 段 A 改写（规格 MODIFIED）：总权重 110 → 142，匠人期望 10 / 142 × 5000 ≈ 352。
        Assert.Equal(40 + 20 + 18 + 12 + 10 + 10 + (4 * 8), RecruitWeights.BaseWeights.ToArray().Sum());
        Assert.Equal(10, RecruitWeights.BaseWeightOf(PieceType.Artisan));
        Assert.Equal(RecruitWeights.BaseWeightOf(PieceType.Synergy), RecruitWeights.BaseWeightOf(PieceType.Artisan));

        HandLedger ledger = HandFixtures.Ledger();
        int artisan = 0;
        int total = 0;
        for (int turn = 0; turn < 500; turn++)
        {
            PlayerHandAccess access = HandFixtures.Begin(ledger, HandFixtures.P0, reveal: 10);
            foreach (PieceType type in access.EnterRecruit().CandidateTypes)
            {
                artisan += type == PieceType.Artisan ? 1 : 0;
                total++;
            }

            ledger.OnPass(HandFixtures.P0);
            ledger.EndTurn(HandFixtures.P0);
        }

        Assert.Equal(5000, total);
        // 10 / 142 × 5000 ≈ 352.1，容差 ±2.5 个百分点（±125）。
        Assert.InRange(artisan, 352 - 125, 352 + 125);
    }

    [Fact]
    public void 匠人权重可配置()
    {
        // 规格 recruitment「匠人权重可配置」：批量跑局把匠人权重配置为 18 → 权重表为 40 / 20 / 18 / 12 / 10 / 18 / 8 / 8 / 8 / 8，其余类型不变。
        // more-pieces-relics 段 A 改写（规格 MODIFIED）：缺省内容集 v2，字面量权重表由六档扩为十档（新四种 8 × 4 = 32）。
        // 端到端：对局配置 MatchOptions.ArtisanWeight = 18 必须真的抵达征募抽样。期望序列在测试里用字面量权重表独立抽一遍
        // （不调用 RecruitWeights.AdjustedTable，避免"比较被测方法与它的委托目标"）。
        // 变异验证见测试报告 M-A4（HandLedger 忽略传入权重，恒用默认表）。
        int[] others = [40, 20, 18, 12, 10, 8, 8, 8, 8];
        PieceType[] otherTypes = [PieceType.Basic, PieceType.Fortress, PieceType.Line, PieceType.Multiplier, PieceType.Synergy,
            PieceType.Bannerman, PieceType.Chain, PieceType.Sentry, PieceType.Boundary];
        for (int i = 0; i < otherTypes.Length; i++)
        {
            Assert.Equal(others[i], RecruitWeights.BaseWeightOf(otherTypes[i], artisanWeight: 18));
        }

        Assert.Equal(18, RecruitWeights.BaseWeightOf(PieceType.Artisan, artisanWeight: 18));
        Assert.Equal(5, RecruitWeights.BaseWeightOf(PieceType.Artisan, artisanWeight: 5));

        var seed = new GameSeed(77);
        MatchFlow match = MatchFixtures.Started(seed, options: MatchOptions.Immediate with { ArtisanWeight = 18 });
        Assert.Equal(18, match.ArtisanWeight);
        Assert.Equal(0, match.Hands.RecruitStreamConsumed);
        RecruitPanelView panel = HandFixtures.Begin(match.Hands, HandFixtures.P0, reveal: 10).EnterRecruit();

        RandomStream expected = seed.Stream(GameSeed.Recruit);
        int[] table = [160, 80, 72, 48, 40, 72, 32, 32, 32, 32];   // 基础 × 4（无徽记），匠人 18 × 4 = 72
        PieceType[] direct = [.. Enumerable.Range(0, panel.ShowCount).Select(_ => RecruitWeights.Order[expected.WeightedPick(table)])];
        Assert.Equal(direct, panel.CandidateTypes);

        // 反面：同一种子在默认权重 10 下抽出的序列与上面不同——否则"配置生效"无从谈起。
        RandomStream baseline = seed.Stream(GameSeed.Recruit);
        int[] defaultTable = [160, 80, 72, 48, 40, 40, 32, 32, 32, 32];
        PieceType[] atDefault = [.. Enumerable.Range(0, panel.ShowCount).Select(_ => RecruitWeights.Order[baseline.WeightedPick(defaultTable)])];
        Assert.NotEqual(atDefault, panel.CandidateTypes);

        // 跑局层接线：Scenario 的原话是"批量跑局把匠人权重配置为 18"，所以 RunConfig → MatchOptions 这一段也要钉住。
        // 变异验证 M-C3（检查阶段）：MatchSession.Create 不传 ArtisanWeight → 补本段前全绿 826（缺口），补后红 1（本测试）。
        Assert.Equal(18, MatchSession.Create(SimFixtures.Config(turnLimit: 4) with { ArtisanWeight = 18 }, seed: 5).Match.ArtisanWeight);
        Assert.Equal(MatchOptions.DefaultArtisanWeight, MatchSession.Create(SimFixtures.Config(turnLimit: 4), seed: 5).Match.ArtisanWeight);
    }

    [Fact]
    public void 新棋子在池中()
    {
        // more-pieces-relics 规格 recruitment「新棋子在池中」：默认配置下旗手子、铁链子、哨兵子、界碑子的出现概率各为 8 / 142。
        // 权重 8 是未校准初值、不可配置：对局配置与跑局配置里除匠人权重外没有任何"权重"项，传入匠人权重也不改变它们。
        // 实抽样：10000 个候选位，每种新棋子期望 10000 × 8 / 142 ≈ 563，容差 ±2.5 个百分点（±250）。
        PieceType[] fresh = [PieceType.Bannerman, PieceType.Chain, PieceType.Sentry, PieceType.Boundary];
        Assert.Equal(142, RecruitWeights.BaseWeights.ToArray().Sum());
        foreach (PieceType type in fresh)
        {
            Assert.Equal(8, RecruitWeights.BaseWeightOf(type));
            Assert.Equal(8, RecruitWeights.BaseWeightOf(type, artisanWeight: 18));
        }

        Assert.Equal(["ArtisanWeight"], typeof(MatchOptions).GetProperties().Select(p => p.Name).Where(n => n.Contains("Weight", StringComparison.Ordinal)));
        Assert.Equal(["ArtisanWeight"], typeof(Siege.Sim.Config.RunConfig).GetProperties().Select(p => p.Name).Where(n => n.Contains("Weight", StringComparison.Ordinal)));

        HandLedger ledger = HandFixtures.Ledger();
        var counts = fresh.ToDictionary(t => t, _ => 0);
        for (int turn = 0; turn < 1000; turn++)
        {
            PlayerHandAccess access = HandFixtures.Begin(ledger, HandFixtures.P0, reveal: 10);
            foreach (PieceType type in access.EnterRecruit().CandidateTypes.Where(counts.ContainsKey))
            {
                counts[type]++;
            }

            ledger.OnPass(HandFixtures.P0);
            ledger.EndTurn(HandFixtures.P0);
        }

        Assert.All(fresh, t => Assert.InRange(counts[t], 563 - 250, 563 + 250));
    }

    [Fact]
    public void 内容集v1的棋池()
    {
        // more-pieces-relics 规格 recruitment「内容集 v1 的棋池」：v1 下只有六种类型，权重 40 / 20 / 18 / 12 / 10 / 10（与引入新棋子之前相同）。
        // ① 表：v1 的类型次序与权重表是 v2 的前六项；② 抽样：v1 账本的面板与"用改动前的六档字面量表直接抽"逐位相同（征募序列零变化），
        // 1000 个面板 × 10 个候选位里一枚新棋子都没有，六种的分布贴合原期望（10000 × 权重 / 110，即改写前「无徽记大样本分布」的期望）。
        Assert.Equal(
            [PieceType.Basic, PieceType.Fortress, PieceType.Line, PieceType.Multiplier, PieceType.Synergy, PieceType.Artisan],
            RecruitWeights.OrderOf(ContentSet.V1));
        Assert.Equal([40, 20, 18, 12, 10, 10], RecruitWeights.BaseWeightsOf(ContentSet.V1).ToArray());

        HandLedger ledger = HandFixtures.Ledger(ContentSet.V1);
        Assert.Equal(ContentSet.V1, ledger.ContentSet);
        RandomStream expected = HandFixtures.Seed.Stream(GameSeed.Recruit);
        int[] legacyTable = [160, 80, 72, 48, 40, 40];   // 改动前的六档表 × 4（无徽记），字面量
        PieceType[] legacyOrder = [PieceType.Basic, PieceType.Fortress, PieceType.Line, PieceType.Multiplier, PieceType.Synergy, PieceType.Artisan];
        int[] counts = new int[6];
        for (int turn = 0; turn < 1000; turn++)
        {
            PlayerHandAccess access = HandFixtures.Begin(ledger, HandFixtures.P0, reveal: 10);
            ImmutableArray<PieceType> panel = access.EnterRecruit().CandidateTypes;
            PieceType[] direct = [.. Enumerable.Range(0, panel.Length).Select(_ => legacyOrder[expected.WeightedPick(legacyTable)])];
            Assert.Equal(direct, panel);
            foreach (PieceType type in panel)
            {
                counts[Array.IndexOf(legacyOrder, type)]++;
            }

            ledger.OnPass(HandFixtures.P0);
            ledger.EndTurn(HandFixtures.P0);
        }

        Assert.Equal(10000, counts.Sum());   // 没有一枚落在六种之外
        int[] expectedCounts = [3636, 1818, 1636, 1091, 909, 909];
        for (int i = 0; i < counts.Length; i++)
        {
            Assert.InRange(counts[i], expectedCounts[i] - 250, expectedCounts[i] + 250);
        }
    }
}
