using Siege.Core.Batch;
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

    // ---------- life-single-stone：单子不成活（规格 openspec/changes/life-single-stone/specs/life-shape） ----------
    // 实现前本段 4 条里红 3 条（单子两眼、共享眼空间、所有者自切；「两子棋串有两个单格眼为活」本就绿，是 M2 的靶子）。
    // 变异 M1（LifeShapeReport.StateOf 去掉 `when stoneCount > 1`）→ 红 7；M2（`stoneCount > 1` 改 `> 2`）→ 红 7；明细见 .trellis/tasks/09-23-life-single-stone/implement.md。

    [Fact]
    public void 单子有两个单格眼为未定()
    {
        // B1 一枚孤子，A1（A2 岩石）与 C1（C2、D1 岩石）各是只贴 B1 的单格眼。眼空间与眼值照常计算（D1），只是状态上限为未定。
        GameBoard board = Grid([
            "........1",
            "###......",
            ".0.#.....",
        ]);
        LifeShapeReport report = LifeShapeReport.Analyze(board);

        Assert.Equal(["A1", "C1"], report.EyeSpacesOf("B1"));
        Assert.Equal(2, report.GroupLifeAt(TestMaps.At("B1"))!.EyeValueSum);
        Assert.Equal(LifeState.Undetermined, report.LifeOf("B1"));
        Assert.False(report.IsProtected(report.EyeSpaceAt(TestMaps.At("A1"))!));
        Assert.Empty(report.Forbidden(B));
    }

    [Fact]
    public void 两子棋串有两个单格眼为活()
    {
        // 与上一条同样的"两个单格眼"，棋串多一枚子（B1–C1）：眼 A1、D1，照常为活。
        GameBoard board = Grid([
            "........1",
            "####.....",
            ".00.#....",
        ]);
        LifeShapeReport report = LifeShapeReport.Analyze(board);

        Assert.Equal(["A1", "D1"], report.EyeSpacesOf("B1"));
        Assert.Equal(2, report.GroupLifeAt(TestMaps.At("B1"))!.EyeValueSum);
        Assert.Equal(LifeState.Alive, report.LifeOf("B1"));
        Assert.Equal(["A1", "D1"], report.Forbidden(B));
    }

    [Fact]
    public void 单子与活形棋串共享眼空间仍禁入()
    {
        // 四子串 A2–C2 + B1 有眼 A1、C1 → 活。孤子 D1 的眼是 C1（与四子串共享）与 E1（只贴 D1），眼值之和 2，但只有 1 枚子 → 未定。
        // C1 经由四子串受保护、对 B 禁入；E1 只属于单子，不禁入。
        GameBoard board = Grid([
            "........1",
            "000###...",
            ".0.0.#...",
        ]);
        LifeShapeReport report = LifeShapeReport.Analyze(board);

        Assert.Equal(LifeState.Alive, report.LifeOf("A2"));
        Assert.Equal(["C1", "E1"], report.EyeSpacesOf("D1"));
        Assert.Equal(2, report.GroupLifeAt(TestMaps.At("D1"))!.EyeValueSum);
        Assert.Equal(LifeState.Undetermined, report.LifeOf("D1"));
        Assert.True(report.IsProtected(report.EyeSpaceAt(TestMaps.At("C1"))!));
        Assert.False(report.IsProtected(report.EyeSpaceAt(TestMaps.At("E1"))!));
        Assert.True(report.IsForbiddenFor(B, TestMaps.At("C1")));
        Assert.False(report.IsForbiddenFor(B, TestMaps.At("E1")));
        Assert.Equal(["A1", "C1"], report.Forbidden(B));
    }

    [Fact]
    public void 切出单子即不再是活形_所有者自切()
    {
        // 所有者 A 的匠人落 C2、在 B1–C1 立栅：两子串切成两枚单子，各自仍有眼值 2 的直四（眼值照算），但都只判未定。
        // 另一半（他人立栅被判破坏活形）在 CaptureResolution/以整批最终状态判定合法性Tests。
        GameBoard board = SingleStoneCut();
        LifeShapeReport before = LifeShapeReport.Analyze(board);
        Assert.Equal(LifeState.Alive, before.LifeOf("B1"));
        Assert.Equal(["A1,A2,A3,A4", "D1,E1,F1,G1"], before.EyeSpacesOf("B1"));

        RehearsalResult result = BatchRehearsal.Rehearse(
            board,
            BatchFixtures.Context(board, A, limit: 5),
            [BatchFixtures.Artisan("C2", TerrainEdit.Fence(TestMaps.At("B1"), TestMaps.At("C1")))],
            new BoardHistory());

        Assert.True(result.IsLegal, result.Failure?.Message);
        LifeShapeReport after = LifeShapeReport.Analyze(result.ProjectedBoard!);
        Assert.Equal(1, after.GroupLifeAt(TestMaps.At("B1"))!.Group.Size);
        Assert.Equal(1, after.GroupLifeAt(TestMaps.At("C1"))!.Group.Size);
        Assert.Equal(2, after.GroupLifeAt(TestMaps.At("B1"))!.EyeValueSum);
        Assert.Equal(2, after.GroupLifeAt(TestMaps.At("C1"))!.EyeValueSum);
        Assert.Equal(LifeState.Undetermined, after.LifeOf("B1"));
        Assert.Equal(LifeState.Undetermined, after.LifeOf("C1"));
        Assert.Empty(after.Forbidden(B));
    }
}
