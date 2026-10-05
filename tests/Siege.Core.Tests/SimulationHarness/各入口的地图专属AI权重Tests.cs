using System.Security.Cryptography;
using System.Text;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Play;
using Siege.Sim.Running;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>规格：simulation-harness —— Requirement: 各入口的地图专属 AI 权重（v2-recalibration 段 B，design D5 / D6）</summary>
[Collection(ConsoleRedirect.Collection)]   // 「重建玩家列表后按覆盖取值」经 Program.Main 重定向 Console.Error
public class 各入口的地图专属AI权重Tests
{
    [Theory]
    // 黄金值取自引入本机制之前的实现（v2-recalibration 段 B 2.1，提交 925af6f 之上、src/ 未改动时实跑）：
    // 同一份未显式配置权重的配置在未登记覆盖的地图上跑种子 1 的一局，日志首部与批次 config.json 的 SHA-256。
    [InlineData("siege-4p-base-v5", 4, "34C451BCF092C3CCAFDDE9A6C1B1FDF7DA6299532B009E76530E8E99EA0D61CE", "3BF58B4CE6CED42B7DCEAB9B5C8C6AAEA47790446684299BCC6C0F8DD95EF1EE")]
    [InlineData("siege-3p-base-v1", 3, "F8CD1029BA7D4BEFFFE502A4E5DC4BA2B1B8990415BA82925DE0FFE627A6F7C0", "7FB1FA1E455C45E771AF708134BC919C959DB93C5492BEA80AC4FFAFCA650B0C")]
    [InlineData("siege-frontier-v2", 4, "A239B7FD8549E784EE1223F575C678596CD30B623805C9E72E4EB37DDEC53D7A", "3CCE95C7127E98F7C9FF8F1B1CB0C6BDC6061D1CA57C68ED03F7AE76CF6D4E16")]
    [InlineData("gen:12345", 4, "E167132EDC2BCE5D1CF3D7099B4DE9DFD2FD7444B27C462AC2165B62180E83AB", "AEF5452CF232262658666587791310C2AFA8424B1353A5331916A756AC879CBF")]
    public void 未登记的地图首部逐字节不变(string mapId, int players, string headerHash, string configHash)
    {
        // formation-tiers D2：黄金值钉在引入计分规则版本之前——显式跑 v1，首部与 config.json 比当时只多配置里的一项 "ScoringVersion":"V1"，
        // 去掉这一项（StripScoringV1 先断言它恰好出现一次）后与黄金值逐字节比；黄金值不重录。
        (string header, string config, _) = RunOneRaw(
            $"unregistered-{mapId.Replace(':', '_')}", BlankConfig(mapId, players) with { ScoringVersion = Core.Scoring.ScoringVersion.V1 });
        header = SimFixtures.StripScoringV1(header);
        config = SimFixtures.StripScoringV1(config);

        Assert.Equal(headerHash, Sha256(header));
        Assert.Equal(configHash, Sha256(config));
        // 各玩家首部权重均为空：序列化省略空值，首部配置里不出现 Weights 键（config.json 另经 Effective() 填默认表，属既有口径）。
        Assert.DoesNotContain("\"Weights\"", header, StringComparison.Ordinal);
        Assert.True(header.Length > 1000, $"首部只有 {header.Length} 字符");   // 覆盖范围下界：砍成空行时哈希比对仍可能被误钉
    }

    [Fact]
    public void 批量跑局在两人图上落成覆盖()
    {
        // 规格 Scenario「批量跑局在 2 人图上落成覆盖」：未显式配置权重 → 批次配置记录与每局日志首部里两名玩家的权重都等于 2 人图覆盖表。
        // 占位期覆盖值等于默认表，config.json 经 Effective() 本来就填默认表、分辨不出；首部未落成时权重为空（省略 Weights 键），以此分辨。
        EvaluationWeights expected = EvaluationWeights.MapOverrides[TwoPlayerBaseMap.Id];
        (string header, string config, MatchLog log) = RunOneRaw("2p-blank", BlankConfig(TwoPlayerBaseMap.Id, 2));

        Assert.All(RunConfig.FromJson(config).Players, p => Assert.Equal(expected, p.Weights));
        Assert.Equal(TwoPlayerBaseMap.Id, log.Header.MapId);
        Assert.Equal(2, log.Header.Config.Players.Count);
        Assert.All(log.Header.Config.Players, p => Assert.Equal(expected, p.Weights));
        Assert.Contains("\"Weights\"", header, StringComparison.Ordinal);
    }

