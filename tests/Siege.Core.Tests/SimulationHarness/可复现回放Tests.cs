using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>规格：simulation-harness —— Requirement: 可复现回放</summary>
public class 可复现回放Tests
{
    [Fact]
    public void 纯AI局可凭种子复现()
    {
        // 用某局的种子与配置重跑 → 每一步的征募候选、选择、批次与结算结果完全一致：日志逐行一致，AI 的决策序列与候选集合也一致。
        // 标准难度带种子扰动子流（ai-P{n}），一并覆盖。
        // 变异验证：M-B16 / M-B23（种子被扰动）均使本测试红；本类专属变异见 失败局可复现 的 M-B21。
        RunConfig config = SimFixtures.Config(turnLimit: 8, difficulty: AiDifficulty.Standard);
        MatchSession a = MatchSession.Create(config, 41);
        MatchLog logA = a.Run();
        string dir = SimFixtures.TempDir("replay");
        string path = logA.WriteTo(dir);

        ReplayResult replay = Replayer.ReplayFile(path);

        Assert.True(replay.Identical, replay.ToString());
        Assert.Equal(logA.DeterministicText(), replay.Replayed.DeterministicText());
        // 比对的文本必须覆盖快照与事件（M-C5：DeterministicText 只出 header 时上面两条恒真）
        Assert.True(replay.LineCount > logA.Turns.Count + logA.Events.Count, $"回放只比了 {replay.LineCount} 行");
        Assert.Equal(SimFixtures.TurnTexts(logA.Turns), SimFixtures.TurnTexts(replay.Replayed.Turns));
        Assert.False(replay.Replayed.IsFailed);
        Assert.Equal(AiDifficulty.Standard, replay.Replayed.Header.Config.Players[0].Difficulty);

        MatchSession b = MatchSession.Create(config, 41);
        b.Run();
        foreach (PlayerId p in a.Match.Players)
        {
            HeuristicTurnController aiA = a.AiOf(p)!;
            HeuristicTurnController aiB = b.AiOf(p)!;
            Assert.NotEmpty(aiA.Decisions);
            Assert.Equal(aiA.Decisions, aiB.Decisions);
            Assert.Equal(aiA.LastCandidates.Select(c => c.ToString()), aiB.LastCandidates.Select(c => c.ToString()));
        }

        Assert.Equal(a.Match.Serialize(), b.Match.Serialize());
        Assert.Equal(a.Match.Hands.Records, b.Match.Hands.Records);

        // 不同种子不会被误判为一致
        MatchLog other = MatchSession.Create(config, 42).Run();
        ReplayResult mismatch = Replayer.Compare(logA, other);
        Assert.False(mismatch.Identical);
        Assert.Equal(1, mismatch.FirstDivergentLine);
    }

    [Fact]
    public void 失败局可复现()
    {
        // 批量跑局中某局因断言失败而终止 → 保留种子、配置与终止前完整日志（文件名带 failed），可单独重跑复现同一失败。
        // 用配置里的测试专用注入点在第 5 个小回合触发断言失败；批量里其他局不受影响。
        // 变异验证 M-B21：MatchSession.Fail 丢弃已记录的事件（Events = []）→ 红 1（本测试）。
        RunConfig config = SimFixtures.Config(count: 3, seedStart: 51, turnLimit: 12, retention: EventRetention.SnapshotsOnly, injectFailureAtTurn: 5);
        string dir = SimFixtures.TempDir("failed");

        BatchSummary summary = BatchRunner.ExecuteToDirectory(config, dir, parallelism: 3);

        Assert.Equal((3, 0, 3), (summary.Count, summary.Completed, summary.Failed));
        Assert.Equal(3, summary.FailedFiles.Count);
        Assert.All(summary.FailedFiles, f => Assert.EndsWith("-failed.jsonl", f));
        Assert.Empty(Directory.GetFiles(dir, "match-*.jsonl").Where(f => !f.EndsWith("-failed.jsonl", StringComparison.Ordinal)));

        MatchLog failed = MatchLog.Read(Path.Combine(dir, "match-0000000000000033-failed.jsonl"));
        Assert.True(failed.IsFailed);
        Assert.Null(failed.Result);
        Assert.Equal(51UL, failed.Seed);
        Assert.Equal(5, failed.Failure!.Turn);
        Assert.Equal(typeof(SimAssertionException).FullName, failed.Failure.ExceptionType);
        Assert.Contains("第 5 个小回合", failed.Failure.Message);
        Assert.Equal(5, failed.Turns.Count);
        // ai-eye 段 B：首部配置里未配置的停手阈值落成实际生效的缺省值（ai-decision「停手阈值」：实际生效的阈值 MUST 写入日志首部）。
        Assert.Equal((config with { PassThreshold = Core.Ai.AiSearchConfig.DefaultPassThreshold }).ToJson(), failed.Header.Config.ToJson());
        // 失败局强制保留完整事件流（含细粒度事件），不受 SnapshotsOnly 影响
        Assert.Equal(EventRetention.Full, failed.Header.Retention);
        Assert.Contains(failed.Events, e => e.Type == LogEventType.Candidates);
        Assert.Contains(failed.Events, e => e.Type == LogEventType.MajorRoundEnded && e.MajorRound == 1);

        ReplayResult replay = Replayer.Replay(failed);
        Assert.True(replay.Identical, replay.ToString());
        Assert.True(replay.Replayed.IsFailed);
        Assert.Equal(failed.Failure.ExceptionType, replay.Replayed.Failure!.ExceptionType);
        Assert.Equal(5, replay.Replayed.Failure.Turn);

        // 去掉注入点重跑同一种子 → 正常完成，前 5 个小回合与失败局逐条一致
        MatchLog healthy = MatchSession.Create(config with { InjectFailureAtTurn = null, EventRetention = EventRetention.Full }, 51).Run();
        Assert.False(healthy.IsFailed);
        Assert.Equal(SimFixtures.TurnTexts(failed.Turns), SimFixtures.TurnTexts(healthy.Turns.Take(5)));
    }
}
