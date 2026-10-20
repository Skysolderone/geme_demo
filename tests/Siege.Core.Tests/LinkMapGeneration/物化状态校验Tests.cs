using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.LinkMapGeneration;

/// <summary>
/// 规格：openspec/changes/open-map/specs/link-map-generation —— Requirement: 物化状态校验（open-map：恰 1 个连通分量且含全部出生棋盘）；
/// map-definition —— Requirement: 地图规格档（Scenario 规格档读写往返 / 非链接档不可增长 / 规格档不改规则）。
/// 载体 <see cref="LinkMapFixtures.Initial"/> 自 open-map 起为 22×16 的 3+2 紧贴图（棋盘 0 的坐标不变）。
/// </summary>
/// <remarks>
/// 变异验证（段 F 实跑，只跑本类）：
/// M-F7 <c>MapValidator.ValidateSpliced</c> 不报 BATTLEFIELD_COUNT_MISMATCH（条件改恒假）→ 「分量数不为一被拒」红（open-map 1.1 重跑）；
/// M-F8 <c>ValidateSpliced</c> 不查每块出生棋盘的信物格数 → 「每块出生棋盘恰两个信物格」红；
/// M-F9 <c>ValidatePlateGaps</c> 对不同战场也跳过间隔 → 「未链接战场不相邻」红；
/// M-A10 <c>LinkMapGenerator.Link</c> 删去规格档检查 → 「非链接档不可增长」红。
/// </remarks>
public class 物化状态校验Tests
{
    /// <summary>在地图原有地形（夹具的林地）之上再加若干地表 / 栅栏，免得替换地形时把改造目标一起抹掉、引出无关的失败项。</summary>
    internal static TerrainData With(MapData map, (string Cell, Surface Surface)[]? surfaces = null, (string A, string B)[]? fences = null, (string Cell, int Height)[]? heights = null)
    {
        TerrainData extra = TestMaps.Terrain(heights: heights, surfaces: surfaces, fences: fences);
        TerrainData t = map.TerrainData;
        return new TerrainData(t.Heights.SetItems(extra.Heights), t.Surfaces.SetItems(extra.Surfaces), t.Bridges, t.Fences.Union(extra.Fences));
    }

    [Fact]
    public void 分量数不为一被拒()
    {
        // Scenario（open-map）：手写的链接档地图，某块出生棋盘与其余 4 块不相邻 → 校验失败并指出连通分量数为 2；加载即异常中止。
        // 棋盘 4 挪到棋盘 2 北侧隔 2 行（(16, 10)，画布 22×18 仍在区间内）：导出 2 个含出生棋盘的分量。
        MapData bad = LinkMapFixtures.Build("test-link-split", [.. LinkMapFixtures.Births.Take(4), LinkMapFixtures.Births[4] with { Origin = new Coord(16, 10) }]);
        MapValidationResult result = MapValidator.Validate(bad);
        Assert.False(result.IsValid);
        MapValidationFailure failure = Assert.Single(result.Failures, f => f.Code == "BATTLEFIELD_COUNT_MISMATCH");
        Assert.Contains("连通分量为 2 个", failure.Message, StringComparison.Ordinal);
        Assert.Equal(bad.AllCoords().Where(bad.IsPlayable).Order(), failure.Coords.Order());   // 指出全部分量

        MapValidationException ex = Assert.Throws<MapValidationException>(() => GameBoard.Load(bad));
        Assert.Contains("BATTLEFIELD_COUNT_MISMATCH", ex.Message, StringComparison.Ordinal);

        // 合规的紧贴图恰 1 个分量、含全部 5 块。
        MapData initial = LinkMapFixtures.Initial();
        Assert.True(MapValidator.Validate(initial).IsValid, MapValidator.Validate(initial).ToString());
        Assert.Empty(MapValidator.Validate(initial).Reports);   // 链接档没有距离报告项
        Assert.Equal(0, initial.MapVersion);
        Assert.Equal(1, Battlefields.Of(initial).Count);
        Assert.Equal([0, 1, 2, 3, 4], Battlefields.Of(initial).All[0].BirthZones);
    }

