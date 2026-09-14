using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.AiDecision;

/// <summary>规格：ai-decision —— Requirement: 启发式评价维度</summary>
public class 启发式评价维度Tests
{
    /// <summary>
    /// 提子 + 抢信物局面（第 5 大回合，P0 行动）。棋形（9×9 局部，行号自下而上）：
    /// <code>
    ///  9 X . . . . . . . .      X = P1
    ///  8 . . . . . . . X .      O = P0
    ///  7 . . . . . . . . .
    ///  6 . . . . O . . X .      F5 = 信物格（军令 +1，未揭示）
    ///  5 . . . O X ! . . .      ! = 妙手 F5：提 E5、占 F5 信物
    ///  4 . . . . O . . . .
    ///    A B C D E F G H J
    /// </code>
    /// P1 的 E5 只剩一口气 F5；P1 另有 H8、H6、A9 使 P1 势力 14（4 子 + 独占 F5,H5,G6,J6,H7,G8,J8,A8,H9,B9）高于 P0 的 10
    ///（3 子 + 独占 E3,D4,F4,C5,D6,F6,E7）；F5 提 E5 后 P0 13（4 子 + 9 独占格，新增 E5、G5）> P1 12，P0 从第 2 名升到第 1 名；
    /// 而 D4 这类安静落点只 +2（12 &lt; 14），名次不变。P0 手牌薄（普通子 2 + 倍增子 1 = 部署上限 3），落 1 子后下回合缺 1 枚，供给维 −1。
    /// </summary>
    internal static MatchFlow CaptureRelicPosition()
    {
        MatchFlow match = AiFixtures.Round5(relics: [("F5", RelicFixtures.Command())])
            .Stones(AiFixtures.P0, "D5", "E4", "E6")
            .Stones(AiFixtures.P1, "E5", "H8", "H6", "A9");
        match.Debug.SeedHand(AiFixtures.P0, (PieceType.Basic, 2), (PieceType.Multiplier, 1));
        return match;
    }

    [Fact]
    public void 评价覆盖七个维度()
    {
        // 设计文档 §15.2 七个维度。F5 落倍增子：势力增量、敌方损失与提子、信物控制、安全度、组合成长、先手位、供给匹配全部非零；
        // 总分 = Σ 权重 × 原始值，逐维可分解。
        // 变异验证 M-A6：Evaluate 不写 Relic 维（raw[2] 恒 0）→ 红 3（本测试、简单难度只看即时收益、估计不得使用真实值）。
        MatchFlow match = CaptureRelicPosition();
        HeuristicTurnController ai = HeuristicAi.Create(match, AiFixtures.P0);
        StagedBatch batch = match.OpenDeploy();
        BatchEvaluator evaluator = ai.CreateEvaluator();

        RehearsalResult result = match.RehearseBatch(batch, ("F5", PieceType.Multiplier));
        Assert.True(result.IsLegal);
        Assert.Single(result.Captures);

        EvaluationBreakdown e = evaluator.Evaluate(batch.Placements, result, batch.Context);
        Assert.Equal(EvaluationBreakdown.DimensionCount, e.Raw.Length);
        Assert.True(e.RawOf(EvaluationDimension.PowerGain) > 0, e.ToString());
        Assert.True(e.RawOf(EvaluationDimension.EnemyLoss) > 0, e.ToString());
        Assert.True(e.RawOf(EvaluationDimension.Relic) > 0, e.ToString());
        Assert.NotEqual(0, e.RawOf(EvaluationDimension.Safety));
        Assert.True(e.RawOf(EvaluationDimension.Growth) > 0, e.ToString());
        Assert.True(e.RawOf(EvaluationDimension.Initiative) > 0, e.ToString());
        Assert.Equal(-1, e.RawOf(EvaluationDimension.Supply));

        long sum = 0;
        foreach (EvaluationDimension d in Enum.GetValues<EvaluationDimension>())
        {
            Assert.Equal(EvaluationWeights.Default.Of(d) * e.RawOf(d), e.ContributionOf(d));
            sum += e.ContributionOf(d);
        }

        Assert.Equal(sum, e.Total);

        // 敌损 = 参赛敌方势力下降 + 提子数 × 2（P1 失去 E5 的军势 1 与 F5 独占格 1，提 1 子）
        long p1Drop = evaluator.Before.Of(AiFixtures.P1).Total - PowerCalculator.Compute(result.ProjectedBoard!, match.Roster).Of(AiFixtures.P1).Total;
        Assert.Equal(2, p1Drop);
        Assert.Equal(p1Drop + BatchEvaluator.CapturePerStone, e.RawOf(EvaluationDimension.EnemyLoss));
    }

