using System.Diagnostics;
using Siege.Core.Ai;
using Siege.Core.Tests.LifeShape;
using Xunit.Abstractions;
using static Siege.Core.Tests.LookaheadFixtures;

namespace Siege.Core.Tests.AiDecision;

/// <summary>
/// 规格：ai-decision（expert-lookahead）—— Requirement: 专家前瞻的确定性与耗时 / Scenario: 耗时上限（计时口径，design D12）。
/// Release、同一进程、同一局面集（link:1 种子 1–3、内置固定配对脚本、截断第 12 大回合、4 名标准 AI 对局的部署局面；retire-legacy-maps 1.4：原 v5 种子 1–3，每 3 个取 1，同 <see cref="LookaheadFixtures.ProbePositions"/>）；
/// 每个局面按 ABBA（高难、专家、专家、高难）交替测单次 <c>Deploy</c> 耗时，丢掉前 3 个局面作预热；比较两组中位数，专家 ≤ 4 × 高难；输出中位数、p90 与比值。
/// 专家一方取 <see cref="LookaheadFixtures.ExpandedExpert"/>（显式 S = 8、λ = 1000‰）：专家预设退回一层（负责人裁决 2026-09-28，段 B 后）之后，
/// 本测试量的是最耗时的可配置项；预设（一层配置）的耗时低于它。
/// ai-seat-vision V9：第一条写死全知（局面集与观察视图都是全知，引入之前的口径）；第二条换成新缺省按座位视野（局面集来自按座位视野的对局，
/// 高难与专家都经 <see cref="HeuristicAi.VisionFor"/> 读投影），其余口径相同。
/// </summary>
/// <remarks>不进默认套件：设 <c>SIEGE_PERF=1</c> 后按 <c>--filter "Category=Perf&amp;FullyQualifiedName~专家"</c> 单独跑（testing.md「慢测试与计时测试」）。超过 4 倍停下报告，不私自降 W。</remarks>
[Collection(nameof(专家前瞻耗时计时Tests))]
public class 专家前瞻耗时计时Tests(ITestOutputHelper output)
{
    private const int Warmup = 3;
    private const double MaxRatio = 4.0;

    [PerfTheory]
    [Trait("Category", "Perf")]
    [InlineData("link:1")]
    public void 专家单次部署耗时中位数不超过高难的四倍(string mapId)
    {
        // retire-legacy-maps 1.4：局面集改到 link:1（LookaheadFixtures.ProbePositions），断言不变。
        // ai-seat-vision V9：本条局面集写死全知（LookaheadFixtures.LinkConfig），Shadow 直接读全知视图——量的是全知口径；新缺省（按座位视野）见下一条。
        Assert.Equal(LookaheadFixtures.MapId, mapId);
        Measure(seatVision: false);
    }

    [PerfTheory]
    [Trait("Category", "Perf")]
    [InlineData("link:1")]
    public void 按座位视野下专家单次部署耗时中位数不超过高难的四倍(string mapId)
    {
        // ai-seat-vision V9（check 补）：同上一条口径（同一局面抽样、ABBA、预热、≤ 4 倍），只换成新缺省——局面来自按座位视野的 4 名标准 AI 对局，
        // 高难与专家两边都经 HeuristicAi.VisionFor 取按座位投影的观察视图与预演口径（LookaheadFixtures.ShadowWithVision），比值不混入投影开销的差异。
        Assert.Equal(LookaheadFixtures.MapId, mapId);
        Assert.True(AiSearchConfig.Hard.SeatVision && ExpandedExpert.SeatVision, "样本口径：两边的配置都是按座位视野（预设缺省）");
        Measure(seatVision: true);
    }

    private void Measure(bool seatVision)
    {
        var hard = new List<long>();
        var expert = new List<long>();
        int index = 0;
        int positions = ProbePositions(seatVision, every: 3, (match, batch) =>
        {
            long Time(AiDifficulty difficulty, AiSearchConfig config)
            {
                var sw = Stopwatch.StartNew();
                if (seatVision)
                {
                    ShadowWithVision(match, batch.Context, difficulty, config);
                }
                else
                {
                    Shadow(match, batch.Context, difficulty, config);
                }

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
        output.WriteLine($"视野：{(seatVision ? "按座位视野" : "全知")}；局面 {positions} 个（预热丢弃 {Warmup} 个），每组样本 {hard.Count}");
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
