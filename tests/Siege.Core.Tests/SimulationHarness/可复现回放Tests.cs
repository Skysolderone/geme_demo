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
    public void 带入对局可回放()
    {
        // 规格 Scenario：回放一局本机玩家带入换型令（连珠子）、三名 AI 各带入 1 件的对局日志 → 开局手牌与原局相同，每一步的征募候选、批次与结算结果逐项一致。
        // 带入按人机对局的口径给出（CarryAi.ForHumanMatch：P0 的换型令不可由种子推出），所以回放必须读首部记录的带入——按配置或种子重抽都会分歧。
        // 变异 M-T1：Replayer 不传首部带入（按配置重建）→ 本测试红。
        const ulong seed = 23;
        var game = new Siege.Core.Determinism.GameSeed(seed);
        System.Collections.Immutable.ImmutableSortedDictionary<PlayerId, Siege.Core.Carry.CarryIn> carries = Siege.Core.Carry.CarryAi.ForHumanMatch(
            game, MatchFixtures.All, MatchFixtures.P0, new Siege.Core.Carry.CarryIn(Siege.Core.Carry.SupplyKind.Commission, PieceType.Line), ContentSet.V2);
        Assert.Equal(4, carries.Count);                                   // 前提：三名 AI 各带 1 件
        MatchSession original = MatchTelemetry.对局日志的记录内容Tests.CarriedSession(seed, carries.ToDictionary(kv => kv.Key.Value, kv => kv.Value), turnLimit: 12);
        string opening = string.Join(" | ", original.Match.Players.Select(p => CarryInOut.CarryFixtures.HandText(original.Match, p)));
        MatchLog log = original.Run();
        string path = log.WriteTo(SimFixtures.TempDir("replay-carry"));

        ReplayResult replay = Replayer.ReplayFile(path);

        Assert.True(replay.Identical, replay.ToString());
        Assert.Equal(log.DeterministicText(), replay.Replayed.DeterministicText());
        Assert.True(replay.LineCount > log.Turns.Count + log.Events.Count, $"回放只比了 {replay.LineCount} 行");
        Assert.Equal(SimFixtures.TurnTexts(log.Turns), SimFixtures.TurnTexts(replay.Replayed.Turns));
        Assert.Equal(CarryInOut.CarryFixtures.CarryText(log.CarryIns), CarryInOut.CarryFixtures.CarryText(replay.Replayed.CarryIns));
        Assert.Contains("P0:Commission>Line", CarryInOut.CarryFixtures.CarryText(replay.Replayed.CarryIns), StringComparison.Ordinal);
        Assert.Contains("Line×1+0", opening, StringComparison.Ordinal);   // 开局手牌确实含换入的连珠子
    }

    [Fact]
    public void 旧日志按无带入回放()
    {
        // 规格正文（可复现回放）：缺带入字段的旧日志按无带入回放。旧日志样本：一局新写出的关闭带入的日志删掉首部三项（开关、带入、配置的带入数量），
        // 引入带入带出之前的首部就是如此。回放 MUST 逐行一致——重建出的首部也不得多出这三项（否则第 1 行即分歧）。
        // 变异 M-T7：按缺字段的旧日志回放时仍写首部带入两项 → 本测试红。
        MatchLog log = MatchSession.Create(SimFixtures.Config(turnLimit: 8), 17).Run();
        string[] lines = log.FullText().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var header = System.Text.Json.Nodes.JsonNode.Parse(lines[0])!.AsObject();
        Assert.True(header.Remove("CarryInOut") && header.Remove("CarryIns") && header["Config"]!.AsObject().Remove("CarryIn"), "新首部应写出三项");
        lines[0] = header.ToJsonString();
        string dir = SimFixtures.TempDir("replay-legacy-carry");
        string path = Path.Combine(dir, "match-legacy.jsonl");
        File.WriteAllText(path, string.Join('\n', lines) + "\n");

        ReplayResult replay = Replayer.ReplayFile(path);

        Assert.True(replay.Identical, replay.ToString());
        Assert.Null(replay.Replayed.Header.CarryInOut);
        Assert.Null(replay.Replayed.Header.Config.CarryIn);
        Assert.True(replay.LineCount > log.Turns.Count, $"回放只比了 {replay.LineCount} 行");
    }

    [Fact]
    public void 失败局可复现()
    {
        // 批量跑局中某局因断言失败而终止 → 保留种子、配置与终止前完整日志（文件名带 failed），可单独重跑复现同一失败。
        // 用配置里的测试专用注入点在第 5 个小回合触发断言失败；批量里其他局不受影响。
        // 变异验证 M-B21：MatchSession.Fail 丢弃已记录的事件（Events = []）→ 红 1（本测试）。
        // 权重与停手阈值写死为 ai-eye 4.5 定值之前的缺省：三局都要走到第 5 个小回合才触发注入（默认阈值 80 下简单难度可能第 1 大回合全员 Pass 终局；段 D2 改写）。
        RunConfig config = SimFixtures.PinPreCalibration(
            SimFixtures.Config(count: 3, seedStart: 51, turnLimit: 12, retention: EventRetention.SnapshotsOnly, injectFailureAtTurn: 5));
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
        // 首部配置 = 跑局配置（阈值已显式写死；"未配置的阈值落成缺省值写入首部"由 停手阈值Tests.阈值进入记录 钉住）。
        // carry-in-out 段 C：未配置的带入数量落成 0 写进首部（写明关闭），期望随之补上这一项。
        Assert.Equal((config with { CarryIn = 0 }).ToJson(), failed.Header.Config.ToJson());
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
