using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Sim.Config;
using Siege.Sim.Running;

namespace Siege.Core.Tests.AiDecision;

/// <summary>规格：ai-decision —— Requirement: 地图专属评价权重覆盖（v2-recalibration 段 B，design D4 / D5 / D7 / D10）</summary>
/// <remarks>
/// 覆盖表的取值由扫档决定（段 B 2.9），本类的机制断言一律写成"等于该地图登记的覆盖表""等于默认表"；
/// 需要"覆盖与默认表不同"才能分辨的断言（显式权重整表优先、覆盖作用于简单难度）经解析函数的纯函数重载注入 <see cref="Probe"/>，不引入可变静态。
/// retire-legacy-maps 段 B（design D2 / 已知歧义 4）：唯一一项 siege-2p-base-v1 随该图删除，缺省登记表为空、机制保留；
/// 原先读缺省表里 2 人图条目的断言一律改为注入 <see cref="Probe"/>（登记 2 人内置棋盘图），缺省表一侧改钉"为空、取默认表"。
/// </remarks>
public class 地图专属评价权重覆盖Tests
{
    /// <summary>注入登记表里的地图：2 人内置棋盘图（retire-legacy-maps 段 B 起；此前为已删除的 siege-2p-base-v1）。</summary>
    internal const string ProbeMapId = "siege-2p-board-v1";

