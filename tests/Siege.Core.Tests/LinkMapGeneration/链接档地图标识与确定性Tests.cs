using System.Text.RegularExpressions;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;

namespace Siege.Core.Tests.LinkMapGeneration;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/link-map-generation —— Requirement: 链接档地图标识与确定性（map-expand-25 D1 / D8：标识 link:&lt;种子&gt;:&lt;人数&gt;，人数进入两条地图流的派生）；
/// map-definition —— Requirement: 旧地图降级为测试夹具（Scenario 夹具仍可加载）；map-generation —— Requirement: 既有生成器降级为测试夹具（Scenario 夹具生成不变）。
/// </summary>
/// <remarks>
/// 变异验证（段 A 实跑，只跑本类）：
/// M-A6 MapCatalog.Resolve 不再识别 link: 而全部转交 Legacy → 「目录解析 link 标识」红（Legacy 把 link:1 当文件路径报 FileNotFound）；
/// M-A7 LinkMapId.Parse 接受 parts.Length == 3 → 「标识解析与规范化」红（map-expand-25 起三段是正式格式，此条记录作废，由下面 M-X* 取代）。
/// map-expand-25 段 A1 变异（只跑本类）：M-X1 Parse 去掉旧两段格式的专门报错分支 → 「标识解析与规范化」红（消息不含"已作废"）；
/// M-X2 MapRandom.Create 去掉人数一项 → 「新地表子流与布局子流互不扰动」与「不同人数不同图」各红。
/// </remarks>
public class 链接档地图标识与确定性Tests
{
    [Fact]
    public void 标识解析与规范化()
    {
        Assert.Equal("link:42:4", LinkMapId.Format(42, 4));
        Assert.Equal((42UL, 2), LinkMapId.Parse("link:42:2"));
        Assert.Equal((42UL, 3), LinkMapId.Parse(" link:042:03 "));
        Assert.Equal("link:42:3", LinkMapId.Normalize(" link:042:03 "));
        Assert.Equal("link:18446744073709551615:4", LinkMapId.Normalize("link:18446744073709551615:4"));
        Assert.Equal("link:0:2", LinkMapId.Normalize("link:0:2"));
        Assert.Throws<ArgumentOutOfRangeException>(() => LinkMapId.Format(1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => LinkMapId.Format(1, 5));

        Assert.True(LinkMapId.IsLinkMap("link"));
        Assert.True(LinkMapId.IsLinkMap("link:1:4"));
        Assert.True(LinkMapId.IsBareRequest(" link "));
        Assert.False(LinkMapId.IsBareRequest("link:1:4"));
        Assert.False(LinkMapId.IsLinkMap("linked:1"));
        Assert.False(LinkMapId.IsLinkMap("board:1"));
        Assert.False(LinkMapId.IsLinkMap(null));

        // 写错的标识：消息说明新格式并给出示例 link:1:4。
        foreach (string bad in new[] { "link", "link:", "link:abc", "link:-1:4", "link:1:n7", "link:1:p6", "board:1", "link:18446744073709551616:4", "", "link: 1:4", "link:1:4:4", "link:1:", "link:1:x" })
        {
            FormatException ex = Assert.Throws<FormatException>(() => LinkMapId.Parse(bad));
            Assert.Contains("link:<地图种子>:<人数>", ex.Message, StringComparison.Ordinal);
            Assert.Contains("link:1:4", ex.Message, StringComparison.Ordinal);
        }

        // 人数只有 2–4。
        foreach (string bad in new[] { "link:1:0", "link:1:1", "link:1:5", "link:1:99999999999" })
        {
            FormatException ex = Assert.Throws<FormatException>(() => LinkMapId.Parse(bad));
            Assert.Contains("人数", ex.Message, StringComparison.Ordinal);
            Assert.Contains("2–4", ex.Message, StringComparison.Ordinal);
        }

        // Scenario（map-expand-25 Q4）：旧的两段写法 link:<种子> 报错作废，说明原因并给出补上人数的写法。
        foreach ((string old, string hint) in new[] { ("link:42", "link:42:4"), (" link:7 ", "link:7:4"), ("link:0", "link:0:4") })
        {
            FormatException ex = Assert.Throws<FormatException>(() => LinkMapId.Parse(old));
            Assert.Contains("已作废", ex.Message, StringComparison.Ordinal);
            Assert.Contains(hint, ex.Message, StringComparison.Ordinal);
            Assert.Contains("link:<地图种子>:<人数>", ex.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void 同种子同初始图()
    {
        // Scenario：用 link:12345:4 连续生成两次初始地图 → 两次的导出文件逐字节相同；标识即规范化标识，人数即地图的最大人数。
        MapData first = LinkMapGenerator.Generate("link:12345:4");
        MapData second = LinkMapGenerator.Generate(12345, 4);
        Assert.Equal(MapFile.ToJson(first), MapFile.ToJson(second));
        Assert.Equal("link:12345:4", first.Id);
        Assert.Equal(4, first.MaxPlayers);
        Assert.Equal(MapFile.Digest(first), MapFile.Digest(second));

        // Generate 按 (种子, 人数) 缓存，上面两次拿到的是同一个对象；这里绕过缓存再独立生成两次（各自从头消费布局流与新地表子流），逐字节相同且与缓存入口同图。
        foreach (int players in new[] { 2, 3, 4 })
        {
            string fresh1 = MapFile.ToJson(LinkMapGenerator.GenerateDetailed(12345, players).Map);
            string fresh2 = MapFile.ToJson(LinkMapGenerator.GenerateDetailed(12345, players).Map);
            Assert.Equal(fresh1, fresh2);
            Assert.Equal(MapFile.ToJson(LinkMapGenerator.Generate(12345, players)), fresh1);
        }

        // 不同种子不同图（样本口径：相等断言不是恒真）。
        Assert.NotEqual(MapFile.ToJson(LinkMapGenerator.Generate(1, 4)), MapFile.ToJson(LinkMapGenerator.Generate(2, 4)));

        // 文件往返：导出再读入逐项相等（规格档、版本号、棋盘清单）。
        MapData restored = MapFile.FromJson(MapFile.ToJson(first));
        Assert.Equal(MapProfile.Link, restored.Profile);
        Assert.Equal(0, restored.MapVersion);
        Assert.Equal(first.Boards, restored.Boards);
        Assert.Equal(MapFile.ToJson(first), MapFile.ToJson(restored));
    }

    [Fact]
    public void 不同人数不同图()
    {
        // map-expand-25 D8：人数是生成参数，进入两条地图流的派生——同种子不同人数的两条布局流取值不同，生成的图也不同（不只是岛更大：平台的落位与主题都另抽）。
        foreach (ulong seed in new ulong[] { 1, 7, 12345 })
        {
            ulong[][] layouts = [.. new[] { 2, 3, 4 }.Select(n => Take(MapRandom.ForLinkLayout(seed, n), 8))];
            ulong[][] surfaces = [.. new[] { 2, 3, 4 }.Select(n => Take(MapRandom.ForLinkSurfaces(seed, n), 8))];
            for (int i = 0; i < 3; i++)
            {
                for (int j = i + 1; j < 3; j++)
                {
                    Assert.NotEqual(layouts[i], layouts[j]);
                    Assert.NotEqual(surfaces[i], surfaces[j]);
                }
            }

            MapData two = LinkMapGenerator.Generate(seed, 2);
            MapData four = LinkMapGenerator.Generate(seed, 4);
            Assert.Equal(2, two.MaxPlayers);
            Assert.Equal(4, four.MaxPlayers);
            Assert.Equal($"link:{seed}:2", two.Id);
            Assert.NotEqual(two.Height, four.Height);   // seat-gap-25 E1：三档岛宽同为 45，2 人岛高 33、4 人 45（原 map-expand-25 断言宽不同）

            // 座位位置只由人数决定；中立平台相对岛左下角的摆法与主题另抽，2 人与 3 人的图不同。
            static string Shape(MapData m) => string.Join(";", m.Boards.Select(p => $"{p.Origin.X - m.Boards.Min(b => b.Origin.X)},{p.Origin.Y - m.Boards.Min(b => b.Origin.Y)},{p.Width}x{p.Height},{p.Theme}"));
            Assert.NotEqual(Shape(two), Shape(LinkMapGenerator.Generate(seed, 3)));
        }
    }

    [Fact]
    public void 对局种子不影响地图()
    {
        // retire-legacy-maps：仓库根目录改取 TestPaths.RepoRoot()（原 FrontierFixtures.RepoRoot，原样迁出）。
        // Scenario：生成只用地图种子派生的随机源与整数运算——生成器源码不得出现对局种子、时钟、环境与浮点。
        string src = Path.Combine(TestPaths.RepoRoot(), "src", "Siege.Core", "Board", "Maps");
        string[] files = [Path.Combine(src, "LinkMapGenerator.cs"), Path.Combine(src, "LinkMapGenerator.Themes.cs"), Path.Combine(src, "LinkMapGenerator.Surfaces.cs"), Path.Combine(src, "LinkMapId.cs")];   // link-map-terrain：主题地形拆在 .Themes.cs；link-map-surfaces：新地表叠加在 .Surfaces.cs
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
        ulong[] layout = Take(MapRandom.ForLinkLayout(12345, 4), 8);
        Assert.Equal(layout, Take(MapRandom.ForLinkLayout(12345, 4), 8));   // 样本口径：同种子同序列，下面的"不等"不是因为每次取值都变
        foreach (string name in new[] { GameSeed.RelicSpawn(4), GameSeed.Recruit, GameSeed.Setup, GameSeed.FlagPick, GameSeed.CarryAi })
        {
            Assert.NotEqual(layout, Take(game.Stream(name), 8));
        }

        // ② 地图随机源复用了 RandomStream 的 internal 构造器与 SplitMix64：钉住对局种子 12345 下两条命名子流的首个取值，
        //    证明复用没有改动对局侧的派生（黄金值原样沿用删除前的测试；zone-pick 已随旧路径删除，relic-gen 已随旧信物删除）。
        Assert.Equal(
            [1172862400557011553UL, 18416610492839591364UL],
            new[] { GameSeed.Recruit, GameSeed.Setup }.Select(name => game.Stream(name).NextUInt64()));

        // ③ 源码扫描：生成侧（MapRandom / 链接档生成器与标识）不见对局种子；Maps 目录下凡碰地图随机源的文件都在名单里；
        //    对局流程（Siege.Core/Match）不见地图随机源、不直接生成地图、不出现地图种子——它只拿到生成好的 MapData 与标识字符串。
        string core = Path.Combine(TestPaths.RepoRoot(), "src", "Siege.Core");
        string maps = Path.Combine(core, "Board", "Maps");
        string[] generatorNames = ["MapRandom.cs", "LinkMapGenerator.cs", "LinkMapGenerator.Themes.cs", "LinkMapGenerator.Surfaces.cs", "LinkMapId.cs"];
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
    public void 新地表子流与布局子流互不扰动()
    {
        // link-map-surfaces D1 / 裁决 E4：新地表叠加用独立子流 MapRandom.ForLinkSurfaces（只由地图种子派生、域常量不同）——
        // 同种子可复现；与布局流、对局各条命名子流都不同构；叠加整段消费多少次都不改变布局流的取值（叠加是在布局流用完之后另起一条流，不共享状态）。
        foreach (ulong seed in new ulong[] { 1, 7, 12345 })
        {
            ulong[] surfaces = Take(MapRandom.ForLinkSurfaces(seed, 4), 8);
            Assert.Equal(surfaces, Take(MapRandom.ForLinkSurfaces(seed, 4), 8));
            Assert.NotEqual(Take(MapRandom.ForLinkLayout(seed, 4), 8), surfaces);
            Assert.NotEqual(Take(MapRandom.ForLinkSurfaces(seed + 1, 4), 8), surfaces);
            Assert.NotEqual(Take(MapRandom.ForLinkSurfaces(seed, 2), 8), surfaces);
            var game = new GameSeed(seed);
            foreach (string name in new[] { GameSeed.RelicSpawn(4), GameSeed.Recruit, GameSeed.Setup, GameSeed.FlagPick, GameSeed.CarryAi })
            {
                Assert.NotEqual(Take(game.Stream(name), 8), surfaces);
            }
        }

        // 子流名可辨（诊断用），两条地图流各自独立构造。
        Assert.Equal("map-link-surfaces", MapRandom.ForLinkSurfaces(1, 4).Name);
        Assert.Equal("map-link", MapRandom.ForLinkLayout(1, 4).Name);
        Assert.Contains("MapRandom.ForLinkSurfaces(", File.ReadAllText(Path.Combine(TestPaths.RepoRoot(), "src", "Siege.Core", "Board", "Maps", "LinkMapGenerator.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void 目录解析link标识()
    {
        // D8：MapCatalog.Resolve 识别 link:<种子>:<人数>；裸 link 不进规则内核；旧两段写法报作废。
        Assert.Equal(MapFile.ToJson(LinkMapGenerator.Generate(7, 3)), MapFile.ToJson(MapCatalog.Resolve(" link:7:3 ")));
        Assert.Equal("link:7:3", MapCatalog.Resolve("link:007:3").Id);

        FormatException bare = Assert.Throws<FormatException>(() => MapCatalog.Resolve("link"));
        Assert.Contains("没有带地图种子", bare.Message, StringComparison.Ordinal);
        Assert.Contains("link:1:4", bare.Message, StringComparison.Ordinal);
        Assert.Throws<FormatException>(() => MapCatalog.Resolve("link:x"));
        Assert.Contains("已作废", Assert.Throws<FormatException>(() => MapCatalog.Resolve("link:7")).Message, StringComparison.Ordinal);

        // board-link-mode 裁决 D7：Resolve 不再转交旧标识族——一律报错并说明只接受 link:<地图种子>:<人数>。缺省（null / 空白）为 link:1:4（map-expand-25 D1）。
        Assert.Equal("link:1:4", MapCatalog.DefaultId);
        Assert.Equal("link:1:4", MapCatalog.Resolve(null).Id);
        foreach (string legacy in new[] { "siege-4p-base-v5", "gen:12345", "board:12345", "siege-frontier-v2", "gen", "board", "maps/x.json" })
        {
            FormatException rejected = Assert.Throws<FormatException>(() => MapCatalog.Resolve(legacy));
            Assert.Contains("只接受 link:<地图种子>:<人数>", rejected.Message, StringComparison.Ordinal);
            Assert.Contains(legacy, rejected.Message, StringComparison.Ordinal);
        }
    }
}
