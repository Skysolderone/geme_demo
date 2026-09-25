using System.Reflection;
using System.Text.Json;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Sim.Analysis;
using Siege.Sim.Cli;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>规格：simulation-harness —— Requirement: 批量跑局</summary>
[Collection(ConsoleRedirect.Collection)]   // 与其他重定向 Console.Error 的测试类串行（见 ConsoleRedirect）
public class 批量跑局Tests
{
    [Fact]
    public void 批量执行并汇总()
    {
        // "200 个种子"的规模走 CLI（Siege.Sim run --count 200）；这里用 6 个种子、3 大回合验证机制：每局一个日志文件 + 配置 + 汇总。
        // 配置可完整序列化并随结果保存（implement 6.1）。
        // 变异验证：本类以 M-B16（见 并行不改变结果）为准，该变异同时使本测试红。
        // more-pieces-relics 段 A：「终局大回合在 1–3」「完整 / 仅快照各有」等断言依赖走法 → 写死内容集 v1（与引入内容集之前逐步相同）；
        // "未配置的内容集落成 v2 写进 config.json 与首部"由 默认评价权重的校准Tests.引用未校准维度产出的数据 与 对局内容集Tests.新局缺省v2 钉住。
        RunConfig config = SimFixtures.Config(count: 6, seedStart: 21, turnLimit: 12, retention: EventRetention.SnapshotsOnly) with { FullEventSamplePermille = 500, ContentSet = ContentSet.V1 };
        string dir = SimFixtures.TempDir("batch");

        BatchSummary summary = BatchRunner.ExecuteToDirectory(config, dir, parallelism: 3);

        Assert.Equal((6, 6, 0), (summary.Count, summary.Completed, summary.Failed));
        Assert.Equal(6, summary.Reasons.Values.Sum());
        Assert.Equal(6, Directory.GetFiles(dir, "match-*.jsonl").Length);
        Assert.True(File.Exists(Path.Combine(dir, "summary.json")));
        RunConfig saved = RunConfig.FromJson(File.ReadAllText(Path.Combine(dir, "config.json")));
        // config.json 写实际生效配置——未显式配置的权重填默认表（旧期望 config.ToJson() 原样 → 新期望 Effective()）。
        // ai-eye 段 B：未配置的停手阈值同样落成实际生效的缺省值写入（ai-decision「停手阈值」：实际生效的阈值 MUST 写入批次配置记录）。
        Assert.Null(config.PassThreshold);
        // flag-contest D2：未配置的冒险概率同样落成缺省值 15 写入（不落成就无法与"首部缺该项 = 旧日志 = 0"区分）。
        Assert.Null(config.FlagRisk);
        Assert.Equal((config with { PassThreshold = Core.Ai.AiSearchConfig.DefaultPassThreshold, FlagRisk = Core.Match.MatchOptions.DefaultFlagRisk }).Effective().ToJson(), saved.ToJson());
        Assert.All(saved.Players, p => Assert.Equal(Core.Ai.EvaluationWeights.Default, p.Weights));
        Assert.Equal((21UL, 6, 12, 500), (saved.SeedStart, saved.Count, saved.TurnLimit, saved.FullEventSamplePermille));   // 段 C：大回合上限 3 → 小回合数截断 12（= 3 × 4 人）

        List<MatchLog> logs = MatchLog.ReadDirectory(dir);
        Assert.Equal(Enumerable.Range(21, 6).Select(i => (ulong)i), logs.Select(l => l.Seed));
        Assert.All(logs, l =>
        {
            Assert.NotNull(l.Result);
            Assert.Equal((config with { PassThreshold = Core.Ai.AiSearchConfig.DefaultPassThreshold, FlagRisk = Core.Match.MatchOptions.DefaultFlagRisk }).ToJson(), l.Header.Config.ToJson());   // ai-eye 段 B / flag-contest D2：首部同样落成实际生效的阈值与冒险概率
            Assert.InRange(l.Result!.MajorRound, 1, 3);
        });

        // 按种子抽样的完整事件流（子流 sim-sample，与对局子流无关）：千分之 500 下 6 局里既有完整也有仅快照
        Assert.Contains(logs, l => l.Header.Retention == EventRetention.Full);
        Assert.Contains(logs, l => l.Header.Retention == EventRetention.SnapshotsOnly);
        Assert.All(logs.Where(l => l.Header.Retention == EventRetention.SnapshotsOnly), l => Assert.DoesNotContain(l.Events, e => LogEventType.IsFineGrained(e.Type)));
        Assert.Contains(logs.Where(l => l.Header.Retention == EventRetention.Full), l => l.Events.Any(e => e.Type == LogEventType.Candidates));
        Assert.Equal(logs.Select(l => l.Header.Retention), BatchRunner.Execute(config, parallelism: 1).Select(l => l.Header.Retention));

        // 压缩输出同样可读
        RunConfig gz = config with { Count = 1, Compress = true };
        string gzDir = SimFixtures.TempDir("batch-gz");
        BatchRunner.ExecuteToDirectory(gz, gzDir, parallelism: 1);
        string gzFile = Assert.Single(Directory.GetFiles(gzDir, "match-*.jsonl.gz"));
        // 首行 header 含 Count / Compress（本批不同），从第二行起逐字节一致
        Assert.Equal(logs[0].DeterministicText().Split('\n').Skip(1), MatchLog.Read(gzFile).DeterministicText().Split('\n').Skip(1));
        Assert.True(new FileInfo(gzFile).Length * 4 < new FileInfo(Path.Combine(dir, logs[0].FileName)).Length);
    }