    [Fact]
    public void 显式权重不被覆盖替换()
    {
        // 规格 Scenario：2 人图上两名玩家显式写了 Eye = 100 的整表权重 → 批次配置记录与日志首部记录的是这张显式表，不是覆盖表。
        EvaluationWeights explicitTable = EvaluationWeights.Default with { Eye = 100 };
        RunConfig config = BlankConfig(TwoPlayerBaseMap.Id, 2) with
        {
            Players = [.. Enumerable.Range(0, 2).Select(_ => new PlayerAiConfig { Difficulty = AiDifficulty.Standard, Weights = explicitTable })],
        };
        (_, string saved, MatchLog log) = RunOneRaw("2p-explicit", config);

        Assert.All(RunConfig.FromJson(saved).Players, p => Assert.Equal(explicitTable, p.Weights));
        Assert.All(log.Header.Config.Players, p => Assert.Equal(explicitTable, p.Weights));
        Assert.NotEqual(EvaluationWeights.ForMapId(TwoPlayerBaseMap.Id), explicitTable);

        // 与默认表不同的登记表（接缝）下同样原样保留：落成不逐维合并。
        RunConfig resolved = config.ResolvedFor(TwoPlayerBaseMap.Create(), AiDecision.地图专属评价权重覆盖Tests.Probe);
        Assert.All(resolved.Players, p => Assert.Same(explicitTable, p.Weights));
    }

    [Fact]
    public void 新日志按首部回放()
    {
        // 规格 Scenario：回放一局 2 人图上按覆盖落成权重的新日志 → 每一步决策与原局相同，重建出的首部与原首部逐字节相同。
        MapData map = TwoPlayerBaseMap.Create();
        MatchLog original = MatchSession.Create(BlankConfig(map.Id, 2) with { TurnLimit = 16 }, 3, map).Run();
        Assert.All(original.Header.Config.Players, p => Assert.Equal(EvaluationWeights.MapOverrides[map.Id], p.Weights));
        Assert.True(original.Turns.Count >= 8, $"样本只有 {original.Turns.Count} 个小回合");
        Assert.Contains(original.Turns, t => !t.Passed);   // 样本口径：AI 确实落过子

        ReplayResult replay = Replayer.Replay(SimFixtures.Clone(original));
        Assert.True(replay.Identical, $"第 {replay.FirstDivergentLine} 行分歧：{replay.Expected} ≠ {replay.Actual}");
        Assert.Equal(original.DeterministicText().Split('\n')[0], replay.Replayed.DeterministicText().Split('\n')[0]);
        Assert.Equal(SimFixtures.TurnTexts(original.Turns), SimFixtures.TurnTexts(replay.Replayed.Turns));
    }

