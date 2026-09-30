using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.TacticalLayers;

/// <summary>
/// 规格：tactical-layers —— Requirement: 单子禁手的标示（change `forbidden-marks`）。本文件是查询部分（tasks 1.1 / 1.2）：
/// 9×9 流程夹具、第 5 大回合（全图可落子），盘面直接摆出。对照与预筛见 <c>.对照.cs</c>，呈现见 <c>.呈现.cs</c>。
/// </summary>
public partial class 单子禁手的标示Tests
{
    /// <summary>P1 的四枚单子围住空格 E5，各自另有不止一口气。</summary>
    private static readonly string[] AroundE5 = ["E4", "E6", "D5", "F5"];

    private static MatchFlow Surrounded() => AiFixtures.Round5().Stones(P1, AroundE5);

    /// <summary>禁手集合的值投影：<c>坐标:类别[:同形序号][:活形所有者]</c>，坐标序。</summary>
    internal static string[] Project(ForbiddenMoveReport report) =>
    [
        .. report.Moves.Select(m =>
            $"{m.Coord.ToNotation()}:{m.Kind}"
            + (m.DuplicateOfSequence is { } s ? $":#{s}" : string.Empty)
            + (m.LifeOwner is { } o ? $":{o}" : string.Empty)),
    ];

    [Fact]
    public void 查询_自杀点()
    {
        // 规格 Scenario「自杀点事先打叉」的查询一半：轮到 P0，空格 E5 四邻全是 P1 的棋子且各有不止一口气 → E5 是禁手、原因自杀手；别的格都不是。
        // 变异 M-Q1（查询把预演给出的自杀手失败当作非禁手）→ 红 11；M-P3（预筛去掉"自身有气"）→ 红 10；本测试均在其中。
        // 变异 M-C1（对局流程的入口不把暂放交给查询）→ 红 4；M-C2（候选不排除已暂放的格）→ 红 10。红数是本 Requirement 三个测试类合计。
        MatchFlow match = Surrounded();
        Assert.All(AroundE5, s => Assert.True(match.Board.LibertiesOf(match.Board.GroupAt(TestMaps.At(s))!).Length > 1));

        ForbiddenMoveReport report = match.ForbiddenMovesOfCurrentPlayer()!;

        Assert.Equal(P0, report.Player);
        Assert.Equal(["E5:Suicide"], Project(report));
        Assert.Equal(ForbiddenMoveKind.Suicide, report.At(TestMaps.At("E5"))!.Kind);
        Assert.Null(report.At(TestMaps.At("A5")));

        // 候选 = 合法落子范围内的空格：81 格 − 4 枚棋子。进入部署阶段（暂放为空）结果相同。
        Assert.Equal(77, report.CandidateCount);
        match.OpenDeploy();
        Assert.Equal(["E5:Suicide"], Project(match.ForbiddenMovesOfCurrentPlayer()!));
    }

    [Fact]
    public void 查询_能提子的点不是自杀点()
    {
        // 规格 Scenario「能提子的点不是自杀点」：E5 四邻全是 P1 的棋子，其中 E4 只剩 E5 这一口气（D4 / F4 / E3 是 P0）→ 落 E5 提走 E4，不是自杀手。
        MatchFlow match = Surrounded().Stones(P0, "D4", "F4", "E3");
        Assert.Equal(["E5"], match.Board.LibertiesOf(match.Board.GroupAt(TestMaps.At("E4"))!).Notations());

        ForbiddenMoveReport report = match.ForbiddenMovesOfCurrentPlayer()!;

        Assert.Null(report.At(TestMaps.At("E5")));
        Assert.Empty(report.Moves);
    }

