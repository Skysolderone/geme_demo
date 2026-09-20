using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>规格：map-definition —— Requirement: 人数适配预算</summary>
public class 人数适配预算Tests
{
    [Fact]
    public void 格数超出预算()
    {
        // 规格 Scenario：4 人地图的可落子格为 130 → 拒绝并报告超出 95–110（terrain-model 裁决 D18）。
        // 12×12 = 144 外接的合成图，只留 14 格障碍 → 可落子恰好 130，与规格算例逐字对上。
        // （不再拿 v3 基准图裁尺寸：13×13 的地形数据在 12×12 上会越界，触发的是 TERRAIN_OUT_OF_BOUNDS。）
        MapData plain = TestMaps.Synthetic(size: 12, maxPlayers: 4);
        MapData oversized = plain with { Obstacles = [.. plain.FirstCells(14)] };
        Assert.Equal(130, oversized.PlayableCount);

        MapValidationResult result = MapValidator.Validate(oversized);

        MapValidationFailure failure = Assert.Single(
            result.Failures, f => f.Code == "PLAYABLE_COUNT_OUT_OF_RANGE");
        Assert.Contains("可落子格为 130", failure.Message, StringComparison.Ordinal);
        Assert.Contains("95–110", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 信物格数不足()
    {
        MapData map = FourPlayerBaseMap.Create();
        MapData stripped = map with
        {
            RelicCells = map.RelicCells
                .Where(kv => kv.Value.Zone == RelicZone.Contested)
                .ToImmutableDictionary(kv => kv.Key, kv => kv.Value),
        };

        MapValidationResult result = MapValidator.Validate(stripped);

        MapValidationFailure failure = Assert.Single(
            result.Failures, f => f.Code == "RELIC_COUNT_OUT_OF_RANGE");
        Assert.Contains("13–15", failure.Message, StringComparison.Ordinal);
        Assert.Contains("少于下限 13", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 出生区数量必须等于最大人数()
    {
        MapData map = FourPlayerBaseMap.Create();
        MapData missingZone = map with { BirthZones = map.BirthZones.RemoveAt(3) };

        MapValidationResult result = MapValidator.Validate(missingZone);

        Assert.Contains(result.Failures, f => f.Code == "BIRTH_ZONE_COUNT_MISMATCH");
    }

    [Theory]
    [InlineData(16, "多于上限 14")]
    [InlineData(15, "多于上限 14")]
    [InlineData(9, "少于下限 10")]
    public void 据点数越界(int count, string direction)
    {
        // 规格 Scenario（scoring-sites）：4 人地图据点为 16 个 → 拒绝并报告超出 10–14 区间。
        // 15 与 9 是紧贴区间外的边界：只测 16 的话把上界写成 15，行为上一条都不红。
        MapData map = WithSiteCount(FourPlayerBaseMap.Create(), count);
        Assert.Equal(count, map.Sites.Count);

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(map).Failures, f => f.Code == "SITE_COUNT_OUT_OF_RANGE");
        Assert.Contains($"据点为 {count} 个", failure.Message, StringComparison.Ordinal);
        Assert.Contains(direction, failure.Message, StringComparison.Ordinal);
        Assert.Contains("10–14", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(14)]
    public void 据点数恰在区间端点时通过(int count)
    {
        MapData map = WithSiteCount(FourPlayerBaseMap.Create(), count);
        Assert.Equal(count, map.Sites.Count);

        Assert.DoesNotContain(MapValidator.Validate(map).Failures, f => f.Code == "SITE_COUNT_OUT_OF_RANGE");
    }

    [Theory]
    [InlineData(2, 5, "6–8")]
    [InlineData(2, 9, "6–8")]
    [InlineData(3, 8, "9–11")]
    [InlineData(3, 12, "9–11")]
    public void 两人三人据点数区间(int players, int count, string range)
    {
        // scoring-sites map-definition 预算表：2 人 6–8、3 人 9–11（估值，定稿时重估）。合成图只用于触发这一条。
        MapData plain = TestMaps.Synthetic(size: 9, maxPlayers: players);
        MapData map = plain with
        {
            Sites = plain.AllCoords().Take(count).ToImmutableDictionary(c => c, _ => SiteTier.Stele),
        };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(map).Failures, f => f.Code == "SITE_COUNT_OUT_OF_RANGE");
        Assert.Contains($"据点为 {count} 个", failure.Message, StringComparison.Ordinal);
        Assert.Contains(range, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>把 v4 的 12 个据点增删到 <paramref name="count"/> 个：增加的取岛上内圈（可落子、非信物、非据点），删除按坐标序从末尾删。</summary>
    private static MapData WithSiteCount(MapData map, int count)
    {
        ImmutableDictionary<Coord, SiteTier> sites = map.Sites;
        string[] spare = ["G6", "F7", "H7", "G8"];
        foreach (string s in spare.Take(Math.Max(0, count - sites.Count)))
        {
            sites = sites.Add(Coord.Parse(s), SiteTier.Stele);
        }

        foreach (Coord c in sites.Keys.Order().Reverse().Take(Math.Max(0, sites.Count - count)).ToArray())
        {
            sites = sites.Remove(c);
        }

        return map with { Sites = sites };
    }

    [Fact]
    public void 两人预算区间()
    {
        // 设计文档 §3.2：2 人 → 可落子 50–65、出生区 2、信物 7–9
        // 规格 Scenario「信物格数不足」：2 人地图只有 5 个信物格 → 报告少于 7
        MapData twoPlayer = TestMaps.Synthetic(size: 8, maxPlayers: 2, relics: FiveContestedRelics());

        MapValidationResult result = MapValidator.Validate(twoPlayer);

        Assert.DoesNotContain(result.Failures, f => f.Code == "PLAYABLE_COUNT_OUT_OF_RANGE"); // 64 ∈ 50–65
        MapValidationFailure relics = Assert.Single(result.Failures, f => f.Code == "RELIC_COUNT_OUT_OF_RANGE");
        Assert.Contains("信物格为 5", relics.Message, StringComparison.Ordinal);
        Assert.Contains("少于下限 7", relics.Message, StringComparison.Ordinal);
        Assert.Contains("7–9", relics.Message, StringComparison.Ordinal);

        // 49 格低于下限 50
        MapValidationFailure tooSmall = Assert.Single(
            MapValidator.Validate(TestMaps.Synthetic(size: 7, maxPlayers: 2)).Failures,
            f => f.Code == "PLAYABLE_COUNT_OUT_OF_RANGE");
        Assert.Contains("50–65", tooSmall.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 三人预算区间()
    {
        // 设计文档 §3.2：3 人 → 可落子 75–90、出生区 3、信物 10–12
        MapData threePlayer = TestMaps.Synthetic(size: 10, maxPlayers: 3);

        MapValidationResult result = MapValidator.Validate(threePlayer);

        MapValidationFailure playable = Assert.Single(result.Failures, f => f.Code == "PLAYABLE_COUNT_OUT_OF_RANGE");
        Assert.Contains("可落子格为 100", playable.Message, StringComparison.Ordinal);
        Assert.Contains("75–90", playable.Message, StringComparison.Ordinal);

        MapValidationFailure relics = Assert.Single(result.Failures, f => f.Code == "RELIC_COUNT_OUT_OF_RANGE");
        Assert.Contains("10–12", relics.Message, StringComparison.Ordinal);

        // 9×9 = 81 ∈ 75–90
        Assert.DoesNotContain(
            MapValidator.Validate(TestMaps.Synthetic(size: 9, maxPlayers: 3)).Failures,
            f => f.Code == "PLAYABLE_COUNT_OUT_OF_RANGE");
    }

    [Fact]
    public void 不支持的人数被拒绝()
    {
        MapData map = FourPlayerBaseMap.Create() with { MaxPlayers = 5 };

        Assert.Contains(MapValidator.Validate(map).Failures, f => f.Code == "UNSUPPORTED_PLAYER_COUNT");
    }

    // ---------- frontier-map：边疆档 4 人预算（300–420 / 5–8 区且多于人数 / 单区 20–225 / 信物 14–24 / 据点 12–22） ----------

    [Fact]
    public void 边疆档按自己的区间校验()
    {
        // 规格 Scenario：4 人边疆档，可落子 360、出生区 6、信物 16、据点 16 → 规模校验通过；同样的数字标成标准档因可落子超出 95–110 被拒。
        // 变异 M-A3：把声明表里边疆档的可落子区间改成标准档的 95–110 → 本测试红。
        MapData frontier = FrontierFixtures.Map();
        Assert.Equal(360, frontier.PlayableCount);
        Assert.Equal(6, frontier.BirthZones.Length);
        Assert.Equal(16, frontier.RelicCells.Count);
        Assert.Equal(16, frontier.Sites.Count);

        MapValidationResult accepted = MapValidator.Validate(frontier);
        Assert.True(accepted.IsValid, accepted.ToString());
        GameBoard.Load(frontier);   // 加载路径同样放行

        MapValidationResult asStandard = MapValidator.Validate(frontier with { Profile = MapProfile.Standard });
        MapValidationFailure playable = Assert.Single(asStandard.Failures, f => f.Code == "PLAYABLE_COUNT_OUT_OF_RANGE");
        Assert.Contains("可落子格为 360", playable.Message, StringComparison.Ordinal);
        Assert.Contains("95–110", playable.Message, StringComparison.Ordinal);
        Assert.Contains(asStandard.Failures, f => f.Code == "BIRTH_ZONE_COUNT_MISMATCH");   // 标准档：6 ≠ 4
        Assert.Throws<MapValidationException>(() => GameBoard.Load(frontier with { Profile = MapProfile.Standard }));
    }

    [Fact]
    public void 边疆档出生区数不得等于人数()
    {
        // 规格 Scenario：4 人边疆档出生区恰为 4 个 → 拒绝并报告低于 5–8 区间。
        // 变异 M-A19：声明表里边疆档的 ZonesMustExceedPlayers 改成 false → 本测试红。
        MapData frontier = FrontierFixtures.Map();
        MapData fourZones = frontier with { BirthZones = [.. frontier.BirthZones.Take(4)] };

        MapValidationResult result = MapValidator.Validate(fourZones);

        MapValidationFailure count = Assert.Single(result.Failures, f => f.Code == "BIRTH_ZONE_COUNT_OUT_OF_RANGE");
        Assert.Contains("出生区为 4 个", count.Message, StringComparison.Ordinal);
        Assert.Contains("低于下限 5", count.Message, StringComparison.Ordinal);
        Assert.Contains("5–8", count.Message, StringComparison.Ordinal);
        Assert.Contains(result.Failures, f => f.Code == "BIRTH_ZONE_COUNT_NOT_ABOVE_PLAYERS");
        Assert.DoesNotContain(result.Failures, f => f.Code == "BIRTH_ZONE_COUNT_MISMATCH");   // "必须等于人数"是标准档的报文
    }

    [Theory]
    [InlineData(5, null)]
    [InlineData(8, null)]
    [InlineData(9, "高于上限 8")]
    public void 边疆档出生区数区间端点(int zones, string? direction)
    {
        // 5 与 8 是区间端点（通过），9 紧贴上界之外。增补的三个区都是 ≥ 20 格的空地矩形，不与既有平台重叠。
        MapData frontier = FrontierFixtures.Map();
        ImmutableHashSet<Coord>[] extra =
        [
            [.. FrontierFixtures.Rect(0, 6, 5, 5)],
            [.. FrontierFixtures.Rect(15, 6, 5, 5)],
            [.. FrontierFixtures.Rect(5, 6, 2, 10)],
        ];
        MapData map = frontier with { BirthZones = [.. frontier.BirthZones.Concat(extra).Take(zones)] };
        Assert.Equal(zones, map.BirthZones.Length);

        MapValidationFailure[] count = [.. MapValidator.Validate(map).Failures.Where(f => f.Code.StartsWith("BIRTH_ZONE_COUNT", StringComparison.Ordinal))];

        if (direction is null)
        {
            Assert.Empty(count);
        }
        else
        {
            Assert.Contains(direction, Assert.Single(count).Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void 边疆档平台过小()
    {
        // 规格 Scenario：某平台只有 12 个可落子格 → 拒绝并指出该平台编号与 20–225 区间。
        // 变异 M-A23：声明表里单区下界 20 改成 12 → 本测试与「边疆档单区下界端点」红。
        // 12 ≥ 9，所以触发的只是边疆档的单区区间，不是两档共用的"容不下 9 枚部署"。
        MapData frontier = FrontierFixtures.Map();
        MapData shrunk = frontier with { BirthZones = frontier.BirthZones.SetItem(2, [.. FrontierFixtures.Rect(0, 13, 3, 4)]) };
        Assert.Equal(12, shrunk.BirthZones[2].Count(shrunk.IsPlayable));

        MapValidationResult result = MapValidator.Validate(shrunk);

        MapValidationFailure size = Assert.Single(result.Failures, f => f.Code == "BIRTH_ZONE_SIZE_OUT_OF_RANGE");
        Assert.Contains("出生区 3 ", size.Message, StringComparison.Ordinal);
        Assert.Contains("12 个可落子格", size.Message, StringComparison.Ordinal);
        Assert.Contains("20–225", size.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Failures, f => f.Code == "BIRTH_ZONE_TOO_SMALL");
    }

    [Theory]
    [InlineData(19, false)]
    [InlineData(20, true)]
    public void 边疆档单区下界端点(int cells, bool accepted)
    {
        MapData frontier = FrontierFixtures.Map();
        MapData map = frontier with { BirthZones = frontier.BirthZones.SetItem(2, [.. FrontierFixtures.Rect(0, 13, 5, 5).Take(cells)]) };

        Assert.Equal(accepted, !MapValidator.Validate(map).Failures.Any(f => f.Code == "BIRTH_ZONE_SIZE_OUT_OF_RANGE"));
    }

    [Theory]
    [InlineData(225, true)]
    [InlineData(226, false)]
    public void 边疆档单区上界端点(int cells, bool accepted)
    {
        // 单区 20–225 的上界（15×15）此前没有行为端点：检查阶段变异 M-C6（声明表 225 → 224）补测前只红 1——
        // 「边疆档平台过小」里的报文文本 "20–225"，225 格的平台是否被接受没人问过；补测后红 3（本测试两行 + 那一条）。
        // 3 号台换成左下角 15×15 的整块（226 时再多取相邻一列的一格）；它与别的平台重叠会另报别的码，这里只看单区规模这一条。
        MapData frontier = FrontierFixtures.Map();
        Coord[] big = [.. FrontierFixtures.Rect(0, 0, 15, 15), new Coord(15, 0)];
        MapData map = frontier with { BirthZones = frontier.BirthZones.SetItem(2, [.. big.Take(cells)]) };
        Assert.Equal(cells, map.BirthZones[2].Count(map.IsPlayable));

        MapValidationFailure[] size = [.. MapValidator.Validate(map).Failures.Where(f => f.Code == "BIRTH_ZONE_SIZE_OUT_OF_RANGE")];

        Assert.Equal(accepted, size.Length == 0);
        Assert.All(size, f =>
        {
            Assert.Contains("出生区 3 ", f.Message, StringComparison.Ordinal);
            Assert.Contains("20–225", f.Message, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData(299, false)]
    [InlineData(300, true)]
    public void 边疆档可落子下界端点(int playable, bool accepted)
    {
        MapData frontier = FrontierFixtures.Map();
        MapData map = frontier with { Obstacles = [.. frontier.Obstacles, .. FrontierFixtures.FreeCells(frontier).Take(360 - playable)] };
        Assert.Equal(playable, map.PlayableCount);

        MapValidationFailure[] failures = [.. MapValidator.Validate(map).Failures.Where(f => f.Code == "PLAYABLE_COUNT_OUT_OF_RANGE")];

        Assert.Equal(accepted, failures.Length == 0);
        Assert.All(failures, f => Assert.Contains("300–420", f.Message, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(420, true)]
    [InlineData(421, false)]
    public void 边疆档可落子上界端点(int playable, bool accepted)
    {
        // 21×21 = 441 的合成图只用于触发这一条。
        MapData plain = TestMaps.Synthetic(size: 21, maxPlayers: 4) with { Profile = MapProfile.Frontier };
        MapData map = plain with { Obstacles = [.. plain.FirstCells(441 - playable)] };
        Assert.Equal(playable, map.PlayableCount);

        Assert.Equal(accepted, !MapValidator.Validate(map).Failures.Any(f => f.Code == "PLAYABLE_COUNT_OUT_OF_RANGE"));
    }

    [Theory]
    [InlineData(13, "少于下限 14")]
    [InlineData(14, null)]
    [InlineData(24, null)]
    [InlineData(25, "多于上限 24")]
    public void 边疆档信物数区间端点(int count, string? direction)
    {
        // 「只改测试不改实现」的变异 M-A20（testing.md：专治测试抄实现）：把本测试期望的区间文本换成据点的 12–22 → 越界的两行红，
        // 说明信物 / 据点两个维度的期望值各自钉住了实现，而不是跟着实现走。
        MapData frontier = FrontierFixtures.Map();
        ImmutableDictionary<Coord, RelicCellSpec> relics = frontier.RelicCells;
        foreach (Coord c in relics.Where(kv => kv.Value.Budget == BudgetTier.Standard).Select(kv => kv.Key).Order().Take(Math.Max(0, 16 - count)))
        {
            relics = relics.Remove(c);
        }

        foreach (Coord c in FrontierFixtures.FreeCells(frontier).Take(Math.Max(0, count - 16)))
        {
            relics = relics.Add(c, new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard));
        }

        MapData map = frontier with { RelicCells = relics };
        Assert.Equal(count, map.RelicCells.Count);

        AssertRange(MapValidator.Validate(map), "RELIC_COUNT_OUT_OF_RANGE", direction, "14–24");
    }

    [Theory]
    [InlineData(11, "少于下限 12")]
    [InlineData(12, null)]
    [InlineData(22, null)]
    [InlineData(23, "多于上限 22")]
    public void 边疆档据点数区间端点(int count, string? direction)
    {
        MapData frontier = FrontierFixtures.Map();
        ImmutableDictionary<Coord, SiteTier> sites = frontier.Sites;
        foreach (Coord c in sites.Where(kv => kv.Value == SiteTier.Campfire).Select(kv => kv.Key).Order().Take(Math.Max(0, 16 - count)))
        {
            sites = sites.Remove(c);
        }

        foreach (Coord c in FrontierFixtures.FreeCells(frontier).Take(Math.Max(0, count - 16)))
        {
            sites = sites.Add(c, SiteTier.Campfire);
        }

        MapData map = frontier with { Sites = sites };
        Assert.Equal(count, map.Sites.Count);

        AssertRange(MapValidator.Validate(map), "SITE_COUNT_OUT_OF_RANGE", direction, "12–22");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void 边疆档两人三人报不支持(int players)
    {
        // 规格：验证版只定 4 人；2 / 3 人边疆图尚未设计，请求时 MUST 报"不支持"。同样人数的标准档是支持的（反面）。
        MapData map = FrontierFixtures.Map() with { MaxPlayers = players };

        MapValidationFailure failure = Assert.Single(MapValidator.Validate(map).Failures, f => f.Code == "UNSUPPORTED_PLAYER_COUNT");
        Assert.Contains($"不支持的人数 {players}", failure.Message, StringComparison.Ordinal);
        Assert.Contains("边疆档", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            MapValidator.Validate(map with { Profile = MapProfile.Standard }).Failures, f => f.Code == "UNSUPPORTED_PLAYER_COUNT");
    }

    private static void AssertRange(MapValidationResult result, string code, string? direction, string range)
    {
        MapValidationFailure[] failures = [.. result.Failures.Where(f => f.Code == code)];
        if (direction is null)
        {
            Assert.Empty(failures);
            return;
        }

        MapValidationFailure failure = Assert.Single(failures);
        Assert.Contains(direction, failure.Message, StringComparison.Ordinal);
        Assert.Contains(range, failure.Message, StringComparison.Ordinal);
    }

    private static KeyValuePair<Coord, RelicCellSpec>[] FiveContestedRelics() =>
        [.. new[] { "B2", "C3", "D4", "E5", "F6" }.Select(n => KeyValuePair.Create(
            Coord.Parse(n), new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard)))];
}
