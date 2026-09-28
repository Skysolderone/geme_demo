using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using static Siege.Core.Tests.LookaheadFixtures;

namespace Siege.Core.Tests.AiDecision;

/// <summary>规格：ai-decision（expert-lookahead）—— Requirement: 前瞻中的停手口径</summary>
public class 前瞻中的停手口径Tests
{
    [Fact]
    public void 高难会Pass时专家也Pass()
    {
        // 停手阈值只在候选生成里生效、口径与高难相同：全部候选落点的边际提升都不超过阈值 → 高难 Pass，专家同样 Pass，不做任何模拟。
        // 阈值取一个任何单枚都越不过的值（P0 盘上有子，无子豁免不适用）。
        static MatchFlow Position() => AiFixtures.Round5().Stones(P0, "E5").Stones(P1, "B8");
        const int unreachable = 1_000_000;
        HeuristicTurnController hard = DecideHard(Position(), unreachable);
        HeuristicTurnController expert = DecideExpert(Position(), unreachable);

        Assert.True(hard.LastChoice!.IsPass);
        Assert.True(expert.LastChoice!.IsPass);
        Assert.Equal(["D:pass"], expert.Decisions);
        LookaheadRecord record = Record(expert);
        Assert.Equal(LookaheadStatus.Pass, record.Status);
        Assert.Empty(record.Entries);
        Assert.Equal(-1, record.ChosenIndex);
        Assert.Equal(0, record.SimulatedRehearsals);

        // expert-strength：专家预设（多样补充上限 8、两层权重 1000‰）同样 Pass，不做多样补充，也不做两层扫描。
        HeuristicTurnController preset = Decide(Position(), AiDifficulty.Expert, AiSearchConfig.Expert with { PassThreshold = unreachable });
        Assert.True(preset.LastChoice!.IsPass);
        LookaheadRecord presetRecord = Record(preset);
        Assert.Equal(LookaheadStatus.Pass, presetRecord.Status);
        Assert.Equal(0, presetRecord.SupplementRehearsals);
        Assert.Equal(0, presetRecord.TwoPlyRehearsals);
        Assert.Equal(0, presetRecord.SimulatedRehearsals);
    }

    /// <summary>
    /// 必失局面（9×9、第 5 大回合、顺序 P0 → P3、专家 P0 的部署上限 1）：
    /// P0 的 7 子串 B5–H5 只剩一口气 J5；J5 的另两个邻格 J4 / J6 是 P1 的子，P0 落 J5 是自杀手，救不回来。
    /// P1 占满第 4、第 6 行与 A5——无论专家落在哪里，下一名对手 P1 的模拟回应都在 J5 提走七子串。
    /// </summary>
    internal static MatchFlow DoomedPosition()
    {
        MatchFlow match = AiFixtures.Round5()
            .Stones(P0, "B5", "C5", "D5", "E5", "F5", "G5", "H5")
            .Stones(P1, "A4", "B4", "C4", "D4", "E4", "F4", "G4", "H4", "J4", "A5", "A6", "B6", "C6", "D6", "E6", "F6", "G6", "H6", "J6");
        match.SetDeployLimit(1);
        return match;
    }

    /// <summary>
    /// 必受损局面（9×9、第 5 大回合、顺序 P0 → P3、专家 P0 的部署上限 1）：P0 的 10 子块 A1–E2 恰有 4 口气 B3 / D3 / F1 / F2（不在危险中），
    /// 四口气全是死路（P1 的 A3 / C3 / E3 / B4 / D4 / G1 / G2 / F3 围住）——P0 在哪口气上落子都只会少一口气，救不回来。
    /// 无论专家落在哪里，下一名对手 P1 的模拟回应都会紧一口气，把 10 子块打进危险（3 口气），前瞻后分数因此全部为负。
    /// </summary>
    internal static MatchFlow SqueezedPosition()
    {
        MatchFlow match = AiFixtures.Round5()
            .Stones(P0, "A1", "B1", "C1", "D1", "E1", "A2", "B2", "C2", "D2", "E2")
            .Stones(P1, "A3", "C3", "E3", "B4", "D4", "G1", "G2", "F3");
        match.SetDeployLimit(1);
        return match;
    }

