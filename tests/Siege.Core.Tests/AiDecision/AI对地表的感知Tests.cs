using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;

namespace Siege.Core.Tests.AiDecision;

/// <summary>
/// 规格 terrain-surfaces · ai-decision —— Requirement: AI 对地表的感知（tasks 1.4 / 2.4 / 3.4 / 4.5）。
/// AI 不另写地表判断：覆盖、气、空区、领地计分都经 Core 唯一查询，评价与预筛因而自然感知地表。
/// 算例都在流程夹具中部摆一枚孤子：retire-legacy-rules 1.4 起是平坦 8×8 链接档图（新地表只能放在未校验的手写链接档图上：校验器禁止出生棋盘内新地表）
/// 棋盘 0 上的 G7（原 9×9 夹具中央 E5 平移 (+2, +2)；四邻与岩台的八个远格都在棋盘 0 内）。
/// </summary>
public class AI对地表的感知Tests
{
    [Fact]
    public void 不去抢荒漠()
    {
        // 规格 Scenario「不去抢荒漠」：同一落点，四邻空草地 vs 四邻空荒漠 → 前者即时势力增量严格更高（5 对 1）。
        // 变异验证 M-S6a（实跑）：PowerCalculator 的领地分改回按 ExclusiveCells 计 → 本测试红（两者都是 5）。
        (EvaluationBreakdown grass, _) = EvaluationFixtures.EvaluateP0(At(TestMaps.Terrain()), "G7");
        (EvaluationBreakdown desert, _) = EvaluationFixtures.EvaluateP0(
            At(TestMaps.Terrain(surfaces: [("G6", Surface.Desert), ("F7", Surface.Desert), ("H7", Surface.Desert), ("G8", Surface.Desert)])), "G7");

        Assert.Equal(5, grass.RawOf(EvaluationDimension.PowerGain));
        Assert.Equal(1, desert.RawOf(EvaluationDimension.PowerGain));
        Assert.True(grass.RawOf(EvaluationDimension.PowerGain) > desert.RawOf(EvaluationDimension.PowerGain));
    }

    [Fact]
    public void 沼泽落子无领地收益()
    {
        // 规格 Scenario「沼泽落子无领地收益」：普通子落在四周空草地的沼泽 G7 → 即时势力增量只含棋子军势 1。
        (EvaluationBreakdown e, _) = EvaluationFixtures.EvaluateP0(At(TestMaps.Terrain(surfaces: [("G7", Surface.Marsh)])), "G7");

        Assert.Equal(1, e.RawOf(EvaluationDimension.PowerGain));
    }

    [Fact]
    public void 岩台远格计入收益()
    {
        // 规格 Scenario「岩台远格计入收益」：普通子落在四周八格为空草地的岩台 G7，附近无敌子 → 即时势力增量 9（军势 1 + 8 个独占空格）。
        (EvaluationBreakdown e, _) = EvaluationFixtures.EvaluateP0(At(TestMaps.Terrain(surfaces: [("G7", Surface.Crag)])), "G7");

        Assert.Equal(9, e.RawOf(EvaluationDimension.PowerGain));
    }

    [Fact]
    public void 空浅滩不算安全()
    {
        // 规格 Scenario「空浅滩不算安全」：己方棋串的气边邻格两格空草地、两格空浅滩 → 被围杀风险按气数 2 评估。
        // 安全维度取自 GroupSafety（气 = GameBoard.LibertiesOf），与唯一的气查询同源。
        MatchFlow match = At(TestMaps.Terrain(surfaces: [("H7", Surface.Shallows), ("G8", Surface.Shallows)])).Stones(AiFixtures.P0, "G7");
        Group group = match.Board.GroupAt(TestMaps.At("G7"))!;
        LifeShapeReport life = LifeShapeReport.Analyze(match.Board);

        GroupSafety safety = GroupSafety.Analyze(match.Board, life.GroupLifeAt(TestMaps.At("G7"))!);

        Assert.Equal(2, safety.Liberties);
        Assert.Equal(["G6", "F7"], match.Board.LibertiesOf(group).Notations());
    }

    [Fact]
    public void 剪枝使用唯一查询()
    {
        // 规格 Scenario「剪枝使用唯一查询」：候选格预筛（不做完整枚举的近似口径）对岩台格与草地格的估计，
        // 等于覆盖查询给出的目标数 + 棋子军势——岩台 1 + 8 = 9、草地 1 + 4 = 5，而不是把一切可落子格当草地估计。
        foreach ((TerrainData terrain, int expected) in new[] { (TestMaps.Terrain(surfaces: [("G7", Surface.Crag)]), 9), (TestMaps.Terrain(), 5) })
        {
            MatchFlow match = At(terrain);
            match.Debug.SeedHand(AiFixtures.P0, (PieceType.Basic, 10));
            HeuristicTurnController ai = HeuristicAi.Create(match, AiFixtures.P0, AiDifficulty.Standard);
            StagedBatch batch = match.OpenDeploy();
            BatchEvaluator evaluator = ai.CreateEvaluator();
            RehearsalResult result = match.RehearseBatch(batch, ("G7", PieceType.Basic));

            EvaluationBreakdown prefilter = evaluator.EvaluatePrefilter(batch.Placements, result, batch.Context);

            Assert.Equal(1 + match.Board.CoverageTargets(TestMaps.At("G7")).Length, expected);
            Assert.Equal(expected, prefilter.RawOf(EvaluationDimension.PowerGain));
        }
    }

    /// <summary>平坦 8×8 链接档图换上指定地形（写在棋盘 0 上）后开到第 5 大回合（四人同锁棋盘 0），顺序 P0 → P3。</summary>
    private static MatchFlow At(TerrainData terrain) =>
        LinkModeFixtures.PlainStarted(terrain).AtRound(5, [AiFixtures.P0, AiFixtures.P1, AiFixtures.P2, AiFixtures.P3]);
}
