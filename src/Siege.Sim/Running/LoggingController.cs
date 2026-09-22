using System.Collections.Immutable;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Core.Scoring;

namespace Siege.Sim.Running;

/// <summary>
/// 一个小回合内由控制者装饰器采集的痕迹：结构参数（来自该玩家自己的面板 / 批次上下文）、预演失败、确认被拒绝。
/// 只旁观，不改变任何决策；每小回合开始时 <see cref="Reset"/>。
/// </summary>
internal sealed class TurnTrace
{
    internal int ShowCount { get; set; }

    internal int FreePickCount { get; set; }

    internal int TypeSlots { get; set; }

    internal int DeployLimit { get; set; }

    internal int Rehearsals { get; set; }

    internal List<(ImmutableArray<Placement> Placements, BatchFailure Failure)> IllegalRehearsals { get; } = [];

    internal List<(ImmutableArray<Placement> Placements, BatchFailure Failure)> Rejections { get; } = [];

    internal void Reset()
    {
        ShowCount = 0;
        FreePickCount = 0;
        TypeSlots = 0;
        DeployLimit = 0;
        Rehearsals = 0;
        IllegalRehearsals.Clear();
        Rejections.Clear();
    }
}

/// <summary>
/// 控制者装饰器：把真正的控制者（正式 AI、调试 AI 或人工）原样转发，只在各阶段旁录 <see cref="TurnTrace"/>。
/// 装饰器不读对局本体，拿到的只是运行器递给该玩家的句柄——不会为写日志把 <c>MatchFlow</c> 交给 AI。
/// </summary>
internal sealed class LoggingController(ITurnController inner, TurnTrace trace) : ITurnController
{
    internal ITurnController Inner { get; } = inner ?? throw new ArgumentNullException(nameof(inner));

    /// <summary>若内层是启发式 AI（含调试 AI 的内层），返回它以读取决策分解。</summary>
    internal HeuristicTurnController? Heuristic => Inner switch
    {
        HeuristicTurnController h => h,
        DebugTurnController d => d.Inner,
        _ => null,
    };

    public void OrganizeHand(PlayerHandAccess hand, int overflow) => Inner.OrganizeHand(hand, overflow);

    public void Recruit(PlayerHandAccess hand, RecruitPanelView panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        trace.ShowCount = panel.ShowCount;
        trace.FreePickCount = panel.FreePickCount;
        trace.TypeSlots = panel.TypeSlots;
        Inner.Recruit(hand, panel);
    }

    public void Deploy(StagedBatch batch, Func<RehearsalResult> rehearse)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(rehearse);
        trace.DeployLimit = batch.Context.DeployLimit;
        Inner.Deploy(batch, () =>
        {
            RehearsalResult result = rehearse();
            trace.Rehearsals++;
            if (!result.IsLegal && result.Failure is { } failure)
            {
                trace.IllegalRehearsals.Add((batch.Placements, failure));
            }

            return result;
        });
    }

    public bool OnRejected(StagedBatch batch, BatchFailure failure)
    {
        ArgumentNullException.ThrowIfNull(batch);
        trace.Rejections.Add((batch.Placements, failure));
        return Inner.OnRejected(batch, failure);
    }
}

/// <summary>跑局层自己的断言失败（不变量被破坏、注入的失败、超出小回合上限）。</summary>
public sealed class SimAssertionException(string message) : Exception(message);