    [Fact]
    public void 并行不改变结果()
    {
        // 同一批种子串行与并行各跑一次 → 每个种子的日志确定性文本逐字节一致（去掉耗时字段后）；决策序列也一致。
        // 变异验证 M-B16：MatchSession.Create 用 Interlocked 递增的静态计数器异或种子 → 红 5（本测试、批量执行并汇总、纯AI局可凭种子复现、失败局可复现、子流互不干扰）。
        // 变异验证 M-C5（check）：DeterministicText 只序列化 header → 原先两条文本断言恒真（header 含种子，逐字节相等与"不同种子不同"都成立）；
        // 补上行数下界与快照 / 事件逐条比对后 → 红 2（本测试、纯AI局可凭种子复现）。
        RunConfig config = SimFixtures.Config(count: 6, seedStart: 31, turnLimit: 12);

        List<MatchLog> serial = BatchRunner.Execute(config, parallelism: 1);
        List<MatchLog> parallel = BatchRunner.Execute(config, parallelism: 4);

        Assert.Equal(6, serial.Count);
        Assert.Equal(serial.Select(l => l.Seed), parallel.Select(l => l.Seed));
        for (int i = 0; i < serial.Count; i++)
        {
            string text = serial[i].DeterministicText();
            Assert.Equal(text, parallel[i].DeterministicText());
            // 确定性文本必须真的覆盖快照与事件：行数 > 快照数 + 事件数（header 与 result 各一行）
            Assert.True(serial[i].Turns.Count > 0 && serial[i].Events.Count > 0);
            Assert.True(text.Split('\n').Length > serial[i].Turns.Count + serial[i].Events.Count, $"确定性文本只有 {text.Split('\n').Length} 行");
            Assert.Equal(SimFixtures.TurnTexts(serial[i].Turns), SimFixtures.TurnTexts(parallel[i].Turns));
            Assert.Equal(serial[i].Events.Select(e => $"{e.Seq}:{e.Turn}:{e.Type}:{e.Player}:{e.Detail}"), parallel[i].Events.Select(e => $"{e.Seq}:{e.Turn}:{e.Type}:{e.Player}:{e.Detail}"));
            Assert.Equal(serial[i].Result!.Winners, parallel[i].Result!.Winners);
            Assert.Equal(serial[i].Result!.Standings.Select(s => $"{s.Rank}:{s.Player}:{s.Power}"), parallel[i].Result!.Standings.Select(s => $"{s.Rank}:{s.Player}:{s.Power}"));
        }

        Assert.True(serial.Select(l => l.DeterministicText()).Distinct().Count() == 6, "不同种子应产生不同对局");
    }

    // ---------- 既有守门延伸到 Siege.Sim 程序集 ----------

