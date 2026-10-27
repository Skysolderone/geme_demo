using System.Numerics;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.AiDecision;

/// <summary>规格：ai-decision —— Requirement: 搭桥候选（ai-bridge-candidate D1–D4）</summary>
/// <remarks>
/// 构造局面（<see cref="Moat"/>）：E5 是深水，P0 的 D4 / E4 / F4 / D5 / F5 / D6 / F6 连成一块、把 E5 从三面围住，只空着 E6。匠人落 E6 并在 E5 搭桥，
/// E5 即成被这一块围住的单格眼（势力 +2、眼位 +1，「E6 匠人 + 搭桥 E5」= 2 + 148 = 150）；不搭桥落 E6 只得 +1。另有 P1 孤子 G9 被 F9 / H9 / G10 夹住只剩 G8 一口气：
/// 落 G8（任何类型、带任何栅栏）都提走 G9（敌损 4、势力 3、眼位 1，总分 1351），两处互不影响，同一批落下即相加。
/// 合法范围收窄为 {E6, G8}，库存普通 5 + 匠人 5，部署上限 2：G8 一格就有普通、匠人与 16 种带栅栏的匠人共 18 个单点（总分都是 1351），把前 N = 12 全占满，
/// 「E6 匠人 + 搭桥 E5」排第 19 名——贪心只在前 N 里组批，同格只取一个，于是关闭时 AI 只落 G8。
/// 权重写死为"势力 1、敌损 300、眼位 148、其余 0"（testing.md「依赖 AI 实际怎么走的断言要把权重写死」），停手阈值写死为 20（规格 Scenario 原文），每个数都能手算。
/// Pass 的总分为 0（空批次各维增量为 0）。规格 Scenario「前 N 之外的搭桥被看见」的算例写的是收益 150、第 30 名：构造局面里收益取 150，名次是第 19 名（同样在前 N = 12 之外）。
/// </remarks>
public class 搭桥候选Tests
{
    private static readonly PlayerId Me = AiFixtures.P0;

    private const int Threshold = 20;

    /// <summary>「E6 匠人 + 搭桥 E5」的单点总分（= 收益，Pass 总分 0）。</summary>
    private const int BridgeGain = 150;

    /// <summary>势力 1、敌损 300、眼位 148、其余维度 0。</summary>
    private static readonly EvaluationWeights Weights =
        new(PowerGain: 1, EnemyLoss: 300, Relic: 0, Safety: 0, Growth: 0, Initiative: 0, Supply: 0, Eye: 148, Threat: 0);

    private static Placement Bridge => new(TestMaps.At("E6"), PieceType.Artisan, TerrainEdit.Bridge(TestMaps.At("E5")));

    /// <summary>E5 深水被 P0 三面围住、G9（P1）只剩 G8 一口气；P0 居本战场首位（链接规则下落后者阈值取 0）。</summary>
    internal static MatchFlow Moat() =>
        LinkModeFixtures.PlainStarted(TestMaps.Terrain(surfaces: [("E5", Surface.DeepWater)]))
            .AtRound(5, [AiFixtures.P0, AiFixtures.P1, AiFixtures.P2, AiFixtures.P3])
            .Stones(Me, "D4", "E4", "F4", "D5", "F5", "D6", "F6", "F9", "H9", "G10")
            .Stones(AiFixtures.P1, "G9")
            .Stones(AiFixtures.P2, "J5")
            .AsLeader(Me);

    private static AiSearchConfig StandardWith(int editSeed, int threshold = Threshold, int cellLimit = 0) =>
        AiSearchConfig.Standard with { PassThreshold = threshold, CandidateCellLimit = cellLimit, EditSeed = editSeed };

