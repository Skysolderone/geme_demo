using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>规格：map-definition —— Requirement: 地图静态校验规则</summary>
public class 地图静态校验规则Tests
{
    [Fact]
    public void 障碍占比越界()
    {
        MapData sparse = FourPlayerBaseMap.Create() with
        {
            Obstacles = [Coord.Parse("A6"), Coord.Parse("L6"), Coord.Parse("F1"), Coord.Parse("F11")],
        };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(sparse).Failures, f => f.Code == "OBSTACLE_RATIO_TOO_LOW");
        Assert.Contains("低于 8%", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(7, false)]    // 7.0% < 8%
    [InlineData(8, true)]     // 恰好 8%，区间是闭的
    [InlineData(12, true)]    // 恰好 12%，区间是闭的
    [InlineData(13, false)]   // 13.0% > 12%
    public void 障碍占比区间两端都是闭的(int obstacleCount, bool accepted)
    {
        // "障碍格数占外接区域的 8%–12%"：10×10 = 100 格，障碍数直接就是百分比。
        MapData map = TestMaps.Synthetic(size: 10, maxPlayers: 4);
        MapData withObstacles = map with { Obstacles = [.. map.FirstCells(obstacleCount)] };

        string[] codes = [.. MapValidator.Validate(withObstacles).Failures
            .Select(f => f.Code)
            .Where(c => c.StartsWith("OBSTACLE_RATIO", StringComparison.Ordinal))];

        Assert.Equal(accepted ? [] : new[] { obstacleCount < 8 ? "OBSTACLE_RATIO_TOO_LOW" : "OBSTACLE_RATIO_TOO_HIGH" }, codes);
    }

    [Fact]
    public void 障碍占比过高()
    {
        MapData map = TestMaps.Synthetic(size: 10, maxPlayers: 4);
        MapData crowded = map with { Obstacles = [.. map.FirstCells(30)] };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(crowded).Failures, f => f.Code == "OBSTACLE_RATIO_TOO_HIGH");
        Assert.Contains("高于 12%", failure.Message, StringComparison.Ordinal);
        Assert.Contains("30.0%", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 信物格必须标注所属分区()
    {
        // 公共区的信物格被标成出生区档位，或出生区的信物格被标成公共区，都必须报错——
        // 分区标错会让信物生成用错强度预算，而那时已经在对局里了。
        MapData map = FourPlayerBaseMap.Create();
        Coord inBirthZone = map.RelicCells.Keys.Order().First(c => map.BirthZoneOf(c) is not null);
        Coord contested = map.RelicCells.Keys.Order().First(c => map.BirthZoneOf(c) is null);

        MapData zoneSwapped = map with
        {
            RelicCells = map.RelicCells
                .SetItem(inBirthZone, new RelicCellSpec(RelicZone.Contested, BudgetTier.High))
                .SetItem(contested, new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth)),
        };

        var failures = MapValidator.Validate(zoneSwapped).Failures
            .Where(f => f.Code == "RELIC_ZONE_MISMATCH").ToArray();

        Assert.Equal(2, failures.Length);
        Assert.Contains(failures, f => f.Coords.Contains(inBirthZone));
        Assert.Contains(failures, f => f.Coords.Contains(contested));
    }

