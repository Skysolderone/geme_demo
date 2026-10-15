using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;
using Siege.Presentation.Visibility;

namespace Siege.Core.Tests.TerrainEditing;

/// <summary>
/// tasks 2.6：全仓排查缓存地形派生数据的位置，逐处确认改造后失效或重算。
/// 清单与结论在 <c>.trellis/tasks/09-18-artisan-terrain-edit/implement.md</c>「段 B / 2.6 缓存排查」。
/// </summary>
/// <remarks>
/// 排查结论只有两类：<b>不缓存</b>（每次从 <see cref="GameBoard.Map"/> 实时导出）与 <b>已改成实时</b>。
/// 本文件给每条"曾经缓存或看起来像缓存"的路径配一条"改造后结果变化"的断言。
/// </remarks>
public class 地形派生数据不缓存Tests
{
    [Fact]
    public void 合法落子范围含本局新架的桥()
    {
        // 排查项 ①（曾经是缓存，已改）：MatchFlow 原先在建局时把可落子格算进 _playableCells 字段，
        // 地形可变之后它是过期数据——本局架出来的桥永远进不了合法落子范围。现改为每次按 Board 现算。
        // retire-legacy-rules 1.5：原名「合法落子范围在保护期后含本局新架的桥」，原注"必须用第 4 大回合之后（保护期内合法范围是出生区格集合，含深水格）"
        // 只对旧模式成立；链接载体（平坦 8×8 链接档图，四人同锁棋盘 0）上合法范围任何大回合都是所在战场的可落子格，构筑期同样成立，故改名。
        // 也必须真的走 MatchFlow.LegalRangeFor：用别的盘面自己算一遍 IsPlayable 是恒真断言，把字段加回去照样绿。
        // 变异 M-B15：LegalRangeFor 改回读构造时算好的字段 → 本条红 1。
        Coord water = TestMaps.At("G7");   // 棋盘 0 中部（原 9×9 合成图的中心 E5）
        MatchFlow match = LinkModeFixtures.PlainStarted(TestMaps.Terrain(surfaces: [("G7", Surface.DeepWater)])).AtRound(5);

        Assert.DoesNotContain(water, match.LegalRangeFor(TestMaps.P0));

        match.Board.ApplyTerrainEdits([TerrainEdit.Bridge(water)]);

        Assert.Contains(water, match.LegalRangeFor(TestMaps.P0));

        // 反面：范围里只多了这一格，没有把别的格子一起放进来。
        MatchFlow twin = LinkModeFixtures.PlainStarted(TestMaps.Terrain(surfaces: [("G7", Surface.DeepWater)])).AtRound(5);
        Assert.Equal(
            twin.LegalRangeFor(TestMaps.P0).Count + 1,
            match.LegalRangeFor(TestMaps.P0).Count);
    }

    [Fact]
    public void MatchFlow的Map随改造更新()
    {
        // 排查项 ②（曾经是缓存，已改）：MatchFlow.Map 原先是建局时拷进字段的那一份 MapData，
        // 改造之后读到的是旧地形。现改为 `Map => Board.Map`。
        // 下游一大片（LegalRangeFor / 日志首部 / FlagPlanting 之外的全部读图处）都经它。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5);
        Assert.False(match.Map.HasFence(TestMaps.At("G7"), TestMaps.At("G8")));

        match.Board.ApplyTerrainEdits([TerrainEdit.Fence(TestMaps.At("G7"), TestMaps.At("G8"))]);

        Assert.True(match.Map.HasFence(TestMaps.At("G7"), TestMaps.At("G8")));
        Assert.Same(match.Board.Map, match.Map);

        // 开局那份仍可读（日志首部的可落子格分母用它）。
        Assert.False(match.Board.BaseMap.HasFence(TestMaps.At("G7"), TestMaps.At("G8")));
    }

    [Fact]
    public void 气边覆盖与棋串每次调用重算()
    {
        // 排查项 ③（不缓存）：Adjacency 的两个导出关系与 GameBoard 的棋串 / 气都是每次调用实时导出，
        // 类型注释明写"不做增量维护也不缓存"。这条钉住"注释属实"。
        GameBoard board = TestMaps.Blank(
            TestMaps.Terrain(surfaces: [("D4", Surface.DeepWater), ("F4", Surface.Forest)]), size: 9);

        // 先各读一遍，制造"若有缓存就会命中"的时机。
        _ = board.LibertyNeighbors(TestMaps.At("C4"));
        _ = board.CoverageTargets(TestMaps.At("E4"));
        _ = board.LibertyNeighbors(TestMaps.At("G6"));

        board.ApplyTerrainEdits(
        [
            TerrainEdit.Bridge(TestMaps.At("D4")),
            TerrainEdit.Burn(TestMaps.At("F4")),
            TerrainEdit.Fence(TestMaps.At("G6"), TestMaps.At("H6")),
        ]);

        Assert.Contains(TestMaps.At("D4"), board.LibertyNeighbors(TestMaps.At("C4")));
        Assert.Contains(TestMaps.At("F4"), board.CoverageTargets(TestMaps.At("E4")));
        Assert.DoesNotContain(TestMaps.At("H6"), board.LibertyNeighbors(TestMaps.At("G6")));
    }

