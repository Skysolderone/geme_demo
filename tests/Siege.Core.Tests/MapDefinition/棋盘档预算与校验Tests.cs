using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>
/// 规格：openspec/changes/board-isolated-gen/specs/map-definition —— Requirement: 棋盘档预算与校验，
/// 以及「地图静态校验规则」中棋盘档的写法（不可达只报告、到中央入口通路豁免、不标咽喉）。
/// 数据是手写的最小棋盘档图（<see cref="BoardMapFixtures.FourPlayerMap"/> / <see cref="BoardMapFixtures.TwoPlayerMap"/>），每条用例只改动一处。
/// </summary>
/// <remarks>
/// 变异验证（board-isolated-gen 段 A 实跑，改的都是 MapValidator.cs，只跑本类；红数按 Theory 行计）：
/// V1 放过棋盘外的可落子格（ValidateSceneryCells 的判据改成运行时恒假）→ 红 3：棋盘之间有可落子格、场景里有可落子格、信物格必须在棋盘内；
/// V2 / V3 / V4 棋盘档声明行 Reach 分别改为 (true, false, false) / (false, true, false) / (false, false, true)（不可达即拒绝 / 要求到中央入口的通路 / 要求标注咽喉）
/// → 各红 21（凡以"合规图无拒绝项"为前提的用例全红，含 棋盘档不可达只报告、合法2人棋盘图、棋盘被孤立不再拒绝）；
/// V5 公共棋盘边长下限 7 → 6 → 红 2：公共棋盘小于7乘7（6×9 被接受、9×16 的报文区间变了）；
/// V6 删除 3 人预算行 → 红 3：出生区数不等于人数加一、各人数都有预算(3)、规模预算按人数(3)；
/// V7 尺寸区间下限 15 → 16 → 红 1：棋盘档的尺寸与清单要求；
/// V9 不可达时不出报告项（ValidateDistanceBalance 的报告分支加运行时恒假条件；连同 生成图通过校验 一起跑）→ 红 12（2 人收窄后重跑）：合法2人棋盘图、棋盘档不可达只报告、生成图通过校验 ×10；
/// T2 只改测试不改实现：规模预算按人数 的 3 人与 2 人期望对调 → 红 2。
/// </remarks>
public class 棋盘档预算与校验Tests
{
    private static ImmutableArray<BoardPlate> Boards => [.. BoardMapFixtures.FourBirths, BoardMapFixtures.FourPublicA, BoardMapFixtures.FourPublicB];

    private static string[] Codes(MapValidationResult result) => [.. result.Failures.Select(f => f.Code)];

