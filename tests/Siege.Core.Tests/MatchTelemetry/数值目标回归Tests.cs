using Siege.Sim.Analysis;
using Siege.Sim.Logging;

namespace Siege.Core.Tests.MatchTelemetry;

/// <summary>规格：match-telemetry —— Requirement: 数值目标回归（末尾附 implement 8.7 的旁证警告守门）</summary>
public class 数值目标回归Tests
{
    [Fact]
    public void 领先者胜率回归()
    {
        // "至少 200 局"的规模走 CLI；这里用真实小样本验证机制，再把样本克隆成 200 局验证样本门槛与置信区间（Wilson，裁决 5）。
        // restore-go-core-rules 段 E：原样本 SimFixtures.Sample 在段 C 之后 4 局全是 turn_limit 截断局、胜者全空——本测试当时的"绿"是空证。
        // 改用有名次的真实样本 SimFixtures.RankedSample（4 局规则级终局，胜者轮换），并把 4 局截断样本混进来作为"被排除的样本"：
        // 期望值由测试内独立判定（读第 3 大回合结束事件的名次 + 结果行胜者），不回调被测的 LeadersAtRound3。
        // 变异验证 M-B5（段 E 重跑）：LeadersAtRound3 取 Rank == 2 的玩家 → 实跑红 3（本测试、截断局不污染胜率、默认排除调试局）。
        // 变异验证 M-E3：Analyze 把领先者段的输入由"有名次的局"改回全部纳入局 → 实跑红 2（本测试、截断局不污染胜率；样本 4 → 8）。
        // 变异验证 M-E3b：Leader 的胜者判定改为"恒不获胜"（anyWins 不累加）→ 实跑红 2（同上；原样本上恒绿的就是这一形状）。
        List<MatchLog> ranked = SimFixtures.RankedSample.Value;
        List<MatchLog> truncated = SimFixtures.Sample.Value;
        foreach (MatchLog log in ranked)
        {
            Assert.True(log.Result!.Converged);
            Assert.NotEmpty(log.Result.Winners);
            LogEvent third = log.Events.Single(e => e.Type == LogEventType.MajorRoundEnded && e.MajorRound == 3);
            List<int> leaders = BalanceAnalyzer.LeadersAtRound3(log);
            Assert.NotEmpty(leaders);
            Assert.Equal(log.Header.Players.Where(p => third.Values![$"P{p}.Rank"] == 1), leaders);
        }

        int wins = ranked.Count(SimulationHarness.批量跑局Tests.LeaderWon);
        Assert.InRange(wins, 1, ranked.Count - 1);   // 样本口径：领先者既有赢也有输，恒真 / 恒假的判定都抓得到

        BalanceReport small = BalanceAnalyzer.Analyze([.. ranked, .. truncated]);
        Assert.Equal(4, small.Leader.Samples);
        Assert.Equal((wins, 4), (small.Leader.AnyOfGroupWins.Successes, small.Leader.AnyOfGroupWins.Trials));
        Assert.False(small.Leader.EnoughSamples);

        string text = ReportWriter.Render(small);
        Assert.Contains("目标 ≤ 50%", text);
        Assert.Contains("样本 4 局", text);
        Assert.Contains("不足 200 局", text);

        List<MatchLog> big =
        [
            .. Enumerable.Range(0, 200).Select(i => SimFixtures.Clone(ranked[i % ranked.Count], seed: 1000UL + (ulong)i)),
            .. Enumerable.Range(0, 12).Select(i => SimFixtures.Clone(truncated[i % truncated.Count], seed: 5000UL + (ulong)i)),
        ];
        BalanceReport large = BalanceAnalyzer.Analyze(big);
        Assert.Equal(200, large.Leader.Samples);
        Assert.Equal(wins * 50, large.Leader.AnyOfGroupWins.Successes);
        Assert.True(large.Leader.EnoughSamples);
        Assert.True(large.Leader.AnyOfGroupWins.Upper - large.Leader.AnyOfGroupWins.Lower < small.Leader.AnyOfGroupWins.Upper - small.Leader.AnyOfGroupWins.Lower);
        Assert.Equal(large.Leader.AnyOfGroupWins.Value > 0.5 ? DeviationDirection.Above : DeviationDirection.Within, large.Targets.LeaderWinRate.Direction);
        Assert.DoesNotContain("不足 200 局", ReportWriter.Render(large));
    }

