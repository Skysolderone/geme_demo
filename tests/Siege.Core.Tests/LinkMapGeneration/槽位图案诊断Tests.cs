using Siege.Core.Board.Maps;
using Siege.Core.Tests.CaptureResolution;
using Xunit.Abstractions;

namespace Siege.Core.Tests.LinkMapGeneration;

/// <summary>
/// 诊断（慢，约 3 分钟）：对全部 536 个逐对可接的图案用统一边长 7、零偏移、种子 1 跑完整链接核对，打印通过情况——
/// LinkMapGenerator.SlotPatterns 白名单就是这里 PASS 的 32 个。改了链接几何或序列清单后重跑并同步白名单。
/// </summary>
public class 槽位图案诊断Tests(ITestOutputHelper output)
{
    [SlowFact]
    [Trait("Category", "Slow")]
    public void 列出通过全部序列的图案()
    {
        int passed = 0;
        int limit = int.TryParse(Environment.GetEnvironmentVariable("SIEGE_PATTERN_LIMIT"), out int l) ? l : LinkMapGenerator.AllSlotFeasiblePatterns.Length;
        for (int i = 0; i < limit; i++)
        {
            var pattern = LinkMapGenerator.AllSlotFeasiblePatterns[i];
            (bool ok, string failure, LinkVerification? v) = LinkMapGenerator.VerifyPattern(pattern, 7, 1);
            if (ok)
            {
                passed++;
                output.WriteLine($"PASS {i} {string.Join(" ", pattern)} 退化 {v!.Degraded}/{v.Links}");
            }
            else
            {
                output.WriteLine($"FAIL {i} {string.Join(" ", pattern)} {failure[..Math.Min(600, failure.Length)]}");
            }
        }

        output.WriteLine($"通过 {passed} / {LinkMapGenerator.AllSlotFeasiblePatterns.Length}");
        Assert.True(passed >= 1);
    }
}
