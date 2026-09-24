using System.Numerics;
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
    /// P1 的 E5 只剩一口气 F5；P1 另有 H8、H6、A9 使 P1 势力 14（4 子 + 独占 10）高于 P0 的 10（3 子 + 独占 7）；
    /// F5 提 E5 后 P0 13 > P1 12，P0 从第 2 名升到第 1 名（段 A：领地重新计分，数值回到 territory-power 时期；逐格手数见「先手位评价生效」）。P0 手牌薄（普通子 2 + 倍增子 1 = 3 枚），第 5 大回合部署上限 4（growth-pass-1 分阶段基础值，原 3），落 1 子后剩 2 枚、下回合缺 2 枚，供给维 −2（原 −1）。
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
    public void 评价覆盖九个维度()
    {
        // 设计文档 §15.2 九个维度（ai-eye 段 A 由「评价覆盖七个维度」改名：规格 Scenario 改为九维）。
        // F5 落倍增子：势力增量、敌方损失与提子、信物控制、安全度、组合成长、先手位、供给匹配全部非零；
        // 眼位 / 威胁两维在本局面的取值由各自的 Scenario 钉（本局面无眼空间变化，不在这里要求非零）。
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
        Assert.Equal(9, EvaluationBreakdown.DimensionCount);
        Assert.Equal(9, Enum.GetValues<EvaluationDimension>().Length);
        Assert.Equal(EvaluationBreakdown.DimensionCount, e.Raw.Length);
        Assert.Contains("眼位", e.ToString(), StringComparison.Ordinal);
        Assert.Contains("威胁", e.ToString(), StringComparison.Ordinal);
        Assert.True(e.RawOf(EvaluationDimension.PowerGain) > 0, e.ToString());
        Assert.True(e.RawOf(EvaluationDimension.EnemyLoss) > 0, e.ToString());
        Assert.True(e.RawOf(EvaluationDimension.Relic) > 0, e.ToString());
        Assert.NotEqual(0, e.RawOf(EvaluationDimension.Safety));
        Assert.True(e.RawOf(EvaluationDimension.Growth) > 0, e.ToString());
        Assert.True(e.RawOf(EvaluationDimension.Initiative) > 0, e.ToString());
        Assert.Equal(-2, e.RawOf(EvaluationDimension.Supply));   // −max(0, 部署上限 4 − 剩余 2)

        BigInteger sum = 0;
        foreach (EvaluationDimension d in Enum.GetValues<EvaluationDimension>())
        {
            Assert.Equal(EvaluationWeights.Default.Of(d) * e.RawOf(d), e.ContributionOf(d));
            sum += e.ContributionOf(d);
        }

        Assert.Equal(sum, e.Total);

        // 敌损 = 参赛敌方势力下降 + 提子数 × 2。段 A 重算：原期望下降 1 → 2 = P1 失去 E5 的军势 1 + 失去独占格 F5 的领地 1（F5 被 P0 落子占据），提 1 子。
        BigInteger p1Drop = evaluator.Before.Of(AiFixtures.P1).Total - PowerCalculator.Compute(result.ProjectedBoard!, match.Roster).Of(AiFixtures.P1).Total;
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

        var onlyEnemyLoss = new EvaluationWeights(PowerGain: 0, EnemyLoss: 10, Relic: 0, Safety: 0, Growth: 0, Initiative: 0, Supply: 0, Eye: 0, Threat: 0);
        var onlyGrowth = new EvaluationWeights(PowerGain: 0, EnemyLoss: 0, Relic: 0, Safety: 0, Growth: 10, Initiative: 0, Supply: 0, Eye: 0, Threat: 0);

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
        // 局面见 CaptureRelicPosition：P1 14 > P0 10；F5 提 E5 后 P0 13 > P1 12，P0 从第 2 名升到第 1 名，先手值 +1。
        // 段 A 重构（领地重新计分）：scoring-sites 为"独占空格不计分"给 P1 补过一子 J1（P1 5 > P0 3、提后 4 = 4）；领地计分后 J1 会让 P1 17 → 提后 15 > P0 13、名次不变，
        // 前提失效，故去掉 J1、回到 territory-power 时期的原局面与原数值（9×9 平地图，逐格手数）：
        //   提前 P0 10 = 3 子 + 独占 7（C5 / D4 / D6 / F4 / E3 / F6 / E7）；P1 14 = 4 子 + 独占 10（F5；G8 / J8 / H7 / H9 / G6 / J6 / H5；B9 / A8）。
        //   提后 P0 13 = 4 子 + 独占 9（原 7 + 空出的 E5 + 新邻格 G5）；P1 12 = 3 子 + 独占 9（失去 F5）。
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

        // 不改变名次的落点（D4：+1 子 +2 独占格 C4 / D3 −1 原独占格 D4 = 12 < 14）：该维度为 0
        RehearsalResult quiet = match.RehearseBatch(batch, ("D4", PieceType.Basic));
        Assert.Equal(0, ai.CreateEvaluator().Evaluate(batch.Placements, quiet, batch.Context).RawOf(EvaluationDimension.Initiative));
    }

    [Fact]
    public void 两眼潜力取自活形查询()
    {
        // ai-eye 段 A（1.4）由「两眼潜力近似区分活形与死形」改写：GroupSafety 自带的"单点气"眼点循环已删除，
        // 两眼潜力 = min(2, 眼值之和)，眼值之和与活形状态只来自 LifeShapeReport；已确定活形的安全分恒为公式上界。
        // 活形（角部，两个单格眼 A1、C1）：          直三（同形去掉 B1，A1-B1-C1 是一块 3 格眼空间）：
        //  3 . . . . .                                3 . . . . .
        //  2 O O O O .                                2 O O O O .
        //  1 . O . O .                                1 . . . O .
        //    A B C D E                                  A B C D E
        // 直三的眼值是 1（life-shape 眼值表），旧的单点气循环在这里给 0——这正是两套眼判定分歧之处。
        // 变异验证 M-C2（check，段 A 重跑）：Score 里 TwoEyePotential × 6 改 × 0 → 红 2（本测试：直三 10 → 4；候选格上限的黄金哈希）。
        GameBoard living = TestMaps.Blank(9);
        foreach (string s in new[] { "B1", "A2", "B2", "C2", "D1", "D2" })
        {
            living.Place(s, TestMaps.P0);
        }

        GameBoard straightThree = TestMaps.Blank(9);
        foreach (string s in new[] { "A2", "B2", "C2", "D1", "D2" })
        {
            straightThree.Place(s, TestMaps.P0);
        }

        GroupSafety alive = GroupSafety.Analyze(living, LifeShapeReport.Analyze(living).GroupLifeAt(TestMaps.At("B2"))!);
        GroupSafety weak = GroupSafety.Analyze(straightThree, LifeShapeReport.Analyze(straightThree).GroupLifeAt(TestMaps.At("B2"))!);

        Assert.True(alive.IsAlive);
        Assert.Equal(2, alive.EyeValueSum);
        Assert.Equal(2, alive.TwoEyePotential);
        Assert.Equal(8, alive.Liberties);
        Assert.Equal(4 + (2 * 6) + 2, GroupSafety.AliveScore);   // 公式上界：气数封顶 4 + 两眼潜力 2 × 6 + 分散气封顶 2
        Assert.Equal(18, alive.Score);

        Assert.False(weak.IsAlive);
        Assert.Equal(1, weak.EyeValueSum);
        Assert.Equal(1, weak.TwoEyePotential);
        Assert.Equal(9, weak.Liberties);
        Assert.Equal(0, weak.DispersedLiberties);   // A1-B1-C1、A3-D3、E1-E2 三段气各自相邻
        Assert.Equal(4 + (1 * 6) + 0 - 0, weak.Score);
    }

    // ---------- 眼位（ai-eye 1.2）：己方眼值之和（每个眼空间只计一次）+ 己方已确定活形棋串数 × 3，取前后增量 ----------
    // 算例全部在 9×9 平地图的左下角手算（行号自下而上），眼空间 / 活形只经 LifeShapeReport 取前提，不经被测的评价器。

    [Fact]
    public void 做出第二个眼获得眼位正贡献()
    {
        //  3 . . . . .
        //  2 O O O O .     A1 已是单格眼（邻格 A2、B1）；C1 的邻格 B1、C2 是 P0，D1 空 → C1 与右侧空地连成大空区，不是眼空间。
        //  1 . O . ! .     ! = 本批次 D1：C1 封闭为第二个单格眼，棋串眼值 1 → 2、成为已确定活形。
        //    A B C D E
        // 原始值 = 眼值增量 1 + 活形数增量 1 × 3 = 4。ai-eye R26 起简单难度也算眼位（D7）：同一批次同为 4（原为 0，段 D2 改写）。
        MatchFlow Position() => AiFixtures.Round5().Stones(AiFixtures.P0, "B1", "A2", "B2", "C2", "D2");
        MatchFlow match = Position();
        LifeShapeReport before = LifeShapeReport.Analyze(match.Board);
        Assert.Equal(["A1"], OwnEyeCells(before, AiFixtures.P0));
        Assert.Equal(LifeState.Undetermined, before.GroupLifeAt(TestMaps.At("B2"))!.Life);

        (EvaluationBreakdown e, RehearsalResult result) = EvaluationFixtures.EvaluateP0(match, "D1");

        LifeShapeReport after = LifeShapeReport.Analyze(result.ProjectedBoard!);
        Assert.Equal(["A1", "C1"], OwnEyeCells(after, AiFixtures.P0));
        Assert.Equal(LifeState.Alive, after.GroupLifeAt(TestMaps.At("B2"))!.Life);
        Assert.Equal(4, e.RawOf(EvaluationDimension.Eye));

        (EvaluationBreakdown easy, _) = EvaluationFixtures.EvaluateP0(Position(), AiDifficulty.Easy, "D1");
        Assert.Equal(4, easy.RawOf(EvaluationDimension.Eye));
    }

    [Fact]
    public void 自填眼位不得分()
    {
        // 基准文档的警告：以绝对值计眼位，"眼变棋子"也会得分。两个算例都不提子：
        //   ① 未定棋串（只有单格眼 A1）填 A1：眼值 1 → 0，原始值 −1；
        //   ② 活形（单格眼 A1、C1）填 A1：眼值 2 → 1、活形 1 → 0，原始值 −1 − 3 = −4。
        // 变异 M-A1：眼位改取结算后的绝对值 → 红 4（本测试 ① 得 0、② 得 +1，及做出第二个眼 / 点直三中间两条）。
        MatchFlow undetermined = AiFixtures.Round5().Stones(AiFixtures.P0, "B1", "A2", "B2", "C2", "D2");
        Assert.Equal(LifeState.Undetermined, LifeShapeReport.Analyze(undetermined.Board).GroupLifeAt(TestMaps.At("B2"))!.Life);
        (EvaluationBreakdown one, RehearsalResult r1) = EvaluationFixtures.EvaluateP0(undetermined, "A1");
        Assert.Empty(r1.Captures);
        Assert.Empty(OwnEyeCells(LifeShapeReport.Analyze(r1.ProjectedBoard!), AiFixtures.P0));
        Assert.Equal(-1, one.RawOf(EvaluationDimension.Eye));

        MatchFlow alive = AiFixtures.Round5().Stones(AiFixtures.P0, "B1", "A2", "B2", "C2", "D1", "D2");
        Assert.Equal(LifeState.Alive, LifeShapeReport.Analyze(alive.Board).GroupLifeAt(TestMaps.At("B2"))!.Life);
        (EvaluationBreakdown two, RehearsalResult r2) = EvaluationFixtures.EvaluateP0(alive, "A1");
        Assert.Empty(r2.Captures);
        Assert.Equal(LifeState.Undetermined, LifeShapeReport.Analyze(r2.ProjectedBoard!).GroupLifeAt(TestMaps.At("B2"))!.Life);
        Assert.Equal(-4, two.RawOf(EvaluationDimension.Eye));
    }

    [Fact]
    public void 点直三中间眼位增量为眼值1加活形3()
    {
        //  3 . . . . .
        //  2 O O O O .     唯一的眼空间是直三 A1-B1-C1（眼值 1，未定）。
        //  1 . ! . O .     ! = 本批次 B1：分成 A1、C1 两个单格眼，眼值 1 → 2，棋串成为已确定活形。
        //    A B C D E
        // 原始值 = 眼值增量 1 + 活形数增量 1 × 3 = 4。
        MatchFlow match = AiFixtures.Round5().Stones(AiFixtures.P0, "A2", "B2", "C2", "D2", "D1");
        LifeShapeReport before = LifeShapeReport.Analyze(match.Board);
        Assert.Equal(["A1", "B1", "C1"], OwnEyeCells(before, AiFixtures.P0));
        Assert.Equal(LifeState.Undetermined, before.GroupLifeAt(TestMaps.At("B2"))!.Life);

        (EvaluationBreakdown e, RehearsalResult result) = EvaluationFixtures.EvaluateP0(match, "B1");

        Assert.Equal(LifeState.Alive, LifeShapeReport.Analyze(result.ProjectedBoard!).GroupLifeAt(TestMaps.At("B2"))!.Life);
        Assert.Equal(4, e.RawOf(EvaluationDimension.Eye));
    }

    [Fact]
    public void 已活时点直三中间眼位增量只有眼值1()
    {
        //  3 . . . . . . . .
        //  2 O O O O O O O .     直三 A1-B1-C1（眼值 1）+ 单格眼 F1（眼值 1）= 2，棋串已是活形。
        //  1 . ! . O O . O .     ! = 本批次 B1：眼值 2 → 3，活形数不变（仍是同一条活棋）。
        //    A B C D E F G H
        // 原始值 = 眼值增量 1 + 活形数增量 0 × 3 = 1（与上一条对照："未成活形"时只有眼值增量）。
        MatchFlow match = AiFixtures.Round5().Stones(AiFixtures.P0, "A2", "B2", "C2", "D2", "E2", "F2", "G2", "D1", "E1", "G1");
        LifeShapeReport before = LifeShapeReport.Analyze(match.Board);
        Assert.Equal(["A1", "B1", "C1", "F1"], OwnEyeCells(before, AiFixtures.P0));
        Assert.Equal(LifeState.Alive, before.GroupLifeAt(TestMaps.At("B2"))!.Life);
        Assert.Single(match.Board.GroupsOf(AiFixtures.P0));

        (EvaluationBreakdown e, RehearsalResult result) = EvaluationFixtures.EvaluateP0(match, "B1");

        Assert.Equal(3, LifeShapeReport.Analyze(result.ProjectedBoard!).GroupLifeAt(TestMaps.At("B2"))!.EyeValueSum);
        Assert.Equal(1, e.RawOf(EvaluationDimension.Eye));
    }

    [Fact]
    public void 共享眼空间只计一次()
    {
        //  3 . . .
        //  2 O . .     P0 只有 A2 一子；本批次 B1（!）：A1 的邻格 A2、B1 全为 P0 → {A1} 成为对 P0 封闭的单格眼空间，
        //  1 . ! .     同时是 {A2}、{B1} 两条互不相连的棋串的眼空间（life-shape「多条棋串共享眼空间」）。
        //    A B C
        // design D1："每个眼空间只计一次，不按相邻棋串重复计"——原始值 = 1，不是按棋串求和的 2；两条棋串各自眼值 1、都是未定，×3 项为 0。
        // 变异 M-A7：EyeOf 改为对己方棋串的 EyeValueSum 求和 → 红 1（本测试，得 2）。
        MatchFlow match = AiFixtures.Round5().Stones(AiFixtures.P0, "A2");
        Assert.Empty(OwnEyeCells(LifeShapeReport.Analyze(match.Board), AiFixtures.P0));

        (EvaluationBreakdown e, RehearsalResult result) = EvaluationFixtures.EvaluateP0(match, "B1");

        LifeShapeReport after = LifeShapeReport.Analyze(result.ProjectedBoard!);
        Assert.Equal(["A1"], OwnEyeCells(after, AiFixtures.P0));
        Assert.Equal(2, after.EyeSpaceAt(TestMaps.At("A1"))!.GroupIndices.Length);   // 样本口径：确实被两条棋串共享
        Assert.Equal(LifeState.Undetermined, after.GroupLifeAt(TestMaps.At("A2"))!.Life);
        Assert.Equal(LifeState.Undetermined, after.GroupLifeAt(TestMaps.At("B1"))!.Life);
        Assert.Equal(1, e.RawOf(EvaluationDimension.Eye));
    }

    // ---------- 威胁（ai-eye 1.3）：敌方非已确定活形、气数 ≤ 危险气数 3 的棋串棋子总数，取前后增量 ----------

    [Fact]
    public void 叫吃获得威胁正贡献()
    {
        //  6 O O O ! ! .     P1 的 5 子棋串 A5-E5，P0 从上下夹住；批次前气为 D6、E6、E4、F5 = 4 口（不在危险气数内）。
        //  5 X X X X X .     ! = 本批次 D6、E6：气降到 E4、F5 = 2 口 → 进入危险气数，且该棋串没有眼、不是活形。
        //  4 O O O O . .     原始值 = 5 − 0 = +5。
        //    A B C D E F
        MatchFlow match = AiFixtures.Round5()
            .Stones(AiFixtures.P1, "A5", "B5", "C5", "D5", "E5")
            .Stones(AiFixtures.P0, "A4", "B4", "C4", "D4", "A6", "B6", "C6");
        Group enemy = match.Board.GroupAt(TestMaps.At("C5"))!;
        Assert.Equal(5, enemy.Size);
        Assert.Equal(4, match.Board.LibertiesOf(enemy).Length);
        Assert.NotEqual(LifeState.Alive, LifeShapeReport.Analyze(match.Board).GroupLifeAt(TestMaps.At("C5"))!.Life);

        (EvaluationBreakdown e, RehearsalResult result) = EvaluationFixtures.EvaluateP0(match, "D6", "E6");

        GameBoard after = result.ProjectedBoard!;
        Assert.Empty(result.Captures);
        Assert.Equal(["E4", "F5"], after.LibertiesOf(after.GroupAt(TestMaps.At("C5"))!).Notations());
        Assert.Equal(5, e.RawOf(EvaluationDimension.Threat));
    }

    // ---------- 眼判定只有 life-shape 一处（ai-eye 1.4 守门） ----------

    [Fact]
    public void AI不自带眼判定只经活形查询()
    {
        // 规格「启发式评价维度」：眼、眼空间、眼值与活形状态 MUST 只来自 life-shape「活形查询」，AI MUST NOT 自带第二套眼判定。
        // 眼判定的形状是"枚举某格的邻格，看它们是不是己方棋子"。源码扫描（去注释后）：
        //   ① Siege.Core/Ai 下每一处邻格枚举（*Neighbors( / CoverageTargets(），从调用点到所在块结束，不得读取占用者 / 所有者；
        //   ② 不得出现 EyePoint / IsEye 一类自带眼判定的标识符。
        // 反面：扫描器必须认出段 A 之前 GroupSafety 的眼点循环与一行 LINQ 写法（否则①是空转）；扫描口径下界：文件数 ≥ 10、邻格枚举 ≥ 1 处。
        // 变异 M-A4：GroupSafety 分散气循环里插入 `_ = board[n].Occupant is { } o && o.Owner == life.Group.Owner;` → 红 1（本测试）。
        // 变异 M-A4b：BatchEvaluator.SafetyOf 里插入一行 LINQ 眼判定（LibertyNeighbors(c).All(n => …Occupant…Owner == _me)）→ 红 1（本测试）。
        string aiDir = Path.Combine(PresentationFixtures.RepoRoot(), "src", "Siege.Core", "Ai");
        string[] files = Directory.GetFiles(aiDir, "*.cs", SearchOption.AllDirectories);
        Assert.True(files.Length >= 10, $"只扫到 {files.Length} 个文件");

        var violations = new List<string>();
        int neighborCalls = 0;
        foreach (string file in files.Order(StringComparer.Ordinal))
        {
            (List<string> found, int calls) = EyeJudgmentScan(File.ReadAllText(file));
            neighborCalls += calls;
            violations.AddRange(found.Select(v => $"{Path.GetFileName(file)}: {v}"));
        }

        Assert.Empty(violations);
        Assert.True(neighborCalls >= 1, "扫描器一处邻格枚举都没命中——扫描口径失效");
        // 段 C：活形查询改经决策内缓存（缺省查询是方法组 LifeShapeReport.Analyze，不再带括号直接调用），反面命中去掉 "("。
        Assert.Contains("LifeShapeReport.Analyze", File.ReadAllText(Path.Combine(aiDir, "BatchEvaluator.cs")), StringComparison.Ordinal);

        Assert.NotEmpty(EyeJudgmentScan(LegacyEyeLoop).Violations);
        Assert.NotEmpty(EyeJudgmentScan(LinqEyeCheck).Violations);
    }

    /// <summary>段 A 之前 <c>GroupSafety.Analyze</c> 的眼点循环原文（扫描器的反面样本）。</summary>
    private const string LegacyEyeLoop = """
        public static GroupSafety Analyze(GameBoard board, Group group)
        {
            var liberties = board.LibertiesOf(group).ToHashSet();
            foreach (Coord liberty in liberties)
            {
                bool isEye = true;
                foreach (Coord n in board.LibertyNeighbors(liberty))
                {
                    Cell cell = board[n];
                    bool ownWall = cell.Occupant is { } o && o.Owner == group.Owner;
                    if (!ownWall)
                    {
                        isEye = false;
                    }
                }
            }
        }
        """;

    /// <summary>一行 LINQ 写法的眼判定（反面样本：没有 foreach、标识符里也不带 eye）。</summary>
    private const string LinqEyeCheck = """
        internal sealed class Probe
        {
            private int Count(GameBoard board, PlayerId me) =>
                board.AllCoords().Count(c => board[c].IsPlayableEmpty && board.LibertyNeighbors(c).All(n => board[n].Occupant is { } o && o.Owner == me));
        }
        """;

    private static readonly System.Text.RegularExpressions.Regex NeighborCall =
        new(@"(?i)\w*(neighbors|coveragetargets)\s*\(", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static readonly System.Text.RegularExpressions.Regex OwnerRead =
        new(@"(?i)\w*(occupant|owner)\w*", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static readonly System.Text.RegularExpressions.Regex EyeIdentifier =
        new(@"(?i)\w*(eyepoint|iseye)\w*", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>对一份源码做眼判定扫描：返回违规描述与邻格枚举调用数。先去掉 <c>//</c> 注释（含 XML 文档注释）。</summary>
    private static (List<string> Violations, int NeighborCalls) EyeJudgmentScan(string source)
    {
        string code = System.Text.RegularExpressions.Regex.Replace(source, "//[^\n]*", string.Empty);
        var violations = new List<string>();
        foreach (System.Text.RegularExpressions.Match m in EyeIdentifier.Matches(code))
        {
            violations.Add($"自带眼判定标识符 {m.Value}");
        }

        System.Text.RegularExpressions.MatchCollection calls = NeighborCall.Matches(code);
        foreach (System.Text.RegularExpressions.Match call in calls)
        {
            // 调用点所在的最内层块：向前找未配对的 '{'，再向后找与之配对的 '}'。foreach 头里的调用，所在块含整个循环体。
            int depth = 0;
            int open = -1;
            for (int i = call.Index - 1; i >= 0; i--)
            {
                if (code[i] == '}')
                {
                    depth++;
                }
                else if (code[i] == '{')
                {
                    if (depth == 0)
                    {
                        open = i;
                        break;
                    }

                    depth--;
                }
            }

            int close = code.Length;
            depth = 0;
            for (int i = open + 1; i < code.Length; i++)
            {
                if (code[i] == '{')
                {
                    depth++;
                }
                else if (code[i] == '}')
                {
                    if (depth == 0)
                    {
                        close = i;
                        break;
                    }

                    depth--;
                }
            }

            string scope = code[call.Index..close];
            if (OwnerRead.Match(scope) is { Success: true } read)
            {
                violations.Add($"邻格枚举 {call.Value} 之后读取了 {read.Value}");
            }
        }

        return (violations, calls.Count);
    }

    /// <summary>某玩家全部眼空间的格（每个眼空间只列一次），按坐标序。</summary>
    private static string[] OwnEyeCells(LifeShapeReport report, PlayerId player) =>
        report.EyeSpaces.Where(s => s.Owner == player).SelectMany(s => s.Cells).Order().Notations();

    [Fact]
    public void 九维权重经配置往返且旧配置缺两维按0读入()
    {
        // ai-eye 1.1：权重表扩为九维。往返必须用"非回填值"证伪（testing.md「带回填兜底的字段」）——
        // 缺字段按 0 读入，写入路径若漏写 Eye / Threat，用 0 往返是恒真的，所以这里写 5 / 7。
        EvaluationWeights nine = EvaluationWeights.Default with { Eye = 5, Threat = 7 };
        Siege.Sim.Config.RunConfig config = SimFixtures.Config() with
        {
            Players = [.. SimFixtures.Config().Players.Select(p => p with { Weights = nine })],
        };
        Siege.Sim.Config.RunConfig back = Siege.Sim.Config.RunConfig.FromJson(config.ToJson());
        Assert.All(back.Players, p => Assert.Equal(nine, p.Weights));
        Assert.Equal(5, back.Players[0].Weights!.Of(EvaluationDimension.Eye));
        Assert.Equal(7, back.Players[0].Weights!.Of(EvaluationDimension.Threat));

        // 旧配置（ai-eye 之前的 config.json / 日志首部）只有七个键：两维按 0 读入 = 权重为 0 的九维，决策与七维实现逐步相同。
        System.Text.Json.Nodes.JsonNode node = System.Text.Json.Nodes.JsonNode.Parse(config.ToJson())!;
        foreach (System.Text.Json.Nodes.JsonNode? player in node["Players"]!.AsArray())
        {
            System.Text.Json.Nodes.JsonObject weights = player!["Weights"]!.AsObject();
            Assert.True(weights.Remove("Eye") && weights.Remove("Threat"));
        }

        string legacy = node.ToJsonString();
        Assert.DoesNotContain("\"Eye\"", legacy, StringComparison.Ordinal);
        Assert.Contains("\"Supply\"", legacy, StringComparison.Ordinal);
        Siege.Sim.Config.RunConfig old = Siege.Sim.Config.RunConfig.FromJson(legacy);
        Assert.All(old.Players, p => Assert.Equal(EvaluationWeights.Default with { Eye = 0, Threat = 0 }, p.Weights));
    }

    [Fact]
    public void 日志首部写入AI评价版本且分解值为九项()
    {
        // ai-eye 1.1：首部记录 AI 评价版本（design Risks「回放兼容」）；落子事件的分解值为九项。
        // Standard 难度：Easy 的非即时维恒 0，钉不住"分解值确实写的是这一维"。
        Siege.Sim.Logging.MatchLog log = Siege.Sim.Running.MatchSession
            .Create(SimFixtures.Config(turnLimit: 8, difficulty: AiDifficulty.Standard), 41).Run();
        // 段 C（裁决 R10）：硬约束 + 阈值 + 预筛口径使走法变化，版本 2 → 3。
        Assert.Equal(3, EvaluationBreakdown.Version);
        Assert.Equal(EvaluationBreakdown.Version, log.Header.AiEvaluationVersion);

        // 往返：写出再读回仍在（写入路径漏写则读回 null）。
        Siege.Sim.Logging.MatchLog parsed = Siege.Sim.Logging.MatchLog.Parse(log.FullText());
        Assert.Equal(EvaluationBreakdown.Version, parsed.Header.AiEvaluationVersion);

        // 旧日志首部没有该字段 → null（版本 1、七维），MUST NOT 回填。
        string[] lines = log.FullText().Split('\n');
        System.Text.Json.Nodes.JsonNode header = System.Text.Json.Nodes.JsonNode.Parse(lines[0])!;
        Assert.True(header.AsObject().Remove("AiEvaluationVersion"));
        lines[0] = header.ToJsonString();
        Assert.Null(Siege.Sim.Logging.MatchLog.Parse(string.Join('\n', lines)).Header.AiEvaluationVersion);

        // 分解值：每条落子事件带九个维度名（样本下界：至少有一条落子事件）。
        List<Siege.Sim.Logging.LogEvent> settled = [.. log.Events.Where(e => e.Type == Siege.Sim.Logging.LogEventType.Settled)];
        Assert.NotEmpty(settled);
        Assert.All(settled, e => Assert.All(Enum.GetNames<EvaluationDimension>(), name => Assert.True(e.Values!.ContainsKey(name), name)));
        Assert.Contains(nameof(EvaluationDimension.Eye), Enum.GetNames<EvaluationDimension>());
        Assert.Contains(nameof(EvaluationDimension.Threat), Enum.GetNames<EvaluationDimension>());
    }

    [Fact]
    public void 旧评价版本的日志回放按首部版本处理()
    {
        // 裁决 R10 / design Risks「回放兼容」：首部版本与当前不同（含缺字段 = 版本 1）→ 停在首部报分歧、不重跑 AI；同版本照常逐步回放。
        // 样本：当前二进制跑出的真实日志，只把首部版本改成 2 / 删掉——对局内容其实可重现，所以"没停下"就会回放一致，变异可见。
        // 变异 M-C7（去掉版本检查）→ 红 1（本测试）。变异 M-C8（缺字段当作当前版本：只比有值的版本）→ 红 1（本测试的 null 一轮）。
        Siege.Sim.Logging.MatchLog log = Siege.Sim.Running.MatchSession
            .Create(SimFixtures.Config(turnLimit: 8, difficulty: AiDifficulty.Standard), 41).Run();
        Assert.Equal(EvaluationBreakdown.Version, log.Header.AiEvaluationVersion);
        Assert.True(log.Turns.Count >= 8, $"小回合 {log.Turns.Count}");

        // 反面：同版本逐步一致。
        Siege.Sim.Running.ReplayResult same = Siege.Sim.Running.Replayer.Replay(log);
        Assert.True(same.Identical, same.ToString());
        Assert.Null(same.AiVersionMismatch);

        foreach (int? old in new int?[] { 2, null })
        {
            Siege.Sim.Logging.MatchLog legacy = new()
            {
                Header = log.Header with { AiEvaluationVersion = old },
                Turns = log.Turns,
                Events = log.Events,
                Result = log.Result,
            };
            Assert.Equal(old, Siege.Sim.Logging.MatchLog.Parse(legacy.DeterministicText()).Header.AiEvaluationVersion);

            Siege.Sim.Running.ReplayResult replay = Siege.Sim.Running.Replayer.Replay(legacy);

            Assert.False(replay.Identical);
            Assert.Equal(1, replay.FirstDivergentLine);
            Assert.NotNull(replay.AiVersionMismatch);
            Assert.Null(replay.MapMismatch);
            Assert.Contains($"当前为 {EvaluationBreakdown.Version}", replay.AiVersionMismatch, StringComparison.Ordinal);
            Assert.Empty(replay.Replayed.Turns);   // 没有重跑对局
            Assert.Equal(EvaluationBreakdown.Version, replay.Replayed.Header.AiEvaluationVersion);
        }
    }
}
