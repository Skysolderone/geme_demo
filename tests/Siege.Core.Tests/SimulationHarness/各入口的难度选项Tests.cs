using System.Text.Json;
using System.Text.RegularExpressions;
using Siege.Core.Ai;
using Siege.Core.Tests.CarryInOut;
using Siege.Sim.Config;
using Siege.Sim.Logging;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>
/// 规格：expert-lookahead / simulation-harness —— Requirement: 各入口的难度选项。
/// 难度名称经 Core 的唯一解析 <see cref="AiDifficultyNames"/>：只认四个名称（不区分大小写），数字、未知名称、空值在跑局 / 开局之前报错并列出可用名称。
/// 「图形版选专家」在 <c>MapSelection/选图界面难度选择Tests</c>（Godot 工程不在解决方案里，另配源码扫描与视图模型测试）。
/// </summary>
[Collection(ConsoleRedirect.Collection)]
public class 各入口的难度选项Tests
{
    private static readonly string[] Names = ["Easy", "Standard", "Hard", "Expert"];

    [Fact]
    public void 批量跑局选专家()
    {
        // run --difficulty Expert --count 2：配置记录与每局日志首部写明四名玩家都是专家、前瞻宽度 4（未显式给 Search 的专家由 ResolvedFor 落成具体搜索配置）。
        string outDir = Path.Combine(SimFixtures.TempDir("difficulty-expert-run"), "out");
        (int code, _, string err) = RunMain("run", "--out", outDir, "--difficulty", "Expert", "--count", "2", "--seed", "1", "--turn-limit", "4", "--serial");
        Assert.True(code == 0, err);

        string config = File.ReadAllText(Path.Combine(outDir, "config.json"));
        Assert.Equal(4, Regex.Matches(config, "\"Difficulty\": \"Expert\"").Count);
        Assert.Equal(4, Regex.Matches(config, "\"LookaheadWidth\": 4").Count);
        RunConfig recorded = RunConfig.FromJson(config);
        Assert.All(recorded.Players, p => Assert.Equal(AiDifficulty.Expert, p.Difficulty));
        Assert.All(recorded.Players, p => Assert.Equal(AiSearchConfig.DefaultLookaheadWidth, p.Search!.LookaheadWidth));

        string[] files = Directory.GetFiles(outDir, "match-*.jsonl");
        Assert.Equal(2, files.Length);
        foreach (string file in files)
        {
            string header = File.ReadLines(file).First();
            Assert.Equal(4, Regex.Matches(header, "\"Difficulty\":\"Expert\"").Count);
            Assert.Equal(4, Regex.Matches(header, "\"LookaheadWidth\":4").Count);
            MatchLog log = MatchLog.Read(file);
            Assert.All(Enumerable.Range(0, 4), p => Assert.Equal(4, log.LookaheadWidthOf(p)));
        }
    }

    [Fact]
    public void 逐玩家难度()
    {
        // 配置文件 Players = 专家、标准、标准、标准：玩家 1（P0）按专家决策，其余三名按标准；配置记录与日志首部逐名写明。
        // "按专家决策"的可观测证据：P0 的每个小回合都有前瞻记录，其余玩家一条都没有（前瞻记录只由前瞻宽度 > 0 的控制者产生）。
        string dir = SimFixtures.TempDir("difficulty-per-player");
        string outDir = Path.Combine(dir, "out");
        string cfg = Path.Combine(dir, "cfg.json");
        File.WriteAllText(cfg, """
            {
              "Players": [ { "Difficulty": "Expert" }, { "Difficulty": "Standard" }, { "Difficulty": "Standard" }, { "Difficulty": "Standard" } ],
              "SeedStart": 1, "Count": 2, "TurnLimit": 8, "FullEventSamplePermille": 0
            }
            """);
        (int code, _, string err) = RunMain("run", "--out", outDir, "--config", cfg, "--serial");
        Assert.True(code == 0, err);

        string config = File.ReadAllText(Path.Combine(outDir, "config.json"));
        Assert.Single(Regex.Matches(config, "\"LookaheadWidth\": 4"));
        RunConfig recorded = RunConfig.FromJson(config);
        Assert.Equal([AiDifficulty.Expert, AiDifficulty.Standard, AiDifficulty.Standard, AiDifficulty.Standard], recorded.Players.Select(p => p.Difficulty));
        Assert.Equal(4, recorded.Players[0].Search!.LookaheadWidth);
        Assert.All(recorded.Players.Skip(1), p => Assert.Null(p.Search));

        foreach (string file in Directory.GetFiles(outDir, "match-*.jsonl"))
        {
            MatchLog log = MatchLog.Read(file);
            Assert.Equal([4, 0, 0, 0], Enumerable.Range(0, 4).Select(log.LookaheadWidthOf));
            Assert.Equal(recorded.Players.Select(p => p.Difficulty), log.Header.Config.Players.Select(p => p.Difficulty));
            TurnSnapshot[] expertTurns = [.. log.Turns.Where(t => t.Player == 0)];
            Assert.NotEmpty(expertTurns);   // 样本口径：P0 在截断前确实行动过
            Assert.All(expertTurns, t => Assert.NotNull(t.Lookahead));
            Assert.All(log.Turns.Where(t => t.Player != 0), t => Assert.Null(t.Lookahead));
        }
    }

