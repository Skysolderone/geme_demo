using System.Text.RegularExpressions;
using Siege.Core.Board.Maps;

// retire-legacy-maps：仓库根目录改取 TestPaths.RepoRoot()（原 FrontierFixtures.RepoRoot，随旧地图夹具删除而原样迁出）。
namespace Siege.Core.Tests.MapSelection;

/// <summary>
/// 规格：map-selection ——「地图的可选清单与标识解析 MUST 来自三个入口共用的那一份解析，界面层 MUST NOT 自带地图清单或自行生成地图」（tasks 3.1 守门）。
/// <c>src/godot</c> 不在解决方案里，对 IL / 反射类守门隐身（testing.md），只能做源码文本扫描；配样本口径下界与反面命中。
/// </summary>
public class 选图界面守门Tests
{
    private static readonly string Src = Path.Combine(TestPaths.RepoRoot(), "src");

    /// <summary>选图界面的全部源码：表现层的选图视图模型 + 图形版脚本。</summary>
    private static (string Path, string Text)[] SelectionSources()
    {
        (string, string)[] files =
        [
            .. Directory.EnumerateFiles(Path.Combine(Src, "Siege.Presentation", "MapSelect"), "*.cs", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(Path.Combine(Src, "godot", "scripts"), "*.cs", SearchOption.AllDirectories))
                .Order(StringComparer.Ordinal)
                .Select(p => (Path.GetRelativePath(Src, p).Replace('\\', '/'), File.ReadAllText(p))),
        ];
        Assert.True(files.Length >= 14, $"样本口径：只扫到 {files.Length} 个文件。");
        Assert.Contains(files, f => f.Item1 == "Siege.Presentation/MapSelect/MapSelectModel.cs");
        Assert.Contains(files, f => f.Item1 == "godot/scripts/GameRoot.MapSelect.cs");
        Assert.Contains(files, f => f.Item1 == "godot/scripts/Hud.MapSelect.cs");
        return files;
    }

