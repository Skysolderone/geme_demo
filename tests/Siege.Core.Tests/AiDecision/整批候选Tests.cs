using System.Numerics;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Determinism;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.AiDecision;

/// <summary>规格：ai-decision —— Requirement: 整批候选（ai-whole-batch D1–D4）</summary>
/// <remarks>
/// 构造局面（<see cref="Atari"/>）：P1 的孤子 G7 被 P0 的 G6 / G8 上下夹住，只剩 F7、H7 两口气。P0 单落 F7 或 H7 只是一枚贴着敌子的孤子（势力 +2），
/// 两枚同批落下才提走 G7、在 G7 做出一只单格眼（势力 +5、眼位 +1）。D5 是远离战斗的孤子（势力 +5，眼位 0）。
/// 权重写死为"势力 1、眼位 w、其余 0"（testing.md「依赖 AI 实际怎么走的断言要把权重写死」），于是单点总分 = 势力原始值，组合总分 = 势力 + w × 眼位，
/// 每个数都能手算；停手阈值写死为 20（规格 Scenario 原文），不读缺省值。Pass 的总分为 0（空批次各维增量为 0）。
/// 规格 Scenario「两枚一起才有收益」的算例写的是单点 −5 / 10、组合 +600：构造局面里 F7、H7 对称，单点都是 +2（同样都不过阈值 20），组合取 w = 595 使其恰为 +600。
/// </remarks>
public class 整批候选Tests
{
    private static readonly PlayerId Me = AiFixtures.P0;

    private const int Threshold = 20;

    /// <summary>势力 1、眼位 <paramref name="eye"/>、其余维度 0。</summary>
    private static EvaluationWeights PowerAndEye(int eye) =>
        new(PowerGain: 1, EnemyLoss: 0, Relic: 0, Safety: 0, Growth: 0, Initiative: 0, Supply: 0, Eye: eye, Threat: 0);

    /// <summary>G7（P1）被 G6 / G8（P0）夹住只剩 F7 / H7 两口气；P0 另在三个盘角补子，居本战场首位（链接规则下落后者阈值取 0）。</summary>
    internal static MatchFlow Atari() =>
        AiFixtures.Round5().Stones(Me, "G6", "G8").Stones(AiFixtures.P1, "G7").AsLeader(Me, "C3", "C10", "J10");

    private static AiSearchConfig StandardWith(int wholeBatch, int threshold = Threshold) =>
        AiSearchConfig.Standard with { PassThreshold = threshold, WholeBatch = wholeBatch };

    /// <summary>在 <see cref="Atari"/> 上以部署上限 <paramref name="limit"/>、合法范围 <paramref name="range"/> 做一次部署决策。</summary>
    private static (HeuristicTurnController Ai, RandomStream Stream, StagedBatch Batch, SettlementDriver Driver) Deploy(
        AiDifficulty difficulty, AiSearchConfig config, EvaluationWeights weights, int limit, params string[] range)
    {
        MatchFlow match = Atari();
        (StagedBatch batch, SettlementDriver driver) = 活形硬约束Tests.Staging(match, PieceType.Basic, limit, range);
        RandomStream stream = match.Seed.Stream(HeuristicAi.StreamName(Me));
        var ai = new HeuristicTurnController(Me, match.Publish, stream, difficulty, weights, config);
        ai.Deploy(batch, () => driver.Rehearse(batch.Context, batch.Placements));
        return (ai, stream, batch, driver);
    }

    /// <summary>独立预演一组落点并按同一评价器求总分（不合法 / 被活形硬约束淘汰时断言失败）。</summary>
    private static BigInteger TotalOf(HeuristicTurnController ai, StagedBatch batch, SettlementDriver driver, params string[] cells)
    {
        Placement[] placements = [.. cells.Select(c => BatchFixtures.P(c))];
        RehearsalResult result = 活形硬约束Tests.Rehearse(batch, driver, placements);
        Assert.True(result.IsLegal, result.Failure?.Message);
        Assert.True(ai.CreateEvaluator().TryEvaluate([.. placements], result, batch.Context, out EvaluationBreakdown? evaluation));
        return evaluation!.Total;
    }

