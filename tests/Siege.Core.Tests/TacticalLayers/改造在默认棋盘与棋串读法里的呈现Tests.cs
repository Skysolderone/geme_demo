using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Presentation.Layers;
using Siege.Presentation.Style;
using Siege.Presentation.Preview;
using Siege.Presentation.Visibility;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.TacticalLayers;

/// <summary>
/// 规格：tactical-layers —— Requirement: 五种战术信息层（「暂放匠人时标出可改造目标」「棋串读法的栅栏侧」
/// 「改造后差集仍可解释」）。tasks 4.1。
/// retire-legacy-rules 1.6：局面载体为平坦 8×8 链接档图上四人同锁棋盘 0（LinkModeFixtures.PlainStarted），原 9×9 坐标平移 (+2, +2)。
/// </summary>
public class 改造在默认棋盘与棋串读法里的呈现Tests
{
    private static MatchFlow Match(TerrainData terrain) => LinkModeFixtures.PlainStarted(terrain).AtRound(5);

    [Fact]
    public void 暂放匠人时标出可改造目标()
    {
        // Scenario「暂放匠人时标出可改造目标」：匠人暂放在四邻含深水与林地的格上、且<b>未打开任何信息层</b>
        // → 默认棋盘标出该深水格、该林地格与可立栅的边。
        // 结构上的保证：目标进的是预演呈现（PreviewHighlights 层），而预演在 ViewerWorld 里与信息层无关——
        // 本用例全程不调 world.Layer(...)，拿到的仍是全部目标。
        // 变异验证 M-SC1（实做）：Core 侧的 EditOptions 少给外圈边 → 本测试红（16 → 4）。
        // 另一条未实做的记法：VisualLayering.LayerOf(EditTarget) 若改为 RenderLayer.TacticalOverlay（信息层的叠加层），本测试也红。
        MatchFlow match = Match(TestMaps.Terrain(surfaces: [("G7", Surface.DeepWater), ("J7", Surface.Forest)]));
        match.Debug.SeedHand(P0, (PieceType.Artisan, 5));
        StagedBatch batch = match.OpenDeploy();
        Assert.Null(batch.Stage(TestMaps.At("H7"), PieceType.Artisan));

        ViewerWorld world = match.World(P0);
        PreviewPresentation shown = world.Preview()!;
        ArtisanEditView artisan = Assert.Single(shown.ArtisanEdits);

        Assert.Equal(["G7"], artisan.BridgeCells.Notations());
        Assert.Equal(["J7"], artisan.BurnCells.Notations());
        Assert.Equal(16, artisan.FenceEdges.Length);

        // 「不依赖打开任何信息层」：目标高亮全部落在预览层，而预览层在默认棋盘上就渲染。
        Assert.All(
            shown.Highlights.Where(h => h.Kind is HighlightKind.EditTarget or HighlightKind.ChosenEdit)
                .Select(h => h.Kind)
                .Concat(shown.EdgeHighlights.Select(h => h.Kind)),
            kind => Assert.Equal(RenderLayer.PreviewHighlights, VisualLayering.LayerOf(kind)));
        Assert.Equal(RenderLayer.PreviewHighlights, VisualLayering.LayerOf(HighlightKind.EditTarget));
        Assert.NotEqual(RenderLayer.TacticalOverlay, VisualLayering.LayerOf(HighlightKind.EditTarget));

        // 与已有设施在外观上区分：候选与已选各有独立的视觉手法，且与其余高亮类别都不同。
        Assert.Equal(HighlightStyle.EditTargetHint, VisualLayering.StyleOf(HighlightKind.EditTarget));
        Assert.Equal(HighlightStyle.EditChosenMark, VisualLayering.StyleOf(HighlightKind.ChosenEdit));

        // 默认棋盘此刻还没有任何设施：候选边与既有栅栏边不会混在一起。
        Assert.Empty(world.Board().Fences);
        Assert.Empty(world.Board().Edits);
    }

    [Fact]
    public void 棋串读法区分栅栏侧()
    {
        // tactical-layers「棋串读法」：栅栏侧 MUST 与空侧可区分，玩家据此看出气边被堵在哪里。
        // P0 的 G7–G8 两子成串；G7–F7 立着栅栏 → F7 虽是几何四邻却不是气；G6 / H7 / H8 / G9 是空侧的气。
        // 变异验证 M-SC2（实做）：TacticalLayers.Liberties 的 FenceSides 恒给 [] → 本测试红 2（含"立栅后栅栏侧与差集都随改造更新"）。
        MatchFlow match = Match(TestMaps.Terrain(fences: [("F7", "G7")])).Stones(P0, "G7", "G8");
        var liberties = (LibertyLayerContent)match.World(P0).Layer(TacticalLayer.Board, BoardReading.Groups);

        LibertyGroupView group = Assert.Single(liberties.Groups, g => g.Owner == P0);
        Assert.Equal(["G7", "G8"], group.Stones.Notations());

        // 栅栏侧：恰有一端在串上的边。
        FenceEdge side = Assert.Single(group.FenceSides);
        Assert.Equal(new FenceEdge(TestMaps.At("F7"), TestMaps.At("G7")), side);

        // 空侧：栅栏那一侧的 F7 不在气里，其余四邻都在。
        Assert.DoesNotContain(TestMaps.At("F7"), group.Liberties);
        Assert.Equal(["G6", "H7", "F8", "H8", "G9"], group.Liberties.Order().Notations());

        // 对照：同一盘面去掉栅栏 → 没有栅栏侧，F7 成为气。
        var noFence = (LibertyLayerContent)Match(TerrainData.Flat).Stones(P0, "G7", "G8").World(P0)
            .Layer(TacticalLayer.Board, BoardReading.Groups);
        LibertyGroupView plain = Assert.Single(noFence.Groups, g => g.Owner == P0);
        Assert.Empty(plain.FenceSides);
        Assert.Contains(TestMaps.At("F7"), plain.Liberties);
    }

