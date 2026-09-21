using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;

namespace Siege.Core.Tests.AiDecision;

/// <summary>规格：ai-decision —— Requirement: AI 决策的可复现性</summary>
public class AI决策的可复现性Tests
{
    [Fact]
    public void 同局面同决策()
    {
        // 同种子 + 同配置重放：全部玩家的征募选择、弃牌与批次部署逐条一致，两局盘面存档逐字节一致。
        // 变异验证 M-A16（Perturb 用 new Random()）→ 红 4（本测试、种子扰动由种子驱动、估计不得使用真实值、高难度不越权）。
        static (string Log, string Save) Play()
        {
            MatchFlow match = MatchFixtures.Started(relics: [("E5", RelicFixtures.Command()), ("B7", RelicFixtures.Depot())]);
            var runner = new MatchRunner(match);
            ImmutableSortedDictionary<PlayerId, HeuristicTurnController> ais = runner.AttachAi(AiDifficulty.Standard);
            runner.RunMajorRounds(3);
            return (AiFixtures.DecisionLog(ais), match.Serialize());
        }

        (string logA, string saveA) = Play();
        (string logB, string saveB) = Play();
        Assert.Equal(logA, logB);
        Assert.Equal(saveA, saveB);
        Assert.Contains("R:", logA);
        Assert.Contains("D:", logA);
        Assert.Matches("D:[A-L][0-9]", logA);
    }

    [Fact]
    public void 并列确定性打破()
    {
        // design.md D4：总分相同 → 按落点排序后的坐标序列字典序（Coord 自身的先行后列比较），不引入随机。
        // 变异验证 M-A19：CompareCoords 反向 → 红 1（本测试）。
        EvaluationBreakdown same = EvaluationBreakdown.Zero(EvaluationWeights.Default);
        CandidateBatch c3 = Batch(same, "C3");
        CandidateBatch b2 = Batch(same, "B2");
        CandidateBatch b2c3 = Batch(same, "C3", "B2");
        CandidateBatch b2b3 = Batch(same, "B3", "B2");
        Assert.Same(b2, CandidateSelection.Best([c3, b2]));
        Assert.Same(b2, CandidateSelection.Best([b2, c3]));
        Assert.Same(b2b3, CandidateSelection.Best([b2c3, b2b3]));
        // 前缀更短者优先；先行后列：B3 (行 3) 排在 C2 (行 2) 之后
        Assert.Same(b2, CandidateSelection.Best([b2b3, b2]));
        Assert.True(CandidateSelection.Compare(Batch(same, "C2"), Batch(same, "B3")) < 0);

        // 整局面：空盘首手，出生区内大量落点同分；选中的批次必须是同分候选里坐标序列最小的那个
        MatchFlow match = MatchFixtures.Started();
        HeuristicTurnController ai = HeuristicAi.Create(match, match.CurrentPlayer!.Value, AiDifficulty.Hard);
        ai.Deploy(match.OpenDeploy(), match.Rehearse);
        CandidateBatch choice = ai.LastChoice!;
        foreach (CandidateBatch other in ai.LastCandidates.Where(c => c.Total == choice.Total))
        {
            Assert.True(CandidateSelection.CompareCoords(choice.SortedCoords, other.SortedCoords) <= 0, $"{choice} 应不晚于 {other}");
        }

        BigInteger top = ai.LastPointRanking[0].Total;
        Assert.True(ai.LastPointRanking.Count(p => p.Total == top) >= 2, "首手应存在同分落点，否则本测试没有覆盖并列");
    }

    private static CandidateBatch Batch(EvaluationBreakdown evaluation, params string[] cells) =>
        new([.. cells.Select(c => new Placement(Coord.Parse(c), PieceType.Basic))], evaluation);
}
