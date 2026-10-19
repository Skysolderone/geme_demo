using System.Text.RegularExpressions;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;

namespace Siege.Core.Tests.LinkMapGeneration;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/link-map-generation —— Requirement: 链接档地图标识与确定性；
/// map-definition —— Requirement: 旧地图降级为测试夹具（Scenario 夹具仍可加载）；map-generation —— Requirement: 既有生成器降级为测试夹具（Scenario 夹具生成不变）。
/// </summary>
/// <remarks>
/// 变异验证（段 A 实跑，只跑本类）：
/// M-A6 MapCatalog.Resolve 不再识别 link: 而全部转交 Legacy → 「目录解析 link 标识」红（Legacy 把 link:1 当文件路径报 FileNotFound）；
/// M-A7 LinkMapId.Parse 接受 parts.Length == 3 → 「标识解析与规范化」红。
/// </remarks>
public class 链接档地图标识与确定性Tests
{
    [Fact]
    public void 标识解析与规范化()
    {
        Assert.Equal("link:42", LinkMapId.Format(42));
        Assert.Equal(42UL, LinkMapId.Parse("link:42"));
        Assert.Equal(42UL, LinkMapId.Parse(" link:042 "));
        Assert.Equal("link:42", LinkMapId.Normalize(" link:042 "));
        Assert.Equal("link:18446744073709551615", LinkMapId.Normalize("link:18446744073709551615"));
        Assert.Equal("link:0", LinkMapId.Normalize("link:0"));

        Assert.True(LinkMapId.IsLinkMap("link"));
        Assert.True(LinkMapId.IsLinkMap("link:1"));
        Assert.True(LinkMapId.IsBareRequest(" link "));
        Assert.False(LinkMapId.IsBareRequest("link:1"));
        Assert.False(LinkMapId.IsLinkMap("linked:1"));
        Assert.False(LinkMapId.IsLinkMap("board:1"));
        Assert.False(LinkMapId.IsLinkMap(null));

        foreach (string bad in new[] { "link", "link:", "link:abc", "link:-1", "link:1:n7", "link:1:p6", "board:1", "link:18446744073709551616", "", "link: 1" })
        {
            FormatException ex = Assert.Throws<FormatException>(() => LinkMapId.Parse(bad));
            Assert.Contains("link:<地图种子>", ex.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void 同种子同初始图()
    {
        // Scenario：用 link:12345 连续生成两次初始地图 → 两次的导出文件逐字节相同；标识即规范化标识。
        MapData first = LinkMapGenerator.Generate("link:12345");
        MapData second = LinkMapGenerator.Generate(12345);
        Assert.Equal(MapFile.ToJson(first), MapFile.ToJson(second));
        Assert.Equal("link:12345", first.Id);
        Assert.Equal(MapFile.Digest(first), MapFile.Digest(second));

        // 不同种子不同图（样本口径：相等断言不是恒真）。
        Assert.NotEqual(MapFile.ToJson(LinkMapGenerator.Generate(1)), MapFile.ToJson(LinkMapGenerator.Generate(2)));

        // 文件往返：导出再读入逐项相等（规格档、版本号、棋盘清单）。
        MapData restored = MapFile.FromJson(MapFile.ToJson(first));
        Assert.Equal(MapProfile.Link, restored.Profile);
        Assert.Equal(0, restored.MapVersion);
        Assert.Equal(first.Boards, restored.Boards);
        Assert.Equal(MapFile.ToJson(first), MapFile.ToJson(restored));
    }

    [Fact]
    public void 对局种子不影响地图()
    {
        // retire-legacy-maps：仓库根目录改取 TestPaths.RepoRoot()（原 FrontierFixtures.RepoRoot，原样迁出）。
        // Scenario：生成只用地图种子派生的随机源与整数运算——生成器源码不得出现对局种子、时钟、环境与浮点。
        string src = Path.Combine(TestPaths.RepoRoot(), "src", "Siege.Core", "Board", "Maps");
        string[] files = [Path.Combine(src, "LinkMapGenerator.cs"), Path.Combine(src, "LinkMapGenerator.Themes.cs"), Path.Combine(src, "LinkMapId.cs")];   // link-map-terrain：主题地形拆在 .Themes.cs
        var banned = new Regex(@"GameSeed|DateTime|Stopwatch|Environment\.TickCount|Random\.Shared|new Random\(|Guid\.NewGuid|\bdouble\b|\bfloat\b|\bdecimal\b|Math\.Round|Math\.Floor|Math\.Ceiling");
        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            Assert.True(text.Length > 1000, file);
            Assert.DoesNotMatch(banned, text);
            Assert.DoesNotContain("Siege.Core.Match", text, StringComparison.Ordinal);
        }

        // 反面：禁用词表确实能命中它该命中的地方。
        Assert.Matches(banned, File.ReadAllText(Path.Combine(TestPaths.RepoRoot(), "src", "Siege.Core", "Determinism", "GameSeed.cs")));
    }

    [Fact]
    public void 地图随机源与对局随机互不可见()
    {
        // retire-legacy-maps（check 补回覆盖）：自已删除的 MapGeneration/地图随机源Tests 三条——「地图随机源不与任何对局子流同构」
        // 「对局四条子流的取值不因地图随机源而变」「生成器不见对局种子_对局流程不见地图种子」——迁来。载体从随旧生成器删除的
        // MapRandom.ForAttempt（gen:）换成仍在用的 MapRandom.ForLinkLayout；所守边界（map-generator D2：地图随机源不是对局种子的子流，两边互不可见）不变。
        // 上面的「对局种子不影响地图」只扫 LinkMapGenerator.cs / LinkMapId.cs 单向，不含 MapRandom.cs 与"对局流程不见地图随机源"的反向。

        // ① 同一个 64 位数既当地图种子又当对局种子：链接档布局序列不等于对局的任何一条命名子流。
        var game = new GameSeed(12345);
        ulong[] layout = Take(MapRandom.ForLinkLayout(12345), 8);
        Assert.Equal(layout, Take(MapRandom.ForLinkLayout(12345), 8));   // 样本口径：同种子同序列，下面的"不等"不是因为每次取值都变
        foreach (string name in new[] { GameSeed.RelicGeneration, GameSeed.Recruit, GameSeed.Setup, GameSeed.FlagPick, GameSeed.CarryAi })
        {
            Assert.NotEqual(layout, Take(game.Stream(name), 8));
        }

        // ② 地图随机源复用了 RandomStream 的 internal 构造器与 SplitMix64：钉住对局种子 12345 下三条命名子流的首个取值，
        //    证明复用没有改动对局侧的派生（黄金值原样沿用删除前的测试；第四条 zone-pick 子流已随旧路径删除）。
        Assert.Equal(
            [10602760394128728250UL, 1172862400557011553UL, 18416610492839591364UL],
            new[] { GameSeed.RelicGeneration, GameSeed.Recruit, GameSeed.Setup }.Select(name => game.Stream(name).NextUInt64()));

        // ③ 源码扫描：生成侧（MapRandom / 链接档生成器与标识）不见对局种子；Maps 目录下凡碰地图随机源的文件都在名单里；
        //    对局流程（Siege.Core/Match）不见地图随机源、不直接生成地图、不出现地图种子——它只拿到生成好的 MapData 与标识字符串。
        string core = Path.Combine(TestPaths.RepoRoot(), "src", "Siege.Core");
        string maps = Path.Combine(core, "Board", "Maps");
        string[] generatorNames = ["MapRandom.cs", "LinkMapGenerator.cs", "LinkMapGenerator.Themes.cs", "LinkMapId.cs"];
        Assert.All(generatorNames, name => Assert.DoesNotContain("GameSeed", File.ReadAllText(Path.Combine(maps, name)), StringComparison.Ordinal));
        // MapRandom.cs 同样禁浮点、时钟与环境（原 生成确定性Tests.生成器源码不含散列次序遍历_浮点_时钟与环境 扫过它；LinkMapGenerator / LinkMapId 由上面「对局种子不影响地图」扫）。
        Assert.DoesNotMatch(
            new Regex(@"DateTime|Stopwatch|Environment|Random\.Shared|new Random\(|Guid\.NewGuid|\bdouble\b|\bfloat\b|\bdecimal\b|MathF|Math\.(Round|Floor|Ceiling|Sqrt|Pow)"),
            File.ReadAllText(Path.Combine(maps, "MapRandom.cs")));
        Assert.Contains("MapRandom.ForLinkLayout(", File.ReadAllText(Path.Combine(maps, "LinkMapGenerator.cs")), StringComparison.Ordinal);   // 反面：扫到的确实是生成器
        string[] touching = [.. Directory.EnumerateFiles(maps, "*.cs").Where(path => File.ReadAllText(path).Contains("MapRandom", StringComparison.Ordinal)).Select(path => Path.GetFileName(path))];
        Assert.Contains("LinkMapGenerator.cs", touching);
        Assert.All(touching, name => Assert.Contains(name, generatorNames));

        string[] matchFiles = [.. Directory.EnumerateFiles(Path.Combine(core, "Match"), "*.cs", SearchOption.AllDirectories)];
        Assert.True(matchFiles.Length >= 10, $"样本口径：Match 目录只扫到 {matchFiles.Length} 个文件。");
        Assert.Contains(matchFiles, path => File.ReadAllText(path).Contains("GameSeed", StringComparison.Ordinal));   // 反面：对局流程确实在用对局种子
        var mapSeedToken = new Regex(@"MapRandom|LinkMapGenerator\.Generate\(|(?i:mapseed)");
        Assert.Empty(matchFiles.Where(path => mapSeedToken.IsMatch(File.ReadAllText(path))).Select(path => Path.GetFileName(path)));
        Assert.Matches(mapSeedToken, File.ReadAllText(Path.Combine(core, "Board", "Maps", "MapCatalog.cs")));   // 反面：记号表能命中目录里的生成调用
    }

    private static ulong[] Take(Siege.Core.Determinism.RandomStream stream, int count) => [.. Enumerable.Range(0, count).Select(_ => stream.NextUInt64())];

    [Fact]
    public void 目录解析link标识()
    {
        // D8：MapCatalog.Resolve 识别 link:<种子>；裸 link 不进规则内核。
        Assert.Equal(MapFile.ToJson(LinkMapGenerator.Generate(7)), MapFile.ToJson(MapCatalog.Resolve(" link:7 ")));
        Assert.Equal("link:7", MapCatalog.Resolve("link:007").Id);

        FormatException bare = Assert.Throws<FormatException>(() => MapCatalog.Resolve("link"));
        Assert.Contains("没有带地图种子", bare.Message, StringComparison.Ordinal);
        Assert.Contains("link:1", bare.Message, StringComparison.Ordinal);
        Assert.Throws<FormatException>(() => MapCatalog.Resolve("link:x"));

        // board-link-mode 裁决 D7：段 D 收口后 Resolve 不再转交旧标识族——一律报错并说明只接受 link:<地图种子>。
        // 缺省（null / 空白）也从 v5 改为 link:1。
        // retire-legacy-maps：旧标识族与 MapCatalog.Legacy 已删除；"Legacy 仍可解析"的反面断言随之删去，FrontierMapV2.Id 换成字面量。
        Assert.Equal("link:1", MapCatalog.Resolve(null).Id);
        foreach (string legacy in new[] { "siege-4p-base-v5", "gen:12345", "board:12345", "siege-frontier-v2", "gen", "board", "maps/x.json" })
        {
            FormatException rejected = Assert.Throws<FormatException>(() => MapCatalog.Resolve(legacy));
            Assert.Contains("只接受 link:<地图种子>", rejected.Message, StringComparison.Ordinal);
            Assert.Contains(legacy, rejected.Message, StringComparison.Ordinal);
        }
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 同链接序列同最终图()
    {
        // Scenario：在 link:12345 上两次都依次链接 (1,4)、(2,5)、(1,2)（规格 1 起编号；这里用 0 起的出生棋盘编号 (0,3)、(1,4)、(0,1)）→ 两次的最终地图逐格相同。
        LinkStep[] steps = [new(0, 3, LinkSide.East), new(1, 4, LinkSide.East), new(0, 1, LinkSide.North)];
        Assert.Equal(MapFile.ToJson(Run(steps)), MapFile.ToJson(Run(steps)));

        // 样本口径：换一条序列最终图就不同，上面的相等断言不是恒真。
        Assert.NotEqual(MapFile.ToJson(Run(steps)), MapFile.ToJson(Run([new(0, 4, LinkSide.East), new(1, 3, LinkSide.East), new(0, 1, LinkSide.North)])));

        static MapData Run(LinkStep[] steps)
        {
            MapData map = LinkMapGenerator.Generate(12345);
            foreach (LinkStep step in steps)
            {
                map = LinkMapGenerator.Link(map, step.X, step.Y, step.Side).Map;
            }

            return map;
        }
    }
}
