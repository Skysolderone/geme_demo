using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Core.Preview;
using Siege.Core.Scoring;
using Siege.Core.Tests.AiDecision;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.MatchSetup;

/// <summary>规格：formation-tiers match-setup —— Requirement: 计分规则版本</summary>
/// <remarks>
/// v1 = 不计阵型（与引入阵型之前逐步相同），v2 = 计阵型；新局缺省 v2，旧存档 / 旧日志缺字段按 v1 并留痕；与内容集相互独立（design.md D2）。
/// 对局内的一切计分用对局的版本：产品代码不得调用不带版本的计分入口（末尾两条守门：源码扫描覆盖含 <c>src/godot</c> 的整个 <c>src/</c>，IL 扫描覆盖解决方案内三个程序集）。
/// <para>变异验证（formation-tiers 段 A，实跑；红数以当次全量 <c>dotnet test</c> 相对基线 11 条的新增失败计，明细见任务 implement 记录）：</para>
/// <list type="bullet">
/// <item>M-S1 <c>ScoringVersions.Default</c> 改为 V1 → 红「新局缺省v2」等。</item>
/// <item>M-S2 <c>MatchFlow.RestoreCore</c> 缺字段回填改为 <c>ScoringVersions.Default</c> → 红「旧存档按v1」。</item>
/// <item>M-S3 <c>MatchFlow.Serialize</c> 不写 <c>ScoringVersion</c> → 红「新存档往返」「新局缺省v2」。</item>
/// <item>M-S4 <c>MatchFlow.PreviewCurrentBatch</c> 改调不带版本的 <c>BatchPreviewBuilder.Build</c> → 红「预演与结算同一版本」与两条守门。</item>
/// <item>M-S5 <c>BatchEvaluator</c> 的结算后势力改调不带版本的 <c>PowerCalculator.Compute</c> → 红「预演与结算同一版本」与两条守门。</item>
/// <item>M-S6 <c>MatchFlow.OnRecalculatePower</c> 传 <c>ScoringVersions.Legacy</c> 而不是对局的版本 → 红「预演与结算同一版本」「产品代码不调用不带版本的计分入口」（字面量判据）等。</item>
/// <item>M-S7 <c>ExpertLookahead.Project</c> 改调不带版本的 <c>PowerCalculator.Compute</c> → 红「预演与结算同一版本」与两条守门。</item>
/// <item>M-S8 <c>MatchFlow.Publish</c> 的公开视图恒写 V1 → 红「新局缺省v2」「预演与结算同一版本」等。</item>
/// <item>M-S9 <c>BatchPreviewBuilder.Build</c> 的开始前势力改调不带版本的 <c>Compute</c> → 红两条守门（实现方实跑）；段 A 检查补「开始前的势力…都按对局版本」后该测试也红。</item>
/// <item>M-S10 <c>MatchFlow.ForecastInitiative</c> 改调不带版本的 <c>Compute</c> → 同 M-S9（两条守门 + 检查补的行为测试）。</item>
/// <item>M-S11 <c>ExpertLookahead.PredictNextOrder</c> 改调不带版本的 <c>Compute</c> → 只红两条守门（行为上要构造"v1 / v2 名次不同且专家排在本轮末位"的局面，未补）。</item>
/// <item>M-S12 <c>MatchSession.ForMatch</c> 不核对配置与对局的版本 → 红「计分规则版本须为已定义的值」。M-S13 建局不校验版本 → 同上。</item>
/// <item>M-S14 <c>BatchEvaluator</c> 的开始前势力写死 <c>ScoringVersions.Legacy</c> → 实现方实跑只红源码扫描；段 A 检查补的行为测试也红（M-C3）。</item>
/// <item>段 A 检查（实跑，过滤 MatchSetup / 阵型的记录 / PowerScore）：M-C1 <c>BatchPreview</c> 的 before 写死 Legacy（定义文件内，源码扫描放行）→ 只红「开始前的势力…都按对局版本」；
/// M-C2 <c>ForecastInitiative</c> 写死 Legacy → 红该测试与源码扫描；M-C4 Godot 脚本加 <c>using static …PowerCalculator;</c> → 红源码扫描；
/// M-C5 Godot 脚本以全限定名调 <c>Evaluate(b, c, g, Siege.Core.Scoring.ScoringRelicCounts.None)</c> → 旧判据 <c>scoring\w*</c> 下 0 红，收紧判据后红源码扫描；
/// M-C6 峰值条目的阵型阶数恒按 V2 写 → 红「v1逐步相同」与 <c>阵型的记录Tests.旧日志照常解析</c>；M-C7 恢复时恒取 v1、忽略存档字段 → 红「新存档往返」等 7 条。</item>
/// </list>
/// </remarks>
public class 计分规则版本Tests
{
    private static readonly PlayerId[] Four = [new(0), new(1), new(2), new(3)];
    private static readonly PlayerId P0 = MatchFixtures.P0;