    [Fact]
    public void 棋盘内有障碍()
    {
        // Scenario：某块 9×9 棋盘的外接矩形内有一格障碍 → 拒绝，指出该棋盘的外接矩形与障碍格坐标。
        // 公共棋盘 A（J9–R17）里挖掉 L11；可落子 286，仍在预算内，不触发别的规则。
        var hole = new Coord(10, 10);
        MapData map = BoardMapFixtures.Build("test-board-hole", 36, 25, Boards, [], hole);
        Assert.Equal(286, map.PlayableCount);

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

    [Theory]
    [InlineData(6, 9, true)]      // Scenario：公共棋盘 6×9 → 拒绝
    [InlineData(7, 9, false)]     // 下限 7 本身合法
    [InlineData(9, 16, true)]     // 上限 15
    [InlineData(9, 15, false)]
    public void 公共棋盘小于7乘7(int width, int height, bool rejected)
    {
        // Scenario：某块公共棋盘为 6×9 → 拒绝并指出该棋盘与公共棋盘的合法边长 7–15。
        // 另加一块 width×height 的公共棋盘（左下角 (31, 17)），与 1 号出生棋盘（y 10–14）隔 2 行；图加宽加高以容纳它。
        var extra = new BoardPlate(new Coord(31, 17), width, height, BoardPlateKind.Public);
        MapData map = BoardMapFixtures.Build("test-board-small", 50, 36, [.. Boards, extra], []);

        MapValidationResult result = MapValidator.Validate(map);

        if (!rejected)
        {
            Assert.Empty(Codes(result));
            return;
        }

        MapValidationFailure failure = Assert.Single(result.Failures);
        Assert.Equal("BOARD_SIZE_OUT_OF_RANGE", failure.Code);
        Assert.Contains($"{width}×{height}", failure.Message, StringComparison.Ordinal);
        Assert.Contains("公共棋盘的合法边长 7–15", failure.Message, StringComparison.Ordinal);
        Assert.Contains(extra.Origin.ToNotation(), failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(8, 6, true)]      // Scenario：出生棋盘 8×6 → 拒绝
    [InlineData(7, 6, false)]
    [InlineData(5, 5, false)]
    public void 出生棋盘越界(int width, int height, bool rejected)
    {
        // Scenario：某块出生棋盘为 8×6 → 拒绝并指出该棋盘与出生棋盘的合法边长 5–7。把 4 号出生棋盘（A 的南侧）换成 width×height，北边仍与 A 隔 2 行。
        var birth = new BoardPlate(new Coord(10, 6 - height), width, height, BoardPlateKind.Birth);
        MapData map = BoardMapFixtures.Build("test-board-birth-size", 36, 25, Boards.SetItem(4, birth), []);
        map = map with { RelicCells = map.RelicCells.Remove(new Coord(12, 3)).Add(new Coord(11, 6 - height + 1), new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth)) };

        MapValidationResult result = MapValidator.Validate(map);

        Assert.Equal(rejected ? ["BOARD_SIZE_OUT_OF_RANGE"] : [], Codes(result));
        if (rejected)
        {
            Assert.Contains("出生棋盘的合法边长 5–7", result.Failures[0].Message, StringComparison.Ordinal);
            Assert.Contains($"{width}×{height}", result.Failures[0].Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void 棋盘之间有可落子格()
    {
        // Scenario：两块棋盘之间有可落子格把它们连起来 → 拒绝并指出格子坐标。
        // A（x 8–16）与 B（x 20–28）之间隔 3 列：第 13 行（y = 12）的 x 17–19 三格可落子，正好把两块棋盘沿四邻接连成一片。
        Coord[] bridge = [new(17, 12), new(18, 12), new(19, 12)];
        MapData map = BoardMapFixtures.Build("test-board-bridge", 36, 25, Boards, [.. bridge]);

        MapValidationResult result = MapValidator.Validate(map);

        MapValidationFailure failure = Assert.Single(result.Failures);
        Assert.Equal("SCENERY_CELL_PLAYABLE", failure.Code);
        Assert.Equal(bridge, failure.Coords.AsEnumerable());
        Assert.Contains("S13", failure.ToString(), StringComparison.Ordinal);
        Assert.Contains("棋盘之间不得相连", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 场景里有可落子格()
    {
        // 第 4 条：不与任何棋盘相邻的孤立可落子格同样拒绝。
        var stray = new Coord(33, 22);
        MapData map = BoardMapFixtures.Build("test-board-stray", 36, 25, Boards, [stray]);

        MapValidationResult result = MapValidator.Validate(map);

        MapValidationFailure failure = Assert.Single(result.Failures);
        Assert.Equal("SCENERY_CELL_PLAYABLE", failure.Code);
        Assert.Equal([stray], failure.Coords.AsEnumerable());
        Assert.Contains(stray.ToNotation(), failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void 出生区数不等于人数加一()
    {
        // Scenario：3 人棋盘档地图只有 3 块出生棋盘 → 拒绝并指出出生区数应为 4。
        // 去掉 1 / 3 号出生棋盘：3×25 + 2×81 = 237 格（在 3 人 190–750 内）、信物 3 + 4 = 7（在 5–20 内），只有出生区数不对。
        ImmutableArray<BoardPlate> three = [Boards[0], Boards[2], Boards[4], BoardMapFixtures.FourPublicA, BoardMapFixtures.FourPublicB];
        MapData map = BoardMapFixtures.Build("test-board-3p-zones", 3, 36, 25, three, []);
        Assert.Equal((237, 7, 3), (map.PlayableCount, map.RelicCells.Count, map.BirthZones.Length));

        MapValidationResult result = MapValidator.Validate(map);

        Assert.Equal(["BIRTH_ZONE_COUNT_NOT_ABOVE_PLAYERS", "BIRTH_ZONE_COUNT_OUT_OF_RANGE"], Codes(result).Order(StringComparer.Ordinal));
        MapValidationFailure zones = Assert.Single(result.Failures, f => f.Code == "BIRTH_ZONE_COUNT_OUT_OF_RANGE");
        Assert.Contains("出生区为 3 个", zones.Message, StringComparison.Ordinal);
        Assert.Contains("4–4", zones.Message, StringComparison.Ordinal);

        // 补回一块出生棋盘（4 块 = 人数 + 1）即通过。
        MapData four = BoardMapFixtures.Build("test-board-3p-ok", 3, 36, 25, [Boards[0], Boards[1], Boards[2], Boards[4], BoardMapFixtures.FourPublicA, BoardMapFixtures.FourPublicB], []);
        Assert.Empty(Codes(MapValidator.Validate(four)));
    }

    [Fact]
    public void 合法2人棋盘图()
    {
        // Scenario：3 块出生棋盘 + 1 块 11×11 公共棋盘、规模合规的 2 人棋盘档地图 → 接受，报告项给出每个出生区到三类目标的距离（公共目标记为"不可达"）。
        MapData map = BoardMapFixtures.TwoPlayerMap();
        Assert.Equal((196, 3, 4, 6), (map.PlayableCount, map.BirthZones.Length, map.Boards.Length, map.RelicCells.Count));   // 3×25 + 121（测试内独立算式）

        MapValidationResult result = MapValidator.Validate(map);

        Assert.True(result.IsValid, result.ToString());
        Assert.Equal(3, result.Reports.Length);
        Assert.All(result.Reports, r => Assert.Equal(("BIRTH_ZONE_DISTANCE_REPORT", MapFindingSeverity.Report), (r.Code, r.Severity)));
        foreach (string target in new[] { "最近公共信物", "中央入口", "最近咽喉" })
        {
            MapValidationFailure report = Assert.Single(result.Reports, r => r.Message.Contains(target, StringComparison.Ordinal));
            Assert.Contains("出生区 1 = 不可达，出生区 2 = 不可达，出生区 3 = 不可达", report.Message, StringComparison.Ordinal);
        }

        Assert.All(MapValidator.DistanceTable(map), metric => Assert.Equal(new int?[] { null, null, null }, metric.Distances.AsEnumerable()));
        GameBoard.Load(map);   // 不抛
    }

    [Fact]
    public void 棋盘档不可达只报告()
    {
        // Scenario（「地图静态校验规则」第 1 / 7 条与咽喉的棋盘档写法）：合规棋盘档图各出生棋盘到不了公共信物格与中央入口 → 接受，这两类目标记为"不可达"。
        // 同一张图换成标准档的可达性处理会被拒：用来证明"不可达"确实存在、不是因为可达才没报。
        MapData map = BoardMapFixtures.FourPlayerMap();
        Assert.Empty(map.ChokePoints);
        Assert.Equal((287, 7, 9), (map.PlayableCount, map.Boards.Length, map.RelicCells.Count));   // 5×25 + 2×81

        MapValidationResult result = MapValidator.Validate(map);

        Assert.True(result.IsValid, result.ToString());
        Assert.DoesNotContain(result.Failures, f => f.Code is "LANDMARK_UNREACHABLE" or "BIRTH_ZONE_ISOLATED" or "CHOKE_NOT_ANNOTATED");
        MapValidationFailure relic = Assert.Single(result.Reports, r => r.Message.Contains("最近公共信物", StringComparison.Ordinal));
        MapValidationFailure entrance = Assert.Single(result.Reports, r => r.Message.Contains("中央入口", StringComparison.Ordinal));
        foreach (MapValidationFailure report in new[] { relic, entrance })
        {
            Assert.Contains("出生区 1 = 不可达", report.Message, StringComparison.Ordinal);
            Assert.Contains("出生区 5 = 不可达", report.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("极差 ", report.Message, StringComparison.Ordinal);
        }

        // "不可达"确实存在（不是因为可达才没报）：距离表里两类目标逐区为 null。
        Assert.Equal(new int?[] { null, null, null, null, null }, MapValidator.DistanceTable(map)[0].Distances.AsEnumerable());
        Assert.Equal(new int?[] { null, null, null, null, null }, MapValidator.DistanceTable(map)[1].Distances.AsEnumerable());
    }

    [Fact]
    public void 棋盘被孤立不再拒绝()
    {
        // board-isolated-gen：取消"全部棋盘连成一片"。北侧另放一块 9×9 公共棋盘，与谁都不相连 → 照样接受。
        var lonely = new BoardPlate(new Coord(14, 27), 9, 9, BoardPlateKind.Public);
        MapData map = BoardMapFixtures.Build("test-board-lonely", 36, 38, [.. Boards, lonely], []);

        MapValidationResult result = MapValidator.Validate(map);

        Assert.True(result.IsValid, result.ToString());
        Assert.DoesNotContain("BOARD_ISOLATED", result.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void 各人数都有预算(int players)
    {
        // 预算表第 8 条按人数三行：出生区数 = 人数 + 1。取 FourPlayerMap 的前 人数 + 1 块出生棋盘 + 两块公共棋盘。
        ImmutableArray<BoardPlate> boards = [.. Boards.Take(players + 1), BoardMapFixtures.FourPublicA, BoardMapFixtures.FourPublicB];
        MapData map = BoardMapFixtures.Build($"test-board-{players}p", players, 36, 25, boards, []);

        MapValidationResult result = MapValidator.Validate(map);

        Assert.True(result.IsValid, result.ToString());
        Assert.Equal(players + 1, map.BirthZones.Length);

        // 5 人或 1 人没有预算行：报"不支持"并列出支持的人数。
        foreach (int unsupported in new[] { 1, 5 })
        {
            MapValidationFailure failure = Assert.Single(MapValidator.Validate(map with { MaxPlayers = unsupported }).Failures, f => f.Code == "UNSUPPORTED_PLAYER_COUNT");
            Assert.Contains("2 / 3 / 4", failure.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(4, 249, 250, 1000)]
    [InlineData(3, 189, 190, 750)]
    [InlineData(2, 124, 125, 500)]
    public void 规模预算按人数(int players, int below, int min, int max)
    {
        // 预算表第 8 条：4 人 250–1000、3 人 190–750、2 人 125–500。用 人数 + 1 块出生棋盘 + 两块公共棋盘的格局（合规），
        // 在公共棋盘里挖洞把可落子格压到下限减 1，报文给出该人数的区间。
        ImmutableArray<BoardPlate> boards = [.. Boards.Take(players + 1), BoardMapFixtures.FourPublicA, BoardMapFixtures.FourPublicB];
        MapData full = BoardMapFixtures.Build($"test-board-budget-{players}", players, 36, 25, boards, []);
        int excess = full.PlayableCount - below;
        Assert.True(excess > 0 && excess <= 162, $"样本口径：要挖 {excess} 格。");

        // 挖洞挖在棋盘里会另报 BOARD_CELL_NOT_PLAYABLE 等：只看可落子格那一条的报文。
        Coord[] holes = [.. BoardMapFixtures.FourPublicB.Cells().Order().Concat(BoardMapFixtures.FourPublicA.Cells().Order()).Take(excess)];
        MapData small = BoardMapFixtures.Build($"test-board-budget-{players}-small", players, 36, 25, boards, [], holes);
        small = small with { RelicCells = small.RelicCells.Where(kv => small.IsPlayable(kv.Key)).ToImmutableDictionary() };
        Assert.Equal(below, small.PlayableCount);

        MapValidationFailure failure = Assert.Single(MapValidator.Validate(small).Failures, f => f.Code == "PLAYABLE_COUNT_OUT_OF_RANGE");
        Assert.Contains($"{min}–{max}", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(MapValidator.Validate(full).Failures, f => f.Code == "PLAYABLE_COUNT_OUT_OF_RANGE");
    }

    [Theory]
    [InlineData(4, 7, 25)]
    [InlineData(3, 5, 20)]
    [InlineData(2, 4, 15)]
    public void 信物格数上限按人数(int players, int min, int max)
    {
        // 预算表第 8 条的信物格数列：4 人 7–25、3 人 5–20、2 人 4–15（区间取自规格，测试内独立写出）。
        // 人数 + 1 块出生棋盘 + 两块公共棋盘的合规格局，在公共棋盘 B 上补标准档信物到恰为上限 → 接受；再多 1 个 → 拒绝并报出方向与区间。
        // 检查阶段补（board-isolated-gen 段 A check）：变异 C7"4 人上限 25 → 30"原先 0 红，补本测试后红 1；
        // C10"3 人上限 20 → 21"红 1；CT1 只改测试（3 人与 2 人的上限对调）红 2。
        ImmutableArray<BoardPlate> boards = [.. Boards.Take(players + 1), BoardMapFixtures.FourPublicA, BoardMapFixtures.FourPublicB];
        MapData map = BoardMapFixtures.Build($"test-board-relics-{players}", players, 36, 25, boards, []);
        Coord[] spare = [.. BoardMapFixtures.FourPublicB.Cells().Where(c => !map.RelicCells.ContainsKey(c)).Order()];
        var spec = new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard);
        MapData full = map with { RelicCells = map.RelicCells.AddRange(spare.Take(max - map.RelicCells.Count).Select(c => KeyValuePair.Create(c, spec))) };
        MapData over = map with { RelicCells = map.RelicCells.AddRange(spare.Take(max + 1 - map.RelicCells.Count).Select(c => KeyValuePair.Create(c, spec))) };
        Assert.Equal((max, max + 1), (full.RelicCells.Count, over.RelicCells.Count));

        MapValidationResult accepted = MapValidator.Validate(full);
        Assert.True(accepted.IsValid, accepted.ToString());

        MapValidationFailure failure = Assert.Single(MapValidator.Validate(over).Failures);
        Assert.Equal("RELIC_COUNT_OUT_OF_RANGE", failure.Code);
        Assert.Contains($"多于上限 {max}", failure.Message, StringComparison.Ordinal);
        Assert.Contains($"{min}–{max}", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 规模预算_信物格与出生区数()
    {
        // 预算表第 8 条（4 人）：出生区 5、信物 7–25。去掉公共棋盘 B 及其两块出生棋盘 → 出生区 3 个、信物 5 个。
        ImmutableArray<BoardPlate> three = [Boards[0], Boards[2], Boards[4], BoardMapFixtures.FourPublicA];
        MapData small = BoardMapFixtures.Build("test-board-budget", 36, 25, three, []);
        Assert.Equal(156, small.PlayableCount);   // 3×25 + 81

        string[] codes = Codes(MapValidator.Validate(small));

        Assert.Contains("PLAYABLE_COUNT_OUT_OF_RANGE", codes);
        Assert.Contains("RELIC_COUNT_OUT_OF_RANGE", codes);          // 5 个，少于 7
        Assert.Contains("BIRTH_ZONE_COUNT_OUT_OF_RANGE", codes);     // 3 个，应为 5
        Assert.Contains("少于下限 7", Assert.Single(MapValidator.Validate(small).Failures, f => f.Code == "RELIC_COUNT_OUT_OF_RANGE").Message, StringComparison.Ordinal);
        Assert.Contains("5–5", Assert.Single(MapValidator.Validate(small).Failures, f => f.Code == "BIRTH_ZONE_COUNT_OUT_OF_RANGE").Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 棋盘两两至少间隔2格()
    {
        // 校验第 3 条。把 3 号出生棋盘（B 的北侧）向西挪到 x = 16 并加宽到 7（x 16–22）：与 2 号出生棋盘（x 10–14）只隔 1 列。
        ImmutableArray<BoardPlate> boards = Boards.SetItem(3, new BoardPlate(new Coord(16, 19), 7, 5, BoardPlateKind.Birth));
        MapData map = BoardMapFixtures.Build("test-board-gap", 36, 25, boards, []);
        map = map with { RelicCells = map.RelicCells.Remove(new Coord(24, 21)).Add(new Coord(18, 21), new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth)) };

        MapValidationResult result = MapValidator.Validate(map);

        MapValidationFailure failure = Assert.Single(result.Failures);
        Assert.Equal("BOARD_TOO_CLOSE", failure.Code);
        Assert.Contains("只隔 1 格", failure.Message, StringComparison.Ordinal);
        Assert.Contains("至少间隔 2 格", failure.Message, StringComparison.Ordinal);
        Assert.Equal([new Coord(10, 19), new Coord(16, 19)], failure.Coords.AsEnumerable());
    }

    [Fact]
    public void 信物格必须在棋盘内()
    {
        // 校验第 5 条：棋盘外的可落子格上放信物 → 两条都报：棋盘外不得有可落子格，信物格必须在棋盘内。
        var outside = new Coord(18, 12);
        MapData map = BoardMapFixtures.Build("test-board-relic-outside", 36, 25, Boards, [outside]);
        map = map with { RelicCells = map.RelicCells.Remove(new Coord(10, 14)).Add(outside, new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard)) };

        MapValidationResult result = MapValidator.Validate(map);

        Assert.Equal(["RELIC_OUTSIDE_BOARD", "SCENERY_CELL_PLAYABLE"], Codes(result).Order(StringComparer.Ordinal));
        Assert.Equal([outside], Assert.Single(result.Failures, f => f.Code == "RELIC_OUTSIDE_BOARD").Coords.AsEnumerable());
    }

    [Fact]
    public void 棋盘档地图不得含深水_高度_非草地地表与栅栏()
    {
        // 校验第 2 / 4 条：全图同一高度、场景只用障碍（design D3）。各改一处，逐项报出。
        MapData map = BoardMapFixtures.FourPlayerMap();
        var scenery = new Coord(0, 0);
        var inBoard = new Coord(21, 9);

        // 场景格由障碍改成深水：不属于棋盘的格子必须全部是障碍格，且棋盘档不得含深水。
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

        // 棋盘边界上的栅栏（A 的东边界与场景之间）。
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
        // 第 6 条：列数与行数各在 15–60、棋盘清单不得为空。夹具为 36×25。
        MapData map = BoardMapFixtures.FourPlayerMap();

        // 改小之后图外的格子越界，结构校验另报若干条；尺寸不合即到此为止，不再往下跑——尺寸那一条是最后一条。
        MapValidationResult tooNarrow = MapValidator.Validate(map with { Width = 14 });
        MapValidationFailure narrow = Assert.Single(tooNarrow.Failures, f => f.Code == "MAP_TOO_NARROW");
        Assert.Equal("MAP_TOO_NARROW", tooNarrow.Failures[^1].Code);
        Assert.Contains("14", narrow.Message, StringComparison.Ordinal);
        Assert.Contains("15", narrow.Message, StringComparison.Ordinal);

        MapValidationResult tooShort = MapValidator.Validate(map with { Height = 14 });
        MapValidationFailure low = Assert.Single(tooShort.Failures, f => f.Code == "MAP_TOO_SHORT");
        Assert.Equal("MAP_TOO_SHORT", tooShort.Failures[^1].Code);
        Assert.Contains("14", low.Message, StringComparison.Ordinal);
        Assert.Contains("15", low.Message, StringComparison.Ordinal);

        MapValidationFailure wide = Assert.Single(MapValidator.Validate(map with { Width = 61 }).Failures);
        Assert.Equal("MAP_TOO_WIDE", wide.Code);
        Assert.Contains("60", wide.Message, StringComparison.Ordinal);

        MapValidationFailure tall = Assert.Single(MapValidator.Validate(map with { Height = 61 }).Failures);
        Assert.Equal("MAP_TOO_TALL", tall.Code);
        Assert.Contains("60", tall.Message, StringComparison.Ordinal);

        // 区间两端都是合法值（15 与 60；宽 15 时棋盘越界会另报别的项，但不报尺寸）。
        string[] sizeCodes = ["MAP_TOO_WIDE", "MAP_TOO_NARROW", "MAP_TOO_TALL", "MAP_TOO_SHORT"];
        foreach (MapData legal in new[] { map with { Width = 60 }, map with { Height = 60 }, map with { Width = 15 }, map with { Height = 15 } })
        {
            Assert.DoesNotContain(MapValidator.Validate(legal).Failures, f => sizeCodes.Contains(f.Code));
        }

        MapValidationFailure empty = Assert.Single(MapValidator.Validate(map with { Boards = [] }).Failures);
        Assert.Equal("BOARDS_REQUIRED", empty.Code);
    }
}
