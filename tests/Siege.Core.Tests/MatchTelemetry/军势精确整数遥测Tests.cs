using System.Numerics;
using System.Text.Json;
using Siege.Core.Scoring;
using Siege.Sim.Analysis;
using Siege.Sim.Logging;

namespace Siege.Core.Tests.MatchTelemetry;

/// <summary>
/// restore-go-core-rules 段 A：势力 / 军势改为任意精度整数后，Sim 快照、峰值记录、事件数值与报告的读写口径。
/// 取代 cap-multiplier 的「倍率封顶遥测Tests」（"生效倍率指数"概念已删）。
/// </summary>
public class 军势精确整数遥测Tests
{
    /// <summary>3^100：161 位二进制，远超 2^63 与 2^127；十进制 48 位。字面值由仓库外独立计算（python：3**100）。</summary>
    private const string Huge = "515377520732011331036461129765621272702107522001";

    [Fact]
    public void 快照与峰值记录的军势逐位往返()
    {
        // match-telemetry「大数不失真」的写入端（段 A 先行）：军势超过 2^63 → 日志里是精确十进制整数（JSON 数字、不加引号、无指数），解析后与写入值逐位一致。
        // 样本取非默认值（testing.md「期望值是 0 / null 的遥测断言抓不到漏写」）：总势力、棋串军势、峰值军势、名次势力、事件数值五处各写一个互不相同的大数。
        // 变异验证 M-AC10（段 A check 实跑）：BigIntegerJsonConverter.Write 把值截成 64 位（value % long.MaxValue）→ 红 2：本测试 + 势力值写出为不带引号的精确十进制整数。
        // 变异验证 M-AC11（段 A check 实跑）：BigIntegerJsonConverter.Read 经 double 解析（reader.GetDouble()）→ 红 4（按用例计）：本测试 + 读入接受/拒绝两条 Theory。
        BigInteger huge = BigInteger.Parse(Huge);
        GroupEntry[] groups = [new() { Stones = ["A1", "A2"], Base = 8, MultiplierCount = 100, Power = huge + 1 }];
        PeakEntry peak = new() { MultiplierCount = 100, MajorRound = 1, Player = 0, Power = huge + 2, Stones = ["A1", "A2"] };
        TurnSnapshot turn = SimFixtures.Turn(1, 1, 0, [65, 0, 0, 0], ["A2:Multiplier"], groupsOfPlayer: groups);
        turn = turn with { PlayersState = [.. turn.PlayersState.Select(p => p.Player == 0 ? p with { Total = huge + 3 } : p)] };
        LogResult result = SimFixtures.ResultOf(1, [0], peak: peak);
        result = result with { Standings = [.. result.Standings.Select(s => s.Player == 0 ? s with { Power = huge + 4 } : s)] };
        LogEvent e = new() { Seq = 1, Turn = 1, MajorRound = 1, Type = LogEventType.MajorRoundEnded, Values = new() { ["P0.Power"] = huge + 5, ["P0.Rank"] = 1 } };
        MatchLog log = SimFixtures.Synthetic(1, [turn], [e], result);

        string text = log.DeterministicText();
        Assert.Contains("\"Power\":515377520732011331036461129765621272702107522002", text);
        Assert.Contains("\"Total\":515377520732011331036461129765621272702107522004", text);
        Assert.Contains("\"P0.Power\":515377520732011331036461129765621272702107522006", text);
        Assert.DoesNotContain("EffectiveMultiplierCount", text);
        Assert.DoesNotContain("E+", text);

        MatchLog restored = MatchLog.Parse(text);
        Assert.Equal(huge + 1, restored.Turns[0].PlayersState[0].Groups[0].Power);
        Assert.Equal(huge + 2, restored.Result!.Peak!.Power);
        Assert.Equal(huge + 3, restored.Turns[0].PlayersState[0].Total);
        Assert.Equal(huge + 4, restored.Result.Standings.Single(s => s.Player == 0).Power);
        Assert.Equal(huge + 5, restored.Events.Single(x => x.Type == LogEventType.MajorRoundEnded).Values!["P0.Power"]);
        Assert.Equal(100, restored.Result.Peak.MultiplierCount);
    }