    /// <summary>按指定计分规则跑 <paramref name="turns"/> 个小回合的真实 AI 对局（权重、阈值、冒险概率、内容集写死为既有口径）。</summary>
    private static MatchSession Played(ScoringVersion? scoring, ulong seed, int turns, AiDifficulty difficulty = AiDifficulty.Standard)
    {
        MatchSession session = MatchSession.Create(
            SimFixtures.PinPreCalibration(SimFixtures.Config(turnLimit: 0, difficulty: difficulty)) with { ScoringVersion = scoring }, seed);
        for (int i = 0; i < turns && session.RunTurn(); i++)
        {
        }

        return session;
    }

    /// <summary>当前盘面按另一版本重算的势力文本（样本口径用：两版本在这块盘面上确实算得不同）。</summary>
    private static string PowerTextUnder(MatchFlow match, ScoringVersion scoring) =>
        AiFixtures.PowerText(PowerCalculator.Compute(
            match.Board, match.PlayerStates.ToDictionary(s => s.Player, s => s.Status), match.Relics.TrueContents(), scoring));

    [Fact]
    public void 新局缺省v2()
    {
        // 规格 Scenario：不显式配置计分规则版本开一局 → 计分规则版本为 v2，写入日志首部与存档，任何玩家都可看到。
        Assert.Equal(ScoringVersion.V2, ScoringVersions.Default);
        Assert.Equal(ScoringVersion.V2, MatchOptions.Default.ScoringVersion);
        Assert.Equal(ScoringVersion.V2, MatchOptions.Immediate.ScoringVersion);

        MatchFlow match = MatchFlow.Create(MapCatalog.Resolve(FourPlayerBaseMap.Id), new GameSeed(5), Four);
        Assert.Equal(MatchPhase.FlagPlanting, match.Phase);
        Assert.Equal(ScoringVersion.V2, match.ScoringVersion);
        Assert.False(match.ScoringVersionBackfilled);
        Assert.Equal(ScoringVersion.V2, match.Publish().ScoringVersion);   // 公开视图：插旗阶段即可读
        Assert.Equal("V2", JsonNode.Parse(match.Serialize())!["ScoringVersion"]!.GetValue<string>());

        // 跑局：配置里不写 → 新建的局取 v2，并落成具体值写进日志首部；公开视图里的势力就是按 v2 算的（样本里出现了成阵的棋串）。
        RunConfig unset = SimFixtures.PinPreCalibration(SimFixtures.Config(turnLimit: 24, difficulty: AiDifficulty.Standard)) with { ScoringVersion = null };
        MatchLog log = BatchRunner.Execute(unset, parallelism: 1)[0];
        Assert.Equal(ScoringVersion.V2, log.Header.Config.ScoringVersion);
        Assert.Equal(ScoringVersion.V2, log.ScoringVersion);
        Assert.Contains("\"ScoringVersion\":\"V2\"", log.DeterministicText().Split('\n')[0], StringComparison.Ordinal);
        Assert.Contains(log.Turns.SelectMany(t => t.PlayersState).SelectMany(p => p.Groups), g => g.FormationTier > 0);
    }

    [Fact]
    public void 旧存档按v1()
    {
        // 规格 Scenario：恢复一个在引入阵型之前保存的存档 → 恢复成功，计分规则版本为 v1，各玩家势力与保存时一致。
        // 旧存档的样本：v1 对局跑 24 个小回合后存档，再删掉 ScoringVersion 字段（引入之前的存档结构就是如此，其余字段不变）。
        // 样本口径：这块盘面按 v2 重算的势力与 v1 不同——否则"回填成 v2"也会得到同样的势力，守门是空证。
        MatchSession session = Played(ScoringVersion.V1, seed: 31, turns: 24);
        MatchFlow match = session.Match;
        string saved = AiFixtures.PowerText(match);
        Assert.NotEqual(saved, PowerTextUnder(match, ScoringVersion.V2));
        JsonObject node = JsonNode.Parse(match.Serialize())!.AsObject();
        Assert.True(node.Remove("ScoringVersion"), "新存档应写出计分规则版本字段");

        MatchFlow restored = MatchFlow.Restore(match.Board.BaseMap, node.ToJsonString());

        Assert.Equal(ScoringVersion.V1, restored.ScoringVersion);
        Assert.True(restored.ScoringVersionBackfilled);
        Assert.Equal(ScoringVersion.V1, restored.Publish().ScoringVersion);
        Assert.Equal(saved, AiFixtures.PowerText(restored));
        Assert.All(restored.Scoreboard.Latest!.Players.SelectMany(p => p.Groups), g => Assert.Equal(0, g.FormationTier));

        // 带字段的 v1 存档：照字段恢复，不留回填痕迹。
        MatchFlow explicitV1 = MatchFlow.Restore(match.Board.BaseMap, match.Serialize());
        Assert.Equal((ScoringVersion.V1, false), (explicitV1.ScoringVersion, explicitV1.ScoringVersionBackfilled));
        Assert.Equal(saved, AiFixtures.PowerText(explicitV1));
    }

