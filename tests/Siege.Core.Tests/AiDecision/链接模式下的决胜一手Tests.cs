using System.Collections.Immutable;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.AiDecision;

/// <summary>
/// 规格：openspec/changes/ai-pass-review/specs/ai-decision —— Requirement: 链接模式下的决胜一手（负责人裁决 P7 = A 窄版，tasks 2.1）。
/// </summary>
/// <remarks>
/// <para>局面沿用 <see cref="链接模式下落后者的停手阈值Tests"/>：P0、P1 同在出生棋盘 0（双人战场），P2、P3 各占一块。简单难度、停手阈值写死 20。
/// link-map-terrain 第 2 组起载体由 <c>link:1</c> 改为平坦的手写链接档图（见 <see cref="FlatMap"/>）：所守的是决胜一手的触发条件与势力判据，不是某张图的地形。
/// 为了构造"最好单点评价收益为负、但落下后势力反超"（Scenario 原文），权重写死为定值前缺省并把 PowerGain 取 −1、EnemyLoss 取 0：简单难度只看这两维与眼位（眼位权重 0），任何增加己方势力的落子评价都为负，
/// AI 依停手阈值必然 Pass——决胜一手是否介入只取决于触发条件与势力（testing.md：依赖 AI 实际走法的断言把权重写死）。</para>
/// <para>"对手本大回合已 Pass"由真实流程给出：把行动顺序设为 P1 先、P0 次，P1 走完一个 Pass 的小回合（<see cref="MatchFlow.PassedThisRound"/> 经公开快照到达 AI）。</para>
/// <para>变异（2.1 实跑，记录见任务 implement.md「第二段」）：MD-1 去掉条件 (1)（对手未 Pass 也触发）→ 红「对手尚未Pass时不触发」；
/// MD-2 去掉条件 (2)（领先也触发）→ 红「领先者不触发」；MD-3 决胜判据改为"严格反超"→ 红「最后一个Pass会输且有反超手_追平即落」。</para>
/// </remarks>
public class 链接模式下的决胜一手Tests
{
    private const int Threshold = 20;

    private static readonly EvaluationWeights Weights = SimFixtures.PreCalibrationWeights with { PowerGain = -1, EnemyLoss = 0 };

    /// <summary>P0、P1 同在出生棋盘 0、P2 在 3、P3 在 4，在平坦的手写链接档图（<see cref="LinkModeFixtures.FlatMap"/>）上开始第 1 大回合。</summary>
    private static MatchFlow Flat() => StartedFlat([0, 0, 3, 4]);

    /// <summary>孤子点：棋盘 0 左下起 (2, 3)（核心区上沿正中），四邻皆空、离两个下角的棋子都 ≥ 2 格（同 C15 测试）。孤子落下 = 军势 1 + 独占领地 4，势力 +5。</summary>
    private static Coord Candidate => BirthCell(FlatMap, 0, 2, 3);

    private static Coord FarCorner => BirthCell(FlatMap, 0, FlatMap.BirthBoard(0).Width - 1, 0);

    private static Coord Corner(int dx, int dy) => BirthCell(FlatMap, 0, dx, dy);

    private static HeuristicTurnController Ai(MatchFlow match) =>
        new(P0, match.Publish, match.Seed.Stream(HeuristicAi.StreamName(P0)), AiDifficulty.Easy, Weights, AiSearchConfig.Easy with { PassThreshold = Threshold });

    /// <summary>P1 先走一个 Pass 的小回合，轮到 P0（行动顺序 P1 → P0 → P2 → P3）。</summary>
    private static MatchFlow AfterP1Passed(MatchFlow match)
    {
        match.Debug.SetOrder(P1, P0, P2, P3);
        match.ToDeploy();
        match.Pass();
        Assert.Equal(P0, match.CurrentPlayer);
        Assert.Equal([P1], match.Publish().PassedThisRound);   // 样本口径：P1 的 Pass 经公开快照可见
        return match;
    }

    /// <summary>P0 只在 <see cref="Candidate"/> 一格、部署上限 1 上决策。</summary>
    private static HeuristicTurnController DecideAtCandidate(MatchFlow match)
    {
        GameBoard board = match.Board;
        var batch = new StagedBatch(board, BatchFixtures.Context(board, P0, 1, BatchFixtures.Stock((PieceType.Basic, 5)), new HashSet<Coord> { Candidate }));
        var driver = new SettlementDriver(board, match.History, new RecordingHooks { Board = board });
        HeuristicTurnController ai = Ai(match);
        ai.Deploy(batch, () => driver.Rehearse(batch.Context, batch.Placements));
        Assert.True(Assert.Single(ai.LastPointRanking).Total < 0);   // 样本口径：唯一单点的评价为负，依停手阈值本将 Pass
        return ai;
    }

