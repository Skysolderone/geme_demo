using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.LinkMapGeneration;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/link-map-generation —— Requirement: 物化状态校验；
/// map-definition —— Requirement: 地图规格档（Scenario 规格档读写往返 / 非链接档不可增长 / 规格档不改规则）。
/// </summary>
/// <remarks>
/// 变异验证（段 A 实跑，只跑本类）：
/// M-A8 MapValidator 链接行 CorridorWidth 改 (2, 4) → 「校验失败中止」红；
/// M-A9 MapValidator.ValidateComponentsHaveBirthBoards 不加失败项 → 「无出生棋盘的分量即校验失败」红；
/// M-A10 LinkMapGenerator.Link 删去规格档检查 → 「非链接档不可增长」红。
/// </remarks>
public class 物化状态校验Tests
{
    [Fact]
    public void 链接后校验()
    {
        // Scenario：任一链接执行完毕 → 当前物化状态通过校验，版本号加 1；手写的"链接 0 与 1 之后"状态同样通过。
        MapData initial = LinkMapFixtures.Initial();
        Assert.True(MapValidator.Validate(initial).IsValid, MapValidator.Validate(initial).ToString());
        Assert.Empty(MapValidator.Validate(initial).Reports);   // 链接档没有距离报告项

        MapData linked = LinkMapFixtures.Linked01();
        MapValidationResult result = MapValidator.Validate(linked);
        Assert.True(result.IsValid, result.ToString());
        Assert.Equal(1, linked.MapVersion);
        Assert.Equal(4, Battlefields.Of(linked).Count);

        // 生成器的链接：版本号 +1、校验通过。
        MapData generated = LinkMapGenerator.Generate(4);
        LinkResult link = LinkMapGenerator.Link(generated, 1, 2);
        Assert.Equal(1, link.Map.MapVersion);
        Assert.True(MapValidator.Validate(link.Map).IsValid);
    }

    [Fact]
    public void 校验失败中止()
    {
        // Scenario：生成器产出一条宽度为 2 的通道 → 校验异常指出该通道。
        ImmutableArray<Coord> narrow = [.. FrontierFixtures.Rect(5, 0, 3, 2), .. FrontierFixtures.Rect(17, 0, 3, 3)];
        MapData bad = LinkMapFixtures.Build(
            "test-link-5", [.. LinkMapFixtures.Births, LinkMapFixtures.PublicBetween01], narrow,
            [(new Coord(12, 4), BudgetTier.High), (new Coord(9, 7), BudgetTier.Standard)], mapVersion: 1);
        MapValidationResult result = MapValidator.Validate(bad);
        Assert.False(result.IsValid);
        MapValidationFailure failure = Assert.Single(result.Failures, f => f.Code == "CORRIDOR_TOO_NARROW");
        Assert.Equal(FrontierFixtures.Rect(5, 0, 3, 2).Order(), failure.Coords.Order());

        MapValidationException ex = Assert.Throws<MapValidationException>(() => GameBoard.Load(bad));
        Assert.Contains("CORRIDOR_TOO_NARROW", ex.Message, StringComparison.Ordinal);

        // 通道过宽（5）同样拒绝；长通道（链接档不限长度）不拒绝。
        ImmutableArray<Coord> wide = [.. FrontierFixtures.Rect(5, 0, 3, 5), .. FrontierFixtures.Rect(17, 0, 3, 3)];
        Assert.Contains(MapValidator.Validate(bad with { Obstacles = LinkMapFixtures.Build("t", [.. LinkMapFixtures.Births, LinkMapFixtures.PublicBetween01], wide, [(new Coord(12, 4), BudgetTier.High), (new Coord(9, 7), BudgetTier.Standard)]).Obstacles }).Failures, f => f.Code == "CORRIDOR_TOO_WIDE");
    }

