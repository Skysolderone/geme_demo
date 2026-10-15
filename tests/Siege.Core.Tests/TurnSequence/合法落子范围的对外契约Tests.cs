using System.Text.RegularExpressions;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;

namespace Siege.Core.Tests.TurnSequence;

/// <summary>规格：turn-sequence —— Requirement: 合法落子范围的对外契约</summary>
public class 合法落子范围的对外契约Tests
{
    /// <summary>
    /// P0 在平坦 8×8 链接载体棋盘 0 角上的两眼活形：C4–F4、D3、F3 围出单格眼 C3、E3（retire-legacy-rules：原 9×9 出生区 0 角上的 A2–D2、B1、D1 / 眼 A1、C1 平移 (+2, +2)）。
    /// </summary>
    private static readonly string[] CornerC3E3 = ["D3", "F3", "C4", "D4", "E4", "F4"];

    [Fact]
    public void 共享出生区内的禁入()
    {
        // 第 2 大回合，P0 与 P1 共享出生区 0（A1–C3）；P0 已在区内做出活形（眼 A1、C1）→ P1 的范围 = 出生区扣除 A1、C1。
        // retire-legacy-rules：链接载体上 P0 / P1 同锁棋盘 0，构筑期的战场就是这块出生棋盘——P1 的范围 = 棋盘 0 的可落子格扣除 P0 的眼 C3、E3，P0 的范围是整块棋盘 0。
        MatchFlow match = LinkModeFixtures.PlainStarted(zones: [0, 0, 2, 3]).AtRound(2).Stones(TestMaps.P0, CornerC3E3);
        HashSet<Coord> board0 = [.. match.Map.Boards[0].Cells()];

        Assert.Equal(board0.Except([TestMaps.At("C3"), TestMaps.At("E3")]).ToHashSet(), match.LegalRangeFor(TestMaps.P1));
        Assert.Equal(board0, match.LegalRangeFor(TestMaps.P0));
    }

    [Fact]
    public void 禁入扣除只在LegalRangeFor且与预演共用同一查询()
    {
        // 守门（tasks 2.4，段 D 按裁决 R12 收窄；原名 禁入只在契约一处扣除且与预演共用同一查询）：
        // ① 源码：从某个格集合里**扣除**禁入格，全仓（Siege.Core、Siege.Sim、Siege.Presentation、src/godot）只有一处。
        //    expert-lookahead D7 把合法落子范围抽成只读公开量的纯函数 PublicRules.LegalRange（对局流程的 MatchFlow.LegalRangeFor 与专家前瞻共用），
        //    这一处随之从 MatchFlow.cs:LegalRangeFor 移到 PublicRules.cs:LegalRange（段 A 改写期望值；"只有一处"不变）。
        //    读取禁入格（ForbiddenCellsFor / IsForbiddenFor / ProtectedCellsOf）对表现、终端、分析放开——那是标示与统计，不是第二份合法范围。
        //    "扣除"认三种形状：Except / ExceptWith 的实参里取禁入格；Where 里对 IsForbiddenFor 取反；RemoveWhere / RemoveAll 里判禁入。
        //    挡不住所有绕法（testing.md「违禁 token 清单挡不住照抄一份算式」），靠 ② 的行为比对兜底。
        // ② 行为：同一盘面上，P1 在全图范围下逐格单子预演得到"活棋禁入"的格，恰好是契约从全图可落子格里扣掉的格。
        //    retire-legacy-rules：载体为平坦 8×8 链接档图（四人同锁棋盘 0），"全图"收窄为本战场（棋盘 0）——其余棋盘本就不在合法范围里，不是禁入扣除；
        //    P2 的角上活形由原 9×9 左上角（A8–D8、B9、D9，眼 A9、C9）翻到棋盘 0 左下角（C4–F4、D3、F3，眼 C3、E3），P0 的环列 +1、行 +2。
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

        Assert.Equal(["PublicRules.cs:LegalRange"], DeductionsOf(files));

        // 读取放开的反面：扫描口径里确实有契约之外的读取者（终端、表现层、预演），它们不算扣除。
        Assert.Contains("BoardRenderer.cs", CallersOf(files, "ForbiddenCellsFor"));
        Assert.Contains("DefaultBoardView.cs", CallersOf(files, "ForbiddenCellsFor"));
        Assert.Contains("BatchRehearsal.cs", CallersOf(files, "IsForbiddenFor"));

        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5).Stones(TestMaps.P0, BatchDeployment.非法批次必须给出可定位的原因Tests.RingF7H7)
            .Stones(TestMaps.P2, CornerC3E3);
        GameBoard board = match.Board;
        BatchContext wide = BatchFixtures.Context(board, TestMaps.P1);
        Coord[] field = [.. match.Map.Boards[0].Cells()];
        var rejected = new List<Coord>();
        foreach (Coord c in field.Where(c => board[c].IsPlayableEmpty).Order())
        {
            RehearsalResult r = BatchRehearsal.Rehearse(board, wide, [new Placement(c, PieceType.Basic)], new BoardHistory());
            if (r.Failure?.Kind == BatchFailureKind.LifeForbidden)
            {
                rejected.Add(c);
            }
        }

        Coord[] removed = [.. field.Where(c => board[c].Terrain == Terrain.Playable).Except(match.LegalRangeFor(TestMaps.P1)).Order()];
        Assert.Equal(["C3", "E3", "F7", "H7"], removed.Notations());
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
