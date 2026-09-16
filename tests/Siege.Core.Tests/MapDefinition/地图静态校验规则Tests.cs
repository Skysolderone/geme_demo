using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>规格：map-definition —— Requirement: 地图静态校验规则</summary>
public class 地图静态校验规则Tests
{
    /// <summary>v1 基准图的 12 格障碍集合：可落子 109、障碍占比 9.9%，两条区间都越界。</summary>
    private static readonly string[] V1Obstacles =
    [
        "C5", "J5", "C7", "J7", "E3", "G3", "E9", "G9", "A6", "L6", "F1", "F11",
    ];

    [Fact]
    public void 障碍占比越界()
    {
        // 规格 Scenario：11×11 地图的障碍格为 12 个（约 9.9%）→ 报告低于 25%。用的就是 v1 基准图的障碍集合。
        MapData sparse = FourPlayerBaseMap.Create() with { Obstacles = [.. V1Obstacles.Select(Coord.Parse)] };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(sparse).Failures, f => f.Code == "OBSTACLE_RATIO_TOO_LOW");
        Assert.Contains("障碍格 12 个", failure.Message, StringComparison.Ordinal);
        Assert.Contains("9.9%", failure.Message, StringComparison.Ordinal);
        Assert.Contains("低于 25%", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 可落子格越界()
    {
        // 规格 Scenario：可落子格为 109 的 11×11 地图 → 拒绝并报告超出 80–95 区间。
        // 校验规则第 6 条，denser-map 新增：可落子格越界既不会报障碍占比、也不会报出生区，
        // 只会表现为"密度不对、冲突时点漂移"，必须有一条显式规则挡住。
        MapData v1 = FourPlayerBaseMap.Create() with { Obstacles = [.. V1Obstacles.Select(Coord.Parse)] };
        Assert.Equal(109, v1.PlayableCount);

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(v1).Failures, f => f.Code == "PLAYABLE_COUNT_OUT_OF_RANGE");
        Assert.Contains("可落子格为 109", failure.Message, StringComparison.Ordinal);
        Assert.Contains("80–95", failure.Message, StringComparison.Ordinal);

        // 下界 80 同样要挡住：区间只有上界有算例时，把下界写成 60 只会让消息文本变化，
        // 行为上一条测试都不红（check 变异 M-CK3 实测）。这里在基准图上再挖 10 格公共区障碍 → 75 格。
        MapData baseMap = FourPlayerBaseMap.Create();
        Coord[] extra = [.. baseMap.AllCoords()
            .Where(c => baseMap.TerrainAt(c) == Terrain.Playable && baseMap.BirthZoneOf(c) is null)
            .Order().Take(10)];
        MapData tooSparse = baseMap with { Obstacles = baseMap.Obstacles.Union(extra) };
        Assert.Equal(75, tooSparse.PlayableCount);

        MapValidationFailure low = Assert.Single(
            MapValidator.Validate(tooSparse).Failures, f => f.Code == "PLAYABLE_COUNT_OUT_OF_RANGE");
        Assert.Contains("可落子格为 75", low.Message, StringComparison.Ordinal);
        Assert.Contains("80–95", low.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 出生区可落子格超出区间()
    {
        // 「出生区内的障碍 MUST NOT 使该区的可落子格少于 12」：把出生区 0 再挖掉 2 格 → 11 格 → 报错。
        MapData map = FourPlayerBaseMap.Create();
        Coord[] extra = [.. map.BirthZones[0].Where(c => map.TerrainAt(c) == Terrain.Playable).Order().Take(2)];
        MapData shrunk = map with { Obstacles = map.Obstacles.Union(extra) };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(shrunk).Failures, f => f.Code == "BIRTH_ZONE_SIZE_OUT_OF_RANGE");
        Assert.Contains("出生区 0 有 11 个可落子格", failure.Message, StringComparison.Ordinal);
        Assert.Contains("12–14", failure.Message, StringComparison.Ordinal);

        // 14 格是上界：区形 15 格里只挖 1 格 → 14，必须通过。
        MapData fourteen = map with
        {
            Obstacles = map.Obstacles.Except(
                [Coord.Parse("B4"), Coord.Parse("K4"), Coord.Parse("B8"), Coord.Parse("K8")]),
        };
        Assert.DoesNotContain(
            MapValidator.Validate(fourteen).Failures, f => f.Code == "BIRTH_ZONE_SIZE_OUT_OF_RANGE");
    }

    [Theory]
    [InlineData(24, false)]   // 24.0% < 25%
    [InlineData(25, true)]    // 恰好 25%，区间是闭的
    [InlineData(35, true)]    // 恰好 35%，区间是闭的
    [InlineData(36, false)]   // 36.0% > 35%
    public void 障碍占比区间两端都是闭的(int obstacleCount, bool accepted)
    {
        // "障碍格数占外接区域的 25%–35%"（denser-map 裁决 5）：10×10 = 100 格，障碍数直接就是百分比。
        MapData map = TestMaps.Synthetic(size: 10, maxPlayers: 4);
        MapData withObstacles = map with { Obstacles = [.. map.FirstCells(obstacleCount)] };

        string[] codes = [.. MapValidator.Validate(withObstacles).Failures
            .Select(f => f.Code)
            .Where(c => c.StartsWith("OBSTACLE_RATIO", StringComparison.Ordinal))];

        Assert.Equal(accepted ? [] : new[] { obstacleCount < 25 ? "OBSTACLE_RATIO_TOO_LOW" : "OBSTACLE_RATIO_TOO_HIGH" }, codes);
    }

    [Fact]
    public void 障碍占比过高()
    {
        MapData map = TestMaps.Synthetic(size: 10, maxPlayers: 4);
        MapData crowded = map with { Obstacles = [.. map.FirstCells(48)] };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(crowded).Failures, f => f.Code == "OBSTACLE_RATIO_TOO_HIGH");
        Assert.Contains("高于 35%", failure.Message, StringComparison.Ordinal);
        Assert.Contains("48.0%", failure.Message, StringComparison.Ordinal);
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
        // 区形里现在有障碍（denser-map），所以要按"可落子格"截取，否则截到的 8 格里会混进障碍。
        MapData shrunk = map with
        {
            BirthZones = map.BirthZones.SetItem(
                0, [.. map.BirthZones[0].Where(c => map.TerrainAt(c) == Terrain.Playable).Order().Take(8)]),
        };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(shrunk).Failures, f => f.Code == "BIRTH_ZONE_TOO_SMALL");
        Assert.Contains("出生区 0 只有 8 个可落子格", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 出生区恰好九格时通过()
    {
        // 阈值是"容得下 9 枚"，9 本身合法。本条只管 BIRTH_ZONE_TOO_SMALL 这一码；
        // "每区 12–14 格"是另一条独立规则（BIRTH_ZONE_SIZE_OUT_OF_RANGE），由「出生区可落子格超出区间」把关。
        MapData map = FourPlayerBaseMap.Create();
        MapData nineCells = map with
        {
            BirthZones = map.BirthZones.SetItem(
                0, [.. map.BirthZones[0].Where(c => map.TerrainAt(c) == Terrain.Playable).Order().Take(9)]),
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
        // 用障碍把出生区 0 封出一块 7 格死角（小于两眼所需的 8 格）。
        // denser-map 之后 D1/D2 只剩 C1/C2 一条出路（E1、D3 已是障碍），所以墙必须绕开 C1/C2，
        // 否则会额外圈出一个 {D1, D2} 的 2 格口袋，Assert.Single 会因为出现两个口袋而红。
        MapData map = FourPlayerBaseMap.Create();
        MapData pocketed = map with
        {
            Obstacles = map.Obstacles.Union([
                Coord.Parse("A1"), Coord.Parse("C2"), Coord.Parse("B3"), Coord.Parse("A4")]),
        };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(pocketed).Failures, f => f.Code == "DEAD_POCKET");
        Assert.Contains("小于形成两眼所需的 8 格", failure.Message, StringComparison.Ordinal);
        Assert.Equal(["B1", "C1", "D1", "A2", "B2", "D2", "A3"], failure.Coords.Notations());
    }

    [Fact]
    public void 面积恰好等于两眼最小格数的空区不算口袋()
    {
        // 规格："连通空区面积小于 8 即判失败"——8 本身是合法的，阈值是闭的下界。
        // 下面用障碍把出生区 0 圈出恰好 8 格（A1 B1 C1 D1 A2 B2 D2 A3）。
        MapData map = FourPlayerBaseMap.Create();
        MapData eightCellPocket = map with
        {
            Obstacles = map.Obstacles.Union([
                Coord.Parse("C2"), Coord.Parse("B3"), Coord.Parse("A4")]),
        };

        Assert.DoesNotContain(
            MapValidator.Validate(eightCellPocket).Failures, f => f.Code == "DEAD_POCKET");

        // 同一块区域少一格（7 格）就必须失败，证明上面那次通过不是因为规则没跑。
        MapData sevenCellPocket = eightCellPocket with
        {
            Obstacles = eightCellPocket.Obstacles.Add(Coord.Parse("A1")),
        };
        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(sevenCellPocket).Failures, f => f.Code == "DEAD_POCKET");
        Assert.Equal(["B1", "C1", "D1", "A2", "B2", "D2", "A3"], failure.Coords.Notations());
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
            Coord.Parse("A1"), Coord.Parse("C2"), Coord.Parse("B3"), Coord.Parse("A4"),
        };
        MapData exempted = map with
        {
            Obstacles = map.Obstacles.Union(walls),
            PocketExemptions = [Coord.Parse("B1")],
            PocketExemptionReasons = ImmutableDictionary<Coord, string>.Empty
                .Add(Coord.Parse("B1"), "测试用：刻意保留的装饰性死角"),
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
        Assert.Contains(Coord.Parse("D4"), map.RelicCells.Keys);
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
