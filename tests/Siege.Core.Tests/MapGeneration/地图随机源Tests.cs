using System.Text.RegularExpressions;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;

namespace Siege.Core.Tests.MapGeneration;

/// <summary>
/// 规格：openspec/changes/map-generator/specs/map-generation —— Requirement: 地图种子与确定性（随机源部分）、校验闭环（第 k 次尝试的随机源由种子与 k 决定）。
/// design D2：地图随机源与对局随机共用同一份 PRNG 实现，但不是对局种子的子流；生成器拿不到对局种子，对局流程拿不到地图种子。
/// </summary>
public class 地图随机源Tests
{
    private static ulong[] Take(RandomStream stream, int count) => [.. Enumerable.Range(0, count).Select(_ => stream.NextUInt64())];

    // retire-legacy-maps 段 B：边疆档随机源（MapRandom.ForAttempt / ForSurfaces）随 gen: 生成器删除，只测它们的五条（同种子同序列、新地表子流独立、
    // 不同序号不同序列、序号为负报错、ForAttempt 黄金值 GoldenA/B/C/K1）一并删除；棋盘档随机源的同类断言在 棋盘档生成参数Tests
    // （「棋盘档随机序列只由种子人数与序号决定且互不重复」「棋盘档随机源与生成图的黄金值」）。

    [Fact]
    public void 对局四条子流的取值不因地图随机源而变()
    {
        // 地图随机源复用了 RandomStream 的 internal 构造器与 SplitMix64；这里钉住对局种子 12345 下四条命名子流的首个取值，
        // 证明复用没有改动对局侧的派生（规格 Scenario「地图种子不扰动对局随机」的底层保证；对局层面的回归由既有黄金值测试守）。
        var game = new GameSeed(12345);
        Assert.Equal(
            [GoldenRelicGen, GoldenRecruit, GoldenSetup, GoldenZonePick],
            new[] { GameSeed.RelicGeneration, GameSeed.Recruit, GameSeed.Setup, GameSeed.ZonePick }.Select(name => game.Stream(name).NextUInt64()));
    }

    private const ulong GoldenRelicGen = 10602760394128728250UL;
    private const ulong GoldenRecruit = 1172862400557011553UL;
    private const ulong GoldenSetup = 18416610492839591364UL;
    private const ulong GoldenZonePick = 5147080619751416923UL;

    [Fact]
    public void 地图随机源不与任何对局子流同构()
    {
        // 同一个 64 位数既当地图种子又当对局种子时，地图序列也不等于对局的任何一条命名子流。
        var game = new GameSeed(12345);
        ulong[] map = Take(MapRandom.ForBoardAttempt(12345, 4, 0), 8);   // retire-legacy-maps 段 B：原用已删除的边疆档随机源 ForAttempt
        foreach (string name in new[] { GameSeed.RelicGeneration, GameSeed.Recruit, GameSeed.Setup, GameSeed.ZonePick })
        {
            Assert.NotEqual(map, Take(game.Stream(name), 8));
        }
    }

    [Fact]
    public void 生成器不见对局种子_对局流程不见地图种子()
    {
        // 守门（design D2，源码扫描）。
        // 变异验证 MG-2：在 BoardMapLayout.cs 的注释里写一处对局种子的类型名 → 本测试红（原在已删除的 FrontierMapLayout.cs 上做）；
        // MG-3：在 Siege.Core/Match/MatchFlow.cs 里加一句引用 BoardMapParameters 的语句 → 本测试红。
        // retire-legacy-maps 段 B：边疆档生成器文件全部删除，名单只剩棋盘档生成器与随机源。
        string core = Path.Combine(TestMaps.RepoRoot(), "src", "Siege.Core");
        string[] generatorNames =
        [
            "MapRandom.cs",
            "BoardMapGenerator.cs", "BoardMapParameters.cs",                        // board-map 段 B：棋盘档生成器
            .. Directory.EnumerateFiles(Path.Combine(core, "Board", "Maps"), "BoardMapLayout*.cs").Select(path => Path.GetFileName(path)!),
        ];
        Assert.True(generatorNames.Length >= 4, $"样本口径：只认出 {generatorNames.Length} 个生成器文件。");
        string[] generatorFiles = [.. generatorNames.Select(name => Path.Combine(core, "Board", "Maps", name))];
        Assert.All(generatorFiles, path => Assert.DoesNotContain("GameSeed", File.ReadAllText(path), StringComparison.Ordinal));
        Assert.Contains(generatorFiles, path => File.ReadAllText(path).Contains("MapRandom.ForBoardAttempt", StringComparison.Ordinal));   // 反面：扫到的确实是生成器

        // Maps 目录下凡是碰随机源或工作态的文件都在上面的名单里——新拆出来的生成器文件不会漏扫。
        // MapCatalog 是"标识 → 地图"的入口，段 B 接入时只许调用生成器，不许碰随机源。
        string[] touching =
        [
            .. Directory.EnumerateFiles(Path.Combine(core, "Board", "Maps"), "*.cs")
                .Where(path => Regex.IsMatch(File.ReadAllText(path), "MapRandom|BoardMapLayout"))
                .Select(path => Path.GetFileName(path)),
        ];
        Assert.All(touching, name => Assert.Contains(name, generatorNames));

        string[] matchFiles = [.. Directory.EnumerateFiles(Path.Combine(core, "Match"), "*.cs", SearchOption.AllDirectories)];
        Assert.True(matchFiles.Length >= 10, $"样本口径：Match 目录只扫到 {matchFiles.Length} 个文件。");
        Assert.Contains(matchFiles, path => File.ReadAllText(path).Contains("GameSeed", StringComparison.Ordinal));                 // 反面：对局流程确实在用对局种子
        var mapSeedToken = new Regex(@"FrontierMapGenerator|FrontierMapLayout|MapGenParameters|GeneratedMapId|MapRandom|GeneratedMap\b|BoardMap\w+|GeneratedBoardMap|(?i:mapseed)");
        Assert.Empty(matchFiles.Where(path => mapSeedToken.IsMatch(File.ReadAllText(path))).Select(path => Path.GetFileName(path)));
    }
}
