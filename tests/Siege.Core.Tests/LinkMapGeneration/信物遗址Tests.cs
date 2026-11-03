using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.PieceRelics;
using Xunit.Abstractions;

namespace Siege.Core.Tests.LinkMapGeneration;

/// <summary>
/// 规格：openspec/specs/link-map-generation —— Requirement: 大图信物遗址（relic-sites 裁决 6 / 7）；map-definition —— Scenario: 信物遗址清单读写往返。
/// 种子 1–20 × 2 / 3 / 4 人的大图：稀有 ⌈n/2⌉ + 普通 n+2 处、两两切比雪夫距离 ≥ 20、每处高度 0（低地，追加裁决 13）、可落子且八邻可落子、不重复、同标识同清单、独立子流；
/// 地图文件往返含遗址；小图清单为空、导出文件没有该段且与引入遗址之前逐字节相同（<c>link:1:4</c> 导出文件的 SHA-256 钉在本类）；校验器守数量 / 位置 / 距离 / 小图为空。
/// </summary>
public class 信物遗址Tests(ITestOutputHelper output)
{
    /// <summary>测试内独立算式：切比雪夫距离（不调被测代码）。</summary>
    private static int Chebyshev(Coord a, Coord b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    /// <summary>测试内独立算式：本格与八邻都可落子。</summary>
    private static bool Inland(MapData map, Coord c)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = c.X + dx, y = c.Y + dy;
                if (x < 0 || y < 0 || x >= map.Width || y >= map.Height || !map.IsPlayable(new Coord(x, y)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    [Fact]
    public void 种子1到20的大图遗址数量距离与位置()
    {
        foreach (int players in new[] { 2, 3, 4 })
        {
            int rare = (players + 1) / 2, common = players + 2;
            for (ulong seed = 1; seed <= 20; seed++)
            {
                MapData map = LinkMapGenerator.Generate(seed, players, MapTier.Large);
                ImmutableArray<RelicSite> sites = map.RelicSites;
                Assert.Equal(rare + common, sites.Length);
                Assert.Equal(rare, sites.Count(s => s.Tier == RelicTier.Rare));
                Assert.Equal(common, sites.Count(s => s.Tier == RelicTier.Common));
                Assert.Equal(sites.Take(rare).Select(s => s.Tier), Enumerable.Repeat(RelicTier.Rare, rare));   // 稀有在前（抽取次序）
                Assert.Equal(sites.Length, sites.Select(s => s.Cell).Distinct().Count());
                foreach (RelicSite site in sites)
                {
                    Assert.True(map.IsPlayable(site.Cell), $"{map.Id} 遗址 {site} 不可落子。");
                    Assert.Equal(0, map.HeightAt(site.Cell));   // 追加裁决 13：遗址只落低地，平台顶 / 坡道不放
                    Assert.True(Inland(map, site.Cell), $"{map.Id} 遗址 {site} 贴着岸线 / 障碍 / 深水。");
                }

                for (int i = 0; i < sites.Length; i++)
                {
                    for (int j = i + 1; j < sites.Length; j++)
                    {
                        Assert.True(Chebyshev(sites[i].Cell, sites[j].Cell) >= 20, $"{map.Id} 遗址 {sites[i]} 与 {sites[j]} 相距 {Chebyshev(sites[i].Cell, sites[j].Cell)} < 20。");
                    }
                }

                Assert.True(MapValidator.Validate(map).IsValid);
                if (players == 4 && seed <= 3)
                {
                    output.WriteLine($"{map.Id}  {string.Join(" ", sites)}");
                }
            }
        }
    }

    [Fact]
    public void 同标识同清单_遗址走独立子流()
    {
        // 绕过缓存独立生成两次：清单逐项相同；不同种子 / 人数的清单不同（样本口径）。
        MapData a = LinkMapGenerator.GenerateDetailed(7, 4, MapTier.Large).Map;
        MapData b = LinkMapGenerator.GenerateDetailed(7, 4, MapTier.Large).Map;
        Assert.Equal(a.RelicSites, b.RelicSites);
        Assert.Equal(MapFile.Digest(a), MapFile.Digest(b));
        Assert.NotEqual(a.RelicSites, LinkMapGenerator.Generate(8, 4, MapTier.Large).RelicSites);
        Assert.NotEqual(a.RelicSites.Select(s => s.Cell), LinkMapGenerator.Generate(7, 3, MapTier.Large).RelicSites.Select(s => s.Cell));

        // 第四条流与既有三条流首值都不同、同种子可复现、按档位派生；小图不派生它（生成器只在大图分支调用——源码扫描见 链接档地图标识与确定性Tests）。
        ulong first = MapRandom.ForLinkRelicSites(7, 4, MapTier.Large).NextUInt64();
        Assert.Equal(first, MapRandom.ForLinkRelicSites(7, 4, MapTier.Large).NextUInt64());
        Assert.NotEqual(first, MapRandom.ForLinkLayout(7, 4, MapTier.Large).NextUInt64());
        Assert.NotEqual(first, MapRandom.ForLinkCoast(7, 4, MapTier.Large).NextUInt64());
        Assert.NotEqual(first, MapRandom.ForLinkSurfaces(7, 4, MapTier.Large).NextUInt64());
        Assert.NotEqual(first, MapRandom.ForLinkRelicSites(7, 4, MapTier.Small).NextUInt64());
        Assert.Equal("map-link-relic-sites", MapRandom.ForLinkRelicSites(7, 4, MapTier.Large).Name);

        // 遗址不改地形：去掉清单后的大图与引入遗址前的地形同一张（平台、障碍、地形逐项相等——由三条既有流决定，不受第四条流影响）。
        MapData stripped = a with { RelicSites = [] };
        Assert.Equal(a.Obstacles, stripped.Obstacles);
        Assert.Equal(a.Boards, stripped.Boards);
    }

    [Fact]
    public void 地图文件往返含遗址_小图不写且字节不变()
    {
        MapData large = MapCatalog.Resolve("link:7:4:l");
        string json = MapFile.ToJson(large);
        Assert.Contains("\"RelicSites\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Tier\": \"Rare\"", json, StringComparison.Ordinal);
        MapData back = MapFile.FromJson(json);
        Assert.Equal(large.RelicSites, back.RelicSites);
        Assert.Equal(MapFile.Digest(large), MapFile.Digest(back));
        Assert.True(MapValidator.Validate(back).IsValid);

        // 落座图经 with 原样保留清单、文件往返不丢。
        MapData seated = Siege.Core.Match.FlagRules.Seat(large, [.. Siege.Core.Match.FlagRules.LegalFlagCells(large).Take(1)]);
        Assert.Equal(large.RelicSites, seated.RelicSites);
        Assert.Equal(large.RelicSites, MapFile.FromJson(MapFile.ToJson(seated)).RelicSites);

        // 档位写错 / 坐标写错 / null 项都指名拒绝。
        Assert.Contains("RelicSites[0]", Assert.Throws<FormatException>(() => MapFile.FromJson(json.Replace("\"Tier\": \"Rare\"", "\"Tier\": \"Epic\"", StringComparison.Ordinal))).Message, StringComparison.Ordinal);
        Assert.Throws<FormatException>(() => MapFile.FromJson(json.Replace("\"Tier\": \"Rare\"", "\"Tier\": null", StringComparison.Ordinal)));

        // 小图：清单为空、导出文件没有该段，且与引入遗址之前逐字节相同（link:1:4 的内容摘要由改动前 main 497f7383 的 Release Sim 导出文件按 MapFile.Digest 口径算出钉住）。
        MapData small = MapCatalog.Resolve("link:1:4");
        Assert.Empty(small.RelicSites);
        string smallJson = MapFile.ToJson(small);
        Assert.DoesNotContain("RelicSites", smallJson, StringComparison.Ordinal);
        Assert.Equal("6A0ECAB53DCD1C15863F1243B5F1A8E6281893F5D2D3CBE2740169C12CCD6381", MapFile.Digest(small));
        Assert.Empty(MapFile.FromJson(smallJson).RelicSites);
    }

    [Fact]
    public void 校验器守遗址规则()
    {
        MapData large = MapCatalog.Resolve("link:7:4:l");
        ImmutableArray<RelicSite> sites = large.RelicSites;
        static string[] Codes(MapData map) => [.. MapValidator.Validate(map).Failures.Select(f => f.Code)];

        // 数量：少一处 / 档位错一处。
        Assert.Contains("RELIC_SITE_COUNT", Codes(large with { RelicSites = sites.RemoveAt(0) }));
        Assert.Contains("RELIC_SITE_COUNT", Codes(large with { RelicSites = sites.SetItem(0, sites[0] with { Tier = RelicTier.Common }) }));
        Assert.Empty(Codes(large with { RelicSites = [.. sites.Reverse()] }));   // 次序不是校验项

        // 位置：挪到岸边 / 岩石旁、不可落子格、重复。
        Coord shore = large.AllCoords().First(c => large.IsPlayable(c) && !Inland(large, c));
        Assert.Contains("RELIC_SITE_GROUND", Codes(large with { RelicSites = sites.SetItem(7, sites[7] with { Cell = shore }) }));
        Coord rock = large.Obstacles.First(c => c.X > 10 && c.Y > 10);
        Assert.Contains("RELIC_SITE_GROUND", Codes(large with { RelicSites = sites.SetItem(7, sites[7] with { Cell = rock }) }));
        // 追加裁决 13：挪到 h ≥ 1 的可落子格（平台顶 / 坡道，八邻都可落子）也不合位置规则。
        Coord high = large.AllCoords().First(c => Inland(large, c) && large.HeightAt(c) > 0);
        Assert.Contains("RELIC_SITE_GROUND", Codes(large with { RelicSites = sites.SetItem(7, sites[7] with { Cell = high }) }));
        Assert.Contains("RELIC_SITE_GROUND", Codes(large with { RelicSites = sites.SetItem(7, sites[7] with { Cell = sites[6].Cell }) }));

        // 距离：把一处挪到另一处旁边（挪到的格本身合位置规则）。
        Coord near = large.AllCoords().First(c => Inland(large, c) && Chebyshev(c, sites[0].Cell) is > 0 and < 20);
        string[] gap = Codes(large with { RelicSites = sites.SetItem(7, sites[7] with { Cell = near }) });
        Assert.Contains("RELIC_SITE_GAP", gap);
        Assert.DoesNotContain("RELIC_SITE_GROUND", gap);

        // 落座图：遗址落进出生棋盘内被拒（兜底；合规旗格本身已保证距离 ≥ 7）。
        Coord flag = Siege.Core.Match.FlagRules.LegalFlagCells(large)[0];
        MapData seated = Siege.Core.Match.FlagRules.Seat(large, [flag]);
        Assert.Empty(Codes(seated));
        Assert.Contains("RELIC_SITE_IN_BIRTH", Codes(seated with { RelicSites = sites.SetItem(7, sites[7] with { Cell = flag }) }));

        // 小图：清单必须为空。
        MapData small = MapCatalog.Resolve("link:1:4");
        Assert.Empty(Codes(small));
        Assert.Contains("RELIC_SITES_FORBIDDEN", Codes(small with { RelicSites = [new RelicSite(small.CentralEntrance, RelicTier.Common)] }));
    }
}
