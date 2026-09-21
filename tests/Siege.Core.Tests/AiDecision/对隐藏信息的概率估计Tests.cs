using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Relics;

namespace Siege.Core.Tests.AiDecision;

/// <summary>规格：ai-decision —— Requirement: 对隐藏信息的概率估计</summary>
public class 对隐藏信息的概率估计Tests
{
    [Theory]
    [InlineData(RelicType.SchoolEmblem, 45)]
    [InlineData(RelicType.Prospecting, 20)]
    [InlineData(RelicType.Depot, 15)]
    [InlineData(RelicType.Conscription, 8)]
    [InlineData(RelicType.Command, 7)]
    [InlineData(RelicType.Vanguard, 5)]
    public void 按分区权重估计未知信物(RelicType type, int percent)
    {
        // 设计文档 §8.2 出生区权重表：徽记 45 / 探勘 20 / 兵站 15 / 征召 8 / 军令 7 / 先锋 5。
        // 先验直接读 RelicWeights（单一实现）；期望价值 = Σ 权重 × 类型价值 = 45×4+20×6+15×5+8×8+7×10+5×7 = 544 → 取整 5。
        // 变异验证 M-A4：PriorPercent 改读 Contested 表 → 红 6（本 Theory 全部 6 条）。
        Assert.Equal(percent, RelicEstimate.PriorPercent(RelicZone.BirthZone, type));
        Assert.Equal(544, RelicEstimate.ExpectedValueScaled(RelicZone.BirthZone));

        var unrevealed = new RelicPublicState(TestMaps.At("B2"), new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth),
            IsRevealed: false, Content: null, RevealedInMajorRound: null, RelicControl.Uncontrolled);
        Assert.Equal(5, RelicEstimate.Estimate(unrevealed));
    }

    [Fact]
    public void 估计不得使用真实值()
    {
        // 设计文档 §15.1。1.5 的"大样本相关系数"用确定性等价物代替，分三段：
        // (1) 敏感度见证：读真实内容的调试评价器对同一批次（A2 普通子，覆盖 B2）在四种不同真实内容下给出不同的信物维分——证明局面对隐藏内容敏感；
        // (2) 正式评价器对同一批次在四种内容下的七维分解逐维相同——估计与真实内容零相关；
        // (3) 同种子两局只在 B2 内容上不同，B2 揭示前所有玩家的决策序列完全一致。
        // 变异验证 M-A5：把 (2) 的正式评价器换成调试评价器 → 红 1（本测试，四种内容的分解不再唯一）。
        RelicContent[] contents = [RelicFixtures.Command(), RelicFixtures.Emblem(PieceType.Basic), RelicFixtures.Vanguard(), RelicFixtures.Depot()];
        var officialBreakdowns = new List<string>();
        var debugRelicRaw = new List<BigInteger>();
        foreach (RelicContent content in contents)
        {
            MatchFlow match = MatchFixtures.Started(relics: [("B2", content)]);
            match.Debug.SetOrder(AiFixtures.P0, AiFixtures.P1, AiFixtures.P2, AiFixtures.P3);
            StagedBatch batch = match.OpenDeploy();
            RehearsalResult result = match.RehearseBatch(batch, ("A2", PieceType.Basic));
            BatchEvaluator official = HeuristicAi.Create(match, AiFixtures.P0).CreateEvaluator();
            officialBreakdowns.Add(official.Evaluate(batch.Placements, result, batch.Context).ToString());
            BatchEvaluator debug = DebugTurnController.Create(new MatchRunner(match), AiFixtures.P0, debugMode: true).Inner.CreateEvaluator();
            debugRelicRaw.Add(debug.Evaluate(batch.Placements, result, batch.Context).RawOf(EvaluationDimension.Relic));
        }

        Assert.True(debugRelicRaw.Distinct().Count() >= 3, $"调试评价器应随真实内容变化：{string.Join(",", debugRelicRaw)}");
        Assert.Single(officialBreakdowns.Distinct());
        Assert.Contains("信物", officialBreakdowns[0]);

        (string a, string b, int compared) = RunUntilReveal(AiDifficulty.Standard);
        Assert.Equal(a, b);
        Assert.True(compared >= 1, "P0 至少要在 B2 揭示前做出一次部署决策，否则本测试没有覆盖到估计路径");
        Assert.Contains("D:", a);
    }

    /// <summary>两局只在 B2 的隐藏内容上不同；逐小回合比较决策日志，直到 B2 在任一局被揭示为止。返回两份日志与 P0 被比较的部署决策数。</summary>
    internal static (string LogA, string LogB, int ComparedP0Deploys) RunUntilReveal(AiDifficulty difficulty)
    {
        Coord b2 = TestMaps.At("B2");
        MatchFlow matchA = MatchFixtures.Started(relics: [("B2", RelicFixtures.Command()), ("E5", RelicFixtures.Vanguard())]);
        MatchFlow matchB = MatchFixtures.Started(relics: [("B2", RelicFixtures.Emblem(PieceType.Basic)), ("E5", RelicFixtures.Vanguard())]);
        Assert.Equal(RelicType.Command, matchA.Relics.Generation.At(b2).Content.Type);
        Assert.Equal(RelicType.SchoolEmblem, matchB.Relics.Generation.At(b2).Content.Type);

        var runnerA = new MatchRunner(matchA);
        var runnerB = new MatchRunner(matchB);
        ImmutableSortedDictionary<PlayerId, HeuristicTurnController> aisA = runnerA.AttachAi(difficulty);
        ImmutableSortedDictionary<PlayerId, HeuristicTurnController> aisB = runnerB.AttachAi(difficulty);

        RelicPublicState StateOf(MatchFlow m) => m.Publish().Relics.Single(r => r.Coord == b2);
        Assert.Equal(RelicEstimate.Estimate(StateOf(matchA)), RelicEstimate.Estimate(StateOf(matchB)));

        string logA = string.Empty;
        string logB = string.Empty;
        int compared = 0;
        for (int turn = 0; turn < 16; turn++)
        {
            PlayerId current = matchA.CurrentPlayer!.Value;
            runnerA.RunTurn();
            runnerB.RunTurn();
            logA = AiFixtures.DecisionLog(aisA);
            logB = AiFixtures.DecisionLog(aisB);
            if (current == AiFixtures.P0)
            {
                compared++;
            }

            if (matchA.Relics.IsRevealed(b2) || matchB.Relics.IsRevealed(b2))
            {
                break;
            }
        }

        return (logA, logB, compared);
    }
}
