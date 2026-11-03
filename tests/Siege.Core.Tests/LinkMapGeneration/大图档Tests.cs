using System.Diagnostics;
using Siege.Core.Tests.CaptureResolution;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Xunit.Abstractions;

namespace Siege.Core.Tests.LinkMapGeneration;

/// <summary>
/// 规格：openspec/specs/link-map-generation —— Requirement: 档位 / 大图档布局（big-map 裁决 3 / 11 / 12，D1 / D2）。
/// 种子 1–20 × 2 / 3 / 4 人的大图：外接矩形 150×150（画布 154×154）、可落子格在校验器大图行的区间内、非可落子占外接矩形 25%–35%、出生座位 0、中立平台 14–18、
/// 全图可落子格沿气边连通（校验通过）、合规旗格 ≥ 可落子格的 50%；三段标识 = <c>:s</c>；大图与小图不同构。
/// 单张生成 ≤ 1 s（Release）的计时断言在 <see cref="种子1到20的大图单张生成在预算内"/>（relic-sites 追加裁决 14：Perf 门控，与 <c>Category=Perf</c> 同机制，快速全集不跑、预算不放宽）。
/// </summary>
/// <remarks>
/// 变异验证（段 A 实跑，mutate.py，备份 → 改 → 跑 → 逐字节还原 → 刷新时间戳）：
/// M-T1 <c>MapRandom.Create</c> 的 Large 混入改为恒不触发（<c>tier == Large &amp;&amp; mapSeed == ulong.MaxValue</c>）→ 只跑「大图与小图不同构_档位进入随机源」红 1 / 1（三条流首值与小图相同）；
/// M-L2 <c>LinkMapId.Format</c> 对 Small 也输出四段 <c>:s</c> → 跑本类「三段标识与s档等价」+ <c>链接档地图标识与确定性Tests</c>：红 5、绿 3（Normalize 回不到三段、小图标识与缓存键全变）。
/// 未做：合规旗格判据的变异见 <c>自由插旗Tests</c>（M-F1 / M-S1 实跑）。
/// </remarks>
public class 大图档Tests(ITestOutputHelper output)
{
    private const int Rect = LinkMapGenerator.LargeIslandSide;
    private const int Canvas = Rect + (2 * LinkMapGenerator.Margin);

    [Fact]
    public void 种子1到20的大图()
    {
        // 预热一次（JIT 与缓存），计时只看之后的 60 张。
        _ = LinkMapGenerator.GenerateDetailed(1, 4, MapTier.Large);
        var playable = new List<int>();
        var shares = new List<int>();
        var flagShares = new List<int>();
        var elapsed = new List<long>();
        foreach (int players in new[] { 2, 3, 4 })
        {
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var sw = Stopwatch.StartNew();
                LinkMapGenerator.IslandGeneration generated = LinkMapGenerator.GenerateDetailed(seed, players, MapTier.Large);
                sw.Stop();
                MapData map = generated.Map;
                string id = LinkMapId.Format(seed, players, MapTier.Large);
                Assert.Equal(id, map.Id);
                Assert.Equal(MapTier.Large, map.Tier);
                Assert.Equal(players, map.MaxPlayers);
                Assert.Equal((Canvas, Canvas), (map.Width, map.Height));
                Assert.Equal(0, map.MapVersion);
                Assert.True(MapValidator.Validate(map).IsValid, $"{id} 未通过校验：{MapValidator.Validate(map)}");

                // 可落子区外接矩形恰 150×150（ISLAND_EXTENT 守下限；留白 2 格恒为障碍守上限）。
                Coord[] cells = [.. map.AllCoords().Where(map.IsPlayable)];
                Assert.Equal((Rect, Rect), (cells.Max(c => c.X) - cells.Min(c => c.X) + 1, cells.Max(c => c.Y) - cells.Min(c => c.Y) + 1));

                int count = map.PlayableCount;
                int share = (Rect * Rect - count) * 100 / (Rect * Rect);
                Assert.InRange(share, 25, 35);
                Assert.Empty(map.BirthZones);
                Assert.Empty(map.BirthBoards());
                int neutral = map.Boards.Count(b => b.Kind == BoardPlateKind.Public);
                Assert.InRange(neutral, LinkMapGenerator.LargeNeutralPlateMin, LinkMapGenerator.LargeNeutralPlateMax);
                Assert.Equal(neutral, map.Boards.Length);
                Assert.Empty(MapValidator.LibertySplit(map));

                int legal = FlagRules.LegalFlagCells(map).Length;
                int flagShare = legal * 100 / count;
                Assert.True(flagShare >= 50, $"{id} 合规旗格 {legal} / 可落子 {count} = {flagShare}%，低于 50%。");

                playable.Add(count);
                shares.Add(share);
                flagShares.Add(flagShare);
                elapsed.Add(sw.ElapsedMilliseconds);
                output.WriteLine($"{id}\t可落子 {count}\t非可落子 {share}%\t合规旗格 {legal}（{flagShare}%）\t中立平台 {neutral}\t湖 {generated.Lakes.Count} 岩区 {generated.RockClusters.Count}\t重抽 布局 {generated.LayoutRedraws} 低地 {generated.LowlandRedraws}\t{sw.ElapsedMilliseconds} ms");
            }
        }