    [Fact]
    public void 旧日志的生效指数字段被忽略()
    {
        // cap-multiplier ～ multiplier-rebalance 时期的日志行带 EffectiveMultiplierCount：该字段已删，读入时忽略，其余字段照常可读（旧日志仍可解析）。
        const string oldGroupLine = "{\"Stones\":[\"A1\",\"A2\"],\"Base\":8,\"LineBonus\":0,\"SynergyBonus\":0,\"MultiplierCount\":8,\"EffectiveMultiplierCount\":3,\"Power\":27}";
        const string oldPeakLine = "{\"MultiplierCount\":7,\"EffectiveMultiplierCount\":3,\"MajorRound\":4,\"Player\":2,\"Power\":91,\"Stones\":[\"E5\",\"E6\"]}";

        GroupEntry group = JsonSerializer.Deserialize<GroupEntry>(oldGroupLine, LogJson.Options)!;
        PeakEntry peak = JsonSerializer.Deserialize<PeakEntry>(oldPeakLine, LogJson.Options)!;

        Assert.Equal((8, 8, (BigInteger)27), (group.Base, group.MultiplierCount, group.Power));
        Assert.Equal((7, 4, 2, (BigInteger)91), (peak.MultiplierCount, peak.MajorRound, peak.Player, peak.Power));
        Assert.DoesNotContain(typeof(GroupEntry).GetProperties(), p => p.Name.Contains("Effective", StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(PeakEntry).GetProperties(), p => p.Name.Contains("Effective", StringComparison.Ordinal));
    }

    [Fact]
    public void 高倍率棋串报告按倍增子数量且最高军势精确()
    {
        // 峰值 8 枚与 3 枚两局 → 倍增子数量分布 3×1，8×1；不再有"生效倍率指数"一列。最高峰值军势是精确整数（不经 double）。
        // 段 A 改写：原「高倍率棋串报告双列输出」（生效指数分布 3×2、报告文案"封顶 3，倍率上限 3.375"）。
        // 变异验证 M-AC12（段 A check 实跑）：BalanceAnalyzer.Multiplier 的 max 改为经 double 往返（new BigInteger((double)peak.Power)）→ 全套只红本测试 1 条（低位失真）。
        BigInteger huge = BigInteger.Parse(Huge);
        MatchLog eight = SimFixtures.Synthetic(
            3,
            [SimFixtures.Turn(1, 1, 0, [60, 0, 0, 0], ["A2:Multiplier"])],
            [],
            SimFixtures.ResultOf(1, [0], peak: new PeakEntry { MultiplierCount = 8, MajorRound = 1, Player = 0, Power = huge, Stones = ["A1"] }));
        MatchLog three = SimFixtures.Synthetic(
            4,
            [SimFixtures.Turn(1, 1, 1, [0, 10, 0, 0], ["H1:Multiplier"])],
            [],
            SimFixtures.ResultOf(1, [1], peak: new PeakEntry { MultiplierCount = 3, MajorRound = 1, Player = 1, Power = 10, Stones = ["H1"] }));

        BalanceReport report = BalanceAnalyzer.Analyze([eight, three]);
        MultiplierSection m = report.Multiplier;
        string rendered = ReportWriter.Render(report);

        Assert.Equal(2, m.MatchesWithPeak);
        Assert.Equal([3, 8], m.PeakCountDistribution.Keys);
        Assert.Equal(huge, m.MaxPeakPower);
        Assert.Contains("峰值倍增子数分布（即倍率指数，不封顶）3×1，8×1", rendered);
        Assert.Contains($"最高 {Huge}", rendered);
        Assert.DoesNotContain("生效倍率指数", rendered);
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("27", "27")]
    [InlineData("\"27\"", "27")]
    [InlineData("-5", "-5")]
    [InlineData("515377520732011331036461129765621272702107522001", Huge)]
    [InlineData("\"515377520732011331036461129765621272702107522001\"", Huge)]
    public void 势力值读入接受整数与整数字符串(string json, string expected)
    {
        Assert.Equal(BigInteger.Parse(expected), JsonSerializer.Deserialize<BigInteger>(json, LogJson.Options));
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("1e30")]
    [InlineData("\"12x\"")]
    [InlineData("true")]
    public void 势力值读入拒绝非整数(string json)
    {
        // 小数与指数写法一律拒绝：接受它们就得经过浮点或自行舍入，两者都会让"逐位一致"失守。
        // 变异验证 M-AC11（段 A check 实跑，Read 经 double 解析）→ 红，本 Theory 的 1.5 / 1e30 两行不再抛出。
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<BigInteger>(json, LogJson.Options));
    }

    [Fact]
    public void 势力值写出为不带引号的精确十进制整数()
    {
        Assert.Equal(Huge, JsonSerializer.Serialize(BigInteger.Parse(Huge), LogJson.Options));
        Assert.Equal("27", JsonSerializer.Serialize((BigInteger)27, LogJson.Options));
        Assert.Equal("{\"Stones\":[],\"Base\":0,\"LineBonus\":0,\"SynergyBonus\":0,\"MultiplierCount\":0,\"Power\":27}",
            JsonSerializer.Serialize(new GroupEntry { Power = 27 }, LogJson.Options));
        Assert.IsType<BigIntegerJsonConverter>(LogJson.Options.GetConverter(typeof(BigInteger)));
    }
}
