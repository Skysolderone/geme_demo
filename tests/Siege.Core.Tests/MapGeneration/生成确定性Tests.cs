using System.Text;
using System.Text.RegularExpressions;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.MapGeneration;

/// <summary>
/// 规格：openspec/changes/map-generator/specs/map-generation —— Requirement: 地图种子与确定性；specs/map-definition —— 内置图行为零变化。
/// 规范：.trellis/spec/core/determinism.md（整数运算、集合按确定性次序遍历、并列确定性打破）。
/// </summary>
public class 生成确定性Tests
{
    [Theory]
    [InlineData(12345UL, 6)]
    [InlineData(1UL, 5)]
    [InlineData(987654321UL, 7)]
    [InlineData(18446744073709551615UL, 8)]
    public void 同种子同参数两次生成的导出文本逐字节相同(ulong seed, int platforms)
    {
        // Scenario: 同种子同图。两次都不走缓存。
        var parameters = new MapGenParameters { PlatformCount = platforms };
        byte[] first = Encoding.UTF8.GetBytes(MapFile.ToJson(FrontierMapGenerator.Generate(seed, parameters)));
        byte[] second = Encoding.UTF8.GetBytes(MapFile.ToJson(FrontierMapGenerator.Generate(seed, parameters)));
        Assert.Equal(first, second);
    }

    [Fact]
    public void 生成图的黄金值_gen12345的导出文本摘要()
    {
        // 钉住生成器本身：任何会让"同一标识产出另一张图"的改动（六步里的任一步、随机数的消费次序、校验闭环的判据）都先在这里红。
        // 红了不等于错——但旧日志、旧存档里的 gen: 标识会重建出另一张图（回放靠 D5 的内容摘要响亮失败）。确认要改再更新此值，并在实施记录里写明。
        // 变异验证 MG-14：把 FrontierMapLayout.FindRiver 的拐弯加价 6 改成 1 → 本测试红（改成 5 时这一张图恰好不变——只钉了一张图，
        // 它挡的是"大改"，不是每一处微调）；MG-13 / MG-16（去掉走廊拓宽、去掉桥头刻开）也在这里红。
        // restore-go-core-rules 段 B 重建（design.md D6 明文接受）：布点步骤去掉了据点，随机子流的消费次序随之改变，
        // 同一 gen: 标识产出的图与此前不同。旧值 CF4009DE…BE5D6（可落子 370）作废；新值取自段 B 完成后的实跑，连跑两次一致。
        // 非自证：变异 M-B19（PlacePublicRelics 的桥头两岸交错相位反过来）在新值上实跑红 1（只红本测试）。
        // 注意：旧注释里的 MG-14（河道拐弯加价 6 → 1）在<b>新</b>的 gen:12345 上恰好不改变这一张图（实跑 0 红），与"改成 5 时恰好不变"同理；
        // 段 F 6.4b 另加 `生成图的黄金值_多种子的导出文本摘要`（种子 1 / 7），MG-14 在那里红。
        GeneratedMap g = FrontierMapGenerator.GenerateDetailed(12345);
        string json = MapFile.ToJson(g.Map).Replace("\r\n", "\n", StringComparison.Ordinal);
        string digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        Assert.Equal(("gen:12345", Golden12345Attempt, Golden12345Playable), (g.Map.Id, g.Attempt, g.Map.PlayableCount));
        Assert.True(Golden12345Digest == digest, $"gen:12345 的导出文本摘要变了：现为 {digest}。");
    }

    private const int Golden12345Attempt = 0;
    private const int Golden12345Playable = 370;
    private const string Golden12345Digest = "2BDE685DDDC949FCA24F28B950859D27D8852EFD2C854086B9F7766963C5CAC3";

