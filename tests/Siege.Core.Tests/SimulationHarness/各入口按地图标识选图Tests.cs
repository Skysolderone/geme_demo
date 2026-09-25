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
    public void 缺省地图不变()
    {
        // 未给地图选项 → siege-4p-base-v5。三个入口的缺省都取自目录的同一个常量。
        // 变异 M-A9：MapCatalog.DefaultId 换成 siege-4p-base-v3 → 本测试红（全套共红 39：所有走缺省地图的入口都跟着变）。
        Assert.Equal("siege-4p-base-v5", MapCatalog.DefaultId);
        Assert.Equal("siege-4p-base-v5", MapCatalog.Resolve(null).Id);
        Assert.Equal("siege-4p-base-v5", MapCatalog.Resolve("  ").Id);
        Assert.Equal("siege-4p-base-v5", MapCatalog.Resolve("siege-4p-base-v5").Id);
        Assert.Equal("siege-4p-base-v5", new Siege.Sim.Config.RunConfig().MapId);
        Assert.Contains("siege-4p-base-v5", MapCatalog.BuiltinIds);
        Assert.Equal(MapFile.ToJson(FourPlayerBaseMap.Create()), MapFile.ToJson(MapCatalog.Resolve(null)));
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
        Assert.Contains("siege-4p-base-v5", playErr, StringComparison.Ordinal);

        // 批量入口：报错且不写出任何输出
        string outDir = Path.Combine(SimFixtures.TempDir("mapcatalog-unknown"), "out");
        (int runCode, string runErr) = RunMain("run", "--out", outDir, "--map", "no-such-map", "--seed", "1");
        Assert.NotEqual(0, runCode);
        Assert.Contains("siege-4p-base-v5", runErr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outDir));
    }

    [Theory]
    [InlineData("siege-4p-base-v4", "siege-4p-base-v5")]
    [InlineData("siege-frontier-v1", "siege-frontier-v2")]
    public void 改名前的旧地图标识报未知地图(string retired, string current)
    {
        // 规格：map-definition —— 内置图随据点摘除改名（tasks 2.3）。旧标识 MUST 报"找不到地图"并列出现名，
        // MUST NOT 悄悄解析成新图——两张图的信物/地形虽然一样，但旧标识对应的权威文件已经不存在，
        // 让它还能解析出图会掩盖"调用方仍在用旧标识"。
        // 变异 M-B20：Builtins 表里把新标识写回旧标识 → 本测试红。
        Assert.DoesNotContain(retired, MapCatalog.BuiltinIds);
        FileNotFoundException ex = Assert.Throws<FileNotFoundException>(() => MapCatalog.Resolve(retired));
        Assert.Contains("找不到地图", ex.Message, StringComparison.Ordinal);
        Assert.Contains(retired, ex.Message, StringComparison.Ordinal);
        Assert.Contains(current, ex.Message, StringComparison.Ordinal);

        // 反面：现名必须解析得出，否则上面那条"旧的报错"可以靠"两个都报错"恒真。
        Assert.Equal(current, MapCatalog.Resolve(current).Id);

        // 旧标识的权威文件也不得留在仓库里——留着的话 Resolve 会经 maps/<标识>.json 回落，旧标识悄悄复活。
        Assert.False(
            File.Exists(Path.Combine(FrontierFixtures.RepoRoot(), "maps", retired + ".json")),
            $"maps/{retired}.json 仍在仓库里。");
    }

    [Fact]
    public void 其余标识按文件路径读入()
    {
        string path = Path.Combine(SimFixtures.TempDir("mapcatalog-file"), "my-frontier.json");
        File.WriteAllText(path, MapFile.ToJson(FrontierFixtures.Map()));

        MapData map = MapCatalog.Resolve(path);

        Assert.Equal("test-frontier-6", map.Id);
        Assert.Equal(MapProfile.Frontier, map.Profile);
        Assert.Equal(6, map.BirthZones.Length);
    }

    [Fact]
    public void 终端版未登记的地图选项拼写被拒绝()
    {
        // tasks 2.2：--map 已在 play 的严格命令行解析里登记；拼错的 --mapp 按 strict-cli 报错，并建议 --map。
        // 变异 M-A11：Program.Play 不读 "map" 选项 → 合法的 --map 也成了未知选项，下面的反面断言红。
        (int code, string err) = RunMain("play", "--mapp", "siege-4p-base-v5", "--seed", "1");
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
    public void 选边疆图()
    {
        // 规格 Scenario：以边疆图启动终端版 → 插旗提示列出 1–6 号平台。frontier-v1 在段 B 才有，这里用测试内构造的 6 平台图
        //（入口 Program.Play 只做"标识 → 地图"的解析后把地图传给 PlayCommand.Run，解析本身由上面几条钉住）。
        // 人选 3 号台后其余三名 AI 由种子选区：互不相同、不与人重复、都在 1–6 内。
        var output = new StringWriter();
        int exit = PlayCommand.Run(42, 4, 1, AiDifficulty.Easy, new StringReader("9\n0\n3\n"), output, FrontierFixtures.Map(), flagRisk: 0, contentSet: ContentSet.V1);   // flag-contest：写死冒险概率 0；more-pieces-relics：写死内容集 v1
        string text = output.ToString();

        Assert.Equal(0, exit);
        Assert.Contains("地图 test-frontier-6", text, StringComparison.Ordinal);
        Assert.Contains("选择你的出生区（1–6）", text, StringComparison.Ordinal);
        Assert.Equal(3, text.Split("选择你的出生区（1–6）").Length - 1);   // 9 与 0 越界被拒，第三次的 3 才被接受
        string locked = text.Split('\n').Single(l => l.Contains("出生区锁定：", StringComparison.Ordinal));
        Assert.Contains("玩家1(你)→3号区", locked, StringComparison.Ordinal);
        int[] zones = [.. System.Text.RegularExpressions.Regex.Matches(locked, @"→(\d+)号区").Select(m => int.Parse(m.Groups[1].Value))];
        Assert.Equal(4, zones.Length);
        Assert.Equal(4, zones.Distinct().Count());
        Assert.All(zones, z => Assert.InRange(z, 1, 6));
    }

    [Fact]
    public void 显式选缺省地图与不带选项逐字相同()
    {
        // tasks 2.2：不带 --map 时同种子对局与改动前逐步相同。改动前后的完整转录对比是一次性做的（记录在任务 implement.md 段 A）；
        // 这里长期钉住的是"缺省 ≡ 显式给 v4"，并配行数下界与字段级断言，防止两边一起只剩首行（testing.md）。
        string script = "2\n" + string.Concat(Enumerable.Repeat("\npass\n", 3));   // 段 C：大回合上限删除，改为 3 个小回合后输入耗尽退出（原 40 行 + 上限 3 收尾）；   // 每个小回合：空行 = 不征募，pass = 不落子
        var implicitOut = new StringWriter();
        var explicitOut = new StringWriter();

        // AI 权重与停手阈值写死为 ai-eye 4.5 定值之前的缺省：默认阈值 80 下简单难度第 1 大回合全员 Pass、对局即终局，走不到第 3 大回合（段 D2 改写）。
        EvaluationWeights w = SimFixtures.PreCalibrationWeights;
        const int t = SimFixtures.PreCalibrationPassThreshold;
        PlayCommand.Run(7, 4, 2, AiDifficulty.Easy, new StringReader(script), implicitOut, weights: w, passThreshold: t, flagRisk: 0, contentSet: ContentSet.V1);
        PlayCommand.Run(7, 4, 2, AiDifficulty.Easy, new StringReader(script), explicitOut, MapCatalog.Resolve("siege-4p-base-v5"), weights: w, passThreshold: t, flagRisk: 0, contentSet: ContentSet.V1);

        string text = implicitOut.ToString();
        Assert.Equal(text, explicitOut.ToString());
        Assert.True(text.Split('\n').Length > 100, "转录过短，对局没有真正进行。");
        Assert.Contains("选择你的出生区（1–4）", text, StringComparison.Ordinal);
        Assert.Contains("第 3 大回合", text, StringComparison.Ordinal);
        Assert.Contains("已退出。种子 7", text, StringComparison.Ordinal);
        Assert.DoesNotContain("地图 ", text.Split('\n')[..6].Aggregate(string.Concat), StringComparison.Ordinal);   // 缺省地图不多打一行，转录与改动前一致
    }

    [Fact]
    public void 三个入口都经同一份目录解析地图()
    {
        // 规格：三个入口 MUST 共用同一份"标识 → 地图"的解析，MUST NOT 各自维护地图清单。
        // src/godot 不在 siege.sln 里，对 IL / 反射类守门隐身（testing.md），只能做源码文本扫描；配样本口径下界与反面命中。
        // 变异 M-A12：把 src/godot/scripts/MatchSession.cs 改回 FourPlayerBaseMap.Create() → 本测试红。
        string root = FrontierFixtures.RepoRoot();
        string[] entryFiles =
        [
            Path.Combine(root, "src", "Siege.Sim", "Play", "PlayCommand.cs"),
            Path.Combine(root, "src", "Siege.Sim", "Running", "MatchSession.cs"),
            Path.Combine(root, "src", "Siege.Sim", "Running", "BatchRunner.cs"),
            .. Directory.EnumerateFiles(Path.Combine(root, "src", "godot", "scripts"), "*.cs", SearchOption.AllDirectories),
        ];
        Assert.True(entryFiles.Length >= 8, $"样本口径：只扫到 {entryFiles.Length} 个入口文件。");
        Assert.True(entryFiles.Sum(p => new FileInfo(p).Length) > 50_000, "样本口径：入口文件总量过小。");

        Assert.Empty(entryFiles.Where(p => File.ReadAllText(p).Contains("FourPlayerBaseMap", StringComparison.Ordinal)).Select(Path.GetFileName));
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
        Assert.Contains("FourPlayerBaseMap", File.ReadAllText(Path.Combine(root, "src", "Siege.Core", "Board", "Maps", "MapCatalog.cs")), StringComparison.Ordinal);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(root, "src"), "MapCatalog.cs", SearchOption.AllDirectories));
    }

    [Fact]
    public void 图形版的地图选项登记在严格命令行解析里_没有绕过它的第二个读取点()
    {
        // 规格：地图选项 MUST 在各入口的严格命令行解析中登记。图形版的合法选项集合 = LaunchArgs 上被读取过的名字，
        // 所以"登记"= 经 LaunchArgs 读取 + 结算在建局之前；任何直接翻 OS.GetCmdline*Args 的读取点都会绕过未知选项校验。
        // src/godot 不在解决方案里，只能做源码文本扫描（行为由 Godot 自检实测：--mapp=x / --auto-demo=1 / --rounds=abc 退出码 1）。
        string dir = Path.Combine(FrontierFixtures.RepoRoot(), "src", "godot", "scripts");
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

    [Fact]
    public void 两人图可选_选中确认后建局恰有2名玩家()
    {
        // 规格：small-maps / map-selection —— Scenario「2 人与 3 人图可选」（段 A 只有 2 人图）：选图清单可选到 2 人图，
        // 变异 M-S5：Program.Run 的缺省人数不改写（条件加 `&& maxPlayers < 0`）→ 本测试与下一条共红 2。
        // 选中确认后拿到的标识交给入口建局（不给人数），对局恰有 2 名玩家。选图模型只产出标识，建局走批量入口的同一份解析。
        var model = new Siege.Presentation.MapSelect.MapSelectModel(1);
        int index = model.Options.ToList().FindIndex(o => o.BuiltinId == TwoPlayerBaseMap.Id);
        Assert.True(index >= 0, "选图清单里没有 2 人图。");
        Assert.True(model.Select(index));
        model.Accept();
        string id = model.Confirm();
        Assert.Equal(TwoPlayerBaseMap.Id, id);

        (Siege.Sim.Config.RunConfig recorded, MatchLog log) = RunOne("pick-2p", "--map", id);
        Assert.Equal(2, recorded.PlayerCount);
        Assert.Equal(TwoPlayerBaseMap.Id, recorded.MapId);
        Assert.Equal(TwoPlayerBaseMap.Id, log.Header.MapId);
        Assert.Equal(2, log.Header.Players.Count);
    }

    [Fact]
    public void 三人图可选_选中确认后建局恰有3名玩家()
    {
        // 规格：small-maps / map-selection —— Scenario「2 人与 3 人图可选」（段 B 补 3 人图）：选图清单可选到 3 人图，
        // 选中确认后拿到的标识交给入口建局（不给人数），对局恰有 3 名玩家。
        var model = new Siege.Presentation.MapSelect.MapSelectModel(1);
        int index = model.Options.ToList().FindIndex(o => o.BuiltinId == ThreePlayerBaseMap.Id);
        Assert.True(index >= 0, "选图清单里没有 3 人图。");
        Assert.True(model.Select(index));
        model.Accept();
        string id = model.Confirm();
        Assert.Equal(ThreePlayerBaseMap.Id, id);

        (Siege.Sim.Config.RunConfig recorded, MatchLog log) = RunOne("pick-3p", "--map", id);
        Assert.Equal(3, recorded.PlayerCount);
        Assert.Equal(ThreePlayerBaseMap.Id, recorded.MapId);
        Assert.Equal(ThreePlayerBaseMap.Id, log.Header.MapId);
        Assert.Equal(3, log.Header.Players.Count);
    }

    [Fact]
    public void 参赛人数缺省取地图人数上限_显式人数照旧()
    {
        // small-maps D3：批量配置 / 终端 / 图形版在未指定人数时用 map.MaxPlayers；显式给人数时仍按给定值；4 人图行为不变。
        // 变异（各红本测试）：M-S4 PlayCommand 缺省写死 `?? 4`；M-S5 批量缺省不改写（共红 2，另一条是上面的「两人图可选」）；
        // M-S6 配置文件写了 Players 也当未指定；M-S7 src/godot/scripts/GameRoot.cs 建局人数 Min(4, map.MaxPlayers) 改成 4。
        // 批量入口：命令行不给 --players
        Assert.Equal(2, RunOne("default-2p", "--map", TwoPlayerBaseMap.Id).Config.PlayerCount);
        Assert.Equal(2, RunOne("default-2p-difficulty", "--map", TwoPlayerBaseMap.Id, "--difficulty", "Easy").Config.PlayerCount);
        Assert.Equal(3, RunOne("default-3p", "--map", ThreePlayerBaseMap.Id).Config.PlayerCount);
        Assert.Equal(4, RunOne("default-4p", "--map", MapCatalog.DefaultId).Config.PlayerCount);
        Assert.Equal(4, RunOne("default-none").Config.PlayerCount);
        Assert.Equal(3, RunOne("explicit-3-on-4p", "--players", "3").Config.PlayerCount);

        // 批量入口：配置文件不写 Players 也算"未指定"；写了就按写的。
        string dir = SimFixtures.TempDir("players-config");
        string noPlayers = Path.Combine(dir, "no-players.json");
        File.WriteAllText(noPlayers, $"{{ \"MapId\": \"{TwoPlayerBaseMap.Id}\" }}");
        Assert.Equal(2, RunOne("config-no-players", "--config", noPlayers).Config.PlayerCount);
        string withPlayers = Path.Combine(dir, "with-players.json");
        File.WriteAllText(withPlayers, "{ \"Players\": [ { \"Difficulty\": \"Easy\" }, { \"Difficulty\": \"Easy\" }, { \"Difficulty\": \"Easy\" } ] }");
        Assert.Equal(3, RunOne("config-players", "--config", withPlayers).Config.PlayerCount);

        // 终端入口：不给人数 → 地图人数上限（2 人图出生区 1–2、对手 1 名）；4 人图仍是 3 名对手。
        var twoOut = new StringWriter();
        Assert.Equal(0, PlayCommand.Run(5, null, 1, AiDifficulty.Easy, new StringReader("q\n"), twoOut, TwoPlayerBaseMap.Create(), flagRisk: 0));
        Assert.Contains("对手 1 名", twoOut.ToString(), StringComparison.Ordinal);
        Assert.Contains("选择你的出生区（1–2）", twoOut.ToString(), StringComparison.Ordinal);
        var fourOut = new StringWriter();
        Assert.Equal(0, PlayCommand.Run(5, null, 1, AiDifficulty.Easy, new StringReader("q\n"), fourOut, flagRisk: 0));
        Assert.Contains("对手 3 名", fourOut.ToString(), StringComparison.Ordinal);

        // 图形版（src/godot 不在解决方案里，只能源码扫描）：每一处建局的人数都取自地图的人数上限（Min(4, MaxPlayers)，
        // 标准档预算表只到 4 人，故恒等于 MaxPlayers；4 封顶是表现层配色只备 4 名玩家的保险）。此项在改动前已成立，由 Godot 自检实测 2 人图建局 2 名玩家。
        string scripts = Path.Combine(FrontierFixtures.RepoRoot(), "src", "godot", "scripts");
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