    /// <summary>去掉整行注释后的代码行（显示名的首词是普通中文词，既有注释里会自然出现；对照表只可能写在代码行里）。</summary>
    private static IEnumerable<string> CodeLines(string text) =>
        text.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal));

    [Fact]
    public void 选图界面不自带地图清单()
    {
        // 变异 MC-1：视图模型的清单改成字面量数组 → 本测试红；变异 MC-8：Hud 选图面板里手写三项标识 → 本测试红。
        (string Path, string Text)[] files = SelectionSources();

        // 内置图的标识字面量与内置图类型，一个都不得出现（注释里也不行——扫描不分代码与注释）。
        var banned = new Regex(@"siege-\d+p|siege-frontier|FourPlayerBaseMap|FrontierMapV\d", RegexOptions.CultureInvariant);
        Assert.Empty(files.Where(f => banned.IsMatch(f.Text)).Select(f => f.Path));

        // board-link-mode 裁决 D7：新局界面没有地图清单——任何选图源码里都不得再出现选项构造、目录的内置表或内置标识清单；
        // 唯一的标识 link:<种子> 经链接图标识的唯一实现拼出，不手拼。
        Assert.Empty(files.Where(f => f.Text.Contains("new MapOption(", StringComparison.Ordinal) || f.Text.Contains("MapCatalog.Builtin", StringComparison.Ordinal)).Select(f => f.Path));
        string model = files.Single(f => f.Path == "Siege.Presentation/MapSelect/MapSelectModel.cs").Text;
        Assert.Contains("LinkMapId.Format(", model, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"""(board|gen|link):", RegexOptions.CultureInvariant), model);   // 不手拼标识
        Assert.DoesNotMatch(new Regex(@"BoardMapId|GeneratedMapId\.(Format|Parse)", RegexOptions.CultureInvariant), model);   // 旧标识族的拼装 / 解析不再出现

        // retire-legacy-maps：内置图、显示名表（BuiltinMaps / BuiltinIds）与内置图类型随旧地图删除——目录不再有任何"内置"成员，
        // 原"显示名只登记在目录里"的逐项扫描失去对象，改为断言目录的公开面只剩缺省标识与解析（反射，比扫源码更直接）。
        Assert.Equal(
            ["DefaultId", "Resolve"],
            typeof(MapCatalog).GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly)
                .Select(m => m.Name).Distinct().Order(StringComparer.Ordinal));
        Assert.DoesNotContain(CodeLines(File.ReadAllText(Path.Combine(Src, "Siege.Core", "Board", "Maps", "MapCatalog.cs"))), l => l.Contains("Builtin", StringComparison.Ordinal));

        // 反面：禁用正则确实能命中旧标识与旧类型名（旧的归属处 MapCatalog 内置表 / FrontierMapV2.cs 已删除，改用字面样本）。
        Assert.Matches(banned, "siege-4p-base-v5");
        Assert.Matches(banned, "siege-frontier-v2");
        Assert.Matches(banned, "FourPlayerBaseMap.Create()");
    }

    [Fact]
    public void 选图界面不自行生成地图_地图只经目录解析()
    {
        // 变异 MC-9：GameRoot 选图阶段直接调生成器出图 → 本测试红（「生成器只经目录调用」同红）；
        // 变异 MC-10：选图阶段绕过目录、按文件自行读图 → 本测试红。
        (string Path, string Text)[] files = SelectionSources();

        // 变异 M-C3（board-map 段 C，实跑）：选图阶段直接调棋盘档生成器出图 → 本测试红（红 1）。
        var banned = new Regex(@"FrontierMapGenerator|FrontierMapLayout|BoardMapGenerator|BoardMapLayout|LinkMapGenerator|MapRandom|MapFile\.|new MapData\(|new TerrainData\(", RegexOptions.CultureInvariant);
        Assert.Empty(files.Where(f => banned.IsMatch(f.Text)).Select(f => f.Path));

        // 视图模型只产出标识，不解析地图；解析发生在图形版入口，且只经目录。
        string model = files.Single(f => f.Path == "Siege.Presentation/MapSelect/MapSelectModel.cs").Text;
        Assert.DoesNotContain("MapCatalog.Resolve(", model, StringComparison.Ordinal);
        Assert.DoesNotContain("MapData", model, StringComparison.Ordinal);
        string stage = files.Single(f => f.Path == "godot/scripts/GameRoot.MapSelect.cs").Text;
        Assert.Contains("MapCatalog.Resolve(_select!.CurrentId)", stage, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(stage, @"MapCatalog\.Resolve\("));

        // 反面：被禁的记号在目录里确实命中。
        Assert.Matches(banned, File.ReadAllText(Path.Combine(Src, "Siege.Core", "Board", "Maps", "MapCatalog.cs")));
        Assert.Contains("LinkMapGenerator.Generate(", File.ReadAllText(Path.Combine(Src, "Siege.Core", "Board", "Maps", "MapCatalog.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void 视图模型不读时钟_新种子只由图形版入口注入()
    {
        // 变异 MC-11：Reroll 里自己读 Stopwatch 取种子 → 本测试红。
        (string Path, string Text)[] files = SelectionSources();
        var clock = new Regex(@"Stopwatch|DateTime|Environment\.TickCount|Random\.Shared|new Random\(|Guid\.NewGuid", RegexOptions.CultureInvariant);

        Assert.Empty(files.Where(f => f.Path.StartsWith("Siege.Presentation/", StringComparison.Ordinal) && clock.IsMatch(f.Text)).Select(f => f.Path));

        // 图形版里取新地图种子的只有选图阶段这一处：时间戳先折成便于读写的短种子再交给视图模型。
        string stage = files.Single(f => f.Path == "godot/scripts/GameRoot.MapSelect.cs").Text;
        // retire-legacy-maps：短种子折叠函数随 GeneratedMapId 删除迁入 LinkMapId（逐位相同，见 选图视图模型Tests）。
        Assert.Single(Regex.Matches(stage, @"LinkMapId\.FriendlySeed\(\(ulong\)Stopwatch\.GetTimestamp\(\)\)"));
        Assert.Single(Regex.Matches(stage, @"Stopwatch\."));
        Assert.DoesNotContain(files, f => f.Path.EndsWith("Hud.MapSelect.cs", StringComparison.Ordinal) && clock.IsMatch(f.Text));
    }

    [Fact]
    public void 选图启动选项登记在严格命令行解析里()
    {
        string root = File.ReadAllText(Path.Combine(Src, "godot", "scripts", "GameRoot.cs"));
        int flag = root.IndexOf("args.Flag(\"map-select\")", StringComparison.Ordinal);
        int settle = root.IndexOf("args.EnsureRecognized();", StringComparison.Ordinal);
        Assert.True(flag > 0 && settle > flag, "--map-select 必须经 LaunchArgs 读取，且在结算之前。");
    }
}