    private static string[] Cells(CandidateBatch? candidate) =>
        candidate is null ? [] : [.. candidate.SortedCoords.Select(c => c.ToNotation())];

    private static string[] Expected(params string[] cells) => [.. cells.Select(TestMaps.At).Order().Select(c => c.ToNotation())];

    [Fact]
    public void 两枚一起才有收益()
    {
        // 部署上限 2、合法范围 {F7, H7}：单点各 +2（≤ 阈值 20），同批落下提走 G7 并做眼：+5 + 595 × 1 = +600。
        // 关闭：贪心逐枚都被撤回 → Pass；开启：剪枝候选（强制贪心 {F7, H7}，两枚留一法下降都是 598 > 20）被选中。
        EvaluationWeights weights = PowerAndEye(595);
        (HeuristicTurnController off, _, StagedBatch batch, SettlementDriver driver) = Deploy(AiDifficulty.Standard, StandardWith(0), weights, 2, "F7", "H7");

        Assert.Equal(2, off.LastPointRanking.Length);
        Assert.All(off.LastPointRanking, p => Assert.Equal(2, p.Total));   // 前提：单点边际提升 2 ≤ 阈值（Pass 总分 0）
        Assert.Equal(0, TotalOf(off, batch, driver));
        Assert.Equal(600, TotalOf(off, batch, driver, "F7", "H7"));
        Assert.True(off.LastChoice!.IsPass, off.LastChoice.ToString());
        Assert.Equal("D:pass", off.Decisions[^1]);
        Assert.Null(off.LastWholeBatch);

        (HeuristicTurnController on, _, _, _) = Deploy(AiDifficulty.Standard, StandardWith(1), weights, 2, "F7", "H7");
        Assert.Equal(Expected("F7", "H7"), Cells(on.LastChoice));
        Assert.Equal(600, on.LastChoice!.Total);
        Assert.Equal("D:" + on.LastChoice.Key, on.Decisions[^1]);
        Assert.NotEqual(WholeBatchSource.None, on.LastWholeBatch!.SourceOf(on.LastChoice));
    }

    [Fact]
    public void 剪枝去掉不出力的子()
    {
        // 部署上限 3、合法范围 {F7, H7, D5}，w = 320：单点 D5 +5、F7 / H7 各 +2，强制贪心（排序次序 D5、F7、H7）得 {A, B, C} = {F7, H7, D5}。
        // 拿掉 D5 整批只降 5（≤ 20）→ 剪掉；拿掉 F7 或 H7 都降 323（≥ 300）→ 保留。剪枝候选 = {F7, H7}，它也是最终选择（总分 325）。
        // 开关为 0 时同一局面 Pass（单点都不过阈值 20）。
        EvaluationWeights weights = PowerAndEye(320);
        (HeuristicTurnController ai, _, StagedBatch batch, SettlementDriver driver) = Deploy(AiDifficulty.Standard, StandardWith(1), weights, 3, "F7", "H7", "D5");

        BigInteger all = TotalOf(ai, batch, driver, "F7", "H7", "D5");
        Assert.Equal(5, all - TotalOf(ai, batch, driver, "F7", "H7"));
        Assert.True(all - TotalOf(ai, batch, driver, "H7", "D5") >= 300);
        Assert.True(all - TotalOf(ai, batch, driver, "F7", "D5") >= 300);

        WholeBatchCandidates trace = ai.LastWholeBatch!;
        Assert.Equal(Expected("F7", "H7"), Cells(trace.Pruned));
        Assert.Equal(325, trace.Pruned!.Total);
        Assert.Equal(Expected("F7", "H7"), Cells(ai.LastChoice));
        Assert.Equal(WholeBatchSource.Pruned, trace.SourceOf(ai.LastChoice));

        (HeuristicTurnController off, _, _, _) = Deploy(AiDifficulty.Standard, StandardWith(0), weights, 3, "F7", "H7", "D5");
        Assert.True(off.LastChoice!.IsPass, off.LastChoice.ToString());
    }

