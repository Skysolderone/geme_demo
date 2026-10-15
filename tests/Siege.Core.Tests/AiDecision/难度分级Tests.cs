using System.Collections.Immutable;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;

namespace Siege.Core.Tests.AiDecision;

/// <summary>规格：ai-decision —— Requirement: 难度分级</summary>
public class 难度分级Tests
{
    /// <summary>简单难度不计的六维（ai-eye R26 起简单难度 = 即时势力增量、敌方损失、眼位三维）。</summary>
    private static readonly EvaluationDimension[] NotEasyDimensions =
    [
        EvaluationDimension.Relic, EvaluationDimension.Safety, EvaluationDimension.Growth,
        EvaluationDimension.Initiative, EvaluationDimension.Supply, EvaluationDimension.Threat,
    ];

    [Fact]
    public void 简单难度只看即时收益与眼位()
    {
        // 设计文档 §15.2：简单难度只考虑即时收益 = 即时势力增量 + 敌方势力损失两维；ai-eye R26 起另加眼位（共用停手阈值 80 下只看两维一子不落）。
        // 其余六维（信物、安全、组合成长、先手位、手牌供给、威胁）恒 0；M = 1（贪心一条）。
        // 同一提子 + 信物局面，标准难度的信物 / 安全 / 供给维非零；眼位在两档同一口径（本盘面的取值不钉，眼位非零的样本见「简单难度也会做活」）。
        // 变异验证 M-A11：BatchEvaluator 忽略 immediateOnly → 红 1（本测试）。
        MatchFlow match = 启发式评价维度Tests.CaptureRelicPosition();
        HeuristicTurnController easy = HeuristicAi.Create(match, AiFixtures.P0, AiDifficulty.Easy);
        HeuristicTurnController standard = HeuristicAi.Create(match, AiFixtures.P0, AiDifficulty.Standard);
        Assert.Equal(1, easy.Config.CandidateBatchCount);
        Assert.True(easy.Config.ImmediateOnly);
        Assert.Equal(8, standard.Config.CandidateBatchCount);
        Assert.Equal(32, AiSearchConfig.Hard.CandidateBatchCount);
        // ai-eye D7 / 裁决 R1：停手阈值三档共用，简单难度同样走阈值（贪心组批对三档是同一段代码，差别只在配置里的数）。
        Assert.Equal(AiSearchConfig.DefaultPassThreshold, easy.Config.PassThreshold);
        Assert.Equal(easy.Config.PassThreshold, standard.Config.PassThreshold);
        Assert.Equal(easy.Config.PassThreshold, AiSearchConfig.Hard.PassThreshold);

        StagedBatch batch = match.OpenDeploy();
        RehearsalResult result = match.RehearseBatch(batch, ("G6", PieceType.Basic));
        EvaluationBreakdown e = easy.CreateEvaluator().Evaluate(batch.Placements, result, batch.Context);
        EvaluationBreakdown s = standard.CreateEvaluator().Evaluate(batch.Placements, result, batch.Context);

        Assert.True(e.RawOf(EvaluationDimension.PowerGain) > 0);
        Assert.True(e.RawOf(EvaluationDimension.EnemyLoss) > 0);
        foreach (EvaluationDimension d in NotEasyDimensions)
        {
            Assert.Equal(0, e.RawOf(d));
        }

        Assert.Equal(s.RawOf(EvaluationDimension.PowerGain), e.RawOf(EvaluationDimension.PowerGain));
        Assert.Equal(s.RawOf(EvaluationDimension.EnemyLoss), e.RawOf(EvaluationDimension.EnemyLoss));
        Assert.Equal(s.RawOf(EvaluationDimension.Eye), e.RawOf(EvaluationDimension.Eye));
        Assert.NotEqual(0, s.RawOf(EvaluationDimension.Relic));
        Assert.Equal(-2, s.RawOf(EvaluationDimension.Supply));   // growth-pass-1：第 5 大回合部署上限 4，−(4 − 2)，原 −1
    }

