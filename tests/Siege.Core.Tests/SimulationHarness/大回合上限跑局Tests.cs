using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>implement 3.1（round-cap D3）：跑局层 <c>--max-rounds</c> 直接写对局配置；上限 0 时只剩小回合硬停（异常 + failed 日志）。</summary>
public class 大回合上限跑局Tests
{
    private static MatchSession AlwaysPlacing(RunConfig config, ulong seed)
    {
        MatchSession session = MatchSession.Create(config, seed);
        foreach (PlayerId p in session.Match.Players)
        {
            session.SetController(p, new AlwaysPlaceController());
        }

        return session;
    }

    [Fact]
    public void 上限写入对局配置并以规则原因终局()
    {
        // --max-rounds 12 → 对局配置上限 12；永不 Pass 的控制者跑到第 12 大回合结束以规则级 MajorRoundLimit 终局，名次来自规则层，不是跑局层的临时名次。
        // 变异验证 M-R11：MatchSession.Create 不透传 MaxMajorRounds（仍用 MatchOptions.Immediate）→ 红 15（会话一致性检查让所有非 15 上限的跑局建局即抛；含本测试）。
        // 变异验证 M-R15：去掉会话构造处"对局上限 ≠ 配置上限即抛" → 红 1（本测试末段）。
        RunConfig config = SimFixtures.Config(maxRounds: 12, retention: EventRetention.SnapshotsOnly);
        MatchSession session = AlwaysPlacing(config, 301);
        Assert.Equal(12, session.Match.MaxMajorRounds);

        MatchLog log = session.Run();
        Assert.False(log.IsFailed);
        Assert.Equal(nameof(EndReason.MajorRoundLimit), log.Result!.Reason);
        Assert.False(log.Result.Converged);
        Assert.Equal(12, log.Result.MajorRound);
        Assert.Equal(12 * 4, log.Result.TurnCount);
        Assert.Equal(12, log.Header.MaxMajorRounds);
        Assert.Equal(MatchPhase.Ended, session.Match.Phase);
        Assert.Equal(EndReason.MajorRoundLimit, session.Match.Result!.Reason);
        Assert.Equal(session.Match.Result.Standings.Select(s => (s.Player.Value, s.Rank)), log.Result.Standings.Select(s => (s.Player, s.Rank)));
        Assert.All(log.Turns, t => Assert.False(t.Passed));
        Assert.DoesNotContain(log.Events, e => e.Type == "MaxRoundsReached");

        // 跑局配置与对局配置的上限不一致 → 响亮失败（不允许两套上限并存）
        MatchFlow other = MatchFixtures.Started();
        Assert.Equal(15, other.MaxMajorRounds);
        Assert.Throws<SiegeRuleException>(() => MatchSession.ForMatch(other, SimFixtures.Config(maxRounds: 12)));
    }

    [Fact]
    public void 上限为0时硬停以失败局记录()
    {
        // 上限 0 + 永不 Pass 的控制者 → 规则层永不因上限终局；跑局层的小回合硬停抛异常并落 failed 日志，不记为终局原因。
        // 变异验证 M-R12：RunTurn 的 MaxTurns 硬停改为 `if (false)` → 红 1（本测试：跑到盘面无合法落点，异常类型变成 InvalidOperationException）。
        // 变异验证 M-RC3（check）：硬停改为 `return false` 且会话收尾把未终局对局记成 MajorRoundLimit 终局 → 红 1（本测试：IsFailed 为 false）。
        Assert.Equal(1_000, RunConfig.DefaultMaxTurns);
        Assert.Equal(15, RunConfig.DefaultMaxMajorRounds);
        Assert.Throws<ArgumentException>(() => SimFixtures.Config(maxRounds: -1).Validated());
        _ = SimFixtures.Config(maxRounds: 0).Validated();

        RunConfig config = SimFixtures.Config(maxRounds: 0, retention: EventRetention.SnapshotsOnly) with { MaxTurns = 30 };
        MatchSession session = AlwaysPlacing(config, 302);
        Assert.Equal(0, session.Match.MaxMajorRounds);

        MatchLog log = session.Run();
        Assert.True(log.IsFailed);
        Assert.Null(log.Result);
        Assert.Equal(typeof(SimAssertionException).FullName, log.Failure!.ExceptionType);
        Assert.Contains("30", log.Failure.Message);
        Assert.Equal(30, log.Failure.Turn);
        Assert.Contains("failed", log.FileName);
        Assert.Equal(0, log.Header.MaxMajorRounds);
        Assert.Equal(MatchPhase.InProgress, session.Match.Phase);
        Assert.Equal(8, session.Match.MajorRound);   // 30 个小回合 = 7 个完整大回合 + 2 个小回合
    }
}

/// <summary>永不 Pass 的控制者：征募尽量选，部署时在合法范围内按坐标序找第一个预演合法的空格落 1 子。</summary>
internal sealed class AlwaysPlaceController : ITurnController
{
    public void OrganizeHand(PlayerHandAccess hand, int overflow)
    {
        for (int i = 0; i < overflow; i++)
        {
            hand.Discard(hand.PrivateView().Types.First());
        }
    }

    public void Recruit(PlayerHandAccess hand, RecruitPanelView panel)
    {
        for (int i = 0; i < panel.PicksRemaining; i++)
        {
            int[] selectable = [.. hand.Panel().Candidates.Where(c => c.IsSelectable).Select(c => c.Index)];
            if (selectable.Length == 0)
            {
                return;
            }

            hand.Pick(selectable[0]);
        }
    }

    public void Deploy(StagedBatch batch, Func<RehearsalResult> rehearse)
    {
        PieceType[] types = [.. batch.Context.Stock.Where(kv => kv.Value > 0).Select(kv => kv.Key)];
        if (types.Length == 0)
        {
            throw new InvalidOperationException("永不 Pass 的控制者没有棋子可落。");
        }

        foreach (Coord cell in batch.Context.LegalRange.Where(c => batch.Board[c].IsPlayableEmpty).Order())
        {
            if (batch.Stage(cell, types[0]) is not null)
            {
                continue;
            }

            if (rehearse().IsLegal)
            {
                return;
            }

            batch.Unstage(cell);
        }

        throw new InvalidOperationException("永不 Pass 的控制者找不到合法落点。");
    }

    public bool OnRejected(StagedBatch batch, BatchFailure failure) => false;
}
