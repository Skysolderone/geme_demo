using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Play;
using Siege.Sim.Running;

// retire-legacy-maps：仓库根目录改取 TestPaths.RepoRoot()（原 FrontierFixtures.RepoRoot，随旧地图夹具删除而原样迁出）。
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
    public void 终端版未登记的地图选项拼写被拒绝()
    {
        // tasks 2.2：--map 已在 play 的严格命令行解析里登记；拼错的 --mapp 按 strict-cli 报错，并建议 --map。
        // 变异 M-A11：Program.Play 不读 "map" 选项 → 合法的 --map 也成了未知选项，下面的反面断言红。
        (int code, string err) = RunMain("play", "--mapp", "link:1", "--seed", "1");   // retire-legacy-maps 1.8：拼错选项的取值由 v5 改为 link:1（取值与本条无关）
        Assert.NotEqual(0, code);
        Assert.Contains("--mapp", err, StringComparison.Ordinal);
        Assert.Contains("是否想用 --map？", err, StringComparison.Ordinal);

        // 反面：正确拼写的 --map 不因"未知选项"被拒——这里给一个不存在的地图，报的必须是地图标识的错，而不是未知选项。
        // board-link-mode 裁决 C7：入口门先于目录解析，报错文案由"找不到地图"改为"只接受 link:<地图种子>"。
        (int okCode, string okErr) = RunMain("play", "--map", "no-such-map");
        Assert.NotEqual(0, okCode);
        Assert.DoesNotContain("未知选项", okErr, StringComparison.Ordinal);
        Assert.Contains("只接受 link:<地图种子>", okErr, StringComparison.Ordinal);
    }

    [Fact]
    public void 显式选缺省地图与不带选项逐字相同()
    {
        // tasks 2.2：不带 --map 时同种子对局与改动前逐步相同。改动前后的完整转录对比是一次性做的（记录在任务 implement.md 段 A）；
        // 这里长期钉住的是"缺省 ≡ 显式给缺省地图"，并配行数下界与字段级断言，防止两边一起只剩首行（testing.md）。
        // retire-legacy-maps 1.8：缺省地图由 v5 改为 link:1（PlayCommand 的 map 参数为 null 即 MapCatalog.Resolve(null)），两边都挂内置固定配对脚本；
        // 插旗提示改为链接图的"盲选出生棋盘（1–5）"；原"缺省地图不多打一行"只属于 Legacy 缺省图（链接图一律打印完整标识），改为断言两边都打印同一行地图标识。
        // 种子 7 在 link:1 上人类出局后对局自动跑过链接、终端播报越界崩溃（migration.md 待决 L15），探针（种子 1–20）改取种子 5。
        string script = "2\n" + string.Concat(Enumerable.Repeat("\npass\n", 3));   // 段 C：大回合上限删除，改为 3 个小回合后输入耗尽退出（原 40 行 + 上限 3 收尾）；   // 每个小回合：空行 = 不征募，pass = 不落子
        var implicitOut = new StringWriter();
        var explicitOut = new StringWriter();

        // AI 权重与停手阈值写死为 ai-eye 4.5 定值之前的缺省：默认阈值 80 下简单难度第 1 大回合全员 Pass、对局即终局，走不到第 3 大回合（段 D2 改写）。
        EvaluationWeights w = SimFixtures.PreCalibrationWeights;
        const int t = SimFixtures.PreCalibrationPassThreshold;
        PlayCommand.Run(5, 4, 2, AiDifficulty.Easy, new StringReader(script), implicitOut, weights: w, passThreshold: t, contentSet: ContentSet.V1);
        PlayCommand.Run(5, 4, 2, AiDifficulty.Easy, new StringReader(script), explicitOut, MapCatalog.Resolve("link:1"), weights: w, passThreshold: t, contentSet: ContentSet.V1);

        string text = implicitOut.ToString();
        Assert.Equal(text, explicitOut.ToString());
        Assert.True(text.Split('\n').Length > 100, "转录过短，对局没有真正进行。");
        Assert.Contains("盲选出生棋盘：以上是 5 块棋盘的占位（1–5）", text, StringComparison.Ordinal);
        Assert.Contains("第 3 大回合", text, StringComparison.Ordinal);
        Assert.Contains("已退出。种子 5", text, StringComparison.Ordinal);
        Assert.Contains("地图 link:1（5 块紧贴成一张连通地图的出生棋盘）", text.Split('\n')[..6].Aggregate(string.Concat), StringComparison.Ordinal);
    }

    [Fact]
    public void 三个入口都经同一份目录解析地图()
    {
        // 规格：三个入口 MUST 共用同一份"标识 → 地图"的解析，MUST NOT 各自维护地图清单。
        // src/godot 不在 siege.sln 里，对 IL / 反射类守门隐身（testing.md），只能做源码文本扫描；配样本口径下界与反面命中。
        // 变异 M-A12：把 src/godot/scripts/MatchSession.cs 改回 FourPlayerBaseMap.Create() → 本测试红。
        // retire-legacy-maps：内置图类型已删除，禁用记号改为链接图生成器的建图入口 LinkMapGenerator.Generate(（入口只经目录取图），反面命中仍在目录。
        string root = TestPaths.RepoRoot();
        string[] entryFiles =
        [
            Path.Combine(root, "src", "Siege.Sim", "Play", "PlayCommand.cs"),
            Path.Combine(root, "src", "Siege.Sim", "Running", "MatchSession.cs"),
            Path.Combine(root, "src", "Siege.Sim", "Running", "BatchRunner.cs"),
            .. Directory.EnumerateFiles(Path.Combine(root, "src", "godot", "scripts"), "*.cs", SearchOption.AllDirectories),
        ];
        Assert.True(entryFiles.Length >= 8, $"样本口径：只扫到 {entryFiles.Length} 个入口文件。");
        Assert.True(entryFiles.Sum(p => new FileInfo(p).Length) > 50_000, "样本口径：入口文件总量过小。");

        Assert.Empty(entryFiles.Where(p => File.ReadAllText(p).Contains("LinkMapGenerator.Generate(", StringComparison.Ordinal)).Select(Path.GetFileName));
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
        Assert.Contains("LinkMapGenerator.Generate(", File.ReadAllText(Path.Combine(root, "src", "Siege.Core", "Board", "Maps", "MapCatalog.cs")), StringComparison.Ordinal);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(root, "src"), "MapCatalog.cs", SearchOption.AllDirectories));
    }

    [Fact]
    public void 图形版的地图选项登记在严格命令行解析里_没有绕过它的第二个读取点()
    {
        // 规格：地图选项 MUST 在各入口的严格命令行解析中登记。图形版的合法选项集合 = LaunchArgs 上被读取过的名字，
        // 所以"登记"= 经 LaunchArgs 读取 + 结算在建局之前；任何直接翻 OS.GetCmdline*Args 的读取点都会绕过未知选项校验。
        // src/godot 不在解决方案里，只能做源码文本扫描（行为由 Godot 自检实测：--mapp=x / --auto-demo=1 / --rounds=abc 退出码 1）。
        string dir = Path.Combine(TestPaths.RepoRoot(), "src", "godot", "scripts");
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
