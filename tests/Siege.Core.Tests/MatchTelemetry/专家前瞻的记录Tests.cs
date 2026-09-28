using System.Security.Cryptography;
using System.Text;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.MatchTelemetry;

/// <summary>
/// 规格：expert-lookahead / match-telemetry —— Requirement: 专家前瞻的记录。
/// 样本一律写死权重与停手阈值（<see cref="LookaheadFixtures.V5Config"/>），种子按段 B 探针挑选：v5 种子 3 整局中专家有已前瞻、Pass 与一次"前瞻改变选择"。
/// 日志端的每条记录都与跑局时从活控制者取下的 <see cref="LookaheadRecord"/> 逐项比对（写入端漏写 / 写错在这里红，而不是只证明"读得出来"）。
/// </summary>
public class 专家前瞻的记录Tests
{
    private static readonly PlayerId P0 = LookaheadFixtures.P0;

    /// <summary>
    /// 1 名专家（P0）+ 3 名标准，v5 种子 3 整局：日志与 P0 每个小回合的活记录（小回合序号 → 记录）。
    /// expert-strength 1.3：本样本钉的是一层前瞻的记录（"种子 3 恰有一次改变选择"等），改为显式的一层配置（专家预设 + 多样补充上限 0 + 两层权重 0，
    /// 阈值 80、候选格上限 0）——与改动前"未显式给搜索配置"时首部落成的搜索配置逐字段相同，断言一字不改。未显式给配置的专家见「回放核对前瞻记录」。
    /// </summary>
    private static readonly Lazy<(MatchLog Log, Dictionary<int, LookaheadRecord?> Live)> ExpertSample = new(() =>
        RunCapturing(LookaheadFixtures.V5Config(OneLayerExpert, LookaheadFixtures.Standard, LookaheadFixtures.Standard, LookaheadFixtures.Standard), 3, P0));

    private static PlayerAiConfig OneLayerExpert => new() { Difficulty = AiDifficulty.Expert, Search = LookaheadFixtures.OneLayerConfig() };

    private static PlayerAiConfig Expert => new() { Difficulty = AiDifficulty.Expert };

    [Fact]
    public void 前瞻前后分数可查()
    {
        (MatchLog log, Dictionary<int, LookaheadRecord?> live) = ExpertSample.Value;
        MatchLog parsed = MatchLog.Parse(log.FullText());   // 落盘文本往返：写出时漏字段在这里红

        TurnSnapshot[] expertTurns = [.. parsed.Turns.Where(t => t.Player == P0.Value)];
        Assert.Equal(live.Count, expertTurns.Length);
        Assert.Equal(expertTurns.Select(t => t.Turn), parsed.LookaheadTurns.Select(t => t.Turn));
        foreach (TurnSnapshot turn in expertTurns)
        {
            Assert.Equal(LiveText(live[turn.Turn]!), LogText(turn.Lookahead!));
        }

        // 样本口径下界：至少一次已前瞻、候选里有真实的模拟回应（非 Pass），且有候选的前瞻后分数不同于前瞻前分数。
        LookaheadLogEntry[] applied = [.. parsed.LookaheadTurns.Select(t => t.Lookahead!).Where(l => l.Status == nameof(LookaheadStatus.Applied))];
        Assert.True(applied.Length >= 3, $"已前瞻 {applied.Length} 次");
        Assert.Contains(applied, l => l.Candidates.Any(c => c.Responder is not null && !string.IsNullOrEmpty(c.Response)));
        Assert.Contains(applied, l => l.Candidates.Any(c => c.Before != c.After));
        Assert.All(applied, l => Assert.InRange(l.Candidates.Count, 2, AiSearchConfig.DefaultLookaheadWidth));
        Assert.All(applied, l => Assert.True(l.SimulatedRehearsals > 0));

        // 被选候选的前瞻前后分数：与日志快照里实际落下的批次一致。
        foreach (TurnSnapshot turn in parsed.LookaheadTurns.Where(t => t.Lookahead!.Status == nameof(LookaheadStatus.Applied)))
        {
            LookaheadCandidateEntry chosen = turn.Lookahead!.ChosenCandidate!;
            Assert.Equal(string.Join(",", turn.Placements.Select(p => p.Split(':')[0]).Order(StringComparer.Ordinal)),
                string.Join(",", LookaheadFixtures.CoordsOf(chosen.Batch).Select(c => c.ToNotation()).Order(StringComparer.Ordinal)));
        }
    }

