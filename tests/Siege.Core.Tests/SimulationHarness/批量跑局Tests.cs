using System.Reflection;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>规格：simulation-harness —— Requirement: 批量跑局</summary>
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
        Assert.Equal(config.ToJson(), saved.ToJson());
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
}
