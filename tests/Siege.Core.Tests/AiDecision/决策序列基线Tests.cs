using Siege.Core.Board.Maps;
using System.Text;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Sim.Config;
using Siege.Sim.Running;

namespace Siege.Core.Tests.AiDecision;

/// <summary>
/// 规格：ai-decision（ai-turn-speed）—— Requirement: 决策序列基线与耗时 / Scenario: 决策序列与基线相同。
/// 固定种子、4 名标准难度 AI 的真实对局（跑局层建局：候选格上限与停手阈值按地图落成缺省值），逐小回合导出 AI 的决策序列，
/// 与改动前钉入的基线（<c>AiDecision/Fixtures/decisions-*.txt</c>）逐字节比对——任何性能优化 MUST NOT 改变任何一步。
/// retire-legacy-maps 2.x：v5 / board:1 两行基线与旧导出器 DecisionSequence 随旧地图删除，只留链接图种子 1 / 2（种子 3 见 <see cref="决策序列基线_链接模式Tests"/>）。
/// </summary>
/// <remarks>
/// 基线生成于 ai-turn-speed 的任何 Core 改动之前（`git diff --stat -- src` 为空时）。规则或 AI 行为有意变更后重建：
/// 设 <c>SIEGE_REBUILD_DECISION_BASELINE=1</c> 再跑本测试，会把当前序列写回源码目录的夹具（仍做比对，重建当次即绿），并在提交记录里写明变更原因。
/// <para>open-map 段 B（裁决 B4）：原 <c>*-omniscient.txt</c>（ai-seat-vision 改前副本）与 <c>*-k0.txt</c>（final-countdown 改前副本）十份随全知开关与 K 一并删除，只留 5 份缺省基线。</para>
/// </remarks>
public class 决策序列基线Tests
{
    /// <summary>
    /// retire-legacy-maps 1.3（裁决 L2 / 设计 D6）：新回归护栏——<c>link:1</c>、对局种子 1 / 2、脚本固定配对、4 名标准难度、截断到第 12 大回合，
    /// 导出口径与 <see cref="决策序列基线_链接模式Tests"/>（种子 3，沿用既有文件、不重钉）完全相同（同一个 <see cref="LinkDecisionSequence.Export"/>）。
    /// 两份基线在删除任何旧地图代码之前、按当时实现钉入；此后每一组删除结束时三条链接图基线必须逐字节不变。
    /// </summary>
    [Theory]
    [InlineData(1UL, "decisions-link1-seed1.txt")]
    [InlineData(2UL, "decisions-link1-seed2.txt")]
    public void 链接图决策序列与基线相同(ulong seed, string fixture)
    {
        string actual = LinkDecisionSequence.Export(seed, 12);
        if (Environment.GetEnvironmentVariable("SIEGE_REBUILD_DECISION_BASELINE") == "1")
        {
            File.WriteAllText(Path.Combine(SourceFixtureDirectory(), fixture), actual, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        string path = Path.Combine(AppContext.BaseDirectory, "AiDecision", "Fixtures", fixture);
        Assert.True(File.Exists(path), $"找不到决策序列基线：{path}");
        string expected = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.True(expected.Length < 200_000, $"基线 {fixture} 超过 200 KB（{expected.Length} 字符）");

        // 样本口径下界（open-map 重钉）：基线里没有任何配对 / 链接事件、至少 30 个小回合，且要么跑满第 12 大回合、要么以规则终局收尾。
        string[] lines = expected.Split('\n');
        Assert.DoesNotContain(lines, l => l.StartsWith("P\t", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.StartsWith("L\t", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("4\t", StringComparison.Ordinal));   // 跑过了第 3 大回合，没有配对 / 链接
        Assert.True(lines.Count(l => l.Length > 0 && char.IsDigit(l[0])) >= 30, "基线小回合不足 30 个");
        Assert.Contains(lines, l => l.StartsWith("# end phase=Ended", StringComparison.Ordinal) || l.Contains("majorRound=13", StringComparison.Ordinal));
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// retire-legacy-maps 4b.4（裁决 L21）：在 <c>link:1</c>、对局种子 1 上为简单与困难难度各补钉一条基线，口径与上面相同（脚本缺省配对、截断到第 12 大回合），
    /// 只新钉、不改任何行为——三条标准难度基线只守标准难度的走法，这两条补上另外两档（专家由 G1 黄金值守）。
    /// </summary>
    [Theory]
    [InlineData(AiDifficulty.Easy, "decisions-link1-easy-seed1.txt")]
    [InlineData(AiDifficulty.Hard, "decisions-link1-hard-seed1.txt")]
    public void 链接图其他难度决策序列与基线相同(AiDifficulty difficulty, string fixture)
    {
        string actual = LinkDecisionSequence.Export(1, 12, difficulty);
        if (Environment.GetEnvironmentVariable("SIEGE_REBUILD_DECISION_BASELINE") == "1")
        {
            File.WriteAllText(Path.Combine(SourceFixtureDirectory(), fixture), actual, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        string path = Path.Combine(AppContext.BaseDirectory, "AiDecision", "Fixtures", fixture);
        Assert.True(File.Exists(path), $"找不到决策序列基线：{path}");
        string expected = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.True(expected.Length < 200_000, $"基线 {fixture} 超过 200 KB（{expected.Length} 字符）");

        // 样本口径下界（open-map 重钉）：首部写明难度；没有配对与链接；至少 30 个小回合；跑满第 12 大回合或以规则终局收尾。
        string[] lines = expected.Split('\n');
        Assert.Contains($"difficulty={difficulty} ", expected, StringComparison.Ordinal);
        Assert.DoesNotContain(lines, l => l.StartsWith("P\t", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.StartsWith("L\t", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("4\t", StringComparison.Ordinal));
        Assert.True(lines.Count(l => l.Length > 0 && char.IsDigit(l[0])) >= 30, "基线小回合不足 30 个");
        Assert.Contains(lines, l => l.StartsWith("# end phase=Ended", StringComparison.Ordinal) || l.Contains("majorRound=13", StringComparison.Ordinal));
        Assert.Equal(expected, actual);
    }

    private static string SourceFixtureDirectory([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "") =>
        Path.Combine(Path.GetDirectoryName(thisFile)!, "Fixtures");
}