    [Fact]
    public void 部署曲线分阶段统计()
    {
        // 第 1–3 / 4–6 / 7+ 三个阶段分别给出部署上限分布：合成三条小回合，各落一个阶段。
        // 变异验证：本类以 M-B5 / M-B7 为准（分桶边界由第 3 / 第 5 / 第 8 大回合三条样本钉住）。
        MatchLog log = SimFixtures.Synthetic(
            1,
            [
                SimFixtures.Turn(1, 3, 0, [1, 1, 1, 1], ["A1:Basic"], deployLimit: 3),
                SimFixtures.Turn(2, 5, 0, [2, 1, 1, 1], ["A2:Basic"], deployLimit: 4),
                SimFixtures.Turn(3, 8, 0, [3, 1, 1, 1], ["A3:Basic"], deployLimit: 6),
                SimFixtures.Turn(4, 9, 1, [3, 2, 1, 1], ["H1:Basic"], deployLimit: 6),
            ],
            [],
            SimFixtures.ResultOf(9, [0]));

        TargetsSection t = BalanceAnalyzer.Analyze([log]).Targets;

        Assert.Equal(new Dictionary<int, int> { [3] = 1 }, t.DeployLimitRounds1To3);
        Assert.Equal(new Dictionary<int, int> { [4] = 1 }, t.DeployLimitRounds4To6);
        Assert.Equal(new Dictionary<int, int> { [6] = 2 }, t.DeployLimitRounds7Plus);
        Assert.Equal(DeviationDirection.Within, t.DeployPhase1.Direction);
        Assert.Equal(DeviationDirection.Within, t.DeployPhase2.Direction);
        Assert.Equal(DeviationDirection.Within, t.DeployPhase3.Direction);
        string text = ReportWriter.Render(BalanceAnalyzer.Analyze([log]));
        Assert.Contains("### 1. 部署上限分阶段分布（growth-pass-1：基础值按大回合 3 / 4 / 5，军令在其上叠加；目标中位数 3 / 3–5 / 5–8）", text);
        Assert.Contains("第 1–3 大回合：3×1", text);
        Assert.Contains("第 4–6 大回合：4×1", text);
        Assert.Contains("第 7 大回合以后：6×2", text);
    }

