using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.AiDecision;

/// <summary>规格：ai-decision —— Requirement: 停手阈值（ai-eye 段 B，2.3 / 2.4；design D4）</summary>
/// <remarks>
/// 贪心组批的保留条件是"这一枚使整批加权总分的提升 <b>严格大于</b> 停手阈值"。边界三条（15 / 20 / 21）用部署上限 1、合法范围 1 格的局面：
/// 空批次（Pass）在简单难度下总分为 0，于是这一枚的边际提升就是它自己的总分；再用 PowerGain 权重把总分精确调到 15 / 20 / 21。
/// 变异 M-B6（保留条件 <c>&gt;</c> 改 <c>&gt;=</c>）→ 红 3（零收益不落子、恰等于阈值不落子、候选格上限黄金哈希）。
/// </remarks>
public class 停手阈值Tests
{
    /// <summary>
    /// 严格提高实现（ai-eye 段 B 2.2 完成、2.3 之前：活形硬约束已生效，保留条件仍是 <c>next.Total &gt; current.Total</c>）的实际运行结果——
    /// v4、种子 31、4 名 Standard AI、24 个小回合，全部小回合快照（去耗时）逐行拼接后的 SHA-256（口径同 <see cref="候选格上限Tests.TurnHash"/>）。
    /// 阈值取 0 时本 change 的实现 MUST 逐步重现它。
    /// <para>life-single-stone（规则变更）重建：8EEC49A7…04679FC8 → DCB7CC7C…CD39033E。去掉单子上限的探针下旧值逐字节复现；
    /// 分叉与 <see cref="候选格上限Tests.V4GoldenTurnHash"/> 同源（第 1 个小回合只差活形记录，第 2 个小回合 P0 的匠人 D3 → B3），归因见那里的注释。</para>
    /// </summary>
    private const string StrictImprovementTurnHash = "DCB7CC7CAC326562BD7FFFDE813172AD8494335298F4B2EBDC44BD17CD39033E";

    private static readonly PlayerId Me = AiFixtures.P0;

    private static HeuristicTurnController DeployWith(
        MatchFlow match, AiDifficulty difficulty, AiSearchConfig config, EvaluationWeights? weights, StagedBatch batch, SettlementDriver driver)
    {
        var ai = new HeuristicTurnController(Me, match.Publish, match.Seed.Stream(HeuristicAi.StreamName(Me)), difficulty, weights, config);
        ai.Deploy(batch, () => driver.Rehearse(batch.Context, batch.Placements));
        return ai;
    }

    /// <summary>
    /// E5 被 P0 三面（D5、E4、E6）与 P1（F5）夹住，是争议格：
    ///  6 . O . .
    ///  5 O ! X .     ! = E5（唯一合法格，部署上限 1）
    ///  4 . O . .
    ///    D E F G
    /// 落 E5：三枚孤子连成一串，棋串军势 3 → 4（+1）；E5 本是争议格、四邻已全被占，领地不增不减；P1 不受损。
    /// 简单难度只评价这两维，总分 = PowerGain 权重 × 1。
    /// </summary>
    private static HeuristicTurnController Marginal(int powerGainWeight, int threshold)
    {
        MatchFlow match = AiFixtures.Round5().Stones(Me, "D5", "E4", "E6").Stones(AiFixtures.P1, "F5");
        (StagedBatch batch, SettlementDriver driver) = 活形硬约束Tests.Staging(match, PieceType.Basic, 1, "E5");
        EvaluationWeights weights = EvaluationWeights.Default with { PowerGain = powerGainWeight };

        HeuristicTurnController ai = DeployWith(match, AiDifficulty.Easy, AiSearchConfig.Easy with { PassThreshold = threshold }, weights, batch, driver);

        PointScore point = Assert.Single(ai.LastPointRanking);
        Assert.Equal(1, point.Evaluation.RawOf(EvaluationDimension.PowerGain));
        Assert.Equal(0, point.Evaluation.RawOf(EvaluationDimension.EnemyLoss));
        Assert.Equal(powerGainWeight, point.Total);
        return ai;
    }

