using System.Collections.Concurrent;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.MapGeneration;

/// <summary>
/// 规格 terrain-surfaces · map-generation —— Requirement: 生成参数（新地表开关）、布局规则（新地表投放）。
/// 断言一律从地图数据反推（连通块、邻接、高度），不读生成器内部的投放记录。
/// </summary>
public class 新地表投放Tests
{
    private static readonly Surface[] Kinds = [Surface.Desert, Surface.Marsh, Surface.Crag, Surface.Shallows];

    private static readonly ConcurrentDictionary<(ulong Seed, int Platforms), MapData> WithSurfaces = new();

    private static MapData On(ulong seed, int platforms = MapGenParameters.DefaultPlatforms) =>
        WithSurfaces.GetOrAdd((seed, platforms), k => FrontierMapGenerator.Generate(k.Seed, new MapGenParameters { PlatformCount = k.Platforms, NewSurfaces = true }));

    /// <summary>平台数 5–8：平台越多、平台外可投放的空间越小，p8 是最紧的一档。</summary>
    public static TheoryData<int> PlatformCounts() => new() { 5, 6, 7, 8 };

    private static MapData Off(ulong seed) => MapGenFixtures.Generated(seed).Map;

    [Fact]
    public void 新地表缺省关闭()
    {
        // 规格 Scenario「新地表缺省关闭」：只给种子 → 不含新地表。逐字节不变由「生成确定性」的黄金摘要测试钉住（gen:12345 等），这里钉"不含"。
        MapData map = FrontierMapGenerator.Generate(12345);

        Assert.Equal("gen:12345", map.Id);
        Assert.DoesNotContain(map.TerrainData.Surfaces.Values, s => Kinds.Contains(s));
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(7UL)]
    [InlineData(12345UL)]
    [InlineData(987654321UL)]
    public void 新地表投放不动布局(ulong seed)
    {
        // 规格 Scenario「新地表投放不动布局」：同种子开 / 关两图，高度、障碍、桥、栅栏、出生区、信物格逐格相同，可落子格数相等，
        // 差别只在平台之外若干草地 / 土路格被改写成新地表。
        // 变异验证 M-S5b（实跑）：投放候选放进林地 → 本测试红。（投放改用布局子流不会改布局——那时布局已定——所以"独立子流"由「地图随机源」的子流测试钉住，不在这里。）
        MapData off = Off(seed);
        MapData on = On(seed);

        Assert.Equal(off.Obstacles, on.Obstacles);
        Assert.Equal(off.TerrainData.Heights.OrderBy(kv => kv.Key), on.TerrainData.Heights.OrderBy(kv => kv.Key));
        Assert.Equal(off.TerrainData.Bridges, on.TerrainData.Bridges);
        Assert.Equal(off.TerrainData.Fences, on.TerrainData.Fences);
        Assert.Equal(off.BirthZones.Length, on.BirthZones.Length);
        for (int i = 0; i < off.BirthZones.Length; i++)
        {
            Assert.Equal(off.BirthZones[i], on.BirthZones[i]);
        }

        Assert.Equal(off.RelicCells.OrderBy(kv => kv.Key), on.RelicCells.OrderBy(kv => kv.Key));
        Assert.Equal(off.PlayableCount, on.PlayableCount);

        Coord[] changed = [.. off.AllCoords().Where(c => off.SurfaceAt(c) != on.SurfaceAt(c))];
        Assert.NotEmpty(changed);
        Assert.All(changed, c =>
        {
            Assert.True(off.SurfaceAt(c) is Surface.Grass or Surface.Road, $"{c.ToNotation()} 原为 {off.SurfaceAt(c).DisplayName()}");
            Assert.Contains(on.SurfaceAt(c), Kinds);
            Assert.Null(off.BirthZoneOf(c));
        });
        Assert.Equal(changed.Length, on.AllCoords().Count(c => Kinds.Contains(on.SurfaceAt(c))));
    }

