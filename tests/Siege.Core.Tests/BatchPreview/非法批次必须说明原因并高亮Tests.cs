using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Scoring;
using Siege.Core.Preview;
using Siege.Presentation.Layers;
using Siege.Presentation.Preview;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.BatchPreview;

/// <summary>规格：batch-preview —— Requirement: 非法批次必须说明原因并高亮</summary>
public class 非法批次必须说明原因并高亮Tests
{
    [Fact]
    public void 自杀手高亮()
    {
        // 设计文档 §6.1 第 5 步：确认自杀手批次 → 说明"自杀手"并高亮结算后仍无气的整条己方棋串（含非落点 E4）。
        // 变异验证 M-F1：FailurePresentation.From 对自杀手也用 FailureFocus 类别 → 本测试红 1。
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("B4", P1).Place("C3", P1).Place("C5", P1).Place("D3", P1).Place("D5", P1)
            .Place("E3", P1).Place("E5", P1).Place("F4", P1)
            .Place("E4", P0);
        SettlementDriver driver = BatchFixtures.Driver(board);

        SettlementOutcome outcome = driver.Confirm(BatchFixtures.Context(board, P0), [BatchFixtures.P("D4"), BatchFixtures.P("C4")]);
        FailurePresentation shown = FailurePresentation.From(outcome.Failure!);

        Assert.False(outcome.Confirmed);
        Assert.Equal("自杀手", shown.Title);
        Assert.Contains("C4、D4、E4", shown.Detail);
        Assert.All(shown.Highlights, h => Assert.Equal(HighlightKind.SuicideRisk, h.Kind));
        Assert.Equal(["C4", "D4", "E4"], shown.Highlights.Select(h => h.Coord).Notations());
    }

    [Fact]
    public void 同形说明()
    {
        // 设计文档 §6.2：结算后盘面与第 12 次提交相同 → 说明触发同形禁则并指出第 12 次提交。预演与确认两条路径给出同一说明。
        // 变异验证 M-F2：FailurePresentation.From 的同形详情不写序号（改为"与历史盘面相同"）→ 本测试红 1。
        GameBoard board = BatchFixtures.KoBoard(holderA: P1, holderB: P1);
        SettlementDriver driver = BatchFixtures.Driver(board);
        KoScript.Play(driver, upTo: 12);
        Assert.True(BatchFixtures.TakeKo(driver, P0, 'A', PieceType.Fortress).Confirmed);

        Core.Preview.BatchPreview preview = BatchPreviewBuilder.Build(board, BatchFixtures.Context(board, P1),
            [new Placement(BatchFixtures.KoPoint('A', P1), PieceType.Basic)], driver.History, Roster(P0, P1), EmptyRelics(board), 5);
        PreviewPresentation previewShown = PreviewPresentation.Build(preview, EmptyHand(P1), LibertyThresholds.Default);
        SettlementOutcome outcome = BatchFixtures.TakeKo(driver, P1, 'A', PieceType.Basic);
        FailurePresentation confirmShown = FailurePresentation.From(outcome.Failure!);

        foreach (FailurePresentation shown in new[] { previewShown.Failure!, confirmShown })
        {
            Assert.Equal(BatchFailureKind.Superko, shown.Kind);
            Assert.Equal("盘面同形禁则", shown.Title);
            Assert.Contains("第 12 次提交", shown.Detail);
            Assert.Equal(12, shown.DuplicateOfSequence);
            Assert.Equal(["C4"], shown.Highlights.Select(h => h.Coord).Notations());
        }

        Assert.False(previewShown.CanConfirm);
    }

    [Fact]
    public void 预占腾空格的文案()
    {
        // 设计文档 §5.4：不能把棋子暂放到本批次预计会提空的格。P0 暂放 D2 将提走 C1–G1；再暂放 C1 → "该格当前已被占据"，而不是泛化的不可落子。
        // 变异验证 M-F3：FailurePresentation.TitleOf(Occupied) 改为"落点不可落子" → 本测试红 1（且"八类失败标题互不相同"红 1）。
        GameBoard board = TestMaps.Blank(size: 9)
            .Place("C1", P1).Place("D1", P1).Place("E1", P1).Place("F1", P1).Place("G1", P1)
            .Place("B1", P0).Place("H1", P0).Place("C2", P0).Place("E2", P0).Place("F2", P0).Place("G2", P0);
        var batch = new StagedBatch(board, BatchFixtures.Context(board, P0));
        Assert.Null(batch.Stage(TestMaps.At("D2"), PieceType.Basic));
        Core.Preview.BatchPreview preview = BatchPreviewBuilder.Build(board, batch.Context, batch.Placements, new BoardHistory(), Roster(P0, P1), EmptyRelics(board), 5);
        Assert.Contains(TestMaps.At("C1"), preview.CapturedCoords);

        BatchFailure failure = batch.Stage(TestMaps.At("C1"), PieceType.Basic)!;
        FailurePresentation shown = FailurePresentation.From(failure);

        Assert.Equal(BatchFailureKind.Occupied, shown.Kind);
        Assert.Equal("该格当前已被占据", shown.Title);
        Assert.NotEqual(FailurePresentation.TitleOf(BatchFailureKind.Unplayable), shown.Title);
        Assert.Contains("C1", shown.Detail);
        Assert.Equal([(TestMaps.At("C1"), HighlightKind.FailureFocus)], shown.Highlights.Select(h => (h.Coord, h.Kind)));
        Assert.Equal(1, batch.Count);
    }

    [Fact]
    public void 十类失败标题互不相同()
    {
        // 规格要求至少区分七类（+ 本层补充的批次内重复落点 + artisan-terrain-edit 的改造目标非法 / 批内重复改造目标）：
        // 每类都有非空且互不相同的标题。枚举是穷举的，新增类别忘了给标题会在这里抛 ArgumentOutOfRangeException。
        // 变异验证：M-F3（见上）→ 本测试红 1。
        string[] titles = [.. Enum.GetValues<BatchFailureKind>().Select(FailurePresentation.TitleOf)];

        Assert.Equal(10, titles.Length);
        Assert.Equal(titles.Length, titles.Distinct().Count());
        Assert.All(titles, t => Assert.False(string.IsNullOrWhiteSpace(t)));
        Assert.Equal("该格当前已被占据", FailurePresentation.TitleOf(BatchFailureKind.Occupied));
    }
}
