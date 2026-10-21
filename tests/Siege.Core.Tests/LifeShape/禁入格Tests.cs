using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;
using static Siege.Core.Tests.LifeShapeFixtures;

namespace Siege.Core.Tests.LifeShape;

/// <summary>规格：openspec/changes/life-shape/specs/life-shape —— Requirement: 禁入格</summary>
public class 禁入格Tests
{
    /// <summary>A 的一条两眼活形，眼空间 E5、G5；J7 的 B 子让外部不封闭。</summary>
    private static GameBoard TwoEyes() => Grid([
        "........1",
        "...00000.",
        "...0.0.0.",
        "...00000.",
        ".........",
        ".........",
        ".........",
    ]);

    [Fact]
    public void 他人活形的眼空间禁入()
    {
        LifeShapeReport report = LifeShapeReport.Analyze(TwoEyes());

        Assert.Equal(LifeState.Alive, report.LifeOf("D4"));
        Assert.Equal(["E5", "G5"], report.ProtectedCellsOf(A).Notations());
        Assert.Empty(report.Forbidden(A));
        Assert.False(report.IsForbiddenFor(A, TestMaps.At("E5")));
        foreach (PlayerId other in new[] { B, C, D })
        {
            Assert.Equal(["E5", "G5"], report.Forbidden(other));
            Assert.True(report.IsForbiddenFor(other, TestMaps.At("G5")));
            Assert.False(report.IsForbiddenFor(other, TestMaps.At("A1")));
        }
    }

    [Fact]
    public void 未定棋串不产生禁入()
    {
        GameBoard board = Grid([
            "........1",
            "...000...",
            "...0.0...",
            "...000...",
            ".........",
            ".........",
            ".........",
        ]);
        LifeShapeReport report = LifeShapeReport.Analyze(board);

        Assert.Equal(["E5"], report.EyeSpacesOf("D4"));
        Assert.Equal(LifeState.Undetermined, report.LifeOf("D4"));
        Assert.All(All, p => Assert.Empty(report.Forbidden(p)));
        Assert.Empty(report.ProtectedCellsOf(A));
        Assert.False(report.IsForbiddenFor(B, TestMaps.At("E5")));
    }

    [Fact]
    public void 弃赛者的活形仍禁入()
    {
        // 活形分析不接名册：棋子还在盘上，所有者从棋子读出，弃赛与否不影响禁入。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(6, TestMaps.All)
            .Stones(TestMaps.P3, "E5", "F5", "G5", "H5", "J5", "E6", "G6", "J6", "E7", "F7", "G7", "H7", "J7")
            .Stones(TestMaps.P1, "D10");
        match.Resign(TestMaps.P3);
        Assert.Equal(PlayerStatus.Resigned, match.StateOf(TestMaps.P3).Status);

        LifeShapeReport report = LifeShapeReport.Analyze(match.Board);

        Assert.Equal(LifeState.Alive, report.LifeOf("E5"));
        foreach (PlayerId active in new[] { TestMaps.P0, TestMaps.P1, TestMaps.P2 })
        {
            Assert.Equal(["F6", "H6"], report.Forbidden(active));
        }
    }

    [Fact]
    public void 活形解除后禁入同步解除()
    {
        GameBoard board = TwoEyes();
        Assert.Equal(["E5", "G5"], LifeShapeReport.Analyze(board).Forbidden(B));

        board.Place(TestMaps.At("E5"), A, PieceType.Basic);
        LifeShapeReport after = LifeShapeReport.Analyze(board);

        Assert.Equal(LifeState.Undetermined, after.LifeOf("D4"));
        Assert.All(new[] { B, C, D }, p => Assert.Empty(after.Forbidden(p)));
        Assert.Empty(after.ProtectedCellsOf(A));
    }

    // ---------- fence-eye-rule tasks 1.3：下游经活形查询自动跟随的算例 ----------

    /// <summary>
    /// 平坦 8×8 链接载体棋盘 0 左下角（棋盘外一律障碍）：P0 主串 C4–D4–D3 围出单格空区 C3 与 E3；E4 是 P0 的另一枚单子
    /// （D4–E4 之间有栅栏，不与主串相连；E5 未架桥深水、F4 是 P1，唯一的气是 E3）。E3 东侧 F3 由 <paramref name="eastOfEye"/> 决定：
    /// <c>"fence"</c> = P1 的棋子、E3–F3 之间有栅栏；<c>"water"</c> = 未架桥深水。
    /// </summary>
    private static MatchFlow FencedEyeMatch(string eastOfEye)
    {
        TerrainData terrain = eastOfEye == "fence"
            ? TestMaps.Terrain(surfaces: [("E5", Surface.DeepWater)], fences: [("D4", "E4"), ("E3", "F3")])
            : TestMaps.Terrain(surfaces: [("E5", Surface.DeepWater), ("F3", Surface.DeepWater)], fences: [("D4", "E4")]);
        MatchFlow match = LinkModeFixtures.PlainStarted(terrain)
            .AtRound(5, [TestMaps.P1, TestMaps.P0, TestMaps.P2, TestMaps.P3])
            .Stones(TestMaps.P0, "C4", "D4", "D3", "E4")
            .Stones(TestMaps.P1, "F4");
        return eastOfEye == "fence" ? match.Stones(TestMaps.P1, "F3") : match;
    }

