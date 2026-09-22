using System.Diagnostics;
using Siege.Core.Board;
using Siege.Sim.Config;
using Siege.Sim.Running;
using Xunit.Abstractions;

namespace Siege.Core.Tests.LifeShape;

/// <summary>
/// tasks 1.5 性能基线（不对应 Scenario）：在 v5 与边疆图的中盘局面上，一次全盘活形分析的耗时中位数
/// 不超过一次全盘棋串计算（<see cref="GameBoard.AllGroups"/>）的 3 倍（宽松上界）。
/// 中盘 = Easy 4 人真实对局（种子 7）跑满 40 个小回合（10 个大回合）。两者交错计时、取中位数之比。
/// 另输出"AllGroups + 全部 LibertiesOf"（含气的全盘棋串计算）为分母的比值，供待决项裁决。
/// </summary>
/// <remarks>
/// 计时测试不进默认套件（xunit 并行跑时计时噪声大，且要跑两局真实对局约 35 s）：
/// 设环境变量 <c>SIEGE_PERF=1</c> 才运行，按 <c>--filter "Category=Perf"</c> 单独跑。
/// 段 A 实测 v5 ≈ 3.0、边疆 ≈ 3.5–3.7，超出 3 倍上界——见 implement.md 段 A 待决 #1，阈值未改。
/// </remarks>
public class 活形分析性能基线Tests
{
    private const int MidGameTurns = 40;
    private const int Samples = 31;
    private const double MaxRatio = 3.0;

    private readonly ITestOutputHelper _output;

    public 活形分析性能基线Tests(ITestOutputHelper output) => _output = output;

    [PerfTheory]
    [Trait("Category", "Perf")]
    [InlineData("siege-4p-base-v5")]
    [InlineData("siege-frontier-v2")]
    public void 中盘全盘活形分析不超过棋串计算的三倍(string mapId)
    {
        RunConfig config = SimFixtures.Config(turnLimit: 0) with { MapId = mapId };
        MatchSession session = MatchSession.Create(config, 7);
        int turns = 0;
        while (turns < MidGameTurns && session.RunTurn())
        {
            turns++;
        }

        GameBoard board = session.Match.Board;
        int stones = board.AllGroups().Sum(g => g.Size);
        Assert.Equal(MidGameTurns, turns);
        Assert.True(stones > 0, "中盘样本盘面上没有棋子。");

        for (int i = 0; i < 5; i++)
        {
            _ = board.AllGroups();
            _ = LifeShapeReport.Analyze(board);
        }

        var groupTicks = new long[Samples];
        var withLibertiesTicks = new long[Samples];
        var lifeTicks = new long[Samples];
        LifeShapeReport? report = null;
        for (int i = 0; i < Samples; i++)
        {
            long t0 = Stopwatch.GetTimestamp();
            _ = board.AllGroups();
            long t1 = Stopwatch.GetTimestamp();
            foreach (Group g in board.AllGroups())
            {
                _ = board.LibertiesOf(g);
            }

            long t2 = Stopwatch.GetTimestamp();
            report = LifeShapeReport.Analyze(board);
            long t3 = Stopwatch.GetTimestamp();
            groupTicks[i] = t1 - t0;
            withLibertiesTicks[i] = t2 - t1;
            lifeTicks[i] = t3 - t2;
        }

        double groupsUs = MedianMicroseconds(groupTicks);
        double withLibertiesUs = MedianMicroseconds(withLibertiesTicks);
        double lifeUs = MedianMicroseconds(lifeTicks);
        double ratio = lifeUs / groupsUs;
        _output.WriteLine(
            $"{mapId}: 小回合 {turns}，棋子 {stones}，棋串 {report!.Groups.Length}，眼空间 {report.EyeSpaces.Length}，" +
            $"活形棋串 {report.Groups.Count(g => g.Life == LifeState.Alive)}；AllGroups 中位 {groupsUs:F1} µs，" +
            $"AllGroups+LibertiesOf 中位 {withLibertiesUs:F1} µs，Analyze 中位 {lifeUs:F1} µs；" +
            $"比 {ratio:F2}（对含气分母 {lifeUs / withLibertiesUs:F2}）");
        Assert.True(ratio <= MaxRatio, $"{mapId}: Analyze 中位 {lifeUs:F1} µs 是 AllGroups 中位 {groupsUs:F1} µs 的 {ratio:F2} 倍，超过 {MaxRatio} 倍。");
    }

    private static double MedianMicroseconds(long[] ticks)
    {
        long[] sorted = [.. ticks.Order()];
        return sorted[sorted.Length / 2] * 1_000_000.0 / Stopwatch.Frequency;
    }
}

/// <summary>只在环境变量 <c>SIEGE_PERF=1</c> 时运行的计时 Theory；默认套件里显示为 Skipped，而不是静默通过。</summary>
public sealed class PerfTheoryAttribute : TheoryAttribute
{
    public PerfTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("SIEGE_PERF") != "1")
        {
            Skip = "计时测试：设 SIEGE_PERF=1 后按 --filter \"Category=Perf\" 单独运行。";
        }
    }
}