    [Theory]
    [InlineData(1ul)]
    [InlineData(7ul)]
    [InlineData(20ul)]
    public void 生成后校验(ulong seed)
    {
        // Scenario（open-map）：由任一种子生成地图 → 布局通过校验，版本号为 0；可落子格总数与画布尺寸都落在校验器声明的区间内（种子 1–20 全部通过见 初始布局Tests）。
        MapData map = LinkMapGenerator.Generate(seed);
        MapValidationResult result = MapValidator.Validate(map);
        Assert.True(result.IsValid, result.ToString());
        Assert.Equal(0, map.MapVersion);
        Assert.Equal(1, Battlefields.Of(map).Count);
    }

    [Fact]
    public void 新地表过多被拒()
    {
        // Scenario（link-map-surfaces D4 / 裁决 E5）：手写的链接档地图，出生棋盘 3（下标 2，P3–U8）的中间带有 5 格荒漠 → 校验失败并指出该棋盘与"至多 4 格新地表"。
        // 6×6 的中间带 7 格，夹具占了两格林地（T4 / T7，可改造目标），其余 5 格（Q7 / R7 / S7 / T5 / T6）全部改荒漠；4 格是边界，接受。
        MapData map = LinkMapFixtures.Initial();
        BoardPlate plate = map.Boards[2];
        Coord[] grass = [.. plate.Cells().Where(c => plate.InMiddleBand(c) && map.SurfaceAt(c) == Surface.Grass).Order()];
        Assert.Equal(["T5", "T6", "Q7", "R7", "S7"], grass.Notations());
        MapData five = map with { TerrainData = With(map, surfaces: [.. grass.Select(c => (c.ToNotation(), Surface.Desert))]) };
        MapValidationResult result = MapValidator.Validate(five);
        MapValidationFailure failure = Assert.Single(result.Failures);
        Assert.Equal("BOARD_SPECIAL_SURFACE", failure.Code);
        Assert.Contains("出生棋盘 P3–U8", failure.Message, StringComparison.Ordinal);
        Assert.Contains("有 5 格", failure.Message, StringComparison.Ordinal);
        Assert.Contains("至多 4 格新地表", failure.Message, StringComparison.Ordinal);
        Assert.Equal(grass, failure.Coords.Order());
        Assert.Throws<MapValidationException>(() => GameBoard.Load(five));

        // 边界：同一块 4 格（去掉 S7）被接受；4 格分属两种地表也照样计数（3 荒漠 + 2 浅滩 = 5 格被拒）。
        MapData four = map with { TerrainData = With(map, surfaces: [.. grass.Take(4).Select(c => (c.ToNotation(), Surface.Desert))]) };
        Assert.True(MapValidator.Validate(four).IsValid, MapValidator.Validate(four).ToString());
        MapData mixed = map with { TerrainData = With(map, surfaces: [.. grass.Select((c, k) => (c.ToNotation(), k < 3 ? Surface.Desert : Surface.Shallows))]) };
        Assert.Contains(MapValidator.Validate(mixed).Failures, f => f.Code == "BOARD_SPECIAL_SURFACE" && f.Message.Contains("有 5 格", StringComparison.Ordinal));
    }

