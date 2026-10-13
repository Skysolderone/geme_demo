using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>规格：map-definition —— Requirement: 地图静态校验规则</summary>
/// <remarks>
/// retire-legacy-maps 1.6：各档共用的规则（结构、地形边界、规模预算、出生区、信物格、容差、必死口袋）的用例由 v5 / v2 改到手写的链接档地图
/// （<see cref="LinkMapFixtures.Initial"/>：41×9、五块 5×5 出生棋盘 C3–G7 / L3–P7 / T3–X7 / AC3–AG7 / AL3–AP7，每块信物格在 (1,1)/(3,3)，即 D4、F6 等），
/// 期望值随之换成链接档的预算（可落子 125–245、单区 25–49）。所守规则不变。新增规格 Scenario「出生区数越界」「出生棋盘信物数不对」，
/// 「出生区内有新地表」「信物格必须位于可落子格」随规格改名为「出生棋盘内有新地表」「信物格不在可落子格上」。
/// 只适用于旧三档的规则（距离均衡、到中央入口可达、咽喉必须标注、地标可达）的用例：载体是 v5 的随 v5 在第 2 组删除，载体是合成图的随规则在第 4 组删除。
/// </remarks>
public class 地图静态校验规则Tests
{
    /// <summary>手写链接档初始图（合规）。</summary>
    private static MapData Link() => LinkMapFixtures.Initial();

    /// <summary>出生棋盘 1（下标 0）右下角的两格 G3 / G4 用 F3 / F4 / G5 三块障碍围死：面积 2 的口袋（棋盘外沿本就是障碍）。</summary>
    private static readonly Coord[] PocketWalls = [Coord.Parse("F3"), Coord.Parse("F4"), Coord.Parse("G5")];

    [Fact]
    public void 合规的手写链接档地图通过()
    {
        // 下面各条都在这张图上只改一处——它本身 MUST 通过，否则"报出某条"可能只是恰好碰上别的失败。
        MapValidationResult result = MapValidator.Validate(Link());
        Assert.True(result.IsValid, result.ToString());
    }

