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
        MatchFlow match = MatchFixtures.Started().AtRound(6, MatchFixtures.All)
            .Stones(MatchFixtures.P3, "D4", "E4", "F4", "G4", "H4", "D5", "F5", "H5", "D6", "E6", "F6", "G6", "H6")
            .Stones(MatchFixtures.P1, "B9");
        match.Resign(MatchFixtures.P3);
        Assert.Equal(PlayerStatus.Resigned, match.StateOf(MatchFixtures.P3).Status);

        LifeShapeReport report = LifeShapeReport.Analyze(match.Board);

        Assert.Equal(LifeState.Alive, report.LifeOf("D4"));
        foreach (PlayerId active in new[] { MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2 })
        {
            Assert.Equal(["E5", "G5"], report.Forbidden(active));
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
}