    private static void AssertPassed(HeuristicTurnController ai)
    {
        Assert.True(ai.LastChoice!.IsPass, ai.LastChoice.ToString());
        Assert.Equal("D:pass", ai.Decisions[^1]);
    }

    [Fact]
    public void 零收益不落子()
    {
        // P0 围出一块 2 格独占空区 A1–B1（眼值 0，不是单格眼，不受活形硬约束）：
        //  2 O O .
        //  1 . ! O     ! = B1（唯一合法格）
        //    A B C
        // 落 B1：领地 −1、棋串军势 +1，势力增量 0；简单难度总分 0。阈值 0 下"提升 0 不大于 0"→ 撤回。
        // ai-eye R26 起简单难度也算眼位：B1 把 2 格空区变成单格眼 A1（眼位原始值 +1），在默认权重下不再是零收益手。
        // 本 Scenario 钉的是"零收益 → 撤回"，故权重写死为眼位 0 的定值前缺省（段 D2 改写）。
        MatchFlow match = AiFixtures.Round5().Stones(Me, "A2", "B2", "C1");
        EyeSpace space = LifeShapeReport.Analyze(match.Board).EyeSpaceAt(TestMaps.At("B1"))!;
        Assert.Equal(2, space.Cells.Length);
        Assert.Equal(Me, space.Owner);
        (StagedBatch batch, SettlementDriver driver) = 活形硬约束Tests.Staging(match, PieceType.Basic, 1, "B1");

        HeuristicTurnController ai = DeployWith(match, AiDifficulty.Easy, AiSearchConfig.Easy with { PassThreshold = 0 }, SimFixtures.PreCalibrationWeights, batch, driver);

        PointScore point = Assert.Single(ai.LastPointRanking);
        Assert.Equal(0, point.Evaluation.RawOf(EvaluationDimension.PowerGain));
        Assert.Equal(0, point.Total);
        AssertPassed(ai);
        Assert.Equal(0, batch.Count);
    }

    [Fact]
    public void 低于阈值不落子() => AssertPassed(Marginal(powerGainWeight: 15, threshold: 20));

    [Fact]
    public void 恰等于阈值不落子() => AssertPassed(Marginal(powerGainWeight: 20, threshold: 20));

    [Fact]
    public void 高于阈值落子()
    {
        // 变异 M-B15（保留条件 > 阈值 改 > 阈值 + 1）→ 红 2（本测试、无落点被保留则Pass——int.MaxValue + 1 溢出为负，全部保留）。
        HeuristicTurnController ai = Marginal(powerGainWeight: 21, threshold: 20);
        Placement placed = Assert.Single(ai.LastChoice!.Placements);
        Assert.Equal(TestMaps.At("E5"), placed.Coord);
        Assert.Equal(21, ai.LastChoice.Total);
    }

    [Fact]
    public void 无落点被保留则Pass()
    {
        // 开放局面（同「候选格上限Tests」）：阈值 0 时 AI 会落子（反面，否则 Pass 是恒真）；阈值取 int.MaxValue 时任何一枚的边际提升都不超过它 → 0 落子 Pass。
        // 变异 M-B7（Greedy 仍按"严格提高"保留、不看阈值）→ 红 4（本测试、低于阈值不落子、恰等于阈值不落子、候选格上限黄金哈希）。
        // pass-threshold-first-stone：P0 在 A1 放一枚远离候选区的子——无子豁免下"盘上没有己方棋子"时阈值取 0，int.MaxValue 就测不到了。
        static MatchFlow Open() => AiFixtures.Round5().Stones(Me, "A1").Stones(AiFixtures.P1, "E5", "F6", "D7").Stones(AiFixtures.P2, "G3");

        MatchFlow eager = Open();
        HeuristicTurnController zero = HeuristicAi.Create(eager, Me, AiDifficulty.Standard, config: AiSearchConfig.Standard with { PassThreshold = 0 });
        StagedBatch eagerBatch = eager.OpenDeploy();
        zero.Deploy(eagerBatch, eager.Rehearse);
        Assert.True(eagerBatch.Count > 0);

        MatchFlow match = Open();
        Assert.False(match.Board.GroupsOf(Me).IsEmpty);
        HeuristicTurnController ai = HeuristicAi.Create(match, Me, AiDifficulty.Standard, config: AiSearchConfig.Standard with { PassThreshold = int.MaxValue });
        StagedBatch batch = match.OpenDeploy();
        ai.Deploy(batch, match.Rehearse);

        Assert.NotEmpty(ai.LastPointRanking);
        Assert.Contains(ai.LastPointRanking, p => p.Total > 0);
        AssertPassed(ai);
        Assert.Equal(0, batch.Count);
    }

