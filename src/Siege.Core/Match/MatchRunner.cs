using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Recruit;

namespace Siege.Core.Match;

/// <summary>
/// 用控制者驱动 <see cref="MatchFlow"/> 的阶段机走完小回合 / 整局。每名玩家一个控制者，可在任意阶段替换（人工接管）。
/// </summary>
/// <remarks>
/// 运行器只做"在正确的阶段把句柄交给正确的控制者"，不含任何规则；被拒绝的批次最多补救 <see cref="MaxRejections"/> 次，之后 Pass，
/// 保证批量跑局不会被一个反复提交非法批次的控制者卡死。
/// </remarks>
public sealed class MatchRunner
{
    /// <summary>同一小回合内允许的最大拒绝次数。</summary>
    public const int MaxRejections = 8;

    private readonly Dictionary<PlayerId, ITurnController> _controllers = [];

    public MatchRunner(MatchFlow match)
    {
        Match = match ?? throw new ArgumentNullException(nameof(match));
    }

    public MatchFlow Match { get; }

    /// <summary>设置或替换某玩家的控制者。可在任意阶段调用；从下一个阶段起生效。</summary>
    public void SetController(PlayerId player, ITurnController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        if (!Match.Players.Contains(player))
        {
            throw new SiegeRuleException($"玩家 {player} 不在本局名单中。");
        }

        _controllers[player] = controller;
    }

    /// <summary>某玩家当前的控制者。</summary>
    public ITurnController ControllerOf(PlayerId player) =>
        _controllers.TryGetValue(player, out ITurnController? c) ? c : throw new SiegeRuleException($"玩家 {player} 没有控制者。");

    /// <summary>跑完当前玩家的一个小回合（从 Idle 到 Idle）。对局已结束或本回合中途结束时提前返回。</summary>
    public void RunTurn()
    {
        Match.BeginTurn();
        if (Match.Phase != MatchPhase.InProgress || Match.Stage == TurnStage.Idle)
        {
            return;
        }

        PlayerId player = Match.CurrentPlayer!.Value;

        // 第 2 阶段：整理手牌（每个阶段开始时重新解析控制者——人工接管从下一阶段生效）
        PlayerHandAccess hand = Match.CurrentHand();
        ControllerOf(player).OrganizeHand(hand, hand.PrivateView().Overflow);

        // 第 3 阶段：征募（超限未解除则 EnterRecruit 抛出，停在整理手牌阶段）
        RecruitPanelView panel = Match.EnterRecruit();
        ControllerOf(player).Recruit(hand, panel);

        // 第 4 阶段：部署
        StagedBatch batch = Match.EnterDeploy();
        ControllerOf(player).Deploy(batch, Match.Rehearse);

        // 第 5 阶段：确认或 Pass
        for (int attempt = 0; ; attempt++)
        {
            SettlementOutcome outcome = Match.Confirm();
            if (outcome.Confirmed)
            {
                return;
            }

            if (attempt >= MaxRejections || !ControllerOf(player).OnRejected(batch, outcome.Failure!))
            {
                Match.Pass();
                return;
            }
        }
    }

    /// <summary>从当前状态跑到对局结束。<paramref name="maxTurns"/> 是防死锁上限，超出即抛出。</summary>
    public MatchResult RunToEnd(int maxTurns = 100_000)
    {
        if (Match.Phase == MatchPhase.FlagPlanting)
        {
            throw new SiegeRuleException("插旗尚未锁定。");
        }

        int turns = 0;
        while (Match.Phase == MatchPhase.InProgress)
        {
            if (++turns > maxTurns)
            {
                throw new SiegeRuleException($"对局超过 {maxTurns} 个小回合仍未结束：疑似死锁。");
            }

            RunTurn();
        }

        return Match.Result!;
    }
}