    [Theory]
    [MemberData(nameof(PlatformCounts))]
    public void 新地表投放规模(int platforms)
    {
        // 规格 Scenario「新地表投放规模」：种子 1–50，四种各 1–3 块、每块 2–6 格，合计占平台之外可落子格的 15%–25%；
        // 每块浅滩至少一格挨着深水（主河），岩台全部 h ≤ 1。
        // 变异验证（实跑）M-S5c：块大小上限 6 改成 8 → 本测试红；M-S5d：浅滩种子格不要求挨河 → 本测试红；
        // M-S5f（check）：去掉"不得与已投放的同种块几何相邻"→ 跨轮次同种块连成 8–12 格的一块，本测试红 4（p5–p8 各一行）。
        var failures = new List<string>();
        for (ulong seed = 1; seed <= 50; seed++)
        {
            MapData map = On(seed, platforms);
            int outside = map.AllCoords().Count(c => map.IsPlayable(c) && map.BirthZoneOf(c) is null);
            int total = map.AllCoords().Count(c => Kinds.Contains(map.SurfaceAt(c)));
            int min = (outside * 15 + 99) / 100;
            int max = outside * 25 / 100;
            if (total < min || total > max)
            {
                failures.Add($"gen:{seed}:p{platforms}:s1 新地表合计 {total}，不在 {min}–{max}（平台外可落子 {outside}）");
            }

            foreach (Surface kind in Kinds)
            {
                List<List<Coord>> blocks = Blocks(map, kind);
                if (blocks.Count is < 1 or > 3)
                {
                    failures.Add($"gen:{seed}:p{platforms}:s1 {kind.DisplayName()} {blocks.Count} 块");
                }

                foreach (List<Coord> block in blocks)
                {
                    if (block.Count is < 2 or > 6)
                    {
                        failures.Add($"gen:{seed}:p{platforms}:s1 {kind.DisplayName()} 块 {block[0].ToNotation()} 有 {block.Count} 格");
                    }

                    if (kind == Surface.Shallows
                        && !block.Any(c => Adjacency.Neighbors(map.Width, map.Height, c).Any(n => map.SurfaceAt(n) == Surface.DeepWater)))
                    {
                        failures.Add($"gen:{seed}:p{platforms}:s1 浅滩块 {block[0].ToNotation()} 不挨河");
                    }

                    if (kind == Surface.Crag && block.Any(c => map.HeightAt(c) > 1))
                    {
                        failures.Add($"gen:{seed}:p{platforms}:s1 岩台块 {block[0].ToNotation()} 有 h=2 的格");
                    }
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Theory]
    [MemberData(nameof(PlatformCounts))]
    public void 新地表不进平台与信物格(int platforms)
    {
        // 规格 Scenario「新地表不进平台与信物格」；同时每张图照常通过边疆档静态校验（含 map-definition 第 9 条）。
        for (ulong seed = 1; seed <= 50; seed++)
        {
            MapData map = On(seed, platforms);
            Assert.All(map.BirthZones.SelectMany(z => z), c => Assert.DoesNotContain(map.SurfaceAt(c), Kinds));
            Assert.All(map.TerrainData.Bridges, c => Assert.DoesNotContain(map.SurfaceAt(c), Kinds));
            Assert.All(map.RelicCells.Keys, c => Assert.DoesNotContain(map.SurfaceAt(c), Kinds));
            MapValidationResult result = MapValidator.Validate(map);
            Assert.True(result.IsValid, $"gen:{seed}:p{platforms}:s1 " + string.Join("；", result.Failures));
        }
    }

    [Fact]
    public void 同种子开新地表两次生成逐字节相同()
    {
        Assert.Equal(
            MapFile.ToJson(FrontierMapGenerator.Generate(424242, new MapGenParameters { NewSurfaces = true, PlatformCount = 7 })),
            MapFile.ToJson(FrontierMapGenerator.Generate("gen:424242:p7:s1")));
        Assert.Equal("gen:424242:p7:s1", FrontierMapGenerator.Generate("gen:424242:p7:s1").Id);
    }

    [Theory]
    [InlineData("gen:12345:s1", "DA4F91F5427FEFE282B11924AEF546A912ADD1DE275C6B1A3323640E79117D3B")]
    [InlineData("gen:45:p5:s1", "2C27EC2A1A65FB9C2BE83CFD7BD5C6B94771533A1650CDAC39C595F2034D9874")]
    [InlineData("gen:10:p8:s1", "8AEDB9ADEFDFF5F8C6E9895613D3E6C29FAE9A7D959DB9427C87EFE4ED244C39")]
    public void 开新地表的生成图黄金值_导出文本摘要(string id, string golden)
    {
        // 与「生成确定性」的黄金值同一用意：`:s1` 标识同样写进日志与存档，凭它 MUST 重建同一张图。投放算法（次序、生成、随机数消费）
        // 任何会让同一 `:s1` 标识产出另一张图的改动都先在这里红——红了不等于错，确认要改再更新此值并在实施记录里写明。
        // 黄金值 2026-09-23 按负责人裁决把投放比例由 8%–16% 调到 15%–25% 后重取（此前的值取自 check 阶段重构之前的实现，重构前后导出逐字节相同）。
        // 45:p5、10:p8 是实施记录里"浅滩无处可放"缺陷的暴露种子。
        MapData map = FrontierMapGenerator.Generate(id);
        string json = MapFile.ToJson(map).Replace("\r\n", "\n", StringComparison.Ordinal);
        string digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)));
        Assert.Equal(id, map.Id);
        Assert.True(golden == digest, $"{id} 的导出文本摘要变了：现为 {digest}。");
    }

    /// <summary>同一地表沿几何四邻连成的块，按块内最小坐标的坐标序。</summary>
    private static List<List<Coord>> Blocks(MapData map, Surface kind)
    {
        var blocks = new List<List<Coord>>();
        var seen = new HashSet<Coord>();
        foreach (Coord start in map.AllCoords().Where(c => map.SurfaceAt(c) == kind).Order())
        {
            if (!seen.Add(start))
            {
                continue;
            }

            var block = new List<Coord>();
            var queue = new Queue<Coord>([start]);
            while (queue.Count > 0)
            {
                Coord c = queue.Dequeue();
                block.Add(c);
                foreach (Coord n in Adjacency.Neighbors(map.Width, map.Height, c))
                {
                    if (map.SurfaceAt(n) == kind && seen.Add(n))
                    {
                        queue.Enqueue(n);
                    }
                }
            }

            blocks.Add(block);
        }

        return blocks;
    }
}
