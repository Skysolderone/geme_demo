using System.Diagnostics;
using System.Globalization;
using System.Text;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.MapGeneration;

/// <summary>
/// map-generator tasks 1.7：种子 1–50 的布局速览，供负责人过目（design 风险「能过校验但不好玩」）。不是断言型测试：
/// 只在环境变量 <c>SIEGE_MAPGEN_GALLERY=1</c> 时写 <c>sim-out/mapgen-gallery.txt</c>（速览）、<c>sim-out/mapgen-stats.txt</c>（统计）、
/// <c>sim-out/mapgen-samples.txt</c>（平台数 5 / 6 / 8 各一张），平时直接返回——全量测试不写盘、不受其耗时影响。
/// PowerShell：<c>$env:SIEGE_MAPGEN_GALLERY=1; dotnet test -c Release --filter 布局速览</c>
/// </summary>
public class 布局速览Tests
{
    [Fact]
    public void 写出种子1到50的布局速览()
    {
        if (Environment.GetEnvironmentVariable("SIEGE_MAPGEN_GALLERY") != "1")
        {
            return;
        }

        string dir = Path.Combine(FrontierFixtures.RepoRoot(), "sim-out");
        Directory.CreateDirectory(dir);

        var gallery = new StringBuilder();
        var stats = new StringBuilder();
        for (int platforms = MapGenParameters.MinPlatforms; platforms <= MapGenParameters.MaxPlatforms; platforms++)
        {
            var attempts = new SortedDictionary<int, int>();
            var playable = new List<int>();
            var millis = new List<long>();
            for (ulong seed = 1; seed <= 50; seed++)
            {
                var watch = Stopwatch.StartNew();
                GeneratedMap g = FrontierMapGenerator.GenerateDetailed(seed, new MapGenParameters { PlatformCount = platforms });
                watch.Stop();
                millis.Add(watch.ElapsedMilliseconds);
                attempts[g.Attempt] = attempts.GetValueOrDefault(g.Attempt) + 1;
                playable.Add(g.Map.PlayableCount);
                if (platforms == MapGenParameters.DefaultPlatforms)
                {
                    gallery.Append(Describe(g)).Append('\n');
                }
            }

            millis.Sort();
            stats.Append(CultureInfo.InvariantCulture, $"平台数 {platforms}：尝试序号分布 {string.Join("，", attempts.Select(kv => $"{kv.Key}→{kv.Value} 张"))}；")
                .Append(CultureInfo.InvariantCulture, $"可落子格 {playable.Min()}–{playable.Max()}；耗时中位数 {millis[millis.Count / 2]} ms、最大 {millis[^1]} ms\n");
        }

        File.WriteAllText(Path.Combine(dir, "mapgen-gallery.txt"), gallery.ToString());
        File.WriteAllText(Path.Combine(dir, "mapgen-stats.txt"), stats.ToString());
        File.WriteAllText(
            Path.Combine(dir, "mapgen-samples.txt"),
            string.Join('\n', new[] { (7UL, 5), (12345UL, 6), (21UL, 8) }.Select(s => Describe(MapGenFixtures.Generated(s.Item1, s.Item2)))));
    }

    private static string Describe(GeneratedMap g)
    {
        MapData map = g.Map;
        var entrance = MapValidator.DistanceTable(map)[1].Distances;
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"== {map.Id}  尝试序号 {g.Attempt}  可落子格 {map.PlayableCount}  信物 {map.RelicCells.Count}  据点 {map.Sites.Count}  桥（每座格数） {string.Join("+", MapGenFixtures.BridgeSpans(map))}\n");
        text.Append("   平台（编号:边长/到中央入口）：")
            .Append(string.Join("  ", g.Platforms.Select((p, i) => $"{i + 1}:{p.Side}/{entrance[i]}")))
            .Append('\n');
        text.Append("   栅栏：").Append(string.Join(" ", map.TerrainData.Fences.OrderBy(f => f.A).ThenBy(f => f.B))).Append('\n');
        text.Append(MapGenFixtures.TextArt(map));
        return text.ToString();
    }
}