    [Fact]
    public void 可落子格越界()
    {
        // 链接档 4 人可落子格区间 125–245（retire-legacy-maps：原为标准档 95–110，算例取 v2 的 85 格与 v3 去岩石的 141 格）。
        // 下界：出生棋盘 1 挖掉一格 → 124；上界：画布 41×9 的场景格全部开放 → 369。
        MapData map = Link();
        MapData tooFew = map with { Obstacles = map.Obstacles.Add(Coord.Parse("E5")) };
        Assert.Equal(124, tooFew.PlayableCount);
        MapValidationFailure low = Assert.Single(
            MapValidator.Validate(tooFew).Failures, f => f.Code == "PLAYABLE_COUNT_OUT_OF_RANGE");
        Assert.Contains("可落子格为 124", low.Message, StringComparison.Ordinal);
        Assert.Contains("125–245", low.Message, StringComparison.Ordinal);
        Assert.Throws<MapValidationException>(() => GameBoard.Load(tooFew));

        // 上界 245 同样要挡住：区间只有下界有算例时，把上界写成 2000 只会让消息文本变化，行为上一条测试都不红。
        MapData tooOpen = map with { Obstacles = [] };
        Assert.Equal(369, tooOpen.PlayableCount);
        MapValidationFailure high = Assert.Single(
            MapValidator.Validate(tooOpen).Failures, f => f.Code == "PLAYABLE_COUNT_OUT_OF_RANGE");
        Assert.Contains("可落子格为 369", high.Message, StringComparison.Ordinal);
        Assert.Contains("125–245", high.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 外接尺寸越界被拒绝而不是抛异常()
    {
        // retire-legacy-maps（check 补回覆盖）：自已删除的 边疆档静态校验Tests.超宽地图被拒绝而不是抛异常 迁来（原载体为边疆档的 25 列上限）。
        // 所守规则不变：外接尺寸不合该档声明的列 / 行区间 → 报拒绝项并到此为止，不往下跑规模与棋盘规则、不在枚举格子时崩溃。
        // 链接档区间取自 MapValidator 声明表：列 20–75、行 8–21；载体是合规的 41×9 手写链接档图，只改外接尺寸一处。
        MapData map = Link();
        (MapData Map, string Code, string Value, string Bound)[] cases =
        [
            (map with { Width = 76 }, "MAP_TOO_WIDE", "76", "75"),
            (map with { Width = 1000 }, "MAP_TOO_WIDE", "1000", "75"),
            (map with { Width = 19 }, "MAP_TOO_NARROW", "19", "20"),
            (map with { Height = 22 }, "MAP_TOO_TALL", "22", "21"),
            (map with { Height = 7 }, "MAP_TOO_SHORT", "7", "8"),
        ];
        foreach ((MapData bad, string code, string value, string bound) in cases)
        {
            MapValidationResult result = MapValidator.Validate(bad);
            MapValidationFailure failure = Assert.Single(result.Failures, f => f.Code == code);
            Assert.Contains(value, failure.Message, StringComparison.Ordinal);
            Assert.Contains(bound, failure.Message, StringComparison.Ordinal);
            // 到此为止：规模、出生区、棋盘清单一条都不再报（只剩结构性前置校验可能报越界障碍）。
            Assert.All(result.Failures, f => Assert.True(f.Code == code || f.Code == "OBSTACLE_OUT_OF_BOUNDS", f.ToString()));
            Assert.Throws<MapValidationException>(() => GameBoard.Load(bad));
        }

        // 区间端点本身合法（只看本条规则：改了尺寸的图别的规则照样会报，这里不管）。
        Assert.DoesNotContain(MapValidator.Validate(map with { Width = 75 }).Failures, f => f.Code == "MAP_TOO_WIDE");
        Assert.DoesNotContain(MapValidator.Validate(map with { Width = 20 }).Failures, f => f.Code == "MAP_TOO_NARROW");
        Assert.DoesNotContain(MapValidator.Validate(map with { Height = 21 }).Failures, f => f.Code == "MAP_TOO_TALL");
        Assert.DoesNotContain(MapValidator.Validate(map with { Height = 8 }).Failures, f => f.Code == "MAP_TOO_SHORT");
    }

    [Fact]
    public void 出生区数越界()
    {
        // 规格 Scenario：手写的链接档地图含 6 块各自合规的出生棋盘 → 拒绝并报告出生区数超出"恰 5 个"。
        MapData six = LinkMapFixtures.Build("test-link-6", [.. LinkMapFixtures.Births, new BoardPlate(new Coord(42, 2), 5, 5, BoardPlateKind.Birth)]);
        Assert.Equal(6, six.BirthZones.Length);

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(six).Failures, f => f.Code == "BIRTH_ZONE_COUNT_OUT_OF_RANGE");
        Assert.Contains("出生区为 6 个", failure.Message, StringComparison.Ordinal);
        Assert.Contains("5–5", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 信物格数越界()
    {
        // 链接档信物格恰 10（规则第 3 条）。retire-legacy-maps 1.6：自 人数适配预算Tests.信物格数不足（v5，13–15，该 Requirement 随旧地图移除）迁来，
        // 期望值换成链接档的区间；所守的"信物格数越界即拒绝并报方向"不变。拿掉一枚 → 9 → 少于下限 10。
        MapData map = Link();
        MapData stripped = map with { RelicCells = map.RelicCells.Remove(map.RelicCells.Keys.Order().First()) };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(stripped).Failures, f => f.Code == "RELIC_COUNT_OUT_OF_RANGE");
        Assert.Contains("10–10", failure.Message, StringComparison.Ordinal);
        Assert.Contains("少于下限 10", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 不支持的人数被拒绝()
    {
        // 链接档只提供 4 人的预算表。retire-legacy-maps 1.6：自 人数适配预算Tests 同名用例（v5 改标 5 人）迁来，载体换成手写链接档地图，断言不变。
        Assert.Contains(MapValidator.Validate(Link() with { MaxPlayers = 5 }).Failures, f => f.Code == "UNSUPPORTED_PLAYER_COUNT");
        Assert.Contains(MapValidator.Validate(Link() with { MaxPlayers = 3 }).Failures, f => f.Code == "UNSUPPORTED_PLAYER_COUNT");
    }

    [Fact]
    public void 出生棋盘信物数不对()
    {
        // 规格 Scenario：出生棋盘 3 上有 3 个信物格 → 拒绝并指出该棋盘与"每块出生棋盘恰 2 个信物格"。
        MapData map = Link();
        BoardPlate third = map.Boards[2];
        Coord extra = new(third.Origin.X + 2, third.Origin.Y + 2);
        MapData three = map with { RelicCells = map.RelicCells.Add(extra, new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth)) };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(three).Failures, f => f.Code == "BIRTH_BOARD_RELIC_COUNT");
        Assert.Equal([third.Origin], failure.Coords);
        Assert.Contains("有 3 个信物格", failure.Message, StringComparison.Ordinal);
        Assert.Contains("恰有 2 个", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 地形数据越界被报出()
    {
        // 3.2：高度 / 地表 / 桥 / 栅栏端点必须在盘内，越界项不参与任何查询。
        // "桥须在深水、栅栏须相邻"由 TerrainData 构造期抛出（裁决 A-1），校验器不重复。
        // retire-legacy-maps 1.6：载体由 9×9 合成图改为 41×9 手写链接档地图（越界格改为第 10 / 11 行），断言不变。
        TerrainData terrain = TestMaps.Terrain(
            heights: [("C10", 1)],
            surfaces: [("D10", Surface.DeepWater)],
            bridges: ["D10"],
            fences: [("D10", "D11")]);
        MapData map = Link() with { TerrainData = terrain };

        MapValidationFailure failure = Assert.Single(MapValidator.Validate(map).Failures, f => f.Code == "TERRAIN_OUT_OF_BOUNDS");
        Assert.Equal(["C10", "D10", "D11"], failure.Coords.Notations());

        Assert.DoesNotContain(MapValidator.Validate(Link()).Failures, f => f.Code == "TERRAIN_OUT_OF_BOUNDS");
    }

    [Fact]
    public void 出生区可落子格超出区间()
    {
        // 链接档单区 25–49（retire-legacy-maps 1.6：原为标准档 12–14）。出生棋盘 1 挖掉 2 格 → 23 格 → 报错。
        MapData map = Link();
        Coord[] extra = [.. map.BirthZones[0].Order().Take(2)];
        MapData shrunk = map with { Obstacles = map.Obstacles.Union(extra) };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(shrunk).Failures, f => f.Code == "BIRTH_ZONE_SIZE_OUT_OF_RANGE");
        Assert.Contains("出生区 1 有 23 个可落子格", failure.Message, StringComparison.Ordinal);
        Assert.Contains("25–49", failure.Message, StringComparison.Ordinal);

        // 49 格是上界：7×7 的出生棋盘必须通过本条；再给它的出生区添一格（开放一格紧贴的场景格）→ 50 → 报错。
        ImmutableArray<BoardPlate> plates = [new(new Coord(2, 2), 7, 7, BoardPlateKind.Birth), .. LinkMapFixtures.Births.Skip(1).Select(p => p with { Origin = new Coord(p.Origin.X + 2, p.Origin.Y) })];
        MapData fortyNine = LinkMapFixtures.Build("test-link-49", plates);
        Assert.Equal(49, fortyNine.BirthZones[0].Count);
        Assert.DoesNotContain(MapValidator.Validate(fortyNine).Failures, f => f.Code == "BIRTH_ZONE_SIZE_OUT_OF_RANGE");

        Coord outside = new(9, 2);
        MapData fifty = fortyNine with
        {
            Obstacles = fortyNine.Obstacles.Remove(outside),
            BirthZones = fortyNine.BirthZones.SetItem(0, fortyNine.BirthZones[0].Add(outside)),
        };
        MapValidationFailure over = Assert.Single(
            MapValidator.Validate(fifty).Failures, f => f.Code == "BIRTH_ZONE_SIZE_OUT_OF_RANGE");
        Assert.Contains("出生区 1 有 50 个可落子格", over.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 信物格必须标注所属分区()
    {
        // 公共区的信物格被标成出生区档位，或出生区的信物格被标成公共区，都必须报错——
        // 分区标错会让信物生成用错强度预算，而那时已经在对局里了。
        // retire-legacy-maps 1.6：链接档初始图没有公共区信物格；改为开放一格场景格（J5）放一枚标成出生区档位的信物，断言不变。
        MapData map = Link();
        Coord inBirthZone = map.RelicCells.Keys.Order().First();
        Coord contested = Coord.Parse("J5");

        MapData zoneSwapped = map with
        {
            Obstacles = map.Obstacles.Remove(contested),
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
        MapData map = Link();
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
        MapData map = Link();
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
        // 阈值是"容得下 9 枚"，9 本身合法。本条只管 BIRTH_ZONE_TOO_SMALL 这一码；
        // "每区 25–49 格"是另一条独立规则（BIRTH_ZONE_SIZE_OUT_OF_RANGE），由「出生区可落子格超出区间」把关。
        MapData map = Link();
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
        MapData map = Link();
        Coord shared = map.BirthZones[0].Order().First();
        MapData overlapping = map with { BirthZones = map.BirthZones.SetItem(1, map.BirthZones[1].Add(shared)) };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(overlapping).Failures, f => f.Code == "BIRTH_ZONE_OVERLAP");
        Assert.Equal([shared.ToNotation()], failure.Coords.Notations());
    }

    [Fact]
    public void 中央入口与咽喉必须位于可落子格()
    {
        // retire-legacy-maps 1.6：链接档地图不要求标注咽喉，这里显式标一个咽喉（出生棋盘 2 的 M5）再把它与中央入口都改成障碍，断言不变。
        MapData map = Link() with { ChokePoints = [Coord.Parse("M5")] };
        MapData broken = map with
        {
            Obstacles = map.Obstacles.Add(map.CentralEntrance).Add(Coord.Parse("M5")),
        };

        var codes = MapValidator.Validate(broken).Failures.Select(f => f.Code).ToArray();

        Assert.Contains("CENTRAL_ENTRANCE_NOT_PLAYABLE", codes);
        Assert.Contains("CHOKE_NOT_PLAYABLE", codes);
    }

    [Fact]
    public void 尺寸非正与越界障碍被报出()
    {
        MapData map = Link();

        Assert.Contains(
            MapValidator.Validate(map with { Width = 0 }).Failures,
            f => f.Code == "MAP_DIMENSION_INVALID");
        Assert.Contains(
            MapValidator.Validate(map with { Obstacles = map.Obstacles.Add(new Coord(50, 20)) }).Failures,
            f => f.Code == "OBSTACLE_OUT_OF_BOUNDS" && f.Coords.Contains(new Coord(50, 20)));
        Assert.Contains(
            MapValidator.Validate(map with { Id = "  " }).Failures,
            f => f.Code == "MAP_ID_MISSING");
        Assert.Contains(
            MapValidator.Validate(map with { DistanceTolerance = -1 }).Failures,
            f => f.Code == "TOLERANCE_NEGATIVE");
    }

    [Fact]
    public void 必死口袋()
    {
        // retire-legacy-maps 1.6：原在 v5 出生区 0 的高台里用三块岩石封出 7 格死角；改为在出生棋盘 1 右下角用 F3 / F4 / G5 三块障碍封出 2 格死角
        // （G3 / G4，棋盘外沿本就是障碍）。剩下的 22 格仍连成一片，不会多出第二个口袋。
        MapData map = Link();
        MapData pocketed = map with { Obstacles = map.Obstacles.Union(PocketWalls) };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(pocketed).Failures, f => f.Code == "DEAD_POCKET");
        Assert.Contains("小于形成两眼所需的 8 格", failure.Message, StringComparison.Ordinal);
        Assert.Equal(["G3", "G4"], failure.Coords.Notations());
    }

    [Fact]
    public void 面积恰好等于两眼最小格数的空区不算口袋()
    {
        // 规格："连通空区面积小于 8 即判失败"——8 本身是合法的，阈值是闭的下界。
        // retire-legacy-maps 1.6：用障碍 E3–E6 与 F7 / G7 把出生棋盘 1 的 F3–G6 圈出恰好 8 格（含信物格 F6，信物格可落子）。
        MapData map = Link();
        Coord[] walls = [.. new[] { "E3", "E4", "E5", "E6", "F7", "G7" }.Select(Coord.Parse)];
        MapData eightCellPocket = map with { Obstacles = map.Obstacles.Union(walls) };

        Assert.DoesNotContain(
            MapValidator.Validate(eightCellPocket).Failures, f => f.Code == "DEAD_POCKET");

        // 同一块区域少一格（7 格）就必须失败，证明上面那次通过不是因为规则没跑。
        MapData sevenCellPocket = eightCellPocket with
        {
            Obstacles = eightCellPocket.Obstacles.Add(Coord.Parse("F3")),
        };
        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(sevenCellPocket).Failures, f => f.Code == "DEAD_POCKET");
        Assert.Equal(["G3", "F4", "G4", "F5", "G5", "F6", "G6"], failure.Coords.Notations());
    }

    [Fact]
    public void 出生区之外的小空区不算必死口袋()
    {
        // 规格只禁止"出生区内"的必死口袋——出生区之外的封闭小空区是装饰自由，误报会把合法地图挡在门外。
        // retire-legacy-maps 1.6：原用一张 10×10 合成图；改为在手写链接档地图的场景里开放 J4 / J5 两格（四周仍是障碍）。
        MapData map = Link();
        Coord[] hole = [Coord.Parse("J4"), Coord.Parse("J5")];
        MapData enclosed = map with { Obstacles = map.Obstacles.Except(hole) };

        Assert.DoesNotContain(MapValidator.Validate(enclosed).Failures, f => f.Code == "DEAD_POCKET");

        // 同一块空区一旦被划进出生区，就必须报出来
        MapData inBirthZone = enclosed with { BirthZones = enclosed.BirthZones.SetItem(0, enclosed.BirthZones[0].Union(hole)) };
        Assert.Contains(MapValidator.Validate(inBirthZone).Failures, f => f.Code == "DEAD_POCKET");
    }

    [Fact]
    public void 必死口袋可显式豁免()
    {
        MapData map = Link();
        MapData exempted = map with
        {
            Obstacles = map.Obstacles.Union(PocketWalls),
            PocketExemptions = [Coord.Parse("G3")],
            PocketExemptionReasons = ImmutableDictionary<Coord, string>.Empty
                .Add(Coord.Parse("G3"), "测试用：刻意保留的装饰性死角"),
        };

        Assert.DoesNotContain(MapValidator.Validate(exempted).Failures, f => f.Code == "DEAD_POCKET");
    }

    [Fact]
    public void 豁免必须写明理由()
    {
        MapData map = Link() with { PocketExemptions = [Coord.Parse("G3")] };

        Assert.Contains(
            MapValidator.Validate(map).Failures, f => f.Code == "POCKET_EXEMPTION_WITHOUT_REASON");
    }

    [Fact]
    public void 信物格不在可落子格上()
    {
        // 规格 Scenario：某个信物格落在棋盘之外的场景格上 → 拒绝并指出该信物格坐标。
        // retire-legacy-maps 1.6：原名「信物格必须位于可落子格」（v5 的 B2 改成岩石）；随规格改名，改在手写链接档地图的场景格 J5 上放一枚信物。
        MapData map = Link();
        Coord scenery = Coord.Parse("J5");
        Assert.False(map.IsPlayable(scenery));
        MapData broken = map with { RelicCells = map.RelicCells.Add(scenery, new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard)) };

        Assert.Contains(
            MapValidator.Validate(broken).Failures,
            f => f.Code == "RELIC_ON_NON_PLAYABLE" && f.Coords.Contains(scenery));
    }

    [Fact]
    public void 放宽容差必须写明理由()
    {
        MapData map = Link();

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
        // retire-legacy-maps 1.6：原用 v5 清空咽喉标注（CHOKE_NOT_ANNOTATED，只属于旧三档）；改用手写链接档地图上的必死口袋（各档共用），断言不变。
        MapData map = Link() with { Obstacles = Link().Obstacles.Union(PocketWalls) };

        MapValidationException ex = Assert.Throws<MapValidationException>(() => GameBoard.Load(map));

        Assert.Contains(ex.Result.Failures, f => f.Code == "DEAD_POCKET");
    }

    [Fact]
    public void 出生棋盘内有新地表()
    {
        // 规格 Scenario：出生棋盘 2 内的 C3 标为浅滩 → 拒绝并指出该棋盘与坐标 C3（棋盘内每一格须为高度 0 的草地）。
        // 规格 terrain-surfaces · map-definition 第 6 条：出生区内的格子 MUST NOT 是荒漠、沼泽、岩台或浅滩；报出生区编号、坐标与地表。
        // retire-legacy-maps 1.6：原名「出生区内有新地表」（v5 出生区 2 的第一格）；随规格改名，棋盘清单把 C3–G7 那块排在第 2 位。
        // 变异验证 M-S0c（实跑）：ValidateBirthZones 漏掉 Shallows → 本测试与「出生区内四种新地表都被拒(Shallows)」各红 1。
        ImmutableArray<BoardPlate> b = LinkMapFixtures.Births;
        MapData map = LinkMapFixtures.Build("test-link-5", [b[1], b[0], b[2], b[3], b[4]]);
        Coord cell = Coord.Parse("C3");
        Assert.Contains(cell, map.BirthZones[1]);
        MapData tainted = map with
        {
            TerrainData = WithSurfaces(map.TerrainData, map.TerrainData.Surfaces.SetItem(cell, Surface.Shallows)),
        };

        MapValidationResult result = MapValidator.Validate(tainted);
        MapValidationFailure failure = Assert.Single(result.Failures, f => f.Code == "BIRTH_ZONE_SPECIAL_SURFACE");
        Assert.Contains("出生区 2", failure.Message, StringComparison.Ordinal);
        Assert.Contains(cell.ToNotation(), failure.Message, StringComparison.Ordinal);
        Assert.Contains("浅滩", failure.Message, StringComparison.Ordinal);
        Assert.Equal([cell], failure.Coords);

        // 链接档的棋盘清单规则同时指出这块棋盘与坐标 C3。
        MapValidationFailure board = Assert.Single(result.Failures, f => f.Code == "BOARD_CELL_NOT_FLAT_GRASS");
        Assert.Equal([cell], board.Coords);
        Assert.Contains("出生棋盘 C3–G7", board.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Surface.Desert)]
    [InlineData(Surface.Marsh)]
    [InlineData(Surface.Crag)]
    [InlineData(Surface.Shallows)]
    public void 出生区内四种新地表都被拒(Surface surface)
    {
        MapData map = Link();
        Coord cell = map.BirthZones[0].Where(map.IsPlayable).Order().First();
        MapData tainted = map with
        {
            TerrainData = WithSurfaces(map.TerrainData, map.TerrainData.Surfaces.SetItem(cell, surface)),
        };

        Assert.Contains(MapValidator.Validate(tainted).Failures, f => f.Code == "BIRTH_ZONE_SPECIAL_SURFACE");
    }

    [Fact]
    public void 公共区域的新地表不受限()
    {
        // 规格 Scenario「公共区域的新地表不受限」：出生区之外标四种新地表，其余校验通过 → 接受。
        // 反面对照：原图本身必须通过（否则"接受"只是恰好没报这条）。
        // retire-legacy-maps 1.6：链接档初始图的出生区之外全是场景格；四种新地表改标在场景格上（原为 v5 公共区的可落子格），断言不变。
        MapData map = Link();
        Assert.True(MapValidator.Validate(map).IsValid);
        Coord[] outside = [.. new[] { "J3", "J4", "J5", "J6" }.Select(Coord.Parse)];
        Assert.All(outside, c => Assert.Null(map.BirthZoneOf(c)));
        Surface[] kinds = [Surface.Desert, Surface.Marsh, Surface.Crag, Surface.Shallows];
        ImmutableDictionary<Coord, Surface> surfaces = map.TerrainData.Surfaces;
        for (int i = 0; i < 4; i++)
        {
            surfaces = surfaces.SetItem(outside[i], kinds[i]);
        }

        MapData painted = map with { TerrainData = WithSurfaces(map.TerrainData, surfaces) };

        Assert.Equal(4, outside.Length);
        MapValidationResult result = MapValidator.Validate(painted);
        Assert.True(result.IsValid, string.Join("；", result.Failures.Select(f => f.Message)));
    }

    private static TerrainData WithSurfaces(TerrainData terrain, ImmutableDictionary<Coord, Surface> surfaces) =>
        new(terrain.Heights, surfaces, terrain.Bridges, terrain.Fences);
}
