using System.Text.Json;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.AiDecision;

/// <summary>规格：ai-decision —— Requirement: 默认评价权重的校准</summary>
public class 默认评价权重的校准Tests
{
    [Theory]
    // 校准表出处：openspec/changes/ai-safety-weight/proposal.md（Safety 一维由 v2 九档各 200 局扫档定为 5，见 design.md §2）；
    // 其余六维仍是 heuristic-ai 阶段的初值，本测试把它们一并钉住（design.md D4）——任一维被改动，必须连同校准记录一起更新才允许变绿。
    [InlineData(EvaluationDimension.PowerGain, 10)]
    [InlineData(EvaluationDimension.EnemyLoss, 8)]
    [InlineData(EvaluationDimension.Relic, 6)]
    [InlineData(EvaluationDimension.Safety, 5)]
    [InlineData(EvaluationDimension.Growth, 4)]
    [InlineData(EvaluationDimension.Initiative, 20)]
    [InlineData(EvaluationDimension.Supply, 2)]
    public void 默认权重被改动(EvaluationDimension dimension, int calibrated)
    {
        EvaluationWeights d = EvaluationWeights.Default;

        // 逐维按下标读（Of 的分派）。
        Assert.Equal(calibrated, d.Of(dimension));

        // 再按属性读一遍：只断言 Of 的话，Of 内部把两维对调仍可能双双落在别的期望值上。
        int byProperty = dimension switch
        {
            EvaluationDimension.PowerGain => d.PowerGain,
            EvaluationDimension.EnemyLoss => d.EnemyLoss,
            EvaluationDimension.Relic => d.Relic,
            EvaluationDimension.Safety => d.Safety,
            EvaluationDimension.Growth => d.Growth,
            EvaluationDimension.Initiative => d.Initiative,
            EvaluationDimension.Supply => d.Supply,
            _ => throw new ArgumentOutOfRangeException(nameof(dimension)),
        };
        Assert.Equal(calibrated, byProperty);
    }

    [Fact]
    public void 安全权重取校准值()
    {
        // design.md §2 / D1：v2 基准图 3 / 5 / 7 / 8 / 10 / 20 / 30 / 40 / 60 九档各 200 局，取 5（不收敛率 9.5%、领先者胜率 51.0%）。
        Assert.Equal(5, EvaluationWeights.Default.Safety);
        Assert.Equal(5, EvaluationWeights.Default.Of(EvaluationDimension.Safety));

        // 旧初值 20 是 v1 上 5 局 2 颗种子试出的未校准值，已被扫档否定（不收敛率 27.5%、领先者胜率 64.5%）。
        Assert.NotEqual(20, EvaluationWeights.Default.Safety);
    }

    [Fact]
    public void 未显式配置权重的跑局用默认权重表()
    {
        // 任务 1.2：PlayerAiConfig.Weights 留空 → AI 用 EvaluationWeights.Default（HeuristicTurnController 的 weights ?? Default）。
        // Easy 只看即时收益两维（Safety 恒 0），必须用 Standard 才能让权重表影响决策。
        RunConfig blank = SimFixtures.Config(maxRounds: 2, difficulty: AiDifficulty.Standard);
        Assert.All(blank.Players, p => Assert.Null(p.Weights));

        MatchSession session = MatchSession.Create(blank, 41);
        MatchLog blankLog = session.Run();
        foreach (PlayerId p in session.Match.Players)
        {
            Assert.Equal(EvaluationWeights.Default, session.AiOf(p)!.Weights);
        }

        // 属性相等还不够：它可能只是读回了同一个静态字段。再从行为上钉——留空跑出的局与显式传 Default 的局逐步一致。
        // 比的是快照 + 事件，不比 DeterministicText：首行 header 里带着配置 JSON，Weights 填不填本身就使整段文本不同，
        // 那样"相等"恒假、"不等"恒真，两条断言都不在守门。
        RunConfig asDefault = blank with { Players = [.. blank.Players.Select(p => p with { Weights = EvaluationWeights.Default })] };
        MatchLog defaultLog = MatchSession.Create(asDefault, 41).Run();

        // 比对配覆盖范围下界（testing.md M-C5）：投影砍到只剩一行时比对恒真。
        Assert.True(blankLog.Turns.Count >= 4, $"样本只有 {blankLog.Turns.Count} 个小回合");
        Assert.NotEmpty(blankLog.Events);
        Assert.Equal(SimFixtures.TurnTexts(blankLog.Turns), SimFixtures.TurnTexts(defaultLog.Turns));
        Assert.Equal(Play(blankLog), Play(defaultLog));

        // 反面：换一张权重表，同种子同配置必须跑出不同的局，否则上面那条"一致"是恒真的。
        RunConfig other = blank with
        {
            Players = [.. blank.Players.Select(p => p with { Weights = EvaluationWeights.Default with { Safety = 200 } })],
        };
        MatchLog otherLog = MatchSession.Create(other, 41).Run();
        Assert.NotEqual(Play(blankLog), Play(otherLog));
    }

    [Fact]
    public void 默认权重的校准依据随值一起更新()
    {
        // 规格：ai-decision「默认评价权重的校准」Scenario 3（design.md D6）。
        // 挡的是本 change 最可能重演的失败：Safety=20 之所以一路沿用，正是因为它的依据（"5 局 / 2 颗种子 / v1 地图 / 待校准"）
        // 写在注释里却没有任何东西强制它与取值同步，于是值被沿用、依据被无视。
        string src = File.ReadAllText(
            Path.Combine(PresentationFixtures.RepoRoot(), "src", "Siege.Core", "Ai", "EvaluationWeights.cs"));

        // 依据里声明的校准值必须就是实际取值：改了值不改注释，这里就对不上。
        // 不用正则，避免 testing.md 记过的"以  开头匹配复合标识符"那类坑。
        int actual = EvaluationWeights.Default.Safety;
        Assert.Contains($"= {actual} 是校准值", src, StringComparison.Ordinal);

        // 依据必须指向可复查的数据，而不只是一句结论。
        Assert.Contains("sim-out/safety", src, StringComparison.Ordinal);
        Assert.Contains("200 局", src, StringComparison.Ordinal);

        // 反面：已被扫档推翻的旧结论不得留在注释里——留着会把下一个人往反方向引。
        Assert.DoesNotContain("阶段 B 的首个校准项", src, StringComparison.Ordinal);
        Assert.DoesNotContain("取 40 一半收敛", src, StringComparison.Ordinal);

        // 反面之二：确认上面那条"声明值等于实际值"不是恒真——换一个不等于实际取值的数字，源码里不该出现。
        Assert.DoesNotContain($"= {actual + 1} 是校准值", src, StringComparison.Ordinal);
    }

    /// <summary>一局的过程投影（快照 + 事件），不含首行 header——header 里带配置 JSON，会把"权重填没填"本身混进比对。</summary>
    private static string Play(MatchLog log) =>
        string.Join("\n", SimFixtures.TurnTexts(log.Turns))
        + "\n----\n"
        + string.Join("\n", log.Events.Select(e => JsonSerializer.Serialize(e)));
}