    [Fact]
    public void 偏离必须显式报告()
    {
        // 平均整局时长 48 分钟 → 明确标出超出 20–30 分钟目标区间、超出 18 分钟（+60%）。墙钟本身在无头跑局中不测（裁决 10），
        // 评估函数是同一个；报告层用结束大回合数（可测）验证渲染出的偏离文本。
        // 变异验证 M-B7：Assess 把 value > high 判为 Within → 红 1（本测试）。
        Deviation minutes = Statistics.Assess(48, 20, 30, " 分钟");
        Assert.Equal(DeviationDirection.Above, minutes.Direction);
        Assert.Equal(18, minutes.Amount);
        Assert.Equal(60, minutes.Percent);
        Assert.Contains("超出目标上限 30 分钟", minutes.ToString());
        Assert.Contains("高 18 分钟（+60%）", minutes.ToString());
        Assert.Equal(DeviationDirection.Below, Statistics.Assess(2, 4, 5).Direction);
        Assert.Equal(DeviationDirection.Within, Statistics.Assess(25, 20, 30).Direction);
        Assert.Equal(DeviationDirection.Unmeasurable, Statistics.Unmeasurable(20, 30, " 分钟").Direction);

        MatchLog[] logs = [.. Enumerable.Range(0, 3).Select(i => SimFixtures.Synthetic(
            (ulong)i + 1, [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"])], [], SimFixtures.ResultOf(15, [0])))];
        BalanceReport report = BalanceAnalyzer.Analyze(logs);
        Assert.Equal(DeviationDirection.Above, report.Targets.EndRound.Direction);
        string text = ReportWriter.Render(report);
        Assert.Contains("偏离：超出目标上限 10 大回合，实测 15 大回合，高 5 大回合（+50%）", text);
        Assert.Contains("整局时长 不可测（目标 20–30 分钟）", text);
        Assert.Contains("不可测（目标 0–3 分钟）", text);
    }
    [Fact]
    public void 时长与地图规模并列()
    {
        // 规格 Scenario：读取对局长度指标 → 报告同时给出该批次地图的可落子格数与信物格数（两者是对局时长的调节手段）。
        // 取自日志首部（PlayableCells、Relics 条数），不按地图标识重建地图。样本：370 格 / 16 信物 与 411 格 / 20 信物各一局，
        // 另一局是 denser-map 之前的旧日志（PlayableCells 为 null）：可落子格统计排除并计数，信物格照常计入。
        // 变异验证 M-E12：MapScale 的可落子格取 Relics.Count（两列取错来源） → 实跑红 1（本测试）。
        RelicEntry Relic(int i) => new() { Coord = $"A{i + 1}", Zone = "Contested", Budget = "Standard", Type = "Command", Magnitude = 1 };
        MatchLog small = SimFixtures.Synthetic(1, [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"])], [], SimFixtures.ResultOf(7, [0]),
            playableCells: 370, relics: [.. Enumerable.Range(0, 16).Select(Relic)]);
        MatchLog frontier = SimFixtures.Synthetic(2, [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"])], [], SimFixtures.ResultOf(9, [1]),
            playableCells: 411, relics: [.. Enumerable.Range(0, 20).Select(Relic)]);
        MatchLog legacy = SimFixtures.Synthetic(3, [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"])], [], SimFixtures.ResultOf(8, [2]),
            playableCells: null, relics: [.. Enumerable.Range(0, 18).Select(Relic)]);

        BalanceReport report = BalanceAnalyzer.Analyze([small, frontier, legacy]);
        MapScaleSection m = report.MapScale;

        Assert.Equal((2, 1, 370, 411, 390.5), (m.PlayableMatches, m.PlayableSkipped, m.MinPlayable, m.MaxPlayable, m.MeanPlayable));
        Assert.Equal((3, 16, 20, 18.0), (m.RelicMatches, m.MinRelics, m.MaxRelics, m.MeanRelics));

        string text = ReportWriter.Render(report);
        int length = text.IndexOf("### 5. 对局结束的大回合数与整局时长", StringComparison.Ordinal);
        Assert.True(length >= 0);
        string section = text[length..text.IndexOf("### 6.", length, StringComparison.Ordinal)];
        Assert.Contains("地图可落子格 370–411（平均 390.5；未记可落子格的旧日志 1 局）", section);
        Assert.Contains("信物格 16–20（平均 18）", section);
    }

    [Fact]
    public void 截断率如实报告()
    {
        // 规格 Scenario：200 局中有 12 局以 turn_limit 结束 → 报告给出截断 12 局、占比 6%，并标出偏离目标 0。
        // 反面：没有截断局时判为在目标内（0 局、0%）。
        // 变异验证 M-E13：截断计数改读 !Converged（把旧日志的 MajorRoundLimit 局也算成截断）→ 实跑红 1（本测试；样本里放了 3 局旧日志）。
        List<MatchLog> logs =
        [
            .. Enumerable.Range(0, 185).Select(i => SimFixtures.Synthetic((ulong)i + 1, [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"])], [], SimFixtures.ResultOf(8, [i % 4]))),
            .. Enumerable.Range(0, 3).Select(i => SimFixtures.Synthetic((ulong)i + 300, [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"])], [], SimFixtures.ResultOf(15, [0], converged: false))),
            .. Enumerable.Range(0, 12).Select(i => SimFixtures.Synthetic((ulong)i + 400, [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"])], [], SimFixtures.TruncatedResult(150))),
        ];

        EndingSection e = BalanceAnalyzer.Analyze(logs).Ending;

        Assert.Equal((200, 12, 188), (e.Matches, e.Truncated, e.Ranked));
        Assert.Equal(0.06, e.TruncatedRate.Value, 10);
        Assert.Equal(DeviationDirection.Above, e.TruncationTarget.Direction);
        Assert.Equal(6, e.TruncationTarget.Amount, 10);
        string text = ReportWriter.Render(BalanceAnalyzer.Analyze(logs));
        Assert.Contains("截断（turn_limit）12 局（6.0%）", text);
        Assert.Contains("偏离：超出目标上限 0%，实测 6%，高 6%", text);

        EndingSection none = BalanceAnalyzer.Analyze([.. logs.Take(185)]).Ending;
        Assert.Equal((0, DeviationDirection.Within), (none.Truncated, none.TruncationTarget.Direction));
        Assert.Contains("截断（turn_limit）0 局（0.0%）", ReportWriter.Render(BalanceAnalyzer.Analyze([.. logs.Take(185)])));
    }

    [Fact]
    public void AI决策质量旁证警告()
    {
        // implement 8.7 / design.md Risks「AI 太弱使分析失去意义」：自杀手尝试率 ≥ 25% 或 Pass 率 ≥ 60% → 报告置顶给出"数值结论不可信"的显式警告；真实小样本不触发。
        // 变异验证 M-C6（check）：AiQuality 把 unreliable 恒置 false → 红 1（本测试）。
        List<MatchLog> sample = SimFixtures.Sample.Value;
        Assert.False(BalanceAnalyzer.Analyze(sample).AiQuality.Unreliable);
        Assert.DoesNotContain("不可信", ReportWriter.Render(BalanceAnalyzer.Analyze(sample)));

        // Pass 率：10 个小回合里 7 个 Pass（70% ≥ 60%）
        MatchLog passing = SimFixtures.Synthetic(
            1,
            Enumerable.Range(1, 10).Select(i => SimFixtures.Turn(i, 1, i % 4, [1, 1, 1, 1], i <= 3 ? new[] { "A1:Basic" } : null)),
            [],
            SimFixtures.ResultOf(3, [0]));
        AiQualitySection byPass = BalanceAnalyzer.Analyze([passing]).AiQuality;
        Assert.Equal((7, 10), (byPass.PassRate.Successes, byPass.PassRate.Trials));
        Assert.True(byPass.SuicideAttemptRate.Value < BalanceAnalyzer.SuicideUnreliableThreshold);
        Assert.True(byPass.Unreliable);
        Assert.Contains("数值结论不可信", byPass.Verdict);
        Assert.Contains("数值结论不可信", ReportWriter.Render(BalanceAnalyzer.Analyze([passing])));

        // 自杀手尝试率：4 次确认 + 2 次自杀手被拒 → 2/6 ≈ 33% ≥ 25%，Pass 率 0
        MatchLog suicidal = SimFixtures.Synthetic(
            2,
            Enumerable.Range(1, 4).Select(i => SimFixtures.Turn(i, 1, i % 4, [1, 1, 1, 1], ["A1:Basic"])),
            Enumerable.Range(1, 2).Select(i => new LogEvent { Seq = i, Turn = i, MajorRound = 1, Type = LogEventType.Rejected, Player = 0, FailureKind = nameof(Batch.BatchFailureKind.Suicide), Coords = ["A1"] }),
            SimFixtures.ResultOf(1, [0]));
        AiQualitySection bySuicide = BalanceAnalyzer.Analyze([suicidal]).AiQuality;
        Assert.Equal((2, 6), (bySuicide.SuicideAttemptRate.Successes, bySuicide.SuicideAttemptRate.Trials));
        Assert.Equal((0, 4), (bySuicide.PassRate.Successes, bySuicide.PassRate.Trials));
        Assert.True(bySuicide.Unreliable);
        Assert.Contains("警告", ReportWriter.Render(BalanceAnalyzer.Analyze([suicidal])));
    }
}
