using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>
/// 跑局层的小回合硬停（防死锁保险）：规则层已无大回合上限（restore-go-core-rules 裁决 #4），永不 Pass 的控制者只会被硬停拦下，
/// 抛异常并落 failed 日志，不记为终局原因。段 E（tasks 5.1）改为 <c>turn_limit</c> 截断时重写本类。
/// </summary>
public class 防死锁硬停Tests
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
    public void 规则层无上限时硬停以失败局记录()
    {
        // 永不 Pass 的控制者 → 规则层永不因轮数终局；跑局层的小回合硬停抛异常并落 failed 日志，不记为终局原因。
        // 变异验证 M-R12：RunTurn 的 MaxTurns 硬停改为 `if (false)` → 红 1（本测试：跑到盘面无合法落点，异常类型变成 InvalidOperationException）。
        // 变异验证 M-RC3（check）：硬停改为 `return false` 且会话收尾把未终局对局记成 MajorRoundLimit 终局 → 红 1（本测试：IsFailed 为 false）。
        Assert.Equal(1_000, RunConfig.DefaultMaxTurns);

        RunConfig config = SimFixtures.Config(retention: EventRetention.SnapshotsOnly) with { MaxTurns = 30, TurnLimit = 0, FlagRisk = 0 };   // flag-contest：写死冒险概率 0   // 截断关闭（0），只剩硬停兜底
        MatchSession session = AlwaysPlacing(config, 302);

        MatchLog log = session.Run();
        Assert.True(log.IsFailed);
        Assert.Null(log.Result);
        Assert.Equal(typeof(SimAssertionException).FullName, log.Failure!.ExceptionType);
        Assert.Contains("30", log.Failure.Message);
        Assert.Equal(30, log.Failure.Turn);
        Assert.Contains("failed", log.FileName);
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
