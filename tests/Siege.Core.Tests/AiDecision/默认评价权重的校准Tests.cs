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
public class 默认评价权重的校准Tests
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
        // 规格 Scenario「默认权重被改动」同样覆盖默认停手阈值；「小规模校准须写明局数与内容集」钉 V2 复核的口径。
        // v2-recalibration 段 A（负责人 2026-09-28 裁决 ①）：在内容集 V2 上以 0 / 20 / 40 / 80 四档、每档 20 局复核，选定 20（ai-eye 段 D 在 V1 上的 80 只作历史口径）。
        // 钉住取值、四档共用（ai-eye 裁决 R1）、源码里的校准口径（地图 / 内容集 / 种子 / 局数 / 档位 / 各档数据 / 选定值 / 数据目录）与依据同步。
        // 历史变异（ai-eye）：M-B12t / M-D2-2 / M-B16 见 ai-eye 实施记录。v2-recalibration 段 A 变异见任务 09-28-v2-recalibration 的 implement.md「段 A」。
        Assert.Equal(20, AiSearchConfig.DefaultPassThreshold);
        Assert.All(Enum.GetValues<AiDifficulty>(), d => Assert.Equal(AiSearchConfig.DefaultPassThreshold, AiSearchConfig.ForDifficulty(d).PassThreshold));

        string src = File.ReadAllText(Path.Combine(PresentationFixtures.RepoRoot(), "src", "Siege.Core", "Ai", "AiDifficulty.cs"));
        string status = AiSearchConfig.PassThresholdCalibrationStatus;
        const string tiers = "0 / 20 / 40 / 80";
        // 产品文本里的校准地图仍写 siege-4p-base-v5（历史口径）；retire-legacy-maps 段 B 起紧跟"该图已删除、棋盘图上未校准"的补注（EvaluationWeights.RetiredCalibrationMapNote）。
        foreach (string evidence in new[] { "v2-recalibration", $"siege-4p-base-v5（{EvaluationWeights.RetiredCalibrationMapNote}）", "V2", "种子 1–20", "20 局", "小样本", tiers, "sim-out/v2-recalibration/pass-" })
        {
            Assert.Contains(evidence, status, StringComparison.Ordinal);
        }

        Assert.Equal("该图已于 retire-legacy-maps 删除，棋盘图上未校准", EvaluationWeights.RetiredCalibrationMapNote);

        // 选定值在档位清单里、口径写明选定值；四个档位各有"截断 / 整局无提子 / 已终局局平均结束大回合"一组数据。
        int[] tierValues = [.. tiers.Split(" / ").Select(int.Parse)];
        Assert.Contains(AiSearchConfig.DefaultPassThreshold, tierValues);
        Assert.Contains($"选定 {AiSearchConfig.DefaultPassThreshold}（", status, StringComparison.Ordinal);
        Assert.All(tierValues, t => Assert.Matches($@"(?<!\d){t} → \d+ / \d+ / \d+\.\d+", status));

        // ai-eye 的 V1、200 局口径不再是机读口径（它只作为历史写在注释里，并标注内容集 V1）。
        // retire-legacy-maps 段 B：口径里唯一允许出现的"未校准"是校准地图已删除的补注（上面已断言它紧跟 v5），去掉补注后不得再有。
        Assert.DoesNotContain("未校准", status.Replace(EvaluationWeights.RetiredCalibrationMapNote, string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("种子 1–200", status, StringComparison.Ordinal);
        Assert.DoesNotContain("sim-out/ai-eye-pass-", status, StringComparison.Ordinal);
        Assert.Contains(status, src, StringComparison.Ordinal);
        Assert.Contains("内容集 V1", src, StringComparison.Ordinal);

        // 已被扫档取代的段 B 初值依据、以及选定值不再成立的"= 8 × 即时势力增量权重"比例依据不得留在源码里（D4 改写，R24；v2-recalibration 1.8）。
        Assert.DoesNotContain("待段 D", src, StringComparison.Ordinal);
        Assert.DoesNotContain("取 2 ×", src, StringComparison.Ordinal);
        Assert.DoesNotContain("= 8 ×", src, StringComparison.Ordinal);
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
                // v2-recalibration 1.8：三维的扫档在内容集 V1 上完成（缺省内容集已是 V2），口径 MUST 注明内容集。
                // retire-legacy-maps 段 B：校准地图 v5 已删除，口径 MUST 紧跟"棋盘图上未校准"的补注。
                foreach (string evidence in new[] { "ai-eye 段 D 校准", "内容集 V1", $"siege-4p-base-v5（{EvaluationWeights.RetiredCalibrationMapNote}）", "种子 1–200", "200 局", "sim-out/ai-eye-" })
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
        // v2-recalibration 段 A（1.7）：停手阈值已在内容集 V2 上复核，其口径不再带"扩展计分后未重扫"；九维权重（上面的循环）仍带。
        Assert.False(AiSearchConfig.PassThresholdCalibrationStatus.EndsWith(EvaluationWeights.ScoringExtendedStatus, StringComparison.Ordinal));
        Assert.DoesNotContain(EvaluationWeights.ScoringExtendedStatus, AiSearchConfig.PassThresholdCalibrationStatus, StringComparison.Ordinal);

        Assert.Equal("沿用旧值、新规则下未单独扫档", EvaluationWeights.NotSweptStatus);
        Assert.Contains(EvaluationWeights.NotSweptStatus, EvaluationWeights.CalibrationStatus, StringComparison.Ordinal);

        // 27 / 20 都是更早口径下的 Safety 取值，各自被当时的扫档否定；现口径下 35 是重新扫出来的（恰与旧值相同），不是继承。
        Assert.NotEqual(27, EvaluationWeights.Default.Safety);
        Assert.NotEqual(20, EvaluationWeights.Default.Safety);
    }

    // retire-legacy-maps 段 A2（主会话裁决 2）：「校准后截断率达标」（种子 1–20 至多截断 1 局）与慢测试版（种子 1–200 至多 10 局）删除——
    // 它们守的是 AI 在 siege-4p-base-v5 上的校准结论，棋盘图上权重"未在新地图上校准"（母任务裁决 6），由 AI 校准 change 在棋盘图上重定。

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
        Assert.NotEqual(saved.ToJson(), (changed with { PassThreshold = AiSearchConfig.DefaultPassThreshold, FlagRisk = Core.Match.MatchOptions.DefaultFlagRisk, ContentSet = ContentSets.Default, ScoringVersion = Core.Scoring.ScoringVersions.Default, CarryIn = 0, CandidateCellLimit = AiSearchConfig.LargeMapCellLimit }).Effective().ToJson());
        // carry-in-out 段 C：未配置的带入数量同样落成 0 写进 config.json。formation-tiers D2：未配置的计分规则版本同样落成缺省 v2 写入。
        // retire-legacy-maps 段 A：夹具地图为 4 人棋盘图（> 150 格），未配置的候选格上限落成 24 写入。
        Assert.Equal(saved.ToJson(), (config with { PassThreshold = AiSearchConfig.DefaultPassThreshold, FlagRisk = Core.Match.MatchOptions.DefaultFlagRisk, ContentSet = ContentSets.Default, ScoringVersion = Core.Scoring.ScoringVersions.Default, CarryIn = 0, CandidateCellLimit = AiSearchConfig.LargeMapCellLimit }).Effective().ToJson());
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
            "ai-eye 段 D 校准（内容集 V1）：Eye / Safety / Threat 三维与停手阈值已在新规则下双向扫档；PowerGain / EnemyLoss / Relic / Growth / Initiative / Supply 六维沿用旧值、新规则下未单独扫档"
            + "；停手阈值另经 v2-recalibration 在内容集 V2 上复核（每档 20 局，见 AiSearchConfig.PassThresholdCalibrationStatus）"   // v2-recalibration 1.8：九维取值不变
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

    [Fact]
    public void 计分规则v2下标注未校准()
    {
        // formation-tiers design D4（testing.md「规则变更后，权重 MUST 先重新标注为未校准」）：阵型改了计分口径，九维权重、停手阈值与 2 人图覆盖表
        // 都是在计分规则 v1 下取得的——取值不改、评价函数不改，但 MUST 带"计分规则 v2 下未校准"的显式标注，直到负责人按验证跑局裁决。
        // 机读常量一处（EvaluationWeights.FormationScoringStatus），四处依据段（九维的校准口径、默认权重、2 人图覆盖表、缺省停手阈值）各自点名引用它。
        // 措辞说明：上一条守门禁止 EvaluationWeights.cs 出现字面量「未校准（」（ai-eye 校准后清掉的旧标注），所以写作「计分规则 v2 下未校准」。
        // 变异（实跑）：M-A1 删掉 DefaultPassThreshold 依据段里的这段标注 → 本测试红。
        Assert.StartsWith("计分规则 v2 下未校准", EvaluationWeights.FormationScoringStatus, StringComparison.Ordinal);
        Assert.Contains("停手阈值", EvaluationWeights.FormationScoringStatus, StringComparison.Ordinal);
        Assert.Contains("2 人图覆盖表", EvaluationWeights.FormationScoringStatus, StringComparison.Ordinal);

        string ai = Path.Combine(PresentationFixtures.RepoRoot(), "src", "Siege.Core", "Ai");
        string weights = File.ReadAllText(Path.Combine(ai, "EvaluationWeights.cs"));
        string difficulty = File.ReadAllText(Path.Combine(ai, "AiDifficulty.cs"));
        Assert.Contains(EvaluationWeights.FormationScoringStatus, weights, StringComparison.Ordinal);
        // 九维校准口径、默认权重、2 人图覆盖表三段各一处 + 常量自己的说明。
        Assert.True(System.Text.RegularExpressions.Regex.Matches(weights, "<b>计分规则 v2 下未校准</b>").Count >= 3);
        Assert.True(System.Text.RegularExpressions.Regex.Matches(weights, "<see cref=\"FormationScoringStatus\"/>").Count >= 3);
        // 缺省停手阈值的依据段与其校准口径常量的说明各引用一次。
        Assert.Contains("<b>计分规则 v2 下未校准</b>", difficulty, StringComparison.Ordinal);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(difficulty, "EvaluationWeights\\.FormationScoringStatus").Count);

        // 取值未改（改值是负责人裁决之后的事）。
        Assert.Equal(new EvaluationWeights(PowerGain: 10, EnemyLoss: 8, Relic: 6, Safety: 35, Growth: 4, Initiative: 20, Supply: 2, Eye: 200, Threat: 25), EvaluationWeights.Default);
        Assert.Equal(20, AiSearchConfig.DefaultPassThreshold);
    }

    [Fact]
    public void 校准地图已删除的补注()
    {
        // 规格：ai-decision「默认评价权重的校准」Scenario「校准地图已删除的补注」（retire-legacy-maps 段 D 收尾补）。
        // 九维权重与停手阈值都是在 siege-4p-base-v5 上扫的档，该图已删除：每处写到它的校准口径（机读常量与两份源码的依据段）都必须紧跟补注，
        // 补注不得与地图分离（例如挪到种子范围之后）。源码里的写法只有三种：字面量补注、<see cref="RetiredCalibrationMapNote"/>、
        // <see cref="EvaluationWeights.RetiredCalibrationMapNote"/>；补注常量自己的说明段（"都在 v5 上做，该图已删除"）豁免。
        // 变异 D-C1（段 D 实跑）：EvaluationWeights.CalibrationOf(Threat) 的口径去掉补注 → 本测试红。
        // 变异 D-C2（段 D 实跑）：AiDifficulty 历史口径段把 <see cref> 补注挪到"种子 1–200"之后（与地图分离）→ 本测试红（只有本测试守这段注释）。
        string note = EvaluationWeights.RetiredCalibrationMapNote;
        const string map = "siege-4p-base-v5";

        int machine = 0;
        foreach (string status in Enum.GetValues<EvaluationDimension>().Select(EvaluationWeights.CalibrationOf).Append(AiSearchConfig.PassThresholdCalibrationStatus))
        {
            int at = status.IndexOf(map, StringComparison.Ordinal);
            if (at < 0)
            {
                continue;
            }

            machine++;
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(status, map));
            Assert.StartsWith($"{map}（{note}）", status[at..], StringComparison.Ordinal);
        }

        Assert.True(machine >= 4, $"样本口径：写到 {map} 的机读口径只有 {machine} 条（应为 Eye / Safety / Threat 与停手阈值）");

        string ai = Path.Combine(PresentationFixtures.RepoRoot(), "src", "Siege.Core", "Ai");
        string[] followers =
        [
            $"（{note}）",
            "</c>（<see cref=\"RetiredCalibrationMapNote\"/>）",
            "</c>（<see cref=\"EvaluationWeights.RetiredCalibrationMapNote\"/>）",
        ];
        foreach ((string file, int minimum) in new[] { ("EvaluationWeights.cs", 5), ("AiDifficulty.cs", 3) })
        {
            string src = File.ReadAllText(Path.Combine(ai, file));
            int[] hits = [.. System.Text.RegularExpressions.Regex.Matches(src, map).Select(m => m.Index)];
            Assert.True(hits.Length >= minimum, $"样本口径：{file} 里只有 {hits.Length} 处 {map}");
            foreach (int at in hits)
            {
                string rest = src[(at + map.Length)..];
                if (rest.StartsWith("</c> 上做，该图已删除", StringComparison.Ordinal))
                {
                    continue;   // 补注常量自己的说明段
                }

                Assert.True(
                    followers.Any(f => rest.StartsWith(f, StringComparison.Ordinal)),
                    $"{file}：第 {src[..at].Count(ch => ch == '\n') + 1} 行的 {map} 之后没有紧跟补注：{rest[..Math.Min(40, rest.Length)]}");
            }
        }
    }

    [Fact]
    public void 地图覆盖表为空且无校准口径()
    {
        // 规格：ai-decision「地图专属评价权重覆盖」Scenario「覆盖表被改动」。原名「两人图覆盖表的校准记录随值一起更新」，钉的是唯一一项
        // siege-2p-base-v1（Eye 50，v2-recalibration 段 B 扫档选定）与它的校准口径。retire-legacy-maps 段 B（design D2 / 已知歧义 4）：
        // 该图删除，条目随之删除，机制保留、表为空；口径常量 TwoPlayerOverrideCalibrationStatus 一并删除。
        // 改为钉"表为空 ⇔ 无任何校准口径"：往表里加一项而不同时给口径（或反之）→ 本测试红。
        Assert.Empty(EvaluationWeights.MapOverrides);
        foreach (string id in MapCatalog.BuiltinIds.Concat(MapCatalog.RetiredIds).Append("board:12345"))
        {
            Assert.Null(EvaluationWeights.MapOverrideCalibrationOf(id));
            Assert.Same(EvaluationWeights.Default, EvaluationWeights.ForMapId(id));
        }

        string src = File.ReadAllText(Path.Combine(PresentationFixtures.RepoRoot(), "src", "Siege.Core", "Ai", "EvaluationWeights.cs"));
        Assert.DoesNotContain("TwoPlayerOverrideCalibrationStatus", src, StringComparison.Ordinal);
        Assert.DoesNotContain("KeyValuePair.Create(", src, StringComparison.Ordinal);   // 登记表里没有任何一行登记
        Assert.Contains("ImmutableSortedDictionary.Create<string, EvaluationWeights>(StringComparer.Ordinal)", src, StringComparison.Ordinal);   // 反面：表确是在这里建的空表
    }

    /// <summary>一局的过程投影（快照 + 事件），不含首行 header——header 里带配置 JSON，会把"权重填没填"本身混进比对。</summary>
    private static string Play(MatchLog log) =>
        string.Join("\n", SimFixtures.TurnTexts(log.Turns))
        + "\n----\n"
        + string.Join("\n", log.Events.Select(e => JsonSerializer.Serialize(e)));
}