    [Fact]
    public void 简单难度也会做活()
    {
        // ai-eye R26：眼位维度下放到简单难度。盘面同「启发式评价维度Tests.做出第二个眼获得眼位正贡献」：
        //  4 O O O O .     C3 已是单格眼；本批次 F3 把 E3 封成第二个单格眼，棋串成为已确定活形。
        //  3 . O . ! .     原始值 = 眼值增量 1 + 活形数增量 1 × 3 = 4，与标准难度同一口径。
        //    C D E F G
        // 其余六维仍恒 0（威胁、安全等只从标准难度起生效）；加权后眼位贡献 4 × 200 = 800 远超停手阈值 80，简单难度因此会落这一手。
        // 变异 M-D2-R26（Evaluate 里眼位重新只在非简单难度计算）→ 红 8（本测试、简单难度只看即时收益与眼位、做出第二个眼获得眼位正贡献，
        // 及 5 条用缺省 Easy 真实跑局的夹具测试：不收敛对局被截断、截断可复现、终端脚本落子、两条生成图存档 / 回放）。
        static MatchFlow Position() => AiFixtures.Round5().Stones(AiFixtures.P0, "D3", "C4", "D4", "E4", "F4");

        (EvaluationBreakdown easy, RehearsalResult result) = EvaluationFixtures.EvaluateP0(Position(), AiDifficulty.Easy, "F3");
        (EvaluationBreakdown standard, _) = EvaluationFixtures.EvaluateP0(Position(), "F3");

        Assert.Equal(LifeState.Alive, LifeShapeReport.Analyze(result.ProjectedBoard!).GroupLifeAt(TestMaps.At("D4"))!.Life);
        Assert.Equal(4, easy.RawOf(EvaluationDimension.Eye));
        Assert.Equal(standard.RawOf(EvaluationDimension.Eye), easy.RawOf(EvaluationDimension.Eye));
        foreach (EvaluationDimension d in NotEasyDimensions)
        {
            Assert.Equal(0, easy.RawOf(d));
        }

        Assert.True(easy.ContributionOf(EvaluationDimension.Eye) > AiSearchConfig.DefaultPassThreshold);
    }

    [Fact]
    public void 简单难度也不拆自己的活形()
    {
        // ai-eye D7：活形硬约束对全部难度生效。简单难度只评价两维、不看眼位，但同样不得把自己的活形填死。
        // 盘面同「活形硬约束Tests.不拆自己的活形」：P0 活形两个单格眼 C3、E3，填任一个都不提子、且使其失去活形。
        // 变异 M-B5（简单难度跳过硬约束）→ 红 1（本测试）。
        MatchFlow match = 活形硬约束Tests.TwoEyes();
        (StagedBatch batch, SettlementDriver driver) = 活形硬约束Tests.Staging(match);
        RehearsalResult fill = 活形硬约束Tests.Rehearse(batch, driver, BatchFixtures.P("C3"));
        Assert.True(fill.IsLegal, fill.Failure?.Message);
        Assert.Empty(fill.Captures);
        Assert.NotEqual(LifeState.Alive, LifeShapeReport.Analyze(fill.ProjectedBoard!).GroupLifeAt(TestMaps.At("D4"))!.Life);

        HeuristicTurnController easy = 活形硬约束Tests.Deploy(match, AiDifficulty.Easy, batch, driver);

        Assert.True(easy.Config.ImmediateOnly);
        Coord[] expected = [.. match.Board.AllCoords().Where(c => match.Board[c].IsPlayableEmpty && c != TestMaps.At("C3") && c != TestMaps.At("E3")).Order()];
        Assert.Equal(expected, easy.LastPointRanking.Select(p => p.Coord).Distinct().Order());
    }

    [Fact]
    public void 高难度不越权()
    {
        // 设计文档 §15.2：最高难度读取的信息集合与简单难度完全相同——三档是同一个类型 + 不同的纯数值配置，
        // 配置对象里只有整数与布尔；闭包无违禁类型；且高难 AI 的决策同样与隐藏内容无关。
        // 变异验证 M-A1（HeuristicTurnController 加 `public RelicLedger? Leak` 属性）→ 红 3（本测试、未知信物不可读、敌方手牌数量不可读）。
        HeuristicTurnController hard = HeuristicAi.Create(LinkModeFixtures.PlainStarted(), AiFixtures.P0, AiDifficulty.Hard);
        Assert.Equal(32, hard.Config.CandidateBatchCount);
        Assert.Equal(24, hard.Config.CandidatePointCount);
        Assert.False(hard.Config.ImmediateOnly);

        ImmutableHashSet<Type> closure = AiFixtures.ReachableTypes(typeof(HeuristicTurnController));
        Assert.Empty(AiFixtures.Violations(typeof(HeuristicTurnController)));
        Assert.Equal(ImmutableHashSet.Create(typeof(AiSearchConfig)), AiFixtures.ReachableTypes(typeof(AiSearchConfig)));
        Assert.All(typeof(AiSearchConfig).GetProperties(), p => Assert.True(p.PropertyType == typeof(int) || p.PropertyType == typeof(bool), p.Name));
        Assert.Contains(typeof(MatchPublicView), closure);

        (string a, string b, int compared) = 对隐藏信息的概率估计Tests.RunUntilReveal(AiDifficulty.Hard);
        Assert.Equal(a, b);
        Assert.True(compared >= 1);
    }