    [Theory]
    [InlineData("run", "3")]
    [InlineData("run", "0")]
    [InlineData("play", "1")]
    [InlineData("play", "3")]
    public void 数字难度被拒绝(string command, string value)
    {
        // 数字（含既有三档的序号）在创建输出目录或开局之前报错，列出四个可用名称，不回落到标准。
        // 变异 M-B1a（play / run 恢复 Enum.Parse(…, ignoreCase: true)）→ 本测试红。
        string outDir = Path.Combine(SimFixtures.TempDir($"difficulty-number-{command}-{value}"), "out");
        var input = new StringReader(string.Empty);
        (int code, string output, string err) = command == "run"
            ? RunMain("run", "--out", outDir, "--difficulty", value, "--count", "1", "--seed", "1", "--turn-limit", "4", "--serial")
            : RunMain(input, "play", "--difficulty", value, "--no-carry", "--seed", "5");

        Assert.Equal(1, code);
        Assert.False(Directory.Exists(outDir));
        Assert.All(Names, n => Assert.Contains(n, err, StringComparison.Ordinal));
        Assert.DoesNotContain("种子", output, StringComparison.Ordinal);   // play 没有开局（开局第一行打印种子）
    }

    [Fact]
    public void 未知难度被拒绝()
    {
        // 命令行与配置文件的未知名称同样在跑局 / 开局之前报错；配置文件经同一处解析（PlayerAiConfig.Difficulty 的属性级转换器）。
        // 变异 M-B1b（配置文件绕过共用解析：去掉属性级转换器，回落到 JsonStringEnumConverter）→ 本测试的配置文件分支红
        // （JsonException 不在入口的受控异常里，且数字 3 会被静默当成专家）。
        string dir = SimFixtures.TempDir("difficulty-unknown");
        foreach (string[] args in new[]
        {
            new[] { "run", "--out", Path.Combine(dir, "cli"), "--difficulty", "Master", "--count", "1", "--serial" },
            new[] { "play", "--difficulty", "Master", "--no-carry", "--seed", "5" },
            new[] { "run", "--out", Path.Combine(dir, "cli-empty"), "--difficulty", "", "--count", "1", "--serial" },
        })
        {
            (int code, string output, string err) = RunMain(new StringReader(string.Empty), args);
            Assert.True(code == 1, $"{string.Join(" ", args)} → {code}：{err}");
            Assert.All(Names, n => Assert.Contains(n, err, StringComparison.Ordinal));
            Assert.DoesNotContain("种子", output, StringComparison.Ordinal);
        }

        Assert.False(Directory.Exists(Path.Combine(dir, "cli")));
        Assert.False(Directory.Exists(Path.Combine(dir, "cli-empty")));

        foreach (string bad in new[] { "\"Master\"", "\"3\"", "3", "\"\"", "null" })
        {
            string cfg = Path.Combine(dir, "cfg.json");
            File.WriteAllText(cfg, $$"""{ "Players": [ { "Difficulty": {{bad}} }, { "Difficulty": "Standard" } ], "Count": 1 }""");
            string outDir = Path.Combine(dir, "cfg-out");
            (int code, _, string err) = RunMain("run", "--out", outDir, "--config", cfg, "--serial");
            Assert.True(code == 1, $"配置文件难度 {bad} → {code}：{err}");
            Assert.All(Names, n => Assert.Contains(n, err, StringComparison.Ordinal));
            Assert.False(Directory.Exists(outDir));
            Assert.Throws<ArgumentException>(() => RunConfig.FromJson(File.ReadAllText(cfg)));
        }
    }

