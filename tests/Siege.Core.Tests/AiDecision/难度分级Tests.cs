using System.Collections.Immutable;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;

namespace Siege.Core.Tests.AiDecision;

/// <summary>规格：ai-decision —— Requirement: 难度分级</summary>
public class 难度分级Tests
{
    [Fact]
    public void 简单难度只看即时收益()
    {
        // 设计文档 §15.2：简单难度只考虑即时收益 = 即时势力增量 + 敌方势力损失两维，其余七维恒 0（ai-eye 起含眼位、威胁）；M = 1（贪心一条）。
        // 同一提子 + 信物局面，标准难度的信物 / 安全 / 供给维非零。
        // 变异验证 M-A11：BatchEvaluator 忽略 immediateOnly → 红 1（本测试）。
        MatchFlow match = 启发式评价维度Tests.CaptureRelicPosition();
        HeuristicTurnController easy = HeuristicAi.Create(match, AiFixtures.P0, AiDifficulty.Easy);
        HeuristicTurnController standard = HeuristicAi.Create(match, AiFixtures.P0, AiDifficulty.Standard);
        Assert.Equal(1, easy.Config.CandidateBatchCount);
        Assert.True(easy.Config.ImmediateOnly);
        Assert.Equal(8, standard.Config.CandidateBatchCount);
        Assert.Equal(32, AiSearchConfig.Hard.CandidateBatchCount);

        StagedBatch batch = match.OpenDeploy();
        RehearsalResult result = match.RehearseBatch(batch, ("F5", PieceType.Basic));
        EvaluationBreakdown e = easy.CreateEvaluator().Evaluate(batch.Placements, result, batch.Context);
        EvaluationBreakdown s = standard.CreateEvaluator().Evaluate(batch.Placements, result, batch.Context);

        Assert.True(e.RawOf(EvaluationDimension.PowerGain) > 0);
        Assert.True(e.RawOf(EvaluationDimension.EnemyLoss) > 0);
        foreach (EvaluationDimension d in new[] { EvaluationDimension.Relic, EvaluationDimension.Safety, EvaluationDimension.Growth, EvaluationDimension.Initiative, EvaluationDimension.Supply, EvaluationDimension.Eye, EvaluationDimension.Threat })
        {
            Assert.Equal(0, e.RawOf(d));
        }

        Assert.Equal(s.RawOf(EvaluationDimension.PowerGain), e.RawOf(EvaluationDimension.PowerGain));
        Assert.Equal(s.RawOf(EvaluationDimension.EnemyLoss), e.RawOf(EvaluationDimension.EnemyLoss));
        Assert.NotEqual(0, s.RawOf(EvaluationDimension.Relic));
        Assert.Equal(-2, s.RawOf(EvaluationDimension.Supply));   // growth-pass-1：第 5 大回合部署上限 4，−(4 − 2)，原 −1
    }

    [Fact]
    public void 高难度不越权()
    {
        // 设计文档 §15.2：最高难度读取的信息集合与简单难度完全相同——三档是同一个类型 + 不同的纯数值配置，
        // 配置对象里只有整数与布尔；闭包无违禁类型；且高难 AI 的决策同样与隐藏内容无关。
        // 变异验证 M-A1（HeuristicTurnController 加 `public RelicLedger? Leak` 属性）→ 红 3（本测试、未知信物不可读、敌方手牌数量不可读）。
        HeuristicTurnController hard = HeuristicAi.Create(MatchFixtures.Started(), AiFixtures.P0, AiDifficulty.Hard);
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
}