    /// <summary>在 <see cref="Moat"/> 上以部署上限 2、合法范围 {E6, G8}、库存普通 5 + 匠人 5 做一次部署决策。</summary>
    internal static (HeuristicTurnController Ai, RandomStream Stream, StagedBatch Batch, SettlementDriver Driver) Deploy(AiDifficulty difficulty, AiSearchConfig config)
    {
        MatchFlow match = Moat();
        GameBoard board = match.Board;
        IReadOnlySet<Coord> range = new[] { "E6", "G8" }.Select(TestMaps.At).ToHashSet();
        var batch = new StagedBatch(board, BatchFixtures.Context(board, Me, 2, BatchFixtures.Stock((PieceType.Basic, 5), (PieceType.Artisan, 5)), range));
        var driver = new SettlementDriver(board, match.History, new RecordingHooks { Board = board });
        RandomStream stream = match.Seed.Stream(HeuristicAi.StreamName(Me));
        var ai = new HeuristicTurnController(Me, match.Publish, stream, difficulty, Weights, config);
        ai.Deploy(batch, () => driver.Rehearse(batch.Context, batch.Placements));
        return (ai, stream, batch, driver);
    }

    /// <summary>独立预演一组落点并按同一评价器求总分（不合法 / 被活形硬约束淘汰时断言失败）。</summary>
    private static BigInteger TotalOf(HeuristicTurnController ai, StagedBatch batch, SettlementDriver driver, params Placement[] placements)
    {
        RehearsalResult result = 活形硬约束Tests.Rehearse(batch, driver, placements);
        Assert.True(result.IsLegal, result.Failure?.Message);
        Assert.True(ai.CreateEvaluator().TryEvaluate([.. placements], result, batch.Context, out EvaluationBreakdown? evaluation));
        return evaluation!.Total;
    }

    private static bool Bridges(CandidateBatch? choice) => choice is not null && choice.Placements.Any(p => p.Edit?.Kind == TerrainEditKind.Bridge);

    [Fact]
    public void 前N之外的搭桥被看见()
    {
        // 阈值 20；「E6 匠人 + 搭桥 E5」收益 150、排第 19 名（前 N = 12 之外）；以它为起点续组 = {搭桥, G8} = 150 + 1351 + 0（两处互不影响）= 1501，高于全部既有候选（都只落 G8 = 1351）。
        // 关闭：AI 只落 G8、不搭桥；开启：AI 落下以搭桥单点为起点的批次。变异（a）去掉搭桥候选 → 本测试红。
        (HeuristicTurnController off, _, StagedBatch batch, SettlementDriver driver) = Deploy(AiDifficulty.Standard, StandardWith(0));

        Assert.Equal(0, TotalOf(off, batch, driver));
        Assert.Equal(BridgeGain, TotalOf(off, batch, driver, Bridge));
        Assert.Equal(12, off.LastPointRanking.Length);
        Assert.All(off.LastPointRanking, p => Assert.Equal("G8", p.Coord.ToNotation()));
        Assert.All(off.LastPointRanking, p => Assert.True(p.Total > BridgeGain, p.ToString()));   // 前提：搭桥单点在前 N 之外
        Assert.DoesNotContain(off.LastPointRanking, p => p.Placement == Bridge);
        Assert.False(Bridges(off.LastChoice), off.LastChoice!.ToString());
        Assert.Null(off.LastBridgeSeed);

        (HeuristicTurnController on, _, _, _) = Deploy(AiDifficulty.Standard, StandardWith(1));
        BridgeSeedCandidate trace = on.LastBridgeSeed!;
        Assert.Equal(Bridge, trace.Seed!.Placement);
        Assert.Equal(BridgeGain, trace.Seed.Total);
        Assert.Equal(0, trace.Scored);   // 未设候选格上限：搭桥单点都已在单点评价里评过，复用分数、不重复预演
        Assert.True(trace.IsSourceOf(on.LastChoice));
        Assert.Contains(Bridge, on.LastChoice!.Placements);
        Assert.Equal(["E6", "G8"], on.LastChoice.SortedCoords.Select(c => c.ToNotation()));
        Assert.Equal(TotalOf(on, batch, driver, [.. on.LastChoice.Placements]), on.LastChoice.Total);
        Assert.All(on.LastCandidates.Where(c => c.Key != on.LastChoice.Key), c => Assert.True(c.Total < on.LastChoice.Total, c.ToString()));
        Assert.Equal("D:" + on.LastChoice.Key, on.Decisions[^1]);
    }

