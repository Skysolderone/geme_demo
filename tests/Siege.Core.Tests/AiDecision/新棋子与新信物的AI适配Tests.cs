using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Relics;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.AiDecision;

/// <summary>
/// 规格：more-pieces-relics ai-decision —— Requirement: 新棋子与新信物的 AI 适配（D10）。
/// AI 只经唯一的势力计算感知新棋子与连营 / 犄角；计分信物只用批次开始前已揭示的公开内容；"组合成长"维口径不变；
/// 信物估值按对局内容集的权重表与表合计归一；改造枚举把快照的工坊标记传给改造合法性唯一实现。
/// </summary>
public class 新棋子与新信物的AI适配Tests
{
    private static readonly PlayerId Me = MatchFixtures.P0;

    private static MatchOptions With(ContentSet set) => MatchOptions.Immediate with { ContentSet = set };

    [Fact]
    public void 新信物的类型价值()
    {
        // 规格 Scenario：已揭示、强度 +1 的驿站与犄角估值分别为 6 与 3（D10 初值，未校准）；工坊 4、连营 3，原六类不变。
        Assert.Equal(6, RelicEstimate.ValueOf(RelicFixtures.Relay()));
        Assert.Equal(3, RelicEstimate.ValueOf(RelicFixtures.Pincer()));
        Assert.Equal(4, RelicEstimate.ValueOf(RelicType.Workshop));
        Assert.Equal(3, RelicEstimate.ValueOf(RelicType.Encampment));
        Assert.Equal(
            [10, 8, 7, 6, 5, 4],
            new[] { RelicType.Command, RelicType.Conscription, RelicType.Vanguard, RelicType.Prospecting, RelicType.Depot, RelicType.SchoolEmblem }
                .Select(RelicEstimate.ValueOf));

        // 已揭示状态走真实内容，与内容集无关（v1 局里本就不会出现新四类，这里只钉估值口径）。
        var revealed = new RelicPublicState(TestMaps.At("B2"), new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard),
            IsRevealed: true, Content: RelicFixtures.Relay(), RevealedInMajorRound: 3, RelicControl.Uncontrolled);
        Assert.Equal(6, RelicEstimate.Estimate(revealed, ContentSet.V2));
        Assert.All(Enum.GetValues<RelicType>(), t => Assert.True(RelicEstimate.ValueOf(t) > 0));
    }

    [Fact]
    public void 未揭示信物的期望价值()
    {
        // 规格 Scenario：内容集 v2 下，出生区 ⌊5152 / 1000⌋ = 5、公共区 ⌊5692 / 1000⌋ = 5。
        // 出生区：360×4 + 160×6 + 120×5 + 64×8 + 56×10 + 40×7 + 50×(3+3+6+4) = 5152；
        // 公共区：216×4 + 108×6 + 72×5 + 108×8 + 108×10 + 108×7 + 70×(3+3+6+4) = 5692。
        // 变异 MC-E1（分母写死 100）→ v2 期望变 51 / 56，应红。
        Assert.Equal(5152, RelicEstimate.ExpectedValueScaled(RelicZone.BirthZone, ContentSet.V2));
        Assert.Equal(5692, RelicEstimate.ExpectedValueScaled(RelicZone.Contested, ContentSet.V2));
        Assert.Equal(1000, RelicEstimate.ScaleOf(ContentSet.V2));
        Assert.Equal(5, RelicEstimate.Estimate(Unrevealed(RelicZone.BirthZone), ContentSet.V2));
        Assert.Equal(5, RelicEstimate.Estimate(Unrevealed(RelicZone.Contested), ContentSet.V2));

        // 评价器取对局公开的内容集：P0 在 E4 落子唯一覆盖未揭示的公共区信物 E5 → 信物维 = V（控制）+ ⌊V/2⌋（发现）。
        // v2：5 + 2 = 7；v1：6 + 3 = 9。
        Assert.Equal(7, RelicRawCoveringE5(ContentSet.V2));
    }

    [Fact]
    public void 内容集v1的期望价值不变()
    {
        // 规格 Scenario：内容集 v1 下出生区 5、公共区 6，与引入新信物之前相同（544 / 100、635 / 100）。
        Assert.Equal(544, RelicEstimate.ExpectedValueScaled(RelicZone.BirthZone, ContentSet.V1));
        Assert.Equal(635, RelicEstimate.ExpectedValueScaled(RelicZone.Contested, ContentSet.V1));
        Assert.Equal(100, RelicEstimate.ScaleOf(ContentSet.V1));
        Assert.Equal(5, RelicEstimate.Estimate(Unrevealed(RelicZone.BirthZone), ContentSet.V1));
        Assert.Equal(6, RelicEstimate.Estimate(Unrevealed(RelicZone.Contested), ContentSet.V1));
        Assert.Equal(9, RelicRawCoveringE5(ContentSet.V1));
    }

    [Fact]
    public void 哨兵加值进入即时势力增量()
    {
        // 规格 Scenario：AI 手中只有哨兵子，两个候选落点其余条件相同——F5 经气边邻接 P1 的 E5、G5 两枚敌子，A1（角）不邻接敌子；
        // 两处都恰好新增 2 格独占空格（F5：F4 / F6；A1：A2 / B1），棋串都只有这一枚哨兵子、没有倍率 → 即时势力增量之差 = 2 × 2 = 4。
        // 差值来自唯一的势力计算：落 F5 后该棋串的哨兵加值就是 4。
        // 组合成长维口径不变（哨兵不是倍增 / 连珠 / 协同）：两处的成长原始值都是 0——变异 MC-G1（成长维计入新来源）应红。
        MatchFlow match = MatchFixtures.Started().AtRound(5, MatchFixtures.All).Stones(MatchFixtures.P1, "E5", "G5");
        match.Debug.SeedHand(Me, (PieceType.Sentry, 3));
        StagedBatch batch = match.OpenDeploy();
        BatchEvaluator evaluator = HeuristicAi.Create(match, Me).CreateEvaluator();

        RehearsalResult near = match.RehearseBatch(batch, ("F5", PieceType.Sentry));
        EvaluationBreakdown nearScore = evaluator.Evaluate(batch.Placements, near, batch.Context);
        GroupPower sentry = Assert.Single(PowerCalculator.Compute(near.ProjectedBoard!, match.Roster).Of(Me).Groups);
        RehearsalResult far = match.RehearseBatch(batch, ("A1", PieceType.Sentry));
        EvaluationBreakdown farScore = evaluator.Evaluate(batch.Placements, far, batch.Context);

        Assert.Equal(4, sentry.SentryBonus);
        Assert.Equal(7, nearScore.RawOf(EvaluationDimension.PowerGain));   // 领地 2 + 基础 1 + 哨兵 4
        Assert.Equal(3, farScore.RawOf(EvaluationDimension.PowerGain));    // 领地 2 + 基础 1
        Assert.Equal(sentry.SentryBonus, nearScore.RawOf(EvaluationDimension.PowerGain) - farScore.RawOf(EvaluationDimension.PowerGain));
        Assert.Equal(0, nearScore.RawOf(EvaluationDimension.Growth));
        Assert.Equal(0, farScore.RawOf(EvaluationDimension.Growth));
    }

    [Fact]
    public void 组合成长不计新来源与计分信物()
    {
        // Requirement 正文："组合成长"维 MUST NOT 计入四种新来源与计分信物（与势力增量双计）。
        // P0 已控制一枚已揭示的犄角（占据 H8），棋串 C3 协同 + C4 普通；候选在 C5 落铁链子。
        // 成长维（旧口径）：协同加值由 1 种 × 2 = 2 变为 2 种 × 2 = 4（不按犄角的 3 计），铁链的 +2（棋串 3 子 − 1）不计 → 原始值 2。
        // 即时势力增量经唯一势力计算感知犄角与铁链：协同 1 × 1 × 3 = 3 → 1 × 2 × 3 = 6，铁链 +2——等于独立算出的结算后势力之差。
        // 变异 MC-G1（成长维加上铁链 / 哨兵 / 旗手 / 界碑加值 → 4）、MC-G2（成长维的协同改按犄角计 → 3）应红。
        MatchFlow match = MatchFixtures.Started(relics: [("H8", RelicFixtures.Pincer())]).AtRound(5, MatchFixtures.All);
        match.Board.Place(TestMaps.At("C3"), Me, PieceType.Synergy);
        match.Board.Place(TestMaps.At("C4"), Me, PieceType.Basic);
        match.Board.Place(TestMaps.At("H8"), Me, PieceType.Basic);
        match.Relics.Reveal(match.Board, 5);
        match.Debug.Recalculate();
        match.Debug.SeedHand(Me, (PieceType.Chain, 3));
        Assert.True(match.Relics.IsRevealed(TestMaps.At("H8")));   // 前提：犄角已揭示且由 P0 控制
        Assert.Equal(3, match.Scoreboard.Latest!.GroupContaining(Me, "C3").SynergyBonus);

        StagedBatch batch = match.OpenDeploy();
        BatchEvaluator evaluator = HeuristicAi.Create(match, Me).CreateEvaluator();
        RehearsalResult result = match.RehearseBatch(batch, ("C5", PieceType.Chain));
        EvaluationBreakdown score = evaluator.Evaluate(batch.Placements, result, batch.Context);

        Assert.Equal(2, score.RawOf(EvaluationDimension.Growth));

        // 势力增量里有犄角（3 → 6）与铁链（+2）：与一份独立算出的结算后势力（已揭示的犄角 = 真实内容，结算盘面上二者一致）相减一致。
        PowerSnapshot after = PowerCalculator.Compute(result.ProjectedBoard!, match.Roster, match.Relics.TrueContents());
        GroupPower grown = after.GroupContaining(Me, "C3");
        Assert.Equal((6, 2), (grown.SynergyBonus, grown.ChainBonus));
        Assert.Equal(after.Of(Me).Total - match.Scoreboard.Latest!.Of(Me).Total, score.RawOf(EvaluationDimension.PowerGain));
    }

    [Fact]
    public void 工坊扩展的目标进入候选()
    {
        // 规格 Scenario：本小回合快照标记工坊生效、AI 持有匠人，落点 E5 隔一格（E7）是未架桥深水 → E5 的候选含"隔一格搭桥 E7"；
        // 快照未标记工坊生效时该候选不出现。目标集合只来自改造合法性唯一实现（候选里每个改造都 IsLegal(…, workshop)）。
        // 排行名额放大到 1000，免得被同分的立栅候选挤出榜外（这条钉的是"进了枚举"）。
        // 变异 MC-W1（完整枚举不传工坊标记）应红。
        MatchFlow match = MatchFixtures.Started(TestMaps.Terrain(surfaces: [("E7", Surface.DeepWater)])).AtRound(5);
        TerrainEdit bridge = TerrainEdit.Bridge(TestMaps.At("E7"));

        ImmutableArray<PointScore> with = Ranking(match, workshop: true);
        ImmutableArray<PointScore> without = Ranking(match, workshop: false);

        Assert.Contains(with, p => p.Coord == TestMaps.At("E5") && p.Edit == bridge);
        Assert.DoesNotContain(without, p => p.Edit == bridge);
        Assert.All(with.Where(p => p.Edit is not null), p => Assert.True(TerrainEditRules.IsLegal(match.Board.Map, p.Coord, p.Edit!.Value, workshop: true)));
        Assert.All(without.Where(p => p.Edit is not null), p => Assert.True(TerrainEditRules.IsLegal(match.Board.Map, p.Coord, p.Edit!.Value)));
    }

    [Fact]
    public void 未揭示的连营不影响AI评价()
    {
        // 规格 Scenario：候选批次会首次覆盖一枚尚未揭示的连营 → 该候选的即时势力增量不含连营加成；该信物只按未揭示期望价值进入信物维。
        // 做法：同一局面、同一候选（D5 连珠子与 C5 连成长 2 的线 + H4 普通子唯一覆盖未揭示的 H5），H5 分别是连营与军令——两份分解逐维相同。
        // 对照（已揭示）：H5 上已有 P0 的子、连营已揭示并由 P0 控制时，同一连珠候选的即时势力增量比军令版多 L = 2（经唯一势力计算感知）。
        // 变异 MC-A1（AI 读真实内容：公开状态不看揭示即给出内容）、MC-A2（评价器不传已揭示内容）应红。
        string Unrevealed(RelicContent content)
        {
            MatchFlow match = LineMatch(content, revealed: false);
            StagedBatch batch = match.OpenDeploy();
            BatchEvaluator evaluator = HeuristicAi.Create(match, Me).CreateEvaluator();
            RehearsalResult result = match.RehearseBatch(batch, ("D5", PieceType.Line), ("H4", PieceType.Basic));
            return evaluator.Evaluate(batch.Placements, result, batch.Context).ToString();
        }

        string encampment = Unrevealed(RelicFixtures.Encampment());
        Assert.Equal(Unrevealed(RelicFixtures.Command()), encampment);

        // 信物维只按未揭示期望：v2 公共区 5，唯一覆盖 +5、发现 +2。
        MatchFlow probe = LineMatch(RelicFixtures.Encampment(), revealed: false);
        StagedBatch probeBatch = probe.OpenDeploy();
        EvaluationBreakdown probeScore = HeuristicAi.Create(probe, Me).CreateEvaluator()
            .Evaluate(probeBatch.Placements, probe.RehearseBatch(probeBatch, ("D5", PieceType.Line), ("H4", PieceType.Basic)), probeBatch.Context);
        Assert.Equal(7, probeScore.RawOf(EvaluationDimension.Relic));

        BigInteger Revealed(RelicContent content)
        {
            MatchFlow match = LineMatch(content, revealed: true);
            StagedBatch batch = match.OpenDeploy();
            BatchEvaluator evaluator = HeuristicAi.Create(match, Me).CreateEvaluator();
            RehearsalResult result = match.RehearseBatch(batch, ("D5", PieceType.Line));
            return evaluator.Evaluate(batch.Placements, result, batch.Context).RawOf(EvaluationDimension.PowerGain);
        }

        Assert.Equal(2, Revealed(RelicFixtures.Encampment()) - Revealed(RelicFixtures.Command()));
    }

    [Fact]
    public void AI源码不另写新加值公式()
    {
        // Requirement 正文：AI MUST NOT 另写一份新棋子或计分信物的加值公式——只经唯一的势力计算（PowerCalculator）感知。
        // 源码扫描 src/Siege.Core/Ai（去掉 // 注释）：不得直接调用四项新加值、不得出现其系数常量、不得自数连营 / 犄角。
        // 反面命中：判据在唯一实现 Scoring/PieceEffects.cs 与 Scoring/PowerCalculator.cs 里确实出现；口径下界：文件数 ≥ 10。
        // 变异 MC-T2（BatchEvaluator 里加一行 PieceEffects.SentryBonus(…) 调用）应红。
        string root = PresentationFixtures.RepoRoot();
        string[] files = Directory.GetFiles(Path.Combine(root, "src", "Siege.Core", "Ai"), "*.cs", SearchOption.AllDirectories);
        Assert.True(files.Length >= 10, $"只扫到 {files.Length} 个文件");
        string[] tokens =
        [
            "BannerBonus(", "ChainBonus(", "SentryBonus(", "BoundaryBonus(",
            "BannerPerRelicCell", "SentryPerForeignStone", "BoundaryPerExclusiveCell", "ScoringRelicCounts",
        ];

        var violations = new List<string>();
        foreach (string file in files.Order(StringComparer.Ordinal))
        {
            string code = LifeShape.空区与封闭眼空间Tests.StripComments(File.ReadAllText(file));
            violations.AddRange(tokens.Where(t => code.Contains(t, StringComparison.Ordinal)).Select(t => $"{Path.GetFileName(file)}: {t}"));
        }

        Assert.Empty(violations);
        string effects = File.ReadAllText(Path.Combine(root, "src", "Siege.Core", "Scoring", "PieceEffects.cs"));
        string calculator = File.ReadAllText(Path.Combine(root, "src", "Siege.Core", "Scoring", "PowerCalculator.cs"));
        Assert.All(tokens.Take(7), t => Assert.Contains(t, effects, StringComparison.Ordinal));
        Assert.Contains("ScoringRelicCounts", calculator, StringComparison.Ordinal);
    }

    private static RelicPublicState Unrevealed(RelicZone zone) =>
        new(TestMaps.At("B2"), new RelicCellSpec(zone, zone == RelicZone.BirthZone ? BudgetTier.Birth : BudgetTier.Standard),
            IsRevealed: false, Content: null, RevealedInMajorRound: null, RelicControl.Uncontrolled);

    /// <summary>P0 在 E4 落普通子唯一覆盖未揭示的公共区军令 E5，返回该候选的信物维原始值。</summary>
    private static BigInteger RelicRawCoveringE5(ContentSet set)
    {
        MatchFlow match = MatchFixtures.Started(options: With(set), relics: [("E5", RelicFixtures.Command())]).AtRound(5, MatchFixtures.All);
        Assert.Equal(set, match.Publish().ContentSet);
        StagedBatch batch = match.OpenDeploy();
        RehearsalResult result = match.RehearseBatch(batch, ("E4", PieceType.Basic));
        return HeuristicAi.Create(match, Me).CreateEvaluator().Evaluate(batch.Placements, result, batch.Context).RawOf(EvaluationDimension.Relic);
    }

    /// <summary>
    /// v2、第 5 大回合、P0 先行：P0 有连珠子 C5，手里有连珠子与普通子；H5 是信物格。
    /// <paramref name="revealed"/> 为真时 P0 已占据 H5 且该信物已揭示（由 P0 控制）。
    /// </summary>
    private static MatchFlow LineMatch(RelicContent content, bool revealed)
    {
        MatchFlow match = MatchFixtures.Started(relics: [("H5", content)]).AtRound(5, MatchFixtures.All);
        match.Board.Place(TestMaps.At("C5"), Me, PieceType.Line);
        if (revealed)
        {
            match.Board.Place(TestMaps.At("H5"), Me, PieceType.Basic);
            match.Relics.Reveal(match.Board, 5);
        }

        match.Debug.Recalculate();
        match.Debug.SeedHand(Me, (PieceType.Line, 3), (PieceType.Basic, 3));
        Assert.Equal(revealed, match.Relics.IsRevealed(TestMaps.At("H5")));
        return match;
    }

    /// <summary>只在 E5 落匠人（部署上限 1）的单点排行；<paramref name="workshop"/> 即本小回合快照的工坊标记（经批次上下文传入）。</summary>
    private static ImmutableArray<PointScore> Ranking(MatchFlow match, bool workshop)
    {
        GameBoard board = match.Board;
        var batch = new StagedBatch(board, BatchFixtures.Context(board, Me, 1, BatchFixtures.Stock((PieceType.Artisan, 3)), new HashSet<Coord> { TestMaps.At("E5") })
            with { WorkshopActive = workshop });
        var driver = new SettlementDriver(board, match.History, new RecordingHooks { Board = board });
        var ai = new HeuristicTurnController(Me, match.Publish, match.Seed.Stream(HeuristicAi.StreamName(Me)), AiDifficulty.Standard,
            weights: null, config: AiSearchConfig.Standard with { CandidatePointCount = 1000 });
        ai.Deploy(batch, () => driver.Rehearse(batch.Context, batch.Placements));
        return ai.LastPointRanking;
    }
}
