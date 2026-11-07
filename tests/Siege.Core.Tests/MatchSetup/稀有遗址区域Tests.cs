using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Core.PieceRelics;

namespace Siege.Core.Tests.MatchSetup;

/// <summary>
/// relic-zones 裁决 2 / 3 / 7：稀有信物遗址在界面上只公开区域（20×20 网格块，裁到画布内），旗心离区域须 ≥ 7（区域外扩 6 圈整块禁插），
/// 遗址自身的 13×13 禁插方块因此整个包在禁插块里；不合规文案只提区域、不带遗址坐标。普通遗址口径不变。
/// </summary>
public class 稀有遗址区域Tests
{
    private static readonly string[] Ids = ["link:1:4:l", "link:7:4:l", "link:3:2:l", "link:5:3:l"];

    [Fact]
    public void 区域是含遗址的网格块_裁到画布内_一块至多一处稀有遗址()
    {
        Assert.True(RelicSites.UncalibratedRareZoneSide <= RelicSites.UncalibratedSiteGap, "区域边长大于遗址间距，一块里可能有两处稀有遗址。");
        foreach (string id in Ids)
        {
            MapData map = MapCatalog.Resolve(id);
            ImmutableArray<RareSiteZone> zones = RelicSites.RareZones(map);
            RelicSite[] rare = [.. map.RelicSites.Where(s => s.Tier == RelicTier.Rare)];
            Assert.Equal(rare.Length, zones.Length);
            Assert.Equal(RelicSites.RareCount(map.MaxPlayers), zones.Length);
            Assert.Equal(zones.Length, zones.Distinct().Count());   // 不同的稀有遗址落在不同的块
            for (int i = 0; i < rare.Length; i++)
            {
                RareSiteZone zone = zones[i];
                Assert.True(zone.Contains(rare[i].Cell), $"{id}：{rare[i]} 不在其区域 {zone} 内。");
                Assert.Equal(0, zone.Origin.X % RelicSites.UncalibratedRareZoneSide);
                Assert.Equal(0, zone.Origin.Y % RelicSites.UncalibratedRareZoneSide);
                Assert.InRange(zone.Width, 1, RelicSites.UncalibratedRareZoneSide);
                Assert.InRange(zone.Height, 1, RelicSites.UncalibratedRareZoneSide);
                Assert.True(zone.Origin.X + zone.Width <= map.Width && zone.Origin.Y + zone.Height <= map.Height, $"{id}：区域 {zone} 出了画布。");
                Assert.Equal(zone, RelicSites.RareZoneOf(rare[i].Cell, map.Width, map.Height));   // 纯函数
            }
        }
    }

    [Fact]
    public void 区域外扩6圈整块禁插_文案只说区域不带坐标()
    {
        // large-60：60×60 上稀有区域四周常被高台禁插圈先挡下，"因区域被拒"与"恰 7 格合规"两条样本改为跨全部地图合计，不再要求每张图都有。
        int inZone = 0;
        bool ringLegal = false;
        foreach (string id in Ids)
        {
            MapData map = MapCatalog.Resolve(id);
            var legal = FlagRules.LegalFlagCells(map).ToHashSet();
            foreach (RelicSite site in map.RelicSites.Where(s => s.Tier == RelicTier.Rare))
            {
                RareSiteZone zone = RelicSites.RareZoneOf(site.Cell, map.Width, map.Height);
                for (int y = Math.Max(0, zone.Origin.Y - 6); y < Math.Min(map.Height, zone.Origin.Y + zone.Height + 6); y++)
                {
                    for (int x = Math.Max(0, zone.Origin.X - 6); x < Math.Min(map.Width, zone.Origin.X + zone.Width + 6); x++)
                    {
                        var c = new Coord(x, y);
                        Assert.False(legal.Contains(c), $"{id}：{c.ToNotation()} 离稀有区域 {zone.DistanceTo(c)} 格却在合规旗格里。");
                        string problem = FlagRules.FlagCellProblem(map, c)!;
                        Assert.NotNull(problem);
                        // 文案开头是悬停格自己的坐标（指针就在那儿，不算泄露），其后不得再出现遗址坐标。
                        Assert.DoesNotContain(site.Cell.ToNotation(), problem[c.ToNotation().Length..], StringComparison.Ordinal);
                        if (problem.Contains("稀有信物遗址区域", StringComparison.Ordinal))
                        {
                            inZone++;
                        }
                    }
                }

            }

            // 区域外恰 7 格的一圈上：凡其余判据都满足的格合规（规则是 ≥ 7 而不是 > 7）——取一处能找到这种格的区域即可。
            ringLegal |= RelicSites.RareZones(map).Any(zone => Ring(map, zone, 7).Any(legal.Contains));
        }

        Assert.True(inZone > 0, "所有样本图的稀有区域周围都没有因区域被拒的格，测不到区域文案。");
        Assert.True(ringLegal, "所有样本图的稀有区域外恰 7 格一圈上都没有合规格。");
    }

    [Fact]
    public void AI盲选的旗都离稀有区域至少7()
    {
        foreach (string id in Ids)
        {
            MapData map = MapCatalog.Resolve(id);
            PlayerId[] players = [.. Enumerable.Range(0, map.MaxPlayers).Select(i => new PlayerId(i))];
            for (ulong seed = 1; seed <= 2; seed++)
            {
                MatchFlow m = MatchFlow.Create(map, new Siege.Core.Determinism.GameSeed(seed), players, LinkModeFixtures.Options);
                m.PlantPrototype();
                foreach (PlayerId p in players)
                {
                    Coord flag = m.Flags.FlagCellOf(p)!.Value;
                    Assert.All(RelicSites.RareZones(map), z => Assert.True(z.DistanceTo(flag) >= 7, $"{id} 种子 {seed}：旗 {flag.ToNotation()} 离稀有区域 {z} 只有 {z.DistanceTo(flag)}。"));
                }
            }
        }
    }

    private static IEnumerable<Coord> Ring(MapData map, RareSiteZone zone, int d)
    {
        for (int y = zone.Origin.Y - d; y <= zone.Origin.Y + zone.Height - 1 + d; y++)
        {
            for (int x = zone.Origin.X - d; x <= zone.Origin.X + zone.Width - 1 + d; x++)
            {
                if (x >= 0 && y >= 0 && x < map.Width && y < map.Height && zone.DistanceTo(new Coord(x, y)) == d)
                {
                    yield return new Coord(x, y);
                }
            }
        }
    }
}
