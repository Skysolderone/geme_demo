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

    [Fact]
    public void 同种子同尝试序号得到同一序列()
    {
        Assert.Equal(Take(MapRandom.ForAttempt(12345, 3), 64), Take(MapRandom.ForAttempt(12345, 3), 64));
    }

    [Fact]
    public void 不同尝试序号或不同种子得到不同序列()
    {
        ulong[] baseline = Take(MapRandom.ForAttempt(12345, 0), 8);
        for (int attempt = 1; attempt < 64; attempt++)
        {
            Assert.NotEqual(baseline, Take(MapRandom.ForAttempt(12345, attempt), 8));
        }

        Assert.NotEqual(baseline, Take(MapRandom.ForAttempt(12346, 0), 8));

        // （种子, 序号）不是简单相加 / 异或：(s, k+1) 与 (s+1, k) 不得撞到同一序列。
        Assert.NotEqual(Take(MapRandom.ForAttempt(7, 1), 8), Take(MapRandom.ForAttempt(8, 0), 8));
        Assert.NotEqual(Take(MapRandom.ForAttempt(6, 1), 8), Take(MapRandom.ForAttempt(7, 0), 8));
    }

    [Fact]
    public void 尝试序号为负即报错()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MapRandom.ForAttempt(1, -1));
    }

    [Fact]
    public void 地图随机源的黄金值()
    {
        // 钉住派生方式：它一变，同一个地图标识就会生成另一张图，旧日志按标识重建地图随之失效（design D4 / D5）。
        // 变异验证 MG-1：把 MapRandom 的域分隔常量末位 D 改成 E → 本测试红。
        Assert.Equal(new[] { GoldenA, GoldenB, GoldenC }, Take(MapRandom.ForAttempt(12345, 0), 3));
        Assert.Equal(GoldenK1, MapRandom.ForAttempt(12345, 1).NextUInt64());
    }

    private const ulong GoldenA = 228866895567684773UL;
    private const ulong GoldenB = 18280812252693126409UL;
    private const ulong GoldenC = 17042747156771785144UL;
    private const ulong GoldenK1 = 9807326605086137499UL;

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
        ulong[] map = Take(MapRandom.ForAttempt(12345, 0), 8);
        foreach (string name in new[] { GameSeed.RelicGeneration, GameSeed.Recruit, GameSeed.Setup, GameSeed.ZonePick })
        {
            Assert.NotEqual(map, Take(game.Stream(name), 8));
        }
    }

    [Fact]
    public void 生成器不见对局种子_对局流程不见地图种子()
    {
        // 守门（design D2，源码扫描）。
        // 变异验证 MG-2：在 FrontierMapLayout.cs 的注释里写一处对局种子的类型名 → 本测试红；
        // MG-3：在 Siege.Core/Match/MatchFlow.cs 里加一句引用 MapGenParameters 的语句 → 本测试红。
        string core = Path.Combine(FrontierFixtures.RepoRoot(), "src", "Siege.Core");
        string[] generatorNames =
        [
            "MapRandom.cs", "MapGenParameters.cs", "FrontierMapGenerator.cs",
            .. Directory.EnumerateFiles(Path.Combine(core, "Board", "Maps"), "FrontierMapLayout*.cs").Select(path => Path.GetFileName(path)!),
        ];
        Assert.True(generatorNames.Length >= 11, $"样本口径：只认出 {generatorNames.Length} 个生成器文件。");
        string[] generatorFiles = [.. generatorNames.Select(name => Path.Combine(core, "Board", "Maps", name))];
        Assert.All(generatorFiles, path => Assert.DoesNotContain("GameSeed", File.ReadAllText(path), StringComparison.Ordinal));
        Assert.Contains(generatorFiles, path => File.ReadAllText(path).Contains("MapRandom.ForAttempt", StringComparison.Ordinal));   // 反面：扫到的确实是生成器

        // Maps 目录下凡是碰随机源或工作态的文件都在上面的名单里——新拆出来的生成器文件不会漏扫。
        // MapCatalog 是"标识 → 地图"的入口，段 B 接入时只许调用生成器，不许碰随机源。
        string[] touching =
        [
            .. Directory.EnumerateFiles(Path.Combine(core, "Board", "Maps"), "*.cs")
                .Where(path => Regex.IsMatch(File.ReadAllText(path), "MapRandom|FrontierMapLayout"))
                .Select(path => Path.GetFileName(path)),
        ];
        Assert.All(touching, name => Assert.Contains(name, generatorNames));

        string[] matchFiles = [.. Directory.EnumerateFiles(Path.Combine(core, "Match"), "*.cs", SearchOption.AllDirectories)];
        Assert.True(matchFiles.Length >= 10, $"样本口径：Match 目录只扫到 {matchFiles.Length} 个文件。");
        Assert.Contains(matchFiles, path => File.ReadAllText(path).Contains("GameSeed", StringComparison.Ordinal));                 // 反面：对局流程确实在用对局种子
        var mapSeedToken = new Regex(@"FrontierMapGenerator|FrontierMapLayout|MapGenParameters|GeneratedMapId|MapRandom|GeneratedMap\b|(?i:mapseed)");
        Assert.Empty(matchFiles.Where(path => mapSeedToken.IsMatch(File.ReadAllText(path))).Select(path => Path.GetFileName(path)));
    }
}