    [Fact]
    public void 前瞻不引入Pass()
    {
        // 前瞻集中 3 个候选的前瞻后分数全部低于 0 → 专家落下前瞻后分数最高的一个，不 Pass。
        // 变异 M-A7d（前瞻后分数再过一次停手阈值）→ 见段 A 实施记录。
        HeuristicTurnController expert = DecideExpert(SqueezedPosition());
        LookaheadRecord record = Record(expert);

        Assert.Equal(LookaheadStatus.Applied, record.Status);
        Assert.Equal(3, record.Entries.Length);
        Assert.All(record.Entries, e => Assert.True(e.ScoreAfter < 0, record.ToText()));
        Assert.All(record.Entries, e => Assert.True(CoordsOf(e.ResponseKey!).Intersect([TestMaps.At("B3"), TestMaps.At("D3"), TestMaps.At("F1"), TestMaps.At("F2")]).Any(), e.ToString()));
        Assert.False(expert.LastChoice!.IsPass);
        Assert.Equal(record.Entries.Max(e => e.ScoreAfter), record.Entries[record.ChosenIndex].ScoreAfter);
        Assert.Equal(record.Entries[record.ChosenIndex].CandidateKey, expert.LastChoice.Key);
    }

    [Fact]
    public void 空批次不进前瞻集()
    {
        // 候选集合含 1 个空批次与 2 个非空候选 → 前瞻集只含这 2 个非空候选（按候选选择规则排序）。
        // 贪心组批里"有一个落点越过阈值，每个扰动次序都会把它加进去"，所以真实决策里空批次与非空候选不会同时出现（段 A 实施记录），
        // 本条直接对前瞻集的构造取样；空批次的供给分给到最高，确保它若混进来会排在最前。
        // 变异 M-A7c（空批次进入前瞻集）→ 见段 A 实施记录。
        var candidates = SyntheticCandidates.List(
            SyntheticCandidates.Of("E5", 3),
            SyntheticCandidates.Pass(1_000),
            SyntheticCandidates.Of("C3,D4", 5));

        var set = ExpertLookahead.LookaheadSet(candidates, 4);

        Assert.Equal(["C3:Basic,D4:Basic", "E5:Basic"], set.Select(c => c.Key));
        Assert.Equal(["C3:Basic,D4:Basic"], ExpertLookahead.LookaheadSet(candidates, 1).Select(c => c.Key));
        Assert.Empty(ExpertLookahead.LookaheadSet(SyntheticCandidates.List(SyntheticCandidates.Pass(0)), 4));
    }

    /// <summary>一枚棋子在 ≤ 1 口气时的安全维危险扣分（GroupSafety：Liberties ≤ 1 → Size × 12；ai-decision「专家难度的一层前瞻」第 6 步）。</summary>
    internal const int AtariDangerPerStone = 12;