    /// <summary>P0 右上角一枚孤子（势力 3），P1 左上角两子成串（势力 5）：P0 落后，落下孤子点后 8 ≥ 5 即反超。</summary>
    private static MatchFlow Trailing() => Flat().Place(P0, FarCorner).Place(P1, Corner(0, 0), Corner(1, 0));

    [Fact]
    public void 最后一个Pass会输且有反超手()
    {
        // Scenario「最后一个 Pass 会输且有反超手」：对手本大回合已 Pass，AI 落后，最好单点评价收益为负，但有合法落子能让势力反超 → 落下而不是 Pass，
        // 本战场本大回合不结算。走真实流程：P0 用正式暂放批次（整块棋盘的合法范围）决策并确认，P2、P3 Pass 收尾。
        MatchFlow match = AfterP1Passed(Trailing());
        MatchPublicView view = match.Publish();
        Assert.True(view.Power!.Of(P0).Total < view.Power.Of(P1).Total);   // 样本口径：P0 落后

        StagedBatch batch = match.ToDeploy();
        HeuristicTurnController ai = Ai(match);
        ai.Deploy(batch, match.Rehearse);

        Assert.True(ai.LastPointRanking[0].Total < 0, $"最好单点收益 {ai.LastPointRanking[0].Total}");   // 样本口径：评价上本将 Pass
        DecisiveMove decisive = Assert.IsType<DecisiveMove>(ai.LastDecisive);
        Assert.False(ai.LastChoice!.IsPass);
        Assert.Equal(decisive.Outcome.Placements, ai.LastChoice.Placements);
        Assert.True(decisive.Outcome.PowerAfter >= decisive.Outcome.RivalAfter);
        Assert.StartsWith("D:", ai.Decisions[^1], StringComparison.Ordinal);
        Assert.NotEqual("D:pass", ai.Decisions[^1]);

        Assert.True(match.Confirm().Confirmed);
        Assert.True(match.Publish().Power!.Of(P0).Total >= match.Publish().Power!.Of(P1).Total);   // 预演口径与正式结算一致：确已反超
        int field = match.BattlefieldOf(P0);
        match.PassRound();
        Assert.False(match.IsBattlefieldSettled(field));
        Assert.Empty(match.Settlements);
        Assert.True(match.PlayerStates.Single(s => s.Player == P0).IsActive);
    }

    [Fact]
    public void 最后一个Pass会输且有反超手_追平即落()
    {
        // "≥ 对手最高"含追平（并列最高视为最高）：P0 一枚孤子 3、P1 左上角 2×2 四子 8；P0 落孤子点后 3 + 5 = 8，恰好追平 → 落下。
        // 判别力：变异 MD-3"决胜判据改为严格反超（margin > 0）"→ 本测试红。
        MatchFlow match = AfterP1Passed(Flat().Place(P0, FarCorner).Place(P1, Corner(0, 0), Corner(1, 0), Corner(0, 1), Corner(1, 1)));
        HeuristicTurnController ai = DecideAtCandidate(match);

        DecisiveMove decisive = Assert.IsType<DecisiveMove>(ai.LastDecisive);
        Assert.Equal(decisive.Outcome.RivalAfter, decisive.Outcome.PowerAfter);   // 样本口径：恰好追平
        Assert.Equal(Candidate, Assert.Single(ai.LastChoice!.Placements).Coord);
        Assert.False(decisive.FromForcedBatch);
    }