    [Fact]
    public void v1逐步相同()
    {
        // 规格 Scenario：以计分规则 v1 用同一种子与同一配置重跑一局此前记录过的 AI 对局 → 每一步的批次与结算结果与引入本项之前逐项相同。
        // "此前记录过"= 候选格上限Tests.V4GoldenTurnHash：种子 31、Standard、24 个小回合，早在引入阵型之前钉下
        // （快照含每步落子、提子、手牌类型、征募面板规模与逐棋串势力明细），黄金值一字未改。
        // 反面：同一配置按 v2 跑出的局不同——否则版本没有抵达对局，守门是空证。
        RunConfig config = SimFixtures.PinPreCalibration(SimFixtures.Config(seedStart: 31, turnLimit: 24, difficulty: AiDifficulty.Standard));

        MatchLog v1 = BatchRunner.Execute(config with { ScoringVersion = ScoringVersion.V1 }, parallelism: 1)[0];
        MatchLog v2 = BatchRunner.Execute(config with { ScoringVersion = ScoringVersion.V2 }, parallelism: 1)[0];

        Assert.Equal(ScoringVersion.V1, v1.Header.Config.ScoringVersion);
        Assert.True(v1.Turns.Count >= 24, $"小回合 {v1.Turns.Count}");
        Assert.Equal(候选格上限Tests.V4GoldenTurnHash, 候选格上限Tests.TurnHash(v1));
        Assert.NotEqual(候选格上限Tests.TurnHash(v1), 候选格上限Tests.TurnHash(v2));

        // v1 局的棋串条目不写阵型阶数（JSON 里整项省略），小回合文本因此与引入之前逐字节相同。
        Assert.All(v1.Turns.SelectMany(t => t.PlayersState).SelectMany(p => p.Groups), g => Assert.Null(g.FormationTier));
        Assert.DoesNotContain("FormationTier", v1.FullText(), StringComparison.Ordinal);
    }

    [Fact]
    public void 新存档往返()
    {
        // 规格 Scenario：保存一局计分规则为 v2 的对局并恢复 → 恢复后的计分规则版本仍为 v2，各玩家势力与保存时一致。
        // v2 不是回填值（缺字段回填 v1），所以本用例同时证伪"写入路径漏写"（testing.md「带回填兜底的字段，写入路径要用非回填值证伪」）。
        // 样本口径：存档时盘面上有成阵的棋串，且按 v1 重算的势力不同。再各自续跑 8 个小回合：恢复局与原局的势力明细逐步相同。
        MatchSession session = Played(ScoringVersion.V2, seed: 31, turns: 24);
        MatchFlow match = session.Match;
        string saved = AiFixtures.PowerText(match);
        Assert.Contains(match.Scoreboard.Latest!.Players.SelectMany(p => p.Groups), g => g.FormationTier > 0);
        Assert.NotEqual(saved, PowerTextUnder(match, ScoringVersion.V1));
        string json = match.Serialize();
        Assert.Equal("V2", JsonNode.Parse(json)!["ScoringVersion"]!.GetValue<string>());

        MatchFlow restored = MatchFlow.Restore(match.Board.BaseMap, json);

        Assert.Equal(ScoringVersion.V2, restored.ScoringVersion);
        Assert.False(restored.ScoringVersionBackfilled);
        Assert.Equal(saved, AiFixtures.PowerText(restored));
        Assert.Equal(json, restored.Serialize());

        MatchSession resumed = MatchSession.ForMatch(restored, session.Config);
        for (int i = 0; i < 8; i++)
        {
            Assert.True(session.RunTurn());
            Assert.True(resumed.RunTurn());
            Assert.Equal(AiFixtures.PowerText(match), AiFixtures.PowerText(restored));
        }

        Assert.Equal(match.Serialize(), restored.Serialize());
    }

