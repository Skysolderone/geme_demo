using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Sim.Analysis;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.MatchTelemetry;

/// <summary>
/// 规格：match-telemetry「决赛倒计时的记录」（final-countdown 2.1）：日志首部记录 K（K = 0 时照写，见 <c>决赛倒计时配置Tests.缺省值</c>）；战场结算记录的原因可取"到期"并带期限大回合；
/// 每个进入倒计时的战场记录其期限；离线分析统计到期结算次数与到期时势力差（等待者决赛胜率、等待大回合数、决赛开局势力差沿用「等待者评估」）。
/// 样本：<c>link:1</c>、对局种子 1、4 名标准 AI、脚本缺省配对、K = 2（与决策序列基线种子 1 同口径：同区战场第 7 大回合结算 → 战场 0 期限第 9 大回合、到期 P3 164 胜 P2 108），
/// 截到 40 个小回合。权重与停手阈值写死为当前取值，不跟随缺省（testing.md「依赖 AI 实际怎么走的断言要把权重写死」）。
/// </summary>
/// <remarks>
/// 变异验证（final-countdown 2.1 实跑，见 implement.md）：M-FC8 日志的结算记录不写期限 → 红 1（「到期结算可查」）；M-FC9 分析端把到期时势力差算成第二名 − 胜者 → 红 1（「分析统计到期结算」）；
/// M-FC12 回放时无视首部 K、一律按 0 重建 → 红 1（「首部K按记录回放」）。
/// </remarks>
public class 决赛倒计时的记录Tests
{
    private static readonly Lazy<(MatchLog Log, MatchFlow Match)> Sample = new(() =>
    {
        var config = new RunConfig
        {
            MapId = "link:1",
            SeedStart = 1,
            Count = 1,
            TurnLimit = 40,
            PassThreshold = Core.Ai.AiSearchConfig.DefaultPassThreshold,
            FinalCountdownRounds = 2,
            Players = [.. Enumerable.Range(0, 4).Select(_ => new PlayerAiConfig { Difficulty = Core.Ai.AiDifficulty.Standard, Weights = Core.Ai.EvaluationWeights.Default })],
        };
        MatchSession session = MatchSession.Create(config, 1, MapCatalog.Resolve(config.MapId), recorded: false, carry: null, pairing: new ScriptedPairingSource());
        return (session.Run(), session.Match);
    });

