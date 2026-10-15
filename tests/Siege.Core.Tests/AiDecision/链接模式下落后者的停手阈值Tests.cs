using System.Collections.Immutable;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.AiDecision;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/ai-decision —— Requirement: 链接模式下落后者的停手阈值（负责人裁决 C15，tasks 3.7）。
/// </summary>
/// <remarks>
/// <para>局面：P0 在出生棋盘 0（内部编号）上，唯一合法格 <see cref="Candidate"/> 是四邻皆空、离任何棋子 ≥ 2 格的孤子点。
/// 简单难度只算即时势力、敌损、眼位三维；权重写死为定值前缺省并把 PowerGain 取 1（testing.md「依赖 AI 实际怎么走的断言要把权重写死」），
/// 孤子势力增量 = 棋串军势 1 + 独占领地 4 = 5，即 Scenario 原文"最好候选的收益为 5"；停手阈值写死为 20（Scenario 原文"缺省停手阈值 20"，不读缺省常量）。
/// P0 盘上始终至少有一枚子，避免无子豁免先行生效。</para>
/// <para>变异（3.7 实跑，记录见任务 check.md）：MC-C15a <c>EffectivePassThreshold</c> 恒取 0 → 红 12（本类领先者 ×2 / 并列 / 等待者 / 旧模式，
/// 停手阈值Tests 4 条、前瞻中的停手口径 1 条、v5 决策序列基线、候选格上限 Legacy 黄金哈希）；MC-C15b 落后者判定去掉（恒不取 0）→ 红 2（本类落后者 ×2）；
/// MC-C15c 比较改为大于等于 → 红 1（并列最高视为最高）。链接图决策序列基线对本条不敏感（见 <see cref="决策序列基线_链接模式Tests"/> 的说明）。</para>
/// </remarks>
public class 链接模式下落后者的停手阈值Tests
{
    private const int Threshold = 20;

    private static readonly EvaluationWeights Weights = SimFixtures.PreCalibrationWeights with { PowerGain = 1 };

    /// <summary>出生棋盘 0 的宽（读棋盘清单；link-map-terrain 起出生区是 3×3 核心区，不能再拿它量边长）。</summary>
    private static int Side => Map.BirthBoard(0).Width;

    /// <summary>孤子点：棋盘 0 左上起 (2, 3)，离两个上角的棋子都 ≥ 2 格。</summary>
    private static Coord Candidate => BirthCell(Map, 0, 2, 3);

    private static Coord Corner(int dx, int dy) => BirthCell(Map, 0, dx, dy);

    /// <summary>右上角（与左上角对称）：两角各一枚孤子势力相等。</summary>
    private static Coord FarCorner => BirthCell(Map, 0, Side - 1, 0);

    /// <summary>P0 以 <paramref name="simulated"/> 指定的控制器在 <see cref="Candidate"/> 一格上决策（部署上限 1、阈值 20）。</summary>
    private static HeuristicTurnController Decide(MatchFlow match, bool simulated = false)
    {
        GameBoard board = match.Board;
        var batch = new StagedBatch(board, BatchFixtures.Context(board, P0, 1, BatchFixtures.Stock((PieceType.Basic, 5)), new HashSet<Coord> { Candidate }));
        var driver = new SettlementDriver(board, match.History, new RecordingHooks { Board = board });
        AiSearchConfig config = AiSearchConfig.Easy with { PassThreshold = Threshold };
        HeuristicTurnController ai = simulated
            ? HeuristicTurnController.ForSimulation(P0, match.Publish(), Weights, config, lifeQuery: null, cacheLife: true)
            : new HeuristicTurnController(P0, match.Publish, match.Seed.Stream(HeuristicAi.StreamName(P0)), AiDifficulty.Easy, Weights, config);
        ai.Deploy(batch, () => driver.Rehearse(batch.Context, batch.Placements));

        // 样本口径：唯一候选是孤子点，收益恰为 5（低于阈值 20）。
        PointScore point = Assert.Single(ai.LastPointRanking);
        Assert.Equal(Candidate, point.Coord);
        Assert.Equal(5, point.Total);
        return ai;
    }