    /// <summary>测试接缝注入的登记表：2 人内置棋盘图一项，眼位与敌方损失两维与默认表不同（取值只为可分辨，与扫档无关）。</summary>
    internal static readonly IReadOnlyDictionary<string, EvaluationWeights> Probe =
        ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, [KeyValuePair.Create(ProbeMapId, EvaluationWeights.Default with { Eye = 7, EnemyLoss = 3 })]);

    private static readonly IReadOnlyDictionary<string, EvaluationWeights> Empty = ImmutableSortedDictionary<string, EvaluationWeights>.Empty;

    [Fact]
    public void 登记了覆盖的地图未显式配置权重时取覆盖()
    {
        // 规格 Scenario（原「2 人图未显式配置权重时取覆盖」）：未显式配置权重 → 该地图登记的覆盖表，而不是默认表。
        // retire-legacy-maps 段 B：缺省登记表为空，登记由注入表 Probe 给出（2 人内置棋盘图）。
        string id = MapCatalog.Resolve(ProbeMapId).Id;
        Assert.Equal(ProbeMapId, id);

        // 登记表（接缝）：取到的恰是登记项本身，不是等值的默认表。
        EvaluationWeights probe = EvaluationWeights.ForMapId(id, null, Probe);
        Assert.Same(Probe[id], probe);
        Assert.NotEqual(EvaluationWeights.Default, probe);
        // 空登记表下同一地图取默认表：登记与否是唯一的分界。缺省登记表当前为空，同样取默认表。
        Assert.Same(EvaluationWeights.Default, EvaluationWeights.ForMapId(id, null, Empty));
        Assert.Same(EvaluationWeights.Default, EvaluationWeights.ForMapId(id));
    }

    [Fact]
    public void 未登记的地图取默认表()
    {
        // 规格 Scenario：未登记的地图上未显式配置权重 → 默认表；登记表里有别的地图也不影响它们。
        // retire-legacy-maps 段 B：样本由 v5 / 3 人图 / gen:12345 / 边疆图改为 4 人与 3 人内置棋盘图、一张 board: 生成图。
        string[] ids = ["siege-4p-board-v1", "siege-3p-board-v1", MapCatalog.Resolve("board:12345").Id];
        Assert.Equal("board:12345", ids[2]);
        foreach (string id in ids)
        {
            Assert.False(EvaluationWeights.MapOverrides.ContainsKey(id), $"{id} 不应登记覆盖。");
            Assert.Same(EvaluationWeights.Default, EvaluationWeights.ForMapId(id));
            Assert.Same(EvaluationWeights.Default, EvaluationWeights.ForMapId(id, null, Probe));
            Assert.Null(EvaluationWeights.MapOverrideCalibrationOf(id));
        }
    }

    [Fact]
    public void 显式权重整表优先()
    {
        // 规格 Scenario：2 人图上显式写了 Safety = 7、其余八维等于默认表的权重 → 恰为这张表；眼位与敌方损失取默认表的值，不取覆盖表的值（整表，不逐维合并）。
        EvaluationWeights explicitTable = EvaluationWeights.Default with { Safety = 7 };
        EvaluationWeights resolved = EvaluationWeights.ForMapId(ProbeMapId, explicitTable, Probe);
        Assert.Same(explicitTable, resolved);
        Assert.Equal(EvaluationWeights.Default.Eye, resolved.Eye);
        Assert.Equal(EvaluationWeights.Default.EnemyLoss, resolved.EnemyLoss);
        Assert.NotEqual(Probe[ProbeMapId].Eye, resolved.Eye);
        Assert.Same(explicitTable, EvaluationWeights.ForMapId(ProbeMapId, explicitTable));

        // 批量入口的落成（RunConfig.ResolvedFor）经同一实现：显式的一名原样，未显式的一名取覆盖。
        MapData map = MapCatalog.Resolve(ProbeMapId);
        RunConfig config = new()
        {
            MapId = map.Id,
            Players = [new PlayerAiConfig { Weights = explicitTable }, new PlayerAiConfig()],
        };
        RunConfig resolvedConfig = config.ResolvedFor(map, Probe);
        Assert.Equal(explicitTable, resolvedConfig.Players[0].Weights);
        Assert.Equal(Probe[map.Id], resolvedConfig.Players[1].Weights);
        Assert.Equal(resolvedConfig.ToJson(), resolvedConfig.ResolvedFor(map, Probe).ToJson());   // 幂等：BatchRunner 与 MatchSession.Create 各调一次
    }

    [Fact]
    public void 覆盖作用于简单难度()
    {
        // 规格 Scenario：2 人图上未显式配置权重的简单难度 AI → 权重等于覆盖表；眼位与敌方损失两维按覆盖表的值计入打分（design D7：解析不分难度）。
        MapData map = MapCatalog.Resolve(ProbeMapId);
        EvaluationWeights expected = Probe[map.Id];
        RunConfig config = new()
        {
            MapId = map.Id,
            Players = [.. Enumerable.Range(0, 2).Select(_ => new PlayerAiConfig { Difficulty = AiDifficulty.Easy })],
            TurnLimit = 2,
        };
        RunConfig resolved = config.ResolvedFor(map, Probe);
        Assert.All(resolved.Players, p => Assert.Equal(expected, p.Weights));
        MatchSession session = MatchSession.Create(resolved, 1, map);
        foreach (PlayerId p in session.Match.Players)
        {
            HeuristicTurnController ai = session.AiOf(p)!;
            Assert.True(ai.Config.ImmediateOnly);
            Assert.Equal(expected, ai.Weights);
        }

        // 打分：简单难度的评价按覆盖表的眼位 / 敌方损失加权（贡献 = 原始值 × 覆盖表的权重，不是默认表的）。
        MatchFlow capture = 启发式评价维度Tests.CaptureRelicPosition();
        HeuristicTurnController easy = HeuristicAi.Create(capture, AiFixtures.P0, AiDifficulty.Easy, EvaluationWeights.ForMapId(map.Id, null, Probe));
        StagedBatch batch = capture.OpenDeploy();
        RehearsalResult result = capture.RehearseBatch(batch, ("F5", PieceType.Basic));
        EvaluationBreakdown e = easy.CreateEvaluator().Evaluate(batch.Placements, result, batch.Context);
        Assert.True(e.RawOf(EvaluationDimension.EnemyLoss) > 0);
        Assert.Equal(e.RawOf(EvaluationDimension.EnemyLoss) * expected.EnemyLoss, e.ContributionOf(EvaluationDimension.EnemyLoss));
        Assert.NotEqual(e.RawOf(EvaluationDimension.EnemyLoss) * EvaluationWeights.Default.EnemyLoss, e.ContributionOf(EvaluationDimension.EnemyLoss));

        // 眼位：盘面同 难度分级Tests.简单难度也会做活（原始值 4）。
        MatchFlow eye = AiFixtures.Round5().Stones(AiFixtures.P0, "B1", "A2", "B2", "C2", "D2");
        eye.Debug.SeedHand(AiFixtures.P0, (PieceType.Basic, 10));
        HeuristicTurnController easyEye = HeuristicAi.Create(eye, AiFixtures.P0, AiDifficulty.Easy, EvaluationWeights.ForMapId(map.Id, null, Probe));
        StagedBatch eyeBatch = eye.OpenDeploy();
        RehearsalResult eyeResult = eye.RehearseBatch(eyeBatch, ("D1", PieceType.Basic));
        EvaluationBreakdown eyeEval = easyEye.CreateEvaluator().Evaluate(eyeBatch.Placements, eyeResult, eyeBatch.Context);
        Assert.Equal(4, eyeEval.RawOf(EvaluationDimension.Eye));
        Assert.Equal(4 * expected.Eye, eyeEval.ContributionOf(EvaluationDimension.Eye));
    }

    [Fact]
    public void 缺省登记表为空()
    {
        // 规格 Scenario「缺省登记表为空」（段 D 由「覆盖只作用于登记的地图」改名）：覆盖表的全部键恰为登记的地图。原钉"恰为 siege-2p-base-v1 一项"；
        // retire-legacy-maps 段 B 删除该条目（机制保留，design D2 / 已知歧义 4）→ 缺省登记表为空，内置棋盘图不登记（留给 AI 校准 change）。
        Assert.Empty(EvaluationWeights.MapOverrides);
        Assert.All(MapCatalog.BuiltinIds, id => Assert.False(EvaluationWeights.MapOverrides.ContainsKey(id)));
    }

    [Fact]
    public void 覆盖表被改动()
    {
        // 规格 Scenario：改覆盖表的任一维取值或增删登记的地图而不同时更新校准口径 → 守门失败。
        // 登记的每一项都必须有指向扫档证据的口径；retire-legacy-maps 段 B 起表为空，未登记的地图（含已删除的旧标识）一律没有口径。
        foreach ((string id, EvaluationWeights table) in EvaluationWeights.MapOverrides)
        {
            string status = EvaluationWeights.MapOverrideCalibrationOf(id) ?? throw new Xunit.Sdk.XunitException($"{id} 登记了覆盖却没有校准口径。");
            Assert.Contains(id, status, StringComparison.Ordinal);
            Assert.NotEqual(EvaluationWeights.Default, table);
        }

        foreach (string id in MapCatalog.BuiltinIds.Concat(MapCatalog.RetiredIds))
        {
            Assert.Equal(EvaluationWeights.MapOverrides.ContainsKey(id), EvaluationWeights.MapOverrideCalibrationOf(id) is not null);
        }
    }

    [Fact]
    public void 覆盖不改人类玩家的合法操作()
    {
        // 规格 Scenario：同一局面上，"登记表为空"与"登记表含 2 人图覆盖"两种情形开局，人类玩家的合法落子范围相同。
        // 覆盖只进 AI 的评价：对局层（MatchFlow）根本不接收它——行为比对之外，另由源码扫描钉住"覆盖符号不出现在 AI 与入口以外"（见下一条）。
        MapData map = MapCatalog.Resolve(ProbeMapId);
        PlayerId human = new(0), ai = new(1);
        string Range(IReadOnlyDictionary<string, EvaluationWeights> table)
        {
            MatchFlow match = MatchFlow.Create(map, new GameSeed(7), [human, ai], MatchOptions.Immediate);
            match.PlantPrototype((human, 0));
            var runner = new MatchRunner(match);
            runner.SetController(ai, HeuristicAi.Create(match, ai, AiDifficulty.Standard, EvaluationWeights.ForMapId(map.Id, null, table)));
            return string.Join(",", match.LegalRangeFor(human).Select(c => c.ToNotation()).Order(StringComparer.Ordinal));
        }

        string withEmpty = Range(Empty);
        Assert.NotEmpty(withEmpty);
        Assert.Equal(withEmpty, Range(Probe));
        Assert.Equal(withEmpty, Range(EvaluationWeights.MapOverrides));
    }

    [Fact]
    public void 按地图取权重只有一处实现且只由三个入口调用()
    {
        // Requirement 正文：覆盖表与"按地图标识取权重"的解析在 AI 层各只有一处实现，批量跑局、终端与图形版三个入口共用（design D4 / D6）。
        // AI 层的 weights ?? Default 回落 MUST NOT 改成按地图取（旧 2 人图日志首部权重为空，回放会改按覆盖决策而分歧）——
        // 占位期覆盖值等于默认表，行为比对分辨不出这一变异，由本扫描钉住。
        string root = PresentationFixtures.RepoRoot();
        string[] files = [.. new[] { Path.Combine(root, "src", "Siege.Core"), Path.Combine(root, "src", "Siege.Sim"), Path.Combine(root, "src", "Siege.Presentation"), Path.Combine(root, "src", "godot", "scripts") }
            .SelectMany(d => Directory.GetFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))];
        Assert.True(files.Length >= 150, $"样本口径：只扫到 {files.Length} 个源文件。");
        Assert.Contains(files, f => f.EndsWith(Path.Combine("godot", "scripts", "MatchSession.cs"), StringComparison.Ordinal));

        var symbol = new Regex(@"ForMapId\w*\(|MapOverrides\w*|MapOverrideCalibrationOf\w*\(");
        string[] users = [.. files.Where(f => symbol.IsMatch(File.ReadAllText(f))).Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).Order(StringComparer.Ordinal)];
        Assert.Equal(
            ["src/Siege.Core/Ai/EvaluationWeights.cs", "src/Siege.Sim/Config/RunConfig.cs", "src/Siege.Sim/Play/PlayCommand.cs", "src/godot/scripts/MatchSession.cs"],
            users);

        // 反面：唯一实现确实在定义文件里（扫描器不是空转）。
        string def = File.ReadAllText(Path.Combine(root, "src", "Siege.Core", "Ai", "EvaluationWeights.cs"));
        Assert.Matches(@"public static EvaluationWeights ForMapId\(", def);
        Assert.Matches(@"internal static EvaluationWeights ForMapId\(", def);
    }
}