    [Fact]
    public void 旧日志不套覆盖()
    {
        // 规格 Scenario：回放一局引入本机制之前在 2 人图上产出、首部权重为空的日志 → AI 按默认表重建，每一步与原局相同。
        // 夹具由测试现场生成（tasks 2.1）：按首部重建的路径（recorded）不落成任何缺省，于是首部与引入本机制之前的 b01 同形——
        // 阈值 80、内容集 V2、冒险概率与带入显式写出，权重为空。
        MapData map = TwoPlayerBaseMap.Create();
        RunConfig legacy = BlankConfig(map.Id, 2) with
        {
            TurnLimit = 16,
            PassThreshold = 80,
            FlagRisk = MatchOptions.DefaultFlagRisk,
            ContentSet = ContentSet.V2,
            CarryIn = 0,
        };
        MatchLog old = MatchSession.Create(legacy, 3, map, recorded: true).Run();
        Assert.All(old.Header.Config.Players, p => Assert.Null(p.Weights));
        Assert.DoesNotContain("\"Weights\"", old.DeterministicText().Split('\n')[0], StringComparison.Ordinal);
        Assert.True(old.Turns.Count >= 8, $"样本只有 {old.Turns.Count} 个小回合");
        Assert.Contains(old.Turns, t => !t.Passed);

        ReplayResult replay = Replayer.Replay(SimFixtures.Clone(old));
        Assert.True(replay.Identical, $"第 {replay.FirstDivergentLine} 行分歧：{replay.Expected} ≠ {replay.Actual}");
        Assert.All(replay.Replayed.Header.Config.Players, p => Assert.Null(p.Weights));

        // 旧日志的走法就是默认表的走法：与显式写默认表的同配置逐步相同（覆盖值与默认表不同之后，把按地图取权重塞进 AI 层回落会让本条红；
        // 占位期二者等值，由 地图专属评价权重覆盖Tests.按地图取权重只有一处实现且只由三个入口调用 的源码扫描兜住）。
        RunConfig asDefault = legacy with { Players = [.. legacy.Players.Select(p => p with { Weights = EvaluationWeights.Default })] };
        MatchLog byDefault = MatchSession.Create(asDefault, 3, map, recorded: true).Run();
        Assert.Equal(SimFixtures.TurnTexts(old.Turns), SimFixtures.TurnTexts(byDefault.Turns));
    }

    [Fact]
    public void 终端入口取覆盖()
    {
        // 规格 Scenario：play --map siege-2p-base-v1 开局、入口不给权重 → 每名 AI 的评价权重等于 2 人图覆盖表；v5 上等于默认表。
        // 脚本：选 1 号区后输入耗尽即退出（只看建 AI 时取的权重，不依赖走法）。
        Assert.All(Ais(TwoPlayerBaseMap.Create(), null), ai => Assert.Same(EvaluationWeights.MapOverrides[TwoPlayerBaseMap.Id], ai.Weights));
        Assert.All(Ais(TwoPlayerBaseMap.Create(), AiDecision.地图专属评价权重覆盖Tests.Probe), ai => Assert.Equal(AiDecision.地图专属评价权重覆盖Tests.Probe[TwoPlayerBaseMap.Id], ai.Weights));
        Assert.All(Ais(FourPlayerBaseMap.Create(), null), ai => Assert.Same(EvaluationWeights.Default, ai.Weights));
        Assert.All(Ais(FourPlayerBaseMap.Create(), AiDecision.地图专属评价权重覆盖Tests.Probe), ai => Assert.Same(EvaluationWeights.Default, ai.Weights));

        static List<HeuristicTurnController> Ais(MapData map, IReadOnlyDictionary<string, EvaluationWeights>? table)
        {
            var ais = new List<HeuristicTurnController>();
            var output = new StringWriter();
            int exit = PlayCommand.Run(5, null, 1, AiDifficulty.Standard, new StringReader("1\n"), output, map, flagRisk: 0, onAi: (_, ai) => ais.Add(ai), mapOverrides: table);
            Assert.Equal(0, exit);
            Assert.Equal(map.MaxPlayers - 1, ais.Count);   // 样本口径：AI 真的建出来了
            return ais;
        }
    }

