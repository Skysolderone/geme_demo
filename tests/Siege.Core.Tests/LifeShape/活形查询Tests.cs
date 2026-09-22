using System.Text.RegularExpressions;
using Siege.Core.Board;
using static Siege.Core.Tests.LifeShapeFixtures;

namespace Siege.Core.Tests.LifeShape;

/// <summary>规格：openspec/changes/life-shape/specs/life-shape —— Requirement: 活形查询</summary>
public class 活形查询Tests
{
    [Fact]
    public void 预演副本可查询()
    {
        GameBoard board = Grid([
            "........1",
            "...00000.",
            "...0.0.0.",
            "...00000.",
            ".........",
            ".........",
            ".........",
        ]);
        string before = Describe(LifeShapeReport.Analyze(board));

        GameBoard rehearsal = board.Clone();
        rehearsal.Place(TestMaps.At("E5"), A, PieceType.Basic);
        LifeShapeReport onCopy = LifeShapeReport.Analyze(rehearsal);

        Assert.Equal(LifeState.Undetermined, onCopy.LifeOf("D4"));
        Assert.Empty(onCopy.Forbidden(B));
        LifeShapeReport official = LifeShapeReport.Analyze(board);
        Assert.Equal(LifeState.Alive, official.LifeOf("D4"));
        Assert.Equal(before, Describe(official));
    }

    [Fact]
    public void 查询确定()
    {
        // 多玩家、多种眼值、共享眼空间、崖壁 / 栅栏 / 深水同盘；两次查询与副本上的查询逐项相同。
        GameBoard board = Grid(
            [
                "2.2...1.1",
                ".2...1.1.",
                "2.2......",
                "...00000.",
                "...0.0.0.",
                "...00000.",
                "0........",
                ".0.~...33",
                "0.0....3.",
            ],
            heights: [("C3", 2)],
            fences: [("H1", "J1")]);
        string first = Describe(LifeShapeReport.Analyze(board));
        string second = Describe(LifeShapeReport.Analyze(board));
        string onCopy = Describe(LifeShapeReport.Analyze(board.Clone()));

        Assert.Equal(first, second);
        Assert.Equal(first, onCopy);
        Assert.Contains("Alive", first, StringComparison.Ordinal);
        // 手算：A 的 B2（眼 A2、B1、C2）与 C1（眼 B1、C2）、中部两眼串（E5、G5）为活；C 的 A9、B8（眼 A8、B9）为活；
        // B 的 H9、D 的 J1 只是各自单子 / 棋串的唯一单格眼（未定）。C2 的三面墙：深水 D2、崖 C3（h=2）。
        Assert.Contains("forbid P1 B1,A2,C2,E5,G5,A8,B9\n", first, StringComparison.Ordinal);
        Assert.Contains("forbid P0 A8,B9\n", first, StringComparison.Ordinal);
    }

    [Fact]
    public void 活形分析不直接遍历无序集合()
    {
        // 守门（tasks 1.4 确定性）：活形分析文件里不得出现 HashSet / Dictionary 一族，集合一律按坐标序的数组 / 列表处理。
        // 正则不以词边界开头，挡住 ImmutableHashSet、ToDictionary 之类复合名（testing.md「源码扫描守门的正则」）。
        string code = 空区与封闭眼空间Tests.StripComments(File.ReadAllText(空区与封闭眼空间Tests.LifeShapeSource()));
        Assert.Matches("class\\s+LifeShapeReport", code);
        string[] hits = [.. new[] { "(?i)\\w*HashSet\\w*", "(?i)\\w*Dictionary\\w*", "(?i)\\w*Lookup\\w*" }
            .SelectMany(p => Regex.Matches(code, p).Select(m => m.Value))];
        Assert.Empty(hits);
    }
}