    [Fact(Skip = "open-map 段 B 删除")]
    public void 到期结算可查()
    {
        // 规格 Scenario：读取一局发生过到期结算的日志 → 首部有 K，战场结算记录的原因为"到期"并带期限大回合。落盘文本往返后逐项与活对象比。
        (MatchLog live, MatchFlow match) = Sample.Value;
        MatchLog log = MatchLog.Parse(live.FullText());

        Assert.Equal(2, log.Header.Config.FinalCountdownRounds);
        Assert.Equal(2, log.FinalCountdownRounds);
        SettlementLogEntry expired = Assert.Single(log.Events.Where(e => e.Settlement is { Reason: nameof(SettlementReason.Expired) }).Select(e => e.Settlement!));
        Assert.Equal((9, 0, (int?)9), (expired.MajorRound, expired.Battlefield, expired.Deadline));
        Assert.Equal(match.Settlements.Select(s => (s.Sequence, s.MajorRound, s.Battlefield, s.Reason.ToString(), s.Deadline)),
            log.Events.Where(e => e.Settlement is not null).Select(e => (e.Settlement!.Sequence, e.Settlement.MajorRound, e.Settlement.Battlefield, e.Settlement.Reason, e.Settlement.Deadline)));

        FinalCountdownLogEntry countdown = Assert.Single(log.Events.Where(e => e.Countdown is not null).Select(e => e.Countdown!));
        Assert.Equal((1, 0, 7, 9, 7), (countdown.Sequence, countdown.Battlefield, countdown.Start, countdown.Deadline, countdown.AssignedInMajorRound));
        Assert.Equal(match.FinalCountdowns.Select(c => (c.Sequence, c.Battlefield, c.Start, c.Deadline, c.AssignedInMajorRound)),
            log.Events.Where(e => e.Countdown is not null).Select(e => (e.Countdown!.Sequence, e.Countdown.Battlefield, e.Countdown.Start, e.Countdown.Deadline, e.Countdown.AssignedInMajorRound)));
        // 事件与配对事件同口径：在第 7 大回合结束的推进之内发出，记在已推进到的第 8 大回合（赋期限的大回合见 AssignedInMajorRound）。
        Assert.Contains(log.Events, e => e.Type == LogEventType.FinalCountdown && e.MajorRound == 8);
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 首部K按记录回放()
    {
        // 回放按日志首部重建（simulation-harness「可复现回放」）：首部 K = 2 的局按 2 重建，到期结算在回放里逐行复现（首部缺 K 的旧日志按 0，由 expert-lookahead 的旧日志夹具守）。
        (MatchLog live, _) = Sample.Value;
        string path = live.WriteTo(SimFixtures.TempDir("final-countdown-replay"));

        ReplayResult replay = Replayer.ReplayFile(path);

        Assert.True(replay.Identical, replay.ToString());
        Assert.True(replay.LineCount > live.Turns.Count, $"回放只比了 {replay.LineCount} 行");
        Assert.Equal(live.DeterministicText(), replay.Replayed.DeterministicText());
        Assert.Contains(replay.Replayed.Events, e => e.Settlement is { Reason: nameof(SettlementReason.Expired), Deadline: 9 });
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 篡改首部K或结算期限即报分歧()
    {
        // final-countdown check 补测（派发项 5）：回放按首部 K 重建、期限随确定性文本逐行比对。
        // ① 首部 K 改成 0：回放按 0 重建，首部行两边一致，分歧出现在其后（原局第 7 大回合结束的倒计时事件 / 第 9 大回合的到期结算重放不出来）；
        // ② 只改到期结算记录的期限（9 → 10）：分歧恰报在该结算事件那一行。
        (MatchLog live, _) = Sample.Value;
        string text = live.FullText();
        Assert.Contains("\"FinalCountdownRounds\":2,", text, StringComparison.Ordinal);
        Assert.True(Replayer.Replay(MatchLog.Parse(text)).Identical);   // 反面：未篡改的一致

        ReplayResult k = Replayer.Replay(MatchLog.Parse(text.Replace("\"FinalCountdownRounds\":2,", "\"FinalCountdownRounds\":0,", StringComparison.Ordinal)));
        Assert.False(k.Identical);
        Assert.True(k.FirstDivergentLine > 1, k.ToString());
        Assert.DoesNotContain(k.Replayed.Events, e => e.Countdown is not null || e.Settlement is { Reason: nameof(SettlementReason.Expired) });

        MatchLog deadline = SimFixtures.Clone(live);
        int at = deadline.Events.FindIndex(e => e.Settlement is { Reason: nameof(SettlementReason.Expired) });
        SettlementLogEntry settled = deadline.Events[at].Settlement!;
        Assert.Equal(9, settled.Deadline);
        string settledLine = System.Text.Json.JsonSerializer.Serialize(deadline.Events[at], LogJson.Options);
        int expectedLine = Array.IndexOf(live.DeterministicText().Split('\n'), settledLine) + 1;
        Assert.True(expectedLine > 1, "样本口径：确定性文本里找到到期结算那一行");
        deadline.Events[at] = deadline.Events[at] with { Settlement = settled with { Deadline = 10 } };
        ReplayResult tampered = Replayer.Replay(deadline);
        Assert.False(tampered.Identical);
        Assert.Equal(expectedLine, tampered.FirstDivergentLine);
        Assert.Contains("\"Deadline\":10", tampered.Expected!, StringComparison.Ordinal);
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 分析统计到期结算()
    {
        // 离线分析：到期结算次数、所在大回合、到期时势力差（胜者 − 第二名，测试内独立算式：164 − 108）、K 分布与起点。
        // 口径样本：同一局抹掉首部 K 当作旧日志（按 0 计、单列缺项）一并纳入——K 分布与缺项计数要把它分开。
        (MatchLog live, _) = Sample.Value;
        string text = live.FullText();
        Assert.Contains("\"FinalCountdownRounds\":2,", text, StringComparison.Ordinal);
        MatchLog old = MatchLog.Parse(text.Replace("\"FinalCountdownRounds\":2,", string.Empty, StringComparison.Ordinal));
        Assert.Null(old.Header.Config.FinalCountdownRounds);

        FinalCountdownSection section = FinalCountdownAnalyzer.Analyze([live, old])!;
        Assert.Null(FinalCountdownAnalyzer.Analyze([old]));   // 只有 K = 0（缺项）的样本：整段不出现

        Assert.Equal(2, section.Matches);
        Assert.Equal("0:1 2:1", string.Join(" ", section.Ks.Select(kv => $"{kv.Key}:{kv.Value}")));
        Assert.Equal(1, section.MissingK);
        Assert.Equal(2, section.Started);
        Assert.Equal("7:2", string.Join(" ", section.Starts.Select(kv => $"{kv.Key}:{kv.Value}")));
        Assert.Equal(2, section.ExpiredCount);
        Assert.Equal("9:2", string.Join(" ", section.ExpiredRounds.Select(kv => $"{kv.Key}:{kv.Value}")));
        Assert.Equal([164 - 108, 164 - 108], section.ExpiredGaps.Select(g => (int)g));
        Assert.Equal((2, 2), (section.SettlementReasons[nameof(SettlementReason.AllPassed)], section.SettlementReasons[nameof(SettlementReason.Expired)]));

        string report = ReportWriter.Render(BalanceAnalyzer.Analyze([live]));
        Assert.Contains("## 决赛倒计时（final-countdown", report, StringComparison.Ordinal);
        Assert.Contains("到期结算 1 次", report, StringComparison.Ordinal);
    }
}
