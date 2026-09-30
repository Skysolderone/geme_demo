using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>
/// 规格：openspec/changes/board-map/specs/map-definition —— Requirement: 棋盘档预算与校验。
/// 数据是手写的最小 4 人棋盘档图（<see cref="BoardMapFixtures.FourPlayerMap"/>），每条用例只改动一处。
/// </summary>
/// <remarks>
/// 变异验证（board-map 段 B 实跑，改的都是 MapValidator.cs）：
/// M-B1 放过棋盘内障碍（棋盘内逐格检查的"不可落子"判据改成运行时恒假）→ 红 1：<see cref="棋盘内有障碍"/>；
/// M-B2 放过 3 格宽通道（声明表里通道宽度上限 2 → 3）→ 红 1：<see cref="通道过宽"/>。
/// 段 B 修正二（2026-09-30 裁决：通道 3–4 格宽，夹具六条通道全部加宽到 3）：
/// M-2c-1 放过 2 格宽通道（声明表里 CorridorWidth (3, 4) → (2, 4)）→ 红 3：<see cref="通道过窄"/> 两行（2 格宽被接受；1 格宽的报文变成"2–4"）与 <see cref="通道过宽"/>（报文变成"2–4"）。
/// </remarks>
public class 棋盘档预算与校验Tests
{
    private static ImmutableArray<BoardPlate> Boards => [.. BoardMapFixtures.FourBirths, BoardMapFixtures.FourPublicA, BoardMapFixtures.FourPublicB];

    private static ImmutableArray<Coord> Corridors => [.. BoardMapFixtures.FourMainCorridor, .. BoardMapFixtures.FourBirthCorridors];

    private static string[] Codes(MapValidationResult result) => [.. result.Failures.Select(f => f.Code)];

    [Fact]
    public void 棋盘内有障碍()
    {
        // Scenario：某块 9×9 棋盘的外接矩形内有一格障碍 → 拒绝，指出该棋盘的外接矩形与障碍格坐标。
        // 公共棋盘 A（J9–R17）里挖掉 L11；可落子 325，仍在预算内，不触发别的规则。
        var hole = new Coord(10, 10);
        MapData map = BoardMapFixtures.Build("test-board-hole", 36, 25, Boards, Corridors, hole);
        Assert.Equal(325, map.PlayableCount);

        MapValidationResult result = MapValidator.Validate(map);

        MapValidationFailure failure = Assert.Single(result.Failures);
        Assert.Equal("BOARD_CELL_NOT_PLAYABLE", failure.Code);
        Assert.Equal([hole], failure.Coords.AsEnumerable());
        Assert.Contains("J9", failure.Message, StringComparison.Ordinal);
        Assert.Contains("R17", failure.Message, StringComparison.Ordinal);
        Assert.Contains("9×9", failure.Message, StringComparison.Ordinal);
        Assert.Contains("L11", failure.ToString(), StringComparison.Ordinal);
        Assert.Throws<MapValidationException>(() => GameBoard.Load(map));
    }

