using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;

namespace Siege.Core.Ai;

/// <summary>
/// <b>测试专用</b>调试 AI（设计文档 §15.3）：与正式 AI 同一套启发式，但信物估值读 <see cref="MatchDebugView"/> 里的真实内容。
/// 类型与构造入口都是 <c>internal</c>，没有 public 构造 / 工厂；构造 MUST 显式传 <c>debugMode: true</c>，并在
/// <see cref="MatchRunner.Annotations"/> 上标注本局用过调试 AI（裁决 9）。
/// </summary>
internal sealed class DebugTurnController : ITurnController
{
    private DebugTurnController(MatchDebugView view, HeuristicTurnController inner)
    {
        View = view;
        Inner = inner;
    }

    /// <summary>全量视图。</summary>
    internal MatchDebugView View { get; }

    /// <summary>实际做决策的启发式 AI（估值函数已换成读真实内容）。</summary>
    internal HeuristicTurnController Inner { get; }

    /// <summary>
    /// 创建调试 AI 并挂到运行器。<paramref name="debugMode"/> 为 <c>false</c> 即视为面向玩家的对局：拒绝并说明仅供测试。
    /// </summary>
    internal static DebugTurnController Create(
        MatchRunner runner,
        PlayerId player,
        bool debugMode,
        AiDifficulty difficulty = AiDifficulty.Standard,
        EvaluationWeights? weights = null,
        AiSearchConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        if (!debugMode)
        {
            throw new SiegeRuleException("调试 AI 仅供测试：面向玩家的对局不得启用；构造时必须显式传 debugMode: true。");
        }

        MatchFlow match = runner.Match;
        var view = new MatchDebugView(match);
        var inner = new HeuristicTurnController(
            player, match.Publish, match.Seed.Stream(HeuristicAi.StreamName(player)), difficulty, weights, config,
            relicValue: state => RelicEstimate.ValueOf(view.ContentOf(state.Coord)));
        var controller = new DebugTurnController(view, inner);
        runner.Annotations.MarkDebugAi(player);
        runner.SetController(player, controller);
        return controller;
    }

    public void OrganizeHand(PlayerHandAccess hand, int overflow) => Inner.OrganizeHand(hand, overflow);

    public void Recruit(PlayerHandAccess hand, RecruitPanelView panel) => Inner.Recruit(hand, panel);

    public void Deploy(StagedBatch batch, Func<RehearsalResult> rehearse) => Inner.Deploy(batch, rehearse);

    public bool OnRejected(StagedBatch batch, BatchFailure failure) => Inner.OnRejected(batch, failure);
}
