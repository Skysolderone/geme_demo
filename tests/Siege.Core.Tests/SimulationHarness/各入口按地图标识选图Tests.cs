using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Sim.Logging;
using Siege.Sim.Play;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>
/// 重定向 <see cref="Console.Error"/> 的测试类必须同在一个 xunit 集合里串行：<c>Console.SetError</c> 是进程级的，
/// 两个类并行时会互相把对方的输出收走（甲保存原值 → 乙保存到甲的替身 → 甲的输出写进乙的缓冲）。
/// </summary>
internal static class ConsoleRedirect
{
    internal const string Collection = "控制台重定向";
}

/// <summary>规格：frontier-map / simulation-harness —— Requirement: 各入口按地图标识选图</summary>
[Collection(ConsoleRedirect.Collection)]
public class 各入口按地图标识选图Tests
{
    [Fact]
    public void 缺省地图()
    {
        // 规格 Scenario（builtin-board-maps / simulation-harness）：不带地图选项启动终端版 → 加载 siege-4p-board-v1，终端输出打印地图标识。
        // 三个入口的缺省都取自目录的同一个常量。原名「缺省地图不变」（缺省为 v5），builtin-board-maps D3 改为 4 人内置棋盘图。
        // 变异 B-C1：MapCatalog.DefaultId 改回 siege-4p-base-v5 → 本测试红（全量新增红 7）；变异 B-C7：PlayCommand 改回"缺省地图不打印地图行" → 全量只红本测试。
        Assert.Equal("siege-4p-board-v1", MapCatalog.DefaultId);
        Assert.Equal("siege-4p-board-v1", MapCatalog.Resolve(null).Id);
        Assert.Equal("siege-4p-board-v1", MapCatalog.Resolve("  ").Id);
        Assert.Equal("siege-4p-board-v1", new Siege.Sim.Config.RunConfig().MapId);
        Assert.Contains("siege-4p-board-v1", MapCatalog.BuiltinIds);
        Assert.Equal(MapFile.ToJson(MapCatalog.Resolve("siege-4p-board-v1")), MapFile.ToJson(MapCatalog.Resolve(null)));

        // 终端版：不给地图 → 棋盘图开局，首部打印地图标识（棋盘图标识便于复现）。第一个提示处输入 q 退出。
        var output = new StringWriter();
        Assert.Equal(0, PlayCommand.Run(5, null, 1, AiDifficulty.Easy, new StringReader("q\n"), output, flagRisk: 0));
        string text = output.ToString();
        Assert.Single(text.Split('\n'), l => l.StartsWith("地图 siege-4p-board-v1（39×41，5 个出生区）", StringComparison.Ordinal));
        Assert.Contains("选择你的出生区（1–5）", text, StringComparison.Ordinal);
        Assert.Contains("对手 3 名", text, StringComparison.Ordinal);
    }