    [Fact]
    public void 预筛挡掉的格也枚举()
    {
        // 候选格上限 K = 1：预筛（代表类型普通子、不带改造、七维）只留提子的 G8，E6 被挡在完整枚举之外；搭桥单点（收益 150 > 20）仍由搭桥候选枚举到并成为起点。
        // 关闭时 E6 从未被评价，AI 只落 G8。变异（b）枚举改成经预筛 → 本测试红。
        (HeuristicTurnController on, _, _, _) = Deploy(AiDifficulty.Standard, StandardWith(1, cellLimit: 1));
        Assert.Equal(["G8"], on.LastCandidateCells.Select(c => c.ToNotation()));
        BridgeSeedCandidate trace = on.LastBridgeSeed!;
        Assert.Equal(Bridge, trace.Seed!.Placement);
        Assert.Equal(BridgeGain, trace.Seed.Total);
        Assert.Equal(1, trace.Scored);   // 预筛挡掉的格上只有这一个搭桥选项，只新预演这一个
        Assert.True(trace.IsSourceOf(on.LastChoice));
        Assert.Contains(Bridge, on.LastChoice!.Placements);

        (HeuristicTurnController off, _, _, _) = Deploy(AiDifficulty.Standard, StandardWith(0, cellLimit: 1));
        Assert.Equal(["G8"], off.LastCandidateCells.Select(c => c.ToNotation()));
        Assert.False(Bridges(off.LastChoice), off.LastChoice!.ToString());
    }

    [Fact]
    public void 收益不过阈值不生成()
    {
        // 阈值取 150：搭桥单点收益恰等于阈值（"严格大于"才可作起点）→ 不生成，决策与关闭时逐条相同、候选集相同。
        // 反面：阈值 149 时生成且被选中（否则"不生成"是恒真）。变异（c）起点比较 > 改 >= → 本测试红。
        (HeuristicTurnController on, _, _, _) = Deploy(AiDifficulty.Standard, StandardWith(1, threshold: BridgeGain));
        (HeuristicTurnController off, _, _, _) = Deploy(AiDifficulty.Standard, StandardWith(0, threshold: BridgeGain));

        Assert.NotNull(on.LastBridgeSeed);
        Assert.Null(on.LastBridgeSeed.Seed);
        Assert.Null(on.LastBridgeSeed.Candidate);
        Assert.Equal(off.Decisions, on.Decisions);
        Assert.Equal(off.LastCandidates.Select(c => c.Key), on.LastCandidates.Select(c => c.Key));
        Assert.False(Bridges(on.LastChoice), on.LastChoice!.ToString());

        (HeuristicTurnController below, _, _, _) = Deploy(AiDifficulty.Standard, StandardWith(1, threshold: BridgeGain - 1));
        Assert.Equal(Bridge, below.LastBridgeSeed!.Seed!.Placement);
        Assert.True(below.LastBridgeSeed.IsSourceOf(below.LastChoice));
    }

    [Fact]
    public void 搭桥候选不消费随机()
    {
        // 高难（M = 32，扰动 31 次）在同一局面上开 / 关各决策一次：ai-P0 子流的消费次数相同；开启时确实选中了搭桥候选（反面：否则相同是恒真）。
        // 高难 N = 24 会把第 19 名的搭桥单点收进前 N，局面另设候选格上限 1 把 E6 挡在预筛之外，保证关闭时看不到它。
        AiSearchConfig hard = AiSearchConfig.Hard with { PassThreshold = Threshold, CandidateCellLimit = 1 };
        (HeuristicTurnController off, RandomStream offStream, _, _) = Deploy(AiDifficulty.Hard, hard with { EditSeed = 0 });
        (HeuristicTurnController on, RandomStream onStream, _, _) = Deploy(AiDifficulty.Hard, hard with { EditSeed = 1 });

        Assert.True(offStream.Consumed > 0, "样本口径：高难确有扰动消费");
        Assert.Equal(offStream.Consumed, onStream.Consumed);
        Assert.False(Bridges(off.LastChoice), off.LastChoice!.ToString());
        Assert.True(on.LastBridgeSeed!.IsSourceOf(on.LastChoice));
    }

