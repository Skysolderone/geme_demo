using Siege.Core.Match;
using Siege.Sim.Analysis;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.MatchTelemetry;

/// <summary>implement 3.2（round-cap D5）：报告"收敛情况"按规则原因 <c>MajorRoundLimit</c> 统计不收敛率；跑局汇总同口径。</summary>
public class 收敛口径Tests
{
    [Fact]
    public void 按规则原因统计不收敛率()
    {
        // 4 局中 3 局以 MajorRoundLimit 终局 → 不收敛率 75%；LogResult.Converged 由 Reason 派生，不再是独立字段。
        // 变异验证 M-R13：Convergence 改回按旧字符串 "MaxMajorRoundsReached" 计数 → 红 1（本测试：Capped 为 0）。报告口径未切换即红。
        MatchLog[] logs = [.. Enumerable.Range(0, 4).Select(i => SimFixtures.Synthetic(
            (ulong)i + 500,
            [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"])],
            [],
            SimFixtures.ResultOf(i == 0 ? 6 : 15, [i], converged: i == 0)))];
        Assert.Equal(3, logs.Count(l => l.Result!.Reason == nameof(EndReason.MajorRoundLimit)));
        Assert.Equal([true, false, false, false], logs.Select(l => l.Result!.Converged));

        ConvergenceSection c = BalanceAnalyzer.Analyze(logs).Convergence;
        Assert.Equal((1, 3), (c.Converged, c.Capped));
        Assert.Equal(0.75, c.CappedRate.Value);
        Assert.Equal(3, c.Reasons[nameof(EndReason.MajorRoundLimit)]);
        Assert.Equal(1, c.Reasons[nameof(EndReason.AllPassed)]);
        Assert.Equal(6, c.MeanMajorRoundsConverged);

        string text = ReportWriter.Render(BalanceAnalyzer.Analyze(logs));
        Assert.Contains("## 收敛情况", text);
        Assert.Contains("达大回合上限终局（MajorRoundLimit）3 局，不收敛率 75.0% (3/4", text);
        Assert.DoesNotContain("MaxMajorRoundsReached", text);

        BatchSummary summary = BatchRunner.Summarize(logs, parallelism: 1, wallClockMs: 0);
        Assert.Equal(3, summary.Capped);
        Assert.Equal(3, summary.Reasons[nameof(EndReason.MajorRoundLimit)]);

        // 文本往返：旧日志里若带 "Converged" 字段也被忽略，仍由 Reason 派生
        MatchLog parsed = MatchLog.Parse(logs[1].FullText().Replace("\"Reason\":\"MajorRoundLimit\"", "\"Reason\":\"MajorRoundLimit\",\"Converged\":true"));
        Assert.False(parsed.Result!.Converged);
    }

    [Fact]
    public void 不收敛率分母只含纳入局()
    {
        // D5 口径：不收敛率 = 纳入分析的局中以 MajorRoundLimit 终局的占比；被排除的污染局（调试 AI）不进分子也不进分母。
        // 变异验证 M-RC4（check）：Analyze 把 Convergence(included) 改为全部未失败局 → 红 1（本测试：3/4 变 5/6）。
        MatchLog[] clean = [.. Enumerable.Range(0, 4).Select(i => SimFixtures.Synthetic(
            (ulong)i + 600,
            [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"])],
            [],
            SimFixtures.ResultOf(i == 0 ? 6 : 15, [i], converged: i == 0)))];
        MatchLog[] contaminated = [.. Enumerable.Range(0, 2).Select(i => SimFixtures.Synthetic(
            (ulong)i + 700,
            [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"])],
            [],
            SimFixtures.ResultOf(15, [i], converged: false) with { UsedDebugAi = true }))];
        Assert.All(contaminated, l => Assert.True(l.IsContaminated));

        BalanceReport report = BalanceAnalyzer.Analyze([.. clean, .. contaminated]);
        Assert.Equal((6, 4, 2), (report.TotalLogs, report.Included, report.ExcludedContaminated));
        ConvergenceSection c = report.Convergence;
        Assert.Equal((1, 3), (c.Converged, c.Capped));
        Assert.Equal(0.75, c.CappedRate.Value);

        ConvergenceSection inclusive = BalanceAnalyzer.Analyze([.. clean, .. contaminated], new AnalysisOptions { IncludeContaminated = true }).Convergence;
        Assert.Equal((1, 5), (inclusive.Converged, inclusive.Capped));
    }
}