    [Fact]
    public void 阈值为0时零变化()
    {
        // 规格：阈值 0 时保留条件退化为"加权总分严格提高"，决策与不含本配置的实现逐步相同。
        // 对照值取自严格提高实现的实际运行（见 StrictImprovementTurnHash）；种子 1–20 的离线决策序列比对另见 implement 记录。
        // 变异 M-B14（会话建 AI 时不传跑局配置的阈值，即实际按缺省 20 跑）→ 红 2（本测试、阈值进入记录）。
        // M-B6（> 改 >=）在本样本上不红：种子 31 前 24 个小回合里没有边际提升恰为 0 的候选，阈值 0 下 > 与 >= 走法相同；该变异由「零收益不落子」「恰等于阈值不落子」挡住。
        // ai-eye 段 D2（4.5）：九维权重写死为对照实现当时的缺省（Eye / Threat = 0）。本测试钉的是"阈值 0 = 严格提高"，不是默认权重；
        // 默认权重改为校准值（Eye 200、Threat 25）后若仍跟随缺省，对照哈希就不再是"严格提高实现"的产物（testing.md「校准与保真度分离」）。
        RunConfig zero = SimFixtures.PinPreCalibration(SimFixtures.Config(seedStart: 31, turnLimit: 24, difficulty: AiDifficulty.Standard)) with { PassThreshold = 0 };
        MatchLog log = BatchRunner.Execute(zero, parallelism: 1)[0];

        Assert.False(log.IsFailed);
        Assert.True(log.Turns.Count >= 24, $"小回合 {log.Turns.Count}");
        Assert.Equal(0, log.Header.Config.PassThreshold);
        string hash = 候选格上限Tests.TurnHash(log);
        Assert.True(StrictImprovementTurnHash == hash, $"阈值 0 的小回合快照哈希：{hash}");

        // 反面：缺省阈值下同一局走法不同——否则"阈值确实传到了 AI"无从证明。
        Assert.NotEqual(StrictImprovementTurnHash, 候选格上限Tests.V4GoldenTurnHash);
    }