    [Fact]
    public void 留一法下降恰等于阈值也剪掉()
    {
        // 同「剪枝去掉不出力的子」，阈值改为 5：拿掉 D5 恰降 5，"不大于阈值"即剪掉（留一法口径是严格大于才保留）。
        // 变异（c）留一法比较 > 改 >= → 剪枝候选留下 D5，本测试红。
        (HeuristicTurnController ai, _, _, _) = Deploy(AiDifficulty.Standard, StandardWith(1, threshold: 5), PowerAndEye(320), 3, "F7", "H7", "D5");

        Assert.Equal(Expected("F7", "H7"), Cells(ai.LastWholeBatch!.Pruned));
    }

    [Fact]
    public void 配对候选补上剪枝漏掉的组合()
    {
        // 部署上限 2、合法范围 {D5, F7, H7}，w = 595：强制贪心按排序取 D5、F7（上限 2）→ 拿掉 F7 降 2、拿掉 D5 降 5 → 逐枚剪空，剪枝候选为空。
        // 配对候选：三个单点都不过阈值，两两组合中只有 {F7, H7}（+600，两枚留一法下降都是 598）有效 → 配对候选 {F7, H7} 被选中。
        // 开关为 0 时 Pass。变异（b）去掉配对候选 → 本测试红。
        EvaluationWeights weights = PowerAndEye(595);
        (HeuristicTurnController ai, _, _, _) = Deploy(AiDifficulty.Standard, StandardWith(1), weights, 2, "D5", "F7", "H7");

        WholeBatchCandidates trace = ai.LastWholeBatch!;
        Assert.Null(trace.Pruned);
        Assert.Equal(Expected("F7", "H7"), Cells(trace.Paired));
        Assert.True(trace.PairedAdded);
        Assert.Equal(Expected("F7", "H7"), Cells(ai.LastChoice));
        Assert.Equal(WholeBatchSource.Paired, trace.SourceOf(ai.LastChoice));

        (HeuristicTurnController off, _, _, _) = Deploy(AiDifficulty.Standard, StandardWith(0), weights, 2, "D5", "F7", "H7");
        Assert.True(off.LastChoice!.IsPass, off.LastChoice.ToString());
    }

    [Fact]
    public void 整批候选不消费随机()
    {
        // 高难（M = 32，扰动 31 次）在同一局面上开 / 关各决策一次：ai-P0 子流的消费次数相同；开启时确实生成并选中了整批候选（反面：否则相同是恒真）。
        EvaluationWeights weights = PowerAndEye(595);
        AiSearchConfig hard = AiSearchConfig.Hard with { PassThreshold = Threshold };
        (HeuristicTurnController off, RandomStream offStream, _, _) = Deploy(AiDifficulty.Hard, hard with { WholeBatch = 0 }, weights, 2, "D5", "F7", "H7");
        (HeuristicTurnController on, RandomStream onStream, _, _) = Deploy(AiDifficulty.Hard, hard with { WholeBatch = 1 }, weights, 2, "D5", "F7", "H7");

        Assert.True(offStream.Consumed > 0, "样本口径：高难确有扰动消费");
        Assert.Equal(offStream.Consumed, onStream.Consumed);
        Assert.True(off.LastChoice!.IsPass);
        Assert.Equal(WholeBatchSource.Paired, on.LastWholeBatch!.SourceOf(on.LastChoice));
    }

