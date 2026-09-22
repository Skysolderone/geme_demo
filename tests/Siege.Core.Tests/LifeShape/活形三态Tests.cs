using Siege.Core.Board;
using Siege.Core.Match;
using static Siege.Core.Tests.LifeShapeFixtures;

namespace Siege.Core.Tests.LifeShape;

/// <summary>规格：openspec/changes/life-shape/specs/life-shape —— Requirement: 活形三态</summary>
/// <remarks>
/// 局面都贴在棋盘下边：第 1、2 行是 A 的形，第 3 行起是外部大空区，J 列顶上一枚 B 子保证外部不封闭。
/// 眼值表见 design D2 / spec「活形三态」。
/// </remarks>
public class 活形三态Tests
{
    [Fact]
    public void 两个单格眼为活()
    {
        GameBoard board = Grid([
            "........1",
            "0000.....",
            ".0.0.....",
        ]);
        LifeShapeReport report = LifeShapeReport.Analyze(board);

        Assert.Equal(["A1", "C1"], report.EyeSpacesOf("A2"));
        Assert.Equal(2, report.GroupLifeAt(TestMaps.At("A2"))!.EyeValueSum);
        Assert.Equal(LifeState.Alive, report.LifeOf("A2"));
    }

    [Fact]
    public void 一个单格眼为未定()
    {
        GameBoard board = Grid([
            "........1",
            "000......",
            ".0.......",
        ]);
        LifeShapeReport report = LifeShapeReport.Analyze(board);

        Assert.Equal(["A1"], report.EyeSpacesOf("A2"));
        Assert.Equal(1, report.GroupLifeAt(TestMaps.At("A2"))!.EyeValueSum);
        Assert.Equal(LifeState.Undetermined, report.LifeOf("A2"));
    }

    [Fact]
    public void 直二不计()
    {
        GameBoard board = Grid([
            "........1",
            "000......",
            "..0......",
        ]);
        LifeShapeReport report = LifeShapeReport.Analyze(board);

        Assert.Equal(["A1,B1"], report.EyeSpacesOf("A2"));
        Assert.Equal(0, report.EyeSpaceAt(TestMaps.At("A1"))!.EyeValue);
        Assert.Equal(0, report.GroupLifeAt(TestMaps.At("A2"))!.EyeValueSum);
        Assert.Equal(LifeState.Dead, report.LifeOf("A2"));
    }

    [Fact]
    public void 方四为死形()
    {
        GameBoard board = Grid([
            "........1",
            "000......",
            "..0......",
            "..0......",
        ]);
        LifeShapeReport report = LifeShapeReport.Analyze(board);

        Assert.Equal(["A1,B1,A2,B2"], report.EyeSpacesOf("A3"));
        Assert.Equal(0, report.EyeSpaceAt(TestMaps.At("A1"))!.EyeValue);
        Assert.Equal(LifeState.Dead, report.LifeOf("A3"));
    }

    [Fact]
    public void 直四为活()
    {
        GameBoard board = Grid([
            "........1",
            "00000....",
            "....0....",
        ]);
        LifeShapeReport report = LifeShapeReport.Analyze(board);

        Assert.Equal(["A1,B1,C1,D1"], report.EyeSpacesOf("A2"));
        Assert.Equal(2, report.EyeSpaceAt(TestMaps.At("A1"))!.EyeValue);
        Assert.Equal(LifeState.Alive, report.LifeOf("A2"));
    }

    [Fact]
    public void 被栅栏隔开的2x2不是方四()
    {
        // 与「方四为死形」同一局面，A2–B2 之间立栅：四格沿气边是 A2–A1–B1–B2 一条折线，不成环。
        GameBoard board = Grid(
            [
                "........1",
                "000......",
                "..0......",
                "..0......",
            ],
            fences: [("A2", "B2")]);
        LifeShapeReport report = LifeShapeReport.Analyze(board);

        Assert.Equal(["A1,B1,A2,B2"], report.EyeSpacesOf("A3"));
        Assert.Equal(2, report.EyeSpaceAt(TestMaps.At("A1"))!.EyeValue);
        Assert.Equal(LifeState.Alive, report.LifeOf("A3"));
    }

