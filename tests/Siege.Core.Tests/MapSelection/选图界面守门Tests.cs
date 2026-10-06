using System.Text.RegularExpressions;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.MapSelection;

/// <summary>
/// 规格：map-selection ——「地图的可选清单与标识解析 MUST 来自三个入口共用的那一份解析，界面层 MUST NOT 自带地图清单或自行生成地图」（tasks 3.1 守门）。
/// <c>src/godot</c> 不在解决方案里，对 IL / 反射类守门隐身（testing.md），只能做源码文本扫描；配样本口径下界与反面命中。
/// </summary>
public class 选图界面守门Tests
{
    private static readonly string Src = Path.Combine(FrontierFixtures.RepoRoot(), "src");

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

        // 选项只在视图模型里构造，且取自目录的内置棋盘图表（builtin-board-maps D5：界面只列内置棋盘图与随机棋盘图）。
        string[] optionBuilders = [.. files.Where(f => f.Text.Contains("new MapOption(", StringComparison.Ordinal)).Select(f => f.Path)];
        Assert.Equal(["Siege.Presentation/MapSelect/MapSelectModel.cs"], optionBuilders);
        string model = files.Single(f => f.Path == "Siege.Presentation/MapSelect/MapSelectModel.cs").Text;
        Assert.Contains("MapCatalog.BuiltinBoards", model, StringComparison.Ordinal);
        // builtin-board-maps D5：预选项是目录的缺省地图（4 人内置棋盘图），取自目录、不写字面量；随机棋盘图的标识经棋盘图标识的唯一实现拼出。
        Assert.Contains("MapCatalog.DefaultId", model, StringComparison.Ordinal);
        Assert.Contains("BoardMapId.Format(", model, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"""(board|gen):", RegexOptions.CultureInvariant), model);   // 不手拼标识

        // 内置图面向人的显示名同样只登记在目录里（裁决 3）：界面层出现任何一个显示名的字面量，就是自带了"标识 → 名字"对照表。
        // 变异 MC-20：Hud 选图面板里自带显示名对照表 → 本测试红。禁用词取自目录本身，新增内置图自动纳入。
        Assert.Equal(MapCatalog.BuiltinIds.Count, MapCatalog.BuiltinMaps.Count);
        foreach (BuiltinMapInfo map in MapCatalog.BuiltinMaps)
        {
            Assert.False(string.IsNullOrWhiteSpace(map.Title));
            Assert.NotEqual(map.Id, map.Title);
            string word = map.Title.Split(' ', '（')[0];   // "标准图 13×13" → "标准图"
            Assert.True(word.Length >= 3, $"显示名 {map.Title} 的首词太短，扫描会误伤。");
            Assert.Empty(files.Where(f => f.Text.Contains(map.Title, StringComparison.Ordinal)).Select(f => $"{f.Path}: {map.Title}"));
            Assert.Empty(files.Where(f => CodeLines(f.Text).Any(l => l.Contains(word, StringComparison.Ordinal))).Select(f => $"{f.Path}: {word}"));
            Assert.Contains($"\"{map.Title}\"", File.ReadAllText(Path.Combine(Src, "Siege.Core", "Board", "Maps", "MapCatalog.cs")), StringComparison.Ordinal);
        }

        // 反面：被禁的记号在它的归属处（目录）确实命中。
        string catalog = File.ReadAllText(Path.Combine(Src, "Siege.Core", "Board", "Maps", "MapCatalog.cs"));
        Assert.Matches(banned, catalog);
        Assert.Matches(banned, File.ReadAllText(Path.Combine(Src, "Siege.Core", "Board", "Maps", "FrontierMapV2.cs")));
    }

    [Fact]
    public void 选图界面不自行生成地图_地图只经目录解析()
    {
        // 变异 MC-9：GameRoot 选图阶段直接调生成器出图 → 本测试红（「生成器只经目录调用」同红）；
        // 变异 MC-10：选图阶段绕过目录、按文件自行读图 → 本测试红。
        (string Path, string Text)[] files = SelectionSources();

        // 变异 M-C3（board-map 段 C，实跑）：选图阶段直接调棋盘档生成器出图 → 本测试红（红 1）。
        var banned = new Regex(@"FrontierMapGenerator|FrontierMapLayout|BoardMapGenerator|BoardMapLayout|MapRandom|MapFile\.|new MapData\(|new TerrainData\(", RegexOptions.CultureInvariant);
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
        Assert.Contains("BoardMapGenerator.Generate(", File.ReadAllText(Path.Combine(Src, "Siege.Core", "Board", "Maps", "MapCatalog.cs")), StringComparison.Ordinal);
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
        Assert.Single(Regex.Matches(stage, @"GeneratedMapId\.FriendlySeed\(\(ulong\)Stopwatch\.GetTimestamp\(\)\)"));
        Assert.Single(Regex.Matches(stage, @"Stopwatch\."));
        Assert.DoesNotContain(files, f => f.Path.EndsWith("Hud.MapSelect.cs", StringComparison.Ordinal) && clock.IsMatch(f.Text));
    }

    [Fact]
    public void 内置棋盘图选中时随机棋盘图的控件整组隐藏_可见性只取自视图模型()
    {
        // builtin-board-maps design D5：内置棋盘图选中时不显示种子与调节控件，只显示完整标识与尺寸说明。
        // src/godot 不在 sln 里，只能做源码扫描（testing.md「引擎层的硬约束，交付时 MUST 自带源码扫描守门」）。钉三件事：
        // ① 种子行 / 换一张与人数行 / 棋盘数行都挂在同一个容器下，不挂在面板主体上；② 该容器的可见性全仓只写一处，且取自视图模型的 IsBoardSelected；
        // ③ 容器确实挂进了面板主体。
        // 变异（builtin-board-maps 段 D 检查，实跑）：K-D1 可见性写死 true → 红 1；K-D2 棋盘数行改挂面板主体 → 红 1；K-D3 可见性改取 Notice 是否为空 → 红 1。
        string code = PresentationFixtures.GodotScriptCode("Hud.MapSelect.cs");
        string show = PresentationFixtures.MethodBody(code, "public void ShowMapSelect(MapSelectModel model, string mapInfo)");
        string build = PresentationFixtures.MethodBody(code, "private void BuildMapSelect(MapSelectModel model)");

        foreach (string row in new[] { "seedRow", "tuneRow", "boardsRow" })
        {
            Assert.Single(Regex.Matches(build, $@"\bseeded\.AddChild\({row}\)"));
            Assert.Empty(Regex.Matches(build, $@"\bbody\.AddChild\({row}\)"));
        }

        Assert.Single(Regex.Matches(build, @"_mapSeededGroup\s*=\s*seeded\s*;"));
        Assert.Single(Regex.Matches(build, @"\bbody\.AddChild\(seeded\)"));

        // 可见性写入全仓（全部引擎层脚本）只有 ShowMapSelect 里这一处，右值是视图模型的 IsBoardSelected（直接写或经同一方法体里的局部变量）。
        string scripts = Path.Combine(Src, "godot", "scripts");
        string[] all = [.. Directory.EnumerateFiles(scripts, "*.cs").Select(p => PresentationFixtures.GodotScriptCode(Path.GetFileName(p)))];
        Assert.True(all.Length >= 20, $"样本口径：只扫到 {all.Length} 个脚本。");
        Assert.Equal(1, all.Sum(t => Regex.Matches(t, @"_mapSeededGroup\.Visible\s*=").Count));
        var write = Regex.Match(show, @"_mapSeededGroup\.Visible\s*=\s*([^;]+);");
        Assert.True(write.Success, "ShowMapSelect 里没有写随机棋盘图控件组的可见性。");
        string rhs = write.Groups[1].Value.Trim();
        if (rhs != "model.IsBoardSelected")
        {
            Assert.Matches(@"^[A-Za-z_]\w*$", rhs);
            Assert.Single(Regex.Matches(show, $@"\b{rhs}\s*=\s*model\.IsBoardSelected\s*;"));
            Assert.Single(Regex.Matches(show, $@"\b{rhs}\s*=[^=]"));   // 局部变量只赋值一次
        }

        // 反面：判据在文件里确实命中（换写法而让上面的 Regex 全部落空时会在这里红）。
        Assert.Contains("IsBoardSelected", show, StringComparison.Ordinal);
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