    [Fact]
    public void 关闭时逐步不变()
    {
        // 开关为 0（整批候选照缺省开）：与引入之前（HEAD 01efcd4）的实现逐步相同——(1) 链接图黄金对局（link:1、种子 31、标准、24 个小回合）的小回合快照哈希等于改前钉值；
        // (2) 日志首部与批次配置都不含该字段；(3) 决策序列（link:1、种子 1–3、标准、截断第 12 大回合）与改前基线副本逐字节相同（5 份副本的全量守门见 决策序列基线Tests）。
        MatchLog log = BatchRunner.Execute(候选格上限Tests.LinkGoldenConfig() with { EditSeed = 0 }, parallelism: 1)[0];
        string hash = 候选格上限Tests.TurnHash(log);
        Assert.True(候选格上限Tests.LinkGoldenTurnHashEditSeedOff == hash, $"搭桥候选开关 0 的链接图黄金小回合哈希：{hash}");
        Assert.DoesNotContain("EditSeed", log.DeterministicText().Split('\n')[0], StringComparison.Ordinal);
        MatchLog parsed = MatchLog.Parse(log.DeterministicText());
        Assert.Null(parsed.Header.Config.EditSeed);
        Assert.All(Enumerable.Range(0, 4), i => Assert.Equal(0, parsed.EditSeedOf(i)));

        static string Fixture(string name) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AiDecision", "Fixtures", name)).Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach (ulong seed in new ulong[] { 1, 2, 3 })
        {
            Assert.Equal(Fixture($"decisions-link1-seed{seed}-es0.txt"), LinkDecisionSequence.Export(seed, LinkDecisionSequence.BaselineRounds, AiDifficulty.Standard, attach: null, configure: c => c with { EditSeed = 0 }));
        }
    }

    [Fact]
    public void 简单难度不受影响()
    {
        // 开关为 1 时 4 名简单 AI 在 link:1 种子 1 下到第 12 大回合的决策序列与开关为 0 时逐字节相同，且就是钉住的简单基线（简单难度不生成搭桥候选）。
        string on = LinkDecisionSequence.Export(1, LinkDecisionSequence.BaselineRounds, AiDifficulty.Easy, attach: null, configure: c => c with { EditSeed = 1 });
        string off = LinkDecisionSequence.Export(1, LinkDecisionSequence.BaselineRounds, AiDifficulty.Easy, attach: null, configure: c => c with { EditSeed = 0 });

        Assert.Equal(off, on);
        string expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AiDecision", "Fixtures", "decisions-link1-easy-seed1.txt")).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(expected, on);

        // 简单难度的控制器即使配置为 1 也不留搭桥候选的痕、不搭桥（同一局面的标准难度会搭桥，见 前N之外的搭桥被看见）。
        (HeuristicTurnController easy, _, _, _) = Deploy(AiDifficulty.Easy, AiSearchConfig.Easy with { PassThreshold = Threshold, EditSeed = 1 });
        Assert.Equal(1, easy.Config.EditSeed);
        Assert.Null(easy.LastBridgeSeed);
        Assert.False(Bridges(easy.LastChoice), easy.LastChoice!.ToString());
    }

