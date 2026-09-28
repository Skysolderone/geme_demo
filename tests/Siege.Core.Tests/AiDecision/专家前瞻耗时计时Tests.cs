using System.Diagnostics;
using Siege.Core.Ai;
using Siege.Core.Tests.LifeShape;
using Xunit.Abstractions;
using static Siege.Core.Tests.LookaheadFixtures;

namespace Siege.Core.Tests.AiDecision;

/// <summary>
/// 规格：ai-decision（expert-lookahead）—— Requirement: 专家前瞻的确定性与耗时 / Scenario: 耗时上限（计时口径，design D12）。
/// Release、同一进程、同一局面集（v5 种子 1–3、4 名标准 AI 对局的部署局面，每 3 个取 1，同 <see cref="LookaheadFixtures.ProbePositions"/>）；
/// 每个局面按 ABBA（高难、专家、专家、高难）交替测单次 <c>Deploy</c> 耗时，丢掉前 3 个局面作预热；比较两组中位数，专家 ≤ 4 × 高难；输出中位数、p90 与比值。
/// 专家一方取 <see cref="LookaheadFixtures.ExpandedExpert"/>（显式 S = 8、λ = 1000‰）：专家预设退回一层（负责人裁决 2026-09-28，段 B 后）之后，
/// 本测试量的是最耗时的可配置项；预设（一层配置）的耗时低于它。
/// </summary>
/// <remarks>不进默认套件：设 <c>SIEGE_PERF=1</c> 后按 <c>--filter "Category=Perf&amp;FullyQualifiedName~专家"</c> 单独跑（testing.md「慢测试与计时测试」）。超过 4 倍停下报告，不私自降 W。</remarks>
[Collection(nameof(专家前瞻耗时计时Tests))]
public class 专家前瞻耗时计时Tests(ITestOutputHelper output)
{
    private const int Warmup = 3;
    private const double MaxRatio = 4.0;

    [PerfTheory]
    [Trait("Category", "Perf")]
    [InlineData("siege-4p-base-v5")]
    public void 专家单次部署耗时中位数不超过高难的四倍(string mapId)
    {
        Assert.Equal("siege-4p-base-v5", mapId);
        var hard = new List<long>();
        var expert = new List<long>();
        int index = 0;
        int positions = ProbePositions(every: 3, (match, batch) =>
        {
            long Time(AiDifficulty difficulty, AiSearchConfig config)
            {
                var sw = Stopwatch.StartNew();
                Shadow(match, batch.Context, difficulty, config);
                sw.Stop();
                return sw.ElapsedTicks;
            }

            long h1 = Time(AiDifficulty.Hard, AiSearchConfig.Hard);
            long e1 = Time(AiDifficulty.Expert, ExpandedExpert);
            long e2 = Time(AiDifficulty.Expert, ExpandedExpert);
            long h2 = Time(AiDifficulty.Hard, AiSearchConfig.Hard);
            if (index++ >= Warmup)
            {
                hard.AddRange([h1, h2]);
                expert.AddRange([e1, e2]);
            }
        });

        double hardMedian = Percentile(hard, 0.5);
        double expertMedian = Percentile(expert, 0.5);
        double ratio = expertMedian / hardMedian;
        output.WriteLine($"局面 {positions} 个（预热丢弃 {Warmup} 个），每组样本 {hard.Count}");
        output.WriteLine($"高难：中位数 {hardMedian:F1} ms，p90 {Percentile(hard, 0.9):F1} ms");
        output.WriteLine($"专家：中位数 {expertMedian:F1} ms，p90 {Percentile(expert, 0.9):F1} ms");
        output.WriteLine($"中位数比值 专家 / 高难 = {ratio:F3}（上限 {MaxRatio}）");
        Assert.True(positions >= 30, $"局面只有 {positions} 个");
        Assert.True(ratio <= MaxRatio, $"专家耗时中位数是高难的 {ratio:F3} 倍，超过 {MaxRatio}");
    }

    private static double Percentile(List<long> ticks, double q)
    {
        long[] sorted = [.. ticks.Order()];
        int i = Math.Min(sorted.Length - 1, (int)Math.Ceiling(q * sorted.Length) - 1);
        return sorted[Math.Max(0, i)] * 1000.0 / Stopwatch.Frequency;
    }
}

/// <summary>计时测试不与其他测试类并行（避免 xunit 并行把噪声带进计时）。</summary>
[CollectionDefinition(nameof(专家前瞻耗时计时Tests), DisableParallelization = true)]
public sealed class 专家前瞻耗时计时Collection
{
}
