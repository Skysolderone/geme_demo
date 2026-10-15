using System.Numerics;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.AiDecision;

/// <summary>规格：ai-decision —— Requirement: 已确定活形的评价中性（ai-eye 段 A，1.3 / 1.4 / 1.5）</summary>
public class 已确定活形的评价中性Tests
{
    [Fact]
    public void 活棋补气不加分()
    {
        // 9×9 平地图，P0 左下角活形（两个单格眼 A1、C1），P1 封住除 D3 外的全部外气：
        //  4 . . . . .
        //  3 X X X ! !     X = P1；! = 本批次 D3、E3
        //  2 O O O O X     O = P0
        //  1 . O . O X
        //    A B C D E
        // 批次前 P0 棋串气 = 眼 A1、C1 + 外气 D3 = 3；落 D3、E3 后外气 D4 / E4 / F3 = 3，即增加 2 口外气，棋串仍是已确定活形。
        // 旧公式（气数 + 两眼潜力 + 分散气 − 危险）下这手是大幅加分：3 气处于危险（大小 × 4），5 气解除危险。活形中性要求恒为 0。
        // 变异 M-A3：GroupSafety.Score 的 `if (IsAlive)` 改成运行时恒假（活棋仍走公式）→ 红 2（本测试 + 候选格上限的黄金哈希；AiDecision 过滤）。
        MatchFlow match = AiFixtures.Round5()
            .Stones(AiFixtures.P0, "D3", "C4", "D4", "E4", "F3", "F4")
            .Stones(AiFixtures.P1, "C5", "D5", "E5", "G3", "G4");
        GroupLife before = LifeShapeReport.Analyze(match.Board).GroupLifeAt(TestMaps.At("D4"))!;
        Assert.Equal(LifeState.Alive, before.Life);
        Assert.Equal(["C3", "E3", "F5"], match.Board.LibertiesOf(before.Group).Notations());

        (EvaluationBreakdown e, RehearsalResult result) = EvaluationFixtures.EvaluateP0(match, "F5", "G5");

        GameBoard after = result.ProjectedBoard!;
        GroupLife now = LifeShapeReport.Analyze(after).GroupLifeAt(TestMaps.At("D4"))!;
        Assert.Equal(LifeState.Alive, now.Life);
        Assert.Equal(["C3", "E3", "H5", "F6", "G6"], after.LibertiesOf(now.Group).Notations());
        Assert.Empty(result.Captures);
        Assert.Single(after.GroupsOf(AiFixtures.P0));

        Assert.Equal(0, e.RawOf(EvaluationDimension.Safety));
    }

    [Fact]
    public void 做活在安全维度上是净收益()
    {
        // P0 棋串 B1 / A2 / B2 / C2 只剩 A1、C1 两口气（P1 封住 A3、B3、C3、D2），A1 已是单格眼：
        //  3 X X X .
        //  2 O O O X
        //  1 . O . !     ! = 本批次 D1：C1 的邻格 B1、C2、D1 全为 P0 → 第二个单格眼
        //    A B C D
        // 落 D1 后该棋串成为已确定活形。活形常数取公式上界，所以做活在安全维度上必然是净收益（原始值为正）。
        MatchFlow match = AiFixtures.Round5()
            .Stones(AiFixtures.P0, "D3", "C4", "D4", "E4")
            .Stones(AiFixtures.P1, "C5", "D5", "E5", "F4");
        GroupLife before = LifeShapeReport.Analyze(match.Board).GroupLifeAt(TestMaps.At("D4"))!;
        Assert.Equal(LifeState.Undetermined, before.Life);
        Assert.Equal(2, match.Board.LibertiesOf(before.Group).Length);

        (EvaluationBreakdown e, RehearsalResult result) = EvaluationFixtures.EvaluateP0(match, "F3");

        Assert.Equal(LifeState.Alive, LifeShapeReport.Analyze(result.ProjectedBoard!).GroupLifeAt(TestMaps.At("D4"))!.Life);
        Assert.True(e.RawOf(EvaluationDimension.Safety) > 0, e.ToString());
    }

