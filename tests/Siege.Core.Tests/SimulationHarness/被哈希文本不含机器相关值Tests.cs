using System.Text.Json;
using Siege.Core.Ai;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>
/// retire-legacy-maps D0（段 0）：日志首部与批次 <c>config.json</c> 会被黄金值哈希比对，不得含机器相关值、哈希前统一行尾。
/// 根因：此前首部 / config 写入 <c>RunConfig.EffectiveParallelism</c>（并行度 0 时 = <c>Environment.ProcessorCount</c>），
/// Windows 28 核钉的黄金值在 8 核机器上 6 条红；<c>config.json</c> 与分析报告按 <c>Environment.NewLine</c> 换行，CRLF / LF 又红 5 条。
/// </summary>
/// <remarks>
/// 变异（本段在 macOS 8 核上实测；过滤范围 = 本类 + 专家前瞻的记录 + 各入口的地图专属AI权重 + 平衡分析方向 + 三档旧难度逐步不变，共 37 条）：
/// M-P1 <c>EffectiveParallelism</c> 的 <c>[JsonIgnore]</c> 换成 <c>[JsonPropertyName("EffectiveParallelism2")]</c>（照写实际核数）→ 本类红 3 条，合计 13 条；
/// M-P2 <c>RecordedParallelism</c> 的 getter 在并行度 0 时返回 <c>Environment.ProcessorCount</c> → 本类红 2 条，合计 7 条；
/// M-P3 <c>FromJson</c> 不清除记录值 → 本类红 1 条，合计 1 条；
/// M-P4 <c>SimFixtures.Sha256Lf</c> 不归一行尾 → 本类红 1 条，合计 1 条（本机输出本来是 LF，黄金值测试察觉不到——这正是本类要守的）；
/// M-P5 <c>RecordedParallelism</c> 的 init 丢弃读入值 → 本类红 1 条 + <c>本change之前的专家日志照常解析</c>，合计 2 条。
/// </remarks>
[Collection(ConsoleRedirect.Collection)]
public class 被哈希文本不含机器相关值Tests
{
    private static RunConfig Auto(int parallelism) => new()
    {
        MapId = "siege-2p-board-v1",
        Players = [.. Enumerable.Range(0, 2).Select(_ => new PlayerAiConfig { Difficulty = AiDifficulty.Easy })],
        SeedStart = 1,
        Count = 1,
        TurnLimit = 4,
        Parallelism = parallelism,
        EventRetention = EventRetention.SnapshotsOnly,
        FullEventSamplePermille = 0,
    };

    private static (string Header, string Config) Run(string name, RunConfig config, int parallelism)
    {
        string dir = SimFixtures.TempDir("machine-free-" + name);
        BatchRunner.ExecuteToDirectory(config, dir, parallelism);
        string log = File.ReadAllText(Directory.GetFiles(dir, "match-*.jsonl").Single());
        return (log[..log.IndexOf('\n', StringComparison.Ordinal)].TrimEnd('\r'), File.ReadAllText(Path.Combine(dir, "config.json")));
    }

    /// <summary>JSON 对象里名字含 "Parallelism" 的全部属性（名 = 原始值）。</summary>
    private static string[] ParallelismKeys(JsonElement obj) =>
        [.. obj.EnumerateObject().Where(p => p.Name.Contains("Parallelism", StringComparison.Ordinal)).Select(p => $"{p.Name}={p.Value.GetRawText()}")];

    [Fact]
    public void 并行度缺省时首部与配置记录不含实际核数()
    {
        // 同一局用两种实际并行度跑（批量入口的 parallelism 实参 1 与 处理器数 + 3，后者与任何真实核数的记录都不同）：首部与 config.json 逐字节相同，
        // 且与并行度有关的字段只有请求值 Parallelism = 0——不出现 EffectiveParallelism（此前写的是本机核数）。
        (string header1, string config1) = Run("p1", Auto(0), 1);
        (string header2, string config2) = Run("pN", Auto(0), Environment.ProcessorCount + 3);
        Assert.Equal(header1, header2);
        Assert.Equal(config1, config2);
        Assert.True(header1.Length > 1000, $"首部只有 {header1.Length} 字符");   // 覆盖范围下界

        using JsonDocument h = JsonDocument.Parse(header1);
        using JsonDocument c = JsonDocument.Parse(config1);
        Assert.Equal(["Parallelism=0"], ParallelismKeys(h.RootElement.GetProperty("Config")));
        Assert.Equal(["Parallelism=0"], ParallelismKeys(c.RootElement));
    }

    [Fact]
    public void 显式并行度照写请求值()
    {
        // 反面对照：显式给了并行度时首部记录的是请求值（与引入本条之前逐字节相同，Parallelism > 0 的既有黄金值因此不变），不是本机核数。
        int requested = Environment.ProcessorCount + 5;
        (string header, string config) = Run("explicit", Auto(requested), 1);
        using JsonDocument h = JsonDocument.Parse(header);
        using JsonDocument c = JsonDocument.Parse(config);
        string[] expected = [$"Parallelism={requested}", $"EffectiveParallelism={requested}"];
        Assert.Equal(expected, ParallelismKeys(h.RootElement.GetProperty("Config")));
        Assert.Equal(expected, ParallelismKeys(c.RootElement));
    }

    [Fact]
    public void 旧日志的记录值原样保留而配置文件读入时丢弃()
    {
        // 旧首部里的实际核数（28）：解析旧日志后再写出原样保留（旧日志逐字节往返，见 专家前瞻的记录Tests.本change之前的专家日志照常解析）；
        // 作为配置文件读入（--config，新批次的输入）时丢弃，不把上一批的核数带进新批次的首部。
        const string legacy = """{"MapId":"siege-2p-board-v1","Players":[{"Difficulty":"Easy"},{"Difficulty":"Easy"}],"Parallelism":0,"EffectiveParallelism":28}""";
        RunConfig fromLog = JsonSerializer.Deserialize<RunConfig>(legacy, new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } })!;
        Assert.Equal(28, fromLog.RecordedParallelism);
        Assert.Contains("\"EffectiveParallelism\": 28", fromLog.ToJson(), StringComparison.Ordinal);

        RunConfig fromFile = RunConfig.FromJson(legacy);
        Assert.Null(fromFile.RecordedParallelism);
        Assert.DoesNotContain("EffectiveParallelism", fromFile.ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void 黄金值哈希与行尾无关()
    {
        // config.json / 报告在 Windows 上是 CRLF、macOS 上是 LF：同一内容两种行尾的哈希必须相同，且等于 LF 文本的哈希。
        const string lf = "{\n  \"A\": 1\n}\n";
        string crlf = lf.Replace("\n", "\r\n", StringComparison.Ordinal);
        Assert.Equal(SimFixtures.Sha256Lf(lf), SimFixtures.Sha256Lf(crlf));
        Assert.NotEqual(SimFixtures.Sha256Lf(crlf), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(crlf))));
    }
}