    /// <summary>
    /// 在测试里独立复算专家一次决策的每个前瞻项：候选预演 B1 → 投影 → 模拟上下文 → 按记录里的回应预演 B2；
    /// 返回（前瞻项、候选、同一评价器对（候选落点、候选自身提子、B2）直接 Evaluate 的分解、回应提走的"决策起点就在盘上"的专家棋子数）。
    /// </summary>
    internal static List<(LookaheadEntry Entry, CandidateBatch Candidate, EvaluationBreakdown Plain, int LostAtStart)> Recompute(MatchFlow match)
    {
        StagedBatch batch = match.OpenDeploy();
        MatchPublicView view = match.Publish();
        HeuristicTurnController expert = HeuristicAi.Create(match, P0, AiDifficulty.Expert, Weights, ExpertConfig());
        expert.Deploy(batch, match.Rehearse);
        LookaheadRecord record = Record(expert);
        BatchEvaluator evaluator = expert.CreateEvaluator();
        Assert.Equal(expert.LastCandidates.Count(c => !c.IsPass), record.Entries.Length);

        var result = new List<(LookaheadEntry, CandidateBatch, EvaluationBreakdown, int)>();
        foreach (LookaheadEntry entry in record.Entries)
        {
            CandidateBatch candidate = expert.LastCandidates.Single(c => c.Key == entry.CandidateKey);
            RehearsalResult b1 = BatchRehearsal.Rehearse(match.Board, batch.Context, candidate.Placements, match.History);
            MatchPublicView projected = ExpertLookahead.Project(view, b1.ProjectedBoard!);
            BatchContext context = ExpertLookahead.SimulatedContext(projected, entry.Responder!.Value, entry.ResponderRound!.Value);
            var history = new BoardHistory();
            history.Record(view.BoardSerialized);
            history.Record(projected.BoardSerialized);
            RehearsalResult b2 = BatchRehearsal.Rehearse(
                projected.Board, context, [.. CoordsOf(entry.ResponseKey!).Select(c => new Placement(c, PieceType.Basic))], history);
            Assert.True(b2.IsLegal, b2.Failure?.Message);
            int lost = b2.Captures.Count(s => s.Owner == P0 && view.Board[s.Coord].Occupant?.Owner == P0);
            EvaluationBreakdown plain = evaluator.Evaluate(candidate.Placements, new RehearsalResult(true, false, null, b1.Captures, b2.ProjectedBoard), batch.Context);
            result.Add((entry, candidate, plain, lost));
        }

        return result;
    }

    [Fact]
    public void 两层单点不受停手阈值()
    {
        // 规格：停手阈值 20、两层权重 1000‰、下一手最佳单点增量 12 → 两层加分 12（下限 0 是裁决口径，两层单点 MUST NOT 施加停手阈值）。
        // 取样：同一局面、同一两层权重（1000‰），停手阈值 0 与 1 000 000 两个引擎给出同一个两层加分，且为正、远低于后者的阈值——
        // 若两层扫描施加阈值，后者必为 0。
        // 变异 E-T8（两层单点只计边际增量大于停手阈值者）→ 见段 A 实施记录。
        static MatchFlow Position() => AiFixtures.Round5().Stones(P0, "D5", "E5").Stones(P1, "F5", "F6").Stones(P2, "B8").Stones(P3, "H2");
        (ExpertLookahead low, MatchPublicView lowView, BatchContext lowMine, BoardHistory lowHistory) = Engine(Position(), StrengthConfig(passThreshold: 0));
        (ExpertLookahead high, MatchPublicView highView, BatchContext highMine, BoardHistory highHistory) = Engine(Position(), StrengthConfig(passThreshold: 1_000_000));

        BigInteger a = low.TwoPlyBonusOn(lowView, lowHistory, lowMine);
        BigInteger b = high.TwoPlyBonusOn(highView, highHistory, highMine);

        Assert.True(a > 0, a.ToString());
        Assert.True(a < 1_000_000, a.ToString());
        Assert.Equal(a, b);
    }