    [Fact]
    public void 预演与结算同一版本()
    {
        // 规格 Scenario：计分规则 v2 的对局里，玩家暂放一枚棋子使某棋串由 2 枚变为 3 枚 → 预演给出的该棋串军势已按一阶阵型计算（⌊3 × 1.5⌋ = 4），
        // 与确认后的结算结果相同。同一批次上 AI 评价的势力增量、专家前瞻的投影视图也按对局的版本（design D2 / D4）。
        // 盘面：9×9、第 5 大回合，P0 的 C5–D5 两枚，暂放 E5 连成 3 枚。v1 对照：同一手在 v1 局里军势是 3。
        (BigInteger previewGroup, int previewTier, BigInteger previewTotal, BigInteger gain, BigInteger projectedGroup, GroupPower settled, BigInteger settledTotal) =
            StageThird(MatchFixtures.V2);

        Assert.Equal((4, 1), ((int)previewGroup, previewTier));
        Assert.Equal((4, 1), ((int)settled.Power, settled.FormationTier));
        Assert.Equal(previewTotal, settledTotal);
        Assert.Equal(4, projectedGroup);

        (BigInteger v1Group, int v1Tier, BigInteger v1Total, BigInteger v1Gain, BigInteger v1Projected, GroupPower v1Settled, BigInteger v1SettledTotal) =
            StageThird(MatchFixtures.V1);
        Assert.Equal((3, 0, 3, 0), ((int)v1Group, v1Tier, (int)v1Settled.Power, v1Settled.FormationTier));
        Assert.Equal(v1Total, v1SettledTotal);
        Assert.Equal(3, v1Projected);

        // AI 评价的势力维 = 结算后 − 开始前：两版本相差的恰是阵型多出的 1 点（2 → 4 对 2 → 3，领地变化相同）。
        Assert.Equal(gain, v1Gain + 1);
        Assert.Equal(settledTotal, v1SettledTotal + 1);
    }

    /// <summary>
    /// 在给定配置的对局里走到 P0 的部署阶段、暂放 E5（C5–D5 已有两枚）：返回预演里含落点的己方棋串的军势与阵型阶数、预演的结算后总势力、
    /// AI 评价的势力增量原始值、专家前瞻投影视图里该棋串的军势，以及确认后势力榜上的该棋串与总势力。
    /// </summary>
    private static (BigInteger PreviewGroup, int PreviewTier, BigInteger PreviewTotal, BigInteger Gain, BigInteger ProjectedGroup, GroupPower Settled, BigInteger SettledTotal)
        StageThird(MatchOptions options)
    {
        MatchFlow match = MatchFixtures.Started(options: options).AtRound(5).Stones(P0, "C5", "D5");
        Assert.Equal(options.ScoringVersion, match.Publish().ScoringVersion);
        Assert.Equal(2, Assert.Single(match.Scoreboard.Latest!.Of(P0).Groups).Power);
        MatchPublicView before = match.Publish();

        match.BeginTurn();
        match.EnterRecruit();
        StagedBatch batch = match.EnterDeploy();
        Assert.Null(batch.Stage(Coord.Parse("E5"), PieceType.Basic));

        Core.Preview.BatchPreview preview = match.PreviewCurrentBatch();
        Assert.True(preview.IsLegal);
        GroupPower previewGroup = Assert.Single(preview.OwnGroups, g => g.ContainsPlacement).Power!;
        BigInteger previewTotal = preview.PowerChanges.Single(c => c.Player == P0).After;

        RehearsalResult rehearsal = match.Rehearse();
        var evaluator = new BatchEvaluator(P0, before, EvaluationWeights.Default, immediateOnly: true);
        BigInteger gain = evaluator.Evaluate([.. batch.Placements], rehearsal, batch.Context).RawOf(EvaluationDimension.PowerGain);
        MatchPublicView projected = ExpertLookahead.Project(before, rehearsal.ProjectedBoard!);
        Assert.Equal(options.ScoringVersion, projected.ScoringVersion);
        BigInteger projectedGroup = Assert.Single(projected.Power!.Of(P0).Groups).Power;

        Assert.True(match.Confirm().Confirmed);
        GroupPower settled = Assert.Single(match.Scoreboard.Latest!.Of(P0).Groups);
        Assert.Equal(["C5", "D5", "E5"], settled.Stones.Notations());
        return (previewGroup.Power, previewGroup.FormationTier, previewTotal, gain, projectedGroup, settled, match.Scoreboard.Latest!.Of(P0).Total);
    }