    [Fact]
    public void 扫档配置可追溯()
    {
        // 规格 Scenario：以匠人权重 18、全部玩家 Safety = 7 执行一批对局 → 配置记录写明匠人权重 18 与四名玩家的完整权重。
        // 读 config.json 原文（不经 RunConfig 反序列化，避免缺省值把漏写掩盖掉）；日志首部的匠人权重取自对局本身。
        // restore-go-core-rules 段 B：原断言里的"据点分值 3 / 8 / 24"随据点摘除删去（规格 Scenario 同步改写）；
        // 段 E 5.1：规格 Scenario 要求配置记录写明"小回合数截断值"——config.json 原文与日志首部都要有（取非缺省值 4，缺省 600 抓不到漏写）。
        // 变异验证 M-E11：RunConfig.TurnLimit 加 [JsonIgnore]（配置记录漏写截断值） → 红 ≥ 10（本测试 + 回放类：回放按缺省 600 重跑、极慢，第 40 分钟终止时已跑 1181 条中红 10）。
        // 变异验证 M-B10（段 B，实跑红 3）：MatchSession.Create 不把 RunConfig.ArtisanWeight 传入对局 → 本测试红（首部回到 10）。
        EvaluationWeights safety7 = EvaluationWeights.Default with { Safety = 7 };
        RunConfig config = SimFixtures.Config(count: 1, turnLimit: 4) with
        {
            ArtisanWeight = 18,
            Players = [.. Enumerable.Range(0, 4).Select(_ => new PlayerAiConfig { Difficulty = AiDifficulty.Easy, Weights = safety7 })],
        };
        string dir = SimFixtures.TempDir("sweep-config");

        BatchRunner.ExecuteToDirectory(config, dir, parallelism: 1);

        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "config.json")));
        Assert.Equal(18, json.RootElement.GetProperty("ArtisanWeight").GetInt32());
        Assert.Equal(4, json.RootElement.GetProperty("TurnLimit").GetInt32());
        JsonElement[] players = [.. json.RootElement.GetProperty("Players").EnumerateArray()];
        Assert.Equal(4, players.Length);
        Assert.All(players, p =>
        {
            JsonElement w = p.GetProperty("Weights");
            Assert.Equal(
                (10, 8, 6, 7, 4, 20, 2),
                (w.GetProperty("PowerGain").GetInt32(), w.GetProperty("EnemyLoss").GetInt32(), w.GetProperty("Relic").GetInt32(), w.GetProperty("Safety").GetInt32(),
                 w.GetProperty("Growth").GetInt32(), w.GetProperty("Initiative").GetInt32(), w.GetProperty("Supply").GetInt32()));
        });

        MatchLog log = Assert.Single(MatchLog.ReadDirectory(dir));
        Assert.Equal(18, log.Header.ArtisanWeight);
        Assert.Equal(4, log.Header.Config.TurnLimit);

        // 反面：地图数据不含据点——写出的地图文本里没有该字段（段 B 守门）。
        Assert.DoesNotContain("\"Sites\"", MapFile.ToJson(MapCatalog.Resolve(log.Header.MapId)), StringComparison.Ordinal);
    }

    [Fact]
    public void Sim程序集不引用Godot()
    {
        // boundaries.md：Siege.Sim MUST NOT 依赖 Godot。看落进程序集的实际引用。
        var referenced = typeof(MatchSession).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referenced, r => r.Name is not null && r.Name.StartsWith("Godot", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(referenced, r => r.Name == "Siege.Core");
    }

    [Fact]
    public void Sim源码不含不受控随机与时间且浮点只在分析层()
    {
        // determinism.md：随机只来自 GameSeed.Stream；System.Random / Guid / DateTime 不得参与任何决定结果的路径（墙钟只能记录，走 Stopwatch）。
        // 裁决 14：double / float 只允许出现在 Analysis/ 目录。
        // 源码扫描守门与 Determinism/随机子流隔离Tests 的 M-D5 同构（在 Relics/ 加 `double` 即红），本条未单独做变异。
        string root = Path.Combine(Determinism.随机子流隔离Tests.SourceRoot(), "src", "Siege.Sim");
        string[] files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")).ToArray();
        Assert.NotEmpty(files);
        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            foreach (string token in new[] { "System.Random", "Random.Shared", "new Random", "DateTime", "Guid", "Environment.TickCount" })
            {
                Assert.False(text.Contains(token, StringComparison.Ordinal), $"{Path.GetRelativePath(root, file)} 含 {token}");
            }

            if (!file.Contains($"{Path.DirectorySeparatorChar}Analysis{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                foreach (string token in new[] { "double", "float", "decimal", "Math.Round", "Math.Floor", "Math.Ceiling" })
                {
                    Assert.False(text.Contains(token, StringComparison.Ordinal), $"{Path.GetRelativePath(root, file)} 含 {token}（浮点只允许在 Analysis/）");
                }
            }
        }
    }

    [Fact]
    public void 未知玩家一律抛SiegeRuleException()
    {
        // boundaries.md「未知玩家必须响亮失败」延伸到会话层：替换控制者 / 接管 / 交还一个不在名单的玩家都抛 SiegeRuleException。
        MatchSession session = MatchSession.Create(SimFixtures.Config(turnLimit: 4), 5);
        var stranger = new PlayerId(9);
        Assert.Throws<SiegeRuleException>(() => session.SetController(stranger, new BlindController()));
        Assert.Throws<SiegeRuleException>(() => session.TakeOver(stranger, new BlindController()));
        Assert.Throws<SiegeRuleException>(() => session.HandBack(stranger));
        Assert.Throws<SiegeRuleException>(() => session.AiOf(stranger));
        Assert.NotNull(session.AiOf(new PlayerId(0)));
        Assert.Equal(AiDifficulty.Easy, session.AiOf(new PlayerId(3))!.Difficulty);

        // 人数与地图不符也响亮失败
        Assert.ThrowsAny<ArgumentException>(() => MatchSession.Create(SimFixtures.Config(players: 5), 5));
        Assert.Throws<ArgumentException>(() => SimFixtures.Config(players: 1).Validated());
    }

    // ---------- strict-cli：命令行 MUST NOT 静默忽略无法识别的选项 ----------

    [Fact]
    public void 未知选项被拒绝()
    {
        // 规格算例：以 --matches 200 调用跑局子命令，而它认识的是 --count（strict-cli tasks 1.1）。
        // 注意 EditDistance("matches", "count") = 7 > 3，够不着建议阈值，--count 是经"合法选项全表"报出的。
        // 变异 M-SC1：删掉 Run 里的 cli.EnsureRecognized() → 本测试红。
        string outDir = Path.Combine(SimFixtures.TempDir("strictcli-unknown"), "out");
        var err = new StringWriter();
        TextWriter saved = Console.Error;
        int code;
        try
        {
            Console.SetError(err);
            code = Siege.Sim.Program.Main(["run", "--out", outDir, "--matches", "200", "--seed", "1"]);
        }
        finally
        {
            Console.SetError(saved);
        }

        Assert.NotEqual(0, code);
        Assert.False(Directory.Exists(outDir));   // D4：报错时不得写出任何输出
        Assert.Contains("--matches", err.ToString(), StringComparison.Ordinal);
        Assert.Contains("--count", err.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void 拼错的选项不被当作缺省值()
    {
        // --cout 3 是 --count 的拼错：必须报错退出，MUST NOT 回退到 Count 缺省值 1 继续跑（strict-cli tasks 1.2）。
        // 同时钉住建议的确定性：--cout 到 --count 与到 --out 的编辑距离都是 1，并列取字典序靠前的 count。
        // 变异 M-SC2：NearestOption 永远返回 null → "是否想用 --count" 消失，本测试红（合法选项全表里仍有 --count，所以断言必须落在建议位置上）。
        string outDir = Path.Combine(SimFixtures.TempDir("strictcli-typo"), "out");
        var cli = new CommandLine(["--out", outDir, "--cout", "3", "--seed", "1"]);
        _ = cli.GetOrNull("out");
        _ = cli.GetOrNull("config");
        _ = cli.GetInt("count", 1);
        _ = cli.GetUInt64("seed", 0);
        ArgumentException ex = Assert.ThrowsAny<ArgumentException>(() => cli.EnsureRecognized());
        Assert.Contains("是否想用 --count", ex.Message, StringComparison.Ordinal);

        var err = new StringWriter();
        TextWriter saved = Console.Error;
        int code;
        try
        {
            Console.SetError(err);
            code = Siege.Sim.Program.Main(["run", "--out", outDir, "--cout", "3", "--seed", "1"]);
        }
        finally
        {
            Console.SetError(saved);
        }

        Assert.NotEqual(0, code);
        Assert.False(Directory.Exists(outDir));
        Assert.Contains("是否想用 --count", err.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void 全部选项被读取时正常执行()
    {
        // 挡"结算过严把正常调用也拒了"：含只被 Flag 读取的 --serial（strict-cli tasks 1.3）。
        // 变异 M-SC3：UnrecognizedOptions 把已消费的 key 也算作未知 → 本测试红。
        string outDir = Path.Combine(SimFixtures.TempDir("strictcli-ok"), "out");

        int code = Siege.Sim.Program.Main(
            ["run", "--out", outDir, "--seed", "1", "--count", "1", "--turn-limit", "8", "--difficulty", "Easy", "--serial"]);

        Assert.Equal(0, code);
        Assert.True(File.Exists(Path.Combine(outDir, "summary.json")));
        Assert.Single(Directory.GetFiles(outDir, "match-*.jsonl"));
    }

    [Fact]
    public void replay两种定位方式不得混用()
    {
        // design D2：--file 与 --dir/--seed 混用时，后者会被静默忽略——这正是 strict-cli 要消灭的失败模式，
        // 因此 replay 直接报错，而不是把 dir/seed 声明成可选项放行。
        // 变异：去掉 Program.Replay 里的 Has("dir") || Has("seed") 检查 → 本测试红。
        // 判据是 `A || B` 形状，两侧各要一个只触发自己的用例（testing.md）：
        //   --file + --seed 只触发右侧，--file + --dir 只触发左侧。变异 M-CK4（去掉 Has("dir") ||）靠后者才红。
        TextWriter saved = Console.Error;
        foreach (string[] args in new[]
        {
            new[] { "replay", "--file", "x.jsonl", "--seed", "7" },
            ["replay", "--file", "x.jsonl", "--dir", "d"],
        })
        {
            var err = new StringWriter();
            int code;
            try
            {
                Console.SetError(err);
                code = Siege.Sim.Program.Main(args);
            }
            finally
            {
                Console.SetError(saved);
            }

            Assert.NotEqual(0, code);
            Assert.Contains("不能混用", err.ToString(), StringComparison.Ordinal);
        }

        // 反面：只给 --dir + --seed 时不因这条检查被拒——必须确实走进 else 分支（报的是找不到日志），
        // 而不是碰巧被别的错误拦下也算"没说混用"。
        var err2 = new StringWriter();
        try
        {
            Console.SetError(err2);
            Siege.Sim.Program.Main(["replay", "--dir", SimFixtures.TempDir("strictcli-replay"), "--seed", "7"]);
        }
        finally
        {
            Console.SetError(saved);
        }

        Assert.DoesNotContain("不能混用", err2.ToString(), StringComparison.Ordinal);
        Assert.Contains("没有种子", err2.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void 开关选项计入消费()
    {
        // Has / Flag 在条件读取里就是该选项的读取方，必须计入消费（design.md D1，strict-cli tasks 1.4）。
        // 变异 M-SC4：Has 不记录消费（改成直接查字典）→ 本测试红。
        var byHas = new CommandLine(["--screenshot"]);
        Assert.True(byHas.Has("screenshot"));
        Assert.Empty(byHas.UnrecognizedOptions());
        byHas.EnsureRecognized();

        var byFlag = new CommandLine(["--serial"]);
        Assert.True(byFlag.Flag("serial"));
        byFlag.EnsureRecognized();

        // 反面：同样的输入，没有任何读取方取用时必须判为未知——否则上面两条恒真
        var untouched = new CommandLine(["--screenshot"]);
        Assert.Equal(new[] { "screenshot" }, untouched.UnrecognizedOptions());
        Assert.ThrowsAny<ArgumentException>(() => untouched.EnsureRecognized());
    }

    [Fact]
    public void 未知选项按字典序报出()
    {
        // 错误信息要可复现：多个未知选项按字典序（Ordinal）稳定排序（strict-cli tasks 2.2）。
        // 变异 M-SC5：UnrecognizedOptions 去掉 .Order(StringComparer.Ordinal) → 本测试红（字典插入序是 zeta、alpha）。
        var cli = new CommandLine(["--zeta", "1", "--alpha", "2", "--count", "3"]);
        Assert.Equal(3, cli.GetInt("count", 0));

        Assert.Equal(new[] { "alpha", "zeta" }, cli.UnrecognizedOptions());
        ArgumentException ex = Assert.ThrowsAny<ArgumentException>(() => cli.EnsureRecognized());
        Assert.True(
            ex.Message.IndexOf("--alpha", StringComparison.Ordinal) is int a and >= 0
            && ex.Message.IndexOf("--zeta", StringComparison.Ordinal) is int z and >= 0
            && a < z,
            ex.Message);

        // 零白名单：合法集合只来自读取动作，没有"声明但未读取"的放行口子（design D1）。
        // 只读了 file 的调用里，--seed 必然算未知——replay 之所以能报更准的"不能混用"，
        // 是因为它自己先检查了互斥，而不是因为 CommandLine 给它开了口子（design D2）。
        var onlyFile = new CommandLine(["--file", "x.jsonl", "--seed", "7"]);
        Assert.Equal("x.jsonl", onlyFile.GetOrNull("file"));
        Assert.Equal(new[] { "seed" }, onlyFile.UnrecognizedOptions());
    }

    [Fact]
    public void analyze也在写出报告之前结算()
    {
        // spec 写的是"任一子命令"，不止 run：analyze 的结算必须先于读日志与写 report.txt（strict-cli tasks 2.4 / design D4）。
        // 变异 M-CK2：删掉 Analyze 里的 cli.EnsureRecognized() → 本测试红。
        string dir = SimFixtures.TempDir("strictcli-analyze");
        var err = new StringWriter();
        TextWriter saved = Console.Error;
        int code;
        try
        {
            Console.SetError(err);
            code = Siege.Sim.Program.Main(["analyze", "--dir", dir, "--oot", "x"]);
        }
        finally
        {
            Console.SetError(saved);
        }

        Assert.NotEqual(0, code);
        Assert.Contains("未知选项 --oot", err.ToString(), StringComparison.Ordinal);
        Assert.Contains("是否想用 --out", err.ToString(), StringComparison.Ordinal);   // 建议路径在真实 CLI 上生效
        Assert.False(File.Exists(Path.Combine(dir, "report.txt")));
    }

    // ---------- restore-go-core-rules 段 E（tasks 5.1）：小回合数截断与已删除的配置项 ----------

    [Fact]
    public void 不收敛对局被截断()
    {
        // 规格 Scenario：截断为 600、第 600 个小回合仍未满足任何终局条件 → 该局以 turn_limit 结束，不产生名次与胜者。
        // 真跑 600 个小回合要几分钟（段 C 实测 1000 小回合 226 s），这里用同一机制的小截断值 10（Easy 4 人在 10 个小回合内不会满足规则终局），
        // 另钉缺省值 600 这条配置口径；"0 = 不截断"由 防死锁硬停Tests 钉住。
        // 变异验证 M-E7：MatchSession.RunTurn 的截断判据 `_turn >= Config.TurnLimit` 改成 `>` → 实跑红 5（本测试、截断可复现、批量执行并汇总、日志覆盖八类记录、候选格上限黄金哈希——都多跑一个小回合）。
        Assert.Equal(600, RunConfig.DefaultTurnLimit);
        Assert.Equal(600, new RunConfig().TurnLimit);

        MatchSession session = MatchSession.Create(SimFixtures.Config(turnLimit: 10), 41);
        MatchLog log = MatchLog.Parse(session.Run().FullText());

        Assert.False(log.IsFailed);
        Assert.Equal(LogResult.TurnLimitReason, log.Result!.Reason);
        Assert.True(log.Result.Truncated);
        Assert.Equal((10, 10), (log.Result.TurnCount, log.Turns.Count));
        Assert.Empty(log.Result.Standings);
        Assert.Empty(log.Result.Winners);
        Assert.Equal(10, log.Header.Config.TurnLimit);

        // 截断不是规则终局：对局本身仍在进行中、规则层没有结果（Core 不知道截断）。
        Assert.Equal(Core.Match.MatchPhase.InProgress, session.Match.Phase);
        Assert.Null(session.Match.Result);
    }

    [Fact]
    public void 截断局不污染胜率()
    {
        // 规格 Scenario：200 局中 12 局以 turn_limit 结束 → 汇总单列"截断 12 局（6%）"，领先者胜率等指标只基于其余 188 局计算并注明样本数。
        // 188 局 = 有名次的真实样本（SimFixtures.RankedSample）克隆；12 局 = 真实截断样本（SimFixtures.Sample）克隆。
        // 截断样本同样有第 3 大回合的领先者：不排除的话领先者样本就是 200——它就是 testing.md 要求的"被排除的样本"。
        // 变异验证 M-E3：BalanceAnalyzer.Analyze 把领先者段的输入由"有名次的局"改回全部纳入局 → 实跑红 2（本测试、领先者胜率回归）。
        List<MatchLog> ranked = SimFixtures.RankedSample.Value;
        List<MatchLog> truncated = SimFixtures.Sample.Value;
        Assert.All(ranked, l => Assert.NotEmpty(l.Result!.Winners));
        Assert.All(truncated, l => Assert.True(l.Result!.Truncated));
        Assert.All(truncated, l => Assert.NotEmpty(BalanceAnalyzer.LeadersAtRound3(l)));

        List<MatchLog> batch =
        [
            .. Enumerable.Range(0, 188).Select(i => SimFixtures.Clone(ranked[i % ranked.Count], seed: 2000UL + (ulong)i)),
            .. Enumerable.Range(0, 12).Select(i => SimFixtures.Clone(truncated[i % truncated.Count], seed: 3000UL + (ulong)i)),
        ];
        BalanceReport report = BalanceAnalyzer.Analyze(batch);

        Assert.Equal((200, 200), (report.TotalLogs, report.Included));
        Assert.Equal((12, 188), (report.Ending.Truncated, report.Ending.Ranked));
        Assert.Equal((12, 200), (report.Ending.TruncatedRate.Successes, report.Ending.TruncatedRate.Trials));
        Assert.Equal(188, report.Leader.Samples);
        int expectedWins = Enumerable.Range(0, 188).Count(i => LeaderWon(ranked[i % ranked.Count]));
        Assert.Equal((expectedWins, 188), (report.Leader.AnyOfGroupWins.Successes, report.Leader.AnyOfGroupWins.Trials));

        string text = ReportWriter.Render(report);
        Assert.Contains("截断（turn_limit）12 局（6.0%）", text);
        Assert.Contains("胜率 / 名次类指标排除截断局 12 局，有效样本 188 局", text);
        Assert.Contains("样本 188 局", text);

        // 批次汇总（summary.json）同样单列截断局数，且截断局计入该批次的局数
        BatchSummary summary = BatchRunner.Summarize(batch, parallelism: 1, wallClockMs: 0);
        Assert.Equal((200, 12), (summary.Count, summary.Truncated));
    }

    /// <summary>测试内独立判定（不回调 <see cref="BalanceAnalyzer.LeadersAtRound3"/>）：第 3 大回合结束时名次为 1 的玩家中有人获胜。</summary>
    internal static bool LeaderWon(MatchLog log)
    {
        LogEvent third = log.Events.Single(e => e.Type == LogEventType.MajorRoundEnded && e.MajorRound == 3);
        return log.Header.Players.Any(p => third.Values![$"P{p}.Rank"] == 1 && log.Result!.Winners.Contains(p));
    }

    [Fact]
    public void 截断可复现()
    {
        // 规格 Scenario：相同种子、相同配置与相同截断值重跑一局被截断的对局 → 在同一个小回合被截断，截断时的盘面完全一致。
        // 逐字节比对要钉住覆盖范围（testing.md）：快照条数 = 截断值、末条快照盘面有子；另逐条比对事件。
        // 反面：换一个更小的截断值，前面的小回合逐条相同——截断只是停止驱动，不改变走法。
        RunConfig config = SimFixtures.Config(turnLimit: 14);
        MatchLog a = MatchSession.Create(config, 43).Run();
        MatchLog b = MatchSession.Create(config, 43).Run();

        Assert.True(a.Result!.Truncated && b.Result!.Truncated);
        Assert.Equal((14, 14, 14), (a.Result.TurnCount, b.Result.TurnCount, a.Turns.Count));
        Assert.True(a.Turns[^1].PlayersState.Sum(p => p.Groups.Sum(g => g.Stones.Count)) > 0, "截断时盘面上应有棋子");
        Assert.Equal(SimFixtures.TurnTexts(a.Turns), SimFixtures.TurnTexts(b.Turns));
        Assert.Equal(
            a.Events.Select(e => $"{e.Seq}:{e.Turn}:{e.Type}:{e.Player}:{e.Detail}"),
            b.Events.Select(e => $"{e.Seq}:{e.Turn}:{e.Type}:{e.Player}:{e.Detail}"));
        Assert.Equal(a.DeterministicText(), b.DeterministicText());

        MatchLog shorter = MatchSession.Create(SimFixtures.Config(turnLimit: 12), 43).Run();
        Assert.Equal(12, shorter.Result!.TurnCount);
        Assert.Equal(SimFixtures.TurnTexts(a.Turns.Take(12)), SimFixtures.TurnTexts(shorter.Turns));
    }

    [Fact]
    public void 截断只存在于跑局驱动循环()
    {
        // 规格：截断 SHALL 只存在于批量跑局的驱动循环，MUST NOT 进入对局规则流程（design D5：Siege.Core 不知道它）。
        // 守门：Siege.Core 源码不出现截断相关符号。样本口径下界：扫到的 Core 源文件 ≥ 80；反面：Sim 的 MatchSession.cs 确实命中同一正则。
        // 正则不以词边界开头（testing.md：复合标识符如 DefaultTurnLimit / IsTruncated 也要抓到）。
        // 变异验证 M-E10：往 Siege.Core/Match/MatchFlow.cs 注入 `internal const int TurnLimit = 0;` → 实跑红 1（本测试）。
        var pattern = new System.Text.RegularExpressions.Regex("(?i)turn_?limit|truncat");
        string src = Path.Combine(Determinism.随机子流隔离Tests.SourceRoot(), "src");
        string[] core = [.. Directory.GetFiles(Path.Combine(src, "Siege.Core"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))];

        Assert.True(core.Length >= 80, $"只扫到 {core.Length} 个 Siege.Core 源文件");
        Assert.All(core, f => Assert.False(pattern.IsMatch(File.ReadAllText(f)), $"{Path.GetFileName(f)} 出现截断相关符号：截断只属于 Siege.Sim 的跑局驱动循环"));
        Assert.Matches(pattern, File.ReadAllText(Path.Combine(src, "Siege.Sim", "Running", "MatchSession.cs")));
    }

    [Theory]
    [InlineData("max-rounds", "6")]
    [InlineData("dominance-start", "7")]
    [InlineData("no-catch-up", null)]
    [InlineData("site-values", "3,8,24")]
    public void 已删除的选项报已删除(string option, string? value)
    {
        // tasks 5.1：RunConfig 去掉据点分值、大回合上限、碾压、补偿选项；严格 CLI 下传入旧选项 MUST 报错并说明已删除——
        // 不是笼统的"未知选项"（那会让人以为拼错了、去找相近的选项），更不能静默忽略。报错先于创建输出目录。
        // 变异验证 M-E1：CommandLine.EnsureRecognized 不查已删除选项表（退回"未知选项"） → 实跑红 4（本 Theory 四行）。
        string outDir = Path.Combine(SimFixtures.TempDir($"retired-{option}"), "out");
        string[] args = value is null
            ? ["run", "--out", outDir, "--seed", "1", $"--{option}"]
            : ["run", "--out", outDir, "--seed", "1", $"--{option}", value];
        var err = new StringWriter();
        TextWriter saved = Console.Error;
        int code;
        try
        {
            Console.SetError(err);
            code = Siege.Sim.Program.Main(args);
        }
        finally
        {
            Console.SetError(saved);
        }

        Assert.Equal(1, code);
        Assert.Contains($"--{option} 已删除", err.ToString(), StringComparison.Ordinal);
        Assert.Contains("restore-go-core-rules", err.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("未知选项", err.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(outDir));

        // 同一张表对任一子命令生效（play 的 --max-rounds 在段 C 删除）：直接对解析器断言，不起终端对局。
        var cli = new CommandLine(["--seed", "1", $"--{option}"]);
        _ = cli.GetOrNull("seed");
        ArgumentException ex = Assert.ThrowsAny<ArgumentException>(() => cli.EnsureRecognized());
        Assert.Contains($"--{option} 已删除", ex.Message, StringComparison.Ordinal);

        // 反面：真正的未知选项仍按"未知选项"报出（已删除表不能吞掉一般的拼错）。
        var typo = new CommandLine(["--seed", "1", "--max-round", "3"]);
        _ = typo.GetOrNull("seed");
        Assert.Contains("未知选项 --max-round", Assert.ThrowsAny<ArgumentException>(() => typo.EnsureRecognized()).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SiteValues", "{\"Low\":3,\"Mid\":8,\"High\":24}")]
    [InlineData("MaxMajorRounds", "15")]
    [InlineData("DominanceStartRound", "7")]
    [InlineData("CatchUpRecruit", "true")]
    [InlineData("maxMajorRounds", "15")]
    public void 配置文件里的已删除配置项被拒绝(string key, string value)
    {
        // tasks 5.1：配置文件（run --config）里的旧键 MUST 报错并说明已删除——静默忽略会让一份旧扫档配置"跑通了但参数没生效"（testing.md）。
        // 与 MapFile / 存档的废弃字段口径一致：大小写不敏感、点名字段；其余未知键仍宽容（反面）。旧日志首部里的配置不经 FromJson，照常可读。
        // 变异验证 M-E2：RunConfig.FromJson 不查已删除键（静默忽略） → 实跑红 5（本 Theory 五行）。
        FormatException ex = Assert.Throws<FormatException>(() => RunConfig.FromJson($"{{\"Count\": 2, \"{key}\": {value}}}"));
        Assert.Contains(key, ex.Message, StringComparison.Ordinal);
        Assert.Contains("已删除", ex.Message, StringComparison.Ordinal);

        Assert.Equal(2, RunConfig.FromJson("{\"Count\": 2, \"_comment\": \"说明\"}").Count);
        Assert.DoesNotContain(key, new RunConfig().ToJson(), StringComparison.OrdinalIgnoreCase);

        // 走 CLI：报错退出码 1、不创建输出目录。
        string dir = SimFixtures.TempDir($"retired-config-{key}");
        string file = Path.Combine(dir, "config.json");
        File.WriteAllText(file, $"{{\"Count\": 2, \"{key}\": {value}}}");
        string outDir = Path.Combine(dir, "out");
        var err = new StringWriter();
        TextWriter saved = Console.Error;
        int code;
        try
        {
            Console.SetError(err);
            code = Siege.Sim.Program.Main(["run", "--out", outDir, "--config", file]);
        }
        finally
        {
            Console.SetError(saved);
        }

        Assert.Equal(1, code);
        Assert.Contains("已删除", err.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(outDir));
    }
}