    [Fact]
    public void 立栅后栅栏侧与差集都随改造更新()
    {
        // Scenario「改造后差集仍可解释」：本局新立的栅栏立刻进栅栏侧与差集，且差集原因指向栅栏。
        // 预置栅栏与本局立起的栅栏在视图里不可区分（information-visibility「改造结果公开」）——这里正是同一条断言路径。
        MatchFlow match = Match(TerrainData.Flat).Stones(P0, "G7", "G8");
        var before = (LibertyLayerContent)match.World(P0).Layer(TacticalLayer.Board, BoardReading.Groups);
        Assert.Empty(Assert.Single(before.Groups, g => g.Owner == P0).FenceSides);

        match.Board.ApplyTerrainEdits([TerrainEdit.Fence(TestMaps.At("F7"), TestMaps.At("G7"))]);
        match.Debug.Recalculate();

        var after = (LibertyLayerContent)match.World(P0).Layer(TacticalLayer.Board, BoardReading.Groups);
        LibertyGroupView group = Assert.Single(after.Groups, g => g.Owner == P0);
        Assert.Equal([new FenceEdge(TestMaps.At("F7"), TestMaps.At("G7"))], group.FenceSides);
        Assert.DoesNotContain(TestMaps.At("F7"), group.Liberties);

        // 差集：F7 仍被 G7 覆盖（栅栏挡气不挡覆盖），但不再是气 → 原因是栅栏。
        ReadingDiffCell diff = Assert.Single(after.Diff.CoveredNotLiberty, c => c.Coord == TestMaps.At("F7"));
        Assert.Equal([TerrainReason.Fence], diff.Reasons);
        Assert.Equal("栅栏", Siege.Presentation.Text.Labels.TerrainReason(diff.Reasons[0]));

        // 默认棋盘上这道新栅栏与预置栅栏同形：都只是 Fences 里的一条边，看不出新旧。
        DefaultBoardView board = match.World(P0).Board();
        Assert.Equal([new FenceEdge(TestMaps.At("F7"), TestMaps.At("G7"))], board.Fences);
    }

    [Fact]
    public void 工坊下标出隔一格目标()
    {
        // more-pieces-relics tactical-layers Scenario「工坊下标出隔一格目标」：本小回合工坊生效，匠人暂放在 H8，K8 是未架桥深水，且未打开任何信息层
        // → 默认棋盘把 K8 标为可搭桥目标；工坊未生效时 K8 不被标出。
        // 走真实对局流程：P0 占据工坊 J6 → 小回合开始的快照标记工坊 → 部署上下文带上标记 → 富预演的改造目标（改造合法性唯一实现）→ 预览层高亮。
        // 对照：同一局面轮到不控制工坊的 P1，同一落点的目标里没有 K8。全程不调 world.Layer(...)。
        // 骨架态即绿（段 B 的 MB-W7 已让富预演传工坊标记）；变异验证见 implement.md 段 D（MD-W1：富预演改造目标不传工坊 → 本测试红）。
        MatchFlow match = LinkModeFixtures.PlainStarted(TestMaps.Terrain(surfaces: [("K8", Surface.DeepWater)]), relics: [("J6", RelicFixtures.Workshop())])
            .AtRound(7, TestMaps.All)
            .Pieces(P0, PieceType.Basic, "J6");
        match.Debug.SeedHand(P0, (PieceType.Artisan, 2));
        match.Debug.SeedHand(P1, (PieceType.Artisan, 2));

        match.BeginTurn();
        Assert.True(match.CurrentSnapshot!.WorkshopActive);
        match.EnterRecruit();
        StagedBatch batch = match.EnterDeploy();
        Assert.Null(batch.Stage(TestMaps.At("H8"), PieceType.Artisan));

        PreviewPresentation shown = match.World(P0).Preview()!;
        ArtisanEditView artisan = Assert.Single(shown.ArtisanEdits);
        Assert.Equal(TestMaps.At("H8"), artisan.ArtisanCell);
        Assert.Equal(["K8"], artisan.BridgeCells.Notations());
        Assert.Contains(shown.Highlights, h => h.Kind == HighlightKind.EditTarget && h.Coord == TestMaps.At("K8"));
        Assert.Equal(RenderLayer.PreviewHighlights, VisualLayering.LayerOf(HighlightKind.EditTarget));

        batch.Unstage(TestMaps.At("H8"));
        Assert.True(match.Confirm().Confirmed);

        match.BeginTurn();
        Assert.Equal(P1, match.CurrentPlayer);
        Assert.False(match.CurrentSnapshot!.WorkshopActive);
        match.EnterRecruit();
        Assert.Null(match.EnterDeploy().Stage(TestMaps.At("H8"), PieceType.Artisan));

        PreviewPresentation without = match.World(P1).Preview()!;
        Assert.Empty(Assert.Single(without.ArtisanEdits).BridgeCells);
        Assert.DoesNotContain(without.Highlights, h => h.Coord == TestMaps.At("K8"));
    }
}
