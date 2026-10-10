using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;
using static Siege.Core.Tests.SimulationHarness.LinkSimFixtures;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/simulation-harness —— Requirement: 各入口按地图标识选图（链接模式：只接受 <c>link:</c>，缺省 <c>link:1</c>）；
/// map-selection「批量入口缺省」；match-setup「原型插旗替代路径」冒险概率废除（实现裁决 C1 / C2，tasks 3.0）。
/// </summary>
/// <remarks>
/// 变异验证（段 C 前半实跑）见各测试注释。
/// </remarks>
[Collection(ConsoleRedirect.Collection)]
public class 各入口按地图标识选图_链接模式Tests
{
    [Fact]
    public void 缺省地图()
    {
        // 规格 Scenario「缺省地图」/ map-selection「批量入口缺省」：不带地图选项启动终端版与批量跑局，加载 link:1，且以链接模式建局。
        // 变异 MC-1：Program.Play 的缺省改回 MapCatalog.DefaultId → 本测试红（终端打印的地图不是 link:1）。
        (int playCode, string playOut, string playErr) = RunMain(null, new StringReader("q\n"), "play", "--seed", "5", "--no-carry");
        Assert.True(playCode == 0, playErr);
        Assert.Contains("地图 link:1（", playOut, StringComparison.Ordinal);

        string outDir = Path.Combine(SimFixtures.TempDir("link-default-run"), "out");
        (int runCode, _, string runErr) = RunMain(null, null, "run", "--out", outDir, "--count", "1", "--seed", "1", "--turn-limit", "4", "--difficulty", "Easy", "--serial");
        Assert.True(runCode == 0, runErr);
        MatchLog log = MatchLog.ReadDirectory(outDir).Single();
        Assert.Equal("link:1", log.Header.MapId);
        Assert.Equal("link:1", RunConfig.FromJson(File.ReadAllText(Path.Combine(outDir, "config.json"))).MapId);
        Assert.True(log.IsLinkMode);
    }

