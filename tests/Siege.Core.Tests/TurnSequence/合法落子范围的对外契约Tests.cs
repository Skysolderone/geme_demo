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
    public void 禁入扣除只在LegalRangeFor且与预演共用同一查询()
    {
        // 守门（tasks 2.4，段 D 按裁决 R12 收窄；原名 禁入只在契约一处扣除且与预演共用同一查询）：
        // ① 源码：从某个格集合里**扣除**禁入格，全仓（Siege.Core、Siege.Sim、Siege.Presentation、src/godot）只有 MatchFlow.LegalRangeFor 一处。
        //    读取禁入格（ForbiddenCellsFor / IsForbiddenFor / ProtectedCellsOf）对表现、终端、分析放开——那是标示与统计，不是第二份合法范围。
        //    "扣除"认三种形状：Except / ExceptWith 的实参里取禁入格；Where 里对 IsForbiddenFor 取反；RemoveWhere / RemoveAll 里判禁入。
        //    挡不住所有绕法（testing.md「违禁 token 清单挡不住照抄一份算式」），靠 ② 的行为比对兜底。
        // ② 行为：同一盘面上，P1 在全图范围下逐格单子预演得到"活棋禁入"的格，恰好是契约从全图可落子格里扣掉的格。
        string root = RepoRoot();
        string[] files =
        [
            .. new[] { Path.Combine("src", "Siege.Core"), Path.Combine("src", "Siege.Sim"), Path.Combine("src", "Siege.Presentation"), Path.Combine("src", "godot") }
                .SelectMany(p => Directory.GetFiles(Path.Combine(root, p), "*.cs", SearchOption.AllDirectories))
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                            && !f.Contains($"{Path.DirectorySeparatorChar}.godot{Path.DirectorySeparatorChar}"))
                .Where(f => Path.GetFileName(f) != "LifeShape.cs"),
        ];
        Assert.True(files.Length > 120, $"扫描口径过小：只扫到 {files.Length} 个文件。");
        Assert.Contains(files, f => f.EndsWith("BoardView.cs", StringComparison.Ordinal));   // 口径含 Godot

        Assert.Equal(["MatchFlow.cs:LegalRangeFor"], DeductionsOf(files));

        // 读取放开的反面：扫描口径里确实有契约之外的读取者（终端、表现层、预演），它们不算扣除。
        Assert.Contains("BoardRenderer.cs", CallersOf(files, "ForbiddenCellsFor"));
        Assert.Contains("DefaultBoardView.cs", CallersOf(files, "ForbiddenCellsFor"));
        Assert.Contains("BatchRehearsal.cs", CallersOf(files, "IsForbiddenFor"));

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

    /// <summary>扣除禁入格的位置，形如 <c>文件名:所在方法名</c>，按序排列（同一方法里出现两处即列两次）。</summary>
    private static string[] DeductionsOf(string[] files)
    {
        const string Query = @"(?:ForbiddenCellsFor|ProtectedCellsOf|IsForbiddenFor)";
        var deduction = new Regex(
            $@"Except\w*\s*\([^;]*?{Query}" +                 // range.Except(…ForbiddenCellsFor(…)) / set.ExceptWith(…)
            $@"|Where\s*\([^;]*?!\s*[\w.()]*?IsForbiddenFor" +  // .Where(c => !life.IsForbiddenFor(p, c))
            $@"|Remove\w*\s*\([^;]*?{Query}",                   // set.RemoveWhere(c => life.IsForbiddenFor(p, c))
            RegexOptions.Singleline);
        var method = new Regex(@"(?:public|private|internal|protected)[^;{}=]*?\s(\w+)\s*(?:<[^>]*>)?\s*\(", RegexOptions.Singleline);
        var hits = new List<string>();
        foreach (string file in files)
        {
            string text = Regex.Replace(File.ReadAllText(file), "//[^\\n]*", string.Empty);
            foreach (System.Text.RegularExpressions.Match m in deduction.Matches(text))
            {
                string owner = method.Matches(text[..m.Index]).LastOrDefault()?.Groups[1].Value ?? "?";
                hits.Add($"{Path.GetFileName(file)}:{owner}");
            }
        }

        return [.. hits.Order(StringComparer.Ordinal)];
    }

    private static string[] CallersOf(string[] files, string method) =>
        [.. files.Where(f => Regex.IsMatch(Regex.Replace(File.ReadAllText(f), "//[^\\n]*", string.Empty), $"\\b{method}\\s*\\("))
            .Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal)];

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
}