    [Fact]
    public void 阈值进入记录()
    {
        // 批次配置记录与日志首部都读原文，不经反序列化（缺省值会掩盖漏写）。
        // 变异 M-B8（RunConfig.ResolvedFor 不落成缺省阈值）→ 段 B 实测红 6；ai-eye 段 D2 重跑（缺省阈值改为 80、多个夹具写死阈值之后）红 13（本测试、首部缺停手阈值的旧日志按0回放，
        // 以及回放 / 配置记录类 11 条：首部缺该项 → 回放按 0 重建而分歧 / 配置记录不符，逐条见任务 09-23-ai-eye 段 D2 记录）。
        // 变异 M-B14（会话建 AI 时不传跑局配置的阈值）→ 红 2（本测试的逐玩家断言、阈值为0时零变化）。
        RunConfig blank = SimFixtures.Config(turnLimit: 4);
        Assert.Null(blank.PassThreshold);
        string dir = SimFixtures.TempDir("pass-threshold-default");
        BatchRunner.ExecuteToDirectory(blank, dir, parallelism: 1);
        Assert.Contains($"\"PassThreshold\": {AiSearchConfig.DefaultPassThreshold}", File.ReadAllText(Path.Combine(dir, "config.json")), StringComparison.Ordinal);
        string file = Directory.EnumerateFiles(dir, "match-*.jsonl").Single();
        Assert.Contains($"\"PassThreshold\":{AiSearchConfig.DefaultPassThreshold},", File.ReadLines(file).First(), StringComparison.Ordinal);

        // 逐玩家的实际生效值：显式剪枝参数（含其中的阈值）原样生效，其余玩家取跑局配置的阈值。样本值 7 / 3 既非缺省 20 也非缺字段回填 0。
        RunConfig mixed = blank with
        {
            PassThreshold = 7,
            Players = [.. blank.Players.Select((p, i) => i == 1 ? p with { Search = AiSearchConfig.Easy with { PassThreshold = 3 } } : p)],
        };
        MatchSession session = MatchSession.Create(mixed, 1);
        MatchLog log = session.Run();
        Assert.Equal([7, 3, 7, 7], session.Match.Players.Select(p => session.AiOf(p)!.Config.PassThreshold));

        string header = log.DeterministicText().Split('\n')[0];
        Assert.Contains("\"PassThreshold\":7,", header, StringComparison.Ordinal);
        Assert.Contains("\"PassThreshold\":3", header, StringComparison.Ordinal);
        RunConfig recorded = MatchLog.Parse(log.DeterministicText()).Header.Config;
        Assert.Equal([7, 3, 7, 7], recorded.Players.Select(p => p.Search?.PassThreshold ?? recorded.PassThreshold!.Value));
    }

    // ---------- pass-threshold-first-stone：无子豁免（design D1） ----------

    /// <summary>
    /// 孤子局面：P0 在指定格落普通子，四邻全是空格、周围没有任何棋子（不连串、不提子、不成眼）。
    /// 简单难度只算即时势力、敌损、眼位三维；孤子势力增量 = 棋串军势 1 + 独占领地 4 = 5。权重写死为定值前缺省（眼位 0）并把 PowerGain 取 6
    /// （testing.md「依赖 AI 实际怎么走的断言要把权重写死」），每枚孤子的边际提升恰为 6 × 5 = 30（spec Scenario 的算例；proposal：简单难度空盘单子加权收益约 30，过不了阈值 80）。
    /// </summary>
    private static HeuristicTurnController LoneStones(MatchFlow match, int limit, params string[] range)
    {
        (StagedBatch batch, SettlementDriver driver) = 活形硬约束Tests.Staging(match, PieceType.Basic, limit, range);
        HeuristicTurnController ai = DeployWith(
            match, AiDifficulty.Easy, AiSearchConfig.Easy with { PassThreshold = AiSearchConfig.DefaultPassThreshold }, SimFixtures.PreCalibrationWeights with { PowerGain = 6 }, batch, driver);

        Assert.Equal(80, ai.Config.PassThreshold);
        Assert.Equal(range.Length, ai.LastPointRanking.Length);
        foreach (PointScore point in ai.LastPointRanking)
        {
            Assert.Equal(5, point.Evaluation.RawOf(EvaluationDimension.PowerGain));
            Assert.Equal(0, point.Evaluation.RawOf(EvaluationDimension.EnemyLoss));
            Assert.Equal(0, point.Evaluation.RawOf(EvaluationDimension.Eye));
            Assert.Equal(30, point.Total);
        }

        return ai;
    }

