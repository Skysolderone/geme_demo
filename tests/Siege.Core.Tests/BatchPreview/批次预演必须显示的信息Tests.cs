using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Preview;
using Siege.Presentation.Layers;
using Siege.Presentation.Preview;
using Siege.Presentation.Visibility;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.BatchPreview;

/// <summary>规格：batch-preview —— Requirement: 批次预演必须显示的信息</summary>
public class 批次预演必须显示的信息Tests
{
    [Fact]
    public void 显示预计提子()
    {
        // 设计文档 §14.1 / implement 2.3：暂放将使敌方 5 枚棋串无气 → 明确标示该棋串将被提走。
        // P1 的 C1–G1 五子只剩 D2 一口气；P0 暂放 D2。
        // 变异验证 M-P1：BatchPreviewBuilder.GroupCaptures 改为每枚提子各成一串（不按棋串分组）→ 本测试红 1（Captures 长度 5 而非 1）。
        GameBoard board = TestMaps.Blank(size: 9)
            .Place("C1", P1).Place("D1", P1).Place("E1", P1).Place("F1", P1).Place("G1", P1)
            .Place("B1", P0).Place("H1", P0).Place("C2", P0).Place("E2", P0).Place("F2", P0).Place("G2", P0);
        Assert.Equal(["D2"], board.LibertiesOf(board.GroupAt(TestMaps.At("C1"))!).Notations());
        string before = board.Serialize();

        Core.Preview.BatchPreview preview = RichPreview(board, P0, Roster(P0, P1), limit: 3, stock: null, history: null, ("D2", PieceType.Basic));
        PreviewPresentation shown = PreviewPresentation.Build(preview, EmptyHand(P0), LibertyThresholds.Default);

        CapturedGroup captured = Assert.Single(preview.Captures);
        Assert.Equal(P1, captured.Owner);
        Assert.Equal(["C1", "D1", "E1", "F1", "G1"], captured.Coords.Notations());
        CaptureView view = Assert.Single(shown.Captures);
        Assert.Contains("将提走", view.Text);
        Assert.Contains("5 子棋串", view.Text);
        Assert.Contains("C1、D1、E1、F1、G1", view.Text);
        Assert.Equal(["C1", "D1", "E1", "F1", "G1"], shown.Highlights.Where(h => h.Kind == HighlightKind.PredictedCapture).Select(h => h.Coord).Notations());
        Assert.True(shown.CanConfirm);
        Assert.Equal(before, board.Serialize());
    }

    [Fact]
    public void 显示自杀风险()
    {
        // 设计文档 §6.1 第 5 步 / implement 2.4：P1 围出 C4/D4/E4 口袋，P0 已有 E4；暂放 D4、C4 后三子合成无气棋串。
        // 暂放阶段只校验第 1–2 步，所以这个组合能暂放；预演 MUST 标示风险并高亮整条棋串（含非落点 E4）。
        // 变异验证 M-P2：BatchPreviewBuilder.OwnGroups 的 IsSuicideRisk 改为 `false` → 本测试红 1。
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("B4", P1).Place("C3", P1).Place("C5", P1).Place("D3", P1).Place("D5", P1)
            .Place("E3", P1).Place("E5", P1).Place("F4", P1)
            .Place("E4", P0);
        var batch = new StagedBatch(board, BatchFixtures.Context(board, P0));
        Assert.Null(batch.Stage(TestMaps.At("D4"), PieceType.Basic));
        Assert.Null(batch.Stage(TestMaps.At("C4"), PieceType.Basic));

        Core.Preview.BatchPreview preview = BatchPreviewBuilder.Build(board, batch.Context, batch.Placements, new BoardHistory(), Roster(P0, P1), EmptyRelics(board), 5);
        PreviewPresentation shown = PreviewPresentation.Build(preview, EmptyHand(P0), LibertyThresholds.Default);

        Assert.False(shown.CanConfirm);
        OwnGroupView risky = Assert.Single(shown.OwnGroups, g => g.IsSuicideRisk);
        Assert.Equal(["C4", "D4", "E4"], risky.Stones.Notations());
        Assert.Equal(DangerLevel.NoLiberty, risky.Danger);
        Assert.Contains("自杀风险", risky.LibertyText);
        Assert.Equal(["C4", "D4", "E4"], shown.Highlights.Where(h => h.Kind == HighlightKind.SuicideRisk).Select(h => h.Coord).Notations());
        Assert.Equal(preview.SuicideStones.Notations(), risky.Stones.Notations());
    }

