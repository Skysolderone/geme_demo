using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.TerrainEditing;

/// <summary>
/// 规格：terrain-edit —— Requirement: 改造不可逆且设施无归属 / 改造公开。
/// tasks 2.1（"不可逆"的对局层含义）与 R-3（公开视图 MUST NOT 出现改造者）。
/// </summary>
public class 改造不可逆与公开Tests
{
    [Fact]
    public void 匠人被提走后设施仍在()
    {
        // terrain-edit「匠人被提走后设施仍在」：设施与棋子彻底解耦，围杀改造者 MUST NOT 撤销设施。
        // 两次改造都走真实的 SettlementDriver.Confirm，不直接调 Board.RemoveStones。
        GameBoard board = TestMaps.Blank(size: 9);
        SettlementDriver driver = BatchFixtures.Driver(board);
        TerrainEdit edit = TerrainEdit.Fence(TestMaps.At("B1"), TestMaps.At("B2"));

        // 第一手：P0 的匠人落 A1，给 B1–B2 立栅（B1 是 A1 的几何四邻，这是一条外圈边）。
        Assert.True(driver.Confirm(
            BatchFixtures.Context(board, TestMaps.P0), [BatchFixtures.Artisan("A1", edit)]).Confirmed);
        Assert.True(board.Map.HasFence(TestMaps.At("B1"), TestMaps.At("B2")));

        // 第二手：P1 把 A1 围杀——角上的匠人只有 A2 与 B1 两条气边。
        SettlementOutcome kill = driver.Confirm(
            BatchFixtures.Context(board, TestMaps.P1), [BatchFixtures.P("A2"), BatchFixtures.P("B1")]);

        Assert.True(kill.Confirmed, kill.Failure?.Message);
        Assert.Equal(["A1"], kill.CaptureRecord!.Captured.Select(c => c.Coord).Notations());
        Assert.Null(board[TestMaps.At("A1")].Occupant);

        // 匠人没了，栅栏还在：地形、改造列表与盘面序列化三处一致。
        Assert.True(board.Map.HasFence(TestMaps.At("B1"), TestMaps.At("B2")));
        Assert.Equal([edit], board.TerrainEdits);
        Assert.Contains(edit.ToString(), board.Serialize(), StringComparison.Ordinal);

        // 设施无归属：它既不属于立栅的 P0，也不属于提走匠人的 P1——棋串里没有它。
        Assert.Equal(2, board.GroupsOf(TestMaps.P1).Sum(g => g.Size));
        Assert.Empty(board.GroupsOf(TestMaps.P0));

        // 立栅的那一侧确实被切断：B1 与 B2 之间不再有气边（栅栏在匠人被提走后仍然生效）。
        Assert.DoesNotContain(TestMaps.At("B2"), board.LibertyNeighbors(TestMaps.At("B1")));
    }

    [Fact]
    public void 弃赛不撤销改造()
    {
        // terrain-edit「弃赛不撤销改造」：搭桥的玩家弃赛后桥仍在原处，任何玩家都可以使用。
        Coord water = TestMaps.At("E5");
        MatchFlow match = MatchFixtures.Started(TestMaps.Terrain(surfaces: [("E5", Surface.DeepWater)])).AtRound(5);
        match.Board.ApplyTerrainEdits([TerrainEdit.Bridge(water)]);

        Assert.True(match.Map.HasBridge(water));

        match.Resign(MatchFixtures.P0);

        Assert.Equal(PlayerStatus.Resigned, match.StateOf(MatchFixtures.P0).Status);
        Assert.True(match.Map.HasBridge(water));
        Assert.Equal([TerrainEdit.Bridge(water)], match.Board.TerrainEdits);

        // "任何玩家都可以使用"：弃赛之后该格对其余玩家仍是合法落点。
        Assert.Contains(water, match.LegalRangeFor(MatchFixtures.P1));
    }

