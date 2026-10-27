using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using static Siege.Core.Tests.SimulationHarness.LinkSimFixtures;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>
/// map-expand-25 D1（段 B）：Sim 的 <c>play</c> / <c>run</c> / <c>map</c> 按 <c>--players</c> 定人数——不给 <c>--map</c> 即 <c>link:1:&lt;人数&gt;</c>，裸 <c>link</c> 即
/// <c>link:&lt;友好种子&gt;:&lt;人数&gt;</c>，显式标识只给标识即按标识的人数，标识的人数与显式人数不一致报错退出（不静默改）；<c>--map-per-match</c> 同口径、换图时人数不变。
/// </summary>
/// <remarks>
/// 变异验证（段 B 实跑，只跑本类）：
/// MB-2 <c>ResolveLinkRequest</c> 去掉不一致判定 → 「人数与地图标识合并」「显式人数与标识人数不一致即报错退出」「配置文件的玩家列表与标识人数」红 3；
/// MB-3 缺省地图写死 <see cref="Siege.Sim.Program.DefaultMapId"/>（不跟人数）→ 「人数与地图标识合并」「只给人数即缺省地图带该人数」红 2；
/// MB-4 <c>run</c> 不把配置文件写明的玩家列表当作显式人数 → 「配置文件的玩家列表与标识人数」红 1。
/// </remarks>
[Collection(ConsoleRedirect.Collection)]
public class 各入口人数选项Tests
{
    private static Func<ulong> Never => () => throw new InvalidOperationException("本用例不应随机取地图种子。");

    [Fact]
    public void 人数与地图标识合并()
    {
        var output = new StringWriter();

        // 都没给 → link:1:4；只给人数 → link:1:<人数>。
        Assert.Equal(("link:1:4", 4), Siege.Sim.Program.ResolveLinkRequest(null, null, "run", output, Never));
        Assert.Equal(("link:1:2", 2), Siege.Sim.Program.ResolveLinkRequest(null, 2, "run", output, Never));
        Assert.Equal(("link:1:3", 3), Siege.Sim.Program.ResolveLinkRequest("  ", 3, "run", output, Never));

        // 只给标识 → 人数取标识的人数段（不回落到 4）；给了相同的人数照常。
        Assert.Equal(("link:9:2", 2), Siege.Sim.Program.ResolveLinkRequest("link:9:2", null, "run", output, Never));
        Assert.Equal(("link:9:3", 3), Siege.Sim.Program.ResolveLinkRequest("link:9:3", 3, "run", output, Never));

        // 裸 link → 友好种子 + 显式人数（没给人数即 4），并打印完整标识。
        Assert.Equal(("link:777:2", 2), Siege.Sim.Program.ResolveLinkRequest("link", 2, "run", output, () => 777));
        Assert.Equal(("link:778:4", 4), Siege.Sim.Program.ResolveLinkRequest("link", null, "run", output, () => 778));
        Assert.Contains("本次地图为 link:777:2", output.ToString(), StringComparison.Ordinal);

        // 不一致 → 报错，报文给两边人数与改正写法；人数越界 → 报错；旧两段写法 → 由唯一解析报作废。
        ArgumentException mismatch = Assert.Throws<ArgumentException>(() => Siege.Sim.Program.ResolveLinkRequest("link:9:4", 2, "run", output, Never));
        Assert.Contains("人数 2 与地图标识 link:9:4 的人数 4 不一致", mismatch.Message, StringComparison.Ordinal);
        Assert.Contains("link:9:2", mismatch.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => Siege.Sim.Program.ResolveLinkRequest(null, 5, "run", output, Never));
        Assert.Throws<ArgumentException>(() => Siege.Sim.Program.ResolveLinkRequest(null, 1, "run", output, Never));
        Assert.Throws<FormatException>(() => Siege.Sim.Program.ResolveLinkRequest("link:9", null, "run", output, Never));
    }

    [Fact]
    public void 显式人数与标识人数不一致即报错退出()
    {
        // 规格（D1）：--players 与显式标识的人数不一致 → 入口报错退出，不开局、不建输出目录、不改写任何一边。
        string outDir = Path.Combine(SimFixtures.TempDir("players-mismatch"), "out");
        (int runCode, _, string runErr) = RunMain(null, null, "run", "--out", outDir, "--players", "2", "--map", "link:1:4", "--count", "1", "--seed", "1", "--serial");
        Assert.Equal(1, runCode);
        Assert.Contains("与地图标识 link:1:4 的人数 4 不一致", runErr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outDir));

