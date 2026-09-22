using System.Text.RegularExpressions;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;

namespace Siege.Core.Tests.TurnSequence;

/// <summary>规格：turn-sequence —— Requirement: 合法落子范围的对外契约</summary>
public class 合法落子范围的对外契约Tests
{
    /// <summary>P2 在出生区 1（G1–J3）角上的两眼活形：F2–J2、H1、F1 围出单格眼 G1、J1。</summary>
    private static readonly string[] CornerG1J1 = ["F1", "H1", "F2", "G2", "H2", "J2"];

    /// <summary>P0 在出生区 0（A1–C3）角上的两眼活形：A2–D2、B1、D1 围出单格眼 A1、C1。</summary>
    private static readonly string[] CornerA1C1 = ["B1", "D1", "A2", "B2", "C2", "D2"];

    [Fact]
    public void 范围随大回合切换()
    {
        // design.md D5：第 3 大回合为该玩家的出生区格集合，第 4 大回合为全图可落子格集合；出生区数据本身不带限制。
        // life-shape：两种情况下都扣除该玩家的禁入格——P2 在出生区 1 角上有活形（眼 G1、J1），P0 / P1 的范围不含这两格，P2 自己的含。
        // 变异验证 M-T12：`MajorRound <= 3` 改为 `< 3` → 红 1（本测试第 3 大回合断言）。
        MatchFlow match = MatchFixtures.Started(zones: [1, 1, 2, 3]).AtRound(3).Stones(MatchFixtures.P2, CornerG1J1);
        Coord[] eyes = [TestMaps.At("G1"), TestMaps.At("J1")];
        IReadOnlySet<Coord> zone1 = match.Map.BirthZones[1];
        var zone1Open = zone1.Except(eyes).ToHashSet();
        Assert.Equal(zone1Open, match.LegalRangeFor(MatchFixtures.P0));
        Assert.Equal(zone1Open, match.LegalRangeFor(MatchFixtures.P1));
        Assert.Equal(7, zone1Open.Count);

        match.AtRound(4);
        IReadOnlySet<Coord> full = match.LegalRangeFor(MatchFixtures.P0);
        Assert.Equal(79, full.Count);
        Assert.Equal(match.Map.AllCoords().Where(c => match.Map.TerrainAt(c) == Terrain.Playable).Except(eyes).ToHashSet(), full);
        Assert.Equal(full, match.LegalRangeFor(MatchFixtures.P1));
        Assert.Superset(eyes.ToHashSet(), match.LegalRangeFor(MatchFixtures.P2).ToHashSet());
    }

    [Fact]
    public void 禁入格不在范围内()
    {
        // 第 5 大回合，P0（A）的活形眼空间为 E5、G5：P1（B）的范围不含这两格，P0 自己的含。
        MatchFlow match = MatchFixtures.Started().AtRound(5).Stones(MatchFixtures.P0, BatchDeployment.非法批次必须给出可定位的原因Tests.RingE5G5);
        Coord e5 = TestMaps.At("E5");
        Coord g5 = TestMaps.At("G5");

        IReadOnlySet<Coord> b = match.LegalRangeFor(MatchFixtures.P1);
        IReadOnlySet<Coord> a = match.LegalRangeFor(MatchFixtures.P0);

        Assert.DoesNotContain(e5, b);
        Assert.DoesNotContain(g5, b);
        Assert.Equal(79, b.Count);
        Assert.Contains(e5, a);
        Assert.Contains(g5, a);
        Assert.Equal(81, a.Count);
    }

    [Fact]
    public void 共享出生区内的禁入()
    {
        // 第 2 大回合，P0 与 P1 共享出生区 0（A1–C3）；P0 已在区内做出活形（眼 A1、C1）→ P1 的范围 = 出生区扣除 A1、C1。
        MatchFlow match = MatchFixtures.Started(zones: [0, 0, 2, 3]).AtRound(2).Stones(MatchFixtures.P0, CornerA1C1);
        IReadOnlySet<Coord> zone0 = match.Map.BirthZones[0];

        Assert.Equal(zone0.Except([TestMaps.At("A1"), TestMaps.At("C1")]).ToHashSet(), match.LegalRangeFor(MatchFixtures.P1));
        Assert.Equal(zone0, match.LegalRangeFor(MatchFixtures.P0));
    }

    [Fact]
    public void 禁入只在契约一处扣除且与预演共用同一查询()
    {
        // 守门（tasks 2.4）：
        // ① 源码：规则层（Siege.Core、Siege.Sim）里按玩家取禁入格集合（ForbiddenCellsFor）只有合法落子范围契约 MatchFlow.cs 一处；
        //    逐格判禁入（IsForbiddenFor）只有预演 BatchRehearsal.cs 一处；二者都是 LifeShapeReport 的方法（唯一实现）。
        // ② 行为：同一盘面上，P1 在全图范围下逐格单子预演得到"活棋禁入"的格，恰好是契约从全图可落子格里扣掉的格。
        string root = RepoRoot();
        string[] files =
        [
            .. new[] { "Siege.Core", "Siege.Sim" }
                .SelectMany(p => Directory.GetFiles(Path.Combine(root, "src", p), "*.cs", SearchOption.AllDirectories))
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .Where(f => Path.GetFileName(f) != "LifeShape.cs"),
        ];
        Assert.True(files.Length > 100, $"扫描口径过小：只扫到 {files.Length} 个文件。");
        Assert.Equal(["MatchFlow.cs"], CallersOf(files, "ForbiddenCellsFor"));
        Assert.Equal(["BatchRehearsal.cs"], CallersOf(files, "IsForbiddenFor"));

        MatchFlow match = MatchFixtures.Started().AtRound(5).Stones(MatchFixtures.P0, BatchDeployment.非法批次必须给出可定位的原因Tests.RingE5G5)
            .Stones(MatchFixtures.P2, "A8", "B8", "C8", "D8", "B9", "D9");
        GameBoard board = match.Board;
        BatchContext wide = BatchFixtures.Context(board, MatchFixtures.P1);
        var rejected = new List<Coord>();
        foreach (Coord c in board.AllCoords().Where(c => board[c].IsPlayableEmpty).Order())
        {
            RehearsalResult r = BatchRehearsal.Rehearse(board, wide, [new Placement(c, PieceType.Basic)], new BoardHistory());
            if (r.Failure?.Kind == BatchFailureKind.LifeForbidden)
            {
                rejected.Add(c);
            }
        }

        Coord[] removed = [.. board.AllCoords().Where(c => board[c].Terrain == Terrain.Playable).Except(match.LegalRangeFor(MatchFixtures.P1)).Order()];
        Assert.Equal(["E5", "G5", "A9", "C9"], removed.Notations());
        Assert.Equal(removed, rejected);
    }

    private static string[] CallersOf(string[] files, string method) =>
        [.. files.Where(f => Regex.IsMatch(Regex.Replace(File.ReadAllText(f), "//[^\\n]*", string.Empty), $"\\b{method}\\s*\\("))
            .Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal)];

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
}
