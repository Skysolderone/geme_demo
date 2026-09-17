using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>规格：map-definition —— Requirement: 地图静态校验规则</summary>
public class 地图静态校验规则Tests
{
    [Fact]
    public void 可落子格越界()
    {
        // 规格 Scenario：可落子格为 85 的 4 人地图 → 拒绝并报告超出 95–110 区间。
        // v2 基准图恰是 85 格：它留在 maps/ 作历史存档，从 terrain-model 起不能加载（4.4）——这条测试同时钉住这一点。
        string path = Path.Combine(RepoRoot(), "maps", "siege-4p-base-v2.json");
        MapData v2 = MapFile.FromJson(File.ReadAllText(path));
        Assert.Equal(85, v2.PlayableCount);

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(v2).Failures, f => f.Code == "PLAYABLE_COUNT_OUT_OF_RANGE");
        Assert.Contains("可落子格为 85", failure.Message, StringComparison.Ordinal);
        Assert.Contains("95–110", failure.Message, StringComparison.Ordinal);
        Assert.Throws<MapValidationException>(() => GameBoard.Load(v2));

        // 上界 110 同样要挡住：区间只有下界有算例时，把上界写成 200 只会让消息文本变化，行为上一条测试都不红。
        // v3 去掉全部岩石 → 105 + 36 = 141。
        MapData tooOpen = FourPlayerBaseMap.Create() with { Obstacles = [] };
        Assert.Equal(141, tooOpen.PlayableCount);

        MapValidationFailure high = Assert.Single(
            MapValidator.Validate(tooOpen).Failures, f => f.Code == "PLAYABLE_COUNT_OUT_OF_RANGE");
        Assert.Contains("可落子格为 141", high.Message, StringComparison.Ordinal);
        Assert.Contains("95–110", high.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 距离沿气边计算()
    {
        // 规格 Scenario：两个出生区到中央入口的几何路径长度相同，其中一条要跨崖壁 → 跨崖那条不计入，按各自沿气边的最短路算。
        // 9×9 平地，入口 E5；出生区 0 = {A5}、出生区 1 = {J5}，几何距离都是 4。
        // 把 G1–G8 抬到 h=2（G9 留作绕行口）：出生区 1 只能绕 J9 → G9 → E9 → E5，气边最短路 12；出生区 0 仍是 4 → 失衡。
        // 变异验证 M-B2：MultiSourceDistances 改回 Adjacency.Neighbors → 两区都算 4，本测试红（守门测试同时红）。
        static MapData Wall(int height) =>
            TestMaps.Synthetic(
                size: 9,
                maxPlayers: 2,
                terrain: TestMaps.Terrain(heights: [.. Enumerable.Range(1, 8).Select(row => ($"G{row}", height))]))
            with
            {
                BirthZones = [[TestMaps.At("A5")], [TestMaps.At("J5")]],
                ChokePoints = [TestMaps.At("E5")],
                CentralEntrance = TestMaps.At("E5"),
            };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(Wall(2)).Failures,
            f => f.Code == "DISTANCE_IMBALANCE" && f.Message.Contains("中央入口", StringComparison.Ordinal));
        Assert.Contains("出生区 0 = 4", failure.Message, StringComparison.Ordinal);
        Assert.Contains("出生区 1 = 12", failure.Message, StringComparison.Ordinal);

        // 同一堵墙降为 h=1 缓坡：两条路都能走，距离相等，不报失衡——证明上面红的是崖壁而不是别的
        Assert.DoesNotContain(MapValidator.Validate(Wall(1)).Failures, f => f.Code == "DISTANCE_IMBALANCE");
    }

