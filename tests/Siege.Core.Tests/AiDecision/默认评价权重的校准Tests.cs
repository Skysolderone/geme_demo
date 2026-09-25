using System.Text.Json;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;
using Siege.Core.Tests.CaptureResolution;
using Xunit.Abstractions;

namespace Siege.Core.Tests.AiDecision;

/// <summary>规格：ai-decision —— Requirement: 默认评价权重的校准</summary>
public class 默认评价权重的校准Tests(ITestOutputHelper output)
{
    [Theory]
    // ai-eye 段 D 校准（v5、种子 1–200、4 人标准难度、每档 200 局）：Eye / Safety / Threat 三维扫过档，其余六维沿用旧值、新规则下未单独扫档
    // （EvaluationWeights.CalibrationOf）。本测试把九维当前取值一并钉住——任一维被改动，必须连同 EvaluationWeights 的依据段一起更新才允许变绿。
    [InlineData(EvaluationDimension.PowerGain, 10)]
    [InlineData(EvaluationDimension.EnemyLoss, 8)]
    [InlineData(EvaluationDimension.Relic, 6)]
    [InlineData(EvaluationDimension.Safety, 35)]
    [InlineData(EvaluationDimension.Growth, 4)]
    [InlineData(EvaluationDimension.Initiative, 20)]
    [InlineData(EvaluationDimension.Supply, 2)]
    // ai-eye 段 D2（4.5）：段 A 新增的两维由 0 改为校准值（sim-out/ai-eye-pass-80 = 选定组合）。
    // 变异 M-D2-1t（只改测试不改实现）：Eye / Threat 两行期望对调（200 ↔ 25）→ 红 2（这两行）。
    [InlineData(EvaluationDimension.Eye, 200)]
    [InlineData(EvaluationDimension.Threat, 25)]
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
            EvaluationDimension.Eye => d.Eye,
            EvaluationDimension.Threat => d.Threat,
            _ => throw new ArgumentOutOfRangeException(nameof(dimension)),
        };
        Assert.Equal(calibrated, byProperty);
    }

    [Fact]
    public void 默认停手阈值被改动()
    {
        // 规格 Scenario「默认权重被改动」同样覆盖默认停手阈值。ai-eye 段 D 校准为 80（= 8 × PowerGain 权重 10，裁决 R24；段 B 的初值依据已由扫档取代）。
        // 钉住取值、三档共用（裁决 R1）、源码里的校准口径（地图 / 种子 / 局数 / 档位 / 数据目录）与依据同步。
        // 变异 M-B12t（只改测试不改实现：期望 80 → 81）→ 红 1（本测试）；M-D2-2（实现 80 → 81）→ 红 1（本测试）。
        // M-B16（简单难度预设漏写阈值、回落到构造缺省 0）→ 红 2（本测试、难度分级Tests.简单难度只看即时收益与眼位）。
        Assert.Equal(80, AiSearchConfig.DefaultPassThreshold);
        Assert.All(Enum.GetValues<AiDifficulty>(), d => Assert.Equal(AiSearchConfig.DefaultPassThreshold, AiSearchConfig.ForDifficulty(d).PassThreshold));

        string src = File.ReadAllText(Path.Combine(PresentationFixtures.RepoRoot(), "src", "Siege.Core", "Ai", "AiDifficulty.cs"));
        string status = AiSearchConfig.PassThresholdCalibrationStatus;
        foreach (string evidence in new[] { "ai-eye 段 D 校准", FourPlayerBaseMap.Id, "种子 1–200", "200 局", "sim-out/ai-eye-pass-" })
        {
            Assert.Contains(evidence, status, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("未校准", status, StringComparison.Ordinal);
        Assert.Contains($"{AiSearchConfig.DefaultPassThreshold} / 160", status, StringComparison.Ordinal);   // 选定值在档位清单里
        Assert.Contains(status, src, StringComparison.Ordinal);

        // 已被扫档取代的段 B 初值依据不得留在源码里（D4 改写，R24）。
        Assert.DoesNotContain("待段 D", src, StringComparison.Ordinal);
        Assert.DoesNotContain("取 2 ×", src, StringComparison.Ordinal);
        Assert.Contains($"DefaultPassThreshold = {AiSearchConfig.DefaultPassThreshold}", src, StringComparison.Ordinal);
        Assert.Contains($"PassThreshold = {AiSearchConfig.DefaultPassThreshold}", src.Replace("DefaultPassThreshold", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain($"PassThreshold = {AiSearchConfig.DefaultPassThreshold + 1}", src, StringComparison.Ordinal);
    }

    [Fact]
    public void 规则变更使校准失效()
    {
        // restore-go-core-rules / life-shape 之后旧校准全部失效；ai-eye 段 D 重新扫档，但只扫了 Eye / Safety / Threat 三维（与停手阈值）。
        // 规格：尚未校准的维度 MUST 带显式标注；去掉标注而无新校准记录则守门失败。于是逐维二选一：
        //   扫过档 → 校准记录必须指向跑局证据（地图、种子范围、局数、数据目录）；没扫过 → 必须恰为"沿用旧值、新规则下未单独扫档"。
        // 变异 M-D2-3（CalibrationOf 的缺省分支改成 "已校准"）→ 红 2（本测试、引用未校准维度产出的数据）。
        EvaluationDimension[] swept = [EvaluationDimension.Safety, EvaluationDimension.Eye, EvaluationDimension.Threat];
        foreach (EvaluationDimension d in Enum.GetValues<EvaluationDimension>())
        {
            string status = EvaluationWeights.CalibrationOf(d);
            if (swept.Contains(d))
            {
                foreach (string evidence in new[] { "ai-eye 段 D 校准", FourPlayerBaseMap.Id, "种子 1–200", "200 局", "sim-out/ai-eye-" })
                {
                    Assert.Contains(evidence, status, StringComparison.Ordinal);
                }
            }
            else
            {
                Assert.StartsWith(EvaluationWeights.NotSweptStatus, status, StringComparison.Ordinal);
                Assert.Contains(d.ToString(), EvaluationWeights.CalibrationStatus, StringComparison.Ordinal);
            }

            // more-pieces-relics D10（3.4）：计分扩展（四种新棋子、连营 / 犄角）之后，九维一律补注"扩展计分后未重扫"——扫过档的三维也不例外，
            // 它们的扫档是在旧内容上做的。数值不变（见 默认权重被改动）。变异 MC-C1（去掉补注）应红。
            Assert.EndsWith("more-pieces-relics 扩展计分后未重扫", status, StringComparison.Ordinal);
        }

        Assert.Equal("more-pieces-relics 扩展计分后未重扫", EvaluationWeights.ScoringExtendedStatus);
        Assert.EndsWith(EvaluationWeights.ScoringExtendedStatus, EvaluationWeights.CalibrationStatus, StringComparison.Ordinal);
        Assert.EndsWith(EvaluationWeights.ScoringExtendedStatus, AiSearchConfig.PassThresholdCalibrationStatus, StringComparison.Ordinal);

        Assert.Equal("沿用旧值、新规则下未单独扫档", EvaluationWeights.NotSweptStatus);
        Assert.Contains(EvaluationWeights.NotSweptStatus, EvaluationWeights.CalibrationStatus, StringComparison.Ordinal);

        // 27 / 20 都是更早口径下的 Safety 取值，各自被当时的扫档否定；现口径下 35 是重新扫出来的（恰与旧值相同），不是继承。
        Assert.NotEqual(27, EvaluationWeights.Default.Safety);
        Assert.NotEqual(20, EvaluationWeights.Default.Safety);
    }

    // 变异（只改测试，段 D2）：M-D2-6t 截断计数取反（Truncated → !Truncated）→ 红 1（本测试）；M-D2-7t 样本下界放大到 10000 × 局数 → 红 1（本测试，下界是活的）。
    // 完整版（种子 1–200）实测截断 2 局（101、171），上限 10 局。
    [Fact]
    public void 校准后截断率达标() => AssertTruncation(count: 20, maxTruncated: 1);

    [SlowFact]
    [Trait("Category", "Slow")]
    public void 校准后截断率达标_种子1至200() => AssertTruncation(count: 200, maxTruncated: 10);

    [Fact]
    public void 引用未校准维度产出的数据()
    {
        // 规格：未校准维度的当前默认值产出的数据，引用时须注明其权重口径。机制上由批次记录自带：config.json 写实际生效的九维权重与停手阈值
        // （RunConfig.Effective / ResolvedFor），即便跑局时一项都没显式配置。本测试钉住"六个未单独扫档的维度，其取值都随数据落盘"。
        // 变异 M-D2-5（Effective 不填默认权重）→ 红 2（本测试、批量跑局Tests.批量执行并汇总）。
        // more-pieces-relics 3.4：九维的口径都补注了"扩展计分后未重扫"，未扫档维度由"恰为 NotSweptStatus"改为"以它开头"。
        EvaluationDimension[] notSwept = [.. Enum.GetValues<EvaluationDimension>().Where(d => EvaluationWeights.CalibrationOf(d).StartsWith(EvaluationWeights.NotSweptStatus, StringComparison.Ordinal))];
        Assert.Equal(6, notSwept.Length);   // 样本口径下界：确有未扫档维度可查

        RunConfig config = SimFixtures.Config(seedStart: 5, turnLimit: 4, difficulty: AiDifficulty.Standard, retention: EventRetention.SnapshotsOnly);
        Assert.All(config.Players, p => Assert.Null(p.Weights));
        Assert.Null(config.PassThreshold);
        Assert.Null(config.FlagRisk);   // flag-contest D2：未配置的冒险概率同样落成缺省值写进记录
        Assert.Null(config.ContentSet);   // more-pieces-relics D8：未配置的内容集同样落成缺省 v2 写进记录
        string dir = SimFixtures.TempDir("calibration-provenance");
        BatchRunner.ExecuteToDirectory(config, dir, parallelism: 1);

        RunConfig saved = RunConfig.FromJson(File.ReadAllText(Path.Combine(dir, "config.json")));
        Assert.Equal(AiSearchConfig.DefaultPassThreshold, saved.PassThreshold);
        Assert.All(saved.Players, p =>
        {
            Assert.NotNull(p.Weights);
            foreach (EvaluationDimension d in notSwept)
            {
                Assert.Equal(EvaluationWeights.Default.Of(d), p.Weights!.Of(d));
            }

            Assert.Equal(EvaluationWeights.Default, p.Weights);
        });

        // 反面：只改一个未扫档维度，记录随之不同——口径不同的两份数据从记录上就区分得开，不会被当成同口径直接比较。
        EvaluationWeights other = EvaluationWeights.Default with { Growth = EvaluationWeights.Default.Growth + 1 };
        RunConfig changed = config with { Players = [.. config.Players.Select(p => p with { Weights = other })] };
        Assert.NotEqual(saved.ToJson(), (changed with { PassThreshold = AiSearchConfig.DefaultPassThreshold, FlagRisk = Core.Match.MatchOptions.DefaultFlagRisk, ContentSet = ContentSets.Default }).Effective().ToJson());
        Assert.Equal(saved.ToJson(), (config with { PassThreshold = AiSearchConfig.DefaultPassThreshold, FlagRisk = Core.Match.MatchOptions.DefaultFlagRisk, ContentSet = ContentSets.Default }).Effective().ToJson());
    }

    /// <summary>
    /// 与校准批次同口径：<c>siege-4p-base-v5</c>、种子 1 起、4 名 Standard AI、未显式配置权重与停手阈值（取默认）、小回合数截断 600。
    /// 缩小版（默认套件）种子 1–20、截断至多 1 局（5%）；完整版种子 1–200、至多 10 局（规格原文）。
    /// CLI 对照：<c>sim-out/ai-eye-pass-80</c> 截断 2 局（种子 101、171），种子 1–20 无截断。
    /// </summary>
    private void AssertTruncation(int count, int maxTruncated)
    {
        RunConfig config = SimFixtures.Config(count: count, seedStart: 1, turnLimit: RunConfig.DefaultTurnLimit, difficulty: AiDifficulty.Standard, retention: EventRetention.SnapshotsOnly);
        Assert.Equal(FourPlayerBaseMap.Id, config.MapId);
        Assert.All(config.Players, p => Assert.Null(p.Weights));
        Assert.Null(config.PassThreshold);

        List<MatchLog> logs = BatchRunner.Execute(config, parallelism: Environment.ProcessorCount);
        Assert.Equal(count, logs.Count);
        Assert.All(logs, l => Assert.False(l.IsFailed));
        Assert.All(logs, l => Assert.Equal(AiSearchConfig.DefaultPassThreshold, l.Header.Config.PassThreshold));

        // 样本口径下界：AI 真的在落子。一子不落的局全部 AllPassed、截断为 0，是空证（段 D2 简单难度在阈值 80 下即如此）。
        int placingTurns = logs.Sum(l => l.Turns.Count(t => !t.Passed));
        Assert.True(placingTurns >= 10 * count, $"{count} 局只有 {placingTurns} 个落子小回合");

        ulong[] truncated = [.. logs.Where(l => l.Result!.Truncated).Select(l => l.Seed).Order()];
        output.WriteLine($"种子 1–{count}：截断 {truncated.Length} 局（{string.Join("、", truncated)}），落子小回合 {placingTurns}");
        Assert.True(truncated.Length <= maxTruncated, $"截断 {truncated.Length} 局 > {maxTruncated}：{string.Join("、", truncated)}");
    }

    [Fact]
    public void 未显式配置权重的跑局用默认权重表()
    {
        // 任务 1.2：PlayerAiConfig.Weights 留空 → AI 用 EvaluationWeights.Default（HeuristicTurnController 的 weights ?? Default）。
        // Easy 只看即时收益两维（Safety 恒 0），必须用 Standard 才能让权重表影响决策。
        RunConfig blank = SimFixtures.Config(turnLimit: 8, difficulty: AiDifficulty.Standard);
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

        // ai-eye 段 D2（4.5）：守门为 ① 机读的校准口径常量钉死、且在源码里；② 依据段里逐维点名当前取值，改了值不改这一段就对不上；
        //   扫过档的三维另须写成"<b>名 = 值</b>——ai-eye 段 D 校准"；③ 已作废的旧结论与"待校准"措辞不得留在注释里。
        // 变异 M-D2-4（实现 Eye 200 → 201）→ 红 2（本测试、默认权重被改动(Eye)）。
        Assert.Equal(
            "ai-eye 段 D 校准：Eye / Safety / Threat 三维与停手阈值已在新规则下双向扫档；PowerGain / EnemyLoss / Relic / Growth / Initiative / Supply 六维沿用旧值、新规则下未单独扫档"
            + "；more-pieces-relics 扩展计分后未重扫",   // more-pieces-relics 3.4：计分扩展后补注，取值不变
            EvaluationWeights.CalibrationStatus);
        Assert.Contains(EvaluationWeights.CalibrationStatus, src, StringComparison.Ordinal);

        EvaluationWeights d = EvaluationWeights.Default;
        foreach ((string name, int value) in new[]
                 {
                     (nameof(d.Safety), d.Safety), (nameof(d.PowerGain), d.PowerGain), (nameof(d.EnemyLoss), d.EnemyLoss),
                     (nameof(d.Relic), d.Relic), (nameof(d.Growth), d.Growth), (nameof(d.Initiative), d.Initiative), (nameof(d.Supply), d.Supply),
                     (nameof(d.Eye), d.Eye), (nameof(d.Threat), d.Threat),
                 })
        {
            Assert.Contains($"{name} = {value}", src, StringComparison.Ordinal);
            // 反面：确认上一行不是恒真——把取值 +1 之后的写法不该出现在源码里。
            Assert.DoesNotContain($"{name} = {value + 1}", src, StringComparison.Ordinal);
        }

        foreach ((string name, int value) in new[] { (nameof(d.Eye), d.Eye), (nameof(d.Safety), d.Safety), (nameof(d.Threat), d.Threat) })
        {
            Assert.Contains($"<b>{name} = {value}</b>——ai-eye 段 D 校准", src, StringComparison.Ordinal);
        }

        // ③ 反面：已被计分口径作废的旧结论不得留在注释里——留着会把下一个人往反方向引。
        Assert.DoesNotContain("是校准值", src, StringComparison.Ordinal);
        Assert.DoesNotContain("sim-out/artisan-w5-s", src, StringComparison.Ordinal);
        Assert.DoesNotContain("阶段 B 的首个校准项", src, StringComparison.Ordinal);
        Assert.DoesNotContain("取 40 一半收敛", src, StringComparison.Ordinal);
        Assert.DoesNotContain("待 ai-eye", src, StringComparison.Ordinal);
        Assert.DoesNotContain("未校准（", src, StringComparison.Ordinal);
    }

    /// <summary>一局的过程投影（快照 + 事件），不含首行 header——header 里带配置 JSON，会把"权重填没填"本身混进比对。</summary>
    private static string Play(MatchLog log) =>
        string.Join("\n", SimFixtures.TurnTexts(log.Turns))
        + "\n----\n"
        + string.Join("\n", log.Events.Select(e => JsonSerializer.Serialize(e)));
}