    [Fact]
    public void 接缝没有气边被拒()
    {
        // Scenario（link-map-terrain 3.2 / 裁决 M6）：手写的链接档地图，接缝两侧相邻的格之间全部隔着栅栏或高度差 ≥ 2 → 校验失败并指出该接缝。
        // 载体（open-map：建局即紧贴）：初始图里出生棋盘 1 东边外圈 H 列紧贴出生棋盘 2 西边外圈 J 列（记法跳过 I），H3–H8 / J3–J8 六对。
        // 这样的图同时违反外圈规则（外圈不得有栅栏与高差）——合规生成图上接缝恒有气边，这条是给手写地图与以后改外圈规则时用的底线。
        MapData linked = LinkMapFixtures.Initial();
        Assert.True(MapValidator.Validate(linked).IsValid);
        string[] rows = ["3", "4", "5", "6", "7"];
        MapData blocked = linked with { TerrainData = With(linked, fences: [.. rows.Select(r => ("H" + r, "J" + r))], heights: [("J8", 2)]) };
        MapValidationResult result = MapValidator.Validate(blocked);
        MapValidationFailure seam = Assert.Single(result.Failures, f => f.Code == "LINK_SEAM_NO_LIBERTY");
        Assert.Equal([.. new[] { "3", "4", "5", "6", "7", "8" }.SelectMany(r => new[] { "H" + r, "J" + r })], seam.Coords.Notations());
        Assert.Contains("出生棋盘 C3–H8", seam.Message, StringComparison.Ordinal);
        Assert.Contains("出生棋盘 J3–O8", seam.Message, StringComparison.Ordinal);
        Assert.Contains(result.Failures, f => f.Code == "BOARD_RIM_NOT_FLAT");   // 外圈规则同时报出

        // 反面：留一对没挡（H8–J8 高度差只有 1）→ 接缝有气边，不报。
        MapData oneOpen = linked with { TerrainData = With(linked, fences: [.. rows.Select(r => ("H" + r, "J" + r))], heights: [("J8", 1)]) };
        Assert.DoesNotContain(MapValidator.Validate(oneOpen).Failures, f => f.Code == "LINK_SEAM_NO_LIBERTY");
    }

    [Fact]
    public void 棋盘清单规则逐条报出()
    {
        // retire-legacy-maps（check 补回覆盖）：MapValidator 的棋盘清单规则（ValidatePlateList / ValidatePlateCells / ValidateBirthPlates / ValidateFlatScenery）
        // 是链接档仍在执行的代码，但下面这些失败码此前只在 棋盘档预算与校验Tests（board: 档，整类随旧档删除）里有用例。
        // 这里把同样的断言换到手写链接档图（LinkMapFixtures，open-map 起 22×16 的 3+2 紧贴图、五块 6×6）上，每例只改一处；所守规则见 map-definition「地图静态校验规则」
        // 的棋盘清单段与 link-map-generation「物化状态校验」，规则本身不变。
        MapData map = LinkMapFixtures.Initial();
        Assert.True(MapValidator.Validate(map).IsValid);   // 载体本身合规

        // ① 清单不得为空：报 BOARDS_REQUIRED 并到此为止（不再报棋盘与规模派生失败）。
        MapValidationResult empty = MapValidator.Validate(map with { Boards = [] });
        Assert.Equal("BOARDS_REQUIRED", Assert.Single(empty.Failures).Code);

        // ② 出生棋盘边长 6–8（link-map-terrain 裁决 M9）：一块 9×6（其余四块挪开、保持紧贴连通，规模仍在预算内）→ 只报这一条。
        MapData wide = LinkMapFixtures.Build("test-link-wide",
        [
            new(new Coord(2, 2), 9, 6, BoardPlateKind.Birth, BoardTheme.Plateau),
            new(new Coord(11, 2), 6, 6, BoardPlateKind.Birth, BoardTheme.River),
            new(new Coord(17, 2), 6, 6, BoardPlateKind.Birth, BoardTheme.Forest),
            new(new Coord(2, 8), 6, 6, BoardPlateKind.Birth, BoardTheme.Palisade),
            new(new Coord(11, 8), 6, 6, BoardPlateKind.Birth, BoardTheme.Mixed),
        ]);
        MapValidationFailure size = Assert.Single(MapValidator.Validate(wide).Failures);
        Assert.Equal("BOARD_SIZE_OUT_OF_RANGE", size.Code);
        Assert.Contains("6–8", size.Message, StringComparison.Ordinal);
        Assert.Equal([new Coord(2, 2)], size.Coords);

        // ③ link-map-terrain D6：删除"棋盘内不得有障碍"——中间带的岩石（G5）合规；外圈的岩石（C5）与核心区的岩石（E5）各由外圈 / 核心区规则报出并指出该格。
        Assert.True(MapValidator.Validate(map with { Obstacles = map.Obstacles.Add(TestMaps.At("G5")) }).IsValid);
        MapValidationFailure rimRock = Assert.Single(
            MapValidator.Validate(map with { Obstacles = map.Obstacles.Add(TestMaps.At("C5")) }).Failures, f => f.Code == "BOARD_RIM_NOT_FLAT");
        Assert.Equal([TestMaps.At("C5")], rimRock.Coords);
        MapValidationFailure coreRock = Assert.Single(
            MapValidator.Validate(map with { Obstacles = map.Obstacles.Add(TestMaps.At("E5")) }).Failures, f => f.Code == "BIRTH_CORE_NOT_FLAT");
        Assert.Equal([TestMaps.At("E5")], coreRock.Coords);

        // ④ 棋盘之外不得有深水（场景格 A1）；⑤ 核心区内部的边上不得有栅栏（E5–E6），核心区与中间带之间的边可以有（F6–G6，裁决 Q4）。
        MapValidationFailure water = Assert.Single(
            MapValidator.Validate(map with { TerrainData = With(map, surfaces: [("A1", Surface.DeepWater)]) }).Failures, f => f.Code == "BOARD_MAP_DEEP_WATER");
        Assert.Equal([TestMaps.At("A1")], water.Coords);
        MapValidationFailure fence = Assert.Single(
            MapValidator.Validate(map with { TerrainData = With(map, fences: [("E5", "E6")]) }).Failures, f => f.Code == "BIRTH_CORE_NOT_FLAT");
        Assert.Equal([TestMaps.At("E5"), TestMaps.At("E6")], fence.Coords.Order().ToArray());
        Assert.True(MapValidator.Validate(map with { TerrainData = With(map, fences: [("F6", "G6")]) }).IsValid);

        // ⑥ 出生棋盘与出生区一一对应：出生区 1 / 2 对调 → 两块棋盘各报一次。
        MapData swapped = map with { BirthZones = [map.BirthZones[1], map.BirthZones[0], .. map.BirthZones.Skip(2)] };
        Assert.Equal(2, MapValidator.Validate(swapped).Failures.Count(f => f.Code == "BIRTH_BOARD_MISMATCH"));

        // ⑦ 信物格必须在棋盘内：把场景格 V5（出生棋盘 2 东侧的留白，记法跳过 I）开放并标成信物格 → 指出该格。
        MapData stray = map with
        {
            Obstacles = map.Obstacles.Remove(TestMaps.At("V5")),
            RelicCells = map.RelicCells.Add(TestMaps.At("V5"), new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard)),
        };
        MapValidationFailure outside = Assert.Single(MapValidator.Validate(stray).Failures, f => f.Code == "RELIC_OUTSIDE_BOARD");
        Assert.Equal([TestMaps.At("V5")], outside.Coords);

