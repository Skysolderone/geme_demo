using System.Reflection;
using System.Text.Json;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
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
        RunConfig config = SimFixtures.Config(count: 6, seedStart: 21, maxRounds: 3, retention: EventRetention.SnapshotsOnly) with { FullEventSamplePermille = 500 };
        string dir = SimFixtures.TempDir("batch");

        BatchSummary summary = BatchRunner.ExecuteToDirectory(config, dir, parallelism: 3);

        Assert.Equal((6, 6, 0), (summary.Count, summary.Completed, summary.Failed));
        Assert.Equal(6, summary.Reasons.Values.Sum());
        Assert.Equal(6, Directory.GetFiles(dir, "match-*.jsonl").Length);
        Assert.True(File.Exists(Path.Combine(dir, "summary.json")));
        RunConfig saved = RunConfig.FromJson(File.ReadAllText(Path.Combine(dir, "config.json")));
        // config.json 写实际生效配置——未显式配置的权重填默认表（旧期望 config.ToJson() 原样 → 新期望 Effective()）。
        Assert.Equal(config.Effective().ToJson(), saved.ToJson());
        Assert.All(saved.Players, p => Assert.Equal(Core.Ai.EvaluationWeights.Default, p.Weights));
        Assert.Equal((21UL, 6, 3, 500), (saved.SeedStart, saved.Count, saved.MaxMajorRounds, saved.FullEventSamplePermille));

        List<MatchLog> logs = MatchLog.ReadDirectory(dir);
        Assert.Equal(Enumerable.Range(21, 6).Select(i => (ulong)i), logs.Select(l => l.Seed));
        Assert.All(logs, l =>
        {
            Assert.NotNull(l.Result);
            Assert.Equal(config.ToJson(), l.Header.Config.ToJson());
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
        RunConfig config = SimFixtures.Config(count: 6, seedStart: 31, maxRounds: 3);

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
        // "小回合数截断值"是段 E 5.1 的新增项，本段尚未实现，不在此断言。
        // 变异验证 M-B10（段 B，实跑红 3）：MatchSession.Create 不把 RunConfig.ArtisanWeight 传入对局 → 本测试红（首部回到 10）。
        EvaluationWeights safety7 = EvaluationWeights.Default with { Safety = 7 };
        RunConfig config = SimFixtures.Config(count: 1, maxRounds: 1) with
        {
            ArtisanWeight = 18,
            Players = [.. Enumerable.Range(0, 4).Select(_ => new PlayerAiConfig { Difficulty = AiDifficulty.Easy, Weights = safety7 })],
        };
        string dir = SimFixtures.TempDir("sweep-config");

        BatchRunner.ExecuteToDirectory(config, dir, parallelism: 1);

        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "config.json")));
        Assert.Equal(18, json.RootElement.GetProperty("ArtisanWeight").GetInt32());
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
        MatchSession session = MatchSession.Create(SimFixtures.Config(maxRounds: 1), 5);
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
            ["run", "--out", outDir, "--seed", "1", "--count", "1", "--max-rounds", "2", "--difficulty", "Easy", "--serial"]);

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
}