    [Fact]
    public void 权重可配置()
    {
        // 同一局面两个候选：A = F5 普通子（提子），B = A1 倍增子（远处成长）。只开敌损权重 → A 优先；只开成长权重 → B 优先。
        // 变异验证 M-A8：ContributionOf 忽略权重（直接返回原始值）→ 红 2（本测试 + 评价覆盖七个维度）。
        MatchFlow match = CaptureRelicPosition();
        match.Debug.SeedHand(AiFixtures.P0, (PieceType.Basic, 50), (PieceType.Multiplier, 1));
        StagedBatch batch = match.OpenDeploy();
        MatchPublicView view = match.Publish();

        var onlyEnemyLoss = new EvaluationWeights(PowerGain: 0, EnemyLoss: 10, Relic: 0, Safety: 0, Growth: 0, Initiative: 0, Supply: 0);
        var onlyGrowth = new EvaluationWeights(PowerGain: 0, EnemyLoss: 0, Relic: 0, Safety: 0, Growth: 10, Initiative: 0, Supply: 0);

        CandidateBatch Candidate(EvaluationWeights weights, string cell, PieceType type)
        {
            var evaluator = new BatchEvaluator(AiFixtures.P0, view, weights, immediateOnly: false);
            RehearsalResult result = match.RehearseBatch(batch, (cell, type));
            return new CandidateBatch(batch.Placements, evaluator.Evaluate(batch.Placements, result, batch.Context));
        }

        Assert.True(CandidateSelection.Compare(Candidate(onlyEnemyLoss, "F5", PieceType.Basic), Candidate(onlyEnemyLoss, "A1", PieceType.Multiplier)) < 0);
        Assert.True(CandidateSelection.Compare(Candidate(onlyGrowth, "F5", PieceType.Basic), Candidate(onlyGrowth, "A1", PieceType.Multiplier)) > 0);
    }

    [Fact]
    public void 先手位评价生效()
    {
        // 设计文档 §11.2：先手值 = 参赛人数 − 势力名次 + 修正；名次上升一位 → 先手值 +1 → 下一大回合更早行动。
        // 局面见 CaptureRelicPosition：P1 14 > P0 10；F5 提 E5 后 P0 13 > P1 12，P0 从第 2 名升到第 1 名。
        // 变异验证 M-A9：InitiativeShift 返回 after − before → 红 2（本测试 + 评价覆盖七个维度）。
        MatchFlow match = CaptureRelicPosition();
        PowerSnapshot before = match.Scoreboard.Latest!;
        Assert.Equal(14, before.Of(AiFixtures.P1).Total);
        Assert.Equal(10, before.Of(AiFixtures.P0).Total);
        Assert.Equal(2, before.RankOf(AiFixtures.P0));

        HeuristicTurnController ai = HeuristicAi.Create(match, AiFixtures.P0);
        StagedBatch batch = match.OpenDeploy();
        RehearsalResult result = match.RehearseBatch(batch, ("F5", PieceType.Basic));
        EvaluationBreakdown e = ai.CreateEvaluator().Evaluate(batch.Placements, result, batch.Context);

        PowerSnapshot after = PowerCalculator.Compute(result.ProjectedBoard!, match.Roster);
        Assert.Equal(13, after.Of(AiFixtures.P0).Total);
        Assert.Equal(12, after.Of(AiFixtures.P1).Total);
        Assert.Equal(1, after.RankOf(AiFixtures.P0));
        Assert.Equal(1, e.RawOf(EvaluationDimension.Initiative));
        Assert.Equal(Siege.Core.Match.InitiativeOrder.ValueOf(4, 2, 0) + 1, Siege.Core.Match.InitiativeOrder.ValueOf(4, 1, 0));
        Assert.True(e.ContributionOf(EvaluationDimension.Initiative) > 0);

        // 不改变名次的落点（D4：+1 子 +2 独占格 −1 原独占格 = 12 < 14）：该维度为 0
        RehearsalResult quiet = match.RehearseBatch(batch, ("D4", PieceType.Basic));
        Assert.Equal(0, ai.CreateEvaluator().Evaluate(batch.Placements, quiet, batch.Context).RawOf(EvaluationDimension.Initiative));
    }

    [Fact]
    public void 两眼潜力近似区分活形与死形()
    {
        // implement 2.2 / 裁决 1：单点气 = 每个邻格都是己方棋子或障碍（外沿同义）；两个单点气必然互不相邻。
        // 活形（角部，两个单点眼 A1、C1）：          死形（同形去掉 B1，A1-B1-C1 连成一只大眼）：
        //  3 . . . . .                                3 . . . . .
        //  2 O O O O .                                2 O O O O .
        //  1 . O . O .                                1 . . . O .
        //    A B C D E                                  A B C D E
        // 变异验证 M-A10：Analyze 判单点气时把"邻格是己方子"放宽为"邻格非空或为气"→ 红 1（本测试：死形的 A1/B1/C1 被误判为眼）。
        GameBoard living = TestMaps.Blank(9);
        foreach (string s in new[] { "B1", "A2", "B2", "C2", "D1", "D2" })
        {
            living.Place(s, TestMaps.P0);
        }

        GameBoard dead = TestMaps.Blank(9);
        foreach (string s in new[] { "A2", "B2", "C2", "D1", "D2" })
        {
            dead.Place(s, TestMaps.P0);
        }

        GroupSafety alive = GroupSafety.Analyze(living, living.GroupAt(TestMaps.At("B2"))!);
        GroupSafety weak = GroupSafety.Analyze(dead, dead.GroupAt(TestMaps.At("B2"))!);

        Assert.Equal(2, alive.EyePoints);
        Assert.Equal(2, alive.TwoEyePotential);
        Assert.Equal(0, weak.EyePoints);
        Assert.Equal(0, weak.TwoEyePotential);
        Assert.True(alive.Score - weak.Score >= 12, $"活形 {alive} 死形 {weak}");
        Assert.Equal(8, alive.Liberties);
        Assert.Equal(9, weak.Liberties);
    }
}
