using System.Collections.Immutable;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;

namespace Siege.Core.Tests.AiDecision;

/// <summary>规格：ai-decision —— Requirement: 活形硬约束（ai-eye 段 B，2.1 / 2.2；design D3）</summary>
/// <remarks>
/// 每条都先用断言钉住前提：被测候选在<b>规则层合法</b>（Core 的活棋禁入 / 破坏活形只管非己方，己方填眼、己方立栅拆活形都合法——
/// 这正是本约束存在的理由）、且不提子。否则"不在排行里"可能只是预演不合法，测的不是本约束。
/// 排行一律把 N 开到覆盖全部合法候选（<see cref="Wide"/>）：被淘汰的候选若以扣分形式留下，就必然出现在排行里。
/// </remarks>
public class 活形硬约束Tests
{
    private static readonly PlayerId Me = AiFixtures.P0;

    /// <summary>N 覆盖全部候选、阈值 0（与停手阈值隔离）。</summary>
    internal static AiSearchConfig Wide(AiDifficulty difficulty) =>
        AiSearchConfig.ForDifficulty(difficulty) with { CandidatePointCount = 1000, PassThreshold = 0 };

    /// <summary>对 <paramref name="match"/> 的权威盘面建一个 P0 批次（库存与合法范围可控），配套结算驱动。</summary>
    internal static (StagedBatch Batch, SettlementDriver Driver) Staging(
        MatchFlow match, PieceType type = PieceType.Basic, int limit = 2, params string[] range)
    {
        GameBoard board = match.Board;
        IReadOnlySet<Coord>? legal = range.Length == 0 ? null : range.Select(TestMaps.At).ToHashSet();
        var batch = new StagedBatch(board, BatchFixtures.Context(board, Me, limit, BatchFixtures.Stock((type, 5)), legal));
        return (batch, new SettlementDriver(board, match.History, new RecordingHooks { Board = board }));
    }

    internal static HeuristicTurnController Deploy(MatchFlow match, AiDifficulty difficulty, StagedBatch batch, SettlementDriver driver)
    {
        var ai = new HeuristicTurnController(Me, match.Publish, match.Seed.Stream(HeuristicAi.StreamName(Me)), difficulty, config: Wide(difficulty));
        ai.Deploy(batch, () => driver.Rehearse(batch.Context, batch.Placements));
        return ai;
    }

    /// <summary>单独预演一个批次（不经 AI），返回结果并清空批次。</summary>
    internal static RehearsalResult Rehearse(StagedBatch batch, SettlementDriver driver, params Placement[] placements)
    {
        batch.Clear();
        foreach (Placement p in placements)
        {
            Assert.Null(batch.Stage(p.Coord, p.Type, p.Edit));
        }

        RehearsalResult result = driver.Rehearse(batch.Context, batch.Placements);
        batch.Clear();
        return result;
    }

    /// <summary>盘面上全部空的可落子格（坐标序）。</summary>
    private static ImmutableArray<Coord> Empties(MatchFlow match) =>
        [.. match.Board.AllCoords().Where(c => match.Board[c].IsPlayableEmpty).Order()];

    private static ImmutableArray<Coord> RankedCells(HeuristicTurnController ai) =>
        [.. ai.LastPointRanking.Select(p => p.Coord).Distinct().Order()];

    /// <summary>
    /// P0 左下角活形，两个单格眼 A1、C1：
    ///  2 O O O O
    ///  1 . O . O
    ///    A B C D
    /// </summary>
    internal static MatchFlow TwoEyes() => AiFixtures.Round5().Stones(Me, "B1", "A2", "B2", "C2", "D1", "D2");

    [Fact]
    public void 不拆自己的活形()
    {
        // 落 A1（不提子）→ 该棋串眼值 2 → 1，不再是已确定活形。A1 同时是单格眼，两个条件都成立；
        // 只满足"单格眼"的情形见「不填自己的单格眼」，只满足"失去活形"的情形见「用改造拆自己的活形也被淘汰」。
        MatchFlow match = TwoEyes();
        LifeShapeReport before = LifeShapeReport.Analyze(match.Board);
        Assert.Equal(LifeState.Alive, before.GroupLifeAt(TestMaps.At("B2"))!.Life);
        (StagedBatch batch, SettlementDriver driver) = Staging(match);

        RehearsalResult fill = Rehearse(batch, driver, BatchFixtures.P("A1"));
        Assert.True(fill.IsLegal, fill.Failure?.Message);
        Assert.Empty(fill.Captures);
        Assert.NotEqual(LifeState.Alive, LifeShapeReport.Analyze(fill.ProjectedBoard!).GroupLifeAt(TestMaps.At("B2"))!.Life);

        HeuristicTurnController ai = Deploy(match, AiDifficulty.Standard, batch, driver);

        // 排行 = 全部空格去掉两个单格眼：其余合法候选一个不少（N 开满），被淘汰的两格一个不在。
        // 变异 M-B1（淘汰改为扣分：违例候选照常打分、PowerGain 原始值减 10^9 留在候选集）→ 红 6（本类除「点三格眼空间的中间不被淘汰」外 5 条 + 简单难度也不拆自己的活形）。
        Assert.Equal([.. Empties(match).Where(c => c != TestMaps.At("A1") && c != TestMaps.At("C1"))], RankedCells(ai));
        Assert.DoesNotContain(ai.LastChoice!.Placements, p => p.Coord == TestMaps.At("A1") || p.Coord == TestMaps.At("C1"));
    }