    [Fact]
    public void 无反超手照常Pass()
    {
        // Scenario「无反超手照常 Pass」：同一情形下 P1 势力远高于 P0 + 5，任何合法批次都追不平 → Pass，本战场按整轮 Pass 结算（P0 战场落败）。
        MatchFlow match = AfterP1Passed(Flat().Place(P0, FarCorner)
            .Place(P1, Corner(0, 0), Corner(1, 0), Corner(0, 1), Corner(1, 1), Corner(2, 0), Corner(0, 2)));
        Assert.True(match.Publish().Power!.Of(P1).Total > match.Publish().Power!.Of(P0).Total + 5);   // 样本口径：孤子点的 +5 也追不平
        HeuristicTurnController ai = DecideAtCandidate(match);

        Assert.Null(ai.LastDecisive);
        Assert.True(ai.LastChoice!.IsPass);
        Assert.Equal("D:pass", ai.Decisions[^1]);

        match.ToDeploy();
        match.Pass();
        match.PassRound();
        BattlefieldSettlementRecord settled = Assert.Single(match.Settlements);
        Assert.Equal(P1, settled.Winner);
        Assert.Equal(EliminationReason.BattlefieldDefeat, match.PlayerStates.Single(s => s.Player == P0).EliminationReason);
    }

    [Fact]
    public void 对手尚未Pass时不触发()
    {
        // Scenario「对手尚未 Pass 时不触发」：同「最后一个 Pass 会输且有反超手」的局面，但 P0 先行动、P1 本大回合尚未 Pass → 决胜一手不触发，
        // AI 依停手阈值 Pass，与引入本条之前相同。判别力：变异 MD-1 去掉条件 (1) → 本测试红（会落下反超的孤子）。
        MatchFlow match = Trailing();
        match.Debug.SetOrder(P0, P1, P2, P3);
        Assert.Empty(match.Publish().PassedThisRound);
        Assert.False(HeuristicTurnController.DecisiveTriggered(match.Publish(), P0));

        HeuristicTurnController ai = DecideAtCandidate(match);

        Assert.Null(ai.LastDecisive);
        Assert.True(ai.LastChoice!.IsPass);
        Assert.Equal("D:pass", ai.Decisions[^1]);
    }

    [Fact]
    public void 领先者不触发()
    {
        // Scenario「领先者不触发」：对手已 Pass、AI 势力为本战场最高 → 不触发，照常 Pass。另：并列最高视为最高，同样不触发。
        // 判别力：变异 MD-2 去掉条件 (2) → 本测试红。
        MatchFlow leading = AfterP1Passed(Flat().Place(P1, FarCorner).Place(P0, Corner(0, 0), Corner(1, 0)));
        Assert.True(leading.Publish().Power!.Of(P0).Total > leading.Publish().Power!.Of(P1).Total);   // 样本口径
        HeuristicTurnController ai = DecideAtCandidate(leading);
        Assert.Null(ai.LastDecisive);
        Assert.True(ai.LastChoice!.IsPass);

        MatchFlow tied = AfterP1Passed(Flat().Place(P0, FarCorner).Place(P1, Corner(0, 0)));
        Assert.Equal(tied.Publish().Power!.Of(P0).Total, tied.Publish().Power!.Of(P1).Total);   // 样本口径
        Assert.False(HeuristicTurnController.DecisiveTriggered(tied.Publish(), P0));
        Assert.True(DecideAtCandidate(tied).LastChoice!.IsPass);
    }

    [Fact]
    public void 旧模式不变()
    {
        // Scenario「旧模式不变」：Legacy 手写夹具里公开快照没有本大回合 Pass 记录，决胜一手恒不触发；P0 落后、唯一候选能反超也照常 Pass。
        // 逐字节口径由既有 Legacy 测试（候选格上限 / 停手阈值黄金哈希等）在全量回归里守住。
        MatchFlow match = MatchFixtures.Started().AtRound(5, [P0, P1, P2, P3]).Stones(P0, "A1").Stones(P1, "J9", "H9");
        Assert.Equal(MatchMode.Legacy, match.Mode);
        match.Debug.SetOrder(P1, P0, P2, P3);
        match.ToDeploy();
        match.Pass();
        MatchPublicView view = match.Publish();
        Assert.Empty(view.PassedThisRound);
        Assert.False(HeuristicTurnController.DecisiveTriggered(view, P0));

        GameBoard board = match.Board;
        var batch = new StagedBatch(board, BatchFixtures.Context(board, P0, 1, BatchFixtures.Stock((PieceType.Basic, 5)), ImmutableHashSet.Create(TestMaps.At("E5"))));
        var driver = new SettlementDriver(board, match.History, new RecordingHooks { Board = board });
        HeuristicTurnController ai = Ai(match);
        ai.Deploy(batch, () => driver.Rehearse(batch.Context, batch.Placements));
        Assert.Null(ai.LastDecisive);
        Assert.True(ai.LastChoice!.IsPass);
    }
}
