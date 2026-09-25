using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
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
        int exit = PlayCommand.Run(42, 4, 1, AiDifficulty.Easy, new StringReader("9\n0\n3\n"), output, FrontierFixtures.Map(), flagRisk: 0);   // flag-contest：写死冒险概率 0
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
        PlayCommand.Run(7, 4, 2, AiDifficulty.Easy, new StringReader(script), implicitOut, weights: w, passThreshold: t, flagRisk: 0);
        PlayCommand.Run(7, 4, 2, AiDifficulty.Easy, new StringReader(script), explicitOut, MapCatalog.Resolve("siege-4p-base-v5"), weights: w, passThreshold: t, flagRisk: 0);

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
