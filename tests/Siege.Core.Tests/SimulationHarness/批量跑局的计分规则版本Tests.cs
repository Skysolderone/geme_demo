using Siege.Core.Ai;
using Siege.Core.Scoring;
using Siege.Core.Tests.AiDecision;
using Siege.Sim.Config;
using Siege.Sim.Logging;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>规格：formation-tiers simulation-harness —— Requirement: 批量跑局的计分规则版本</summary>
/// <remarks>
/// 批量跑局可指定计分规则版本（命令行 <c>--scoring v1|v2</c> 或配置文件 <c>ScoringVersion</c>），缺省 v2，实际生效值写进 <c>config.json</c>；
/// 未识别的取值报错退出、不产出对局（design.md D2；testing.md「静默忽略的输入会产出口径错误的数据」）。
/// <para>变异验证（formation-tiers 段 A，实跑，明细见任务 implement 记录）：</para>
/// <list type="bullet">
/// <item>M-H1 <c>Program.Run</c> 不读 <c>--scoring</c>（恒取配置文件的值）→ 红「指定v1跑一批」（未认领的选项被严格命令行拒绝）。</item>
/// <item>M-H2 <c>RunConfig.ResolvedFor</c> 不把未配置的版本落成缺省值 → 红「缺省v2」等。</item>
/// <item>M-H3 <c>ScoringVersions.Parse</c> 对未知名称回退缺省值 → 红「未识别的取值」。</item>
/// <item>M-H4 <c>MatchSession.Create</c> 建局时不把版本写进对局配置 → 红「指定v1跑一批」（会话核对配置与对局不一致）。</item>
/// <item>M-H5 去掉 <c>RunConfig.ScoringVersion</c> 的属性级转换器 → 红「未识别的取值」的配置文件分支。</item>
/// </list>
/// </remarks>
[Collection(ConsoleRedirect.Collection)]
public class 批量跑局的计分规则版本Tests
{
    /// <summary>既有口径的批次配置文件：种子 31、标准难度、24 个小回合，权重 / 阈值 / 冒险概率 / 内容集写死，<b>不写</b>计分规则版本。</summary>
    private static string ConfigFile(string dir, ScoringVersion? scoring = null)
    {
        RunConfig config = SimFixtures.PinPreCalibration(SimFixtures.Config(seedStart: 31, turnLimit: 24, difficulty: AiDifficulty.Standard)) with
        {
            ScoringVersion = scoring,
            Parallelism = 1,
        };
        string file = Path.Combine(dir, "batch.json");
        File.WriteAllText(file, config.ToJson());
        return file;
    }

    private static (RunConfig Config, string ConfigText, List<MatchLog> Logs, string[] Headers) ReadBatch(string outDir)
    {
        string text = File.ReadAllText(Path.Combine(outDir, "config.json"));
        string[] files = [.. Directory.GetFiles(outDir, "match-*.jsonl").Order(StringComparer.Ordinal)];
        return (RunConfig.FromJson(text), text, [.. files.Select(MatchLog.Read)], [.. files.Select(f => File.ReadLines(f).First())]);
    }

    [Fact]
    public void 指定v1跑一批()
    {
        // 规格 Scenario：以计分规则 v1 跑一批对局 → 该批的配置记录与每局日志首部都写着 v1，各局与引入阵型之前同一配置的结果逐项相同。
        // "引入之前同一配置的结果"= 候选格上限Tests.V4GoldenTurnHash（种子 31、Standard、24 个小回合，引入阵型之前钉下，一字未改）。
        // 命令行优先于配置文件：文件里写 V2、命令行给 v1 → 生效 v1。
        string dir = SimFixtures.TempDir("scoring-v1");
        string outDir = Path.Combine(dir, "out");
        (int code, _, string err) = RunMain("run", "--out", outDir, "--config", ConfigFile(dir, ScoringVersion.V2), "--scoring", "v1", "--count", "2", "--serial");
        Assert.True(code == 0, err);

        (RunConfig saved, string configText, List<MatchLog> logs, string[] headers) = ReadBatch(outDir);
        Assert.Equal(ScoringVersion.V1, saved.ScoringVersion);
        Assert.Contains("\"ScoringVersion\": \"V1\"", configText, StringComparison.Ordinal);
        Assert.Equal(2, logs.Count);
        Assert.All(logs, l => Assert.Equal(ScoringVersion.V1, l.Header.Config.ScoringVersion));
        Assert.All(headers, h => Assert.Contains("\"ScoringVersion\":\"V1\"", h, StringComparison.Ordinal));
        Assert.All(logs, l => Assert.True(l.Turns.Count >= 24, $"小回合 {l.Turns.Count}"));
        Assert.Equal(候选格上限Tests.V4GoldenTurnHash, 候选格上限Tests.TurnHash(logs[0]));
        Assert.All(logs, l => Assert.All(l.Turns.SelectMany(t => t.PlayersState).SelectMany(p => p.Groups), g => Assert.Null(g.FormationTier)));

        // 对照：同一份配置文件按 v2 跑 → 配置记录与首部写 v2，结果不同（版本确实抵达了对局），棋串条目带阵型阶数。
        string outV2 = Path.Combine(dir, "out-v2");
        (code, _, err) = RunMain("run", "--out", outV2, "--config", ConfigFile(dir), "--scoring", "V2", "--count", "2", "--serial");
        Assert.True(code == 0, err);
        (RunConfig savedV2, _, List<MatchLog> logsV2, string[] headersV2) = ReadBatch(outV2);
        Assert.Equal(ScoringVersion.V2, savedV2.ScoringVersion);
        Assert.All(headersV2, h => Assert.Contains("\"ScoringVersion\":\"V2\"", h, StringComparison.Ordinal));
        Assert.NotEqual(候选格上限Tests.TurnHash(logs[0]), 候选格上限Tests.TurnHash(logsV2[0]));
        Assert.Contains(logsV2.SelectMany(l => l.Turns).SelectMany(t => t.PlayersState).SelectMany(p => p.Groups), g => g.FormationTier > 0);

        // 配置文件单独指定也生效（不给命令行选项）。
        string outFile = Path.Combine(dir, "out-file");
        (code, _, err) = RunMain("run", "--out", outFile, "--config", ConfigFile(dir, ScoringVersion.V1), "--count", "1", "--serial");
        Assert.True(code == 0, err);
        (RunConfig savedFile, _, List<MatchLog> logsFile, _) = ReadBatch(outFile);
        Assert.Equal(ScoringVersion.V1, savedFile.ScoringVersion);
        Assert.Equal(候选格上限Tests.V4GoldenTurnHash, 候选格上限Tests.TurnHash(logsFile[0]));
    }