    [Theory]
    [InlineData(1UL, 1, 383, "E726108A634E025733FE54FC6059CBE8E535C2F294E9C731E1C0EBF8D4B481DC")]
    [InlineData(7UL, 0, 370, "BC137EAEA5E4E9048FD0C89242F1BA929460D5EEC61DE42719633F27C29A8C50")]
    public void 生成图的黄金值_多种子的导出文本摘要(ulong seed, int attempt, int playable, string golden)
    {
        // restore-go-core-rules 段 F 6.4b：单颗种子（gen:12345）挡不住布局参数的微调——MG-14（河道拐弯加价 6 → 1）在它上面 0 红。
        // 扩到另外两颗种子。选种方法：在未改动的代码上导出种子 1–12 与 12345 的摘要，再在 MG-14 变异下导出一遍，1–12 全部变化、12345 不变；
        // 取尝试序号与可落子格各不相同的 1（重试 1 次、383 格）与 7（首次成功、370 格）。黄金值取自**未变异**那次导出（连跑一致），不是由变异后的代码生成。
        // 变异验证 MG-14（段 F 实跑）：拐弯加价 6 → 1 → 红 2（本 Theory 两行；gen:12345 那条仍绿）。
        GeneratedMap g = FrontierMapGenerator.GenerateDetailed(seed);
        string json = MapFile.ToJson(g.Map).Replace("\r\n", "\n", StringComparison.Ordinal);
        string digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        Assert.Equal(($"gen:{seed}", attempt, playable), (g.Map.Id, g.Attempt, g.Map.PlayableCount));
        Assert.True(golden == digest, $"gen:{seed} 的导出文本摘要变了：现为 {digest}。");
    }

    [Fact]
    public void 并发生成不串味()
    {
        // 生成器没有任何静态可变状态：多线程同时生成不同种子，各自的结果与单线程逐字节相同。
        ulong[] seeds = [.. Enumerable.Range(101, 12).Select(i => (ulong)i)];
        string[] sequential = [.. seeds.Select(s => MapFile.ToJson(FrontierMapGenerator.Generate(s)))];
        string[] parallel = [.. seeds.AsParallel().AsOrdered().WithDegreeOfParallelism(4).Select(s => MapFile.ToJson(FrontierMapGenerator.Generate(s)))];
        Assert.Equal(sequential, parallel);
    }

    [Fact]
    public void 不同种子不同图()
    {
        // Scenario: 不同种子不同图——种子 1–20 的平台布局（各平台外接方块的集合）至少 19 种互不相同。
        string[] layouts =
        [
            .. Enumerable.Range(1, 20).Select(seed => string.Join(
                ";", MapGenFixtures.Generated((ulong)seed).Platforms.Select(p => $"{p.X},{p.Y},{p.Side}").Order(StringComparer.Ordinal))),
        ];
        Assert.True(layouts.Distinct(StringComparer.Ordinal).Count() >= 19, "种子 1–20 的平台布局重复超过 1 张。");
    }

    [Fact]
    public void 平台数是标识的一部分_换平台数即换图()
    {
        string six = MapFile.ToJson(MapGenFixtures.Generated(5, 6).Map);
        string seven = MapFile.ToJson(MapGenFixtures.Generated(5, 7).Map);
        Assert.NotEqual(six, seven);
    }

    [Theory]
    [InlineData(5, "gen:77:p5")]
    [InlineData(6, "gen:77")]
    [InlineData(8, "gen:77:p8")]
    public void 生成图的标识为规范化标识_规格档为边疆档(int platforms, string expectedId)
    {
        MapData map = MapGenFixtures.Generated(77, platforms).Map;
        Assert.Equal(expectedId, map.Id);
        Assert.Equal(MapProfile.Frontier, map.Profile);
    }

    [Fact]
    public void 生成图经地图文件往返后逐项相同()
    {
        // 段 B 的 map 子命令要把生成图导出成文件再按路径加载：导出文本往返不变，是"导出文件与按标识生成逐项相同"的前提。
        foreach (ulong seed in new ulong[] { 1, 2, 3 })
        {
            string json = MapFile.ToJson(MapGenFixtures.Generated(seed).Map);
            Assert.Equal(json, MapFile.ToJson(MapFile.FromJson(json)));
        }
    }

    [Fact]
    public void 内置图不受生成器影响()
    {
        // map-definition 增量：内置图行为零变化——边疆手工图的导出文本仍与仓库里的权威文件一致（v4 的同类断言在 地图规格档Tests）。
        string disk = File.ReadAllText(Path.Combine(FrontierFixtures.RepoRoot(), "maps", "siege-frontier-v2.json"));
        Assert.Equal(Normalize(disk), Normalize(MapFile.ToJson(FrontierMapV2.Create())));
        Assert.Equal(["siege-4p-base-v5", "siege-frontier-v2"], MapCatalog.BuiltinIds);

        static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
    }