    [Fact]
    public void 无子时不受阈值限制()
    {
        // 阈值 80，P0 盘上一枚子都没有：提升 30 的落点按实际阈值 0 判定而保留。
        // 变异 M-F1（去掉豁免：EffectivePassThreshold 恒取配置值）→ 全量红 2（本测试、同批次内不中途切换）；其余全绿，黄金哈希不受影响。
        MatchFlow match = AiFixtures.Round5();
        Assert.True(match.Board.GroupsOf(Me).IsEmpty);

        HeuristicTurnController ai = LoneStones(match, 1, "E5");

        Placement placed = Assert.Single(ai.LastChoice!.Placements);
        Assert.Equal(TestMaps.At("E5"), placed.Coord);
        Assert.Equal(30, ai.LastChoice.Total);
        Assert.Equal("D:" + ai.LastChoice.Key, ai.Decisions[^1]);
    }

    [Fact]
    public void 有子后恢复阈值()
    {
        // 与「无子时不受阈值限制」同一局面，只多一枚远离 E5 的己方子 A1（不影响 E5 的任何一维）：提升 30 不大于配置阈值 80 → 撤回、Pass。
        // 本条在改动前即绿（描述的是原行为）；变异 M-F3（豁免条件反转：有子取 0、无子取配置值）→ 全量红 8（本测试、无子时不受阈值限制、同批次内不中途切换、
        // 低于阈值不落子、恰等于阈值不落子、无落点被保留则Pass、候选格上限黄金哈希、地形可离线重建）。
        MatchFlow match = AiFixtures.Round5().Stones(Me, "A1");
        Assert.False(match.Board.GroupsOf(Me).IsEmpty);

        HeuristicTurnController ai = LoneStones(match, 1, "E5");

        AssertPassed(ai);
    }

    [Fact]
    public void 同批次内不中途切换()
    {
        // 阈值 80，P0 批次开始前无子，部署上限 2、两个互不相邻也不共覆盖的候选格 B2 / E5（各自提升 30）。
        // 第一枚保留后批次里已有己方暂放子，但豁免只按正式盘面判定：第二枚仍按阈值 0 判定而保留，整批 60。
        // 变异 M-F2（按暂放后盘面判定：Greedy 里批次已有先前保留的暂放子就改用配置阈值）→ 全量红 1（本测试）；「无子时不受阈值限制」仍绿。
        MatchFlow match = AiFixtures.Round5();
        Assert.True(match.Board.GroupsOf(Me).IsEmpty);

        HeuristicTurnController ai = LoneStones(match, 2, "B2", "E5");

        Assert.Equal(1, ai.Config.CandidateBatchCount);
        Assert.Equal([TestMaps.At("B2"), TestMaps.At("E5")], ai.LastChoice!.Placements.Select(p => p.Coord).Order());
        Assert.Equal(60, ai.LastChoice.Total);
    }

    // ---------- 2.4：配置、回放、命令行 ----------

    [Fact]
    public void 阈值须为非负整数()
    {
        // 变异 M-B13（AiSearchConfig.Validated 不拒负阈值）→ 红 1（本测试）。
        Assert.Throws<ArgumentOutOfRangeException>(() => (AiSearchConfig.Standard with { PassThreshold = -1 }).Validated());
        Assert.Equal(0, (AiSearchConfig.Standard with { PassThreshold = 0 }).Validated().PassThreshold);
        Assert.Throws<ArgumentException>(() => (SimFixtures.Config() with { PassThreshold = -1 }).Validated());
    }

    [Fact]
    public void 跑局配置的停手阈值文本往返()
    {
        // 变异 M-B9：RunConfig.PassThreshold 标 [JsonIgnore] → 红 7（本测试、阈值进入记录、首部缺停手阈值的旧日志按0回放、命令行停手阈值写入配置记录与 SimulationHarness 3 条）。
        RunConfig unset = SimFixtures.Config();
        Assert.DoesNotContain("PassThreshold", unset.ToJson(), StringComparison.Ordinal);
        Assert.Null(RunConfig.FromJson(unset.ToJson()).PassThreshold);

        foreach (int t in new[] { 0, 7 })
        {
            RunConfig set = unset with { PassThreshold = t };
            Assert.Contains($"\"PassThreshold\": {t}", set.ToJson(), StringComparison.Ordinal);
            Assert.Equal(t, RunConfig.FromJson(set.ToJson()).PassThreshold);
        }
    }