    [Fact]
    public void 关闭时逐步不变()
    {
        // 开关为 0：与引入之前的实现逐步相同——(1) 链接图黄金对局（link:1、种子 31、标准、24 个小回合）的小回合快照哈希等于改前钉值；
        // (2) 日志首部与批次配置都不含该字段；(3) 决策序列（link:1、种子 3、标准、截断第 12 大回合）与改前基线副本逐字节相同（5 份副本的全量守门见 决策序列基线Tests）。
        // ai-bridge-candidate：两处样本同时关掉搭桥候选（EditSeed = 0），钉值是引入整批候选之前的口径。
        MatchLog log = BatchRunner.Execute(候选格上限Tests.LinkGoldenConfig() with { WholeBatch = 0, EditSeed = 0 }, parallelism: 1)[0];
        string hash = 候选格上限Tests.TurnHash(log);
        Assert.True(候选格上限Tests.LinkGoldenTurnHashWholeBatchOff == hash, $"开关 0 的链接图黄金小回合哈希：{hash}");
        Assert.DoesNotContain("WholeBatch", log.DeterministicText().Split('\n')[0], StringComparison.Ordinal);
        MatchLog parsed = MatchLog.Parse(log.DeterministicText());
        Assert.Null(parsed.Header.Config.WholeBatch);
        Assert.All(Enumerable.Range(0, 4), i => Assert.Equal(0, parsed.WholeBatchOf(i)));

        // 样本取种子 3：开关 1 的缺省基线（decisions-link1-seed3.txt）与改前副本不同（反面：否则"开关 0 与改前相同"是恒真）。
        static string Fixture(string name) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AiDecision", "Fixtures", name)).Replace("\r\n", "\n", StringComparison.Ordinal);
        string expected = Fixture("decisions-link1-seed3-wb0.txt");
        Assert.NotEqual(expected, Fixture("decisions-link1-seed3.txt"));
        Assert.Equal(expected, LinkDecisionSequence.Export(3, 12, AiDifficulty.Standard, attach: null, configure: c => c with { WholeBatch = 0, EditSeed = 0 }));
    }

    [Fact]
    public void 简单难度不受影响()
    {
        // 开关为 1 时 4 名简单 AI 在 link:1 种子 1 下到第 12 大回合的决策序列与开关为 0 时逐字节相同，且就是钉住的简单基线（简单难度不生成整批候选）。
        string on = LinkDecisionSequence.Export(1, 12, AiDifficulty.Easy, attach: null, configure: c => c with { WholeBatch = 1 });
        string off = LinkDecisionSequence.Export(1, 12, AiDifficulty.Easy, attach: null, configure: c => c with { WholeBatch = 0 });

        Assert.Equal(off, on);
        string expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AiDecision", "Fixtures", "decisions-link1-easy-seed1.txt")).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(expected, on);

        // 简单难度的控制器即使配置为 1 也不留整批候选的痕。
        (HeuristicTurnController easy, _, _, _) = Deploy(AiDifficulty.Easy, AiSearchConfig.Easy with { PassThreshold = Threshold, WholeBatch = 1 }, PowerAndEye(595), 2, "F7", "H7");
        Assert.Equal(1, easy.Config.WholeBatch);
        Assert.Null(easy.LastWholeBatch);
        Assert.True(easy.LastChoice!.IsPass);
    }

    [Fact]
    public void 开关进入记录()
    {
        // 未显式配置开关的标准难度批次：首部的跑局配置记有 "WholeBatch":1，逐玩家的实际生效值（MatchLog.WholeBatchOf，与会话派生同一口径）为 1，会话建出的 AI 也是 1。
        // 显式 Search（开关 0）的玩家原样生效；跑局配置给 0 时不写出、按 0 读回。
        RunConfig blank = SimFixtures.Config(turnLimit: 4, difficulty: AiDifficulty.Standard);
        Assert.Null(blank.WholeBatch);
        MatchSession session = MatchSession.Create(blank, 1);
        MatchLog log = session.Run();

        string header = log.DeterministicText().Split('\n')[0];
        Assert.Contains("\"WholeBatch\":1", header, StringComparison.Ordinal);
        MatchLog parsed = MatchLog.Parse(log.DeterministicText());
        Assert.Equal(1, parsed.Header.Config.WholeBatch);
        Assert.All(Enumerable.Range(0, 4), i => Assert.Equal(1, parsed.WholeBatchOf(i)));
        Assert.All(session.Match.Players, p => Assert.Equal(1, session.AiOf(p)!.Config.WholeBatch));

        RunConfig mixed = blank with
        {
            WholeBatch = 0,
            Players = [.. blank.Players.Select((p, i) => i == 2 ? p with { Search = AiSearchConfig.Standard with { WholeBatch = 1 } } : p)],
        };
        MatchSession mixedSession = MatchSession.Create(mixed, 1);
        MatchLog mixedLog = mixedSession.Run();
        Assert.Equal([0, 0, 1, 0], mixedSession.Match.Players.Select(p => mixedSession.AiOf(p)!.Config.WholeBatch));
        MatchLog mixedParsed = MatchLog.Parse(mixedLog.DeterministicText());
        Assert.Null(mixedParsed.Header.Config.WholeBatch);
        Assert.Equal([0, 0, 1, 0], Enumerable.Range(0, 4).Select(mixedParsed.WholeBatchOf));
    }