    [Fact]
    public void 开关进入记录()
    {
        // 未显式配置开关的标准难度批次：首部的跑局配置记有 "EditSeed":1，逐玩家的实际生效值（MatchLog.EditSeedOf，与会话派生同一口径）为 1，会话建出的 AI 也是 1。
        // 显式 Search（开关 1）的玩家原样生效；跑局配置给 0 时不写出、按 0 读回。
        RunConfig blank = SimFixtures.Config(turnLimit: 4, difficulty: AiDifficulty.Standard);
        Assert.Null(blank.EditSeed);
        MatchSession session = MatchSession.Create(blank, 1);
        MatchLog log = session.Run();

        string header = log.DeterministicText().Split('\n')[0];
        Assert.Contains("\"EditSeed\":1", header, StringComparison.Ordinal);
        MatchLog parsed = MatchLog.Parse(log.DeterministicText());
        Assert.Equal(1, parsed.Header.Config.EditSeed);
        Assert.All(Enumerable.Range(0, 4), i => Assert.Equal(1, parsed.EditSeedOf(i)));
        Assert.All(session.Match.Players, p => Assert.Equal(1, session.AiOf(p)!.Config.EditSeed));

        RunConfig mixed = blank with
        {
            EditSeed = 0,
            Players = [.. blank.Players.Select((p, i) => i == 2 ? p with { Search = AiSearchConfig.Standard with { EditSeed = 1 } } : p)],
        };
        MatchSession mixedSession = MatchSession.Create(mixed, 1);
        MatchLog mixedLog = mixedSession.Run();
        Assert.Equal([0, 0, 1, 0], mixedSession.Match.Players.Select(p => mixedSession.AiOf(p)!.Config.EditSeed));
        MatchLog mixedParsed = MatchLog.Parse(mixedLog.DeterministicText());
        Assert.Null(mixedParsed.Header.Config.EditSeed);
        Assert.Equal([0, 0, 1, 0], Enumerable.Range(0, 4).Select(mixedParsed.EditSeedOf));
    }

