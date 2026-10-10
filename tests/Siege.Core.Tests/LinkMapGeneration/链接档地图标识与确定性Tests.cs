using System.Text.RegularExpressions;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

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
        MapData second = LinkMapGenerator.Generate(12345, maxAttempts: 8);   // 不走缓存的路径也逐字节相同
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
        // Scenario：生成只用地图种子派生的随机源与整数运算——生成器源码不得出现对局种子、时钟、环境与浮点。
        string src = Path.Combine(FrontierFixtures.RepoRoot(), "src", "Siege.Core", "Board", "Maps");
        string[] files = [Path.Combine(src, "LinkMapGenerator.cs"), Path.Combine(src, "LinkMapId.cs")];
        var banned = new Regex(@"GameSeed|DateTime|Stopwatch|Environment\.TickCount|Random\.Shared|new Random\(|Guid\.NewGuid|\bdouble\b|\bfloat\b|\bdecimal\b|Math\.Round|Math\.Floor|Math\.Ceiling");
        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            Assert.True(text.Length > 1000, file);
            Assert.DoesNotMatch(banned, text);
            Assert.DoesNotContain("Siege.Core.Match", text, StringComparison.Ordinal);
        }

        // 反面：禁用词表确实能命中它该命中的地方。
        Assert.Matches(banned, File.ReadAllText(Path.Combine(FrontierFixtures.RepoRoot(), "src", "Siege.Core", "Determinism", "GameSeed.cs")));
    }

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

        // board-link-mode 裁决 D7：段 D 收口后 Resolve 不再转交旧标识族——一律报错并说明只接受 link:<地图种子>；旧标识只经 Legacy（夹具）。
        // 缺省（null / 空白）也从 v5 改为 link:1。
        Assert.Equal("link:1", MapCatalog.Resolve(null).Id);
        Assert.Equal("siege-4p-base-v5", MapCatalog.Legacy.Resolve(null).Id);
        foreach (string legacy in new[] { "siege-4p-base-v5", "gen:12345", "board:12345", FrontierMapV2.Id, "gen", "board", "maps/x.json" })
        {
            FormatException rejected = Assert.Throws<FormatException>(() => MapCatalog.Resolve(legacy));
            Assert.Contains("只接受 link:<地图种子>", rejected.Message, StringComparison.Ordinal);
            Assert.Contains(legacy, rejected.Message, StringComparison.Ordinal);
        }

        Assert.Equal("gen:12345", MapCatalog.Legacy.Resolve("gen:12345").Id);   // 反面：夹具解析照常

        // Legacy 不认 link:（把它当文件路径找不到，并在可用标识里提到 link: 写法）。
        FileNotFoundException notLegacy = Assert.Throws<FileNotFoundException>(() => MapCatalog.Legacy.Resolve("link:1"));
        Assert.Contains("link:<地图种子>", notLegacy.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 夹具仍可加载_夹具生成不变()
    {
        // 旧地图经 MapCatalog.Legacy 加载后与各自的权威来源逐字节相同：内置图、gen:、board:。
        Assert.Equal(MapFile.ToJson(FourPlayerBaseMap.Create()), MapFile.ToJson(MapCatalog.Legacy.Resolve("siege-4p-base-v5")));
        Assert.Equal(MapFile.ToJson(FourPlayerBaseMap.Create()), MapFile.ToJson(MapCatalog.Legacy.Resolve(null)));
        Assert.Equal(MapFile.ToJson(FrontierMapGenerator.Generate(12345)), MapFile.ToJson(MapCatalog.Legacy.Resolve("gen:12345")));
        Assert.Equal(MapFile.ToJson(BoardMapGenerator.Generate(12345)), MapFile.ToJson(MapCatalog.Legacy.Resolve("board:12345")));
        Assert.Equal(MapProfile.Standard, MapCatalog.Legacy.Resolve(null).Profile);
        Assert.Equal(0, MapCatalog.Legacy.Resolve("board:12345").MapVersion);
        Assert.DoesNotContain("\"MapVersion\"", MapFile.ToJson(MapCatalog.Legacy.Resolve("board:12345")), StringComparison.Ordinal);
        Assert.Throws<FormatException>(() => MapCatalog.Legacy.Resolve("gen"));
        Assert.Throws<FormatException>(() => MapCatalog.Legacy.Resolve("board"));
        Assert.Throws<FileNotFoundException>(() => MapCatalog.Legacy.Resolve("no-such-map"));
    }

    [Fact]
    public void 同链接序列同最终图()
    {
        // Scenario：在 link:12345 上两次都依次链接 (1,4)、(2,5)、(1,2)（规格 1 起编号；这里用 0 起的出生棋盘编号 (0,3)、(1,4)、(0,1)）→ 两次的最终地图逐格相同。
        (int X, int Y)[] steps = [(0, 3), (1, 4), (0, 1)];
        Assert.Equal(MapFile.ToJson(Run(steps)), MapFile.ToJson(Run(steps)));

        // 样本口径：换一条序列最终图就不同，上面的相等断言不是恒真。
        Assert.NotEqual(MapFile.ToJson(Run(steps)), MapFile.ToJson(Run([(0, 4), (1, 3), (0, 1)])));

        static MapData Run((int X, int Y)[] steps)
        {
            MapData map = LinkMapGenerator.Generate(12345);
            foreach ((int x, int y) in steps)
            {
                map = LinkMapGenerator.Link(map, x, y).Map;
            }

            return map;
        }
    }
}