    [Fact]
    public void 两层单点受活形硬约束()
    {
        // 规格：某候选之后，专家下一手增量最高的单点不提子且会使己方一条已确定活形失去活形，次高的合法单点增量为 30 → 最高者不计入，两层加分按次高者计。
        // 取样：P0 左下活形两个单格眼 A1、C1（活形硬约束Tests.TwoEyes）；下一手单点限定在 {A1, J5}、类型倍增子，权重只留势力增量（写死：眼位与安全维为 0，
        // 否则填眼永远是负分，"最高者是拆活形的单点"这一前提无从成立）。A1 填眼：规则合法、不提子、失去活形，原始总分高于 J5（先用断言钉住）；
        // J5（P1 的 J4 / J6 之间，一口气）合法且通过活形硬约束 → 最大单点增量 = J5 的总分。
        // 变异 E-T1（两层扫描改用 Evaluate，不过活形硬约束）→ 见段 A 实施记录。
        var powerOnly = new EvaluationWeights(PowerGain: 10, EnemyLoss: 0, Relic: 0, Safety: 0, Growth: 0, Initiative: 0, Supply: 0, Eye: 0, Threat: 0);
        MatchFlow match = 活形硬约束Tests.TwoEyes().Stones(P1, "J4", "J6").Stones(P2, "B8").Stones(P3, "H8");
        (ExpertLookahead engine, MatchPublicView view, BatchContext mine, BoardHistory history) = Engine(match, StrengthConfig(), powerOnly);
        BatchContext held = mine with { Stock = new Dictionary<PieceType, int> { [PieceType.Multiplier] = 1 } };
        BatchContext next = engine.NextMoveContext(view, held)! with { LegalRange = ImmutableHashSet.Create(TestMaps.At("A1"), TestMaps.At("J5")) };

        var evaluator = new BatchEvaluator(P0, view, powerOnly, immediateOnly: false);
        ImmutableArray<Placement> fill = [new Placement(TestMaps.At("A1"), PieceType.Multiplier)];
        RehearsalResult filled = BatchRehearsal.Rehearse(view.Board, next, fill, history);
        Assert.True(filled.IsLegal, filled.Failure?.Message);
        Assert.Empty(filled.Captures);
        Assert.NotEqual(LifeState.Alive, LifeShapeReport.Analyze(filled.ProjectedBoard!).GroupLifeAt(TestMaps.At("B2"))!.Life);
        Assert.False(evaluator.TryEvaluate(fill, filled, next, out _));
        ImmutableArray<Placement> j5 = [new Placement(TestMaps.At("J5"), PieceType.Multiplier)];
        RehearsalResult other = BatchRehearsal.Rehearse(view.Board, next, j5, history);
        Assert.True(other.IsLegal, other.Failure?.Message);
        Assert.True(evaluator.TryEvaluate(j5, other, next, out EvaluationBreakdown? second));
        Assert.True(evaluator.Evaluate(fill, filled, next).Total > second.Total, $"{evaluator.Evaluate(fill, filled, next).Total} / {second.Total}");
        Assert.True(second.Total > 0);

        Assert.Equal(second.Total, engine.BestSingleGain(view, next, history));
        Assert.Equal(2, engine.TwoPlyRehearsals);
    }

    [Fact]
    public void 回应后不重判活形硬约束()
    {
        // 候选已在候选生成里通过活形硬约束；下一名对手的模拟回应提走专家的一条未定棋串 → 候选仍留在前瞻集中，前瞻后分数按九维正常计算，
        // 损失体现在即时势力增量等维度上。
        // 复算（Recompute）：同一评价器直接 Evaluate，再按裁决（2026-09-27 段 A 中）扣去被提走的决策起点己方棋子的危险分——总分须与记录相等，且即时势力增量维低于候选自身。
        // tasks 1.7 的变异"改用 TryEvaluate"在本实现里是等价变异（回应不可能破坏专家的已确定活形——预演第 6 步拒绝；合成结果带的是候选自身的提子），
        // 改用替代变异 M-A7a'（回应提走专家棋子的候选作废）→ 见段 A 实施记录。
        var items = Recompute(DoomedPosition());

        Assert.True(items.Count >= 3);
        foreach ((LookaheadEntry entry, CandidateBatch candidate, EvaluationBreakdown plain, int lost) in items)
        {
            Assert.Equal(P1, entry.Responder);
            Assert.Contains(TestMaps.At("J5"), CoordsOf(entry.ResponseKey!));
            Assert.Equal(7, lost);
            Assert.Equal(plain.Total - (Weights.Safety * AtariDangerPerStone * lost), entry.ScoreAfter);
            Assert.True(plain.RawOf(EvaluationDimension.PowerGain) < candidate.Evaluation.RawOf(EvaluationDimension.PowerGain), $"{candidate} / {plain}");
        }
    }
}