    [Theory]
    [InlineData(ScoringVersion.V1)]
    [InlineData(ScoringVersion.V2)]
    public void 开始前的势力在预演顺序预测与AI评价里都按对局版本(ScoringVersion scoring)
    {
        // 补充守门（formation-tiers 段 A 检查）：「预演与结算同一版本」的盘面在批次开始前只有 2 枚（0 阶），v1 / v2 在"开始前"算得一样，
        // 于是预演的开始前势力（BatchPreviewBuilder 的 before）、顺序预测（MatchFlow.ForecastInitiative）、AI 评价的开始前势力（BatchEvaluator._before）
        // 即使悄悄按 v1 算也看不出来——原先只有源码扫描能抓。这里让开始前就有一条 3 枚的棋串（v2 一阶 4、v1 3），三处都要与势力榜（按对局版本）一致。
        // 变异验证（段 A 检查，实跑）：M-C1 BatchPreview 的 before 改传 ScoringVersions.Legacy（定义文件内写死版本，源码扫描放行）→ 只有本测试红；
        //   M-C2 ForecastInitiative 改传 ScoringVersions.Legacy → 本测试 + 源码扫描红；M-C3 BatchEvaluator 的 _before 改传 ScoringVersions.Legacy（即 M-S14）→ 本测试 + 源码扫描红；
        //   M-S9 / M-S10（改调不带版本的重载）→ 本测试 + 两条守门红。
        MatchOptions options = scoring == ScoringVersion.V1 ? MatchFixtures.V1 : MatchFixtures.V2;
        MatchFlow match = MatchFixtures.Started(options: options).AtRound(5).Stones(P0, "C5", "D5", "E5");
        MatchPublicView before = match.Publish();
        GroupPower three = Assert.Single(before.Power!.Of(P0).Groups);
        Assert.Equal(scoring == ScoringVersion.V2 ? (4, 1) : (3, 0), ((int)three.Power, three.FormationTier));   // 样本口径：两版本在开始前就不同
        BigInteger beforeTotal = before.Power.Of(P0).Total;

        Assert.Equal(beforeTotal, match.PublishSupplement().NextOrderForecast!.Of(P0).Power);

        match.BeginTurn();
        match.EnterRecruit();
        StagedBatch batch = match.EnterDeploy();
        Assert.Null(batch.Stage(Coord.Parse("F5"), PieceType.Basic));
        Core.Preview.BatchPreview preview = match.PreviewCurrentBatch();
        Assert.True(preview.IsLegal);
        PowerChange change = preview.PowerChanges.Single(c => c.Player == P0);
        Assert.Equal(beforeTotal, change.Before);

        RehearsalResult rehearsal = match.Rehearse();
        var evaluator = new BatchEvaluator(P0, before, EvaluationWeights.Default, immediateOnly: true);
        BigInteger gain = evaluator.Evaluate([.. batch.Placements], rehearsal, batch.Context).RawOf(EvaluationDimension.PowerGain);

        Assert.True(match.Confirm().Confirmed);
        BigInteger settled = match.Scoreboard.Latest!.Of(P0).Total;
        Assert.Equal(settled, change.After);
        Assert.Equal(settled - beforeTotal, gain);
    }

    [Fact]
    public void 与内容集相互独立()
    {
        // 规格 Scenario：以内容集 v1、计分规则 v2 开一局 → 对局正常进行，棋池为原六种棋子，军势按阵型计算。
        RunConfig config = SimFixtures.PinPreCalibration(SimFixtures.Config(turnLimit: 0, difficulty: AiDifficulty.Standard)) with
        {
            ContentSet = ContentSet.V1,
            ScoringVersion = ScoringVersion.V2,
        };
        MatchSession session = MatchSession.Create(config, 31);
        for (int i = 0; i < 24; i++)
        {
            Assert.True(session.RunTurn());
        }

        MatchFlow match = session.Match;
        Assert.Equal((ContentSet.V1, ScoringVersion.V2), (match.ContentSet, match.ScoringVersion));
        PieceType[] candidates = [.. match.Hands.Records.SelectMany(r => r.Candidates)];
        Assert.True(candidates.Length >= 60, $"样本口径：只抽了 {candidates.Length} 个候选位");
        Assert.All(candidates, t => Assert.Contains(t, ContentSets.PieceTypesOf(ContentSet.V1)));

        // 军势按阵型计算：每条棋串的阶数等于按棋子数查门槛（测试内独立的阶梯），军势等于独立整数式；样本里确有成阵的棋串。
        GroupPower[] groups = [.. match.Scoreboard.Latest!.Players.SelectMany(p => p.Groups)];
        Assert.Contains(groups, g => g.FormationTier > 0);
        Assert.All(groups, g =>
        {
            int n = g.Stones.Length;
            int tier = n >= 12 ? 4 : n >= 8 ? 3 : n >= 5 ? 2 : n >= 3 ? 1 : 0;
            int exponent = g.MultiplierCount + tier;
            Assert.Equal(tier, g.FormationTier);
            Assert.Equal((BigInteger)(g.BaseTotal + g.PositionBonus) * BigInteger.Pow(3, exponent) / BigInteger.Pow(2, exponent), g.Power);
        });

        // 四种组合都合法：都能建局、跑几个小回合，会话配置与对局配置一致。
        foreach (ContentSet set in Enum.GetValues<ContentSet>())
        {
            foreach (ScoringVersion scoring in Enum.GetValues<ScoringVersion>())
            {
                MatchSession combo = MatchSession.Create(config with { ContentSet = set, ScoringVersion = scoring }, 7);
                for (int i = 0; i < 4; i++)
                {
                    Assert.True(combo.RunTurn());
                }

                Assert.Equal((set, scoring), (combo.Match.ContentSet, combo.Match.ScoringVersion));
                Assert.Equal((set, scoring), (combo.Config.ContentSet, combo.Config.ScoringVersion));
            }
        }
    }