        playable.Sort();
        elapsed.Sort();
        // 耗时只记录（追加裁决 14：并行负载下的计时抖动不进快速集），断言见 种子1到20的大图单张生成在预算内。
        output.WriteLine($"可落子 最小 {playable[0]} 中位 {playable[playable.Count / 2]} 最大 {playable[^1]}；非可落子占比 {shares.Min()}–{shares.Max()}%；合规旗格占比 {flagShares.Min()}–{flagShares.Max()}%；耗时 最小 {elapsed[0]} 中位 {elapsed[elapsed.Count / 2]} 最大 {elapsed[^1]} ms");
    }

    [Siege.Core.Tests.LifeShape.PerfFact]
    [Trait("Category", "Perf")]
    public void 种子1到20的大图单张生成在预算内()
    {
        // 预算：单张 ≤ 1 s（Release，裁决 12）；追加裁决 14 从快速集搬出（并行跑时单跑 0.2–0.9 s 的生成会被负载抖到 1 s 以上），设 SIEGE_PERF=1 按 Category=Perf 单独跑，预算不放宽。
        // 预热一次（JIT 与缓存），计时只看之后的 60 张；每张绕过缓存重新生成。
        _ = LinkMapGenerator.GenerateDetailed(1, 4, MapTier.Large);
        var elapsed = new List<long>();
        foreach (int players in new[] { 2, 3, 4 })
        {
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var sw = Stopwatch.StartNew();
                _ = LinkMapGenerator.GenerateDetailed(seed, players, MapTier.Large);
                sw.Stop();
                elapsed.Add(sw.ElapsedMilliseconds);
            }
        }

        elapsed.Sort();
        output.WriteLine($"耗时 最小 {elapsed[0]} 中位 {elapsed[elapsed.Count / 2]} 最大 {elapsed[^1]} ms");
#if !DEBUG
        Assert.True(elapsed[^1] <= 1000, $"大图单张生成最慢 {elapsed[^1]} ms，超出 1 s 预算。");
#else
        output.WriteLine("Debug 构建只记录，不断言。");
