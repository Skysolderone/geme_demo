using System.Collections.Immutable;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Preview;
using Siege.Core.Scoring;
using Siege.Presentation.Layers;
using Siege.Presentation.Preview;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.BatchPreview;

/// <summary>
/// 规格：batch-preview —— Requirement: 批次预演必须显示的信息（第 1 项「匠人另显示其改造目标与动作类型」、
/// 第 4 项「按应用改造后的地形计算」、第 7 项「当前匠人落点的全部合法改造目标」）。tasks 4.1。
/// </summary>
/// <remarks>
/// <para>合法目标一律由 <see cref="TerrainEditRules.LegalTargets"/>（改造合法性唯一实现）给出，
/// 经 Core 富预演的 <see cref="EditOutlook"/> 送到表现层；<see cref="PreviewPresentation"/> 只拼文案与高亮，
/// 不增删条目、不自判合法性。守门见 <c>可改造目标逐条来自改造合法性唯一实现</c> 与
/// <see cref="UI层不含规则计算Tests.表现层不调用规则计算入口"/>（<c>TerrainEditRules</c> / <c>TerrainWriter</c> 在禁表里）。</para>
/// </remarks>
public class 改造在预演中的呈现Tests
{
    /// <summary>9×9 空盘：E5 深水、G5 林地，其余平地。</summary>
    private static GameBoard Board() =>
        TestMaps.Blank(TestMaps.Terrain(surfaces: [("E5", Surface.DeepWater), ("G5", Surface.Forest)]), size: 9);

    private static PreviewPresentation Shown(GameBoard board, params Placement[] placements) =>
        PreviewPresentation.Build(
            BatchPreviewBuilder.Build(board, BatchFixtures.Context(board, P0), placements, new BoardHistory(), Roster(P0, P1),
                EmptyRelics(board), majorRound: 5),
            EmptyHand(P0),
            LibertyThresholds.Default);