    [Fact]
    public void 专家的整批候选进入首部的搜索配置()
    {
        // 专家的搜索配置由 RunConfig.ResolvedFor 落成写进首部：开关随跑局配置（缺省 1 写出，0 不写出）。
        RunConfig expert = SimFixtures.Config(turnLimit: 0, difficulty: AiDifficulty.Expert);
        Assert.All(expert.ResolvedFor(Siege.Core.Board.Maps.MapCatalog.Resolve("link:1:4")).Players, p => Assert.Equal(1, p.Search!.WholeBatch));
        RunConfig off = (expert with { WholeBatch = 0 }).ResolvedFor(Siege.Core.Board.Maps.MapCatalog.Resolve("link:1:4"));
        Assert.All(off.Players, p => Assert.Equal(0, p.Search!.WholeBatch));
        Assert.DoesNotContain("WholeBatch", off.ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void 前瞻模拟的对手不生成整批候选()
    {
        // design D3（裁决 3）：模拟对手的配置由专家本人的配置派生，开关清零。
        MatchFlow match = Atari();
        var lookahead = new ExpertLookahead(Me, match.Publish(), new HeuristicTurnController(Me, match.Publish, match.Seed.Stream("t")).CreateEvaluator(),
            EvaluationWeights.Default, AiSearchConfig.Expert, lifeQuery: null, cacheLife: true);
        Assert.Equal(1, AiSearchConfig.Expert.WholeBatch);
        Assert.Equal(0, lookahead.SimulatedConfig.WholeBatch);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    public void 无效开关被拒绝(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => (AiSearchConfig.Standard with { WholeBatch = value }).Validated());
        Assert.Throws<ArgumentException>(() => (SimFixtures.Config() with { WholeBatch = value }).Validated());
        Assert.ThrowsAny<ArgumentException>(() => (SimFixtures.Config() with { Players = [.. SimFixtures.Config().Players.Select(p => p with { Search = AiSearchConfig.Standard with { WholeBatch = value } })] }).Validated());

        // 入口：命令行给出无效值 → 报错退出、不写任何输出。
        string badDir = Path.Combine(SimFixtures.TempDir($"whole-batch-cli-bad-{value}"), "out");
        TextWriter saved = Console.Error;
        using var err = new StringWriter();
        int code;
        try
        {
            Console.SetError(err);
            code = Siege.Sim.Program.Main(["run", "--out", badDir, "--seed", "1", "--count", "1", "--serial", "--whole-batch", value.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        }
        finally
        {
            Console.SetError(saved);
        }

        Assert.NotEqual(0, code);
        Assert.False(Directory.Exists(badDir));
        Assert.Contains("整批候选", err.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void 跑局配置的整批候选开关文本往返()
    {
        // 未配置不写；显式 0 也不写（读回为缺项，回放按 0）；1 写出并读回。搜索配置同理（0 不写出）。
        RunConfig unset = SimFixtures.Config();
        Assert.DoesNotContain("WholeBatch", unset.ToJson(), StringComparison.Ordinal);
        Assert.Null(RunConfig.FromJson(unset.ToJson()).WholeBatch);
        Assert.DoesNotContain("WholeBatch", (unset with { WholeBatch = 0 }).ToJson(), StringComparison.Ordinal);
        Assert.Null(RunConfig.FromJson((unset with { WholeBatch = 0 }).ToJson()).WholeBatch);
        Assert.Contains("\"WholeBatch\": 1", (unset with { WholeBatch = 1 }).ToJson(), StringComparison.Ordinal);
        Assert.Equal(1, RunConfig.FromJson((unset with { WholeBatch = 1 }).ToJson()).WholeBatch);

        string search = System.Text.Json.JsonSerializer.Serialize(AiSearchConfig.Standard with { WholeBatch = 0 });
        Assert.DoesNotContain("WholeBatch", search, StringComparison.Ordinal);
        Assert.Contains("\"WholeBatch\":1", System.Text.Json.JsonSerializer.Serialize(AiSearchConfig.Standard), StringComparison.Ordinal);
        Assert.Equal(0, System.Text.Json.JsonSerializer.Deserialize<AiSearchConfig>(search)!.WholeBatch);
    }

    [Fact]
    public void 首部缺整批候选开关的旧日志按0回放()
    {
        // 会话层：新建的局取缺省 1；按首部重建的局缺字段取 0、有字段取记录值。
        RunConfig config = SimFixtures.Config(turnLimit: 8, difficulty: AiDifficulty.Standard);
        Assert.Equal(1, MatchSession.Create(config, 1).WholeBatch);
        Assert.Equal(1, MatchSession.Create(config, 1).Config.WholeBatch);
        Assert.Equal(0, MatchSession.Create(config, 1, map: null, recorded: true).WholeBatch);
        Assert.Equal(1, MatchSession.Create(config with { WholeBatch = 1 }, 1, map: null, recorded: true).WholeBatch);

        // 开关 0 的局首部不含该项，回放按 0 重建、逐行相同；开关 1 的局按首部的 1 重建、逐行相同。
        foreach (int value in new[] { 0, 1 })
        {
            MatchLog log = BatchRunner.Execute(config with { WholeBatch = value }, parallelism: 1)[0];
            ReplayResult replay = Replayer.Replay(MatchLog.Parse(log.DeterministicText()));
            Assert.True(replay.Identical, replay.ToString());
            Assert.True(replay.LineCount >= 9, $"比对行数 {replay.LineCount}");
        }
    }

    [Fact]
    public void 命令行整批候选开关写入配置记录()
    {
        // 缺省：config.json 与首部写明 1；--whole-batch 0：两处都不写。
        string onDir = Path.Combine(SimFixtures.TempDir("whole-batch-cli-on"), "out");
        Assert.Equal(0, Siege.Sim.Program.Main(["run", "--out", onDir, "--seed", "1", "--count", "1", "--turn-limit", "4", "--difficulty", "Standard", "--serial"]));
        Assert.Contains("\"WholeBatch\": 1", File.ReadAllText(Path.Combine(onDir, "config.json")), StringComparison.Ordinal);
        Assert.Contains("\"WholeBatch\":1", File.ReadLines(Directory.GetFiles(onDir, "match-*.jsonl").Single()).First(), StringComparison.Ordinal);

        string offDir = Path.Combine(SimFixtures.TempDir("whole-batch-cli-off"), "out");
        Assert.Equal(0, Siege.Sim.Program.Main(["run", "--out", offDir, "--seed", "1", "--count", "1", "--turn-limit", "4", "--difficulty", "Standard", "--serial", "--whole-batch", "0"]));
        Assert.DoesNotContain("WholeBatch", File.ReadAllText(Path.Combine(offDir, "config.json")), StringComparison.Ordinal);
        Assert.DoesNotContain("WholeBatch", File.ReadLines(Directory.GetFiles(offDir, "match-*.jsonl").Single()).First(), StringComparison.Ordinal);
    }
}
