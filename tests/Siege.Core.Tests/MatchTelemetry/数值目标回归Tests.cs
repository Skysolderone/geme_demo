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
        // 变异验证 M-B5：LeadersAtRound3 取 Rank == 2 的玩家 → 红 1（本测试）。
        List<MatchLog> sample = SimFixtures.Sample.Value;
        BalanceReport small = BalanceAnalyzer.Analyze(sample);
        Assert.Equal(4, small.Leader.Samples);
        Assert.Equal(4, small.Leader.AnyOfGroupWins.Trials);
        Assert.False(small.Leader.EnoughSamples);
        foreach (MatchLog log in sample)
        {
            List<int> leaders = BalanceAnalyzer.LeadersAtRound3(log);
            Assert.NotEmpty(leaders);
            LogEvent third = log.Events.Single(e => e.Type == LogEventType.MajorRoundEnded && e.MajorRound == 3);
            Assert.All(leaders, p => Assert.Equal(1, third.Values![$"P{p}.Rank"]));
        }

        string text = ReportWriter.Render(small);
        Assert.Contains("目标 ≤ 50%", text);
        Assert.Contains("不足 200 局", text);

        List<MatchLog> big = [.. Enumerable.Range(0, 200).Select(i => SimFixtures.Clone(sample[i % sample.Count], seed: 1000UL + (ulong)i))];
        BalanceReport large = BalanceAnalyzer.Analyze(big);
        Assert.Equal(200, large.Leader.Samples);
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