    [Fact]
    public void 查询_劫点带重复的提交序号()
    {
        // 规格 Scenario「劫点事先打叉」：双劫盘面（BatchFixtures.KoBoard，P1 持两劫）。第 1 次提交 P0 落 A1、第 2 次提交 P1 落 A9（都远离劫争），
        // 第 3 次提交 P0 在 D4 提走 P1 的 C4。此刻 P1 立刻在 C4 落子会提回 D4，盘面与第 2 次提交后的盘面重复 → C4 是禁手、原因同形、序号 2。
        GameBoard board = BatchFixtures.KoBoard(TestMaps.P1, TestMaps.P1);
        SettlementDriver driver = BatchFixtures.Driver(board);
        Assert.True(driver.Confirm(BatchFixtures.Context(board, P0), [BatchFixtures.P("A1")]).Confirmed);
        Assert.True(driver.Confirm(BatchFixtures.Context(board, P1), [BatchFixtures.P("A9")]).Confirmed);
        SettlementOutcome taken = BatchFixtures.TakeKo(driver, P0, 'A', PieceType.Basic);
        Assert.True(taken.Confirmed);
        Assert.Equal(["C4"], taken.CaptureRecord!.Captured.Select(s => s.Coord).Notations());

        ForbiddenMoveReport report = ForbiddenMoves.Query(board, P1, RangeOf(board, P1), [], driver.History);

        ForbiddenMove ko = report.At(TestMaps.At("C4"))!;
        Assert.Equal(ForbiddenMoveKind.Superko, ko.Kind);
        Assert.Equal(2, ko.DuplicateOfSequence);
        Assert.Equal(["C4:Superko:#2"], Project(report));

        // 禁手是"对谁而言"的：同一格 C4 对 P0 只是粘劫（与 B4 / C3 / C5 连成一串、不提子），盘面不重复 → 不是禁手。
        Assert.Null(ForbiddenMoves.Query(board, P0, RangeOf(board, P0), [], driver.History).At(TestMaps.At("C4")));
    }

    [Fact]
    public void 查询_破坏活形带所有者()
    {
        // P0 一字两眼（A2–E2，单格眼 A1 / E1，B1 / C1 / D1 / F1 是岩石）。P1 暂放匠人 C3、在 C2–D2 立栅：P0 串被切成两条各一眼 → 破坏活形。
        // 整批判定：暂放已破坏活形，再落任何一子整批仍被第 6 步拒绝 → 全部候选格都是禁手，原因破坏活形、所有者 P0。
        // 撤掉改造（匠人不改造）后，没有任何禁手。
        GameBoard board = LifeShapeFixtures.Grid(
        [
            ".......",
            ".......",
            "00000..",
            ".###.#.",
        ]);
        Assert.Equal(LifeState.Alive, LifeShapeReport.Analyze(board).LifeOf("A2"));
        IReadOnlySet<Coord> range = RangeOf(board, P1);
        Assert.DoesNotContain(TestMaps.At("A1"), range);
        Assert.DoesNotContain(TestMaps.At("E1"), range);
        Placement[] cut = [BatchFixtures.Artisan("C3", TerrainEdit.Fence(TestMaps.At("C2"), TestMaps.At("D2")))];

        ForbiddenMoveReport report = ForbiddenMoves.Query(board, P1, range, cut, new BoardHistory());

        // 候选：4×7 = 28 格 − 4 岩石 − 5 棋子 − 2 禁入眼 − 1 暂放 = 16。
        Assert.Equal(16, report.CandidateCount);
        Assert.Equal(16, report.Moves.Length);
        Assert.All(report.Moves, m => Assert.Equal((ForbiddenMoveKind.BreaksLife, (PlayerId?)P0), (m.Kind, m.LifeOwner)));
        Assert.Null(report.At(TestMaps.At("C3")));

        Assert.Empty(ForbiddenMoves.Query(board, P1, range, [BatchFixtures.P("C3", PieceType.Artisan)], new BoardHistory()).Moves);
    }