    [Fact]
    public void 改造结果人人可见且不显示改造者()
    {
        // terrain-edit「改造公开」：桥、栅栏与被烧过的地表是盘面的一部分，对全部玩家可见；
        // 同时 MUST NOT 记录或展示改造者（R-3）——改造方只活在 MatchFlow.TerrainEdits（遥测）里。
        TerrainEdit fence = TerrainEdit.Fence(TestMaps.At("E5"), TestMaps.At("E6"));
        MatchFlow match = MatchFixtures.Started(
            TestMaps.Terrain(surfaces: [("D4", Surface.DeepWater), ("F4", Surface.Forest)])).AtRound(5);
        match.Board.ApplyTerrainEdits([TerrainEdit.Bridge(TestMaps.At("D4")), fence, TerrainEdit.Burn(TestMaps.At("F4"))]);

        MatchPublicView view = match.Publish();

        // 三种改造都在公开快照里，四家看到的是同一份（Publish 不带视角参数）。
        Assert.True(view.Board.Map.HasBridge(TestMaps.At("D4")));
        Assert.True(view.Board.Map.HasFence(TestMaps.At("E5"), TestMaps.At("E6")));
        Assert.Equal(Surface.Grass, view.Board.Map.SurfaceAt(TestMaps.At("F4")));
        Assert.Contains(fence.ToString(), view.BoardSerialized, StringComparison.Ordinal);
        Assert.Equal(match.Board.Serialize(), view.BoardSerialized);

        // 守门：公开视图的类型闭包里根本不存在"带改造方的改造记录"这个类型。
        Assert.DoesNotContain(typeof(TerrainEditRecord), PresentationFixtures.ReachableTypes(typeof(MatchPublicView)));

        // 反面：改造方确实被记下来了，只是不在公开视图里——否则上一条断言可以靠"压根没记"通过。
        var board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("D4", Surface.DeepWater)]), size: 9);
        SettlementOutcome outcome = BatchFixtures.Driver(board).Confirm(
            BatchFixtures.Context(board, TestMaps.P1), [BatchFixtures.Artisan("C4", TerrainEdit.Bridge(TestMaps.At("D4")))]);
        Assert.Equal(TestMaps.At("C4"), outcome.CaptureRecord!.Edits.Single().ArtisanCoord);
    }

    [Fact]
    public void 默认棋盘上的新旧设施同形且不带改造者()
    {
        // terrain-edit「改造公开」+ visual-style-baseline「新旧设施同形」在<b>表现层</b>的落点（tasks 4.1「公开视图含当前设施」）：
        // 默认棋盘的桥 / 栅栏 / 地表字段里分不出预置与本局新增，且整个闭包里没有改造者。
        // 变异验证 M-SC3（实做）：DefaultBoardView.From 把 Edits 恒传 [] → 本测试红（Edits.Length 3 → 0，落成反馈失去数据来源）。
        TerrainData terrain = TestMaps.Terrain(
            surfaces: [("D4", Surface.DeepWater), ("F4", Surface.DeepWater), ("G4", Surface.Forest), ("H4", Surface.Forest)],
            bridges: ["D4"],
            fences: [("B1", "B2")]);
        MatchFlow match = MatchFixtures.Started(terrain).AtRound(5);
        match.Board.ApplyTerrainEdits(
            [TerrainEdit.Bridge(TestMaps.At("F4")), TerrainEdit.Fence(TestMaps.At("C1"), TestMaps.At("C2")), TerrainEdit.Burn(TestMaps.At("H4"))]);
        match.Debug.Recalculate();

        Siege.Presentation.Visibility.DefaultBoardView board = match.World(MatchFixtures.P0).Board();

        // 预置桥 D4 与本局架的桥 F4：三个可见字段逐项相同。
        Siege.Presentation.Visibility.BoardCellView preset = board.CellAt(TestMaps.At("D4"));
        Siege.Presentation.Visibility.BoardCellView built = board.CellAt(TestMaps.At("F4"));
        Assert.Equal((preset.Terrain, preset.Surface, preset.HasBridge), (built.Terrain, built.Surface, built.HasBridge));
        Assert.True(built.HasBridge);

        // 预置栅栏与本局立的栅栏混在同一个集合里，没有第二个字段区分。
        Assert.Equal(
            [new FenceEdge(TestMaps.At("B1"), TestMaps.At("B2")), new FenceEdge(TestMaps.At("C1"), TestMaps.At("C2"))],
            board.Fences);

        // 被烧的林地读起来就是草地（未烧的 G4 仍是林地）。
        Assert.Equal(Surface.Grass, board.CellAt(TestMaps.At("H4")).Surface);
        Assert.Equal(Surface.Forest, board.CellAt(TestMaps.At("G4")).Surface);

        // Edits 只是"本局改了哪几处"的清单，没有归属；整个闭包里不存在带改造方的记录类型（R-3）。
        Assert.Equal(3, board.Edits.Length);
        Assert.DoesNotContain(typeof(TerrainEditRecord), PresentationFixtures.ReachableTypes(typeof(Siege.Presentation.Visibility.DefaultBoardView)));
        Assert.DoesNotContain(typeof(TerrainEditRecord), PresentationFixtures.ReachableTypes(typeof(Siege.Presentation.Visibility.PublicWorld)));
    }
}