    [Fact]
    public void 覆盖表与空格归属按新地形重算()
    {
        // 排查项 ④（不缓存）：CoverageMap.Compute 每次结算重算一次。
        // 烧林使该格由"不是覆盖目标"变成可被覆盖 → 空格归属随之由中立变为独占（terrain-edit「烧林后可被覆盖」）。
        // 段 B 改写：原来用的是据点控制（SiteControl），据点摘除后改断言同一份覆盖表给出的空格归属三态，行为等价。
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("F4", Surface.Forest)]), size: 9);
        board.Place(TestMaps.At("E4"), TestMaps.P0, PieceType.Artisan);

        CellOwnership before = CoverageMap.Compute(board).OwnershipOf(TestMaps.At("F4"));
        Assert.Equal(OwnershipKind.Neutral, before.Kind);

        board.ApplyTerrainEdits([TerrainEdit.Burn(TestMaps.At("F4"))]);

        CellOwnership after = CoverageMap.Compute(board).OwnershipOf(TestMaps.At("F4"));
        Assert.Equal(new CellOwnership(OwnershipKind.Exclusive, TestMaps.P0), after);
    }

    [Fact]
    public void 表现层的地形视图随改造更新()
    {
        // 排查项 ⑤（不缓存）：DefaultBoardView.From 每次从公开快照的 Board.Map 现取地表与栅栏集合，
        // Godot 的 BoardView.Build 在每次刷新时重建。段 C 只需管"画得对不对"，不需要再加失效机制。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5);
        DefaultBoardView before = DefaultBoardView.From(match.World(TestMaps.P0).Public);
        Assert.DoesNotContain(new FenceEdge(TestMaps.At("G7"), TestMaps.At("G8")), before.Fences);

        match.Board.ApplyTerrainEdits([TerrainEdit.Fence(TestMaps.At("G7"), TestMaps.At("G8"))]);

        DefaultBoardView after = DefaultBoardView.From(match.World(TestMaps.P0).Public);
        Assert.Contains(new FenceEdge(TestMaps.At("G7"), TestMaps.At("G8")), after.Fences);
    }

    [Fact]
    public void 距离表按传入地图现算()
    {
        // 排查项 ⑥（不缓存，但有口径）：MapValidator.DistanceTable 是纯函数（传入哪份地图就按哪份算），
        // 调用方只看开局地图：retire-legacy-maps 4.2 起距离均衡校验随旧档删除，唯一调用方是 `Siege.Sim map` 子命令的距离打印（L10 保留）。
        // 结论：本局架的桥不会回头改变这张表——这是有意的，它描述的是**地图设计**，不是对局态。
        MapData map = TestMaps.Blank(TestMaps.Terrain(surfaces: [("E5", Surface.DeepWater)]), size: 9).Map;
        MapData bridged = TerrainWriter.ApplyAll(map, [TerrainEdit.Bridge(TestMaps.At("E5"))]);

        Assert.True(map.TerrainData.IsUnbridgedDeepWater(TestMaps.At("E5")));
        Assert.False(bridged.TerrainData.IsUnbridgedDeepWater(TestMaps.At("E5")));
        Assert.Equal(map.PlayableCount + 1, bridged.PlayableCount);
    }

    [Fact]
    public void 距离表沿气边计算()
    {
        // retire-legacy-maps（check 补回覆盖）：MapValidator.DistanceTable / MultiSourceDistances 按 L10 保留（`Siege.Sim map` 打印距离表），
        // 它们唯一的测试 地图静态校验规则Tests.距离沿气边计算 随距离均衡校验（DISTANCE_IMBALANCE）在第 4 组删除。这里把同一个算例迁来，
        // 断言从"失衡失败消息里的两个距离"改为直接读距离表——所守的"最短落子距离沿气边、崖壁挡住的路不算路"不变。
        // 9×9 平地，入口 E5；出生区 1 = {A5}、出生区 2 = {J5}，几何距离都是 4。
        // 把 G1–G8 抬到 h=2（G9 留作绕行口）：出生区 2 只能绕 J9 → G9 → E9 → E5，气边最短路 12；出生区 1 仍是 4。
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
        Assert.Equal(new int?[] { 4, 12 }, entrance.Distances.ToArray());
        Assert.Equal(new int?[] { 4, 12 }, Assert.Single(MapValidator.DistanceTable(Wall(2)), m => m.Name == "最近咽喉").Distances.ToArray());

        // 同一堵墙降为 h=1 缓坡：两条路都能走，距离相等——证明上面的 12 是崖壁造成的而不是别的。
        Assert.Equal(new int?[] { 4, 4 }, Assert.Single(MapValidator.DistanceTable(Wall(1)), m => m.Name == "中央入口").Distances.ToArray());

        // 没有公共信物：该项目标集合为空，整列为 null（不是 0）。
        BirthZoneDistance relic = Assert.Single(MapValidator.DistanceTable(Wall(2)), m => m.Name == "最近公共信物");
        Assert.Empty(relic.Targets);
        Assert.All(relic.Distances, d => Assert.Null(d));
    }
}
