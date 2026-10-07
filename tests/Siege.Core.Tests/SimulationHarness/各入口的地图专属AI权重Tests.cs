using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Play;
using Siege.Sim.Running;
using Probes = Siege.Core.Tests.AiDecision.地图专属评价权重覆盖Tests;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>规格：simulation-harness —— Requirement: 各入口的地图专属 AI 权重（v2-recalibration 段 B，design D5 / D6）</summary>
/// <remarks>
/// retire-legacy-maps 段 B（design D2 / 已知歧义 4）：缺省登记表里唯一一项 siege-2p-base-v1 随该图删除，机制保留、表为空。
/// 本类原先读缺省表里 2 人图条目的断言，改为两半：缺省表一侧钉"未登记 → 不落成、取默认表"；"登记了覆盖时落成 / 按首部回放 / 显式优先"
/// 经纯函数接缝 <c>RunConfig.ResolvedFor(map, 登记表)</c> 与 <c>PlayCommand.Run(mapOverrides:)</c> 注入 <see cref="Probes.Probe"/>（登记 2 人内置棋盘图）。
/// 批量入口与会话不接受注入表，它们"都经 ResolvedFor(map) 落成"由源码断言钉住（见「批量跑局按登记表落成覆盖」）。
/// </remarks>
[Collection(ConsoleRedirect.Collection)]   // 「重建玩家列表后按覆盖取值」经 Program.Main 重定向 Console.Error
public class 各入口的地图专属AI权重Tests
{
    [Theory]
    // retire-legacy-maps 段 B：原四行钉在 v5 / 3p / 边疆 / gen:12345 上（引入本机制之前的实现实跑，见 v2-recalibration 段 B 2.1），随旧图删除。
    // 现改钉三张内置棋盘图与 board:12345 上的首部与 config.json（LF 归一后 SHA-256）为本 change 的基线：同一份未显式配置权重的配置在未登记覆盖的地图上
    // 跑种子 1 的一局、Standard、8 个小回合截断。原四行旧值：首部 34C451BC… / F8CD1029… / A239B7FD… / E167132E…，config 97EFBB69… / E4EEA516… / A3C49989… / 01B8407B…。
    [InlineData("siege-4p-board-v1", 4, "0C221021D32B847E024C059C6EE2CE632AC7884FFFB74930C85A8E68789AB04E", "CE8A890C931C3D97B1704EB3BAF11A8ED76B8A777026A9F006AC96E594611EFE")]
    [InlineData("siege-3p-board-v1", 3, "EF91171DC84976B249A6C1D436989EB99E178351989D59ED4B9D27A6837B84FE", "27BCB5599611DCCD80137020E50AAC583092B1B9AB9435A9E125A57ED006B2C2")]
    [InlineData("siege-2p-board-v1", 2, "9EBC0B3786FA9D8EF9EA9D8F010C7C6489BDD5EF1691388D4400C3F14E784EE8", "2E2E54A20A57B062D3C1E14716830333ED07EB8C6324E472C8431D5281800C28")]
    [InlineData("board:12345", 4, "FD018F2E5A7765AA64A8AAF87C8ACFBFF4FE8E51D5C4C0942670AF94AEBC6A5A", "1B691D2ACC3D52AAEB093469E5B9B365CBB826A438ABB34288F79A210B1E7216")]
    public void 未登记的地图首部逐字节不变(string mapId, int players, string headerHash, string configHash)
    {
        (string header, string config, _) = RunOneRaw($"unregistered-{mapId.Replace(':', '_')}", BlankConfig(mapId, players));

        Assert.Equal((headerHash, configHash), (Sha256(header), Sha256(config)));
        // 各玩家首部权重均为空：序列化省略空值，首部配置里不出现 Weights 键（config.json 另经 Effective() 填默认表，属既有口径）。
        Assert.DoesNotContain("\"Weights\"", header, StringComparison.Ordinal);
        Assert.True(header.Length > 1000, $"首部只有 {header.Length} 字符");   // 覆盖范围下界：砍成空行时哈希比对仍可能被误钉
        Assert.Contains($"\"MapId\":\"{mapId}\"", header, StringComparison.Ordinal);
    }

