using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.LinkMapGeneration;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/link-map-generation —— Requirement: 物化状态校验（段 F 拼接版）；
/// map-definition —— Requirement: 地图规格档（Scenario 规格档读写往返 / 非链接档不可增长 / 规格档不改规则）。
/// </summary>
/// <remarks>
/// 变异验证（段 F 实跑，只跑本类）：
/// M-F7 <c>MapValidator.ValidateSpliced</c> 不报 BATTLEFIELD_COUNT_MISMATCH（条件改恒假）→ 「校验失败中止」红；
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
    public void 链接后校验()
    {
        // Scenario：任一链接执行完毕 → 当前布局通过校验，版本号加 1；手写的"链接 0 与 1 之后"状态同样通过。
        MapData initial = LinkMapFixtures.Initial();
        Assert.True(MapValidator.Validate(initial).IsValid, MapValidator.Validate(initial).ToString());
        Assert.Empty(MapValidator.Validate(initial).Reports);   // 链接档没有距离报告项

        MapData linked = LinkMapFixtures.Linked01();
        MapValidationResult result = MapValidator.Validate(linked);
        Assert.True(result.IsValid, result.ToString());
        Assert.Equal(1, linked.MapVersion);
        Assert.Equal(4, Battlefields.Of(linked).Count);
        Assert.Equal([0, 1], Battlefields.Of(linked).ById(0)!.BirthZones);

        // 生成器的拼接：版本号 +1、校验通过；手写的拼接态与生成器对同一初始图拼出来的逐格相同。
        MapData generated = LinkMapGenerator.Generate(4);
        LinkResult link = LinkMapGenerator.Link(generated, 1, 2, LinkSide.East);
        Assert.Equal(1, link.Map.MapVersion);
        Assert.True(MapValidator.Validate(link.Map).IsValid);
        Assert.Equal(MapFile.ToJson(linked), MapFile.ToJson(LinkMapGenerator.Link(initial, 0, 1, LinkSide.East).Map));
    }

    [Fact]
    public void 校验失败中止()
    {
        // Scenario：拼接结果让两个未链接的战场相邻 → 校验异常指出这两个战场，对局中止。
        // 0 号与 1 号边对边紧贴，但版本号仍为 0（没有任何链接）：导出 4 个战场 ≠ 5 − 0。
        MapData bad = LinkMapFixtures.Build("test-link-adjacent", [LinkMapFixtures.Births[0], LinkMapFixtures.Births[1] with { Origin = new Coord(8, 2) }, .. LinkMapFixtures.Births.Skip(2)]);
        MapValidationResult result = MapValidator.Validate(bad);
        Assert.False(result.IsValid);
        MapValidationFailure failure = Assert.Single(result.Failures, f => f.Code == "BATTLEFIELD_COUNT_MISMATCH");
        Assert.Contains("相邻", failure.Message, StringComparison.Ordinal);
        Assert.Equal(bad.Boards[0].Cells().Concat(bad.Boards[1].Cells()).Order(), failure.Coords.Order());   // 指出这两个战场

        MapValidationException ex = Assert.Throws<MapValidationException>(() => GameBoard.Load(bad));
        Assert.Contains("BATTLEFIELD_COUNT_MISMATCH", ex.Message, StringComparison.Ordinal);

        // 反向：版本号说有一次链接，棋盘却仍各自隔绝 → 同样拒绝。
        MapData notLinked = LinkMapFixtures.Initial() with { MapVersion = 1 };
        Assert.Contains(MapValidator.Validate(notLinked).Failures, f => f.Code == "BATTLEFIELD_COUNT_MISMATCH" && f.Message.Contains("没有沿接缝连通", StringComparison.Ordinal));
    }

    [Fact]
    public void 接缝没有气边被拒()
    {
        // Scenario（link-map-terrain 3.2 / 裁决 M6）：手写的链接档地图，接缝两侧相邻的格之间全部隔着栅栏或高度差 ≥ 2 → 校验失败并指出该接缝。
        // 载体：手写的"链接 0 与 1 之后"（出生棋盘 1 东边外圈 H 列紧贴出生棋盘 2 西边外圈 J 列，H3–H8 / J3–J8 六对）。
        // 这样的图同时违反外圈规则（外圈不得有栅栏与高差）——合规生成图上接缝恒有气边，这条是给手写地图与以后改外圈规则时用的底线。
        MapData linked = LinkMapFixtures.Linked01();
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
    public void 未链接战场不相邻()
    {
        // 角对角相触（间隔 0 但几何不相邻）仍是"相邻"：不同战场的棋盘至少隔 1 格；同一战场内（拼接后）的棋盘紧贴不报。
        MapData corner = LinkMapFixtures.Build("test-link-corner", [LinkMapFixtures.Births[0], LinkMapFixtures.Births[1] with { Origin = new Coord(8, 8) }, .. LinkMapFixtures.Births.Skip(2)]);
        MapValidationFailure failure = Assert.Single(MapValidator.Validate(corner).Failures, f => f.Code == "BOARD_TOO_CLOSE");
        Assert.Contains("未链接的战场不得相邻", failure.Message, StringComparison.Ordinal);
        Assert.Equal(5, Battlefields.Of(corner).Count);

        Assert.DoesNotContain(MapValidator.Validate(LinkMapFixtures.Linked01()).Failures, f => f.Code == "BOARD_TOO_CLOSE");
    }

    [Fact]
    public void 棋盘清单规则逐条报出()
    {
        // retire-legacy-maps（check 补回覆盖）：MapValidator 的棋盘清单规则（ValidatePlateList / ValidatePlateCells / ValidateBirthPlates / ValidateFlatScenery）
        // 是链接档仍在执行的代码，但下面这些失败码此前只在 棋盘档预算与校验Tests（board: 档，整类随旧档删除）里有用例。
        // 这里把同样的断言换到手写链接档图（LinkMapFixtures，link-map-terrain 起 46×10、五块 6×6）上，每例只改一处；所守规则见 map-definition「地图静态校验规则」
        // 的棋盘清单段与 link-map-generation「物化状态校验」，规则本身不变。
        MapData map = LinkMapFixtures.Initial();
        Assert.True(MapValidator.Validate(map).IsValid);   // 载体本身合规

        // ① 清单不得为空：报 BOARDS_REQUIRED 并到此为止（不再报棋盘与规模派生失败）。
        MapValidationResult empty = MapValidator.Validate(map with { Boards = [] });
        Assert.Equal("BOARDS_REQUIRED", Assert.Single(empty.Failures).Code);

        // ② 出生棋盘边长 6–8（link-map-terrain 裁决 M9）：一块 9×6（其余四块挪开、保持间隔，规模仍在预算内）→ 只报这一条。
        MapData wide = LinkMapFixtures.Build("test-link-wide",
        [
            new(new Coord(2, 2), 9, 6, BoardPlateKind.Birth, BoardTheme.Plateau),
            new(new Coord(14, 2), 6, 6, BoardPlateKind.Birth, BoardTheme.River),
            new(new Coord(23, 2), 6, 6, BoardPlateKind.Birth, BoardTheme.Forest),
            new(new Coord(32, 2), 6, 6, BoardPlateKind.Birth, BoardTheme.Palisade),
            new(new Coord(41, 2), 6, 6, BoardPlateKind.Birth, BoardTheme.Mixed),
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

        // ⑦ 信物格必须在棋盘内：把场景格 J5（出生棋盘 1、2 之间）开放并标成信物格 → 指出该格。
        MapData stray = map with
        {
            Obstacles = map.Obstacles.Remove(TestMaps.At("J5")),
            RelicCells = map.RelicCells.Add(TestMaps.At("J5"), new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard)),
        };
        MapValidationFailure outside = Assert.Single(MapValidator.Validate(stray).Failures, f => f.Code == "RELIC_OUTSIDE_BOARD");
        Assert.Equal([TestMaps.At("J5")], outside.Coords);

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

        MapData overlap = LinkMapFixtures.Build("test-link-overlap", [LinkMapFixtures.Births[0], LinkMapFixtures.Births[1] with { Origin = new Coord(6, 2) }, .. LinkMapFixtures.Births.Skip(2)], mapVersion: 1);
        MapValidationFailure overlapped = Assert.Single(MapValidator.Validate(overlap).Failures, f => f.Code == "BOARD_OVERLAP");
        Assert.Equal(TestMaps.Rect(6, 2, 2, 6).Order(), overlapped.Coords.Order());   // retire-legacy-maps：Rect 由已删除的 FrontierFixtures 原样迁到 TestMaps

        // 出生区数与信物预算：塞第 6 块出生棋盘即超出。
        MapData tooMany = LinkMapFixtures.Build("test-link-6", [.. LinkMapFixtures.Births, new(new Coord(47, 2), 7, 7, BoardPlateKind.Birth)]);
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

        MapData linked = LinkMapFixtures.Linked01();
        string linkedJson = MapFile.ToJson(linked);
        Assert.Contains("\"MapVersion\": 1", linkedJson, StringComparison.Ordinal);
        MapData linkedBack = MapFile.FromJson(linkedJson);
        Assert.Equal(1, linkedBack.MapVersion);
        Assert.Equal(linked.Boards, linkedBack.Boards);
        Assert.Equal(linkedJson, MapFile.ToJson(linkedBack));

        // 负的版本号拒绝读入；未知规格档的报错文案列出链接档。
        Assert.Throws<FormatException>(() => MapFile.FromJson(linkedJson.Replace("\"MapVersion\": 1", "\"MapVersion\": -1", StringComparison.Ordinal)));
        FormatException profile = Assert.Throws<FormatException>(() => MapFile.FromJson(json.Replace("\"Profile\": \"Link\"", "\"Profile\": 9", StringComparison.Ordinal)));
        Assert.Contains("Link", profile.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 非链接档不可增长()
    {
        // Scenario：对一张棋盘档地图尝试执行链接 → 系统拒绝并指出该规格档不允许增长；标准档、边疆档同理。
        // retire-legacy-maps：棋盘图 / v5 / gen: 载体随旧地图删除，改为手写 9×9 夹具；4.1 起枚举只剩 Link，非链接档改用已移除旧档的原数值 0 / 1 / 2（断言不变）。
        foreach (MapData map in new[] { 0, 1, 2 }.Select(v => TestMaps.NineByNine() with { Profile = (MapProfile)v }))
        {
            SiegeRuleException ex = Assert.Throws<SiegeRuleException>(() => LinkMapGenerator.Link(map, 0, 1, LinkSide.East));
            Assert.Contains("不允许增长", ex.Message, StringComparison.Ordinal);
        }

        // 链接档但标识不存在 / 相同 → ArgumentException。
        MapData link = LinkMapFixtures.Initial();
        Assert.Throws<ArgumentException>(() => LinkMapGenerator.Link(link, 0, 0, LinkSide.East));
        Assert.Throws<ArgumentException>(() => LinkMapGenerator.Link(link, 0, 7, LinkSide.North));
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