    [Fact]
    public void 不攻打活棋()
    {
        // P1 右上角活形（两个单格眼 J9、G9），P0 封住 G7 / H7 / J7，P1 的外气只剩 E8、E9、F7 三口：
        //  9 ! X . X .     X = P1；! = 本批次 E8、E9
        //  8 ! X X X X
        //  7 . . O O O     O = P0
        //    E F G H J
        // 落 E8、E9 后外气由 3 降到 1（总气 5 → 3，已进入危险气数），但它是已确定活形、提不动：威胁维度 MUST 为 0。
        // 1.5：敌方已确定活形也不计入任何潜在提子价值——敌损只含已实现的势力下降与提子，逐项复算相等。
        // 变异 M-A2：ThreatOf 删掉"排除活形敌串"条件 → 红 1（本测试，威胁 +6）。
        // 变异 M-A5：敌损加一项不排除活形的"潜在提子价值"（全部非己方棋串中气数 ≤ 3 者的棋子数）→ 红 2（本测试 + 评价覆盖九个维度）。
        MatchFlow match = AiFixtures.Round5()
            .Stones(AiFixtures.P1, "J10", "K9", "J9", "H9", "G10", "G9")
            .Stones(AiFixtures.P0, "H8", "J8", "K8");
        GroupLife before = LifeShapeReport.Analyze(match.Board).GroupLifeAt(TestMaps.At("J9"))!;
        Assert.Equal(LifeState.Alive, before.Life);
        Assert.Equal(["G8", "F9", "F10", "H10", "K10"], match.Board.LibertiesOf(before.Group).Notations());

        (EvaluationBreakdown e, RehearsalResult result) = EvaluationFixtures.EvaluateP0(match, "F9", "F10");

        GameBoard after = result.ProjectedBoard!;
        GroupLife now = LifeShapeReport.Analyze(after).GroupLifeAt(TestMaps.At("J9"))!;
        Assert.Equal(LifeState.Alive, now.Life);
        Assert.Equal(3, after.LibertiesOf(now.Group).Length);
        Assert.True(after.LibertiesOf(now.Group).Length <= GroupSafety.DangerLiberties);
        Assert.Empty(result.Captures);

        Assert.Equal(0, e.RawOf(EvaluationDimension.Threat));

        // 敌损 = 参赛敌方势力下降 + 提子数 × 2（本批次无提子），不含任何对活棋的"潜在提子价值"。
        BigInteger p1Drop = PowerCalculator.Compute(match.Board, match.Roster).Of(AiFixtures.P1).Total
            - PowerCalculator.Compute(after, match.Roster).Of(AiFixtures.P1).Total;
        Assert.Equal(p1Drop, e.RawOf(EvaluationDimension.EnemyLoss));
    }
}

/// <summary>眼位 / 威胁 / 活形中性测试共用：给 P0 补足手牌，开部署，预演指定落点（普通子）并评价。</summary>
internal static class EvaluationFixtures
{
    internal static (EvaluationBreakdown Evaluation, RehearsalResult Result) EvaluateP0(MatchFlow match, params string[] cells) =>
        EvaluateP0(match, AiDifficulty.Standard, cells);

    internal static (EvaluationBreakdown Evaluation, RehearsalResult Result) EvaluateP0(MatchFlow match, AiDifficulty difficulty, params string[] cells)
    {
        match.Debug.SeedHand(AiFixtures.P0, (PieceType.Basic, 10));
        HeuristicTurnController ai = HeuristicAi.Create(match, AiFixtures.P0, difficulty);
        StagedBatch batch = match.OpenDeploy();
        BatchEvaluator evaluator = ai.CreateEvaluator();
        RehearsalResult result = match.RehearseBatch(batch, [.. cells.Select(c => (c, PieceType.Basic))]);
        Assert.True(result.IsLegal, $"夹具批次不合法：{result.Failure?.Message}");
        return (evaluator.Evaluate(batch.Placements, result, batch.Context), result);
    }
}