    [Fact]
    public void 专家的搭桥候选进入首部的搜索配置()
    {
        // 专家的搜索配置由 RunConfig.ResolvedFor 落成写进首部：开关随跑局配置（缺省 1 写出，0 不写出）。
        RunConfig expert = SimFixtures.Config(turnLimit: 0, difficulty: AiDifficulty.Expert);
        Assert.All(expert.ResolvedFor(Siege.Core.Board.Maps.MapCatalog.Resolve("link:1:4")).Players, p => Assert.Equal(1, p.Search!.EditSeed));
        RunConfig off = (expert with { EditSeed = 0 }).ResolvedFor(Siege.Core.Board.Maps.MapCatalog.Resolve("link:1:4"));
        Assert.All(off.Players, p => Assert.Equal(0, p.Search!.EditSeed));
        Assert.DoesNotContain("EditSeed", off.ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void 前瞻模拟的对手不生成搭桥候选()
    {
        // design D2（裁决 3）：模拟对手的配置由专家本人的配置派生，开关清零。
        MatchFlow match = Moat();
        var lookahead = new ExpertLookahead(Me, match.Publish(), new HeuristicTurnController(Me, match.Publish, match.Seed.Stream("t")).CreateEvaluator(),
            EvaluationWeights.Default, AiSearchConfig.Expert, lifeQuery: null, cacheLife: true);
        Assert.Equal(1, AiSearchConfig.Expert.EditSeed);
        Assert.Equal(0, lookahead.SimulatedConfig.EditSeed);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    public void 无效开关被拒绝(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => (AiSearchConfig.Standard with { EditSeed = value }).Validated());
        Assert.Throws<ArgumentException>(() => (SimFixtures.Config() with { EditSeed = value }).Validated());
        Assert.ThrowsAny<ArgumentException>(() => (SimFixtures.Config() with { Players = [.. SimFixtures.Config().Players.Select(p => p with { Search = AiSearchConfig.Standard with { EditSeed = value } })] }).Validated());

        // 入口：命令行给出无效值 → 报错退出、不写任何输出。
        string badDir = Path.Combine(SimFixtures.TempDir($"edit-seed-cli-bad-{value}"), "out");
        TextWriter saved = Console.Error;
        using var err = new StringWriter();
        int code;
        try
        {
            Console.SetError(err);
            code = Siege.Sim.Program.Main(["run", "--out", badDir, "--seed", "1", "--count", "1", "--serial", "--edit-seed", value.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        }
        finally
        {
            Console.SetError(saved);
        }

        Assert.NotEqual(0, code);
        Assert.False(Directory.Exists(badDir));
        Assert.Contains("搭桥候选", err.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void 跑局配置的搭桥候选开关文本往返()
    {
        // 未配置不写；显式 0 也不写（读回为缺项，回放按 0）；1 写出并读回。搜索配置同理（0 不写出）。
        RunConfig unset = SimFixtures.Config();
        Assert.DoesNotContain("EditSeed", unset.ToJson(), StringComparison.Ordinal);
        Assert.Null(RunConfig.FromJson(unset.ToJson()).EditSeed);
        Assert.DoesNotContain("EditSeed", (unset with { EditSeed = 0 }).ToJson(), StringComparison.Ordinal);
        Assert.Null(RunConfig.FromJson((unset with { EditSeed = 0 }).ToJson()).EditSeed);
        Assert.Contains("\"EditSeed\": 1", (unset with { EditSeed = 1 }).ToJson(), StringComparison.Ordinal);
        Assert.Equal(1, RunConfig.FromJson((unset with { EditSeed = 1 }).ToJson()).EditSeed);

        string search = System.Text.Json.JsonSerializer.Serialize(AiSearchConfig.Standard with { EditSeed = 0 });
        Assert.DoesNotContain("EditSeed", search, StringComparison.Ordinal);
        Assert.Contains("\"EditSeed\":1", System.Text.Json.JsonSerializer.Serialize(AiSearchConfig.Standard), StringComparison.Ordinal);
        Assert.Equal(0, System.Text.Json.JsonSerializer.Deserialize<AiSearchConfig>(search)!.EditSeed);
    }

    [Fact]
    public void 首部缺搭桥候选开关的旧日志按0回放()
    {
        // 会话层：新建的局取缺省 1；按首部重建的局缺字段取 0、有字段取记录值。
        RunConfig config = SimFixtures.Config(turnLimit: 8, difficulty: AiDifficulty.Standard);
        Assert.Equal(1, MatchSession.Create(config, 1).EditSeed);
        Assert.Equal(1, MatchSession.Create(config, 1).Config.EditSeed);
        Assert.Equal(0, MatchSession.Create(config, 1, map: null, recorded: true).EditSeed);
        Assert.Equal(1, MatchSession.Create(config with { EditSeed = 1 }, 1, map: null, recorded: true).EditSeed);

        // 开关 0 的局首部不含该项，回放按 0 重建、逐行相同；开关 1 的局按首部的 1 重建、逐行相同。
        foreach (int value in new[] { 0, 1 })
        {
            MatchLog log = BatchRunner.Execute(config with { EditSeed = value }, parallelism: 1)[0];
            ReplayResult replay = Replayer.Replay(MatchLog.Parse(log.DeterministicText()));
            Assert.True(replay.Identical, replay.ToString());
            Assert.True(replay.LineCount >= 9, $"比对行数 {replay.LineCount}");
        }
    }

    [Fact]
    public void 命令行搭桥候选开关写入配置记录()
    {
        // 缺省：config.json 与首部写明 1；--edit-seed 0：两处都不写。
        string onDir = Path.Combine(SimFixtures.TempDir("edit-seed-cli-on"), "out");
        Assert.Equal(0, Siege.Sim.Program.Main(["run", "--out", onDir, "--seed", "1", "--count", "1", "--turn-limit", "4", "--difficulty", "Standard", "--serial"]));
        Assert.Contains("\"EditSeed\": 1", File.ReadAllText(Path.Combine(onDir, "config.json")), StringComparison.Ordinal);
        Assert.Contains("\"EditSeed\":1", File.ReadLines(Directory.GetFiles(onDir, "match-*.jsonl").Single()).First(), StringComparison.Ordinal);

        string offDir = Path.Combine(SimFixtures.TempDir("edit-seed-cli-off"), "out");
        Assert.Equal(0, Siege.Sim.Program.Main(["run", "--out", offDir, "--seed", "1", "--count", "1", "--turn-limit", "4", "--difficulty", "Standard", "--serial", "--edit-seed", "0"]));
        Assert.DoesNotContain("EditSeed", File.ReadAllText(Path.Combine(offDir, "config.json")), StringComparison.Ordinal);
        Assert.DoesNotContain("EditSeed", File.ReadLines(Directory.GetFiles(offDir, "match-*.jsonl").Single()).First(), StringComparison.Ordinal);
    }
}