    [Fact]
    public void 不填自己的单格眼()
    {
        // P0 棋串只有一个单格眼 A1（C1 与 D1、E1…… 相连通向开阔处，不是眼）：
        //  2 O O O O
        //  1 . O . .
        //    A B C D
        // 棋串本来就不是已确定活形，"失去活形"条件不成立——只靠"落点是批次开始前己方的单格眼"淘汰。
        MatchFlow match = AiFixtures.Round5().Stones(Me, "B1", "A2", "B2", "C2", "D2");
        LifeShapeReport before = LifeShapeReport.Analyze(match.Board);
        Assert.Equal(LifeState.Undetermined, before.GroupLifeAt(TestMaps.At("B2"))!.Life);
        Assert.True(before.EyeSpaceAt(TestMaps.At("A1")) is { Cells.Length: 1 } eye && eye.Owner == Me);
        Assert.Null(before.EyeSpaceAt(TestMaps.At("C1")));
        (StagedBatch batch, SettlementDriver driver) = Staging(match);

        RehearsalResult fill = Rehearse(batch, driver, BatchFixtures.P("A1"));
        Assert.True(fill.IsLegal, fill.Failure?.Message);
        Assert.Empty(fill.Captures);

        HeuristicTurnController ai = Deploy(match, AiDifficulty.Standard, batch, driver);

        Assert.Equal([.. Empties(match).Where(c => c != TestMaps.At("A1"))], RankedCells(ai));
    }

    [Fact]
    public void 点三格眼空间的中间不被淘汰()
    {
        // 直三眼空间 A1–B1–C1（眼值 1，未定），点中间 B1 → A1、C1 两个单格眼，棋串成为已确定活形：
        //  2 O O O O
        //  1 . ! . O     ! = B1
        //    A B C D
        // 落点在己方眼空间里，但它是 3 格而不是单格——不淘汰，交给眼位维度（原始值 = 眼值 +1、活形 +3 = 4，同「点直三中间眼位增量为眼值1加活形3」）。
        // 变异 M-B3（单格眼判据去掉"格数为 1"）→ 红 4（本测试、停手阈值Tests.零收益不落子、停手阈值Tests.阈值为0时零变化、候选格上限黄金哈希）。
        MatchFlow match = AiFixtures.Round5().Stones(Me, "A2", "B2", "C2", "D2", "D1");
        EyeSpace space = LifeShapeReport.Analyze(match.Board).EyeSpaceAt(TestMaps.At("B1"))!;
        Assert.Equal(3, space.Cells.Length);
        Assert.Equal(Me, space.Owner);
        (StagedBatch batch, SettlementDriver driver) = Staging(match);

        HeuristicTurnController ai = Deploy(match, AiDifficulty.Standard, batch, driver);

        Assert.Equal(Empties(match), RankedCells(ai));
        PointScore middle = ai.LastPointRanking.Single(p => p.Coord == TestMaps.At("B1"));
        Assert.Equal(4, middle.Evaluation.RawOf(EvaluationDimension.Eye));
    }

    [Fact]
    public void 为提子而填眼放行()
    {
        // P0 棋串的单格眼 A1（同「不填自己的单格眼」）；右上角 P1 孤子 J9 只剩 J8 一口气（H9 是 P0）。
        // 单格眼的气边邻居全是 P0，落在眼里本身提不了子——"填眼 + 提子"只可能出现在批次层：同批次 A1 + J8。
        // 变异 M-B2（去掉"无提子"条件）→ 红 1（本测试：该批次被淘汰）。
        MatchFlow match = AiFixtures.Round5().Stones(Me, "B1", "A2", "B2", "C2", "D2", "H9").Stones(AiFixtures.P1, "J9");
        (StagedBatch batch, SettlementDriver driver) = Staging(match);
        HeuristicTurnController ai = new(Me, match.Publish, match.Seed.Stream(HeuristicAi.StreamName(Me)), AiDifficulty.Standard, config: Wide(AiDifficulty.Standard));
        BatchEvaluator evaluator = ai.CreateEvaluator();

        // 反面：只填眼（无提子）→ 淘汰。否则下面的"放行"是恒真。
        RehearsalResult fillOnly = Rehearse(batch, driver, BatchFixtures.P("A1"));
        Assert.True(fillOnly.IsLegal, fillOnly.Failure?.Message);
        Assert.Empty(fillOnly.Captures);
        Assert.False(evaluator.TryEvaluate([BatchFixtures.P("A1")], fillOnly, batch.Context, out _));

        ImmutableArray<Placement> both = [BatchFixtures.P("A1"), BatchFixtures.P("J8")];
        RehearsalResult capture = Rehearse(batch, driver, [.. both]);
        Assert.True(capture.IsLegal, capture.Failure?.Message);
        Assert.Equal(["J9"], capture.Captures.Select(c => c.Coord.ToNotation()));

        Assert.True(evaluator.TryEvaluate(both, capture, batch.Context, out EvaluationBreakdown? scored));
        Assert.True(scored.RawOf(EvaluationDimension.EnemyLoss) > 0, scored.ToString());
    }