        (int playCode, string playOut, string playErr) = RunMain(null, new StringReader("q\n"), "play", "--players", "3", "--map", "link:1:2", "--seed", "5", "--no-carry");
        Assert.Equal(1, playCode);
        Assert.Contains("与地图标识 link:1:2 的人数 2 不一致", playErr, StringComparison.Ordinal);
        Assert.DoesNotContain("种子 5（", playOut, StringComparison.Ordinal);   // 没有开局

        (int mapCode, string mapOut, string mapErr) = RunMain(null, null, "map", "--players", "2", "--map", "link:1:3");
        Assert.Equal(1, mapCode);
        Assert.Contains("不一致", mapErr, StringComparison.Ordinal);
        Assert.DoesNotContain("地图校验", mapOut, StringComparison.Ordinal);

        // 人数越界同样在开局前报错。
        (int badCode, _, string badErr) = RunMain(null, new StringReader("q\n"), "play", "--players", "5", "--seed", "5", "--no-carry");
        Assert.Equal(1, badCode);
        Assert.Contains("人数 5 不受支持", badErr, StringComparison.Ordinal);
    }

    [Fact]
    public void 只给人数即缺省地图带该人数()
    {
        // 规格（D1）：没给 --map 时缺省为 link:1:<players>；终端版按该人数开局（插旗占位数 = 2 人岛的平台数），map 子命令打印的也是这张图。
        (int code, string output, string err) = RunMain(null, new StringReader("q\n"), "play", "--players", "2", "--seed", "5", "--no-carry");
        Assert.True(code == 0, err);
        MapData two = MapCatalog.Resolve("link:1:2");
        Assert.Contains($"地图 link:1:2（{two.BirthZones.Length} 块出生平台的 2 人浮空岛）", output, StringComparison.Ordinal);
        Assert.Contains("对手 1 名", output, StringComparison.Ordinal);

        (int mapCode, string mapOut, string mapErr) = RunMain(null, null, "map", "--players", "3");
        Assert.True(mapCode == 0, mapErr);
        Assert.Contains("地图 link:1:3  ", mapOut, StringComparison.Ordinal);

        // 只给标识、不给人数：按标识的人数开局（不撞缺省的 4 人）。
        (int idCode, string idOut, string idErr) = RunMain(null, new StringReader("q\n"), "play", "--map", "link:1:3", "--seed", "5", "--no-carry");
        Assert.True(idCode == 0, idErr);
        Assert.Contains("对手 2 名", idOut, StringComparison.Ordinal);
    }

    [Fact]
    public void 配置文件的玩家列表与标识人数()
    {
        // run --config：配置文件写明了玩家列表（4 名）即为显式人数，与标识 link:1:2 不一致 → 报错退出；
        // 没写玩家列表 → 人数按标识（2），玩家列表按 2 人重建，config.json 与日志首部都记 2 名玩家与 link:1:2。
        string dir = SimFixtures.TempDir("players-config");
        string declared = Path.Combine(dir, "declared.json");
        File.WriteAllText(declared, "{ \"MapId\": \"link:1:2\", \"Players\": [{}, {}, {}, {}] }");
        (int badCode, _, string badErr) = RunMain(null, null, "run", "--out", Path.Combine(dir, "bad"), "--config", declared, "--serial");
        Assert.Equal(1, badCode);
        Assert.Contains("人数 4 与地图标识 link:1:2 的人数 2 不一致", badErr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(dir, "bad")));

        string implicitFile = Path.Combine(dir, "implicit.json");
        File.WriteAllText(implicitFile, "{ \"MapId\": \"link:1:2\" }");
        string outDir = Path.Combine(dir, "ok");
        (int code, _, string err) = RunMain(null, null, "run", "--out", outDir, "--config", implicitFile, "--count", "1", "--seed", "1", "--turn-limit", "2", "--serial");
        Assert.True(code == 0, err);
        RunConfig written = RunConfig.FromJson(File.ReadAllText(Path.Combine(outDir, "config.json")));
        Assert.Equal(("link:1:2", 2), (written.MapId, written.PlayerCount));
        MatchLog log = MatchLog.ReadDirectory(outDir).Single();
        Assert.False(log.IsFailed, log.Failure?.Message);
        Assert.Equal("link:1:2", log.Header.MapId);
        Assert.Equal(2, log.Header.Config.PlayerCount);
    }

    [Fact]
    public void 每局换图人数不变()
    {
        // --map-per-match 同口径：起始标识带人数，第 i 局用 起始 + i、人数不变；起始标识写成旧的两段格式即报错。
        RunConfig config = new RunConfig { MapId = "link:7:2", MapPerMatch = true, Count = 3, Players = [new(), new()] }.Validated();
        Assert.Equal(["link:7:2", "link:8:2", "link:9:2"], Enumerable.Range(0, 3).Select(config.MapIdAt));
        Assert.Throws<FormatException>(() => (config with { MapId = "link:7" }).Validated());
    }
}