    [Fact]
    public void 查询_暂放改变禁手()
    {
        // 规格 Scenario「暂放改变禁手」：E5 对 P0 是自杀点（E4 还有 E3 一口气）。P0 先暂放 E3 → 再落 E5 时整批提走 E4 → E5 不再是禁手；撤回后又是。
        MatchFlow match = Surrounded().Stones(P0, "D4", "F4");
        Assert.Equal(["E3", "E5"], match.Board.LibertiesOf(match.Board.GroupAt(TestMaps.At("E4"))!).Notations());
        StagedBatch batch = match.OpenDeploy();
        Assert.Equal(["E5:Suicide"], Project(match.ForbiddenMovesOfCurrentPlayer()!));

        Assert.Null(batch.Stage(TestMaps.At("E3"), PieceType.Basic));
        ForbiddenMoveReport staged = match.ForbiddenMovesOfCurrentPlayer()!;

        Assert.Empty(staged.Moves);
        // 已暂放的格不是候选。
        Assert.Equal(74, staged.CandidateCount);

        Assert.True(batch.Unstage(TestMaps.At("E3")));
        Assert.Equal(["E5:Suicide"], Project(match.ForbiddenMovesOfCurrentPlayer()!));
    }

    [Fact]
    public void 查询_暂放自身非法时按整批判定()
    {
        // 整批判定的另一面：P0 先把子暂放进自杀点 E5（暂放只过预演第 1–2 步）。此后再落一子，整批仍因 E5 无气被拒 → 其余候选格都是自杀手；
        // 只有 E3 例外——落 E3 后整批提走 E4，E5 得气。
        MatchFlow match = Surrounded().Stones(P0, "D4", "F4");
        StagedBatch batch = match.OpenDeploy();
        Assert.Null(batch.Stage(TestMaps.At("E5"), PieceType.Basic));
        Assert.Equal(BatchFailureKind.Suicide, match.Rehearse().Failure!.Kind);

        ForbiddenMoveReport report = match.ForbiddenMovesOfCurrentPlayer()!;

        Assert.Equal(74, report.CandidateCount);
        Assert.Equal(73, report.Moves.Length);
        Assert.Null(report.At(TestMaps.At("E3")));
        Assert.All(report.Moves, m => Assert.Equal(ForbiddenMoveKind.Suicide, m.Kind));
    }

    [Fact]
    public void 查询_库存与额度不影响禁手()
    {
        // 规格 Scenario「库存不影响禁手」：P0 手牌为空、部署额度为 0，E5 对 P0 仍是禁手。
        MatchFlow match = Surrounded();
        match.Debug.SeedHand(P0);
        match.SetDeployLimit(0);
        StagedBatch batch = match.OpenDeploy();
        Assert.Equal(0, batch.Context.DeployLimit);
        Assert.Equal(0, batch.Context.StockOf(PieceType.Basic));
        Assert.NotNull(batch.Stage(TestMaps.At("A5"), PieceType.Basic));

        Assert.Equal(["E5:Suicide"], Project(match.ForbiddenMovesOfCurrentPlayer()!));
    }

    [Fact]
    public void 查询_不改对局状态()
    {
        // 规格：查询 MUST NOT 改变对局状态。部署阶段、已有一枚暂放时查询前后，对局权威状态的指纹（盘面、历史、账本、流程、暂放……）逐字相同。
        MatchFlow match = Surrounded().Stones(P0, "D4", "F4");
        StagedBatch batch = match.OpenDeploy();
        Assert.Null(batch.Stage(TestMaps.At("A5"), PieceType.Basic));
        string before = Fingerprint(match);

        ForbiddenMoveReport report = match.ForbiddenMovesOfCurrentPlayer()!;

        Assert.NotEmpty(report.Moves);
        Assert.Equal(before, Fingerprint(match));
    }

    [Fact]
    public void 查询_没有当前行动玩家时为空()
    {
        // 插旗阶段没有当前行动玩家 → 查询为 null（D6：此时不显示）。
        Assert.Null(MatchFixtures.Create().ForbiddenMovesOfCurrentPlayer());
    }

    /// <summary>合法落子范围契约（第 5 大回合：全图可落子格扣除禁入格）。</summary>
    internal static IReadOnlySet<Coord> RangeOf(GameBoard board, PlayerId player) =>
        PublicRules.LegalRange(board, majorRound: 5, birthZone: 0, player, LifeShapeReport.Analyze(board));
}