    [Fact]
    public void 出生区被孤立()
    {
        // 规格 Scenario：某出生区的全部边缘都是崖壁或深水，没有气边通向中央入口 → 拒绝并指出该出生区编号。
        // 9×9：出生区 0 = A1 B1 A2 B2 抬到 h=2，四周全是 h=0（崖壁）；出生区 1 = J9 在平地上可达。
        (string, int)[] plateau = [("A1", 2), ("B1", 2), ("A2", 2), ("B2", 2)];
        MapData cliffs = Isolation(TestMaps.Terrain(heights: plateau));

        MapValidationFailure failure = Assert.Single(MapValidator.Validate(cliffs).Failures, f => f.Code == "BIRTH_ZONE_ISOLATED");
        Assert.Contains("出生区 0", failure.Message, StringComparison.Ordinal);
        Assert.Equal(["A1", "B1", "A2", "B2"], failure.Coords.Notations());

        // 补一格 h=1 缓坡 C1 就连通了
        Assert.DoesNotContain(
            MapValidator.Validate(Isolation(TestMaps.Terrain(heights: [.. plateau, ("C1", 1)]))).Failures,
            f => f.Code == "BIRTH_ZONE_ISOLATED");

        // 深水同样封死：平地出生区被 C1 C2 C3 B3 A3 一圈深水围住
        MapData moat = Isolation(TestMaps.Terrain(surfaces:
            [("C1", Surface.DeepWater), ("C2", Surface.DeepWater), ("C3", Surface.DeepWater), ("B3", Surface.DeepWater), ("A3", Surface.DeepWater)]));
        Assert.Contains(MapValidator.Validate(moat).Failures, f => f.Code == "BIRTH_ZONE_ISOLATED" && f.Message.Contains("出生区 0", StringComparison.Ordinal));

        // 架一座桥就通了
        MapData bridged = Isolation(TestMaps.Terrain(
            surfaces: [("C1", Surface.DeepWater), ("C2", Surface.DeepWater), ("C3", Surface.DeepWater), ("B3", Surface.DeepWater), ("A3", Surface.DeepWater)],
            bridges: ["C2"]));
        Assert.DoesNotContain(MapValidator.Validate(bridged).Failures, f => f.Code == "BIRTH_ZONE_ISOLATED");
    }

    [Fact]
    public void 地形数据越界被报出()
    {
        // 3.2：高度 / 地表 / 桥 / 栅栏端点必须在盘内，越界项不参与任何查询。
        // "桥须在深水、栅栏须相邻"由 TerrainData 构造期抛出（裁决 A-1），校验器不重复。
        TerrainData terrain = TestMaps.Terrain(
            heights: [("M1", 1)],
            surfaces: [("L11", Surface.DeepWater)],
            bridges: ["L11"],
            fences: [("L11", "L12")]);
        MapData map = TestMaps.Synthetic(size: 9, maxPlayers: 4, terrain: terrain);

        MapValidationFailure failure = Assert.Single(MapValidator.Validate(map).Failures, f => f.Code == "TERRAIN_OUT_OF_BOUNDS");
        Assert.Equal(["M1", "L11", "L12"], failure.Coords.Notations());

        Assert.DoesNotContain(
            MapValidator.Validate(TestMaps.Synthetic(size: 9, maxPlayers: 4)).Failures, f => f.Code == "TERRAIN_OUT_OF_BOUNDS");
    }

    [Fact]
    public void 出生区可落子格超出区间()
    {
        // 「出生区内的障碍 MUST NOT 使该区的可落子格少于 12」：把出生区 0 挖掉 2 格 → 11 格 → 报错。
        MapData map = FourPlayerBaseMap.Create();
        Coord[] extra = [.. map.BirthZones[0].Where(map.IsPlayable).Order().Take(2)];
        MapData shrunk = map with { Obstacles = map.Obstacles.Union(extra) };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(shrunk).Failures, f => f.Code == "BIRTH_ZONE_SIZE_OUT_OF_RANGE");
        Assert.Contains("出生区 0 有 11 个可落子格", failure.Message, StringComparison.Ordinal);
        Assert.Contains("12–14", failure.Message, StringComparison.Ordinal);

