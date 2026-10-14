using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>规格：map-definition —— Requirement: 地图静态校验规则（link-map-terrain 增量：链接档清单规则改写）</summary>
/// <remarks>
/// retire-legacy-maps 1.6：各档共用的规则（结构、地形边界、规模预算、出生区、信物格、容差、必死口袋）的用例由 v5 / v2 改到手写的链接档地图。
/// link-map-terrain 第 2 组：载体 <see cref="LinkMapFixtures.Initial"/> 改为 46×10、五块 6×6 出生棋盘 C3–H8 / M3–R8 / V3–AA8 / AE3–AK8 / AO3–AT8，
/// 出生区 = 核心区（棋盘左下角起 (1,1)–(3,3)，出生棋盘 1 即 D4–F6），信物格在核心区对角（D4、F6 等），中间带两格林地（出生棋盘 1 的 G4、G7）；
/// 期望值随之换成新预算（可落子 170–320、单区恰 9、信物 10–15）。新增规格 Scenario「外圈有高差或栅栏」「核心区有地形」「棋盘被切断」「可改造目标不足」「主题重复」，
/// 「出生棋盘内有新地表」改为中间带浅滩（新失败码 BOARD_SPECIAL_SURFACE，待主会话裁决码名）。
/// 变异验证（link-map-terrain 第 2 组实跑，见任务汇报）：每条新失败码各删去 / 改恒假一次，对应用例红。
/// </remarks>
public class 地图静态校验规则Tests
{
    /// <summary>手写链接档初始图（合规）。</summary>
    private static MapData Link() => LinkMapFixtures.Initial();

    /// <summary>出生棋盘 1（下标 0）核心区左下角 D4 用 C4 / D3（外圈）与 E4 / D5（核心区）四块障碍围死：面积 1 的口袋。</summary>
    private static readonly Coord[] PocketWalls = [Coord.Parse("C4"), Coord.Parse("D3"), Coord.Parse("E4"), Coord.Parse("D5")];

    [Fact]
    public void 合规的手写链接档地图通过()
    {
        // 下面各条都在这张图上只改一处——它本身 MUST 通过，否则"报出某条"可能只是恰好碰上别的失败。
        MapValidationResult result = MapValidator.Validate(Link());
        Assert.True(result.IsValid, result.ToString());
        Assert.Equal(180, Link().PlayableCount);
    }