    private static void AssertPlaced(HeuristicTurnController ai)
    {
        Placement placed = Assert.Single(ai.LastChoice!.Placements);
        Assert.Equal(Candidate, placed.Coord);
    }

    private static void AssertPassed(HeuristicTurnController ai)
    {
        Assert.True(ai.LastChoice!.IsPass, ai.LastChoice.ToString());
        Assert.Equal("D:pass", ai.Decisions[^1]);
    }

    /// <summary>P0、P1 同在棋盘 0（双人战场），P2、P3 各占一块。P0 一枚右上角孤子；P1 左上角三子成串，势力高于 P0。</summary>
    private static MatchFlow Trailing() =>
        Started([0, 0, 3, 4]).Place(P0, FarCorner).Place(P1, Corner(0, 0), Corner(1, 0), Corner(0, 1));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 落后者不轻易停手(bool simulated)
    {
        // Scenario「落后者不轻易停手」：Link 双人战场、AI 势力低于对手，最好候选收益 5 < 阈值 20 → 停手阈值取 0，AI 落子而不是 Pass。
        // simulated = true：专家前瞻的模拟对手（ForSimulation）走同一处判定（ai-decision「前瞻中的停手口径」的复用点）。
        MatchFlow match = Trailing();
        MatchPublicView view = match.Publish();
        Assert.True(view.Power!.Of(P0).Total < view.Power.Of(P1).Total);   // 样本口径
        Assert.Equal(match.BattlefieldOf(P0), match.BattlefieldOf(P1));

        HeuristicTurnController ai = Decide(match, simulated);

        AssertPlaced(ai);
        Assert.Equal(Threshold, ai.Config.PassThreshold);   // 配置与日志首部仍记配置值，豁免只在决策入口生效
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 领先者照常停手(bool simulated)
    {
        // Scenario「领先者照常停手」：同一局面换成 AI 势力高于对手 → 停手阈值为缺省值，收益 5 不过阈值，AI Pass。
        MatchFlow match = Started([0, 0, 3, 4]).Place(P1, FarCorner).Place(P0, Corner(0, 0), Corner(1, 0), Corner(0, 1));
        MatchPublicView view = match.Publish();
        Assert.True(view.Power!.Of(P0).Total > view.Power.Of(P1).Total);   // 样本口径

        AssertPassed(Decide(match, simulated));
    }

    [Fact]
    public void 并列最高视为最高()
    {
        // 规格："并列最高视为最高"——两人各一枚对称角上的孤子、势力相等 → 不算落后，阈值为缺省值，AI Pass。
        // 判别力：比较由严格大于改为大于等于即红。
        MatchFlow match = Started([0, 0, 3, 4]).Place(P0, FarCorner).Place(P1, Corner(0, 0));
        MatchPublicView view = match.Publish();
        Assert.Equal(view.Power!.Of(P1).Total, view.Power.Of(P0).Total);   // 样本口径

        AssertPassed(Decide(match));
    }

    [Fact]
    public void 等待者照常停手()
    {
        // Scenario「等待者照常停手」：AI 独自在单人战场（P0 独占棋盘 0），即便全局势力低于别的战场里的玩家，阈值仍为缺省值 → Pass。
        MatchFlow match = Started([0, 3, 3, 4]).Place(P0, FarCorner)
            .Place(P1, BirthCell(Map, 3, 0, 0), BirthCell(Map, 3, 1, 0), BirthCell(Map, 3, 0, 1));
        MatchPublicView view = match.Publish();
        Assert.True(view.Power!.Of(P0).Total < view.Power.Of(P1).Total);   // 样本口径：只比同战场，不比全局
        Assert.Single(match.ActivePlayersIn(match.BattlefieldOf(P0)));

        AssertPassed(Decide(match));
    }
}