    [Fact]
    public void 显示军势预览()
    {
        // 设计文档 §10.1 算例：普通子×3 + 堡垒子×1 + 倍增子×2 → 基础 9、倍率 2.25、军势 ⌊9×2.25⌋ = 20。
        // 盘面已有 C3–E3 普通子、F3 堡垒子、G3 倍增子；暂放一枚倍增子 H3 接入。
        // 变异验证 M-P3：GroupPowerView.From 的 MultiplierText 改用 `power.MultiplierCount.ToString()` → 本测试红 1（"2" ≠ "2.25"）。
        GameBoard board = TestMaps.Blank(size: 9)
            .Place("C3", P0).Place("D3", P0).Place("E3", P0)
            .Place("F3", P0, PieceType.Fortress).Place("G3", P0, PieceType.Multiplier);

        Core.Preview.BatchPreview preview = RichPreview(board, P0, Roster(P0), limit: 3, stock: null, history: null, ("H3", PieceType.Multiplier));
        PreviewPresentation shown = PreviewPresentation.Build(preview, EmptyHand(P0), LibertyThresholds.Default);

        OwnGroupView group = Assert.Single(shown.OwnGroups);
        Assert.True(group.ContainsPlacement);
        GroupPowerView power = group.Power!;
        Assert.Equal(9, power.BaseTotal);
        Assert.Equal(0, power.PositionBonus);
        Assert.Equal(2, power.MultiplierCount);
        Assert.Equal(2, power.EffectiveExponent);
        Assert.Equal("2.25", power.MultiplierText);
        Assert.Equal(20, power.Power);
        Assert.Equal("（基础 9 + 位置加值 0）× 2.25 = 20", power.FormulaText);
    }

    [Fact]
    public void 显示势力与排名变化()
    {
        // 规格算例：势力 45 → 62、排名第 3 → 第 2；裁决 1：他人被挤动的名次也要显示。11×11 空盘，四方各据一列互不覆盖：
        //   P0 A1–A6 堡垒 + A7 倍增：⌊25×1.5⌋=37 + 独占 B1–B7、A8 共 8 = 45；暂放 A8 堡垒、A9 堡垒、A10 普通 → ⌊34×1.5⌋=51 + 独占 B1–B10、A11 共 11 = 62。
        //   P1 H1–H4 堡垒 + H5、H6 倍增：⌊18×2.25⌋=40 + 独占 G1–G6、J1–J6、H7 共 13 = 53（第 2 → 第 3）。
        //   P2 L1–L6 堡垒 + L7、L8 倍增：⌊26×2.25⌋=58 + 独占 K1–K8、L9 共 9 = 67（第 1 不变）。
        //   P3 E1、E2 堡垒 + E3 倍增：⌊9×1.5⌋=13 + 独占 D1–D3、F1–F3、E4 共 7 = 20（第 4 不变）。
        // 变异验证 M-P4：BatchPreviewBuilder 的 RankBefore 改读 after.RankOf → 本测试红 1。
        GameBoard board = TestMaps.Blank(size: 11);
        Column(board, P0, "A", 1, PieceType.Fortress, PieceType.Fortress, PieceType.Fortress, PieceType.Fortress, PieceType.Fortress, PieceType.Fortress, PieceType.Multiplier);
        Column(board, P1, "H", 1, PieceType.Fortress, PieceType.Fortress, PieceType.Fortress, PieceType.Fortress, PieceType.Multiplier, PieceType.Multiplier);
        Column(board, P2, "L", 1, PieceType.Fortress, PieceType.Fortress, PieceType.Fortress, PieceType.Fortress, PieceType.Fortress, PieceType.Fortress, PieceType.Multiplier, PieceType.Multiplier);
        Column(board, P3, "E", 1, PieceType.Fortress, PieceType.Fortress, PieceType.Multiplier);

        Core.Preview.BatchPreview preview = RichPreview(board, P0, Roster(P0, P1, P2, P3), limit: 3,
            stock: BatchFixtures.Stock((PieceType.Fortress, 2), (PieceType.Basic, 1)), history: null,
            ("A8", PieceType.Fortress), ("A9", PieceType.Fortress), ("A10", PieceType.Basic));
        PreviewPresentation shown = PreviewPresentation.Build(preview, EmptyHand(P0), LibertyThresholds.Default);

        PowerChangeView mine = Assert.Single(shown.PowerChanges, c => c.IsViewer);
        Assert.Equal((45L, 62L, 17L, 3, 2), (mine.Before, mine.After, mine.Delta, mine.RankBefore!.Value, mine.RankAfter!.Value));
        Assert.Contains("势力 45 → 62", mine.Text);
        Assert.Contains("排名 第 3 → 第 2", mine.Text);

        PowerChangeView pushed = Assert.Single(shown.PowerChanges, c => c.Player == P1);
        Assert.Equal((53L, 53L, 2, 3), (pushed.Before, pushed.After, pushed.RankBefore!.Value, pushed.RankAfter!.Value));
        Assert.True(pushed.RankChanged);
        Assert.Equal((67L, 1, 1), (shown.PowerChanges.Single(c => c.Player == P2).After, shown.PowerChanges.Single(c => c.Player == P2).RankBefore!.Value, shown.PowerChanges.Single(c => c.Player == P2).RankAfter!.Value));
        Assert.Equal((20L, 4, 4), (shown.PowerChanges.Single(c => c.Player == P3).After, shown.PowerChanges.Single(c => c.Player == P3).RankBefore!.Value, shown.PowerChanges.Single(c => c.Player == P3).RankAfter!.Value));
        Assert.Equal(4, shown.PowerChanges.Length);
    }