    [Fact]
    public void 可落子格越界()
    {
        // 链接档 4 人可落子格区间 170–320（link-map-terrain 3.3 回写；原 125–245）。
        // 下界：出生棋盘 1–4 的中间带各放 3 块岩石（G5 / G6 / E7 一类，不切断棋盘）→ 180 − 12 = 168；上界：画布 46×10 的场景格全部开放 → 460。
        MapData map = Link();
        Coord[] rocks = [.. map.Boards.Take(4).SelectMany(b => new[] { new Coord(b.Origin.X + 4, b.Origin.Y + 2), new Coord(b.Origin.X + 4, b.Origin.Y + 3), new Coord(b.Origin.X + 2, b.Origin.Y + 4) })];
        MapData tooFew = map with { Obstacles = map.Obstacles.Union(rocks) };
        Assert.Equal(168, tooFew.PlayableCount);
        MapValidationFailure low = Assert.Single(
            MapValidator.Validate(tooFew).Failures, f => f.Code == "PLAYABLE_COUNT_OUT_OF_RANGE");
        Assert.Contains("可落子格为 168", low.Message, StringComparison.Ordinal);
        Assert.Contains("170–320", low.Message, StringComparison.Ordinal);
        Assert.Throws<MapValidationException>(() => GameBoard.Load(tooFew));

        // 上界 320 同样要挡住：区间只有下界有算例时，把上界写成 2000 只会让消息文本变化，行为上一条测试都不红。
        MapData tooOpen = map with { Obstacles = [] };
        Assert.Equal(460, tooOpen.PlayableCount);
        MapValidationFailure high = Assert.Single(
            MapValidator.Validate(tooOpen).Failures, f => f.Code == "PLAYABLE_COUNT_OUT_OF_RANGE");
        Assert.Contains("可落子格为 460", high.Message, StringComparison.Ordinal);
        Assert.Contains("170–320", high.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 外接尺寸越界被拒绝而不是抛异常()
    {
        // retire-legacy-maps（check 补回覆盖）：自已删除的 边疆档静态校验Tests.超宽地图被拒绝而不是抛异常 迁来（原载体为边疆档的 25 列上限）。
        // 所守规则不变：外接尺寸不合该档声明的列 / 行区间 → 报拒绝项并到此为止，不往下跑规模与棋盘规则、不在枚举格子时崩溃。
        // 链接档区间取自 MapValidator 声明表：列 20–85、行 8–24（link-map-terrain 3.3 回写；原列 20–75、行 8–21）；载体是合规的 46×10 手写链接档图，只改外接尺寸一处。
        MapData map = Link();
        (MapData Map, string Code, string Value, string Bound)[] cases =
        [
            (map with { Width = 86 }, "MAP_TOO_WIDE", "86", "85"),
            (map with { Width = 1000 }, "MAP_TOO_WIDE", "1000", "85"),
            (map with { Width = 19 }, "MAP_TOO_NARROW", "19", "20"),
            (map with { Height = 25 }, "MAP_TOO_TALL", "25", "24"),
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
        Assert.DoesNotContain(MapValidator.Validate(map with { Width = 85 }).Failures, f => f.Code == "MAP_TOO_WIDE");
        Assert.DoesNotContain(MapValidator.Validate(map with { Width = 20 }).Failures, f => f.Code == "MAP_TOO_NARROW");
        Assert.DoesNotContain(MapValidator.Validate(map with { Height = 24 }).Failures, f => f.Code == "MAP_TOO_TALL");
        Assert.DoesNotContain(MapValidator.Validate(map with { Height = 8 }).Failures, f => f.Code == "MAP_TOO_SHORT");
    }

    [Fact]
    public void 出生区数越界()
    {
        // 规格 Scenario：手写的链接档地图含 6 块各自合规的出生棋盘 → 拒绝并报告出生区数超出"恰 5 个"。
        MapData six = LinkMapFixtures.Build("test-link-6", [.. LinkMapFixtures.Births, LinkMapFixtures.Births[0] with { Origin = new Coord(47, 2) }]);
        Assert.Equal(6, six.BirthZones.Length);

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(six).Failures, f => f.Code == "BIRTH_ZONE_COUNT_OUT_OF_RANGE");
        Assert.Contains("出生区为 6 个", failure.Message, StringComparison.Ordinal);
        Assert.Contains("5–5", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 信物格数越界()
    {
        // 链接档信物格 10–15（link-map-terrain D5：每块出生棋盘 2 个出生区档 + 至多 1 个公共区档；原恰 10）。
        // 拿掉一枚 → 9 → 少于下限 10。
        MapData map = Link();
        MapData stripped = map with { RelicCells = map.RelicCells.Remove(map.RelicCells.Keys.Order().First()) };
        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(stripped).Failures, f => f.Code == "RELIC_COUNT_OUT_OF_RANGE");
        Assert.Contains("10–15", failure.Message, StringComparison.Ordinal);
        Assert.Contains("少于下限 10", failure.Message, StringComparison.Ordinal);

        // 上界：每块中间带放 1 个公共区档信物 → 15 → 合规；第 1 块再放 1 个 → 16 → 多于上限 15（同时该棋盘的公共区档超过 1 个）。
        var contested = new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard);
        MapData fifteen = map with { RelicCells = map.RelicCells.AddRange(map.Boards.Select(b => KeyValuePair.Create(new Coord(b.Origin.X + 4, b.Origin.Y + 2), contested))) };
        Assert.Equal(15, fifteen.RelicCells.Count);
        Assert.True(MapValidator.Validate(fifteen).IsValid, MapValidator.Validate(fifteen).ToString());
        MapData sixteen = fifteen with { RelicCells = fifteen.RelicCells.Add(new Coord(map.Boards[0].Origin.X + 4, map.Boards[0].Origin.Y + 3), contested) };
        MapValidationResult over = MapValidator.Validate(sixteen);
        Assert.Contains("多于上限 15", Assert.Single(over.Failures, f => f.Code == "RELIC_COUNT_OUT_OF_RANGE").Message, StringComparison.Ordinal);
        Assert.Equal([map.Boards[0].Origin], Assert.Single(over.Failures, f => f.Code == "BIRTH_BOARD_RELIC_COUNT").Coords);
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
        // 规格 Scenario：出生棋盘 3 的核心区里有 3 个出生区档信物格 → 拒绝并指出该棋盘与"每块出生棋盘恰 2 个出生区档信物格"。
        MapData map = Link();
        BoardPlate third = map.Boards[2];
        Coord extra = new(third.CoreOrigin.X + 1, third.CoreOrigin.Y + 1);   // 核心区中心
        Assert.Contains(extra, map.BirthZones[2]);
        MapData three = map with { RelicCells = map.RelicCells.Add(extra, new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth)) };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(three).Failures, f => f.Code == "BIRTH_BOARD_RELIC_COUNT");
        Assert.Equal([third.Origin], failure.Coords);
        Assert.Contains("有 3 个出生区档", failure.Message, StringComparison.Ordinal);
        Assert.Contains("恰有 2 个出生区档", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 外圈有高差或栅栏()
    {
        // 规格 Scenario：出生棋盘 1 的外圈有一格高度为 1，或外圈两格之间有栅栏 → 拒绝并指出该棋盘与对应的格或边（BOARD_RIM_NOT_FLAT）。
        MapData map = Link();
        MapValidationFailure height = Assert.Single(
            MapValidator.Validate(map with { TerrainData = Overlay(map, heights: [("C5", 1)]) }).Failures, f => f.Code == "BOARD_RIM_NOT_FLAT");
        Assert.Equal([TestMaps.At("C5")], height.Coords);
        Assert.Contains("出生棋盘 C3–H8", height.Message, StringComparison.Ordinal);

        MapValidationFailure fence = Assert.Single(
            MapValidator.Validate(map with { TerrainData = Overlay(map, fences: [("C5", "C6")]) }).Failures, f => f.Code == "BOARD_RIM_NOT_FLAT");
        Assert.Equal(["C5", "C6"], fence.Coords.Notations());
        Assert.Contains("C5-C6", fence.Message, StringComparison.Ordinal);

        // 外圈与棋盘外场景格之间的栅栏、外圈与核心区之间的栅栏都算"外圈相关的边"；外圈的深水（可落子的桥格也一样）不是草地或林地。
        Assert.Contains(MapValidator.Validate(map with { TerrainData = Overlay(map, fences: [("B5", "C5")]) }).Failures, f => f.Code == "BOARD_RIM_NOT_FLAT");
        Assert.Contains(MapValidator.Validate(map with { TerrainData = Overlay(map, fences: [("C5", "D5")]) }).Failures, f => f.Code == "BOARD_RIM_NOT_FLAT");
        Assert.Contains(MapValidator.Validate(map with { TerrainData = Overlay(map, surfaces: [("C5", Surface.DeepWater)], bridges: ["C5"]) }).Failures, f => f.Code == "BOARD_RIM_NOT_FLAT");

        // 反面：外圈可以有林地（裁决 Q1）。
        Assert.True(MapValidator.Validate(map with { TerrainData = Overlay(map, surfaces: [("C5", Surface.Forest)]) }).IsValid);
    }

    [Fact]
    public void 核心区有地形()
    {
        // 规格 Scenario：出生棋盘 4 的核心区有一格是林地 → 拒绝并指出该棋盘与该格坐标（BIRTH_CORE_NOT_FLAT）。
        MapData map = Link();
        BoardPlate fourth = map.Boards[3];
        Coord cell = new(fourth.CoreOrigin.X + 1, fourth.CoreOrigin.Y + 2);
        MapValidationFailure forest = Assert.Single(
            MapValidator.Validate(map with { TerrainData = Overlay(map, surfaces: [(cell.ToNotation(), Surface.Forest)]) }).Failures, f => f.Code == "BIRTH_CORE_NOT_FLAT");
        Assert.Equal([cell], forest.Coords);
        Assert.Contains($"出生棋盘 {fourth.Origin.ToNotation()}–", forest.Message, StringComparison.Ordinal);

        // 高度、深水、岩石同样被拒；核心区与中间带之间的栅栏可以有（裁决 Q4，见 物化状态校验Tests.棋盘清单规则逐条报出 ⑤）。
        Assert.Contains(MapValidator.Validate(map with { TerrainData = Overlay(map, heights: [(cell.ToNotation(), 1)]) }).Failures, f => f.Code == "BIRTH_CORE_NOT_FLAT");
        Assert.Contains(MapValidator.Validate(map with { TerrainData = Overlay(map, surfaces: [(cell.ToNotation(), Surface.DeepWater)]) }).Failures, f => f.Code == "BIRTH_CORE_NOT_FLAT");
        Assert.Contains(MapValidator.Validate(map with { Obstacles = map.Obstacles.Add(cell) }).Failures, f => f.Code == "BIRTH_CORE_NOT_FLAT");
    }

    [Fact]
    public void 棋盘被切断()
    {
        // 规格 Scenario：出生棋盘 5 的一组中间带格被岩石与深水围住、与外圈不连通 → 拒绝并指出该棋盘与被隔开的格（BOARD_SPLIT）。
        // 6×6 的中间带每格都挨着外圈，围不出孤岛；这里把出生棋盘 5 换成 8×8，左下角起 (5,5) 的 AT8 四邻：西 / 东两格岩石、南 / 北两格深水。
        ImmutableArray<BoardPlate> b = LinkMapFixtures.Births;
        MapData map = LinkMapFixtures.Build("test-link-split", [b[0], b[1], b[2], b[3], b[4] with { Width = 8, Height = 8 }]);
        Assert.True(MapValidator.Validate(map).IsValid, MapValidator.Validate(map).ToString());
        BoardPlate fifth = map.Boards[4];
        Coord At(int dx, int dy) => new(fifth.Origin.X + dx, fifth.Origin.Y + dy);
        Coord island = At(5, 5);
        MapData cut = map with
        {
            Obstacles = map.Obstacles.Add(At(4, 5)).Add(At(6, 5)),
            TerrainData = Overlay(map, surfaces: [(At(5, 4).ToNotation(), Surface.DeepWater), (At(5, 6).ToNotation(), Surface.DeepWater)]),
        };

        MapValidationFailure failure = Assert.Single(MapValidator.Validate(cut).Failures, f => f.Code == "BOARD_SPLIT");
        Assert.Equal([island], failure.Coords);
        Assert.Contains($"出生棋盘 {fifth.Origin.ToNotation()}–", failure.Message, StringComparison.Ordinal);

        // 反面：同样两块岩石、只放一格深水 → 孤岛仍与北边相连，不报。
        MapData open = cut with { TerrainData = Overlay(map, surfaces: [(At(5, 4).ToNotation(), Surface.DeepWater)]) };
        Assert.DoesNotContain(MapValidator.Validate(open).Failures, f => f.Code == "BOARD_SPLIT");
    }

    [Fact]
    public void 可改造目标不足()
    {
        // 规格 Scenario：出生棋盘 2 内林地格与未架桥深水格合计只有 1 个 → 拒绝并指出该棋盘与"至少 2 个可改造目标"（BOARD_TOO_FEW_EDIT_TARGETS）。
        MapData map = Link();
        BoardPlate second = map.Boards[1];
        Coord[] forest = [.. LinkMapFixtures.ForestOf(second)];
        TerrainData one = new(map.TerrainData.Heights, map.TerrainData.Surfaces.Remove(forest[1]), map.TerrainData.Bridges, map.TerrainData.Fences);
        MapValidationFailure failure = Assert.Single(MapValidator.Validate(map with { TerrainData = one }).Failures, f => f.Code == "BOARD_TOO_FEW_EDIT_TARGETS");
        Assert.Equal([second.Origin], failure.Coords);
        Assert.Contains("合计 1 个", failure.Message, StringComparison.Ordinal);
        Assert.Contains("至少 2 个可改造目标", failure.Message, StringComparison.Ordinal);

        // 未架桥的深水算目标，架了桥的不算。
        TerrainData water = new(one.Heights, one.Surfaces.SetItem(forest[1], Surface.DeepWater), one.Bridges, one.Fences);
        Assert.True(MapValidator.Validate(map with { TerrainData = water }).IsValid, MapValidator.Validate(map with { TerrainData = water }).ToString());
        TerrainData bridged = new(water.Heights, water.Surfaces, water.Bridges.Add(forest[1]), water.Fences);
        Assert.Contains(MapValidator.Validate(map with { TerrainData = bridged }).Failures, f => f.Code == "BOARD_TOO_FEW_EDIT_TARGETS");
    }

    [Fact]
    public void 主题重复()
    {
        // 规格 Scenario：两块出生棋盘的主题都是河流 → 拒绝并指出这两块棋盘（BOARD_THEME_INVALID）。
        ImmutableArray<BoardPlate> b = LinkMapFixtures.Births;
        MapData twice = LinkMapFixtures.Build("test-link-theme", [b[0], b[1], b[2] with { Theme = BoardTheme.River }, b[3], b[4]]);
        MapValidationFailure failure = Assert.Single(MapValidator.Validate(twice).Failures, f => f.Code == "BOARD_THEME_INVALID");
        Assert.Equal([b[1].Origin, b[2].Origin], failure.Coords);
        Assert.Contains("出生棋盘 M3–R8", failure.Message, StringComparison.Ordinal);
        Assert.Contains("出生棋盘 V3–AA8", failure.Message, StringComparison.Ordinal);
        Assert.Contains("河流", failure.Message, StringComparison.Ordinal);

        // 出生棋盘缺主题、主题取值无法识别也被拒，各指出该棋盘。
        MapData missing = LinkMapFixtures.Build("test-link-theme", [b[0] with { Theme = null }, b[1], b[2], b[3], b[4]]);
        Assert.Equal([b[0].Origin], Assert.Single(MapValidator.Validate(missing).Failures, f => f.Code == "BOARD_THEME_INVALID").Coords);
        MapData unknown = LinkMapFixtures.Build("test-link-theme", [b[0] with { Theme = (BoardTheme)9 }, b[1], b[2], b[3], b[4]]);
        Assert.Equal([b[0].Origin], Assert.Single(MapValidator.Validate(unknown).Failures, f => f.Code == "BOARD_THEME_INVALID").Coords);
    }

    [Fact]
    public void 地形数据越界被报出()
    {
        // 3.2：高度 / 地表 / 桥 / 栅栏端点必须在盘内，越界项不参与任何查询。
        // "桥须在深水、栅栏须相邻"由 TerrainData 构造期抛出（裁决 A-1），校验器不重复。
        // link-map-terrain：载体画布 46×10，越界格改为第 11 / 12 行。
        TerrainData terrain = Overlay(
            Link(),
            heights: [("C11", 1)],
            surfaces: [("D11", Surface.DeepWater)],
            bridges: ["D11"],
            fences: [("D11", "D12")]);
        MapData map = Link() with { TerrainData = terrain };

        MapValidationFailure failure = Assert.Single(MapValidator.Validate(map).Failures, f => f.Code == "TERRAIN_OUT_OF_BOUNDS");
        Assert.Equal(["C11", "D11", "D12"], failure.Coords.Notations());

        Assert.DoesNotContain(MapValidator.Validate(Link()).Failures, f => f.Code == "TERRAIN_OUT_OF_BOUNDS");
    }

    [Fact]
    public void 出生区可落子格超出区间()
    {
        // 链接档单区恰 9 格（link-map-terrain：出生区 = 3×3 核心区；原 25–49）。核心区挖掉一格 → 8 格 → 报错。
        MapData map = Link();
        MapData shrunk = map with { Obstacles = map.Obstacles.Add(map.BirthZones[0].Order().First()) };
        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(shrunk).Failures, f => f.Code == "BIRTH_ZONE_SIZE_OUT_OF_RANGE");
        Assert.Contains("出生区 1 有 8 个可落子格", failure.Message, StringComparison.Ordinal);
        Assert.Contains("9–9", failure.Message, StringComparison.Ordinal);

        // 上界同样是 9：给出生区添一格中间带格 G5 → 10 → 报错。
        MapData ten = map with { BirthZones = map.BirthZones.SetItem(0, map.BirthZones[0].Add(Coord.Parse("G5"))) };
        MapValidationFailure over = Assert.Single(
            MapValidator.Validate(ten).Failures, f => f.Code == "BIRTH_ZONE_SIZE_OUT_OF_RANGE");
        Assert.Contains("出生区 1 有 10 个可落子格", over.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(MapValidator.Validate(map).Failures, f => f.Code == "BIRTH_ZONE_SIZE_OUT_OF_RANGE");
    }

    [Fact]
    public void 信物格必须标注所属分区()
    {
        // 公共区的信物格被标成出生区档位，或出生区的信物格被标成公共区，都必须报错——
        // 分区标错会让信物生成用错强度预算，而那时已经在对局里了。
        // 链接档初始图的公共区信物格在核心区之外；这里开放一格场景格（J5）放一枚标成出生区档位的信物，断言不变。
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
        // "每区恰 9 格"是另一条独立规则（BIRTH_ZONE_SIZE_OUT_OF_RANGE），由「出生区可落子格超出区间」把关。
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
        // link-map-terrain：出生区缩成 3×3 核心区后，口袋必须切进核心区才"在出生区内"——用 C4 / D3（外圈）与 E4 / D5（核心区）四块障碍把核心区左下角 D4 围成 1 格。
        // 剩下的格仍连成一片，不会多出第二个口袋；外圈 / 核心区的障碍另由清单规则报出，这里只看 DEAD_POCKET。
        MapData map = Link();
        MapData pocketed = map with { Obstacles = map.Obstacles.Union(PocketWalls) };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(pocketed).Failures, f => f.Code == "DEAD_POCKET");
        Assert.Contains("小于形成两眼所需的 8 格", failure.Message, StringComparison.Ordinal);
        Assert.Equal(["D4"], failure.Coords.Notations());
    }

    [Fact]
    public void 面积恰好等于两眼最小格数的空区不算口袋()
    {
        // 规格："连通空区面积小于 8 即判失败"——8 本身是合法的，阈值是闭的下界。
        // link-map-terrain：用障碍 F3 / E4 / E5 / F6 / G6 / H6 把出生棋盘 1 的 F4 / F5（核心区）与 G3–H5 圈出恰好 8 格（含林地 G4，林地可落子、不挡气）。
        MapData map = Link();
        Coord[] walls = [.. new[] { "F3", "E4", "E5", "F6", "G6", "H6" }.Select(Coord.Parse)];
        MapData eightCellPocket = map with { Obstacles = map.Obstacles.Union(walls) };

        Assert.DoesNotContain(
            MapValidator.Validate(eightCellPocket).Failures, f => f.Code == "DEAD_POCKET");

        // 同一块区域少一格（7 格）就必须失败，证明上面那次通过不是因为规则没跑。
        MapData sevenCellPocket = eightCellPocket with
        {
            Obstacles = eightCellPocket.Obstacles.Add(Coord.Parse("H3")),
        };
        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(sevenCellPocket).Failures, f => f.Code == "DEAD_POCKET");
        Assert.Equal(["G3", "F4", "G4", "H4", "F5", "G5", "H5"], failure.Coords.Notations());
    }