        // 14 格是上界：把缓坡 E2 划进出生区 0（13 → 14）必须通过本条（距离随之失衡，那是另一条规则）。
        MapData fourteen = map with { BirthZones = map.BirthZones.SetItem(0, map.BirthZones[0].Add(Coord.Parse("E2"))) };
        Assert.DoesNotContain(
            MapValidator.Validate(fourteen).Failures, f => f.Code == "BIRTH_ZONE_SIZE_OUT_OF_RANGE");
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
            BirthZones = map.BirthZones.SetItem(
                0, [.. map.BirthZones[0].Where(map.IsPlayable).Order().Take(8)]),
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
                0, [.. map.BirthZones[0].Where(map.IsPlayable).Order().Take(9)]),
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
        // 用岩石把 F 列与第 6 行整条封死：出生区 0 的出口 E2/E3 撞在 F2/F3 上，它到中央入口、公共信物与咽喉都不可达。
        // 不可达 MUST 报错，MUST NOT 被"距离算不出来就跳过"静默吞掉。
        MapData map = FourPlayerBaseMap.Create();
        MapData walled = map with
        {
            Obstacles = map.Obstacles.Union(
                Enumerable.Range(0, map.Height).Select(y => new Coord(5, y))
                    .Concat(Enumerable.Range(0, map.Width).Select(x => new Coord(x, 5)))),
        };

        Assert.Contains(MapValidator.Validate(walled).Failures, f => f.Code == "LANDMARK_UNREACHABLE");
        Assert.Contains(MapValidator.Validate(walled).Failures, f => f.Code == "BIRTH_ZONE_ISOLATED");
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
        // 规格 Scenario：把咽喉标注收窄到只剩出生区 0 门前那座桥 G4，四个出生区到"最近咽喉"的距离立刻拉开
        MapData lopsided = FourPlayerBaseMap.Create() with { ChokePoints = [Coord.Parse("G4")] };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(lopsided).Failures, f => f.Code == "DISTANCE_IMBALANCE");
        Assert.Contains("出生区 0 = 4", failure.Message, StringComparison.Ordinal);
        Assert.Contains("超出容差 1", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 必死口袋()
    {
        // 用岩石 B1 C2 D3 在出生区 0（A1–D4 减 A1 C4 D4 的 13 格高台）里封出 7 格死角（小于两眼所需的 8 格）：
        // 高台四周本就是崖壁 / 岩石 / 深水，三块石头就够。剩下的 C1 D1 D2 经 D2–E2 缓坡仍与全盘连通，不会多出第二个口袋。
        MapData map = FourPlayerBaseMap.Create();
        MapData pocketed = map with
        {
            Obstacles = map.Obstacles.Union([Coord.Parse("B1"), Coord.Parse("C2"), Coord.Parse("D3")]),
        };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(pocketed).Failures, f => f.Code == "DEAD_POCKET");
        Assert.Contains("小于形成两眼所需的 8 格", failure.Message, StringComparison.Ordinal);
        Assert.Equal(["A2", "B2", "A3", "B3", "C3", "A4", "B4"], failure.Coords.Notations());
    }

    [Fact]
    public void 面积恰好等于两眼最小格数的空区不算口袋()
    {
        // 规格："连通空区面积小于 8 即判失败"——8 本身是合法的，阈值是闭的下界。
        // 用岩石 B1 C1 D1 D2 D3 把出生区 0 圈出恰好 8 格（A2 B2 C2 A3 B3 C3 A4 B4），区内不剩别的格。
        MapData map = FourPlayerBaseMap.Create();
        MapData eightCellPocket = map with
        {
            Obstacles = map.Obstacles.Union([
                Coord.Parse("B1"), Coord.Parse("C1"), Coord.Parse("D1"), Coord.Parse("D2"), Coord.Parse("D3")]),
        };

        Assert.DoesNotContain(
            MapValidator.Validate(eightCellPocket).Failures, f => f.Code == "DEAD_POCKET");

        // 同一块区域少一格（7 格）就必须失败，证明上面那次通过不是因为规则没跑。
        MapData sevenCellPocket = eightCellPocket with
        {
            Obstacles = eightCellPocket.Obstacles.Add(Coord.Parse("A2")),
        };
        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(sevenCellPocket).Failures, f => f.Code == "DEAD_POCKET");
        Assert.Equal(["B2", "C2", "A3", "B3", "C3", "A4", "B4"], failure.Coords.Notations());
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
            RelicCells = ImmutableDictionary<Coord, RelicCellSpec>.Empty,
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
        var walls = new[] { Coord.Parse("B1"), Coord.Parse("C2"), Coord.Parse("D3") };
        MapData exempted = map with
        {
            Obstacles = map.Obstacles.Union(walls),
            PocketExemptions = [Coord.Parse("A2")],
            PocketExemptionReasons = ImmutableDictionary<Coord, string>.Empty
                .Add(Coord.Parse("A2"), "测试用：刻意保留的装饰性死角"),
        };

        Assert.DoesNotContain(MapValidator.Validate(exempted).Failures, f => f.Code == "DEAD_POCKET");
    }

    [Fact]
    public void 豁免必须写明理由()
    {
        MapData map = FourPlayerBaseMap.Create() with { PocketExemptions = [Coord.Parse("A2")] };

        Assert.Contains(
            MapValidator.Validate(map).Failures, f => f.Code == "POCKET_EXEMPTION_WITHOUT_REASON");
    }

    [Fact]
    public void 信物格必须位于可落子格()
    {
        MapData map = FourPlayerBaseMap.Create();
        Assert.Contains(Coord.Parse("B2"), map.RelicCells.Keys);
        MapData broken = map with { Obstacles = map.Obstacles.Add(Coord.Parse("B2")) };

        Assert.Contains(
            MapValidator.Validate(broken).Failures,
            f => f.Code == "RELIC_ON_NON_PLAYABLE" && f.Coords.Contains(Coord.Parse("B2")));
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
    public void 据点与信物重合()
    {
        // 规格 Scenario（scoring-sites 规则 8）：把一个石碑标在信物格 G7 上 → 拒绝并指出该坐标。
        MapData map = FourPlayerBaseMap.Create();
        Coord g7 = Coord.Parse("G7");
        Assert.True(map.RelicCells.ContainsKey(g7));
        MapData broken = map with { Sites = map.Sites.Add(g7, SiteTier.Stele) };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(broken).Failures, f => f.Code == "SITE_ON_RELIC_CELL");
        Assert.Equal([g7], failure.Coords);
        Assert.Contains("G7", failure.ToString(), StringComparison.Ordinal);
        Assert.Throws<MapValidationException>(() => GameBoard.Load(broken));
    }

    [Fact]
    public void 据点在不可落子格()
    {
        // 规格 Scenario：把一个篝火标在未架桥的深水格上 → 拒绝并指出该坐标。C4 是护城河转角（未架桥深水）。
        MapData map = FourPlayerBaseMap.Create();
        Coord c4 = Coord.Parse("C4");
        Assert.True(map.TerrainData.IsUnbridgedDeepWater(c4));
        MapData broken = map with { Sites = map.Sites.Add(c4, SiteTier.Campfire) };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(broken).Failures, f => f.Code == "SITE_ON_NON_PLAYABLE");
        Assert.Equal([c4], failure.Coords);
    }

    [Fact]
    public void 据点必须标注档位()
    {
        // 规则 8：档位必填。SiteTier 从 1 起编，default(SiteTier) = 0 即"未标注"。
        MapData map = FourPlayerBaseMap.Create();
        Coord b3 = Coord.Parse("B3");
        MapData broken = map with { Sites = map.Sites.SetItem(b3, default) };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(broken).Failures, f => f.Code == "SITE_TIER_MISSING");
        Assert.Equal([b3], failure.Coords);
    }

    [Theory]
    [InlineData(SiteTier.Stele, "最近石碑")]
    [InlineData(SiteTier.Campfire, "最近篝火")]
    public void 到据点的距离失衡(SiteTier tier, string target)
    {
        // 规格 Scenario：出生区 A 到最近石碑的气边距离为 6，B 为 9 → 拒绝并指出出生区编号、目标"石碑"与两个距离值。
        // 10×10 平地：出生区 0 = {A5}、出生区 1 = {K5}；据点 D2 → A5 距 3+3 = 6，K5 距 6+3 = 9。
        // 中央入口与咽喉放在 F6（A5 距 6、K5 距 5，差 1 在容差内），让据点成为唯一的失衡来源；无信物 → 公共信物项跳过。
        MapData map = TestMaps.Synthetic(size: 10, maxPlayers: 2) with
        {
            BirthZones = [[TestMaps.At("A5")], [TestMaps.At("K5")]],
            ChokePoints = [TestMaps.At("F6")],
            CentralEntrance = TestMaps.At("F6"),
            Sites = ImmutableDictionary<Coord, SiteTier>.Empty.Add(TestMaps.At("D2"), tier),
        };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(map).Failures, f => f.Code == "DISTANCE_IMBALANCE");
        Assert.Contains(target, failure.Message, StringComparison.Ordinal);
        Assert.Contains("出生区 0 = 6", failure.Message, StringComparison.Ordinal);
        Assert.Contains("出生区 1 = 9", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 校验不通过则拒绝加载()
    {
        MapData map = FourPlayerBaseMap.Create() with { ChokePoints = [] };

        MapValidationException ex = Assert.Throws<MapValidationException>(() => GameBoard.Load(map));

        Assert.Contains(ex.Result.Failures, f => f.Code == "CHOKE_NOT_ANNOTATED");
    }

    /// <summary>「出生区被孤立」的夹具：9×9，出生区 0 = A1 B1 A2 B2，出生区 1 = J9，入口与咽喉 E5，地形由调用方给。</summary>
    private static MapData Isolation(TerrainData terrain) =>
        TestMaps.Synthetic(size: 9, maxPlayers: 2, terrain: terrain) with
        {
            BirthZones = [[.. new[] { "A1", "B1", "A2", "B2" }.Select(Coord.Parse)], [Coord.Parse("J9")]],
            ChokePoints = [Coord.Parse("E5")],
            CentralEntrance = Coord.Parse("E5"),
        };

    /// <summary>整列格子，用于距离容差的边界构造。</summary>
    private static ImmutableHashSet<Coord> Column(string letter) =>
        [.. Enumerable.Range(1, 8).Select(row => Coord.Parse($"{letter}{row}"))];

    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "siege.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("找不到仓库根目录（siege.sln）。");
    }
}