    [Fact]
    public void 三格为未定()
    {
        GameBoard board = Grid([
            "........1",
            "0000.....",
            "...0.....",
        ]);
        LifeShapeReport report = LifeShapeReport.Analyze(board);

        Assert.Equal(["A1,B1,C1"], report.EyeSpacesOf("A2"));
        Assert.Equal(1, report.EyeSpaceAt(TestMaps.At("A1"))!.EyeValue);
        Assert.Equal(LifeState.Undetermined, report.LifeOf("A2"));
    }

    [Fact]
    public void 单格眼加三格眼空间为活()
    {
        // design Open Question 3 / 裁决 R1：眼值相加，1 + 1 = 2。
        GameBoard board = Grid([
            "........1",
            "000000...",
            ".0...0...",
        ]);
        LifeShapeReport report = LifeShapeReport.Analyze(board);

        Assert.Equal(["A1", "C1,D1,E1"], report.EyeSpacesOf("A2"));
        Assert.Equal(1, report.EyeSpaceAt(TestMaps.At("A1"))!.EyeValue);
        Assert.Equal(1, report.EyeSpaceAt(TestMaps.At("D1"))!.EyeValue);
        Assert.Equal(2, report.GroupLifeAt(TestMaps.At("A2"))!.EyeValueSum);
        Assert.Equal(LifeState.Alive, report.LifeOf("A2"));
    }

    [Fact]
    public void 五格为未定()
    {
        GameBoard board = Grid([
            "........1",
            "000000...",
            ".....0...",
        ]);
        LifeShapeReport report = LifeShapeReport.Analyze(board);

        Assert.Equal(["A1,B1,C1,D1,E1"], report.EyeSpacesOf("A2"));
        Assert.Equal(1, report.EyeSpaceAt(TestMaps.At("A1"))!.EyeValue);
        Assert.Equal(LifeState.Undetermined, report.LifeOf("A2"));
    }

    [Fact]
    public void 六格为活()
    {
        GameBoard board = Grid([
            "........1",
            "0000000..",
            "......0..",
        ]);
        LifeShapeReport report = LifeShapeReport.Analyze(board);

        Assert.Equal(["A1,B1,C1,D1,E1,F1"], report.EyeSpacesOf("A2"));
        Assert.Equal(2, report.EyeSpaceAt(TestMaps.At("A1"))!.EyeValue);
        Assert.Equal(LifeState.Alive, report.LifeOf("A2"));
    }

    [Fact]
    public void 状态随盘面重算()
    {
        GameBoard board = Grid([
            "........1",
            "0000.....",
            ".0.0.....",
        ]);
        Assert.Equal(LifeState.Alive, LifeShapeReport.Analyze(board).LifeOf("A2"));

        board.Place(TestMaps.At("A1"), A, PieceType.Basic);
        LifeShapeReport after = LifeShapeReport.Analyze(board);

        Assert.Equal(["C1"], after.EyeSpacesOf("A2"));
        Assert.Equal(LifeState.Undetermined, after.LifeOf("A2"));
    }

    [Fact]
    public void 活形不入存档()
    {
        // 在 11×11 对局盘上给 P0 搭一条两眼活形（眼 E5、G5），存档 → 恢复 → 重算，与存档前逐项一致；存档 JSON 不含活形字段。
        MatchFlow match = MatchFixtures.Started().AtRound(5)
            .Stones(MatchFixtures.P0, "D4", "E4", "F4", "G4", "H4", "D5", "F5", "H5", "D6", "E6", "F6", "G6", "H6")
            .Stones(MatchFixtures.P1, "B9");
        LifeShapeReport before = LifeShapeReport.Analyze(match.Board);
        Assert.Equal(LifeState.Alive, before.LifeOf("D4"));

        string json = match.Serialize();
        foreach (string word in new[] { "life", "eye", "forbid", "alive", "活形", "禁入" })
        {
            Assert.DoesNotContain(word, json, StringComparison.OrdinalIgnoreCase);
        }

        MatchFlow restored = MatchFlow.RestoreUnvalidated(match.Board.BaseMap, match.Relics.Generation, json);
        LifeShapeReport after = LifeShapeReport.Analyze(restored.Board);

        Assert.Equal(Describe(before), Describe(after));
        Assert.Equal(LifeState.Alive, after.LifeOf("D4"));
    }
}