    [Fact]
    public void 批量跑局按登记表落成覆盖()
    {
        // 规格 Scenario（原「批量跑局在 2 人图上落成覆盖」）：未显式配置权重 → 批次配置记录与每局日志首部里各玩家的权重都等于该地图登记的覆盖表。
        MapData map = MapCatalog.Resolve(Probes.ProbeMapId);
        RunConfig blank = BlankConfig(map.Id, 2);

        // 缺省登记表为空：经批量入口跑一局，什么都不落成（首部省略 Weights 键）。
        (string header, string config, MatchLog log) = RunOneRaw("2p-blank", blank);
        Assert.DoesNotContain("\"Weights\"", header, StringComparison.Ordinal);
        Assert.All(log.Header.Config.Players, p => Assert.Null(p.Weights));
        Assert.All(RunConfig.FromJson(config).Players, p => Assert.Equal(EvaluationWeights.Default, p.Weights));   // config.json 经 Effective() 填默认表

        // 注入登记表：落成写进配置，再经会话写进首部。
        EvaluationWeights expected = Probes.Probe[map.Id];
        RunConfig resolved = blank.ResolvedFor(map, Probes.Probe);
        Assert.All(resolved.Players, p => Assert.Equal(expected, p.Weights));
        MatchLog withProbe = MatchSession.Create(resolved, 1, map).Run();
        Assert.Equal(map.Id, withProbe.Header.MapId);
        Assert.Equal(2, withProbe.Header.Config.Players.Count);
        Assert.All(withProbe.Header.Config.Players, p => Assert.Equal(expected, p.Weights));
        Assert.Contains("\"Weights\"", withProbe.DeterministicText().Split('\n')[0], StringComparison.Ordinal);

        // 批量入口与会话建局都经同一个落成（取缺省登记表的那个重载）：任一处绕开它，"登记了就落成"在产品里就不成立。
        string running = Path.Combine(FrontierFixtures.RepoRoot(), "src", "Siege.Sim", "Running");
        Assert.Contains("config = config.ResolvedFor(map);", File.ReadAllText(Path.Combine(running, "BatchRunner.cs")), StringComparison.Ordinal);
        Assert.Contains("config = config.ResolvedFor(map);", File.ReadAllText(Path.Combine(running, "MatchSession.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void 显式权重不被覆盖替换()
    {
        // 规格 Scenario：两名玩家显式写了 Eye = 100 的整表权重 → 批次配置记录与日志首部记录的是这张显式表，不是覆盖表。
        MapData map = MapCatalog.Resolve(Probes.ProbeMapId);
        EvaluationWeights explicitTable = EvaluationWeights.Default with { Eye = 100 };
        RunConfig config = BlankConfig(map.Id, 2) with
        {
            Players = [.. Enumerable.Range(0, 2).Select(_ => new PlayerAiConfig { Difficulty = AiDifficulty.Standard, Weights = explicitTable })],
        };
        (_, string saved, MatchLog log) = RunOneRaw("2p-explicit", config);

        Assert.All(RunConfig.FromJson(saved).Players, p => Assert.Equal(explicitTable, p.Weights));
        Assert.All(log.Header.Config.Players, p => Assert.Equal(explicitTable, p.Weights));

        // 与默认表不同的登记表（接缝）下同样原样保留：落成不逐维合并。
        Assert.NotEqual(Probes.Probe[map.Id], explicitTable);
        RunConfig resolved = config.ResolvedFor(map, Probes.Probe);
        Assert.All(resolved.Players, p => Assert.Same(explicitTable, p.Weights));
    }

    [Fact]
    public void 新日志按首部回放()
    {
        // 规格 Scenario：回放一局按覆盖落成权重的新日志 → 每一步决策与原局相同，重建出的首部与原首部逐字节相同。
        // retire-legacy-maps 段 B：覆盖由注入表落成（缺省表为空），回放只读首部，不依赖登记表。
        MapData map = MapCatalog.Resolve(Probes.ProbeMapId);
        RunConfig resolved = (BlankConfig(map.Id, 2) with { TurnLimit = 8 }).ResolvedFor(map, Probes.Probe);   // 段 B：原 16 个小回合（9×9 图），棋盘图上减半控耗时
        MatchLog original = MatchSession.Create(resolved, 3, map).Run();
        Assert.All(original.Header.Config.Players, p => Assert.Equal(Probes.Probe[map.Id], p.Weights));
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
        // 规格 Scenario：回放一局引入本机制之前产出、首部权重为空的日志 → AI 按默认表重建，每一步与原局相同。
        // 夹具由测试现场生成（tasks 2.1）：按首部重建的路径（recorded）不落成任何缺省，于是首部与引入本机制之前同形——
        // 阈值 80、内容集 V2、冒险概率与带入显式写出，权重为空。retire-legacy-maps 段 B：地图由 siege-2p-base-v1 改为 2 人内置棋盘图；
        // 棋盘图是大图，引入本机制之前的批量入口已把候选格上限落成写进首部（frontier-map），这里同样显式写出（否则按首部重建取 0 = 全盘枚举，单测 30 余秒）；
        // 小回合由 16 减到 8 控耗时。
        MapData map = MapCatalog.Resolve(Probes.ProbeMapId);
        RunConfig legacy = BlankConfig(map.Id, 2) with
        {
            TurnLimit = 8,
            CandidateCellLimit = AiSearchConfig.DefaultCellLimitFor(map.PlayableCount),
            PassThreshold = 80,
            FlagRisk = MatchOptions.DefaultFlagRisk,
            ContentSet = ContentSet.V2,
            CarryIn = 0,
        };
        MatchLog old = MatchSession.Create(legacy, 3, map, recorded: true).Run();
        Assert.All(old.Header.Config.Players, p => Assert.Null(p.Weights));
        Assert.Equal(AiSearchConfig.LargeMapCellLimit, old.Header.Config.CandidateCellLimit);   // 样本口径：确是大图口径
        Assert.DoesNotContain("\"Weights\"", old.DeterministicText().Split('\n')[0], StringComparison.Ordinal);
        Assert.True(old.Turns.Count >= 8, $"样本只有 {old.Turns.Count} 个小回合");
        Assert.Contains(old.Turns, t => !t.Passed);

        ReplayResult replay = Replayer.Replay(SimFixtures.Clone(old));
        Assert.True(replay.Identical, $"第 {replay.FirstDivergentLine} 行分歧：{replay.Expected} ≠ {replay.Actual}");
        Assert.All(replay.Replayed.Header.Config.Players, p => Assert.Null(p.Weights));

        // 旧日志的走法就是默认表的走法：与显式写默认表的同配置逐步相同（把按地图取权重塞进 AI 层回落由
        // 地图专属评价权重覆盖Tests.按地图取权重只有一处实现且只由三个入口调用 的源码扫描兜住）。
        RunConfig asDefault = legacy with { Players = [.. legacy.Players.Select(p => p with { Weights = EvaluationWeights.Default })] };
        MatchLog byDefault = MatchSession.Create(asDefault, 3, map, recorded: true).Run();
        Assert.Equal(SimFixtures.TurnTexts(old.Turns), SimFixtures.TurnTexts(byDefault.Turns));
    }

    [Fact]
    public void 终端入口取覆盖()
    {
        // 规格 Scenario：终端开局、入口不给权重 → 每名 AI 的评价权重等于该地图登记的覆盖表；未登记的地图上等于默认表。
        // 脚本：选 1 号区后输入耗尽即退出（只看建 AI 时取的权重，不依赖走法）。
        MapData two = MapCatalog.Resolve(Probes.ProbeMapId);
        MapData four = MapCatalog.Resolve("siege-4p-board-v1");
        Assert.All(Ais(two, null), ai => Assert.Same(EvaluationWeights.Default, ai.Weights));   // 缺省登记表为空
        Assert.All(Ais(two, Probes.Probe), ai => Assert.Equal(Probes.Probe[two.Id], ai.Weights));
        Assert.All(Ais(four, null), ai => Assert.Same(EvaluationWeights.Default, ai.Weights));
        Assert.All(Ais(four, Probes.Probe), ai => Assert.Same(EvaluationWeights.Default, ai.Weights));

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
        // 规格 Scenario：图形版开局 → AI 权重按本局地图标识取（登记了覆盖取覆盖，否则默认表）。src/godot 不在解决方案里，按 testing.md 用源码扫描：
        // 建 AI 的唯一一处传入按本局地图标识取得的权重，不自带对照表、不直接读默认表。
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
            Assert.DoesNotContain(Probes.ProbeMapId, text, StringComparison.Ordinal);
            Assert.DoesNotContain("siege-2p-base-v1", text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void 重建玩家列表后按覆盖取值()
    {
        // 规格 Scenario：以带显式权重的配置文件批量跑局、同时给了难度参数 → 配置文件里的权重被丢弃（既有行为，design D5），改按地图覆盖、再按缺省表取值。
        // retire-legacy-maps 段 B：缺省登记表为空，2 人内置棋盘图未登记 → 批次配置记录里两名玩家的权重等于默认表（经 Effective() 填入），首部权重为空。
        string dir = SimFixtures.TempDir("map-weights-rebuild");
        string file = Path.Combine(dir, "explicit.json");
        EvaluationWeights explicitTable = EvaluationWeights.Default with { Eye = 100 };
        RunConfig withWeights = new()
        {
            MapId = Probes.ProbeMapId,
            Players = [.. Enumerable.Range(0, 2).Select(_ => new PlayerAiConfig { Difficulty = AiDifficulty.Standard, Weights = explicitTable })],
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
        RunConfig recorded = RunConfig.FromJson(File.ReadAllText(Path.Combine(outDir, "config.json")));
        Assert.Equal(Probes.ProbeMapId, recorded.MapId);
        Assert.Equal(2, recorded.Players.Count);
        Assert.All(recorded.Players, p => Assert.Equal(EvaluationWeights.Default, p.Weights));
        Assert.All(recorded.Players, p => Assert.NotEqual(explicitTable, p.Weights));   // 显式权重确被丢弃
        MatchLog log = MatchLog.Parse(File.ReadAllText(Directory.GetFiles(outDir, "match-*.jsonl").Single()));
        Assert.All(log.Header.Config.Players, p => Assert.Null(p.Weights));
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

    /// <summary>LF 归一后的 SHA-256（见 <see cref="SimFixtures.Sha256Lf"/>）。</summary>
    internal static string Sha256(string text) => SimFixtures.Sha256Lf(text);
}
