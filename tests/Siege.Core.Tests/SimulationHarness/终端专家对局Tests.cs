using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Sim.Play;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>
/// expert-lookahead tasks 2.2：终端 <c>play --difficulty Expert</c> 开局，AI 玩家按专家决策。
/// 终端对局不写对局日志（tasks 原文"日志首部记有专家与前瞻宽度 4"写不成），改由开局行 + 测试接缝 <c>onAi</c> 核对：
/// 每名 AI 的难度与实际生效的搜索配置是专家、前瞻宽度 4，且跑过的部署决策留下了前瞻记录。
/// </summary>
public class 终端专家对局Tests
{
    [Fact]
    public void 专家难度开局_AI按专家决策()
    {
        // 脚本同 终端对局Tests：选 1 号区，第 1 大回合落 B1 并确认，第 2 大回合 Pass，第 3 大回合提示处输入耗尽干净退出（保护期内不依赖 AI 走法）。
        // 权重与停手阈值写死（testing.md「依赖 AI 实际怎么走的断言要把权重写死」）。
        // 变异 M-B2（终端建 AI 时用高难的搜索配置）→ 本测试红。
        var ais = new Dictionary<PlayerId, HeuristicTurnController>();
        var output = new StringWriter();
        int exit = PlayCommand.Run(
            42, 4, 1, AiDifficulty.Expert, new StringReader("1\n1\nB1 B\nv\nok\n\npass\n"), output,
            weights: LookaheadFixtures.Weights, passThreshold: LookaheadFixtures.PassThreshold, flagRisk: 0, contentSet: ContentSet.V1,
            onAi: (p, ai) => ais.Add(p, ai));
        string text = output.ToString();

        Assert.Equal(0, exit);
        Assert.Contains("对手 3 名 Expert AI（前瞻宽度 4）", text, StringComparison.Ordinal);
        Assert.Contains("玩家1(你) 落子 B1B", text, StringComparison.Ordinal);
        Assert.Contains("第 3 大回合", text, StringComparison.Ordinal);
        Assert.Contains("已退出。种子 42", text, StringComparison.Ordinal);

        Assert.Equal([1, 2, 3], ais.Keys.Select(p => p.Value).Order());
        Assert.All(ais.Values, ai =>
        {
            Assert.Equal(AiSearchConfig.DefaultLookaheadWidth, ai.Config.LookaheadWidth);
            Assert.Equal(AiSearchConfig.Expert with { CandidateCellLimit = ai.Config.CandidateCellLimit, PassThreshold = LookaheadFixtures.PassThreshold }, ai.Config);
            Assert.True(ai.Decisions.Count >= 2, $"只做了 {ai.Decisions.Count} 次决策");   // 样本口径：两个大回合里每名 AI 都部署过
            Assert.NotNull(ai.LastLookahead);   // 专家的每次部署决策都留前瞻记录（宽度 0 的控制者恒为 null）
        });
        Assert.Contains(ais.Values, ai => ai.LastLookahead!.Status != LookaheadStatus.Pass || ai.Decisions.Any(d => d != "D:pass"));
    }
}