    [Fact]
    public void 图形版入口取覆盖()
    {
        // 规格 Scenario：图形版选 2 人图开局 → AI 权重等于 2 人图覆盖表；选 v5 → 默认表。src/godot 不在解决方案里，按 testing.md 用源码扫描
        // （另在段 B 2.4 以 Debug 构建 + 无头自检的启动日志人工核对）：建 AI 的唯一一处传入按本局地图标识取得的权重，不自带对照表、不直接读默认表。
        string scripts = Path.Combine(FrontierFixtures.RepoRoot(), "src", "godot", "scripts");
        string[] files = Directory.GetFiles(scripts, "*.cs");
        Assert.True(files.Length >= 10, $"样本口径：只扫到 {files.Length} 个脚本。");

        string session = File.ReadAllText(Path.Combine(scripts, "MatchSession.cs"));
        Assert.Matches(@"Weights = EvaluationWeights\.ForMapId\(match\.Map\.Id\);", session);
        string[] creates = [.. System.Text.RegularExpressions.Regex.Matches(string.Concat(files.Select(File.ReadAllText)), @"HeuristicAi\.Create\([^;]*;").Select(m => m.Value)];
        Assert.Equal(["HeuristicAi.Create(Match, player, Difficulty, Weights, Search));"], creates);
        Assert.All(files, f =>
        {
            string text = File.ReadAllText(f);
            Assert.DoesNotContain("EvaluationWeights.Default", text, StringComparison.Ordinal);
            Assert.DoesNotContain("new EvaluationWeights(", text, StringComparison.Ordinal);
            Assert.DoesNotContain(TwoPlayerBaseMap.Id, text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void 重建玩家列表后按覆盖取值()
    {
        // 规格 Scenario：2 人图上以带显式权重的配置文件批量跑局、同时给了难度参数 → 配置文件里的权重被丢弃（既有行为，design D5），
        // 批次配置记录中两名玩家的权重等于 2 人图覆盖表；首部同样落成（占位期只有首部分辨得出"落成了覆盖"与"回落默认表"）。
        string dir = SimFixtures.TempDir("map-weights-rebuild");
        string file = Path.Combine(dir, "explicit.json");
        RunConfig withWeights = new()
        {
            MapId = TwoPlayerBaseMap.Id,
            Players = [.. Enumerable.Range(0, 2).Select(_ => new PlayerAiConfig { Difficulty = AiDifficulty.Standard, Weights = EvaluationWeights.Default with { Eye = 100 } })],
        };
        File.WriteAllText(file, withWeights.ToJson());
        string outDir = Path.Combine(dir, "out");
        var err = new StringWriter();
        TextWriter saved = Console.Error;
        int code;
        try
        {
            Console.SetError(err);
            code = Siege.Sim.Program.Main(["run", "--config", file, "--difficulty", "Standard", "--out", outDir, "--count", "1", "--seed", "1", "--turn-limit", "4", "--serial"]);
        }
        finally
        {
            Console.SetError(saved);
        }

        Assert.True(code == 0, err.ToString());
        EvaluationWeights expected = EvaluationWeights.MapOverrides[TwoPlayerBaseMap.Id];
        RunConfig recorded = RunConfig.FromJson(File.ReadAllText(Path.Combine(outDir, "config.json")));
        Assert.Equal(TwoPlayerBaseMap.Id, recorded.MapId);
        Assert.Equal(2, recorded.Players.Count);
        Assert.All(recorded.Players, p => Assert.Equal(expected, p.Weights));
        MatchLog log = MatchLog.Parse(File.ReadAllText(Directory.GetFiles(outDir, "match-*.jsonl").Single()));
        Assert.All(log.Header.Config.Players, p => Assert.Equal(expected, p.Weights));
    }

    /// <summary>未显式配置权重的 Standard 配置（种子 1、1 局、8 个小回合截断、串行）。</summary>
    internal static RunConfig BlankConfig(string mapId, int players) => new()
    {
        MapId = mapId,
        Players = [.. Enumerable.Range(0, players).Select(_ => new PlayerAiConfig { Difficulty = AiDifficulty.Standard })],
        SeedStart = 1,
        Count = 1,
        TurnLimit = 8,
        Parallelism = 1,
        EventRetention = EventRetention.SnapshotsOnly,
        FullEventSamplePermille = 0,
    };

    /// <summary>经批量入口落盘一局，返回日志首行（首部）、config.json 原文与解析后的日志。</summary>
    internal static (string Header, string Config, MatchLog Log) RunOneRaw(string name, RunConfig config)
    {
        string dir = SimFixtures.TempDir("map-weights-" + name);
        BatchRunner.ExecuteToDirectory(config, dir, parallelism: 1);
        string log = File.ReadAllText(Directory.GetFiles(dir, "match-*.jsonl").Single());
        string header = log[..log.IndexOf('\n', StringComparison.Ordinal)].TrimEnd('\r');
        return (header, File.ReadAllText(Path.Combine(dir, "config.json")), MatchLog.Parse(log));
    }

    internal static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
