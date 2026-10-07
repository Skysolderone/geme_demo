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
        // 规格 Scenario：可落子格低于该人数预算下限的地图 → 拒绝并报告超出区间。
        // retire-legacy-maps 段 C：标准档 4 人 95–110 随标准档删除，改在 2 人合规棋盘档图（BoardMapFixtures.TwoPlayerMap，196 格）上验棋盘档 2 人 125–500：
        // 在公共棋盘里挖 72 格 → 124（下限减 1）报出；挖 71 格 → 125（下限本身）不报。上界：把 28×21 的场景格全部改成可落子 → 588 > 500 报出
        // （只测下界时把上界写成 1000 不会红）。挖洞 / 场景可落子会另报棋盘规则，这里只看可落子格那一条。
        // 变异 MC-S1（段 C 实跑）：2 人上限 500 → 600 → 本测试、人数适配预算Tests.格数超出预算(2)、棋盘档预算与校验Tests.规模预算按人数(2) 共红 3。
        MapData plain = BoardMapFixtures.TwoPlayerMap();
        Assert.Equal(196, plain.PlayableCount);
        Coord[] publicCells = [.. plain.Boards.Single(b => b.Kind == BoardPlateKind.Public).Cells().Order()];
        MapData Dug(int holes) => plain with { Obstacles = plain.Obstacles.Union(publicCells.Take(holes)) };

        MapData sparse = Dug(72);
        Assert.Equal(124, sparse.PlayableCount);
        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(sparse).Failures, f => f.Code == "PLAYABLE_COUNT_OUT_OF_RANGE");
        Assert.Contains("可落子格为 124", failure.Message, StringComparison.Ordinal);
        Assert.Contains("125–500", failure.Message, StringComparison.Ordinal);
        Assert.Throws<MapValidationException>(() => GameBoard.Load(sparse));
        Assert.DoesNotContain(MapValidator.Validate(Dug(71)).Failures, f => f.Code == "PLAYABLE_COUNT_OUT_OF_RANGE");   // 125 是下限本身

        MapData tooOpen = plain with { Obstacles = [] };
        Assert.Equal(588, tooOpen.PlayableCount);
        MapValidationFailure high = Assert.Single(
            MapValidator.Validate(tooOpen).Failures, f => f.Code == "PLAYABLE_COUNT_OUT_OF_RANGE");
        Assert.Contains("可落子格为 588", high.Message, StringComparison.Ordinal);
        Assert.Contains("125–500", high.Message, StringComparison.Ordinal);

        // 反面：原图（196）不报本码。
        Assert.DoesNotContain(MapValidator.Validate(plain).Failures, f => f.Code == "PLAYABLE_COUNT_OUT_OF_RANGE");
    }

    [Fact]
    public void 距离沿气边计算()
    {
        // 规格 Scenario：两个出生区到中央入口的几何路径长度相同，其中一条要跨崖壁 → 跨崖那条不计入，按各自沿气边的最短路算。
        // 9×9 平地，入口 E5；出生区 0 = {A5}、出生区 1 = {J5}，几何距离都是 4。
        // 把 G1–G8 抬到 h=2（G9 留作绕行口）：出生区 1 只能绕 J9 → G9 → E9 → E5，气边最短路 12；出生区 0 仍是 4。
        // retire-legacy-maps 段 C："极差超容差即拒绝"随标准档删除，距离只作报告项；本条改为直接钉距离表（校验器的报告项与 map 子命令共用这一份口径），
        // 合成图不走校验（规格档无关），期望值不变。
        // 变异验证 M-B2：MultiSourceDistances 改回 Adjacency.Neighbors → 两区都算 4，本测试红（守门测试同时红；段 C 改写后重跑，红）。
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

        BirthZoneDistance entrance = Assert.Single(MapValidator.DistanceTable(Wall(2)), m => m.Name == "中央入口");
        Assert.Equal(new int?[] { 4, 12 }, entrance.Distances.AsEnumerable());

        // 同一堵墙降为 h=1 缓坡：两条路都能走，距离相等——证明上面拉开的是崖壁而不是别的
        BirthZoneDistance gentle = Assert.Single(MapValidator.DistanceTable(Wall(1)), m => m.Name == "中央入口");
        Assert.Equal(new int?[] { 4, 4 }, gentle.Distances.AsEnumerable());
    }

    [Fact]
    public void 地形数据越界被报出()
    {
        // 3.2：高度 / 地表 / 桥 / 栅栏端点必须在盘内，越界项不参与任何查询。
        // "桥须在深水、栅栏须相邻"由 TerrainData 构造期抛出（裁决 A-1），校验器不重复。
        // retire-legacy-maps 段 C：底图由 9×9 标准档合成图（标准档已删除，校验到档位即止）换成 36×25 的 4 人合规棋盘档图；
        // 越界坐标随外接范围平移：高度在 (36, 0)，深水 + 桥 + 栅栏在 (0, 25)–(0, 26)。
        // 变异 MC-S3（段 C 实跑）：越界扫描漏掉栅栏端点 → 本测试红 1。
        Coord highOut = new(36, 0);
        Coord waterOut = new(0, 25);
        Coord fenceOut = new(0, 26);
        var terrain = new TerrainData(
            ImmutableDictionary<Coord, int>.Empty.Add(highOut, 1),
            ImmutableDictionary<Coord, Surface>.Empty.Add(waterOut, Surface.DeepWater),
            [waterOut],
            [new FenceEdge(waterOut, fenceOut)]);
        MapData plain = BoardMapFixtures.FourPlayerMap();
        MapData map = plain with { TerrainData = terrain };

        MapValidationFailure failure = Assert.Single(MapValidator.Validate(map).Failures, f => f.Code == "TERRAIN_OUT_OF_BOUNDS");
        Assert.Equal([highOut, waterOut, fenceOut], failure.Coords);

        Assert.DoesNotContain(MapValidator.Validate(plain).Failures, f => f.Code == "TERRAIN_OUT_OF_BOUNDS");
    }

    [Fact]
    public void 出生区可落子格超出区间()
    {
        // 单区可落子格必须落在该人数的区间内。retire-legacy-maps 段 C：标准档 4 人单区 12–14 随标准档删除，改在 4 人合规棋盘档图上验棋盘档单区 25–49：
        // 把出生区 0 换成公共棋盘 A（9×9）坐标序前 n 格——24 与 50 报出，25 与 49（两端）不报。出生区与出生棋盘不再相等会另报，这里只看单区格数那一条。
        // 变异 MC-S2（段 C 实跑）：4 人单区上限 49 → 50 → 本测试红 1。
        MapData plain = BoardMapFixtures.FourPlayerMap();
        Coord[] publicA = [.. BoardMapFixtures.FourPublicA.Cells().Order()];
        MapData Zones(int firstZoneCells) => plain with
        {
            BirthZones = plain.BirthZones.SetItem(0, [.. publicA.Take(firstZoneCells)]),
        };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(Zones(24)).Failures, f => f.Code == "BIRTH_ZONE_SIZE_OUT_OF_RANGE");
        Assert.Contains("出生区 1 有 24 个可落子格", failure.Message, StringComparison.Ordinal);
        Assert.Contains("25–49", failure.Message, StringComparison.Ordinal);

        // 上下界都钉住：25、49 通过本条，50 报出。
        Assert.DoesNotContain(MapValidator.Validate(Zones(25)).Failures, f => f.Code == "BIRTH_ZONE_SIZE_OUT_OF_RANGE");
        Assert.DoesNotContain(MapValidator.Validate(Zones(49)).Failures, f => f.Code == "BIRTH_ZONE_SIZE_OUT_OF_RANGE");
        Assert.Contains(MapValidator.Validate(Zones(50)).Failures, f => f.Code == "BIRTH_ZONE_SIZE_OUT_OF_RANGE");
    }

    [Fact]
    public void 信物格必须标注所属分区()
    {
        // 公共区的信物格被标成出生区档位，或出生区的信物格被标成公共区，都必须报错——
        // 分区标错会让信物生成用错强度预算，而那时已经在对局里了。
        // retire-legacy-maps 段 B：底图由 v5 换成 4 人内置棋盘图（各档共用规则）。
        MapData map = BoardMap();
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
        Assert.DoesNotContain(MapValidator.Validate(map).Failures, f => f.Code == "RELIC_ZONE_MISMATCH");   // 反面：原图不报
    }

    [Fact]
    public void 出生区不得出现高阶预算信物()
    {
        // 设计文档 §8.1：出生区不生成效果 +2 的高阶信物。retire-legacy-maps 段 B：底图由 v5 换成 4 人内置棋盘图。
        MapData map = BoardMap();
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
        // retire-legacy-maps 段 B：底图由 v5 换成 4 人内置棋盘图（"容得下 9 枚"对所有规格档一律生效）。
        MapData map = BoardMap();
        MapData shrunk = map with
        {
            BirthZones = map.BirthZones.SetItem(
                0, [.. map.BirthZones[0].Where(map.IsPlayable).Order().Take(8)]),
        };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(shrunk).Failures, f => f.Code == "BIRTH_ZONE_TOO_SMALL");
        Assert.Contains("出生区 1 只有 8 个可落子格", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 出生区恰好九格时通过()
    {
        // 阈值是"容得下 9 枚"，9 本身合法。本条只管 BIRTH_ZONE_TOO_SMALL 这一码；单区格数区间是另一条独立规则。
        // retire-legacy-maps 段 B：底图由 v5 换成 4 人内置棋盘图。
        MapData map = BoardMap();
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
        // retire-legacy-maps 段 B：底图由 v5 换成 4 人内置棋盘图。
        MapData map = BoardMap();
        Coord shared = map.BirthZones[0].Order().First();
        MapData overlapping = map with { BirthZones = map.BirthZones.SetItem(1, map.BirthZones[1].Add(shared)) };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(overlapping).Failures, f => f.Code == "BIRTH_ZONE_OVERLAP");
        Assert.Equal([shared.ToNotation()], failure.Coords.Notations());
    }

    [Fact]
    public void 中央入口与咽喉必须位于可落子格()
    {
        // retire-legacy-maps 段 B：原在 v5 上同时堵住中央入口与一个咽喉；中央入口改在 4 人内置棋盘图上验。
        // 段 C：咽喉原在边疆档小图上验（边疆档已删除）。棋盘档不要求标注咽喉，但字段仍在、标了就必须可落子：
        // 在内置棋盘图上把坐标序第一个空闲格（公共棋盘里）标为咽喉 → 合法；再把它堵成岩石 → 报 CHOKE_NOT_PLAYABLE 且只指向它。
        // 变异 MC-S4（段 C 实跑）：咽喉可落子判据加恒假条件 → 本测试红 1。
        MapData board = BoardMap();
        MapData blocked = board with { Obstacles = board.Obstacles.Add(board.CentralEntrance) };
        Assert.Contains(MapValidator.Validate(blocked).Failures, f => f.Code == "CENTRAL_ENTRANCE_NOT_PLAYABLE");
        Assert.DoesNotContain(MapValidator.Validate(board).Failures, f => f.Code == "CENTRAL_ENTRANCE_NOT_PLAYABLE");

        Coord choke = TestMaps.FreeCells(board).First();
        MapData annotated = board with { ChokePoints = [choke] };
        Assert.True(MapValidator.Validate(annotated).IsValid, MapValidator.Validate(annotated).ToString());
        MapData broken = annotated with { Obstacles = annotated.Obstacles.Add(choke) };
        MapValidationFailure failure = Assert.Single(MapValidator.Validate(broken).Failures, f => f.Code == "CHOKE_NOT_PLAYABLE");
        Assert.Equal([choke], failure.Coords);
    }

    [Fact]
    public void 尺寸非正与越界障碍被报出()
    {
        // retire-legacy-maps 段 B：底图由 v5 换成 4 人内置棋盘图；越界障碍取外接范围之外的一格（原 (20,20) 在 39×41 的图内）。
        MapData map = BoardMap();
        Coord outside = new(map.Width + 1, map.Height + 1);

        Assert.Contains(
            MapValidator.Validate(map with { Width = 0 }).Failures,
            f => f.Code == "MAP_DIMENSION_INVALID");
        Assert.Contains(
            MapValidator.Validate(map with { Obstacles = map.Obstacles.Add(outside) }).Failures,
            f => f.Code == "OBSTACLE_OUT_OF_BOUNDS" && f.Coords.Contains(outside));
        Assert.Contains(
            MapValidator.Validate(map with { Id = "  " }).Failures,
            f => f.Code == "MAP_ID_MISSING");
        // retire-legacy-maps 段 C：原另有"容差为负报 TOLERANCE_NEGATIVE"一条，距离容差随距离均衡拒绝删除，校验器不再读它。
    }

    [Fact]
    public void 信物格必须位于可落子格()
    {
        // retire-legacy-maps 段 B：底图由 v5（信物格 B2）换成 4 人内置棋盘图，取坐标序第一个信物格。
        MapData map = BoardMap();
        Coord relic = map.RelicCells.Keys.Order().First();
        MapData broken = map with { Obstacles = map.Obstacles.Add(relic) };

        Assert.Contains(
            MapValidator.Validate(broken).Failures,
            f => f.Code == "RELIC_ON_NON_PLAYABLE" && f.Coords.Contains(relic));
        Assert.DoesNotContain(MapValidator.Validate(map).Failures, f => f.Code == "RELIC_ON_NON_PLAYABLE");
    }

    [Theory]
    [InlineData(Surface.Desert)]
    [InlineData(Surface.Marsh)]
    [InlineData(Surface.Crag)]
    [InlineData(Surface.Shallows)]
    public void 出生区内四种新地表都被拒(Surface surface)
    {
        // retire-legacy-maps 段 C 检查补：原 BIRTH_ZONE_SPECIAL_SURFACE（出生区内不得有荒漠 / 沼泽 / 岩台 / 浅滩）随标准 / 边疆档删除，
        // 删除的依据是"棋盘档的出生区恰是出生棋盘、棋盘内每格必须是 h=0 草地"——这里把这条依据钉住：出生区 2 坐标序第一格标成新地表，
        // 必须被拒绝、且拒绝项指向该格（棋盘规则 BOARD_CELL_NOT_FLAT_GRASS 接住了原规则的职责）。
        // 变异 MC-S5（检查方实跑）：ValidatePlateCells 的"非草地"判据改成只看高度 → 本测试红 4。
        MapData map = BoardMap();
        Coord cell = map.BirthZones[1].Order().First();
        MapData tainted = map with
        {
            TerrainData = new TerrainData(map.TerrainData.Heights, map.TerrainData.Surfaces.SetItem(cell, surface), map.TerrainData.Bridges, map.TerrainData.Fences),
        };

        MapValidationResult result = MapValidator.Validate(tainted);

        MapValidationFailure failure = Assert.Single(result.Failures);
        Assert.Equal("BOARD_CELL_NOT_FLAT_GRASS", failure.Code);
        Assert.Equal([cell], failure.Coords);
        Assert.Throws<MapValidationException>(() => GameBoard.Load(tainted));
        Assert.True(MapValidator.Validate(map).IsValid);   // 反面：原图通过
    }

    [Fact]
    public void 校验不通过则拒绝加载()
    {
        // retire-legacy-maps 段 B：底图由 v5 换成边疆档小图（缺咽喉）；段 C 边疆档删除，改在 4 人内置棋盘图上把中央入口堵成岩石。
        MapData map = BoardMap();
        MapData broken = map with { Obstacles = map.Obstacles.Add(map.CentralEntrance) };

        MapValidationException ex = Assert.Throws<MapValidationException>(() => GameBoard.Load(broken));

        Assert.Contains(ex.Result.Failures, f => f.Code == "CENTRAL_ENTRANCE_NOT_PLAYABLE");
        GameBoard.Load(map);   // 反面：合法图照常加载
    }

    /// <summary>4 人内置棋盘图：共用规则的底图（retire-legacy-maps 段 B 起取代 v5）。</summary>
    private static MapData BoardMap() => MapCatalog.Resolve(MapCatalog.DefaultId);
}