    [Fact]
    public void 计分规则版本须为已定义的值()
    {
        // 0 不是合法版本（显式编号 1 / 2）：对局配置、跑局配置、计分入口与存档里的非法值都要响亮失败，不静默当成某个版本。
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MatchFlow.Create(MapCatalog.Resolve(FourPlayerBaseMap.Id), new GameSeed(1), Four, MatchOptions.Immediate with { ScoringVersion = 0 }));
        Assert.Throws<ArgumentException>(() => (SimFixtures.Config() with { ScoringVersion = (ScoringVersion)3 }).Validated());
        Assert.Throws<ArgumentOutOfRangeException>(() => TestMaps.Blank().Place("C5", P0).Score((ScoringVersion)3));
        Assert.Throws<ArgumentOutOfRangeException>(() => FormationTiers.TierOf(0, 5));

        MatchSession session = Played(ScoringVersion.V1, seed: 11, turns: 4, AiDifficulty.Easy);
        JsonObject node = JsonNode.Parse(session.Match.Serialize())!.AsObject();
        node["ScoringVersion"] = "V9";
        Assert.ThrowsAny<Exception>(() => MatchFlow.Restore(session.Match.Board.BaseMap, node.ToJsonString()));

        // 会话核对跑局配置与对局配置的版本一致（与内容集同一做法）。
        MatchFlow v2 = MatchFlow.Create(MapCatalog.Resolve(FourPlayerBaseMap.Id), new GameSeed(1), Four, MatchOptions.Immediate with { FlagRisk = 0 });
        v2.PlantPrototype();
        Assert.Throws<SiegeRuleException>(() => MatchSession.ForMatch(v2, SimFixtures.Config() with { ScoringVersion = ScoringVersion.V1 }));
    }

    // ---------- 守门：产品代码不调用不带版本的计分入口（design D2） ----------

    /// <summary>计分入口的调用形状：<c>PowerCalculator.Compute / Evaluate / GroupPowerOf(</c>、势力榜的 <c>.Recalculate(</c>、<c>BatchPreviewBuilder.Build(</c>。</summary>
    private static readonly Regex EntryCall = new(@"PowerCalculator\s*\.\s*(Compute|Evaluate|GroupPowerOf)\s*\(|\.\s*Recalculate\s*\(|BatchPreviewBuilder\s*\.\s*Build\s*\(");

    /// <summary>
    /// 实参里带版本的判据：出现含 scoring 的标识符（<c>ScoringVersion</c>、<c>_scoring</c>、<c>view.ScoringVersion</c>…）；公式的四参形态带 <c>formationTier</c>。
    /// 只认版本本身的标识符：<c>…ScoringVersion…</c>、独立的 <c>scoring</c> / <c>_scoring</c>、<c>formationTier</c>。命名空间段 <c>Siege.Core.Scoring.</c> 与
    /// <c>ScoringRelicCounts</c> / <c>ScoringRelicsOf</c>（计分信物）不算——否则 <c>Evaluate(b, c, g, Siege.Core.Scoring.ScoringRelicCounts.None)</c>
    /// 这个不带版本的重载会被当成带版本放过（段 A 检查变异 M-C5 实证：旧判据 <c>scoring\w*</c> 下 0 红）。
    /// </summary>
    private static readonly Regex VersionArgument = new(@"(?i)scoringversion\w*|(?<![\w.])_?scoring(?![\w.])|formationTier\w*");

    /// <summary>经别名或 <c>using static</c> 引入计分入口所在类型：调用处就不再出现类型名，<see cref="EntryCall"/> 扫不到（段 A 检查补；<c>src/godot</c> 只有源码扫描兜底）。</summary>
    private static readonly Regex AliasedEntryOwner = new(@"using\s+(static\s+[\w.]*|\w+\s*=\s*[\w.]*)(PowerCalculator|PowerScoreboard|BatchPreviewBuilder)");

    /// <summary>实参里写死版本的形状：<c>ScoringVersions.Legacy / Default</c> 或 <c>ScoringVersion.V1 / V2</c>。只允许出现在三个定义文件里的 v1 转发处。</summary>
    private static readonly Regex HardCodedVersion = new(@"ScoringVersions\s*\.\s*(Legacy|Default)|ScoringVersion\s*\.\s*V\d");

    /// <summary>不带版本的入口所在的三个定义文件：这里的 v1 转发是设计的一部分（入口说明里写明语义固定为 v1）。</summary>
    private static readonly string[] DefinitionFiles = ["PowerCalculator.cs", "PowerScoreboard.cs", "BatchPreview.cs"];

    /// <summary>逐个取出 <paramref name="code"/> 里计分入口调用的实参文本（配平括号）。</summary>
    private static List<string> EntryCallArguments(string code)
    {
        var calls = new List<string>();
        foreach (System.Text.RegularExpressions.Match m in EntryCall.Matches(code))
        {
            int depth = 1;
            int i = m.Index + m.Length;
            int start = i;
            for (; i < code.Length && depth > 0; i++)
            {
                depth += code[i] switch { '(' => 1, ')' => -1, _ => 0 };
            }

            Assert.True(depth == 0, $"括号不配平：{code[m.Index..Math.Min(code.Length, m.Index + 80)]}");
            calls.Add(code[start..(i - 1)]);
        }

        return calls;
    }

    /// <summary>一段代码里的违例：不带版本的调用，以及（<paramref name="allowHardCoded"/> 为假时）写死版本的调用。</summary>
    private static List<string> Violations(string code, bool allowHardCoded) =>
        [.. EntryCallArguments(code).Where(args => !VersionArgument.IsMatch(args) || (!allowHardCoded && HardCodedVersion.IsMatch(args)))];

    [Fact]
    public void 产品代码不调用不带版本的计分入口()
    {
        // design D2：计算入口保留不带版本的形态时其语义固定为 v1，产品代码不得调用——否则那一处会悄悄按 v1 计分（预演与结算对不上、AI 看不见阵型）。
        // 源码扫描整个 src/（含不在解决方案里的 src/godot，testing.md「不在解决方案里的工程对守门测试完全隐身」）：每一处计分入口调用的实参里
        // 都要带版本（标识符含 scoring）；三个定义文件之外不得把版本写死成 Legacy / Default / V1 / V2——对局内的计分一律传对局的版本。
        string root = Path.Combine(PresentationFixtures.RepoRoot(), "src");
        string[] files = [.. Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" or ".godot"))];
        Assert.True(files.Length >= 170, $"样本口径：src 下只扫到 {files.Length} 个源文件");
        Assert.True(files.Count(f => f.Contains(Path.Combine("src", "godot", "scripts"), StringComparison.Ordinal)) >= 10, "样本口径：没扫到引擎层脚本");

        int calls = 0;
        var violations = new List<string>();
        foreach (string file in files)
        {
            string code = Regex.Replace(File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal), @"//[^\n]*", string.Empty);
            calls += EntryCallArguments(code).Count;
            bool definition = DefinitionFiles.Contains(Path.GetFileName(file));
            violations.AddRange(Violations(code, allowHardCoded: definition).Select(args => $"{Path.GetRelativePath(root, file)}: ({Regex.Replace(args, @"\s+", " ")})"));
            violations.AddRange(AliasedEntryOwner.Matches(code).Select(m => $"{Path.GetRelativePath(root, file)}: 别名引入 {m.Value}"));
        }

        // 样本口径下界：扫描确实命中了产品代码里的调用（改动时实测 12 处：MatchFlow 3 + MatchFlow.Preview 2 + BatchPreview 2 + BatchEvaluator 2 + ExpertLookahead 2 + PowerScoreboard 1）。
        Assert.True(calls >= 12, $"只命中 {calls} 处计分入口调用，扫描口径可疑");
        Assert.Empty(violations);

        // 反面断言：判据在测试内字面量上确实命中——不带版本的三种入口各一、写死版本的一种；带版本的不误伤。
        Assert.Single(Violations("x = PowerCalculator.Compute(Board, roster, Relics.TrueContents());", allowHardCoded: false));
        Assert.Single(Violations("Mark(Scoreboard.Recalculate(context.Board, roster, MajorRound, Relics.TrueContents()));", allowHardCoded: false));
        Assert.Single(Violations("return BatchPreviewBuilder.Build(Board, _batch!.Context, _batch.Placements, History, Roster, Relics, MajorRound);", allowHardCoded: false));
        Assert.Single(Violations("g = PowerCalculator.Evaluate(board, coverage, group, ScoringRelicCounts.None);", allowHardCoded: false));
        Assert.Single(Violations("g = Siege.Core.Scoring.PowerCalculator.Evaluate(b, c, g, Siege.Core.Scoring.ScoringRelicCounts.None);", allowHardCoded: false));
        Assert.Empty(Violations("x = PowerCalculator.Compute(after, _roster, _knownRelics, _scoring);", allowHardCoded: false));
        Assert.Empty(Violations("x = Siege.Core.Scoring.PowerCalculator.Compute(b, r, k, scoring);", allowHardCoded: false));
        Assert.Single(Violations("x = PowerCalculator.Compute(after, _roster, _knownRelics, ScoringVersions.Legacy);", allowHardCoded: false));
        Assert.Single(AliasedEntryOwner.Matches("using static Siege.Core.Scoring.PowerCalculator;"));
        Assert.Single(AliasedEntryOwner.Matches("using PC = Siege.Core.Scoring.PowerCalculator;"));
        Assert.Empty(AliasedEntryOwner.Matches("using Siege.Core.Scoring;"));
        Assert.Empty(Violations("x = PowerCalculator.Compute(after, _roster, _knownRelics, ScoringVersions.Legacy);", allowHardCoded: true));
        Assert.Empty(Violations("x = PowerCalculator.Compute(copy, roster, RevealedRelics.Of(view.Relics), view.ScoringVersion);", allowHardCoded: false));
    }

    [Fact]
    public void 不带版本的计分入口在程序集内没有外部调用方()
    {
        // 同一条约束的 IL 口径（解决方案内的 Core / Sim / Presentation 三个程序集）：按重载身份判定，不依赖实参怎么写。
        // "不带版本的入口" = PowerCalculator / PowerScoreboard / BatchPreviewBuilder 上名为 Compute / Evaluate / GroupPowerOf / Recalculate / Build、
        // 参数里既没有 ScoringVersion 也没有 formationTier 的公开方法；它们只允许被自己所在类型里的转发调用。
        Type[] owners = [typeof(PowerCalculator), typeof(PowerScoreboard), typeof(BatchPreviewBuilder)];
        string[] names = ["Compute", "Evaluate", "GroupPowerOf", "Recalculate", "Build"];
        static bool Versioned(MethodBase m) => m.GetParameters().Any(p => p.ParameterType == typeof(ScoringVersion) || p.Name == "formationTier");
        MethodInfo[] entries = [.. owners.SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => names.Contains(m.Name))];
        MethodInfo[] unversioned = [.. entries.Where(m => !Versioned(m))];
        // 口径：不带版本的入口共 8 个（Compute ×3、Evaluate ×2、GroupPowerOf ×1、Recalculate ×1、Build ×1），带版本的 5 个。
        Assert.Equal(8, unversioned.Length);
        Assert.Equal(5, entries.Length - unversioned.Length);

        Assembly[] assemblies = [typeof(PowerCalculator).Assembly, typeof(MatchSession).Assembly, PresentationFixtures.PresentationAssembly];
        var outside = new List<string>();
        int versionedCalls = 0;
        foreach (Assembly assembly in assemblies)
        {
            foreach ((MethodBase caller, MemberInfo target, _) in PresentationFixtures.IlReferences(assembly))
            {
                if (target is not MethodInfo method || !entries.Contains(method))
                {
                    continue;
                }

                if (Versioned(method))
                {
                    versionedCalls++;
                }
                else if (!DeclaredWithin(caller, method.DeclaringType!))
                {
                    outside.Add($"{caller.DeclaringType?.FullName}.{caller.Name} → {method.DeclaringType!.Name}.{method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})");
                }
            }
        }

        Assert.True(versionedCalls >= 12, $"样本口径：只扫到 {versionedCalls} 处带版本的调用");
        Assert.Empty(outside);

        static bool DeclaredWithin(MethodBase caller, Type owner)
        {
            for (Type? t = caller.DeclaringType; t is not null; t = t.DeclaringType)
            {
                if (t == owner)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
