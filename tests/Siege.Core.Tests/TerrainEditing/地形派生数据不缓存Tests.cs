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
    public void 合法落子范围在保护期后含本局新架的桥()
    {
        // 排查项 ①（曾经是缓存，已改）：MatchFlow 原先在建局时把可落子格算进 _playableCells 字段，
        // 地形可变之后它是过期数据——本局架出来的桥永远进不了合法落子范围。现改为每次按 Board 现算。
        // 注意必须用第 4 大回合之后：保护期内合法范围是出生区格集合（含深水格），测不出这条。
        // 也必须真的走 MatchFlow.LegalRangeFor：用别的盘面自己算一遍 IsPlayable 是恒真断言，把字段加回去照样绿。
        // 变异 M-B15：LegalRangeFor 改回读构造时算好的字段 → 本条红 1。
        Coord water = TestMaps.At("E5");   // 9×9 合成图的中心，不属于任何角落出生区
        MatchFlow match = MatchFixtures.Started(TestMaps.Terrain(surfaces: [("E5", Surface.DeepWater)])).AtRound(5);

        Assert.DoesNotContain(water, match.LegalRangeFor(MatchFixtures.P0));

        match.Board.ApplyTerrainEdits([TerrainEdit.Bridge(water)]);

        Assert.Contains(water, match.LegalRangeFor(MatchFixtures.P0));

        // 反面：范围里只多了这一格，没有把别的格子一起放进来。
        MatchFlow twin = MatchFixtures.Started(TestMaps.Terrain(surfaces: [("E5", Surface.DeepWater)])).AtRound(5);
        Assert.Equal(
            twin.LegalRangeFor(MatchFixtures.P0).Count + 1,
            match.LegalRangeFor(MatchFixtures.P0).Count);
    }

    [Fact]
    public void MatchFlow的Map随改造更新()
    {
        // 排查项 ②（曾经是缓存，已改）：MatchFlow.Map 原先是建局时拷进字段的那一份 MapData，
        // 改造之后读到的是旧地形。现改为 `Map => Board.Map`。
        // 下游一大片（LegalRangeFor / 日志首部 / FlagPlanting 之外的全部读图处）都经它。
        MatchFlow match = MatchFixtures.Started().AtRound(5);
        Assert.False(match.Map.HasFence(TestMaps.At("E5"), TestMaps.At("E6")));

        match.Board.ApplyTerrainEdits([TerrainEdit.Fence(TestMaps.At("E5"), TestMaps.At("E6"))]);

        Assert.True(match.Map.HasFence(TestMaps.At("E5"), TestMaps.At("E6")));
        Assert.Same(match.Board.Map, match.Map);

        // 开局那份仍可读（日志首部的可落子格分母用它）。
        Assert.False(match.Board.BaseMap.HasFence(TestMaps.At("E5"), TestMaps.At("E6")));
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
        MatchFlow match = MatchFixtures.Started().AtRound(5);
        DefaultBoardView before = DefaultBoardView.From(match.World(MatchFixtures.P0).Public);
        Assert.DoesNotContain(new FenceEdge(TestMaps.At("E5"), TestMaps.At("E6")), before.Fences);

        match.Board.ApplyTerrainEdits([TerrainEdit.Fence(TestMaps.At("E5"), TestMaps.At("E6"))]);

        DefaultBoardView after = DefaultBoardView.From(match.World(MatchFixtures.P0).Public);
        Assert.Contains(new FenceEdge(TestMaps.At("E5"), TestMaps.At("E6")), after.Fences);
    }

    [Fact]
    public void 距离表按传入地图现算()
    {
        // 排查项 ⑥（不缓存，但有口径）：MapValidator.DistanceTable 是纯函数（传入哪份地图就按哪份算），
        // 唯二调用方是建局时的 Validate 与 `Siege.Sim map` 子命令，都只看开局地图。
        // 结论：本局架的桥不会回头改变距离均衡校验的结论——这是有意的，校验是**地图设计**的守门，不是对局态。
        MapData map = TestMaps.Blank(TestMaps.Terrain(surfaces: [("E5", Surface.DeepWater)]), size: 9).Map;
        MapData bridged = TerrainWriter.ApplyAll(map, [TerrainEdit.Bridge(TestMaps.At("E5"))]);

        Assert.True(map.TerrainData.IsUnbridgedDeepWater(TestMaps.At("E5")));
        Assert.False(bridged.TerrainData.IsUnbridgedDeepWater(TestMaps.At("E5")));
        Assert.Equal(map.PlayableCount + 1, bridged.PlayableCount);
    }
}
