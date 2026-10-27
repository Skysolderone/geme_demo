using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>规格：map-definition —— Requirement: 地图静态校验规则（link-map-terrain 增量：链接档清单规则改写）</summary>
/// <remarks>
/// retire-legacy-maps 1.6：各档共用的规则（结构、地形边界、规模预算、出生区、信物格、必死口袋）的用例由 v5 / v2 改到链接档地图。
/// map-expand-25 段 A2：手写的 22×16 紧贴图在浮空岛校验器下恒不合规（画布、平台高度、坡道、岛屿外接矩形都不符），载体改为生成图 <c>link:1:2</c>：
/// 画布 34×34、6 块平台（1 号 E5–L10 河流、2 号 Y5–AE10 河流、3 号 O14–V21 河流、4 号 Z14–AE21 栅栏阵、5 号 E16–J21 栅栏阵、6 号 Q25–V30 林地），
/// 可落子 820、信物 16（出生区档 12 + 公共区档 4）。各用例的格子取平台相对坐标或按条件挑选，只在必须写死坐标时写死并先断言前提。
/// 期望值换成 2 人档预算（可落子 760–876、出生区 6–8、信物 12–20、画布恰 34）。
/// 删除的断言：山脊隔断线（高台主题去掉、平台整体高 2 后校验器只认河道，见 MapValidator.BarriersOf）；外圈新地表另报 BOARD_RIM_NOT_FLAT（外圈平坦规则删除，D7）。
/// map-expand-25 段 A3：浮空岛不放静态信物格（relic-redesign R29，2026-10-26 用户裁决），link:1:2 信物 0、可落子 833（信物不再消费布局流，坡道与低地重抽；
/// 平台落位、主题与平台内部不变）；信物区间与"每块恰 2 个出生区档"规则删除，「信物格数越界」「出生棋盘信物数不对」两条随之删除；
/// 信物格合法性（分区、档位、可落子）的用例改为在图上手工补信物格。
/// </remarks>
public class 地图静态校验规则Tests
{
    /// <summary>生成图 <c>link:1:2</c>（合规；生成器按 (种子, 人数) 缓存，每次取到同一个对象）。</summary>
    private static MapData Link() => LinkMapGenerator.Generate(1, 2);

    /// <summary>6 号平台 Q25–V30（林地主题、6×6）：全部可落子、没有隔断线与栅栏、核心区 R26–T28，用作"干净"的平台。</summary>
    private static BoardPlate Clean => Link().Boards[5];

    /// <summary>6 号平台核心区右下角 T26 用 S26 / T27（核心区）与 U26 / T25（中间带、外圈）四块障碍围死：面积 1 的口袋。</summary>
    private static readonly Coord[] PocketWalls = [.. new[] { "S26", "U26", "T25", "T27" }.Select(Coord.Parse)];

    [Fact]
    public void 合规的链接档生成图通过()
    {
        // 下面各条都在这张图上只改一处——它本身 MUST 通过，否则"报出某条"可能只是恰好碰上别的失败。
        MapData map = Link();
        MapValidationResult result = MapValidator.Validate(map);
        Assert.True(result.IsValid, result.ToString());
        Assert.Equal(833, map.PlayableCount);
        Assert.Equal(6, map.Boards.Length);
        Assert.DoesNotContain("RelicCells", MapFile.ToJson(map), StringComparison.Ordinal);   // 不放静态信物格（段 A3；relic-redesign W1 起地图数据没有该字段）
        Assert.Equal("Q25", Clean.Origin.ToNotation());
        Assert.Equal(BoardTheme.Forest, Clean.Theme);
        Assert.All(Clean.Cells(), c => Assert.True(map.IsPlayable(c), c.ToNotation()));
        Assert.Equal("R26", Clean.CoreOrigin.ToNotation());
    }

