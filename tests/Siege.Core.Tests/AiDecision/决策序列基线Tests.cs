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
/// <para>link-map-surfaces 2.2（裁决 E7）重钉 5 份：link:1 中间带投放 9 格新地表，首部地图摘要变、走法随之变；首个分歧小回合与落点（逐份见任务目录 rebaseline.txt）：
/// 种子 1 第 4 小回合 P1（原落 H9 / G10 挨着新浅滩 G9）、种子 2 第 7 小回合 P0（同上）、种子 3 第 3 小回合 P0（原落 J8 挨着新浅滩 K8）、
/// 简单种子 1 第 2 小回合 P3（新落点 V8 即新岩台）、困难种子 1 第 4 小回合 P1（原落 H9 挨着 G9）——分歧都发生在新地表格或其四邻。五份仍截到第 12 大回合、48 个小回合。</para>
/// <para>link-map-surfaces 1.3（实施后裁决 F1 / F2：小棋盘免除核心区四邻限制、混合主题补位）再重钉 5 份：link:1 新地表 9 → 10 格。相对第一次重钉的首个分歧：
/// 种子 1 / 困难种子 1 第 5 小回合 P2（新落点 K14 即新荒漠）、种子 2 第 5 小回合 P2（原落 K13 挨着新荒漠 K14）、种子 3 第 5 小回合 P0（新落点 P4 即新岩台）、
/// 简单种子 1 第 14 小回合 P0（原落 V4、新落 P4，都是新岩台）——仍都在新地表格或其四邻；相对改前基线的首个分歧与第一次相同（种子 2 除外：第 5 小回合 P2）。逐份见任务目录 rebaseline.txt。</para>
/// <para>ai-whole-batch 1.6（整批候选缺省开）重钉 2 份：困难种子 1 首个分歧在第 37 小回合（第 10 大回合）P2 选中配对候选；种子 3 见 <see cref="决策序列基线_链接模式Tests"/>。
/// 种子 1 / 2 与简单种子 1 逐字节不变。改前 5 份另存为 <c>*-wb0.txt</c>，由 <see cref="整批候选关闭时与改前基线相同"/> 以开关 0 逐字节比对（见任务 10-22-ai-whole-batch 的 rebaseline.txt）。</para>
/// <para>relic-redesign 接线段 W1（旧信物删除 + 地图信物刷新接入对局，AI 评价版本 11 → 12）重录全部 15 份（5 份缺省 + 5 份 <c>*-wb0</c> + 5 份 <c>*-es0</c>，开关副本经 <c>RebuildIfRequested</c>）：
/// 首部只有地图摘要一行变（地图文件不再写 RelicCells 字段，link:1:4 摘要 58EF4E07… → AF22F0AB…）；决策行第 1–3 大回合逐字节相同，首个分歧都在第一批地图信物落位之后——
/// 种子 1 / 2 第 4 大回合 P2、种子 3 与困难种子 1 第 4 大回合 P1、简单种子 1 第 5 大回合 P2（第 4 大回合开始时第一批信物落位，盘面与 AI 评价随之变）；三组开关副本的首个分歧与各自缺省份相同。</para>
/// <para>relic-redesign 段 C1（信物维改为势力增量口径 <c>RelicEstimate</c> 三桶 + AI 挂出改为势力增量最大落点，AI 评价版本 12 → 13）重录 12 份：三份简单难度（缺省 / wb0 / es0）<b>逐字节不变</b>
/// （简单难度不算信物维、基线不带入，是本次改动不泄漏到简单难度的反面对照）；其余 12 份首部只有版本号变、第 1–3 大回合逐字节相同，首个分歧都在第一批信物落位之后——
/// 种子 1（三份）第 4 大回合 P2（原 X5 / AD11 / W13 旗手 → X5 / AD11 / W23 普通）、种子 2（三份）第 4 大回合 P2（原 X5 / AA6 倍增 / AD11 / AF14 哨兵 → AA6 / AD11 / U21 倍增）、
/// 困难种子 1（三份）第 4 大回合 P1（原 O4 旗手 / X5 / AD11 / Q27 旗手 → N4 旗手 / O4 旗手 / X5 / AD11）、种子 3 缺省第 6 大回合 P1（原 O6 连珠 + K14 → 只落 O6）、种子 3 wb0 / es0 第 6 大回合 P1（原 O6 → AP32）。</para>
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
        string actual = LinkDecisionSequence.Export(seed, LinkDecisionSequence.BaselineRounds);
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
        // map-expand-25 段 A2：截断改为 LinkDecisionSequence.BaselineRounds（6）个大回合，小回合下界 30 → 20、收尾大回合 13 → BaselineRounds + 1。
        Assert.True(lines.Count(l => l.Length > 0 && char.IsDigit(l[0])) >= 20, "基线小回合不足 20 个");
        Assert.Contains(lines, l => l.StartsWith("# end phase=Ended", StringComparison.Ordinal) || l.Contains($"majorRound={LinkDecisionSequence.BaselineRounds + 1}", StringComparison.Ordinal));
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
        string actual = LinkDecisionSequence.Export(1, LinkDecisionSequence.BaselineRounds, difficulty);
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
        // map-expand-25 段 A2：截断改为 LinkDecisionSequence.BaselineRounds（6）个大回合，小回合下界 30 → 20、收尾大回合 13 → BaselineRounds + 1。
        Assert.True(lines.Count(l => l.Length > 0 && char.IsDigit(l[0])) >= 20, "基线小回合不足 20 个");
        Assert.Contains(lines, l => l.StartsWith("# end phase=Ended", StringComparison.Ordinal) || l.Contains($"majorRound={LinkDecisionSequence.BaselineRounds + 1}", StringComparison.Ordinal));
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// ai-whole-batch 守门（规格「整批候选」：开关为 0 时 AI 决策与引入之前逐步相同）：5 份基线在引入整批候选之前（HEAD 0b1b4bf）的原文另存为 <c>*-wb0.txt</c>，
    /// 以跑局配置显式给开关 0 重跑同一导出，逐字节比对。首部文案不随配置变化，副本与改前文件逐字节相同。
    /// </summary>
    [Theory]
    [InlineData(1UL, AiDifficulty.Standard, "decisions-link1-seed1-wb0.txt")]
    [InlineData(2UL, AiDifficulty.Standard, "decisions-link1-seed2-wb0.txt")]
    [InlineData(3UL, AiDifficulty.Standard, "decisions-link1-seed3-wb0.txt")]
    [InlineData(1UL, AiDifficulty.Easy, "decisions-link1-easy-seed1-wb0.txt")]
    [InlineData(1UL, AiDifficulty.Hard, "decisions-link1-hard-seed1-wb0.txt")]
    public void 整批候选关闭时与改前基线相同(ulong seed, AiDifficulty difficulty, string fixture)
    {
        string actual = LinkDecisionSequence.Export(seed, LinkDecisionSequence.BaselineRounds, difficulty, attach: null, configure: c => c with { WholeBatch = 0, EditSeed = 0 });
        RebuildIfRequested(fixture, actual);
        string path = Path.Combine(AppContext.BaseDirectory, "AiDecision", "Fixtures", fixture);
        Assert.True(File.Exists(path), $"找不到改前基线副本：{path}");
        string expected = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains($"difficulty={difficulty} ", expected, StringComparison.Ordinal);
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// ai-bridge-candidate 守门（规格「搭桥候选」Scenario「关闭时逐步不变」）：5 份基线在引入搭桥候选之前（HEAD 01efcd4，整批候选缺省开）的原文另存为 <c>*-es0.txt</c>，
    /// 以跑局配置显式给搭桥候选开关 0（整批候选仍取缺省 1）重跑同一导出，逐字节比对。整批候选的守门（<c>*-wb0.txt</c>）同时关掉两个开关。
    /// </summary>
    [Theory]
    [InlineData(1UL, AiDifficulty.Standard, "decisions-link1-seed1-es0.txt")]
    [InlineData(2UL, AiDifficulty.Standard, "decisions-link1-seed2-es0.txt")]
    [InlineData(3UL, AiDifficulty.Standard, "decisions-link1-seed3-es0.txt")]
    [InlineData(1UL, AiDifficulty.Easy, "decisions-link1-easy-seed1-es0.txt")]
    [InlineData(1UL, AiDifficulty.Hard, "decisions-link1-hard-seed1-es0.txt")]
    public void 搭桥候选关闭时与改前基线相同(ulong seed, AiDifficulty difficulty, string fixture)
    {
        string actual = LinkDecisionSequence.Export(seed, LinkDecisionSequence.BaselineRounds, difficulty, attach: null, configure: c => c with { EditSeed = 0 });
        RebuildIfRequested(fixture, actual);
        string path = Path.Combine(AppContext.BaseDirectory, "AiDecision", "Fixtures", fixture);
        Assert.True(File.Exists(path), $"找不到改前基线副本：{path}");
        string expected = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains($"difficulty={difficulty} ", expected, StringComparison.Ordinal);
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// 规则有意变更后重录开关副本（relic-redesign W1 起与主基线同一开关 <c>SIEGE_REBUILD_DECISION_BASELINE=1</c>）：开关副本守的是"关闭时与开关引入前逐步相同"，
    /// 规则本身变了（例如信物刷新接入对局）时，副本与主基线一起按新规则重录——副本仍是"同一规则下关掉该开关"的导出。
    /// </summary>
    private static void RebuildIfRequested(string fixture, string actual)
    {
        if (Environment.GetEnvironmentVariable("SIEGE_REBUILD_DECISION_BASELINE") == "1")
        {
            File.WriteAllText(Path.Combine(SourceFixtureDirectory(), fixture), actual, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    private static string SourceFixtureDirectory([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "") =>
        Path.Combine(Path.GetDirectoryName(thisFile)!, "Fixtures");
}