    [Fact]
    public void 选择变化可查()
    {
        (MatchLog log, _) = ExpertSample.Value;
        LookaheadLogEntry[] records = [.. MatchLog.Parse(log.FullText()).LookaheadTurns.Select(t => t.Lookahead!)];
        LookaheadLogEntry changed = Assert.Single(records, r => r.Changed);   // 样本口径：种子 3 恰有一次（段 B 探针）
        Assert.True(changed.Chosen >= 1);
        LookaheadCandidateEntry first = changed.Candidates[0];
        LookaheadCandidateEntry chosen = changed.ChosenCandidate!;
        Assert.NotEqual(first.Batch, chosen.Batch);
        Assert.True(first.Before >= chosen.Before, "前瞻集按自身分排序，第一个的前瞻前分数不低于被选者");
        Assert.True(chosen.After > first.After, "被选者的前瞻后分数严格高于第一个（同分取原次序）");
        Assert.All(records, r => Assert.Equal(r.Chosen > 0, r.Changed));
    }

    [Fact]
    public void 不前瞻与Pass也有记录()
    {
        // Pass：整局以整轮 Pass 结束，专家最后的决策是 Pass，记录状态为 Pass、前瞻集为空、下标 −1。
        (MatchLog log, Dictionary<int, LookaheadRecord?> live) = ExpertSample.Value;
        LookaheadLogEntry[] pass = [.. MatchLog.Parse(log.FullText()).LookaheadTurns.Select(t => t.Lookahead!).Where(l => l.Status == nameof(LookaheadStatus.Pass))];
        Assert.NotEmpty(pass);
        Assert.All(pass, l => Assert.Empty(l.Candidates));
        Assert.All(pass, l => Assert.Equal(-1, l.Chosen));
        Assert.All(pass, l => Assert.Null(l.ChosenCandidate));
        Assert.Contains(live.Values, r => r!.Status == LookaheadStatus.Pass);

        // 不前瞻：前瞻宽度 1 的专家（经配置文件的 Search 显式给出）每次非 Pass 的决策都记"不前瞻"，前瞻集只有 1 个候选、前后分数相同、无对手。
        RunConfig w1 = LookaheadFixtures.V5Config(
            new PlayerAiConfig { Difficulty = AiDifficulty.Expert, Search = LookaheadFixtures.ExpertConfig(LookaheadFixtures.PassThreshold, width: 1) with { CandidateCellLimit = AiSearchConfig.LargeMapCellLimit } },
            LookaheadFixtures.Standard, LookaheadFixtures.Standard, LookaheadFixtures.Standard) with { TurnLimit = 8 };
        (MatchLog small, _) = RunCapturing(w1, 1, P0);
        LookaheadLogEntry[] notApplied = [.. MatchLog.Parse(small.FullText()).LookaheadTurns.Select(t => t.Lookahead!)];
        Assert.NotEmpty(notApplied);
        Assert.Contains(notApplied, l => l.Status == nameof(LookaheadStatus.NotApplied));
        Assert.All(notApplied.Where(l => l.Status == nameof(LookaheadStatus.NotApplied)), l =>
        {
            LookaheadCandidateEntry only = Assert.Single(l.Candidates);
            Assert.Equal(0, l.Chosen);
            Assert.False(l.Changed);
            Assert.Equal(only.Before, only.After);
            Assert.Null(only.Responder);
        });
    }

