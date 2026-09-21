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
    // restore-go-core-rules 起七维全部标注为未校准（EvaluationWeights.CalibrationStatus）：计分口径变了，此前每一档扫档结论所依赖的分数尺度都不复存在。
    // 本测试把七维当前取值一并钉住——任一维被改动，必须连同 EvaluationWeights 的依据段一起更新才允许变绿。
    [InlineData(EvaluationDimension.PowerGain, 10)]
    [InlineData(EvaluationDimension.EnemyLoss, 8)]
    [InlineData(EvaluationDimension.Relic, 6)]
    [InlineData(EvaluationDimension.Safety, 35)]
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
    public void 规则变更使校准失效()
    {
        // restore-go-core-rules 改写了军势公式与总势力构成，旧口径下扫档得到的全部默认权重随之失效。
        // 规格「默认评价权重的校准」：在 ai-eye 重新扫档之前，权重表 MUST 带显式的未校准标注，
        // 且 MUST NOT 声称任一维度已校准——本条钉住标注在位，并挡住"悄悄把值改回旧口径的取值"。

        Assert.Contains("未校准", EvaluationWeights.CalibrationStatus);
        Assert.DoesNotContain("已校准", EvaluationWeights.CalibrationStatus);

        // 27 / 20 都是更早口径下的 Safety 取值，各自被当时的扫档否定；现口径下同样不是校准值。
        Assert.NotEqual(27, EvaluationWeights.Default.Safety);
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

        // restore-go-core-rules 段 B（tasks 2.6）：计分口径一变，七维的校准依据全部失效。
        // 守门改为：① 有一条机读的"未校准"标注常量，且它就是 ai-decision 规格要求的显式标注；
        //           ② 依据段里逐维点名当前取值，改了值不改这一段就对不上；③ 已作废的旧结论不得留在注释里。
        Assert.Equal("未校准（restore-go-core-rules 起失效，待 ai-eye）", EvaluationWeights.CalibrationStatus);
        Assert.Contains(EvaluationWeights.CalibrationStatus, src, StringComparison.Ordinal);
        Assert.Contains("ai-eye", src, StringComparison.Ordinal);

        EvaluationWeights d = EvaluationWeights.Default;
        foreach ((string name, int value) in new[]
                 {
                     (nameof(d.Safety), d.Safety), (nameof(d.PowerGain), d.PowerGain), (nameof(d.EnemyLoss), d.EnemyLoss),
                     (nameof(d.Relic), d.Relic), (nameof(d.Growth), d.Growth), (nameof(d.Initiative), d.Initiative), (nameof(d.Supply), d.Supply),
                 })
        {
            Assert.Contains($"{name} = {value}", src, StringComparison.Ordinal);
            // 反面：确认上一行不是恒真——把取值 +1 之后的写法不该出现在源码里。
            Assert.DoesNotContain($"{name} = {value + 1}", src, StringComparison.Ordinal);
        }

        // ③ 反面：已被计分口径作废的旧结论不得留在注释里——留着会把下一个人往反方向引。
        Assert.DoesNotContain("是校准值", src, StringComparison.Ordinal);
        Assert.DoesNotContain("sim-out/artisan-w5-s", src, StringComparison.Ordinal);
        Assert.DoesNotContain("阶段 B 的首个校准项", src, StringComparison.Ordinal);
        Assert.DoesNotContain("取 40 一半收敛", src, StringComparison.Ordinal);
    }

    /// <summary>一局的过程投影（快照 + 事件），不含首行 header——header 里带配置 JSON，会把"权重填没填"本身混进比对。</summary>
    private static string Play(MatchLog log) =>
        string.Join("\n", SimFixtures.TurnTexts(log.Turns))
        + "\n----\n"
        + string.Join("\n", log.Events.Select(e => JsonSerializer.Serialize(e)));
}