    [Fact]
    public void 名称不区分大小写()
    {
        // play --difficulty expert：全部 AI 玩家按专家难度开局（开局行写明难度与实际生效的前瞻宽度）。
        (int code, string output, string err) = RunMain(new StringReader("q\n"), "play", "--difficulty", "expert", "--no-carry", "--seed", "5");
        Assert.True(code == 0, err);
        Assert.Contains("对手 3 名 Expert AI（前瞻宽度 4）", output, StringComparison.Ordinal);

        foreach ((string text, AiDifficulty expected) in new[] { ("easy", AiDifficulty.Easy), ("STANDARD", AiDifficulty.Standard), ("hArD", AiDifficulty.Hard), ("Expert", AiDifficulty.Expert) })
        {
            Assert.Equal(expected, AiDifficultyNames.Parse(text));
            Assert.True(AiDifficultyNames.TryParse(text, out AiDifficulty parsed));
            Assert.Equal(expected, parsed);
        }

        // 配置文件同样不区分大小写，写出时仍是规范名称（首部逐字节与改动前相同的前提）。
        RunConfig config = RunConfig.FromJson("""{ "Players": [ { "Difficulty": "expert" }, { "Difficulty": "standard" } ] }""");
        Assert.Equal([AiDifficulty.Expert, AiDifficulty.Standard], config.Players.Select(p => p.Difficulty));
        Assert.Contains("\"Difficulty\": \"Expert\"", config.ToJson(), StringComparison.Ordinal);
        Assert.Contains("\"Difficulty\": \"Standard\"", config.ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void 缺省仍为标准()
    {
        // 批量跑局、终端都不给难度：AI 为标准难度，配置记录与首部没有前瞻宽度（与改动前逐字节相同的走法由「三档旧难度逐步不变」的黄金值钉住）。
        string outDir = Path.Combine(SimFixtures.TempDir("difficulty-default"), "out");
        (int code, _, string err) = RunMain("run", "--out", outDir, "--count", "1", "--seed", "1", "--turn-limit", "4", "--serial");
        Assert.True(code == 0, err);
        string config = File.ReadAllText(Path.Combine(outDir, "config.json"));
        Assert.Equal(4, Regex.Matches(config, "\"Difficulty\": \"Standard\"").Count);
        Assert.DoesNotContain("LookaheadWidth", config, StringComparison.Ordinal);
        MatchLog log = MatchLog.Read(Directory.GetFiles(outDir, "match-*.jsonl").Single());
        Assert.Empty(log.LookaheadTurns);
        Assert.DoesNotContain("Lookahead", log.FullText(), StringComparison.Ordinal);

        (int playCode, string playOut, string playErr) = RunMain(new StringReader("q\n"), "play", "--no-carry", "--seed", "5");
        Assert.True(playCode == 0, playErr);
        Assert.Contains("对手 3 名 Standard AI", playOut, StringComparison.Ordinal);
        Assert.DoesNotContain("前瞻宽度", playOut, StringComparison.Ordinal);
        Assert.Equal(AiDifficulty.Standard, new PlayerAiConfig().Difficulty);
    }

    [Fact]
    public void 用法说明列出四档名称()
    {
        // D10：终端 play 与批量 run 的用法说明都列出 Easy|Standard|Hard|Expert。
        (int code, string output, _) = RunMain("no-such-command");
        Assert.Equal(2, code);
        Assert.Equal("Easy|Standard|Hard|Expert", AiDifficultyNames.Usage);
        Assert.Equal(2, Regex.Matches(output, Regex.Escape("--difficulty <Easy|Standard|Hard|Expert>")).Count);
    }

    [Fact]
    public void 真实档案目录不被触碰()
    {
        // play 的用例一律带 --no-carry：本类任何用例都不得读写真实的 %APPDATA%\Siege。
        string before = RealProfileDir.State();
        RunMain(new StringReader("q\n"), "play", "--difficulty", "Hard", "--no-carry", "--seed", "5");
        Assert.Equal(before, RealProfileDir.State());
    }

    private static (int Code, string Out, string Error) RunMain(params string[] args) => RunMain(new StringReader(string.Empty), args);

    private static (int Code, string Out, string Error) RunMain(TextReader input, params string[] args)
    {
        var output = new StringWriter();
        var err = new StringWriter();
        TextWriter savedOut = Console.Out;
        TextWriter savedErr = Console.Error;
        var play = new StringWriter();
        try
        {
            Console.SetOut(output);
            Console.SetError(err);
            int code = Siege.Sim.Program.Execute(args, () => throw new InvalidOperationException("本测试不应随机取地图种子。"), input, play);
            return (code, output.ToString() + play.ToString(), err.ToString());
        }
        finally
        {
            Console.SetOut(savedOut);
            Console.SetError(savedErr);
        }
    }
}