    [Fact]
    public void 可落子格越界()
    {
        // 2 人浮空岛可落子格区间 760–876（MapValidator 声明表）。下界：低地（平台外）按坐标序挖掉 74 格 → 833 − 74 = 759；上界：画布全部开放。
        MapData map = Link();
        Coord[] rocks = [.. map.AllCoords().Where(c => map.IsPlayable(c) && !map.Boards.Any(b => b.Contains(c))).Order().Take(74)];
        MapData tooFew = map with { Obstacles = map.Obstacles.Union(rocks) };
        Assert.Equal(759, tooFew.PlayableCount);
        MapValidationFailure low = Assert.Single(
            MapValidator.Validate(tooFew).Failures, f => f.Code == "PLAYABLE_COUNT_OUT_OF_RANGE");
        Assert.Contains("可落子格为 759", low.Message, StringComparison.Ordinal);
        Assert.Contains("760–876", low.Message, StringComparison.Ordinal);
        Assert.Throws<MapValidationException>(() => GameBoard.Load(tooFew));

        // 上界 876 同样要挡住：区间只有下界有算例时，把上界写成 2000 只会让消息文本变化，行为上一条测试都不红。
        MapData tooOpen = map with { Obstacles = [] };
        Assert.True(tooOpen.PlayableCount > 876, tooOpen.PlayableCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        MapValidationFailure high = Assert.Single(
            MapValidator.Validate(tooOpen).Failures, f => f.Code == "PLAYABLE_COUNT_OUT_OF_RANGE");
        Assert.Contains($"可落子格为 {tooOpen.PlayableCount}", high.Message, StringComparison.Ordinal);
        Assert.Contains("760–876", high.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 外接尺寸越界被拒绝而不是抛异常()
    {
        // retire-legacy-maps（check 补回覆盖）：自已删除的 边疆档静态校验Tests.超宽地图被拒绝而不是抛异常 迁来。
        // 所守规则不变：外接尺寸不合该档声明 → 报拒绝项并到此为止，不往下跑规模与棋盘规则、不在枚举格子时崩溃。
        // map-expand-25 D7：画布边长必须恰等于 W(n)+2M，2 人档为 34（原列 22–25、行 16–18 的区间删除）。
        MapData map = Link();
        (MapData Map, string Code, string Value, string Bound)[] cases =
        [
            (map with { Width = 35 }, "MAP_TOO_WIDE", "35", "34"),
            (map with { Width = 1000 }, "MAP_TOO_WIDE", "1000", "34"),
            (map with { Width = 33 }, "MAP_TOO_NARROW", "33", "34"),
            (map with { Height = 35 }, "MAP_TOO_TALL", "35", "34"),
            (map with { Height = 33 }, "MAP_TOO_SHORT", "33", "34"),
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

        // 恰等于 34 合法：基图本身四条尺寸码一条都不报。
        Assert.DoesNotContain(MapValidator.Validate(map).Failures, f => f.Code is "MAP_TOO_WIDE" or "MAP_TOO_NARROW" or "MAP_TOO_TALL" or "MAP_TOO_SHORT");
    }

    [Fact]
    public void 出生区数越界()
    {
        // 2 人浮空岛出生区（= 平台）6–8 个。拿掉最后一块平台（连同出生区与其信物）→ 5 个，低于下限；把前三块再列一遍 → 9 个，高于上限。
        MapData map = Link();
        BoardPlate last = map.Boards[^1];
        MapData five = map with
        {
            Boards = map.Boards.RemoveAt(map.Boards.Length - 1),
            BirthZones = map.BirthZones.RemoveAt(map.BirthZones.Length - 1),
        };
        Assert.Equal(5, five.BirthZones.Length);
        MapValidationFailure low = Assert.Single(
            MapValidator.Validate(five).Failures, f => f.Code == "BIRTH_ZONE_COUNT_OUT_OF_RANGE");
        Assert.Contains("出生区为 5 个", low.Message, StringComparison.Ordinal);
        Assert.Contains("低于下限 6", low.Message, StringComparison.Ordinal);
        Assert.Contains("6–8", low.Message, StringComparison.Ordinal);

        MapData nine = map with { Boards = map.Boards.AddRange(map.Boards.Take(3)), BirthZones = map.BirthZones.AddRange(map.BirthZones.Take(3)) };
        Assert.Equal(9, nine.BirthZones.Length);
        MapValidationFailure high = Assert.Single(
            MapValidator.Validate(nine).Failures, f => f.Code == "BIRTH_ZONE_COUNT_OUT_OF_RANGE");
        Assert.Contains("出生区为 9 个", high.Message, StringComparison.Ordinal);
        Assert.Contains("高于上限 8", high.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(MapValidator.Validate(map).Failures, f => f.Code == "BIRTH_ZONE_COUNT_OUT_OF_RANGE");
    }

    [Fact]
    public void 不支持的人数被拒绝()
    {
        // 链接档只提供 2–4 人的预算表（map-expand-25：原只有 4 人一行，3 人曾是反例）。
        Assert.Contains(MapValidator.Validate(Link() with { MaxPlayers = 5 }).Failures, f => f.Code == "UNSUPPORTED_PLAYER_COUNT");
        Assert.Contains(MapValidator.Validate(Link() with { MaxPlayers = 1 }).Failures, f => f.Code == "UNSUPPORTED_PLAYER_COUNT");
        Assert.DoesNotContain(MapValidator.Validate(Link()).Failures, f => f.Code == "UNSUPPORTED_PLAYER_COUNT");
    }

    [Fact]
    public void 隔断线没有通道被拒()
    {
        // 规格 Scenario（terrain-barriers D5 / 实施修订 J5）：河道横穿平台但没有桥 → 拒绝并指出该平台与"隔断线须恰有 1 处通道"。
        // 载体是 1 号平台 E5–L10（河流主题）自带的 F 列河道 F5–F10，桥在 F5。map-expand-25：山脊随高台主题去掉，原山脊三段断言删除。
        MapData map = Link();
        Coord bridge = Coord.Parse("F5");
        string[] column = ["F5", "F6", "F7", "F8", "F9", "F10"];
        Assert.True(map.HasBridge(bridge));
        Assert.All(column, c => Assert.Equal(Surface.DeepWater, map.SurfaceAt(Coord.Parse(c))));
        TerrainData t = map.TerrainData;

        MapValidationResult noBridge = MapValidator.Validate(map with { TerrainData = new TerrainData(t.Heights, t.Surfaces, t.Bridges.Remove(bridge), t.Fences) });
        MapValidationFailure river = Assert.Single(noBridge.Failures, f => f.Code == "BOARD_BARRIER_INVALID");
        Assert.Contains("出生棋盘 E5–L10", river.Message, StringComparison.Ordinal);   // 记法跳过 I：E–L 共 7 列
        Assert.Contains("有 0 座桥", river.Message, StringComparison.Ordinal);
        Assert.Contains("恰有 1 处通道", river.Message, StringComparison.Ordinal);
        Assert.Equal(column, river.Coords.Notations());

        // 两座桥 = 两处通道，同样被拒（只指出两座桥）。
        MapValidationFailure twoBridges = Assert.Single(
            MapValidator.Validate(map with { TerrainData = new TerrainData(t.Heights, t.Surfaces, t.Bridges.Add(Coord.Parse("F8")), t.Fences) }).Failures,
            f => f.Code == "BOARD_BARRIER_INVALID");
        Assert.Equal(["F5", "F8"], twoBridges.Coords.Notations());
    }

    [Fact]
    public void 隔断线经过核心区被拒()
    {
        // tasks 2.1：6 号平台（Q25–V30，核心区 R26–T28）的第 27 行 Q27–V27 整行深水、桥 U27 → 隔断线经过核心区，BOARD_BARRIER_INVALID 指出 R27–T27；核心区规则同时报出。
        MapData map = Link();
        string[] row = ["Q27", "R27", "S27", "T27", "U27", "V27"];
        MapValidationResult result = MapValidator.Validate(map with { TerrainData = Overlay(map, surfaces: [.. row.Select(c => (c, Surface.DeepWater))], bridges: ["U27"]) });
        MapValidationFailure core = Assert.Single(result.Failures, f => f.Code == "BOARD_BARRIER_INVALID");
        Assert.Contains("经过核心区", core.Message, StringComparison.Ordinal);
        Assert.Equal(["R27", "S27", "T27"], core.Coords.Notations());
        Assert.Contains(result.Failures, f => f.Code == "BIRTH_CORE_NOT_FLAT");
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

        // 高度（map-expand-25：核心区须高 2，这里压到 1）、深水、岩石同样被拒；核心区与中间带之间的栅栏可以有（裁决 Q4，见 物化状态校验Tests）。
        Assert.Contains(MapValidator.Validate(map with { TerrainData = Overlay(map, heights: [(cell.ToNotation(), 1)]) }).Failures, f => f.Code == "BIRTH_CORE_NOT_FLAT");
        Assert.Contains(MapValidator.Validate(map with { TerrainData = Overlay(map, surfaces: [(cell.ToNotation(), Surface.DeepWater)]) }).Failures, f => f.Code == "BIRTH_CORE_NOT_FLAT");
        Assert.Contains(MapValidator.Validate(map with { Obstacles = map.Obstacles.Add(cell) }).Failures, f => f.Code == "BIRTH_CORE_NOT_FLAT");
    }

    [Fact]
    public void 棋盘被切断()
    {
        // 规格 Scenario：平台内一格被岩石与深水围住、与平台其余格不连通 → 拒绝并指出该平台与被隔开的格（BOARD_SPLIT）。
        // map-expand-25：外圈不再要求可落子，干净的 6 号平台上直接围核心区中心 S27：西 / 东两格岩石、南 / 北两格深水；其余核心区角格仍经中间带与外圈相连。
        MapData map = Link();
        BoardPlate plate = Clean;
        Coord At(int dx, int dy) => new(plate.CoreOrigin.X + dx, plate.CoreOrigin.Y + dy);
        Coord island = At(1, 1);
        Assert.Equal("S27", island.ToNotation());
        MapData cut = map with
        {
            Obstacles = map.Obstacles.Add(At(0, 1)).Add(At(2, 1)),
            TerrainData = Overlay(map, surfaces: [(At(1, 0).ToNotation(), Surface.DeepWater), (At(1, 2).ToNotation(), Surface.DeepWater)]),
        };

        MapValidationFailure failure = Assert.Single(MapValidator.Validate(cut).Failures, f => f.Code == "BOARD_SPLIT");
        Assert.Equal([island], failure.Coords);
        Assert.Contains($"出生棋盘 {plate.Origin.ToNotation()}–", failure.Message, StringComparison.Ordinal);

        // 反面：同样两块岩石、只放一格深水 → 中心格仍与北边相连，不报。
        MapData open = cut with { TerrainData = Overlay(map, surfaces: [(At(1, 0).ToNotation(), Surface.DeepWater)]) };
        Assert.DoesNotContain(MapValidator.Validate(open).Failures, f => f.Code == "BOARD_SPLIT");
    }

    [Fact]
    public void 可改造目标不足()
    {
        // 规格 Scenario：平台内林地格与未架桥深水格合计只有 1 个 → 拒绝并指出该平台与"至少 2 个可改造目标"（BOARD_TOO_FEW_EDIT_TARGETS）。
        // 载体是 6 号平台（林地主题，平台内没有深水）：只留第一格林地，其余林地改回草地。
        MapData map = Link();
        BoardPlate plate = Clean;
        Coord[] forest = [.. plate.Cells().Where(c => map.SurfaceAt(c) == Surface.Forest).Order()];
        Assert.True(forest.Length >= 2);
        Assert.DoesNotContain(plate.Cells(), c => map.SurfaceAt(c) == Surface.DeepWater);
        TerrainData t = map.TerrainData;
        TerrainData one = new(t.Heights, t.Surfaces.RemoveRange(forest.Skip(1)), t.Bridges, t.Fences);
        MapValidationFailure failure = Assert.Single(MapValidator.Validate(map with { TerrainData = one }).Failures, f => f.Code == "BOARD_TOO_FEW_EDIT_TARGETS");
        Assert.Equal([plate.Origin], failure.Coords);
        Assert.Contains("合计 1 个", failure.Message, StringComparison.Ordinal);
        Assert.Contains("至少 2 个可改造目标", failure.Message, StringComparison.Ordinal);

        // 未架桥的深水算目标，架了桥的不算。
        TerrainData water = new(one.Heights, one.Surfaces.SetItem(forest[1], Surface.DeepWater), one.Bridges, one.Fences);
        Assert.DoesNotContain(MapValidator.Validate(map with { TerrainData = water }).Failures, f => f.Code == "BOARD_TOO_FEW_EDIT_TARGETS");
        TerrainData bridged = new(water.Heights, water.Surfaces, water.Bridges.Add(forest[1]), water.Fences);
        Assert.Contains(MapValidator.Validate(map with { TerrainData = bridged }).Failures, f => f.Code == "BOARD_TOO_FEW_EDIT_TARGETS");
    }

    [Fact]
    public void 地形数据越界被报出()
    {
        // 3.2：高度 / 地表 / 桥 / 栅栏端点必须在盘内，越界项不参与任何查询。
        // "桥须在深水、栅栏须相邻"由 TerrainData 构造期抛出（裁决 A-1），校验器不重复。
        // map-expand-25：载体画布 34×34，越界格改为第 35 / 36 行。
        TerrainData terrain = Overlay(
            Link(),
            heights: [("C35", 1)],
            surfaces: [("D35", Surface.DeepWater)],
            bridges: ["D35"],
            fences: [("D35", "D36")]);
        MapData map = Link() with { TerrainData = terrain };

        MapValidationFailure failure = Assert.Single(MapValidator.Validate(map).Failures, f => f.Code == "TERRAIN_OUT_OF_BOUNDS");
        Assert.Equal(["C35", "D35", "D36"], failure.Coords.Notations());

        Assert.DoesNotContain(MapValidator.Validate(Link()).Failures, f => f.Code == "TERRAIN_OUT_OF_BOUNDS");
    }

    [Fact]
    public void 出生区可落子格超出区间()
    {
        // 链接档单区恰 9 格（出生区 = 3×3 核心区）。核心区挖掉一格 → 8 格 → 报错。
        MapData map = Link();
        MapData shrunk = map with { Obstacles = map.Obstacles.Add(map.BirthZones[0].Order().First()) };
        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(shrunk).Failures, f => f.Code == "BIRTH_ZONE_SIZE_OUT_OF_RANGE");
        Assert.Contains("出生区 1 有 8 个可落子格", failure.Message, StringComparison.Ordinal);
        Assert.Contains("9–9", failure.Message, StringComparison.Ordinal);

        // 上界同样是 9：给出生区添一格 1 号平台中间带的可落子格 → 10 → 报错。
        BoardPlate first = map.Boards[0];
        Coord middle = first.Cells().Order().First(c => first.InMiddleBand(c) && map.IsPlayable(c));
        MapData ten = map with { BirthZones = map.BirthZones.SetItem(0, map.BirthZones[0].Add(middle)) };
        MapValidationFailure over = Assert.Single(
            MapValidator.Validate(ten).Failures, f => f.Code == "BIRTH_ZONE_SIZE_OUT_OF_RANGE");
        Assert.Contains("出生区 1 有 10 个可落子格", over.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(MapValidator.Validate(map).Failures, f => f.Code == "BIRTH_ZONE_SIZE_OUT_OF_RANGE");
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
        // retire-legacy-rules L18：原末段"容差为负 → TOLERANCE_NEGATIVE"随容差规则删除（见 中央入口与容差不再校验）。
    }

    [Fact]
    public void 必死口袋()
    {
        // 口袋必须切进核心区才"在出生区内"——用 S26 / T27（核心区）与 U26 / T25 四块障碍把 6 号平台核心区右下角 T26 围成 1 格。
        // 剩下的格仍连成一片，不会多出第二个口袋；核心区的障碍另由清单规则报出，这里只看 DEAD_POCKET。
        MapData map = Link();
        MapData pocketed = map with { Obstacles = map.Obstacles.Union(PocketWalls) };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(pocketed).Failures, f => f.Code == "DEAD_POCKET");
        Assert.Contains("小于形成两眼所需的 8 格", failure.Message, StringComparison.Ordinal);
        Assert.Equal(["T26"], failure.Coords.Notations());
    }

    [Fact]
    public void 面积恰好等于两眼最小格数的空区不算口袋()
    {
        // 规格："连通空区面积小于 8 即判失败"——8 本身是合法的，阈值是闭的下界。
        // map-expand-25：6 号平台东南角 T25–V26 与 T27 / U27 共 8 格（含核心区 T26 / T27），南 / 东两侧是平台崖壁（高 2 对低地高 0，不通气），
        // 再用障碍 S25 / S26 / S27 / T28 / U28 / V27 封住西、北两侧，空区就是封闭的。
        // 段 A3：坡道重抽后 T25 成了坡道（高 1，与南侧低地通气），空区东移一列：U25–V27 六格与核心区 T26 / T27 共 8 格，
        // 南 / 东两侧是平台崖壁，用障碍 T25 / S26 / S27 / T28 / U28 / V28 封住西、北两侧。
        MapData map = Link();
        Assert.DoesNotContain(new[] { "U25", "V25", "T26", "U26", "V26", "T27", "U27", "V27" }.Select(Coord.Parse), c => map.HeightAt(c) != 2);
        Coord[] walls = [.. new[] { "T25", "S26", "S27", "T28", "U28", "V28" }.Select(Coord.Parse)];
        MapData eightCellPocket = map with { Obstacles = map.Obstacles.Union(walls) };

        Assert.DoesNotContain(
            MapValidator.Validate(eightCellPocket).Failures, f => f.Code == "DEAD_POCKET");

        // 同一块区域少一格（7 格）就必须失败，证明上面那次通过不是因为规则没跑。
        MapData sevenCellPocket = eightCellPocket with
        {
            Obstacles = eightCellPocket.Obstacles.Add(Coord.Parse("V25")),
        };
        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(sevenCellPocket).Failures, f => f.Code == "DEAD_POCKET");
        Assert.Equal(["U25", "T26", "U26", "V26", "T27", "U27", "V27"], failure.Coords.Notations());
    }

    [Fact]
    public void 出生区之外的小空区不算必死口袋()
    {
        // 规格只禁止"出生区内"的必死口袋——出生区之外的封闭小空区是装饰自由，误报会把合法地图挡在门外。
        // 在画布西南角的留白里开放 A1 / A2 两格（四周仍是障碍）。
        MapData map = Link();
        Coord[] hole = [Coord.Parse("A1"), Coord.Parse("A2")];
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
            PocketExemptions = [Coord.Parse("T26")],
            PocketExemptionReasons = ImmutableDictionary<Coord, string>.Empty
                .Add(Coord.Parse("T26"), "测试用：刻意保留的装饰性死角"),
        };

        Assert.DoesNotContain(MapValidator.Validate(exempted).Failures, f => f.Code == "DEAD_POCKET");
    }

    [Fact]
    public void 豁免必须写明理由()
    {
        MapData map = Link() with { PocketExemptions = [Coord.Parse("T26")] };

        Assert.Contains(
            MapValidator.Validate(map).Failures, f => f.Code == "POCKET_EXEMPTION_WITHOUT_REASON");
    }

    [Fact]
    public void 中央入口与容差不再校验()
    {
        // 规格 Scenario「中央入口与容差不再校验」（retire-legacy-rules L18，裁决 R3）：中央入口与一个标注的咽喉落在不可落子的场景格上，
        // 距离容差为 3 且没有放宽理由 → 校验器不因这几项报出任何失败（基图本身合规，所以整体通过）；导出再读回，四个字段逐项相同。
        // 变异：恢复原 ValidateLandmarks 或 ValidateTolerance 中任一条 → IsValid 为假，本测试红。
        Coord entrance = Coord.Parse("A5");   // map-expand-25：画布西侧留白
        Coord choke = Coord.Parse("B4");
        MapData map = Link() with { CentralEntrance = entrance, ChokePoints = [choke], DistanceTolerance = 3, ToleranceRelaxReason = null };
        Assert.False(map.IsPlayable(entrance));
        Assert.False(map.IsPlayable(choke));

        MapValidationResult result = MapValidator.Validate(map);
        Assert.True(result.IsValid, result.ToString());

        MapData back = MapFile.FromJson(MapFile.ToJson(map));
        Assert.Equal(entrance, back.CentralEntrance);
        Assert.Equal([choke], back.ChokePoints.Order());
        Assert.Equal(3, back.DistanceTolerance);
        Assert.Null(back.ToleranceRelaxReason);
        Assert.Equal(MapFile.ToJson(map), MapFile.ToJson(back));
    }

    [Fact]
    public void 校验不通过则拒绝加载()
    {
        // retire-legacy-maps 1.6：改用链接档地图上的必死口袋（各档共用），断言不变。
        MapData map = Link() with { Obstacles = Link().Obstacles.Union(PocketWalls) };

        MapValidationException ex = Assert.Throws<MapValidationException>(() => GameBoard.Load(map));

        Assert.Contains(ex.Result.Failures, f => f.Code == "DEAD_POCKET");
    }

    [Fact]
    public void 中间带新地表被接受()
    {
        // 规格 Scenario（link-map-surfaces 改写，原「出生棋盘内有新地表」被拒）：平台的中间带有一格标为浅滩 → 接受加载，不报新地表相关的失败。
        // 6 号平台的 S29 在中间带，不在外圈、不在出生区（核心区）里；该平台原有 2 格新地表，加 1 格仍在每块至多 4 格之内。四种新地表逐一试一遍。
        MapData map = Link();
        Coord cell = Coord.Parse("S29");
        Assert.True(Clean.InMiddleBand(cell));
        Assert.Equal(Surface.Grass, map.SurfaceAt(cell));
        Assert.True(MapValidator.Validate(map).IsValid);
        foreach (Surface surface in new[] { Surface.Shallows, Surface.Desert, Surface.Marsh, Surface.Crag })
        {
            MapData painted = map with { TerrainData = Overlay(map, surfaces: [("S29", surface)]) };
            Assert.Equal(surface, painted.SurfaceAt(cell));
            MapValidationResult result = MapValidator.Validate(painted);
            Assert.True(result.IsValid, result.ToString());
            Assert.NotNull(GameBoard.Load(painted));
        }
    }

    [Fact]
    public void 外圈或核心区的新地表被拒()
    {
        // 规格 Scenario（link-map-surfaces D4）：6 号平台（Q25–V30）的外圈有一格标为荒漠，或核心区有一格标为沼泽 → 拒绝加载并指出该平台与该格坐标（BOARD_SPECIAL_SURFACE）。
        // 核心区的沼泽同时违反"核心区 9 格是草地"与出生区第 6 条——那两条原样保留，这里另给出更精确的失败信息。
        // map-expand-25：外圈平坦规则（BOARD_RIM_NOT_FLAT）删除，外圈荒漠只报 BOARD_SPECIAL_SURFACE。
        MapData map = Link();
        foreach ((string at, Surface surface, string where) in new[] { ("Q27", Surface.Desert, "外圈"), ("S27", Surface.Marsh, "核心区") })
        {
            Coord cell = Coord.Parse(at);
            Assert.Equal(where == "外圈", Clean.OnRim(cell));
            Assert.Equal(where == "核心区", Clean.InCore(cell));
            MapValidationResult result = MapValidator.Validate(map with { TerrainData = Overlay(map, surfaces: [(at, surface)]) });
            MapValidationFailure board = Assert.Single(result.Failures, f => f.Code == "BOARD_SPECIAL_SURFACE");
            Assert.Equal([cell], board.Coords);
            Assert.Contains("出生棋盘 Q25–V30", board.Message, StringComparison.Ordinal);
            Assert.Contains($"{at}（{where}）是{surface.DisplayName()}", board.Message, StringComparison.Ordinal);
            Assert.Contains("只许在中间带", board.Message, StringComparison.Ordinal);
            Assert.Equal(where == "核心区", result.Failures.Any(f => f.Code == "BIRTH_CORE_NOT_FLAT"));
        }

        // 规格 terrain-surfaces · map-definition 第 6 条（底线）：出生区（核心区 S26）内的新地表另报出生区编号、坐标与地表。
        // 变异验证 M-S0c（retire-legacy-maps 实跑）：ValidateBirthZones 漏掉 Shallows → 本测试与「出生区内四种新地表都被拒(Shallows)」各红 1。
        MapData core = map with { TerrainData = Overlay(map, surfaces: [("S26", Surface.Shallows)]) };
        MapValidationFailure zone = Assert.Single(MapValidator.Validate(core).Failures, f => f.Code == "BIRTH_ZONE_SPECIAL_SURFACE");
        Assert.Contains("出生区 6", zone.Message, StringComparison.Ordinal);
        Assert.Contains("S26", zone.Message, StringComparison.Ordinal);
        Assert.Contains("浅滩", zone.Message, StringComparison.Ordinal);
        Assert.Equal([Coord.Parse("S26")], zone.Coords);
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
        // 规格 Scenario「公共区域的新地表不受限」：平台之外标四种新地表，其余校验通过 → 接受。
        // 反面对照：原图本身必须通过（否则"接受"只是恰好没报这条）。
        // map-expand-25：平台之外是可落子的低地，四种新地表标在按坐标序前四个低地草地格上（平台内的新地表只许在中间带，见「中间带新地表被接受」「外圈或核心区的新地表被拒」）。
        MapData map = Link();
        Assert.True(MapValidator.Validate(map).IsValid);
        Coord[] outside = [.. map.AllCoords().Where(c => map.IsPlayable(c) && map.SurfaceAt(c) == Surface.Grass && !map.Boards.Any(b => b.Contains(c))).Order().Take(4)];
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

    /// <summary>在地图原有地形之上叠加若干项，免得替换地形时把改造目标一起抹掉、引出无关的失败项。</summary>
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