    [Fact]
    public void 棋盘小于5乘5()
    {
        // Scenario：某块棋盘为 4×6 → 拒绝，指出该棋盘与合法边长 5–15。
        // 另加一块 4×6 的公共棋盘（左下角 (31, 17)），用一条 3 格宽、2 格长的通道接到 1 号出生棋盘的北边。
        var small = new BoardPlate(new Coord(31, 17), 4, 6, BoardPlateKind.Public);
        MapData map = BoardMapFixtures.Build(
            "test-board-small", 36, 25, [.. Boards, small], [.. Corridors, .. FrontierFixtures.Rect(31, 15, 3, 2)]);

        MapValidationResult result = MapValidator.Validate(map);

        MapValidationFailure failure = Assert.Single(result.Failures);
        Assert.Equal("BOARD_SIZE_OUT_OF_RANGE", failure.Code);
        Assert.Contains("4×6", failure.Message, StringComparison.Ordinal);
        Assert.Contains("5–15", failure.Message, StringComparison.Ordinal);
        Assert.Contains(small.Origin.ToNotation(), failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(8, 5, true)]      // 出生棋盘上限 7
    [InlineData(7, 5, false)]
    public void 出生棋盘的边长在5到7之间(int width, int height, bool rejected)
    {
        // 校验第 1 条：出生棋盘的宽与高各在 5–7。把 4 号出生棋盘（南侧）换成 width×height，通道仍接在 x = 12。
        ImmutableArray<BoardPlate> boards = Boards.SetItem(4, new BoardPlate(new Coord(10, 6 - height), width, height, BoardPlateKind.Birth));
        MapData map = BoardMapFixtures.Build("test-board-birth-size", 36, 25, boards, Corridors);

        string[] codes = Codes(MapValidator.Validate(map));

        Assert.Equal(rejected ? ["BOARD_SIZE_OUT_OF_RANGE"] : [], codes);
        if (rejected)
        {
            Assert.Contains("5–7", MapValidator.Validate(map).Failures[0].Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void 通道过宽()
    {
        // Scenario：两块棋盘之间的通道为 5 格宽 → 拒绝，指出该通道的格子坐标与合法宽度 3–4。
        // A–B 之间的通道由第 12–14 行三行改成第 11–15 行五行（3 格长 × 5 格宽）。
        Coord[] wide = [.. FrontierFixtures.Rect(17, 10, 3, 5)];
        MapData map = BoardMapFixtures.Build("test-board-wide", 36, 25, Boards, [.. wide, .. BoardMapFixtures.FourBirthCorridors]);

        MapValidationResult result = MapValidator.Validate(map);

        MapValidationFailure failure = Assert.Single(result.Failures);
        Assert.Equal("CORRIDOR_TOO_WIDE", failure.Code);
        Assert.Equal(wide.Order(), failure.Coords.AsEnumerable());
        Assert.Contains("3–4", failure.Message, StringComparison.Ordinal);
        Assert.Contains("通道宽 5 格", failure.Message, StringComparison.Ordinal);

        // 4 格宽合法。
        MapData four = BoardMapFixtures.Build(
            "test-board-four-wide", 36, 25, Boards, [.. FrontierFixtures.Rect(17, 10, 3, 4), .. BoardMapFixtures.FourBirthCorridors]);
        Assert.Empty(Codes(MapValidator.Validate(four)));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(1)]
    public void 通道过窄(int width)
    {
        // Scenario：两块棋盘之间的通道为 2 格宽 → 拒绝，指出该通道的格子坐标与合法宽度 3–4（1 格宽同样拒绝）。
        // A–B 之间的通道由第 12–14 行三行改成自第 13 行起的 width 行（3 格长 × width 格宽）。
        Coord[] narrow = [.. FrontierFixtures.Rect(17, 12, 3, width)];
        MapData map = BoardMapFixtures.Build("test-board-narrow", 36, 25, Boards, [.. narrow, .. BoardMapFixtures.FourBirthCorridors]);

        MapValidationResult result = MapValidator.Validate(map);

        MapValidationFailure failure = Assert.Single(result.Failures);
        Assert.Equal("CORRIDOR_TOO_NARROW", failure.Code);
        Assert.Equal(narrow.Order(), failure.Coords.AsEnumerable());
        Assert.Contains("3–4", failure.Message, StringComparison.Ordinal);
        Assert.Contains($"通道宽 {width} 格", failure.Message, StringComparison.Ordinal);
        Assert.Contains("S13", failure.ToString(), StringComparison.Ordinal);   // 通道西南角 (17, 12)
    }

    [Fact]
    public void 棋盘被孤立()
    {
        // Scenario：某块公共棋盘没有任何通道与其他棋盘相连 → 拒绝并指出该棋盘。
        // 图加高到 37 行，北侧另放一块 9×9 公共棋盘（左下角 (14, 26)），不接任何通道。
        var lonely = new BoardPlate(new Coord(14, 26), 9, 9, BoardPlateKind.Public);
        MapData map = BoardMapFixtures.Build("test-board-lonely", 36, 37, [.. Boards, lonely], Corridors);

        MapValidationResult result = MapValidator.Validate(map);

        MapValidationFailure failure = Assert.Single(result.Failures);
        Assert.Equal("BOARD_ISOLATED", failure.Code);
        Assert.Contains(lonely.Origin.ToNotation(), failure.Message, StringComparison.Ordinal);
        Assert.Contains("9×9", failure.Message, StringComparison.Ordinal);
        Assert.Equal([lonely.Origin], failure.Coords.AsEnumerable());
    }

    [Fact]
    public void 场景里有可落子格()
    {
        // Scenario：棋盘之外有一格既不属于任何通道也不是障碍的可落子格 → 拒绝并指出该格坐标。
        var stray = new Coord(33, 22);
        MapData map = BoardMapFixtures.Build("test-board-stray", 36, 25, Boards, [.. Corridors, stray]);

        MapValidationResult result = MapValidator.Validate(map);

        MapValidationFailure failure = Assert.Single(result.Failures);
        Assert.Equal("SCENERY_CELL_PLAYABLE", failure.Code);
        Assert.Equal([stray], failure.Coords.AsEnumerable());
        Assert.Contains(stray.ToNotation(), failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void 合法棋盘图()
    {
        // Scenario：5 块出生棋盘 + 2 块公共棋盘、通道与规模全部合规 → 接受，报告项给出每个出生区到三类目标的距离。
        MapData map = BoardMapFixtures.FourPlayerMap();
        Assert.Equal(326, map.PlayableCount);                       // 5×25 + 2×81 + 9 + 5×6（测试内独立算式）
        Assert.Equal((5, 7, 9), (map.BirthZones.Length, map.Boards.Length, map.RelicCells.Count));

        MapValidationResult result = MapValidator.Validate(map);

        Assert.True(result.IsValid, result.ToString());
        Assert.Equal(3, result.Reports.Length);
        Assert.All(result.Reports, r => Assert.Equal("BIRTH_ZONE_DISTANCE_REPORT", r.Code));
        Assert.All(result.Reports, r => Assert.Equal(MapFindingSeverity.Report, r.Severity));

        // 到中央入口 N13 的沿气边距离（手算，通道 3 格宽：横向覆盖 y = 12、竖向覆盖 x = 12 / 24）：
        // 0 / 2 / 4 号出生棋盘贴着公共棋盘 A，各 1 + 2 + 4 = 7；1 / 3 号贴着公共棋盘 B，要先横穿 B 与 A–B 通道：1 号 = 2 + 9 + 3 + 5 = 19，3 号 = 7 + 12 = 19。
        MapValidationFailure entrance = Assert.Single(result.Reports, r => r.Message.Contains("中央入口", StringComparison.Ordinal));
        Assert.Contains("= 7", entrance.Message, StringComparison.Ordinal);
        Assert.Contains("= 19", entrance.Message, StringComparison.Ordinal);
        Assert.Contains("极差 12", entrance.Message, StringComparison.Ordinal);
        Assert.Equal(new int?[] { 7, 19, 7, 19, 7 }, MapValidator.DistanceTable(map)[1].Distances.AsEnumerable());

        GameBoard.Load(map);   // 不抛
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void 棋盘档只定4人_请求2或3人报不支持(int players)
    {
        // 预算表「仅 4 人；请求 2 / 3 人时 MUST 报"不支持"」。
        MapData map = BoardMapFixtures.FourPlayerMap() with { MaxPlayers = players };

        MapValidationResult result = MapValidator.Validate(map);

        MapValidationFailure failure = Assert.Single(result.Failures, f => f.Code == "UNSUPPORTED_PLAYER_COUNT");
        Assert.Contains("棋盘档", failure.Message, StringComparison.Ordinal);
        Assert.Contains("4 人", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 规模预算_可落子格与信物格与出生区数()
    {
        // 预算表第 8 条：可落子 250–1000、出生区 5、信物 7–20。
        // 可落子不足：去掉公共棋盘 B 及其两块出生棋盘 → 3×25 + 81 + 通道 3×6 = 174 格、出生区 3 个。
        ImmutableArray<BoardPlate> three = [Boards[0], Boards[2], Boards[4], BoardMapFixtures.FourPublicA];
        ImmutableArray<Coord> corridors = [.. FrontierFixtures.Rect(6, 11, 2, 3), .. FrontierFixtures.Rect(11, 17, 3, 2), .. FrontierFixtures.Rect(11, 6, 3, 2)];
        MapData small = BoardMapFixtures.Build("test-board-budget", 36, 25, three, corridors);
        small = small with { RelicCells = small.RelicCells.Where(kv => small.IsPlayable(kv.Key)).ToImmutableDictionary() };
        Assert.Equal(174, small.PlayableCount);

        string[] codes = Codes(MapValidator.Validate(small));

        Assert.Contains("PLAYABLE_COUNT_OUT_OF_RANGE", codes);
        Assert.Contains("RELIC_COUNT_OUT_OF_RANGE", codes);          // 5 个，少于 7
        Assert.Contains("BIRTH_ZONE_COUNT_OUT_OF_RANGE", codes);     // 3 个，应为 5
        MapValidationFailure zones = Assert.Single(MapValidator.Validate(small).Failures, f => f.Code == "BIRTH_ZONE_COUNT_OUT_OF_RANGE");
        Assert.Contains("5", zones.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 棋盘两两至少间隔2格()
    {
        // 校验第 3 条。把 3 号出生棋盘（B 的北侧）向西挪到 x = 16 并加宽到 7（x 16–22，与 B 的 x 20–28 重叠 3 列、3 格宽通道接得上）：
        // 与 2 号出生棋盘（x 10–14）只隔 1 列。
        ImmutableArray<BoardPlate> boards = Boards.SetItem(3, new BoardPlate(new Coord(16, 19), 7, 5, BoardPlateKind.Birth));
        ImmutableArray<Coord> corridors = [.. Corridors.Where(c => !(c.X >= 24 && c.X <= 26 && c.Y >= 17)), .. FrontierFixtures.Rect(20, 17, 3, 2)];
        MapData map = BoardMapFixtures.Build("test-board-gap", 36, 25, boards, corridors);
        map = map with { RelicCells = map.RelicCells.Remove(new Coord(24, 21)).Add(new Coord(18, 21), new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth)) };

        MapValidationResult result = MapValidator.Validate(map);

        MapValidationFailure failure = Assert.Single(result.Failures);
        Assert.Equal("BOARD_TOO_CLOSE", failure.Code);
        Assert.Contains("2", failure.Message, StringComparison.Ordinal);
        Assert.Equal([new Coord(10, 19), new Coord(16, 19)], failure.Coords.AsEnumerable());
    }

    [Theory]
    [InlineData(1, false)]   // 长 1：棋盘间隔也只有 1，"间隔 ≥ 2"同时报出
    [InlineData(2, true)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    public void 通道长度在2到4之间(int length, bool accepted)
    {
        // 校验第 4 条：通道 2–4 格长。把 2 号出生棋盘（公共棋盘 A 的北侧）上下挪动，使它与 A 之间的通道（x = 11–13，自 y = 17 起）长为 length。
        ImmutableArray<BoardPlate> boards = Boards.SetItem(2, new BoardPlate(new Coord(10, 17 + length), 5, 5, BoardPlateKind.Birth));
        ImmutableArray<Coord> corridors = [.. Corridors.Where(c => !(c.X >= 11 && c.X <= 13 && c.Y > 16)), .. FrontierFixtures.Rect(11, 17, 3, length)];
        MapData map = BoardMapFixtures.Build("test-board-length", 36, 30, boards, corridors);
        map = map with
        {
            RelicCells = map.RelicCells.Remove(new Coord(12, 21))
                .Add(new Coord(12, 17 + length + 2), new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth)),
        };

        MapValidationResult result = MapValidator.Validate(map);

        if (accepted)
        {
            Assert.Empty(Codes(result));
            return;
        }

        MapValidationFailure failure = Assert.Single(result.Failures, f => f.Code == "CORRIDOR_LENGTH_OUT_OF_RANGE");
        Assert.Contains("2–4", failure.Message, StringComparison.Ordinal);
        Assert.Equal(3 * length, failure.Coords.Length);
        Assert.Equal(length == 1 ? ["BOARD_TOO_CLOSE", "CORRIDOR_LENGTH_OUT_OF_RANGE"] : ["CORRIDOR_LENGTH_OUT_OF_RANGE"], Codes(result).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void 通道必须恰与两块不同的棋盘相邻()
    {
        // 校验第 4 条。死胡同：从 1 号出生棋盘北边伸出一条 3 格宽、2 格长的通道，另一端不接棋盘。
        Coord[] deadEnd = [.. FrontierFixtures.Rect(32, 15, 3, 2)];
        MapData map = BoardMapFixtures.Build("test-board-dead-end", 36, 25, Boards, [.. Corridors, .. deadEnd]);

        MapValidationResult result = MapValidator.Validate(map);

        MapValidationFailure failure = Assert.Single(result.Failures);
        Assert.Equal("CORRIDOR_ENDS_INVALID", failure.Code);
        Assert.Equal(deadEnd, failure.Coords.AsEnumerable());
    }

    [Fact]
    public void 通道不是直条()
    {
        // 校验第 4 条：通道是直条。A–B 通道（y 11–13）多出一格（R15，在通道上方）→ 形状不是填满的矩形。
        MapData map = BoardMapFixtures.Build("test-board-bent", 36, 25, Boards, [.. Corridors, new Coord(17, 14)]);

        MapValidationResult result = MapValidator.Validate(map);

        MapValidationFailure failure = Assert.Single(result.Failures);
        Assert.Equal("CORRIDOR_NOT_STRAIGHT", failure.Code);
        Assert.Equal(10, failure.Coords.Length);
    }

    [Fact]
    public void 信物格必须在棋盘内()
    {
        // 校验第 7 条：通道格不得是信物格。把公共信物 (10, 14) 挪到 A–B 通道的中间格 (18, 12)。
        MapData map = BoardMapFixtures.FourPlayerMap();
        var onCorridor = new Coord(18, 12);
        map = map with { RelicCells = map.RelicCells.Remove(new Coord(10, 14)).Add(onCorridor, new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard)) };

        MapValidationResult result = MapValidator.Validate(map);

        MapValidationFailure failure = Assert.Single(result.Failures);
        Assert.Equal("RELIC_OUTSIDE_BOARD", failure.Code);
        Assert.Equal([onCorridor], failure.Coords.AsEnumerable());
    }

    [Fact]
    public void 棋盘档地图不得含深水_高度_非草地地表与栅栏()
    {
        // 校验第 2 / 4 / 6 条：全图同一高度、场景只用障碍（design D3）。各改一处，逐项报出。
        MapData map = BoardMapFixtures.FourPlayerMap();
        var scenery = new Coord(0, 0);
        var inBoard = new Coord(21, 9);
        var corridor = new Coord(18, 12);

        // 场景格由障碍改成深水：不属于棋盘与通道的格子必须全部是障碍格，且棋盘档不得含深水。
        MapData water = map with
        {
            Obstacles = map.Obstacles.Remove(scenery),
            TerrainData = TestMaps.Terrain(surfaces: [(scenery.ToNotation(), Surface.DeepWater)]),
        };
        MapValidationFailure deep = Assert.Single(MapValidator.Validate(water).Failures);
        Assert.Equal(("BOARD_MAP_DEEP_WATER", scenery), (deep.Code, deep.Coords[0]));

        // 棋盘内一格 h=1。
        MapData raised = map with { TerrainData = TestMaps.Terrain(heights: [(inBoard.ToNotation(), 1)]) };
        MapValidationFailure high = Assert.Single(MapValidator.Validate(raised).Failures);
        Assert.Equal(("BOARD_CELL_NOT_FLAT_GRASS", inBoard), (high.Code, high.Coords[0]));

        // 棋盘内一格林地。
        MapData forest = map with { TerrainData = TestMaps.Terrain(surfaces: [(inBoard.ToNotation(), Surface.Forest)]) };
        MapValidationFailure wood = Assert.Single(MapValidator.Validate(forest).Failures);
        Assert.Equal(("BOARD_CELL_NOT_FLAT_GRASS", inBoard), (wood.Code, wood.Coords[0]));

        // 通道格 h=1。
        MapData ramp = map with { TerrainData = TestMaps.Terrain(heights: [(corridor.ToNotation(), 1)]) };
        MapValidationFailure slope = Assert.Single(MapValidator.Validate(ramp).Failures);
        Assert.Equal(("CORRIDOR_CELL_NOT_FLAT", corridor), (slope.Code, slope.Coords[0]));

        // 棋盘边界上的栅栏（A 的东边界与通道之间）。
        MapData fenced = map with { TerrainData = TestMaps.Terrain(fences: [(new Coord(16, 12).ToNotation(), new Coord(17, 12).ToNotation())]) };
        Assert.Contains(MapValidator.Validate(fenced).Failures, f => f.Code == "BOARD_MAP_FENCE");
    }

    [Fact]
    public void 出生棋盘与出生区必须一一对应()
    {
        // 「棋盘清单」：第 i 块出生棋盘的格子集合恰等于出生区 i。把出生区 0 与 1 对调 → 次序对不上。
        MapData map = BoardMapFixtures.FourPlayerMap();
        MapData swapped = map with { BirthZones = map.BirthZones.SetItem(0, map.BirthZones[1]).SetItem(1, map.BirthZones[0]) };

        MapValidationResult result = MapValidator.Validate(swapped);

        Assert.Equal(["BIRTH_BOARD_MISMATCH", "BIRTH_BOARD_MISMATCH"], Codes(result));
    }

    [Fact]
    public void 棋盘档的尺寸与清单要求()
    {
        // 声明表的棋盘档一行（第 8 条）：列数与行数各在 20–50、棋盘清单不得为空。夹具为 36×25。
        MapData map = BoardMapFixtures.FourPlayerMap();

        // 改小之后图外的格子越界，结构校验另报若干条；尺寸不合即到此为止，不再往下跑——尺寸那一条是最后一条。
        MapValidationResult tooNarrow = MapValidator.Validate(map with { Width = 19 });
        MapValidationFailure narrow = Assert.Single(tooNarrow.Failures, f => f.Code == "MAP_TOO_NARROW");
        Assert.Equal("MAP_TOO_NARROW", tooNarrow.Failures[^1].Code);
        Assert.Contains("19", narrow.Message, StringComparison.Ordinal);
        Assert.Contains("20", narrow.Message, StringComparison.Ordinal);

        MapValidationResult tooShort = MapValidator.Validate(map with { Height = 19 });
        MapValidationFailure low = Assert.Single(tooShort.Failures, f => f.Code == "MAP_TOO_SHORT");
        Assert.Equal("MAP_TOO_SHORT", tooShort.Failures[^1].Code);
        Assert.Contains("19", low.Message, StringComparison.Ordinal);
        Assert.Contains("20", low.Message, StringComparison.Ordinal);

        MapValidationFailure wide = Assert.Single(MapValidator.Validate(map with { Width = 51 }).Failures);
        Assert.Equal("MAP_TOO_WIDE", wide.Code);
        Assert.Contains("50", wide.Message, StringComparison.Ordinal);

        MapValidationFailure tall = Assert.Single(MapValidator.Validate(map with { Height = 51 }).Failures);
        Assert.Equal("MAP_TOO_TALL", tall.Code);
        Assert.Contains("50", tall.Message, StringComparison.Ordinal);

        // 区间两端都是合法值：宽 35（原先的下限 36 之下）不再因宽度被拒。
        string[] sizeCodes = ["MAP_TOO_WIDE", "MAP_TOO_NARROW", "MAP_TOO_TALL", "MAP_TOO_SHORT"];
        foreach (MapData legal in new[] { map with { Width = 50 }, map with { Height = 50 }, map with { Width = 35 }, map with { Width = 20 }, map with { Height = 20 } })
        {
            Assert.DoesNotContain(MapValidator.Validate(legal).Failures, f => sizeCodes.Contains(f.Code));
        }

        MapValidationFailure empty = Assert.Single(MapValidator.Validate(map with { Boards = [] }).Failures);
        Assert.Equal("BOARDS_REQUIRED", empty.Code);
    }
}