    [Fact]
    public void 显示部署额度()
    {
        // 规格算例：部署上限 5、已暂放 3 → "3 / 5"。走完整的 MatchFlow → PreviewCurrentBatch → ViewerWorld 链路。
        // 变异验证 M-P5：PreviewPresentation.QuotaText 改为 `$"{preview.DeployUsed}/{preview.DeployLimit}"` → 本测试红 1。
        MatchFlow match = AiFixtures.Round5();
        match.SetDeployLimit(5);
        StagedBatch batch = match.OpenDeploy();
        Assert.Null(batch.Stage(TestMaps.At("B2"), PieceType.Basic));
        Assert.Null(batch.Stage(TestMaps.At("E5"), PieceType.Basic));
        Assert.Null(batch.Stage(TestMaps.At("H8"), PieceType.Basic));

        PreviewPresentation shown = match.World(P0).Preview()!;

        Assert.Equal("3 / 5", shown.QuotaText);
        Assert.Equal((3, 5), (shown.DeployUsed, shown.DeployLimit));
        HandCostView cost = Assert.Single(shown.HandCosts);
        Assert.Equal("普通子 ×3（库存 50）", cost.Text);
        Assert.Equal(["B2", "E5", "H8"], shown.StagedPieces.Select(p => p.Coord).Notations());
    }

    [Fact]
    public void Pass确认前警示撤销本轮新征募()
    {
        // implement 2.11 / 裁决 4（设计文档 §5.5）：本轮新征募 3 枚且暂放为 0 → 确认前提示"确认 0 落子将撤销本轮新征募的 3 枚棋子"，不要求二次确认。
        // 变异验证 M-P6：PassWarning 改读 ownHand.TotalCount → 本测试红 1（53 ≠ 3）。
        MatchFlow match = AiFixtures.Round5();
        match.BeginTurn();
        Recruit.RecruitPanelView panel = match.EnterRecruit();
        Recruit.PlayerHandAccess hand = match.CurrentHand();
        foreach (int index in panel.Candidates.Where(c => c.IsSelectable).Take(3).Select(c => c.Index))
        {
            hand.Pick(index);
        }

        Assert.Equal(3, hand.PrivateView().PendingGained);
        match.EnterDeploy();

        PreviewPresentation shown = match.World(P0).Preview()!;
        Assert.Equal("确认 0 落子将撤销本轮新征募的 3 枚棋子", shown.PassWarning);
        Assert.True(shown.CanConfirm);

        Assert.Null(match.CurrentBatch!.Stage(TestMaps.At("E5"), PieceType.Basic));
        Assert.Null(match.World(P0).Preview()!.PassWarning);
    }

    private static void Column(GameBoard board, PlayerId owner, string column, int fromRow, params PieceType[] types)
    {
        for (int i = 0; i < types.Length; i++)
        {
            board.Place(Coord.Parse($"{column}{fromRow + i}"), owner, types[i]);
        }
    }
}