    [Fact]
    public void 工坊下列出隔一格目标()
    {
        // more-pieces-relics batch-preview「计分信物与新棋子的预演」Scenario「工坊下列出隔一格目标」：本小回合工坊生效，暂放匠人在 F6，F8 是林地
        // → 预演列出的合法改造目标含"烧林 F8"。工坊标记经批次上下文（来自快照）传入，目标集合仍只由改造合法性唯一实现给出；
        // 同一暂放在工坊未生效时不含该目标。
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("F8", Surface.Forest)]), size: 9);
        Placement[] placements = [new Placement(TestMaps.At("F6"), PieceType.Artisan, null)];

        Core.Preview.BatchPreview with = BatchPreviewBuilder.Build(board, BatchFixtures.Context(board, P0) with { WorkshopActive = true },
            placements, new BoardHistory(), Roster(P0, P1), EmptyRelics(board), majorRound: 5);
        Core.Preview.BatchPreview without = BatchPreviewBuilder.Build(board, BatchFixtures.Context(board, P0),
            placements, new BoardHistory(), Roster(P0, P1), EmptyRelics(board), majorRound: 5);

        Assert.Contains(TerrainEdit.Burn(TestMaps.At("F8")), Assert.Single(with.EditOptions).Legal);
        Assert.DoesNotContain(TerrainEdit.Burn(TestMaps.At("F8")), Assert.Single(without.EditOptions).Legal);
        Assert.Equal(TerrainEditRules.LegalTargets(board.Map, TestMaps.At("F6"), workshop: true), Assert.Single(with.EditOptions).Legal);
    }

    [Fact]
    public void 显示改造目标()
    {
        // Scenario「显示改造目标」：暂放一枚匠人并指定"对相邻深水格搭桥" → 界面显示该目标格与动作类型，并把它计入同一枚额度。
        // 守门：StagedPieceView 若丢掉 Edit / EditText（恒传 null），本测试红——这一条本段没有实做变异，只作记法。
        GameBoard board = Board();
        PreviewPresentation shown = Shown(board, BatchFixtures.Artisan("F5", TerrainEdit.Bridge(TestMaps.At("E5"))));

        StagedPieceView staged = Assert.Single(shown.StagedPieces);
        Assert.Equal((TestMaps.At("F5"), PieceType.Artisan), (staged.Coord, staged.Type));
        Assert.Equal(TerrainEdit.Bridge(TestMaps.At("E5")), staged.Edit);
        Assert.Equal("搭桥 E5", staged.EditText);

        // 「计入同一枚额度」：一枚匠人 + 一次改造 = 1 枚额度，不是 2。
        Assert.Equal("1 / 3", shown.QuotaText);
        Assert.Equal(1, shown.DeployUsed);
        Assert.True(shown.CanConfirm);

        // 已选目标单独一类高亮，与候选分得开。
        Assert.Equal(["E5"], shown.Highlights.Where(h => h.Kind == HighlightKind.ChosenEdit).Select(h => h.Coord).Notations());
        Assert.DoesNotContain(shown.Highlights, h => h is { Kind: HighlightKind.EditTarget, Coord.X: 4, Coord.Y: 4 });

        ArtisanEditView artisan = Assert.Single(shown.ArtisanEdits);
        Assert.Equal((TestMaps.At("F5"), "搭桥 E5"), (artisan.ArtisanCell, artisan.ChosenText));
        Assert.Single(artisan.Targets, t => t.IsChosen);
    }

    [Fact]
    public void 显示可改造目标()
    {
        // Scenario「显示可改造目标」：匠人暂放在四邻含深水与林地的格上 → 界面列出可搭桥的深水格、可烧的林地格与可立栅的边。
        // F5 的四邻是 E5（深水）、G5（林地）、F4、F6。
        // 变异验证 M-SC1（实做）：BatchPreviewBuilder 的 EditOptions 只留以落点为端的内圈边（回到 T-11 之前的口径）→ 本测试红（16 → 4）。
        GameBoard board = Board();
        PreviewPresentation shown = Shown(board, BatchFixtures.P("F5", PieceType.Artisan));

        ArtisanEditView artisan = Assert.Single(shown.ArtisanEdits);
        Assert.Null(artisan.Chosen);
        Assert.Equal(ArtisanEditView.NoEditText, artisan.ChosenText);
        Assert.Equal(["E5"], artisan.BridgeCells.Notations());
        Assert.Equal(["G5"], artisan.BurnCells.Notations());

        // 裁决 T-11：边目标 16 条（4 条内圈 + 12 条外圈），互不重复。
        Assert.Equal(16, artisan.FenceEdges.Length);
        Assert.Equal(16, artisan.FenceEdges.Distinct().Count());
        Assert.Equal(4, artisan.FenceEdges.Count(e => e.A == TestMaps.At("F5") || e.B == TestMaps.At("F5")));
        Assert.Contains(new FenceEdge(TestMaps.At("F5"), TestMaps.At("F6")), artisan.FenceEdges);   // 内圈
        Assert.Contains(new FenceEdge(TestMaps.At("F6"), TestMaps.At("F7")), artisan.FenceEdges);   // 外圈

        // 格目标进格高亮，边目标进边高亮——立栅的目标是边，格高亮表达不了它。
        Assert.Equal(["E5", "G5"], shown.Highlights.Where(h => h.Kind == HighlightKind.EditTarget).Select(h => h.Coord).Notations());
        Assert.Equal(16, shown.EdgeHighlights.Count(h => h.Kind == HighlightKind.EditTarget));
        Assert.Empty(shown.EdgeHighlights.Where(h => h.Kind == HighlightKind.ChosenEdit));

        // 文案带动作名与目标记法。
        Assert.Contains("搭桥 E5", artisan.Targets.Select(t => t.Text));
        Assert.Contains("烧林 G5", artisan.Targets.Select(t => t.Text));
        Assert.Contains("立栅 F5–F6", artisan.Targets.Select(t => t.Text));
    }

    [Fact]
    public void 可改造目标逐条来自改造合法性唯一实现()
    {
        // 守门（tasks 4.1「合法目标必须来自 TerrainEditRules，表现层不得自算」）：预演给出的集合与唯一实现的输出逐条相等，
        // 顺序也相同——表现层若自己过滤 / 补漏 / 重排，这里立刻红。
        // 变异验证 M-SC1（实做）：Core 侧少给外圈边 → 本测试红（与唯一实现的输出不再逐条相等）。
        GameBoard board = TestMaps.Blank(
            TestMaps.Terrain(
                surfaces: [("E5", Surface.DeepWater), ("G5", Surface.Forest), ("F7", Surface.DeepWater)],
                bridges: ["F7"],
                fences: [("F5", "F6")]),
            size: 9);

        foreach (Coord cell in board.AllCoords())
        {
            // 落点本身非法（未架桥深水）时预演一样列目标：EditOptions 不因失败而清空，玩家才能换目标或换落点。
            PreviewPresentation shown = Shown(board, new Placement(cell, PieceType.Artisan));
            ArtisanEditView artisan = Assert.Single(shown.ArtisanEdits);
            ImmutableArray<TerrainEdit> expected = TerrainEditRules.LegalTargets(board.Map, cell);
            Assert.Equal(expected, [.. artisan.Targets.Select(t => t.Edit)]);
        }

        // 反面：唯一实现在这张盘面上确实给出了三种动作，否则上面的"逐条相等"可能只是两边都空。
        ImmutableArray<TerrainEdit> f5 = TerrainEditRules.LegalTargets(board.Map, TestMaps.At("F5"));
        Assert.Equal([TerrainEditKind.Bridge, TerrainEditKind.Fence, TerrainEditKind.Burn], f5.Select(e => e.Kind).Distinct().Order());

        // 已架桥的 F7、已有栅栏的 F5–F6 不在集合里（R-5：没有逆向动作）。
        Assert.DoesNotContain(TerrainEdit.Bridge(TestMaps.At("F7")), f5);
        Assert.DoesNotContain(TerrainEdit.Fence(TestMaps.At("F5"), TestMaps.At("F6")), f5);
    }

    [Fact]
    public void 可改造目标按批次开始前的地形枚举()
    {
        // design 默认 2「批次内不链式」/ D-B′：合法目标按<b>批次开始前</b>的地形枚举，与预演第 1 步同源——
        // 同一批里前一枚匠人要烧的林地，对后一枚匠人来说<b>仍然</b>是候选（它此刻还是林地）。
        // 「同一目标批内唯一」是批次层的判定（DuplicateEditInBatch），不在这里预先剔除，否则就成了第二实现。
        // 变异验证 M-SC6（实做）：EditOptions 改按 rehearsal.ProjectedBoard 的地形枚举 → 本测试与 `显示改造目标` 红。
        GameBoard board = Board();
        PreviewPresentation shown = Shown(
            board,
            BatchFixtures.Artisan("F5", TerrainEdit.Burn(TestMaps.At("G5"))),
            BatchFixtures.P("H5", PieceType.Artisan));

        Assert.True(shown.CanConfirm);
        ArtisanEditView second = Assert.Single(shown.ArtisanEdits, a => a.ArtisanCell == TestMaps.At("H5"));
        Assert.Equal(["G5"], second.BurnCells.Notations());
        Assert.Equal(TerrainEditRules.LegalTargets(board.Map, TestMaps.At("H5")), [.. second.Targets.Select(t => t.Edit)]);

        // 对照：第一枚匠人确实把 G5 烧了（否则上面的"仍然是候选"是空断言）。
        ArtisanEditView first = Assert.Single(shown.ArtisanEdits, a => a.ArtisanCell == TestMaps.At("F5"));
        Assert.Equal(TerrainEdit.Burn(TestMaps.At("G5")), first.Chosen);
    }

    [Fact]
    public void 改造后的气与自杀风险()
    {
        // Scenario「改造后的气与自杀风险」：指定的立栅会使己方棋串在结算后无气 → 按应用该栅栏后的地形标示自杀风险并高亮其位置。
        // P0 的 E5 孤子四邻 D5/F5/E4/E6：D5/E4/E6 由 P1 占住，只剩 E5–F5 一口气；匠人落 F6（不与 E5 相邻）给 E5–F5 立栅。
        // 「改造先于提子」的结算顺序由段 B 的 TerrainEditing 用例守着；本用例钉的是它在表现层的出口——
        // 自杀风险与那道栅栏的高亮同时出现。本段没有为它实做新变异。
        GameBoard board = TestMaps.Blank(size: 9)
            .Place("E5", P0)
            .Place("D5", P1).Place("E4", P1).Place("E6", P1);
        TerrainEdit fence = TerrainEdit.Fence(TestMaps.At("E5"), TestMaps.At("F5"));
        PreviewPresentation shown = Shown(board, BatchFixtures.Artisan("F6", fence));

        Assert.False(shown.CanConfirm);
        Assert.Equal(BatchFailureKind.Suicide, shown.Failure!.Kind);
        OwnGroupView risky = Assert.Single(shown.OwnGroups, g => g.IsSuicideRisk);
        Assert.Equal(["E5"], risky.Stones.Notations());
        Assert.Equal(DangerLevel.NoLiberty, risky.Danger);

        // 「高亮其位置」：无气的棋子按自杀风险高亮，那道栅栏按已选改造高亮——玩家才看得出是哪一手堵死了自己。
        Assert.Equal(["E5"], shown.Highlights.Where(h => h.Kind == HighlightKind.SuicideRisk).Select(h => h.Coord).Notations());
        Assert.Equal(fence.Edge, Assert.Single(shown.EdgeHighlights, h => h.Kind == HighlightKind.ChosenEdit).Edge);

        // 对照：同一落点不立栅就没有自杀风险——风险确实来自改造后的地形，不是落点本身。
        PreviewPresentation without = Shown(board, BatchFixtures.P("F6", PieceType.Artisan));
        Assert.True(without.CanConfirm);
        Assert.DoesNotContain(without.OwnGroups, g => g.IsSuicideRisk);
    }

    [Fact]
    public void 改造目标非法时仍列出全部合法目标()
    {
        // 裁决 T-6 / 玩家体验：失败时 EditOptions 照样有值，玩家才能换一个合法目标。
        // 同时钉住"已选目标不在合法集合里也要画出来"——否则玩家看不懂失败说的是哪条边。
        GameBoard board = Board();
        TerrainEdit far = TerrainEdit.Fence(TestMaps.At("A1"), TestMaps.At("A2"));
        PreviewPresentation shown = Shown(board, BatchFixtures.Artisan("F5", far));

        Assert.False(shown.CanConfirm);
        Assert.Equal(BatchFailureKind.TerrainEditIllegal, shown.Failure!.Kind);
        Assert.Equal("改造目标非法", shown.Failure.Title);

        ArtisanEditView artisan = Assert.Single(shown.ArtisanEdits);
        Assert.Equal(far, artisan.Chosen);
        Assert.Equal(18, artisan.Targets.Length);               // 16 条边 + 1 个深水格 + 1 个林地格
        Assert.DoesNotContain(artisan.Targets, t => t.IsChosen);
        Assert.Equal(far.Edge, Assert.Single(shown.EdgeHighlights, h => h.Kind == HighlightKind.ChosenEdit).Edge);
    }
}
