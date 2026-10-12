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
        MapData bad = LinkMapFixtures.Build("test-link-adjacent", [LinkMapFixtures.Births[0], new(new Coord(7, 2), 5, 5, BoardPlateKind.Birth), .. LinkMapFixtures.Births.Skip(2)]);
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
    public void 未链接战场不相邻()
    {
        // 角对角相触（间隔 0 但几何不相邻）仍是"相邻"：不同战场的棋盘至少隔 1 格；同一战场内（拼接后）的棋盘紧贴不报。
        MapData corner = LinkMapFixtures.Build("test-link-corner", [LinkMapFixtures.Births[0], new(new Coord(7, 7), 5, 5, BoardPlateKind.Birth), .. LinkMapFixtures.Births.Skip(2)]);
        MapValidationFailure failure = Assert.Single(MapValidator.Validate(corner).Failures, f => f.Code == "BOARD_TOO_CLOSE");
        Assert.Contains("未链接的战场不得相邻", failure.Message, StringComparison.Ordinal);
        Assert.Equal(5, Battlefields.Of(corner).Count);

        Assert.DoesNotContain(MapValidator.Validate(LinkMapFixtures.Linked01()).Failures, f => f.Code == "BOARD_TOO_CLOSE");
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

        MapData overlap = LinkMapFixtures.Build("test-link-overlap", [LinkMapFixtures.Births[0], new(new Coord(5, 2), 5, 5, BoardPlateKind.Birth), .. LinkMapFixtures.Births.Skip(2)], mapVersion: 1);
        MapValidationFailure overlapped = Assert.Single(MapValidator.Validate(overlap).Failures, f => f.Code == "BOARD_OVERLAP");
        Assert.Equal(FrontierFixtures.Rect(5, 2, 2, 5).Order(), overlapped.Coords.Order());

        // 可落子格总数预算：5 块出生棋盘之和 125–245；一块 7×7 的拼接图里塞第 6 块棋盘即超出。
        MapData tooMany = LinkMapFixtures.Build("test-link-6", [.. LinkMapFixtures.Births, new(new Coord(42, 2), 7, 7, BoardPlateKind.Birth)]);
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
        foreach (MapData map in new[] { BoardMapFixtures.FourPlayerMap(), MapCatalog.Legacy.Resolve(null), MapCatalog.Legacy.Resolve("gen:12345") })
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