    [Fact]
    public void 隔着栅栏贴眼的他人棋子解除禁入()
    {
        // fence-eye-rule tasks 1.3 算例（走真实对局流程，第 5 大回合 P1 行动）：P1 的 F3 隔着栅栏贴住 P0 的单格眼 E3。
        // 旧规则下栅栏算墙，E3 对 P0 封闭，主串 C3 + E3 两眼成活，C3 / E3 对 P1 禁入；新规则下 E3 沿眼边贴到 P1 的 F3、不封闭——
        // 主串只剩 C3 一个单格眼，未定，不受保护，禁入解除：合法落子范围、单子禁手查询与正式结算都经活形查询自动跟随，没有各自的栅栏判定。
        // 变异验证见 .trellis/tasks/10-21-fence-eye-rule/implement.md（M-F1：眼边加回"无栅栏"一条 → 本测试红）。
        MatchFlow match = FencedEyeMatch("fence");
        Assert.DoesNotContain(TestMaps.At("F3"), match.Board.LibertyNeighbors(TestMaps.At("E3")));   // 前提：E3 不是 F3 的气

        LifeShapeReport life = LifeShapeReport.Analyze(match.Board);
        Assert.Null(life.EyeSpaceAt(TestMaps.At("E3")));
        Assert.Equal(["C3"], life.EyeSpacesOf("C4"));
        Assert.Equal(LifeState.Undetermined, life.LifeOf("C4"));
        Assert.Empty(life.Forbidden(TestMaps.P1));

        // 合法落子范围（PublicRules.LegalRange 唯一扣除点）含 E3 与 C3。
        IReadOnlySet<Coord> range = match.LegalRangeFor(TestMaps.P1);
        Assert.Contains(TestMaps.At("E3"), range);
        Assert.Contains(TestMaps.At("C3"), range);

        // 单子禁手查询：E3 不是禁手（落下即提走 E4 单子）；C3 只是自杀手——禁入已解除，剩下的是普通的气的规则。
        ForbiddenMoveReport forbidden = match.ForbiddenMovesOfCurrentPlayer()!;
        Assert.Null(forbidden.At(TestMaps.At("E3")));
        Assert.Equal(ForbiddenMoveKind.Suicide, forbidden.At(TestMaps.At("C3"))!.Kind);

        // P1 正式落 E3：提走 E4，合法确认。
        SettlementOutcome outcome = match.PlayTurn("E3");
        Assert.True(outcome.Confirmed);
        Assert.Equal(TestMaps.P1, match.Board[TestMaps.At("E3")].Occupant!.Value.Owner);
        Assert.Null(match.Board[TestMaps.At("E4")].Occupant);
        Assert.Equal(TestMaps.P0, match.Board[TestMaps.At("D3")].Occupant!.Value.Owner);
    }

    [Fact]
    public void 深水贴眼时禁入照旧()
    {
        // 上一条的对照：同一局面，E3 东侧换成未架桥深水（墙，裁决 F2）→ E3 对 P0 封闭，主串 C3 + E3 两眼成活，两格对 P1 禁入，
        // 合法落子范围扣除、暂放 E3 报"活棋禁入"。说明上一条变绿靠的是"栅栏不是墙"，而不是禁入机制失效。
        MatchFlow match = FencedEyeMatch("water");

        LifeShapeReport life = LifeShapeReport.Analyze(match.Board);
        Assert.Equal(["C3", "E3"], life.EyeSpacesOf("C4"));
        Assert.Equal(LifeState.Alive, life.LifeOf("C4"));
        Assert.Equal(["C3", "E3"], life.Forbidden(TestMaps.P1));

        IReadOnlySet<Coord> range = match.LegalRangeFor(TestMaps.P1);
        Assert.DoesNotContain(TestMaps.At("E3"), range);
        Assert.DoesNotContain(TestMaps.At("C3"), range);

        StagedBatch batch = match.OpenDeploy();
        BatchFailure? failure = batch.Stage(TestMaps.At("E3"), PieceType.Basic);
        Assert.NotNull(failure);
        Assert.Equal(BatchFailureKind.LifeForbidden, failure.Kind);
    }
}