    [Fact]
    public void 出生区之外的小空区不算必死口袋()
    {
        // 规格只禁止"出生区内"的必死口袋——出生区之外的封闭小空区是装饰自由，误报会把合法地图挡在门外。
        // 在手写链接档地图的场景里开放 K4 / K5 两格（四周仍是障碍；J 列紧贴出生棋盘 1 的东边外圈，不用）。
        MapData map = Link();
        Coord[] hole = [Coord.Parse("K4"), Coord.Parse("K5")];
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
            PocketExemptions = [Coord.Parse("D4")],
            PocketExemptionReasons = ImmutableDictionary<Coord, string>.Empty
                .Add(Coord.Parse("D4"), "测试用：刻意保留的装饰性死角"),
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
        // retire-legacy-maps 1.6：改用手写链接档地图上的必死口袋（各档共用），断言不变。
        MapData map = Link() with { Obstacles = Link().Obstacles.Union(PocketWalls) };

        MapValidationException ex = Assert.Throws<MapValidationException>(() => GameBoard.Load(map));

        Assert.Contains(ex.Result.Failures, f => f.Code == "DEAD_POCKET");
    }

    [Fact]
    public void 出生棋盘内有新地表()
    {
        // 规格 Scenario（link-map-terrain 改写）：出生棋盘 2 的中间带有一格标为浅滩 → 拒绝并指出该棋盘与该格坐标（BOARD_SPECIAL_SURFACE）。
        // 棋盘清单把 C3–H8 那块排在第 2 位；G5 在它的中间带，不在出生区（核心区）里，所以出生区第 6 条（BIRTH_ZONE_SPECIAL_SURFACE）不报——棋盘级规则覆盖了出生区之外。
        ImmutableArray<BoardPlate> b = LinkMapFixtures.Births;
        MapData map = LinkMapFixtures.Build("test-link-5", [b[1], b[0], b[2], b[3], b[4]]);
        Coord cell = Coord.Parse("G5");
        Assert.True(map.Boards[1].InMiddleBand(cell));
        MapData tainted = map with { TerrainData = Overlay(map, surfaces: [("G5", Surface.Shallows)]) };

        MapValidationResult result = MapValidator.Validate(tainted);
        MapValidationFailure board = Assert.Single(result.Failures, f => f.Code == "BOARD_SPECIAL_SURFACE");
        Assert.Equal([cell], board.Coords);
        Assert.Contains("出生棋盘 C3–H8", board.Message, StringComparison.Ordinal);
        Assert.Contains("G5 是浅滩", board.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Failures, f => f.Code == "BIRTH_ZONE_SPECIAL_SURFACE");

        // 规格 terrain-surfaces · map-definition 第 6 条（底线）：出生区（核心区 E5）内的新地表另报出生区编号、坐标与地表。
        // 变异验证 M-S0c（retire-legacy-maps 实跑）：ValidateBirthZones 漏掉 Shallows → 本测试与「出生区内四种新地表都被拒(Shallows)」各红 1。
        MapData core = map with { TerrainData = Overlay(map, surfaces: [("E5", Surface.Shallows)]) };
        MapValidationFailure zone = Assert.Single(MapValidator.Validate(core).Failures, f => f.Code == "BIRTH_ZONE_SPECIAL_SURFACE");
        Assert.Contains("出生区 2", zone.Message, StringComparison.Ordinal);
        Assert.Contains("E5", zone.Message, StringComparison.Ordinal);
        Assert.Contains("浅滩", zone.Message, StringComparison.Ordinal);
        Assert.Equal([Coord.Parse("E5")], zone.Coords);
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
        Assert.Contains(MapValidator.Validate(tainted).Failures, f => f.Code == "BOARD_SPECIAL_SURFACE");
    }

    [Fact]
    public void 公共区域的新地表不受限()
    {
        // 规格 Scenario「公共区域的新地表不受限」：棋盘之外标四种新地表，其余校验通过 → 接受。
        // 反面对照：原图本身必须通过（否则"接受"只是恰好没报这条）。
        // 链接档初始图的棋盘之外全是场景格；四种新地表标在场景格上（棋盘内的新地表由清单规则禁止，见「出生棋盘内有新地表」）。
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

    /// <summary>在地图原有地形（夹具的林地）之上叠加若干项，免得替换地形时把改造目标一起抹掉、引出无关的失败项。</summary>
    private static TerrainData Overlay(
        MapData map,
        (string Cell, int Height)[]? heights = null,
        (string Cell, Surface Surface)[]? surfaces = null,
        string[]? bridges = null,
        (string A, string B)[]? fences = null)
    {
        TerrainData extra = TestMaps.Terrain(heights, surfaces, bridges, fences);
        TerrainData t = map.TerrainData;
        return new TerrainData(t.Heights.SetItems(extra.Heights), t.Surfaces.SetItems(extra.Surfaces), t.Bridges.Union(extra.Bridges), t.Fences.Union(extra.Fences));
    }
}