    [Fact]
    public void 首部缺停手阈值的旧日志按0回放()
    {
        // 该项出现之前的日志首部没有 PassThreshold：当时的保留条件就是"严格提高"（= 阈值 0）。回放 MUST 按 0 重建，重建的首部也不得多出一项。
        // 样本：阈值 0 跑出的局去掉这一项。它与缺省阈值下的同一局走法不同（「阈值为0时零变化」的反面），所以按缺省重建必然分歧。
        // 变异 M-B10（按首部重建时缺字段取缺省阈值而不是 0）→ 红 1（本测试）。
        RunConfig config = SimFixtures.Config(seedStart: 31, turnLimit: 24, difficulty: AiDifficulty.Standard);
        MatchLog zero = BatchRunner.Execute(config with { PassThreshold = 0 }, parallelism: 1)[0];
        string text = zero.DeterministicText();
        Assert.Contains("\"PassThreshold\":0,", text, StringComparison.Ordinal);
        MatchLog old = MatchLog.Parse(text.Replace("\"PassThreshold\":0,", string.Empty, StringComparison.Ordinal));
        Assert.Null(old.Header.Config.PassThreshold);

        ReplayResult replay = Replayer.Replay(old);

        Assert.True(replay.Identical, replay.ToString());
        Assert.True(replay.LineCount >= 25, $"比对行数 {replay.LineCount}");
        Assert.Null(replay.Replayed.Header.Config.PassThreshold);

        // 会话层：新建的局取缺省阈值并写进首部配置；按首部重建的局缺字段取 0，有字段取记录值。
        Assert.Equal(AiSearchConfig.DefaultPassThreshold, MatchSession.Create(config, 1).PassThreshold);
        Assert.Equal(AiSearchConfig.DefaultPassThreshold, MatchSession.Create(config, 1).Config.PassThreshold);
        Assert.Equal(0, MatchSession.Create(config, 1, map: null, recorded: true).PassThreshold);
        Assert.Equal(7, MatchSession.Create(config with { PassThreshold = 7 }, 1, map: null, recorded: true).PassThreshold);
    }

    [Fact]
    public void 命令行停手阈值写入配置记录()
    {
        // 严格 CLI：--pass-threshold 被认领并写进 config.json 与日志首部；负值在跑局之前报错、不写任何输出。
        // 变异 M-B11：Program.Run 不读 --pass-threshold → 未识别选项报错、退出码非 0 → 红 1（本测试）。
        string outDir = Path.Combine(SimFixtures.TempDir("pass-threshold-cli"), "out");
        int code = Siege.Sim.Program.Main(
            ["run", "--out", outDir, "--seed", "1", "--count", "1", "--turn-limit", "4", "--difficulty", "Easy", "--serial", "--pass-threshold", "7"]);

        Assert.Equal(0, code);
        Assert.Contains("\"PassThreshold\": 7", File.ReadAllText(Path.Combine(outDir, "config.json")), StringComparison.Ordinal);
        Assert.Contains("\"PassThreshold\":7,", File.ReadLines(Directory.GetFiles(outDir, "match-*.jsonl").Single()).First(), StringComparison.Ordinal);

        string badDir = Path.Combine(SimFixtures.TempDir("pass-threshold-cli-bad"), "out");
        TextWriter saved = Console.Error;
        using var err = new StringWriter();
        int bad;
        try
        {
            Console.SetError(err);
            bad = Siege.Sim.Program.Main(["run", "--out", badDir, "--seed", "1", "--count", "1", "--serial", "--pass-threshold", "-1"]);
        }
        finally
        {
            Console.SetError(saved);
        }

        Assert.NotEqual(0, bad);
        Assert.False(Directory.Exists(badDir));
        Assert.Contains("停手阈值", err.ToString(), StringComparison.Ordinal);
    }
}