    [Fact]
    public void 非专家没有前瞻记录()
    {
        // 4 名标准：日志中没有任何前瞻记录，确定性文本（含首部）与改动前逐字节相同——黄金值与 难度分级Tests.三档旧难度逐步不变 的标准档同一个（HEAD 03d45f6 钉下）。
        // 变异 M-B3a（非专家也写前瞻记录：控制者没有记录时写一条 Pass）→ 本测试红。
        MatchSession session = MatchSession.Create(LookaheadFixtures.V5Config(
            LookaheadFixtures.Standard, LookaheadFixtures.Standard, LookaheadFixtures.Standard, LookaheadFixtures.Standard), 1);
        MatchLog log = session.Run();
        string text = log.DeterministicText();
        Assert.Empty(log.LookaheadTurns);
        Assert.DoesNotContain("Lookahead", log.FullText(), StringComparison.Ordinal);
        Assert.Equal(28, log.Turns.Count);
        Assert.Equal("29353FC976C82867E9DD76AE7229223B876146D5DA53F523366824A7A407736A", Sha256(text));
    }

    [Fact]
    public void 旧日志照常解析()
    {
        // 引入专家难度之前产生的日志：4 名标准的日志与改动前逐字节相同（上一条的黄金值），直接当作旧日志离线解析、回放。
        // 另造一份首部里带显式 Search（无前瞻宽度字段）的旧式日志：前瞻宽度读为 0。
        MatchLog fresh = MatchSession.Create(LookaheadFixtures.V5Config(
            LookaheadFixtures.Standard, LookaheadFixtures.Standard, LookaheadFixtures.Standard, LookaheadFixtures.Standard) with { TurnLimit = 12 }, 1).Run();
        MatchLog old = MatchLog.Parse(fresh.FullText());
        Assert.Empty(old.LookaheadTurns);
        Assert.All(old.Turns, t => Assert.Null(t.Lookahead));
        Assert.Equal([0, 0, 0, 0], Enumerable.Range(0, 4).Select(old.LookaheadWidthOf));
        ReplayResult replay = Replayer.Replay(old);
        Assert.True(replay.Identical, replay.ToString());

        const string legacySearch = """{"CandidatePointCount":24,"CandidateBatchCount":32,"ImmediateOnly":false,"CandidateCellLimit":24,"PassThreshold":80}""";
        string header = fresh.DeterministicText().Split('\n')[0];
        string withSearch = header.Replace("\"Difficulty\":\"Standard\"", "\"Difficulty\":\"Hard\",\"Search\":" + legacySearch, StringComparison.Ordinal);
        Assert.NotEqual(header, withSearch);
        MatchLog legacy = MatchLog.Parse(withSearch + "\n");
        Assert.All(legacy.Header.Config.Players, p => Assert.NotNull(p.Search));
        Assert.Equal([0, 0, 0, 0], Enumerable.Range(0, 4).Select(legacy.LookaheadWidthOf));
    }

