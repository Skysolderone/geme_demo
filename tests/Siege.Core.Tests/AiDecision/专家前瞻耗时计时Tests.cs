using System.Diagnostics;
using Siege.Core.Ai;
using Siege.Core.Tests.LifeShape;
using Xunit.Abstractions;
using static Siege.Core.Tests.LookaheadFixtures;

namespace Siege.Core.Tests.AiDecision;

/// <summary>
/// 规格：ai-decision（expert-lookahead）—— Requirement: 专家前瞻的确定性与耗时 / Scenario: 耗时比值如实输出（计时口径，design D12；retire-legacy-maps 段 D 由「耗时上限」改名，方法原名「专家单次部署耗时中位数不超过高难的四倍」）。
/// Release、同一进程、同一局面集（4 人棋盘图种子 1–3、4 名标准 AI 对局的部署局面，每 3 个取 1，同 <see cref="LookaheadFixtures.ProbePositions"/>；retire-legacy-maps 段 A2 之前为 v5）；
/// 每个局面按 ABBA（高难、专家、专家、高难）交替测单次 <c>Deploy</c> 耗时，丢掉前 3 个局面作预热；输出两组中位数、p90 与比值。
/// retire-legacy-maps 段 A2（主会话裁决 2）：「专家 ≤ 4 × 高难」与「局面不少于 30 个」两条断言删除——那是 v5 上的性能结论，棋盘图上的上限由 AI 校准 change 重定。
/// 专家一方取 <see cref="LookaheadFixtures.ExpandedExpert"/>（显式 S = 8、λ = 1000‰）：专家预设退回一层（负责人裁决 2026-09-28，段 B 后）之后，
/// 本测试量的是最耗时的可配置项；预设（一层配置）的耗时低于它。
/// </summary>
/// <remarks>不进默认套件：设 <c>SIEGE_PERF=1</c> 后按 <c>--filter "Category=Perf&amp;FullyQualifiedName~专家前瞻耗时计时"</c> 单独跑（testing.md「慢测试与计时测试」）。只输出比值、不断言上限；棋盘图上的上限由 AI 校准 change 重定。</remarks>
[Collection(nameof(专家前瞻耗时计时Tests))]
public class 专家前瞻耗时计时Tests(ITestOutputHelper output)
{
    private const int Warmup = 3;
    /// <summary>已删除的 v5 上曾断言的上限（专家 ≤ 4 × 高难），只用于输出对照，不参与断言。</summary>
    private const double RetiredV5RatioCap = 4.0;

    [PerfTheory]
    [Trait("Category", "Perf")]
    [InlineData(SimFixtures.Board4)]
    public void 耗时比值如实输出(string mapId)
    {
        Assert.Equal(SimFixtures.Board4, mapId);
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
        output.WriteLine($"中位数比值 专家 / 高难 = {ratio:F3}（已删除的 v5 上曾以 {RetiredV5RatioCap} 倍为上限，棋盘图上不断言）");
        Assert.True(hard.Count > 0 && expert.Count > 0, $"局面只有 {positions} 个，预热后没有样本");
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