    [Fact]
    public void 难度名称与次序()
    {
        // expert-lookahead MODIFIED「难度分级」：四档依次为简单、标准、高难、专家；专家在末尾追加，既有三档的名称与序号不变。
        // 专家预设 = 高难 + 前瞻宽度 4（design D9）；三档旧预设与改动前逐字段相等（字面量取自 HEAD 03d45f6 的预设）。
        // 变异 M-A2a：Expert 插在 Hard 之前（Hard 序号变 3）→ 见段 A 实施记录。
        Assert.Equal(["Easy", "Standard", "Hard", "Expert"], Enum.GetNames<AiDifficulty>());
        Assert.Equal([0, 1, 2, 3], Enum.GetValues<AiDifficulty>().Select(d => (int)d));
        Assert.Equal(2, (int)AiDifficulty.Hard);
        Assert.Equal(3, (int)AiDifficulty.Expert);

        // v2-recalibration 段 A（1.7）：四档预设的停手阈值随缺省值由 80 改为 20（V2 复核选定值，四档共用）；其余字段与 HEAD 03d45f6 相同。
        Assert.Equal(new AiSearchConfig(6, 1, true, 0, 20), AiSearchConfig.Easy);
        Assert.Equal(new AiSearchConfig(12, 8, false, 0, 20), AiSearchConfig.Standard);
        Assert.Equal(new AiSearchConfig(24, 32, false, 0, 20), AiSearchConfig.Hard);
        // expert-strength：专家预设退回一层（负责人裁决 2026-09-28，段 B 后：扩样配对 好 7 / 同 33 / 差 20，p = 0.019）= 高难 + 前瞻宽度 4，
        // 多样补充上限与两层权重为 0（可配置项，Search 显式给出才生效）；三档旧预设三行一字不改、两项为 0。
        Assert.Equal(new AiSearchConfig(24, 32, false, 0, 20, LookaheadWidth: 4), AiSearchConfig.Expert);
        Assert.Equal(0, AiSearchConfig.Expert.DiverseSupplementLimit);
        Assert.Equal(0, AiSearchConfig.Expert.TwoPlyWeightPermille);
        Assert.All([AiDifficulty.Easy, AiDifficulty.Standard, AiDifficulty.Hard], d => Assert.Equal(0, AiSearchConfig.ForDifficulty(d).DiverseSupplementLimit));
        Assert.All([AiDifficulty.Easy, AiDifficulty.Standard, AiDifficulty.Hard], d => Assert.Equal(0, AiSearchConfig.ForDifficulty(d).TwoPlyWeightPermille));
        Assert.Equal(AiSearchConfig.Expert, AiSearchConfig.ForDifficulty(AiDifficulty.Expert));
        Assert.Equal(AiSearchConfig.Expert with { CandidateCellLimit = 24 }, AiSearchConfig.ForMap(AiDifficulty.Expert, 411));
        Assert.All([AiDifficulty.Easy, AiDifficulty.Standard, AiDifficulty.Hard], d => Assert.Equal(0, AiSearchConfig.ForDifficulty(d).LookaheadWidth));
    }

    // retire-legacy-maps 1.5（裁决 L8）：「三档旧难度逐步不变」（v5 种子 1 整局的三档黄金哈希）随 v5 删除。
    // 它守的是"引入专家难度时三档旧难度不变"——那次改动早已完成；三档难度今天的走法由链接图三条决策序列基线（4 名标准）
    // 与 AiDecision 其余用例（简单难度只看即时收益与眼位、高难度不越权等手写局面）守护。

    [Fact]
    public void 专家难度不越权()
    {
        // MODIFIED「难度分级」Scenario「高难度不越权」扩到专家：专家与其余三档是同一个控制者类型 + 纯数值配置；
        // 前瞻组件（每次决策新建、不经控制者的实例字段可达）单列为闭包根，同样不得触及违禁类型，且不持有任何随机流（D5）。
        HeuristicTurnController expert = HeuristicAi.Create(LinkModeFixtures.PlainStarted(), AiFixtures.P0, AiDifficulty.Expert);
        Assert.Equal(AiSearchConfig.Expert, expert.Config);
        Assert.Empty(AiFixtures.Violations(typeof(HeuristicTurnController)));
        Assert.Empty(AiFixtures.Violations(typeof(ExpertLookahead)));
        ImmutableHashSet<Type> lookahead = AiFixtures.ReachableTypes(typeof(ExpertLookahead));
        Assert.Contains(typeof(MatchPublicView), lookahead);
        Assert.Contains(typeof(BatchEvaluator), lookahead);
        Assert.DoesNotContain(typeof(Siege.Core.Determinism.RandomStream), lookahead);
        Assert.Contains(typeof(LookaheadRecord), AiFixtures.ReachableTypes(typeof(HeuristicTurnController)));
    }
}