    [Fact]
    public void 未知标识报错()
    {
        // 规格 Scenario：以 no-such-map 启动任一入口 → 报错退出并列出可用地图标识，不开始对局，MUST NOT 静默回落到缺省地图。
        // 变异 M-A10：Resolve 找不到时 return 缺省地图 → 本测试红。
        FileNotFoundException ex = Assert.Throws<FileNotFoundException>(() => MapCatalog.Resolve("no-such-map"));
        Assert.Contains("no-such-map", ex.Message, StringComparison.Ordinal);
        Assert.NotEmpty(MapCatalog.BuiltinIds);
        Assert.All(MapCatalog.BuiltinIds, id => Assert.Contains(id, ex.Message, StringComparison.Ordinal));

        // 终端版入口
        (int playCode, string playErr) = RunMain("play", "--map", "no-such-map", "--seed", "1");
        Assert.NotEqual(0, playCode);
        Assert.Contains("siege-4p-board-v1", playErr, StringComparison.Ordinal);   // retire-legacy-maps 段 B：可用清单原含 v5

        // 批量入口：报错且不写出任何输出
        string outDir = Path.Combine(SimFixtures.TempDir("mapcatalog-unknown"), "out");
        (int runCode, string runErr) = RunMain("run", "--out", outDir, "--map", "no-such-map", "--seed", "1");
        Assert.NotEqual(0, runCode);
        Assert.Contains("siege-4p-board-v1", runErr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outDir));
    }

    [Fact]
    public void 改名前的旧地图标识报已删除()
    {
        // 规格：map-definition —— 内置图改名 / 删除后旧标识 MUST NOT 悄悄解析成别的图，旧标识的权威文件也不得留在仓库里。
        // 原 Theory（siege-4p-base-v4 → v5、siege-frontier-v1 → v2 报"找不到地图"并列出现名）：retire-legacy-maps 段 B 起 v5 与边疆 v2 本身也删除，
        // 旧标识一律报"已删除"并列出现有地图（明细见 已删除地图明确报错Tests），这里只留"旧文件不得复活旧标识"一半。
        // 变异 M-B20：Builtins 表里把新标识写回旧标识 → 本测试红（旧标识出现在内置清单里）。
        foreach (string retired in new[] { "siege-4p-base-v4", "siege-frontier-v1" })
        {
            Assert.DoesNotContain(retired, MapCatalog.BuiltinIds);
            RetiredMapException ex = Assert.Throws<RetiredMapException>(() => MapCatalog.Resolve(retired));
            Assert.Contains("已删除", ex.Message, StringComparison.Ordinal);
            Assert.Contains(MapCatalog.DefaultId, ex.Message, StringComparison.Ordinal);
        }

        // 仓库里不再有 maps/ 目录（旧图的权威文件随 retire-legacy-maps 段 B 删除；隐式回落 maps/<标识>.json 也已删除）。
        Assert.False(Directory.Exists(Path.Combine(TestMaps.RepoRoot(), "maps")), "仓库里仍有 maps/ 目录。");
        Assert.True(File.Exists(Path.Combine(TestMaps.RepoRoot(), "siege.sln")));   // 反面：找对了仓库根
    }

    [Fact]
    public void 其余标识按文件路径读入()
    {
        // retire-legacy-maps 段 C：样本由边疆档小图换成 4 人合规棋盘档图（档位非缺省值，读回棋盘档才说明整份文件读进来了）。
        string path = Path.Combine(SimFixtures.TempDir("mapcatalog-file"), "my-board.json");
        File.WriteAllText(path, MapFile.ToJson(BoardMapFixtures.FourPlayerMap()));

        MapData map = MapCatalog.Resolve(path);

        Assert.Equal("test-board-4p", map.Id);
        Assert.Equal(MapProfile.Board, map.Profile);
        Assert.Equal(5, map.BirthZones.Length);
        Assert.Equal(7, map.Boards.Length);
    }

    [Fact]
    public void 终端版未登记的地图选项拼写被拒绝()
    {
        // tasks 2.2：--map 已在 play 的严格命令行解析里登记；拼错的 --mapp 按 strict-cli 报错，并建议 --map。
        // 变异 M-A11：Program.Play 不读 "map" 选项 → 合法的 --map 也成了未知选项，下面的反面断言红。
        (int code, string err) = RunMain("play", "--mapp", "siege-4p-board-v1", "--seed", "1");
        Assert.NotEqual(0, code);
        Assert.Contains("--mapp", err, StringComparison.Ordinal);
        Assert.Contains("是否想用 --map？", err, StringComparison.Ordinal);

        // 反面：正确拼写的 --map 不因"未知选项"被拒——这里给一个不存在的地图，报的必须是找不到地图，而不是未知选项。
        (int okCode, string okErr) = RunMain("play", "--map", "no-such-map");
        Assert.NotEqual(0, okCode);
        Assert.DoesNotContain("未知选项", okErr, StringComparison.Ordinal);
        Assert.Contains("找不到地图", okErr, StringComparison.Ordinal);
    }

    [Fact]
    public void 区数多于人数的棋盘图()
    {
        // 规格 Scenario：以边疆图启动终端版 → 插旗提示列出 1–6 号平台。frontier-v1 在段 B 才有，这里用测试内构造的 6 平台图
        //（入口 Program.Play 只做"标识 → 地图"的解析后把地图传给 PlayCommand.Run，解析本身由上面几条钉住）。
        // 人选 3 号台后其余三名 AI 由种子选区：互不相同、不与人重复、都在区号范围内。
        // retire-legacy-maps 段 C：边疆档删除，地图换成 4 人合规棋盘档图（5 块出生棋盘，区数仍多于人数）；提示随之列 1–5。段 D 随规格 Scenario 由「选边疆图」改名为「区数多于人数的棋盘图」。
        var output = new StringWriter();
        int exit = PlayCommand.Run(42, 4, 1, AiDifficulty.Easy, new StringReader("9\n0\n3\n"), output, BoardMapFixtures.FourPlayerMap(), flagRisk: 0, contentSet: ContentSet.V1);   // flag-contest：写死冒险概率 0；more-pieces-relics：写死内容集 v1
        string text = output.ToString();

        Assert.Equal(0, exit);
        Assert.Contains("地图 test-board-4p", text, StringComparison.Ordinal);
        Assert.Contains("选择你的出生区（1–5）", text, StringComparison.Ordinal);
        Assert.Equal(3, text.Split("选择你的出生区（1–5）").Length - 1);   // 9 与 0 越界被拒，第三次的 3 才被接受
        string locked = text.Split('\n').Single(l => l.Contains("出生区锁定：", StringComparison.Ordinal));
        Assert.Contains("玩家1(你)→3号区", locked, StringComparison.Ordinal);
        int[] zones = [.. System.Text.RegularExpressions.Regex.Matches(locked, @"→(\d+)号区").Select(m => int.Parse(m.Groups[1].Value))];
        Assert.Equal(4, zones.Length);
        Assert.Equal(4, zones.Distinct().Count());
        Assert.All(zones, z => Assert.InRange(z, 1, 5));
    }

    [Fact]
    public void 三个入口都经同一份目录解析地图()
    {
        // 规格：三个入口 MUST 共用同一份"标识 → 地图"的解析，MUST NOT 各自维护地图清单。
        // src/godot 不在 siege.sln 里，对 IL / 反射类守门隐身（testing.md），只能做源码文本扫描；配样本口径下界与反面命中。
        // 变异 M-A12：把 src/godot/scripts/MatchSession.cs 改回直接构造地图 → 本测试红。
        // retire-legacy-maps 段 B：被禁记号由 FourPlayerBaseMap（随旧图删除）改为棋盘档生成器 BoardMapGenerator——入口不得绕过目录自己出图。
        string root = TestMaps.RepoRoot();
        string[] entryFiles =
        [
            Path.Combine(root, "src", "Siege.Sim", "Play", "PlayCommand.cs"),
            Path.Combine(root, "src", "Siege.Sim", "Running", "MatchSession.cs"),
            Path.Combine(root, "src", "Siege.Sim", "Running", "BatchRunner.cs"),
            .. Directory.EnumerateFiles(Path.Combine(root, "src", "godot", "scripts"), "*.cs", SearchOption.AllDirectories),
        ];
        Assert.True(entryFiles.Length >= 8, $"样本口径：只扫到 {entryFiles.Length} 个入口文件。");
        Assert.True(entryFiles.Sum(p => new FileInfo(p).Length) > 50_000, "样本口径：入口文件总量过小。");

        Assert.Empty(entryFiles.Where(p => File.ReadAllText(p).Contains("BoardMapGenerator", StringComparison.Ordinal)).Select(Path.GetFileName));
        foreach (string[] entry in new[]
        {
            new[] { "src", "Siege.Sim", "Program.cs" },
            ["src", "Siege.Sim", "Running", "BatchRunner.cs"],
            ["src", "godot", "scripts", "GameRoot.cs"],
        })
        {
            Assert.Contains("MapCatalog.Resolve(", File.ReadAllText(Path.Combine([root, .. entry])), StringComparison.Ordinal);
        }

        // 反面：被禁的记号在它的归属处（目录本身）确实命中；目录只有一份。
        Assert.Contains("BoardMapGenerator", File.ReadAllText(Path.Combine(root, "src", "Siege.Core", "Board", "Maps", "MapCatalog.cs")), StringComparison.Ordinal);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(root, "src"), "MapCatalog.cs", SearchOption.AllDirectories));
    }

    [Fact]
    public void 图形版的地图选项登记在严格命令行解析里_没有绕过它的第二个读取点()
    {
        // 规格：地图选项 MUST 在各入口的严格命令行解析中登记。图形版的合法选项集合 = LaunchArgs 上被读取过的名字，
        // 所以"登记"= 经 LaunchArgs 读取 + 结算在建局之前；任何直接翻 OS.GetCmdline*Args 的读取点都会绕过未知选项校验。
        // src/godot 不在解决方案里，只能做源码文本扫描（行为由 Godot 自检实测：--mapp=x / --auto-demo=1 / --rounds=abc 退出码 1）。
        string dir = Path.Combine(TestMaps.RepoRoot(), "src", "godot", "scripts");
        (string Name, string Text)[] scripts = [.. Directory.GetFiles(dir, "*.cs").Order().Select(f => (Path.GetFileName(f), File.ReadAllText(f)))];
        Assert.True(scripts.Length >= 10, $"样本口径：只扫到 {scripts.Length} 个脚本。");

        string root = scripts.Single(s => s.Name == "GameRoot.cs").Text;
        Assert.Contains("args.Text(\"map\"", root, StringComparison.Ordinal);
        int settle = root.IndexOf("args.EnsureRecognized();", StringComparison.Ordinal);
        int resolve = root.IndexOf("MapCatalog.Resolve(", StringComparison.Ordinal);
        Assert.True(settle > 0 && resolve > settle, "结算必须在解析地图 / 建局之前。");

        // 命令行只在构造 LaunchArgs 时取一次；别处不得再翻原始参数（那样的选项不进合法集合，拼错也不会报）。
        string[] rawReads = [.. scripts.SelectMany(s => System.Text.RegularExpressions.Regex.Matches(s.Text, @"GetCmdline\w*\(").Select(m => $"{s.Name}: {m.Value}"))];
        Assert.Equal(["GameRoot.cs: GetCmdlineUserArgs(", "GameRoot.cs: GetCmdlineArgs("], rawReads);
        Assert.Contains("new LaunchArgs(OS.GetCmdlineUserArgs(), OS.GetCmdlineArgs())", root, StringComparison.Ordinal);
        Assert.DoesNotContain(scripts, s => s.Name != "LaunchArgs.cs" && s.Text.Contains("StartsWith(\"--", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("siege-2p-board-v1", 2)]
    [InlineData("siege-3p-board-v1", 3)]
    public void 两人与三人棋盘图可选_选中确认后建局人数等于地图人数上限(string builtinId, int players)
    {
        // 规格：builtin-board-maps / map-selection —— Scenario「2 人与 3 人棋盘图可选」：选图清单可选到 2 人与 3 人内置棋盘图，
        // 选中确认后拿到的标识交给入口建局（不给人数），对局恰有 2 / 3 名玩家。选图模型只产出标识，建局走批量入口的同一份解析。
        // 变异 M-S5：Program.Run 的缺省人数不改写（条件加 `&& maxPlayers < 0`）→ 红 3（本测试两组 + 下一条，builtin-board-maps 段 D 实跑）。
        var model = new Siege.Presentation.MapSelect.MapSelectModel(1);
        int index = model.Options.ToList().FindIndex(o => o.BuiltinId == builtinId);
        Assert.True(index >= 0, $"选图清单里没有 {builtinId}。");
        Assert.True(model.Select(index));
        model.Accept();
        string id = model.Confirm();
        Assert.Equal(builtinId, id);

        (Siege.Sim.Config.RunConfig recorded, MatchLog log) = RunOne("pick-" + builtinId, "--map", id);
        Assert.Equal(players, recorded.PlayerCount);
        Assert.Equal(builtinId, recorded.MapId);
        Assert.Equal(builtinId, log.Header.MapId);
        Assert.Equal(players, log.Header.Players.Count);
    }

    [Fact]
    public void 参赛人数缺省取地图人数上限_显式人数照旧()
    {
        // small-maps D3：批量配置 / 终端 / 图形版在未指定人数时用 map.MaxPlayers；显式给人数时仍按给定值；4 人图行为不变。
        // 变异（各红本测试）：M-S4 PlayCommand 缺省写死 `?? 4`；M-S5 批量缺省不改写（另红上面的「两人与三人棋盘图可选」两组）；
        // M-S6 配置文件写了 Players 也当未指定；M-S7 src/godot/scripts/GameRoot.cs 建局人数 Min(4, map.MaxPlayers) 改成 4。
        // 批量入口：命令行不给 --players
        Assert.Equal(2, RunOne("default-2p", "--map", "siege-2p-board-v1").Config.PlayerCount);
        Assert.Equal(2, RunOne("default-2p-difficulty", "--map", "siege-2p-board-v1", "--difficulty", "Easy").Config.PlayerCount);
        Assert.Equal(3, RunOne("default-3p", "--map", "siege-3p-board-v1").Config.PlayerCount);
        Assert.Equal(4, RunOne("default-4p", "--map", MapCatalog.DefaultId).Config.PlayerCount);
        Assert.Equal(4, RunOne("default-none").Config.PlayerCount);
        Assert.Equal(3, RunOne("explicit-3-on-4p", "--players", "3").Config.PlayerCount);

        // 批量入口：配置文件不写 Players 也算"未指定"；写了就按写的。
        string dir = SimFixtures.TempDir("players-config");
        string noPlayers = Path.Combine(dir, "no-players.json");
        File.WriteAllText(noPlayers, "{ \"MapId\": \"siege-2p-board-v1\" }");
        Assert.Equal(2, RunOne("config-no-players", "--config", noPlayers).Config.PlayerCount);
        string withPlayers = Path.Combine(dir, "with-players.json");
        File.WriteAllText(withPlayers, "{ \"Players\": [ { \"Difficulty\": \"Easy\" }, { \"Difficulty\": \"Easy\" }, { \"Difficulty\": \"Easy\" } ] }");
        Assert.Equal(3, RunOne("config-players", "--config", withPlayers).Config.PlayerCount);

        // 终端入口：不给人数 → 地图人数上限（2 人棋盘图出生区 1–3、对手 1 名）；4 人图仍是 3 名对手。
        // retire-legacy-maps 段 B：2 / 3 人图由 siege-2p-base-v1 / siege-3p-base-v1 改为 2 / 3 人内置棋盘图（出生区数 = 人数 + 1）。
        var twoOut = new StringWriter();
        Assert.Equal(0, PlayCommand.Run(5, null, 1, AiDifficulty.Easy, new StringReader("q\n"), twoOut, MapCatalog.Resolve("siege-2p-board-v1"), flagRisk: 0));
        Assert.Contains("对手 1 名", twoOut.ToString(), StringComparison.Ordinal);
        Assert.Contains("选择你的出生区（1–3）", twoOut.ToString(), StringComparison.Ordinal);
        var fourOut = new StringWriter();
        Assert.Equal(0, PlayCommand.Run(5, null, 1, AiDifficulty.Easy, new StringReader("q\n"), fourOut, flagRisk: 0));
        Assert.Contains("对手 3 名", fourOut.ToString(), StringComparison.Ordinal);

        // 图形版（src/godot 不在解决方案里，只能源码扫描）：每一处建局的人数都取自地图的人数上限（Min(4, MaxPlayers)，
        // 标准档预算表只到 4 人，故恒等于 MaxPlayers；4 封顶是表现层配色只备 4 名玩家的保险）。此项在改动前已成立，由 Godot 自检实测 2 人图建局 2 名玩家。
        string scripts = Path.Combine(TestMaps.RepoRoot(), "src", "godot", "scripts");
        string[] creates = [.. Directory.GetFiles(scripts, "*.cs")
            .SelectMany(f => File.ReadLines(f))
            .Where(l => l.Contains("MatchSession.Create(", StringComparison.Ordinal))];
        Assert.True(creates.Length >= 3, $"样本口径：只扫到 {creates.Length} 处建局。");
        Assert.All(creates, l => Assert.Contains(".MaxPlayers", l, StringComparison.Ordinal));
    }

    /// <summary>经批量入口跑 1 局（4 个小回合截断），返回落盘的 config.json 与该局日志。</summary>
    private static (Siege.Sim.Config.RunConfig Config, MatchLog Log) RunOne(string name, params string[] extra)
    {
        string outDir = Path.Combine(SimFixtures.TempDir("players-default-" + name), "out");
        (int code, string err) = RunMain(["run", "--out", outDir, "--count", "1", "--seed", "1", "--turn-limit", "4", "--serial", .. extra]);
        Assert.True(code == 0, err);
        var config = Siege.Sim.Config.RunConfig.FromJson(File.ReadAllText(Path.Combine(outDir, "config.json")));
        MatchLog log = MatchLog.Parse(File.ReadAllText(Directory.GetFiles(outDir, "match-*.jsonl").Single()));
        return (config, log);
    }

    private static (int Code, string Error) RunMain(params string[] args)
    {
        var err = new StringWriter();
        TextWriter saved = Console.Error;
        try
        {
            Console.SetError(err);
            return (Siege.Sim.Program.Main(args), err.ToString());
        }
        finally
        {
            Console.SetError(saved);
        }
    }
}