    [Fact]
    public void 回放核对前瞻记录()
    {
        // 1 名未显式给搜索配置的专家（首部落成前瞻宽度 4）+ 1 名显式前瞻宽度 2 的专家 + 2 名标准；回放按首部重建前瞻宽度，重新产生的每条前瞻记录与日志逐项相同。
        // 显式宽度 2 的那名让"回放不读首部的前瞻宽度"两种写法（归 0 / 回落到难度预设 4）都会分歧。
        // 变异 M-B3c（回放不读首部的前瞻宽度）→ 本测试红。
        PlayerAiConfig w2 = new() { Difficulty = AiDifficulty.Expert, Search = LookaheadFixtures.ExpertConfig(LookaheadFixtures.PassThreshold, width: 2) with { CandidateCellLimit = AiSearchConfig.LargeMapCellLimit } };
        RunConfig config = LookaheadFixtures.V5Config(Expert, w2, LookaheadFixtures.Standard, LookaheadFixtures.Standard) with { TurnLimit = 16 };
        MatchLog log = MatchSession.Create(config, 2).Run();
        string path = log.WriteTo(SimFixtures.TempDir("lookahead-replay"));

        string header = File.ReadLines(path).First();
        Assert.Contains("\"LookaheadWidth\":4", header, StringComparison.Ordinal);
        Assert.Contains("\"LookaheadWidth\":2", header, StringComparison.Ordinal);
        MatchLog original = MatchLog.Read(path);
        Assert.Equal([4, 2, 0, 0], Enumerable.Range(0, 4).Select(original.LookaheadWidthOf));
        Assert.Contains(original.LookaheadTurns, t => t.Player == 0 && t.Lookahead!.Status == nameof(LookaheadStatus.Applied));
        Assert.Contains(original.LookaheadTurns, t => t.Player == 1 && t.Lookahead!.Status == nameof(LookaheadStatus.Applied));
        Assert.All(original.LookaheadTurns.Where(t => t.Player == 1), t => Assert.True(t.Lookahead!.Candidates.Count <= 2));

        ReplayResult replay = Replayer.Replay(original);
        Assert.True(replay.Identical, replay.ToString());
        Assert.Equal(original.LookaheadTurns.Select(t => LogText(t.Lookahead!)), replay.Replayed.LookaheadTurns.Select(t => LogText(t.Lookahead!)));

        // 篡改原日志里一条前瞻记录的前瞻后分数：回放 MUST 在该小回合那一行报分歧（前瞻记录参与逐行核对，不是回放输入）。
        TurnSnapshot target = original.LookaheadTurns.First(t => t.Lookahead!.Status == nameof(LookaheadStatus.Applied));
        LookaheadCandidateEntry tampered = target.Lookahead!.Candidates[0] with { After = target.Lookahead.Candidates[0].After + 1 };
        var forged = new MatchLog
        {
            Header = original.Header,
            Turns = [.. original.Turns.Select(t => t.Turn == target.Turn
                ? t with { Lookahead = t.Lookahead! with { Candidates = [tampered, .. t.Lookahead.Candidates.Skip(1)] } }
                : t)],
            Events = original.Events,
            Result = original.Result,
        };
        ReplayResult diverged = Replayer.Replay(forged);
        Assert.False(diverged.Identical);
        Assert.Contains($"\"Turn\":{target.Turn},", diverged.Expected, StringComparison.Ordinal);
    }

    /// <summary>逐小回合跑一局，记下 <paramref name="expert"/> 每个小回合结束时的活记录（小回合序号 → 记录），最后收尾出日志。</summary>
    private static (MatchLog Log, Dictionary<int, LookaheadRecord?> Live) RunCapturing(RunConfig config, ulong seed, PlayerId expert)
    {
        MatchSession session = MatchSession.Create(config, seed);
        var live = new Dictionary<int, LookaheadRecord?>();
        while (true)
        {
            PlayerId? current = session.Match.CurrentPlayer;
            int before = session.TurnCount;
            bool more = session.RunTurn();
            if (session.TurnCount > before && current == expert)
            {
                live[session.TurnCount] = session.AiOf(expert)!.LastLookahead;
            }

            if (!more)
            {
                break;
            }
        }

        return (session.Run(), live);
    }

    /// <summary>活记录的独立文本投影（逐字段，不经日志端的转写函数）。</summary>
    private static string LiveText(LookaheadRecord record) =>
        $"{record.Status}|{record.ChosenIndex}|{record.ChangedChoice}|{record.SimulatedRehearsals}|"
        + string.Join(";", record.Entries.Select(e =>
            $"{e.CandidateKey}/{e.ScoreBefore}/{e.Responder?.Value.ToString() ?? "-"}/{e.ResponderRound?.ToString() ?? "-"}/{e.SimulatedDeployLimit?.ToString() ?? "-"}/{e.ResponseKey ?? "-"}/{e.ScoreAfter}"));

    /// <summary>日志记录的同形文本投影。</summary>
    private static string LogText(LookaheadLogEntry entry) =>
        $"{entry.Status}|{entry.Chosen}|{entry.Changed}|{entry.SimulatedRehearsals}|"
        + string.Join(";", entry.Candidates.Select(c =>
            $"{c.Batch}/{c.Before}/{c.Responder?.ToString() ?? "-"}/{c.ResponderRound?.ToString() ?? "-"}/{c.DeployLimit?.ToString() ?? "-"}/{c.Response ?? "-"}/{c.After}"));

    private static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