        // ⑧ 出生区数须多于人数（声明表 ZonesMustExceedPlayers）：只有 4 块出生棋盘的 4 人图 → 与区间越界同时报出。
        MapData four = LinkMapFixtures.Build("test-link-4", [.. LinkMapFixtures.Births.Take(4)]);
        MapValidationResult fourResult = MapValidator.Validate(four);
        Assert.Contains(fourResult.Failures, f => f.Code == "BIRTH_ZONE_COUNT_NOT_ABOVE_PLAYERS");
        Assert.Contains(fourResult.Failures, f => f.Code == "BIRTH_ZONE_COUNT_OUT_OF_RANGE");
    }

    [Fact]
    public void 每块出生棋盘恰两个信物格()
    {
        MapData initial = LinkMapFixtures.Initial();
        Coord one = initial.RelicCells.Keys.First(initial.BirthZones[2].Contains);
        MapData fewer = initial with { RelicCells = initial.RelicCells.Remove(one) };
        Assert.Contains(MapValidator.Validate(fewer).Failures, f => f.Code == "BIRTH_BOARD_RELIC_COUNT" && f.Coords.SequenceEqual([initial.Boards[2].Origin]));
        Assert.Contains(MapValidator.Validate(fewer).Failures, f => f.Code == "RELIC_COUNT_OUT_OF_RANGE");

        MapData more = initial with { RelicCells = initial.RelicCells.Add(new Coord(5, 3), new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth)) };
        Assert.Contains(MapValidator.Validate(more).Failures, f => f.Code == "BIRTH_BOARD_RELIC_COUNT" && f.Coords.SequenceEqual([initial.Boards[0].Origin]));
    }

    [Fact]
    public void 可落子格都属于棋盘_场景格不可落子()
    {
        // 棋盘之外的可落子格（不挨任何棋盘）→ SCENERY_CELL_PLAYABLE，且这片格没有出生棋盘 → BATTLEFIELD_WITHOUT_BIRTH_BOARD；棋盘重叠 → BOARD_OVERLAP。
        MapData initial = LinkMapFixtures.Initial();
        ImmutableArray<Coord> stray = [new Coord(3, 0), new Coord(4, 0)];
        MapData loose = initial with { Obstacles = initial.Obstacles.Except(stray) };
        MapValidationResult result = MapValidator.Validate(loose);
        Assert.Equal(stray, Assert.Single(result.Failures, f => f.Code == "SCENERY_CELL_PLAYABLE").Coords);
        Assert.Equal(stray, Assert.Single(result.Failures, f => f.Code == "BATTLEFIELD_WITHOUT_BIRTH_BOARD").Coords);
        Assert.DoesNotContain(result.Failures, f => f.Code == "BATTLEFIELD_COUNT_MISMATCH");

        MapData overlap = LinkMapFixtures.Build("test-link-overlap", [LinkMapFixtures.Births[0], LinkMapFixtures.Births[1] with { Origin = new Coord(6, 2) }, .. LinkMapFixtures.Births.Skip(2)]);
        MapValidationFailure overlapped = Assert.Single(MapValidator.Validate(overlap).Failures, f => f.Code == "BOARD_OVERLAP");
        Assert.Equal(TestMaps.Rect(6, 2, 2, 6).Order(), overlapped.Coords.Order());   // retire-legacy-maps：Rect 由已删除的 FrontierFixtures 原样迁到 TestMaps

        // 出生区数与信物预算：塞第 6 块出生棋盘即超出。
        MapData tooMany = LinkMapFixtures.Build("test-link-6", [.. LinkMapFixtures.Births, new(new Coord(14, 8), 7, 7, BoardPlateKind.Birth)]);
        Assert.Contains(MapValidator.Validate(tooMany).Failures, f => f.Code is "BIRTH_ZONE_COUNT_OUT_OF_RANGE" or "RELIC_COUNT_OUT_OF_RANGE");
    }

    [Fact]
    public void 规格档读写往返()
    {
        // Scenario：把一张链接档地图的初始状态导出为文件再读入 → 规格档仍为链接，棋盘清单与其余字段逐项相等；版本号非零时写出、零时省略。
        MapData initial = LinkMapFixtures.Initial();
        string json = MapFile.ToJson(initial);
        Assert.Contains("\"Profile\": \"Link\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"MapVersion\"", json, StringComparison.Ordinal);
        MapData restored = MapFile.FromJson(json);
        Assert.Equal(MapProfile.Link, restored.Profile);
        Assert.Equal(0, restored.MapVersion);
        Assert.Equal(initial.Boards, restored.Boards);
        Assert.Equal(initial.Obstacles.Order(), restored.Obstacles.Order());
        Assert.Equal(initial.RelicCells.OrderBy(kv => kv.Key), restored.RelicCells.OrderBy(kv => kv.Key));
        Assert.Equal(json, MapFile.ToJson(restored));
    }

    [Fact]
    public void 规格档不改规则()
    {
        // Scenario：在链接档地图上，一条棋串的全部气边邻格均被敌子占据 → 无气，判定与标准档相同。
        GameBoard board = GameBoard.Load(LinkMapFixtures.Initial());
        var a = new PlayerId(0);
        var b = new PlayerId(1);
        var center = new Coord(4, 4);   // 0 号棋盘中心
        board.Place(center, a, PieceType.Basic);
        foreach (Coord n in board.LibertyNeighbors(center))
        {
            board.Place(n, b, PieceType.Basic);
        }

        Group group = board.GroupAt(center)!;
        Assert.Empty(board.LibertiesOf(group));
        Assert.True(board.IsCaptured(group));

        // 棋盘角上的棋子只有 2 口气（棋盘外是障碍，与画布边缘同义）。
        GameBoard corner = GameBoard.Load(LinkMapFixtures.Initial());
        corner.Place(new Coord(2, 2), a, PieceType.Basic);
        Assert.Equal(2, corner.LibertiesOf(corner.GroupAt(new Coord(2, 2))!).Length);
    }
}