    [Theory]
    [InlineData("siege-frontier-v2")]
    [InlineData("siege-4p-base-v5")]
    [InlineData("gen:12345")]
    [InlineData("board:7")]
    [InlineData("board")]
    [InlineData("gen")]
    public void 旧标识报错(string mapId)
    {
        // 规格 Scenario「旧标识报错」：以旧标识族启动 play / run，入口报错退出并说明只接受 link:<地图种子>，不开始对局、不取地图种子、不写输出目录。
        // 变异 MC-2：RequireLinkMap 恒放行 → 本测试 6 例全红（旧标识照常开局 / 跑局）。
        (int playCode, string playOut, string playErr) = RunMain(null, new StringReader("1\nq\n"), "play", "--map", mapId, "--seed", "1", "--no-carry");
        Assert.Equal(1, playCode);
        Assert.Contains("只接受 link:<地图种子>", playErr, StringComparison.Ordinal);
        Assert.DoesNotContain("种子 1（", playOut, StringComparison.Ordinal);   // 没有开局

        string outDir = Path.Combine(SimFixtures.TempDir($"link-retired-{mapId.Replace(':', '_')}"), "out");
        (int runCode, _, string runErr) = RunMain(null, null, "run", "--out", outDir, "--map", mapId, "--seed", "1", "--count", "1", "--serial");
        Assert.Equal(1, runCode);
        Assert.Contains("只接受 link:<地图种子>", runErr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outDir));
    }

    [Fact]
    public void 旧标识写在配置文件里同样报错()
    {
        // run --config 里的 MapId 也过同一道门（合并命令行之后、校验之前）。
        string dir = SimFixtures.TempDir("link-retired-config");
        string file = Path.Combine(dir, "config.json");
        File.WriteAllText(file, "{ \"MapId\": \"siege-4p-base-v5\", \"Count\": 1 }");
        (int code, _, string err) = RunMain(null, null, "run", "--out", Path.Combine(dir, "out"), "--config", file, "--serial");
        Assert.Equal(1, code);
        Assert.Contains("只接受 link:<地图种子>", err, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(dir, "out")));
    }

    [Fact]
    public void 未知标识报错()
    {
        // 规格 Scenario「未知标识报错」：no-such-map 在 play / run 都报错退出，不开始对局、不回落到缺省地图。
        (int playCode, _, string playErr) = RunMain(null, new StringReader("1\nq\n"), "play", "--map", "no-such-map", "--seed", "1", "--no-carry");
        Assert.Equal(1, playCode);
        Assert.Contains("只接受 link:<地图种子>", playErr, StringComparison.Ordinal);

        string outDir = Path.Combine(SimFixtures.TempDir("link-unknown"), "out");
        (int runCode, _, string runErr) = RunMain(null, null, "run", "--out", outDir, "--map", "no-such-map", "--seed", "1", "--serial");
        Assert.Equal(1, runCode);
        Assert.StartsWith("错误：", runErr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outDir));

        // 格式不对的 link: 标识同样报错（由 LinkMapId 的唯一解析报出）。
        (int badCode, _, string badErr) = RunMain(null, null, "run", "--out", outDir, "--map", "link:abc", "--seed", "1", "--serial");
        Assert.Equal(1, badCode);
        Assert.Contains("link:<地图种子>", badErr, StringComparison.Ordinal);
    }

    [Fact]
    public void 链接图建局为链接模式并挂配对来源()
    {
        // 实现裁决 C1：link: 对局一律以链接模式建局并挂配对来源（批量跑局缺省为由对局种子派生的配对种子来源）；旧地图仍是 Legacy 夹具。
        // 变异 MC-3：MatchSession.Create 不设 Mode = Link → MatchFlow.Create 报"链接图只能以链接模式建局"，本测试红。
        MatchSession link = MatchSession.Create(Config(turnLimit: 1), 1);
        Assert.Equal(MatchMode.Link, link.Match.Mode);
        Assert.IsType<SeededPairingSource>(link.Match.PairingSource);

        MatchSession legacy = MatchSession.Create(SimFixtures.Config(turnLimit: 1), 1, resolveMap: MapCatalog.Legacy.Resolve);   // board-link-mode 裁决 D7：Legacy 夹具显式经 MapCatalog.Legacy 解析
        Assert.Equal(MatchMode.Legacy, legacy.Match.Mode);
        Assert.Null(legacy.Match.PairingSource);
    }

    [Fact]
    public void 冒险概率选项已删除()
    {
        // 实现裁决 C2 / match-setup「原型插旗替代路径」：--flag-risk 不再是 run 的选项（严格命令行报未知选项）；链接图的配置里写冒险概率即报错；
        // 链接图的批次配置与日志首部不写冒险概率字段。
        // 变异 MC-4：Program.Run 仍读 --flag-risk → 未知选项不再报出，本测试红。
        string dir = SimFixtures.TempDir("link-flag-risk");
        (int code, _, string err) = RunMain(null, null, "run", "--out", Path.Combine(dir, "cli"), "--seed", "1", "--count", "1", "--serial", "--flag-risk", "37");
        Assert.NotEqual(0, code);
        Assert.Contains("flag-risk", err, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(dir, "cli")));

        ArgumentException ex = Assert.Throws<ArgumentException>(() => (Config(turnLimit: 1) with { FlagRisk = 37 }).Validated());
        Assert.Contains("冒险概率已废除", ex.Message, StringComparison.Ordinal);

        // 变异 MC-5：ResolvedFor 对链接图也落成缺省冒险概率 → 首部出现 FlagRisk，本断言红。
        MatchLog log = MatchSession.Create(Config(turnLimit: 1), 1).Run();
        string header = log.DeterministicText().Split('\n')[0];
        Assert.DoesNotContain("FlagRisk", header, StringComparison.Ordinal);
        Assert.Contains("\"PassThreshold\":", header, StringComparison.Ordinal);   // 反面：配置确实写进了首部
    }

    [Fact]
    public void 链接图批量跑局()
    {
        // 规格 Scenario「链接图批量跑局」的冒烟（不跑批量：1 局、截断到第 4 大回合）：run --map link:1 能建局并跑起来，日志首部记链接模式、
        // 第 3 大回合结束时的配对事件由配对种子来源给出（tasks 3.0「run 在 link: 上能建局并跑起来」）。
        string outDir = Path.Combine(SimFixtures.TempDir("link-run"), "out");
        (int code, _, string err) = RunMain(null, null, "run", "--out", outDir, "--map", "link:1", "--count", "1", "--seed", "1", "--turn-limit", "14", "--difficulty", "Easy", "--serial");
        Assert.True(code == 0, err);
        MatchLog log = MatchLog.ReadDirectory(outDir).Single();
        Assert.False(log.IsFailed, log.Failure?.Message);
        Assert.Equal(nameof(MatchMode.Link), log.Header.Mode);
        LogEvent pairing = Assert.Single(log.EventsOf(LogEventType.PairingApplied));   // 样本口径：种子 1 的锁定结果有两块单人棋盘 → 首轮一对
        Assert.Equal(PairingSourceNames.Seeded, pairing.Pairing!.Source);
        Assert.NotEmpty(log.EventsOf(LogEventType.BattlefieldsLinked));
    }
}