    [Fact]
    public void 出生区不得出现高阶预算信物()
    {
        // 设计文档 §8.1：出生区不生成效果 +2 的高阶信物
        MapData map = FourPlayerBaseMap.Create();
        Coord inBirthZone = map.RelicCells.Keys.Order().First(c => map.BirthZoneOf(c) is not null);
        MapData upgraded = map with
        {
            RelicCells = map.RelicCells.SetItem(inBirthZone, new RelicCellSpec(RelicZone.BirthZone, BudgetTier.High)),
        };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(upgraded).Failures, f => f.Code == "RELIC_BUDGET_MISMATCH");
        Assert.Contains(inBirthZone, failure.Coords);
    }

    [Fact]
    public void 出生区容不下九枚部署()
    {
        MapData map = FourPlayerBaseMap.Create();
        MapData shrunk = map with
        {
            BirthZones = map.BirthZones.SetItem(0, [.. map.BirthZones[0].Order().Take(8)]),
        };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(shrunk).Failures, f => f.Code == "BIRTH_ZONE_TOO_SMALL");
        Assert.Contains("出生区 0 只有 8 个可落子格", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 出生区恰好九格时通过()
    {
        // 阈值是"容得下 9 枚"，9 本身合法。
        MapData map = FourPlayerBaseMap.Create();
        MapData nineCells = map with
        {
            BirthZones = map.BirthZones.SetItem(0, [.. map.BirthZones[0].Order().Take(9)]),
        };

        Assert.DoesNotContain(
            MapValidator.Validate(nineCells).Failures, f => f.Code == "BIRTH_ZONE_TOO_SMALL");
    }

    [Fact]
    public void 出生区不得重叠()
    {
        MapData map = FourPlayerBaseMap.Create();
        Coord shared = map.BirthZones[0].Order().First();
        MapData overlapping = map with { BirthZones = map.BirthZones.SetItem(1, map.BirthZones[1].Add(shared)) };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(overlapping).Failures, f => f.Code == "BIRTH_ZONE_OVERLAP");
        Assert.Equal([shared.ToNotation()], failure.Coords.Notations());
    }

    [Fact]
    public void 中央入口与咽喉必须位于可落子格()
    {
        MapData map = FourPlayerBaseMap.Create();
        MapData broken = map with
        {
            Obstacles = map.Obstacles.Add(map.CentralEntrance).Add(map.ChokePoints.Order().First()),
        };

        var codes = MapValidator.Validate(broken).Failures.Select(f => f.Code).ToArray();

        Assert.Contains("CENTRAL_ENTRANCE_NOT_PLAYABLE", codes);
        Assert.Contains("CHOKE_NOT_PLAYABLE", codes);
    }

    [Fact]
    public void 地标不可达被报出()
    {
        // 用障碍把出生区 0 完全封死在角落：它到中央入口、公共信物与咽喉都不可达。
        // 不可达 MUST 报错，MUST NOT 被"距离算不出来就跳过"静默吞掉。
        MapData map = FourPlayerBaseMap.Create();
        MapData walled = map with
        {
            Obstacles = map.Obstacles.Union(
                Enumerable.Range(0, 11).Select(y => new Coord(5, y))
                    .Concat(Enumerable.Range(0, 11).Select(x => new Coord(x, 5)))),
        };

        Assert.Contains(MapValidator.Validate(walled).Failures, f => f.Code == "LANDMARK_UNREACHABLE");
    }

    [Fact]
    public void 尺寸非正与越界障碍被报出()
    {
        MapData map = FourPlayerBaseMap.Create();

        Assert.Contains(
            MapValidator.Validate(map with { Width = 0 }).Failures,
            f => f.Code == "MAP_DIMENSION_INVALID");
        Assert.Contains(
            MapValidator.Validate(map with { Obstacles = map.Obstacles.Add(new Coord(20, 20)) }).Failures,
            f => f.Code == "OBSTACLE_OUT_OF_BOUNDS" && f.Coords.Contains(new Coord(20, 20)));
        Assert.Contains(
            MapValidator.Validate(map with { Id = "  " }).Failures,
            f => f.Code == "MAP_ID_MISSING");
        Assert.Contains(
            MapValidator.Validate(map with { DistanceTolerance = -1 }).Failures,
            f => f.Code == "TOLERANCE_NEGATIVE");
    }

    [Fact]
    public void 出生区距离失衡()
    {
        // 把咽喉标注收窄到只剩左下角一处，四个出生区到"最近咽喉"的距离立刻拉开
        MapData lopsided = FourPlayerBaseMap.Create() with { ChokePoints = [Coord.Parse("C6")] };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(lopsided).Failures, f => f.Code == "DISTANCE_IMBALANCE");
        Assert.Contains("出生区 0 =", failure.Message, StringComparison.Ordinal);
        Assert.Contains("超出容差 1", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 必死口袋()
    {
        // 用障碍把出生区左下角封出一块 6 格死角（小于两眼所需的 8 格）
        MapData map = FourPlayerBaseMap.Create();
        MapData pocketed = map with
        {
            Obstacles = map.Obstacles.Union([
                Coord.Parse("C1"), Coord.Parse("C2"), Coord.Parse("C3"),
                Coord.Parse("A4"), Coord.Parse("B4")]),
        };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(pocketed).Failures, f => f.Code == "DEAD_POCKET");
        Assert.Contains("小于形成两眼所需的 8 格", failure.Message, StringComparison.Ordinal);
        Assert.Equal(["A1", "B1", "A2", "B2", "A3", "B3"], failure.Coords.Notations());
    }

    [Fact]
    public void 面积恰好等于两眼最小格数的空区不算口袋()
    {
        // 规格："连通空区面积小于 8 即判失败"——8 本身是合法的，阈值是闭的下界。
        // 下面用障碍把出生区 0 的角上圈出恰好 8 格（A1 B1 C1 A2 B2 C2 A3 B3）。
        MapData map = FourPlayerBaseMap.Create();
        MapData eightCellPocket = map with
        {
            Obstacles = map.Obstacles.Union([
                Coord.Parse("D1"), Coord.Parse("D2"), Coord.Parse("C3"),
                Coord.Parse("A4"), Coord.Parse("B4")]),
        };

        Assert.DoesNotContain(
            MapValidator.Validate(eightCellPocket).Failures, f => f.Code == "DEAD_POCKET");

        // 同一块区域少一格（7 格）就必须失败，证明上面那次通过不是因为规则没跑。
        MapData sevenCellPocket = eightCellPocket with
        {
            Obstacles = eightCellPocket.Obstacles.Add(Coord.Parse("C1")),
        };
        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(sevenCellPocket).Failures, f => f.Code == "DEAD_POCKET");
        Assert.Equal(["A1", "B1", "A2", "B2", "C2", "A3", "B3"], failure.Coords.Notations());
    }

    [Fact]
    public void 距离极差恰好等于容差时通过()
    {
        // 容差 1 是闭的：极差 1 必须通过，极差 2 必须失败。
        // 两个出生区分别是 A 列与 B/C 列的整列，地标只有中央入口 E5 一处。
        MapData Map(string zoneTwoColumn) => new()
        {
            Id = "test-distance",
            Width = 8,
            Height = 8,
            MaxPlayers = 2,
            Obstacles = [],
            BirthZones = [Column("A"), Column(zoneTwoColumn)],
            RelicCells = System.Collections.Immutable.ImmutableDictionary<Coord, RelicCellSpec>.Empty,
            ChokePoints = [Coord.Parse("E5")],
            CentralEntrance = Coord.Parse("E5"),
        };

        // A 列到 E5 的最短距离 4，B 列 3 → 极差 1
        Assert.DoesNotContain(MapValidator.Validate(Map("B")).Failures, f => f.Code == "DISTANCE_IMBALANCE");

        // A 列 4，C 列 2 → 极差 2
        Assert.Contains(MapValidator.Validate(Map("C")).Failures, f => f.Code == "DISTANCE_IMBALANCE");
    }

    [Fact]
    public void 出生区之外的小空区不算必死口袋()
    {
        // 规格只禁止"出生区内"的必死口袋——公共区的封闭小空区是设计师的装饰自由，
        // 误报会把合法地图挡在门外。这里用一张没有出生区的合成图围出 4 格封闭空区。
        MapData map = TestMaps.Synthetic(size: 10, maxPlayers: 4);
        MapData enclosed = map with
        {
            Obstacles = [.. new[] { "B1", "C1", "A2", "D2", "A3", "D3", "B4", "C4" }.Select(Coord.Parse)],
        };

        Assert.DoesNotContain(MapValidator.Validate(enclosed).Failures, f => f.Code == "DEAD_POCKET");

        // 同一块空区一旦被划进出生区，就必须报出来
        MapData inBirthZone = enclosed with
        {
            BirthZones = [[.. new[] { "B2", "C2", "B3", "C3" }.Select(Coord.Parse)], [], [], []],
        };
        Assert.Contains(MapValidator.Validate(inBirthZone).Failures, f => f.Code == "DEAD_POCKET");
    }

    [Fact]
    public void 必死口袋可显式豁免()
    {
        MapData map = FourPlayerBaseMap.Create();
        var walls = new[]
        {
            Coord.Parse("C1"), Coord.Parse("C2"), Coord.Parse("C3"),
            Coord.Parse("A4"), Coord.Parse("B4"),
        };
        MapData exempted = map with
        {
            Obstacles = map.Obstacles.Union(walls),
            PocketExemptions = [Coord.Parse("A1")],
            PocketExemptionReasons = ImmutableDictionary<Coord, string>.Empty
                .Add(Coord.Parse("A1"), "测试用：刻意保留的装饰性死角"),
        };

        Assert.DoesNotContain(MapValidator.Validate(exempted).Failures, f => f.Code == "DEAD_POCKET");
    }

    [Fact]
    public void 豁免必须写明理由()
    {
        MapData map = FourPlayerBaseMap.Create() with { PocketExemptions = [Coord.Parse("A1")] };

        Assert.Contains(
            MapValidator.Validate(map).Failures, f => f.Code == "POCKET_EXEMPTION_WITHOUT_REASON");
    }

    [Fact]
    public void 信物格必须位于可落子格()
    {
        MapData map = FourPlayerBaseMap.Create();
        MapData broken = map with { Obstacles = map.Obstacles.Add(Coord.Parse("D4")) };

        Assert.Contains(
            MapValidator.Validate(broken).Failures,
            f => f.Code == "RELIC_ON_NON_PLAYABLE" && f.Coords.Contains(Coord.Parse("D4")));
    }

    [Fact]
    public void 咽喉必须显式标注()
    {
        MapData map = FourPlayerBaseMap.Create() with { ChokePoints = [] };

        Assert.Contains(MapValidator.Validate(map).Failures, f => f.Code == "CHOKE_NOT_ANNOTATED");
    }

    [Fact]
    public void 放宽容差必须写明理由()
    {
        MapData map = FourPlayerBaseMap.Create();

        Assert.Contains(
            MapValidator.Validate(map with { DistanceTolerance = 3 }).Failures,
            f => f.Code == "TOLERANCE_RELAX_WITHOUT_REASON");
        Assert.DoesNotContain(
            MapValidator.Validate(map with
            {
                DistanceTolerance = 3,
                ToleranceRelaxReason = "3 人图不套方形对称，按 §3.2 逐图放宽",
            }).Failures,
            f => f.Code == "TOLERANCE_RELAX_WITHOUT_REASON");
    }

    [Fact]
    public void 校验不通过则拒绝加载()
    {
        MapData map = FourPlayerBaseMap.Create() with { ChokePoints = [] };

        MapValidationException ex = Assert.Throws<MapValidationException>(() => GameBoard.Load(map));

        Assert.Contains(ex.Result.Failures, f => f.Code == "CHOKE_NOT_ANNOTATED");
    }

    /// <summary>整列格子，用于距离容差的边界构造。</summary>
    private static System.Collections.Immutable.ImmutableHashSet<Coord> Column(string letter) =>
        [.. Enumerable.Range(1, 8).Select(row => Coord.Parse($"{letter}{row}"))];
}