    [Fact]
    public void 生成器源码不含散列次序遍历_浮点_时钟与环境()
    {
        // 守门（tasks 1.5 的退路）：Coord 的散列集合在"只增不删"时恰好按插入序遍历，把某处改成散列遍历并不能稳定地让字节比对变红，
        // 所以改为源码扫描——生成器的工作态只许用数组与列表。不可变散列容器只许出现在最后灌数据的 FrontierMapGenerator.cs 里，且只写不遍历。
        // 变异验证 MG-5：在 FrontierMapLayout.RaiseFences 里把候选列表改成 `new HashSet<(P, P)>()` 再 foreach → 本测试红；
        // MG-6：在 FrontierMapLayout 里加一处 `double` 局部变量 → 本测试红。
        string maps = Path.Combine(FrontierFixtures.RepoRoot(), "src", "Siege.Core", "Board", "Maps");
        // 工作态按六步拆成了若干 partial 文件（FrontierMapLayout*.cs），一并扫。
        string[] layoutFiles = [.. Directory.EnumerateFiles(maps, "FrontierMapLayout*.cs").Order(StringComparer.Ordinal)];
        Assert.True(layoutFiles.Length >= 8, $"样本口径：只扫到 {layoutFiles.Length} 个工作态文件。");
        string layout = StripComments(string.Join("\n", layoutFiles.Select(File.ReadAllText)));
        string generator = StripComments(File.ReadAllText(Path.Combine(maps, "FrontierMapGenerator.cs")));
        string random = StripComments(File.ReadAllText(Path.Combine(maps, "MapRandom.cs")));

        Assert.Contains("_rng.NextInt(", layout, StringComparison.Ordinal);                          // 反面：扫到的确实是工作态
        Assert.DoesNotMatch(@"\b(HashSet|Dictionary|SortedSet|SortedDictionary|Hashtable|Lookup|GroupBy|ToHashSet|ToDictionary|ToLookup|Distinct|AsParallel)\b", layout);
        Assert.DoesNotMatch(@"\b(Immutable\w+|FrozenSet|FrozenDictionary|ISet|IDictionary|ConcurrentBag)\b", layout);       // 换个名字的散列容器同样不许

        // FrontierMapGenerator：散列容器只有不可变 builder，且任何 builder 都不出现在 foreach / LINQ 的数据源位置。
        Assert.DoesNotMatch(@"(?<!Immutable)\b(HashSet|Dictionary)<", generator);
        string[] builders = [.. Regex.Matches(generator, @"var (\w+) = Immutable(?:HashSet|Dictionary)\.CreateBuilder").Select(m => m.Groups[1].Value)];
        Assert.True(builders.Length >= 7, $"样本口径：只认出 {builders.Length} 个 builder。");
        Assert.All(builders, name => Assert.DoesNotMatch($@"\bin {name}\b|\b{name}\.(Select|Where|First|Order|ToArray|ToList)", generator));

        // 地图数据自带的散列容器（RelicCells / BirthZones / Obstacles……）只许计数与查询，不许拿来 foreach 或取"第一个"。
        Assert.DoesNotMatch(@"foreach\s*\([^)]*\bin\s+map\.|\bmap\.\w+(\.\w+)*\.(First|FirstOrDefault|Last|ElementAt|Take|Skip)\(", generator);

        foreach (string source in new[] { layout, generator, random })
        {
            Assert.DoesNotMatch(@"\b(double|float|decimal|Half|Single|Double|MathF|Math\.(Sqrt|Cbrt|Pow|Round|Floor|Ceiling|Truncate|Log|Log2|Log10|Exp|Sin|Cos|Tan|Atan|Atan2))\b", source);
            Assert.DoesNotMatch(@"\b\d+\.\d+[fdmFDM]?\b|\b\d+[fdmFDM]\b", source);                                                   // 浮点 / 十进制字面量
            Assert.DoesNotMatch(@"\b(DateTime|DateTimeOffset|Stopwatch|TimeProvider|Environment|Guid|Random\.Shared|new Random|RandomNumberGenerator|GetHashCode|HashCode)\b", source);
        }

        static string StripComments(string text) => Regex.Replace(text, @"//[^\n]*", string.Empty);
    }
}