    [Fact]
    public void 用改造拆自己的活形也被淘汰()
    {
        // 地图预置栅栏 A1–A2、J1–J2，P0 一排 B1…H1：A1 的气边邻居只剩 B1、J1 的只剩 H1 → 两个单格眼，整排是已确定活形。
        //  2 . . . ! . . . . .     ! = 匠人落点 D2
        //  1 . O O O|O O O O .     | = 本候选立的栅栏 D1–E1（左右两段各只剩一个眼 → 两条未定棋串）
        //    A B C D E F G H J
        // 立栅不落进任何眼，"单格眼"条件不成立——只靠"原活形棋串失去活形"淘汰。
        // 变异 M-B4（去掉"原活形失活"条件）→ 红 1（本测试）。
        MatchFlow match = MatchFixtures
            .Started(TestMaps.Terrain(fences: [("A1", "A2"), ("J1", "J2")]))
            .AtRound(5, [Me, AiFixtures.P1, AiFixtures.P2, AiFixtures.P3])
            .Stones(Me, "B1", "C1", "D1", "E1", "F1", "G1", "H1");
        Assert.Equal(LifeState.Alive, LifeShapeReport.Analyze(match.Board).GroupLifeAt(TestMaps.At("B1"))!.Life);
        TerrainEdit cut = TerrainEdit.Fence(TestMaps.At("D1"), TestMaps.At("E1"));
        Assert.Contains(cut, TerrainEditRules.LegalTargets(match.Board.Map, TestMaps.At("D2")));
        (StagedBatch batch, SettlementDriver driver) = Staging(match, PieceType.Artisan, 1, "D2");

        RehearsalResult split = Rehearse(batch, driver, BatchFixtures.Artisan("D2", cut));
        Assert.True(split.IsLegal, split.Failure?.Message);
        Assert.Empty(split.Captures);
        LifeShapeReport after = LifeShapeReport.Analyze(split.ProjectedBoard!);
        Assert.Equal(LifeState.Undetermined, after.GroupLifeAt(TestMaps.At("B1"))!.Life);
        Assert.Equal(LifeState.Undetermined, after.GroupLifeAt(TestMaps.At("H1"))!.Life);
        Assert.NotSame(after.GroupLifeAt(TestMaps.At("B1")), after.GroupLifeAt(TestMaps.At("H1")));

        HeuristicTurnController ai = Deploy(match, AiDifficulty.Standard, batch, driver);

        Assert.DoesNotContain(ai.LastPointRanking, p => p.Edit == cut);
        Assert.Contains(ai.LastPointRanking, p => p.Coord == TestMaps.At("D2") && p.Edit is null);   // 同一落点不改造：不拆活形，照常进排行
        Assert.True(ai.LastPointRanking.Length >= 2, $"排行只有 {ai.LastPointRanking.Length} 项");
    }

    [Fact]
    public void 全部候选被淘汰则Pass()
    {
        // 合法范围只有两个单格眼 A1、C1（同「不拆自己的活形」的盘面）：两个候选在规则层都合法、都不提子，全部被淘汰。
        // 变异 M-B1 → 红（排行非空）。
        MatchFlow match = TwoEyes();
        (StagedBatch batch, SettlementDriver driver) = Staging(match, PieceType.Basic, 2, "A1", "C1");
        foreach (string cell in new[] { "A1", "C1" })
        {
            RehearsalResult r = Rehearse(batch, driver, BatchFixtures.P(cell));
            Assert.True(r.IsLegal, r.Failure?.Message);
            Assert.Empty(r.Captures);
        }

        HeuristicTurnController ai = Deploy(match, AiDifficulty.Standard, batch, driver);

        Assert.Empty(ai.LastPointRanking);
        Assert.Null(ai.LastChoice);
        Assert.Equal(0, batch.Count);
        Assert.Equal("D:pass", ai.Decisions[^1]);
    }
}