    [Fact]
    public void 无出生棋盘的分量即校验失败()
    {
        // 「战场的定义」：无出生棋盘的分量不应出现在链接档——孤立的公共棋盘（没接任何出生棋盘）是生成器缺陷。
        var orphan = new BoardPlate(new Coord(30, 12), 9, 9, BoardPlateKind.Public);
        MapData bad = LinkMapFixtures.Build(
            "test-link-5", [.. LinkMapFixtures.Births, orphan], [],
            [(new Coord(34, 16), BudgetTier.High), (new Coord(31, 13), BudgetTier.Standard)], mapVersion: 1);
        MapValidationResult result = MapValidator.Validate(bad);
        MapValidationFailure failure = Assert.Single(result.Failures, f => f.Code == "BATTLEFIELD_WITHOUT_BIRTH_BOARD");
        Assert.Equal(orphan.Cells().Order(), failure.Coords);
        Assert.DoesNotContain(result.Failures, f => f.Code == "BOARD_ISOLATED");   // 链接档不要求全部棋盘连成一片

        // 对照：棋盘档对同样的孤立棋盘报 BOARD_ISOLATED（连通性按档分流只在声明表里）。
        MapData asBoard = bad with { Profile = MapProfile.Board, ChokePoints = [new Coord(2, 2)], Width = 45, Height = 25 };
        Assert.Contains(MapValidator.Validate(asBoard).Failures, f => f.Code == "BOARD_ISOLATED");
    }

    [Fact]
    public void 可落子格都属于棋盘或通道_场景格不可落子()
    {
        // 棋盘之外、不挨任何棋盘的可落子格 → SCENERY_CELL_PLAYABLE；通道格是信物格 → RELIC_OUTSIDE_BOARD。
        MapData stray = LinkMapFixtures.Initial() with { Obstacles = LinkMapFixtures.Initial().Obstacles.Remove(new Coord(30, 12)) };
        Assert.Contains(MapValidator.Validate(stray).Failures, f => f.Code == "SCENERY_CELL_PLAYABLE");

        MapData linked = LinkMapFixtures.Linked01();
        MapData relicInCorridor = linked with { RelicCells = linked.RelicCells.Add(new Coord(6, 1), new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard)) };
        Assert.Contains(MapValidator.Validate(relicInCorridor).Failures, f => f.Code == "RELIC_OUTSIDE_BOARD");

        // 信物格数预算：全图 5–14（出生棋盘各 1 + 至多三块公共棋盘各 3）；超出上限即报。
        MapData tooMany = linked with
        {
            RelicCells = linked.RelicCells.AddRange(Enumerable.Range(0, 9).Select(i => new KeyValuePair<Coord, RelicCellSpec>(new Coord(9 + i % 7, 1 + i / 7), new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard)))),
        };
        Assert.Contains(MapValidator.Validate(tooMany).Failures, f => f.Code == "RELIC_COUNT_OUT_OF_RANGE");
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
            SiegeRuleException ex = Assert.Throws<SiegeRuleException>(() => LinkMapGenerator.Link(map, 0, 1, 1));
            Assert.Contains("不允许增长", ex.Message, StringComparison.Ordinal);
        }

        // 链接档但标识不存在 / 相同 → ArgumentException。
        MapData link = LinkMapFixtures.Initial();
        Assert.Throws<ArgumentException>(() => LinkMapGenerator.Link(link, 0, 0, 1));
        Assert.Throws<ArgumentException>(() => LinkMapGenerator.Link(link, 0, 7, 1));
    }

    [Fact]
    public void 规格档不改规则()
    {
        // Scenario：在链接档地图上，一条棋串的全部气边邻格均被敌子占据 → 无气，判定与标准档相同。
        GameBoard board = GameBoard.Load(LinkMapFixtures.Initial());
        var a = new PlayerId(0);
        var b = new PlayerId(1);
        var center = new Coord(2, 2);
        board.Place(center, a, PieceType.Basic);
        foreach (Coord n in board.LibertyNeighbors(center))
        {
            board.Place(n, b, PieceType.Basic);
        }

        Group group = board.GroupAt(center)!;
        Assert.Empty(board.LibertiesOf(group));
        Assert.True(board.IsCaptured(group));

        // 角上的棋子只有 2 口气（画布边缘与棋盘边缘同义：棋盘外是障碍）。
        GameBoard corner = GameBoard.Load(LinkMapFixtures.Initial());
        corner.Place(new Coord(0, 0), a, PieceType.Basic);
        Assert.Equal(2, corner.LibertiesOf(corner.GroupAt(new Coord(0, 0))!).Length);
    }
}
