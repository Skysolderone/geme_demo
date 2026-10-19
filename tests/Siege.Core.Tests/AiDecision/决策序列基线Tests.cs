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
/// <para>final-countdown 1.4 按缺省 K = 2 重钉（导出走跑局层缺省，K 落成 2）。改前的五条基线原样另存为 <c>*-k0.txt</c>，
/// 由 <c>Battlefield.决赛倒计时Tests.K为0时链接图决策序列与改前基线逐字节相同</c> 以 K = 0 逐字节比对。逐条说明（导出里新增的 <c>C</c> 行 = 战场进入倒计时）：</para>
/// <list type="bullet">
/// <item>种子 1（标准，2+1+1，zones=3,3,1,0）：同区战场 4（P0、P1）第 7 大回合整轮 Pass 结算、P0 胜 → R = 7，首轮战场 1（P2、P3）期限第 9 大回合；
/// 第 9 大回合结束时战场 1 到期结算（P3 164 胜 P2 108），第 10 大回合起决赛。改前该战场到第 12 大回合仍未结算。第 1–9 大回合的决策行逐字节不变。</item>
/// <item>种子 2（标准，2+1+1，zones=3,0,1,0）：同区战场 1（P1、P3）第 8 大回合整轮 Pass 结算、P3 胜 → R = 8，首轮战场 2（P0、P2）期限第 10 大回合；
/// 第 10 大回合结束时到期结算（P0 140 胜 P2 101），第 11 大回合起决赛。改前第 12 大回合才整轮 Pass 结算（P0 144 胜 P2 108）。第 1–10 大回合的决策行逐字节不变。</item>
/// <item>简单种子 1（2+1+1）：截到第 12 大回合没有任何战场结算、不出现等待者，逐字节不变。</item>
/// <item>困难种子 1（2+1+1）：同区战场 4 第 7 大回合整轮 Pass 结算、P1 胜 → R = 7，首轮战场 1 期限第 9 大回合；第 9 大回合结束时到期结算（P2 84 胜 P3 80），
/// 第 10 大回合起决赛。改前第 11 大回合才整轮 Pass 结算（同样 P2 84 胜 P3 80）。第 1–9 大回合的决策行逐字节不变。</item>
/// </list>
/// <para>ai-seat-vision 1.4（AI 缺省改为按座位视野，裁决 V1 / V4）按新缺省重钉。改前五条原样另存为 <c>*-omniscient.txt</c>，由
/// <c>正式对战AI的信息边界_链接模式Tests.全知配置下与改前相同</c> 以显式全知逐字节比对（五条全过）。逐条说明：</para>
/// <list type="bullet">
/// <item>种子 1（标准）、简单种子 1、困难种子 1：逐字节不变（按座位视野下这三局截到第 12 大回合的每一手与全知相同）。</item>
/// <item>种子 2（标准）：第 1–4 大回合与第 5 大回合 P2 逐字节不变；第 5 大回合 P3（与 P1 同在链接后的战场）原落 4 子 <c>AB3:Basic,Z6:Multiplier,Y7:Synergy,X8:Fortress</c>，
/// 现落 3 子 <c>AB3:Fortress,Y7:Synergy,X8:Multiplier</c>。实测两批的九维原始值只差先手维（维度 6）：全知下 P3 的全局势力名次 2 → 1（他战场的 P2 势力 60 可见），
/// 按座位视野下他战场玩家在投影里势力为 0，名次只在同战场内比较、1 → 1，先手维为 0；贪心组批的逐枚边际随之变。此后同区战场 0 改在第 9 大回合整轮 Pass 结算
/// （P3 341 胜 P1 30；原第 8 大回合 347 对 30），倒计时期限第 11 大回合（原 10），战场 1 第 11 大回合到期结算（P0 142 胜 P2 108；原第 10 大回合 140 对 101），第 12 大回合起决赛；42 → 44 个小回合。</item>
/// </list>
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

    /// <summary>
    /// open-map 段 A 自洽检查（design D9 / 实施裁决 A8）：锁定后座位视野 = 全图，且唯一战场上永不出现等待者、倒计时起点永不设定，
    /// 所以显式全知（<c>*-omniscient.txt</c>）与 K = 0（<c>*-k0.txt</c>）两组基线 MUST 与缺省逐字节相同——段 A 把三组一次重钉为同一内容，段 B 删掉两组副本。
    /// 变异（open-map 1.5 实跑）：把任一副本改动一个字节 → 本测试红。
    /// </summary>
    [Theory]
    [InlineData("decisions-link1-seed1")]
    [InlineData("decisions-link1-seed2")]
    [InlineData("decisions-link1-seed3")]
    [InlineData("decisions-link1-easy-seed1")]
    [InlineData("decisions-link1-hard-seed1")]
    public void 全知与K0副本与缺省逐字节相同(string stem)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "AiDecision", "Fixtures");
        byte[] standard = File.ReadAllBytes(Path.Combine(dir, stem + ".txt"));
        Assert.True(standard.Length > 1000, stem);
        Assert.Equal(standard, File.ReadAllBytes(Path.Combine(dir, stem + "-omniscient.txt")));
        Assert.Equal(standard, File.ReadAllBytes(Path.Combine(dir, stem + "-k0.txt")));
    }

    /// <summary>
    /// open-map 段 A 自洽检查的"执行"半边（质检补；design D6：唯一战场上倒计时起点 R 永不设定，K 不起作用）：显式 K = 0、缺省视野跑一局，
    /// 与 <c>*-k0.txt</c>（= 缺省基线副本）逐字节相同。显式全知的那半边由 <c>正式对战AI的信息边界_链接模式Tests.全知配置下与改前相同</c> 实跑；
    /// 原 K = 0 实跑比对 <c>决赛倒计时Tests.K为0时链接图决策序列与改前基线逐字节相同</c> 随整文件 Skip 停跑，这里接替。段 B 随 K 一起删除。
    /// 变异：把任一 <c>*-k0.txt</c> 改动一个字节 → 本测试与「全知与K0副本与缺省逐字节相同」同时红。
    /// </summary>
    [Theory]
    [InlineData(1UL, AiDifficulty.Standard, "decisions-link1-seed1-k0.txt")]
    [InlineData(2UL, AiDifficulty.Standard, "decisions-link1-seed2-k0.txt")]
    [InlineData(3UL, AiDifficulty.Standard, "decisions-link1-seed3-k0.txt")]
    [InlineData(1UL, AiDifficulty.Easy, "decisions-link1-easy-seed1-k0.txt")]
    [InlineData(1UL, AiDifficulty.Hard, "decisions-link1-hard-seed1-k0.txt")]
    public void K为0实跑与缺省基线逐字节相同(ulong seed, AiDifficulty difficulty, string fixture)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "AiDecision", "Fixtures", fixture);
        string expected = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.True(expected.Split('\n').Count(l => l.Length > 0 && char.IsDigit(l[0])) >= 30, "基线小回合不足 30 个");
        Assert.DoesNotContain(expected.Split('\n'), l => l.StartsWith("C\t", StringComparison.Ordinal));   // 没有任何战场进入倒计时

        Assert.Equal(expected, LinkDecisionSequence.Export(seed, 12, difficulty, attach: null, finalCountdown: 0));
    }

    private static string SourceFixtureDirectory([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "") =>
        Path.Combine(Path.GetDirectoryName(thisFile)!, "Fixtures");
}
