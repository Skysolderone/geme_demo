using Siege.Core.Ai;
using Siege.Sim.Analysis;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.MatchTelemetry;

/// <summary>规格：match-telemetry —— Requirement: 分析排除测试污染</summary>
public class 分析排除测试污染Tests
{
    [Fact]
    public void 默认排除调试局()
    {
        // 混有 10 局调试 AI 局 → 默认排除并说明数量；显式要求包含时纳入。人工接管局同样排除。分析只读文件，不碰内存对象。
        // 变异验证 M-B12：MatchLog.IsContaminated 忽略 UsedDebugAi → 红 1（本测试）。
        // restore-go-core-rules 段 E：样本由 Sample（段 C 起 4 局全是截断局，领先者样本按新口径为 0）换成有名次的 RankedSample——
        // 否则下面的 Leader.Samples == 4 读的是截断局，"污染局被排除"就只剩纳入计数一条腿在证。
        List<MatchLog> sample = SimFixtures.RankedSample.Value;
        string dir = SimFixtures.TempDir("exclude-debug");
        foreach (MatchLog log in sample)
        {
            log.WriteTo(dir);
        }

        for (int i = 0; i < 10; i++)
        {
            SimFixtures.Clone(sample[i % sample.Count], seed: 500UL + (ulong)i, result: r => r with { UsedDebugAi = true, DebugAiPlayers = [2] }).WriteTo(dir);
        }

        SimFixtures.Clone(sample[0], seed: 600, result: r => r with
        {
            Takeovers = [new TakeoverEntry { Sequence = 1, Player = 1, MajorRound = 2, Stage = "Recruit", Kind = "TakenOver" }],
        }).WriteTo(dir);

        List<MatchLog> fromDisk = MatchLog.ReadDirectory(dir);
        Assert.Equal(15, fromDisk.Count);
        BalanceReport report = BalanceAnalyzer.Analyze(fromDisk);
        Assert.Equal((15, 4, 11, 0), (report.TotalLogs, report.Included, report.ExcludedContaminated, report.ExcludedFailed));
        Assert.Equal(4, report.Leader.Samples);
        Assert.Contains("排除调试 AI / 人工接管局 11（默认排除）", ReportWriter.Render(report));

        BalanceReport inclusive = BalanceAnalyzer.Analyze(fromDisk, new AnalysisOptions { IncludeContaminated = true });
        Assert.Equal((15, 0), (inclusive.Included, inclusive.ExcludedContaminated));
        Assert.Contains("已显式要求包含", ReportWriter.Render(inclusive));

        // 真实路径：配置里的调试 AI 会经 MatchRunner.Annotations 落到日志的标注
        RunConfig config = SimFixtures.Config(turnLimit: 4) with
        {
            Players = [new PlayerAiConfig { DebugAi = true, Difficulty = AiDifficulty.Easy }, new PlayerAiConfig { Difficulty = AiDifficulty.Easy }, new PlayerAiConfig { Difficulty = AiDifficulty.Easy }, new PlayerAiConfig { Difficulty = AiDifficulty.Easy }],
        };
        MatchLog debugLog = MatchSession.Create(config, 77).Run();
        Assert.True(debugLog.Result!.UsedDebugAi);
        Assert.Equal([0], debugLog.Result.DebugAiPlayers);
        Assert.Equal([0], debugLog.Header.DebugAiPlayers);
        Assert.True(debugLog.IsContaminated);
        Assert.Equal(0, BalanceAnalyzer.Analyze([debugLog]).Included);
    }
}