#endif
    }

    [SlowFact]
    [Trait("Category", "Slow")]
    public void 种子1到200的大图都在预算内()
    {
        // 吸取 seat-gap-25「按 1–20 钉的下限在更大种子上撞线」的教训：4 人大图扩大到种子 1–200 只生成、只看可落子格区间与校验通过（SIEGE_SLOW=1 单独跑）。
        var playable = new List<int>();
        for (ulong seed = 1; seed <= 200; seed++)
        {
            MapData map = LinkMapGenerator.Generate(seed, 4, MapTier.Large);   // 生成器内部已过校验，越界即抛
            playable.Add(map.PlayableCount);
        }

        playable.Sort();
        output.WriteLine($"种子 1–200 × 4 人：可落子 最小 {playable[0]} 中位 {playable[playable.Count / 2]} 最大 {playable[^1]}");
    }

    [Fact]
    public void 三段标识与s档等价()
    {
        // 裁决 11 / 歧义 17：三段写法 = s 档；四段 s 写法解析后规范化回三段；大图恒四段。
        Assert.Equal((1UL, 4, MapTier.Small), LinkMapId.Parse("link:1:4"));
        Assert.Equal((1UL, 4, MapTier.Small), LinkMapId.Parse("link:1:4:s"));
        Assert.Equal((7UL, 2, MapTier.Large), LinkMapId.Parse(" link:007:02:l "));
        Assert.Equal("link:1:4", LinkMapId.Normalize("link:1:4:s"));
        Assert.Equal("link:7:2:l", LinkMapId.Normalize(" link:007:02:l "));
        Assert.Equal("link:1:4", LinkMapId.Format(1, 4, MapTier.Small));
        Assert.Equal("link:1:4", LinkMapId.Format(1, 4));
        Assert.Equal("link:1:4:l", LinkMapId.Format(1, 4, MapTier.Large));
        Assert.Equal(MapFile.Digest(MapCatalog.Resolve("link:1:4")), MapFile.Digest(MapCatalog.Resolve("link:1:4:s")));
        Assert.Same(MapCatalog.Resolve("link:1:4"), MapCatalog.Resolve("link:1:4:s"));   // 同一缓存项：不是两张碰巧相同的图

        // 非法档位：消息同时给出格式与示例。
        foreach (string bad in new[] { "link:1:4:x", "link:1:4:L", "link:1:4:", "link:1:4:4", "link:1:4:s:l" })
        {
            FormatException ex = Assert.Throws<FormatException>(() => LinkMapId.Parse(bad));
            Assert.Contains("link:<地图种子>:<人数>", ex.Message, StringComparison.Ordinal);
            Assert.Contains("link:1:4", ex.Message, StringComparison.Ordinal);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => LinkMapId.Format(1, 4, (MapTier)7));
    }

    [Fact]
    public void 大图与小图不同构_档位进入随机源()
    {
        // D1：Large 在 (种子, 人数) 一侧混入 TierDomain，三条子流首值都与小图不同；Small 不混（小图取值由黄金图与基线守）。
        foreach (string stream in new[] { "layout", "surfaces", "coast" })
        {
            Func<MapTier, Siege.Core.Determinism.RandomStream> of = stream switch
            {
                "layout" => tier => MapRandom.ForLinkLayout(1, 4, tier),
                "surfaces" => tier => MapRandom.ForLinkSurfaces(1, 4, tier),
                _ => tier => MapRandom.ForLinkCoast(1, 4, tier),
            };
            Assert.NotEqual(of(MapTier.Small).NextUInt64(), of(MapTier.Large).NextUInt64());
            Assert.Equal(MapRandom.ForLinkLayout(1, 4).NextUInt64(), MapRandom.ForLinkLayout(1, 4, MapTier.Small).NextUInt64());
        }

        MapData small = MapCatalog.Resolve("link:1:4");
        MapData large = MapCatalog.Resolve("link:1:4:l");
        Assert.Equal((49, 49), (small.Width, small.Height));
        Assert.Equal((Canvas, Canvas), (large.Width, large.Height));
        Assert.NotEqual(MapFile.Digest(small), MapFile.Digest(large));
        Assert.Equal(MapFile.Digest(large), MapFile.Digest(LinkMapGenerator.Generate(1, 4, MapTier.Large)));   // 同标识同图
    }

    [Fact]
    public void 大图地图文件往返带档位_小图不写档位()
    {
        // D1 小图零字节原则：小图导出文件没有 Tier 字段；大图写 Large，读回后档位与出生棋盘（不带主题）原样。
        string smallJson = MapFile.ToJson(MapCatalog.Resolve("link:1:4"));
        Assert.DoesNotContain("\"Tier\"", smallJson, StringComparison.Ordinal);
        Assert.Equal(MapTier.Small, MapFile.FromJson(smallJson).Tier);

        MapData large = MapCatalog.Resolve("link:2:3:l");
        string largeJson = MapFile.ToJson(large);
        Assert.Contains("\"Tier\": \"Large\"", largeJson, StringComparison.Ordinal);
        MapData back = MapFile.FromJson(largeJson);
        Assert.Equal(MapTier.Large, back.Tier);
        Assert.Equal(MapFile.Digest(large), MapFile.Digest(back));

        // 落座图（D4）：出生棋盘不带主题也能读回；档位字段写错即拒绝。
        MapData seated = FlagRules.Seat(large, [.. FlagRules.LegalFlagCells(large).Take(1)]);
        MapData seatedBack = MapFile.FromJson(MapFile.ToJson(seated));
        Assert.Equal(1, seatedBack.MapVersion);
        Assert.Null(seatedBack.BirthBoard(0).Theme);
        Assert.Equal(MapFile.Digest(seated), MapFile.Digest(seatedBack));
        Assert.Throws<FormatException>(() => MapFile.FromJson(largeJson.Replace("\"Tier\": \"Large\"", "\"Tier\": \"Huge\"", StringComparison.Ordinal)));
    }
}