    [Fact]
    public void 缺省v2()
    {
        // 规格 Scenario：不指定计分规则版本跑一批对局 → 配置记录写着 v2（日志首部同样落成 v2）。命令行与配置文件都不给。
        string dir = SimFixtures.TempDir("scoring-default");
        string outDir = Path.Combine(dir, "out");
        (int code, _, string err) = RunMain("run", "--out", outDir, "--count", "1", "--seed", "1", "--turn-limit", "8", "--serial");
        Assert.True(code == 0, err);

        (RunConfig saved, string configText, List<MatchLog> logs, string[] headers) = ReadBatch(outDir);
        Assert.Equal(ScoringVersion.V2, saved.ScoringVersion);
        Assert.Contains("\"ScoringVersion\": \"V2\"", configText, StringComparison.Ordinal);
        Assert.Equal(ScoringVersion.V2, Assert.Single(logs).Header.Config.ScoringVersion);
        Assert.Contains("\"ScoringVersion\":\"V2\"", Assert.Single(headers), StringComparison.Ordinal);

        // 配置文件不写该项同样落成 v2。
        string outFile = Path.Combine(dir, "out-file");
        (code, _, err) = RunMain("run", "--out", outFile, "--config", ConfigFile(dir), "--count", "1", "--serial");
        Assert.True(code == 0, err);
        Assert.Equal(ScoringVersion.V2, ReadBatch(outFile).Config.ScoringVersion);
        Assert.Equal(ScoringVersion.V2, ScoringVersions.Default);
    }

    [Fact]
    public void 未识别的取值()
    {
        // 规格 Scenario：把计分规则版本指定为 v3 → 报错退出，不产出对局。命令行与配置文件经同一处严格解析：未知名称、数字、空值都不接受，
        // 错误信息列出可用取值；输出目录不被创建。
        string dir = SimFixtures.TempDir("scoring-unknown");
        foreach (string bad in new[] { "v3", "3", "2", "", "v1 " })
        {
            string outDir = Path.Combine(dir, "cli-out");
            (int code, _, string err) = RunMain("run", "--out", outDir, "--scoring", bad, "--count", "1", "--serial");
            Assert.True(code == 1, $"--scoring \"{bad}\" → {code}：{err}");
            Assert.Contains("v1|v2", err, StringComparison.Ordinal);
            Assert.False(Directory.Exists(outDir));
        }

        foreach (string bad in new[] { "\"V3\"", "\"3\"", "3", "2", "\"\"" })
        {
            string cfg = Path.Combine(dir, "cfg.json");
            File.WriteAllText(cfg, $$"""{ "ScoringVersion": {{bad}}, "Count": 1 }""");
            string outDir = Path.Combine(dir, "cfg-out");
            (int code, _, string err) = RunMain("run", "--out", outDir, "--config", cfg, "--serial");
            Assert.True(code == 1, $"配置文件 ScoringVersion {bad} → {code}：{err}");
            Assert.Contains("v1|v2", err, StringComparison.Ordinal);
            Assert.False(Directory.Exists(outDir));
        }

        // 解析本身：只认两个名称（不区分大小写），不去空白、不认数字。
        Assert.Equal(ScoringVersion.V1, ScoringVersions.Parse("V1"));
        Assert.Equal(ScoringVersion.V2, ScoringVersions.Parse("v2"));
        Assert.Throws<ArgumentException>(() => ScoringVersions.Parse("v3"));
        Assert.Throws<ArgumentException>(() => ScoringVersions.Parse("1"));
        Assert.Throws<ArgumentException>(() => ScoringVersions.Parse(null));
        Assert.False(ScoringVersions.TryParse(" v1", out _));
    }

    private static (int Code, string Out, string Error) RunMain(params string[] args)
    {
        var output = new StringWriter();
        var err = new StringWriter();
        TextWriter savedOut = Console.Out;
        TextWriter savedErr = Console.Error;
        try
        {
            Console.SetOut(output);
            Console.SetError(err);
            int code = Siege.Sim.Program.Execute(args, () => throw new InvalidOperationException("本测试不应随机取地图种子。"));
            return (code, output.ToString(), err.ToString());
        }
        finally
        {
            Console.SetOut(savedOut);
            Console.SetError(savedErr);
        }
    }
}
