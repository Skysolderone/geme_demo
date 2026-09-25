using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Sim.Play;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>
/// life-shape tasks 3.3：终端版文本盘面用独立符号标出禁入格与已活棋串，图例同步。
/// 禁入格记作 <c>xN</c>（N = 活形所有者的玩家号），已活棋串的子在类型字母后加 <c>@</c>（如 <c>1B@</c>）；二者都不与既有符号冲突。
/// </summary>
public class 终端活形与禁入标示Tests
{
    private static readonly PlayerId P0 = MatchFixtures.P0;
    private static readonly PlayerId P1 = MatchFixtures.P1;

    /// <summary>P0 的环形活形（单格眼 E5、G5），第 5 大回合。</summary>
    private static MatchFlow RingMatch() =>
        MatchFixtures.Started().AtRound(5, [P1, P0, MatchFixtures.P2, MatchFixtures.P3])
            .Stones(P0, BatchDeployment.非法批次必须给出可定位的原因Tests.RingE5G5);

    /// <summary>取某一行（围棋记法行号）里第 <paramref name="column"/> 列（A = 0）的 3 字符格。行首是 <c>{行号,3} </c>，每格 3 字符。</summary>
    private static string CellText(string output, int row, int column)
    {
        string line = output.Split('\n').First(l => l.StartsWith($"{row,3} ", StringComparison.Ordinal));
        return line.Substring(4 + (3 * column), 3);
    }

    private static string Render(MatchFlow match, PlayerId me)
    {
        var output = new StringWriter();
        new BoardRenderer(output).Board(match.Publish(), me);
        return output.ToString();
    }

    [Fact]
    public void 文本盘面标出禁入格与已活棋串()
    {
        // P1 看盘：P0 的眼 E5、G5 是 P1 的禁入格 → "x1"（玩家1 = P0 的活形）；环上每枚子都是已活棋串的子 → "1B@"。
        string text = Render(RingMatch(), P1);

        Assert.Equal("x1 ", CellText(text, 5, 4));
        Assert.Equal("x1 ", CellText(text, 5, 6));
        Assert.Equal("1B@", CellText(text, 4, 3));
        Assert.Equal("1B@", CellText(text, 6, 7));
        Assert.Equal(" . ", CellText(text, 5, 0));

        // 标出的禁入格恰好就是活形查询给 P1 的禁入格集合（测试侧独立取数；终端不另判规则）。
        MatchPublicView view = RingMatch().Publish();
        Coord[] marked =
        [
            .. view.Board.AllCoords().Where(c => CellText(text, c.Y + 1, c.X).StartsWith('x')).Order(),
        ];
        Assert.Equal(view.LifeShape.ForbiddenCellsFor(P1).Order(), marked);
    }

    [Fact]
    public void 所有者看自己的眼不标禁入()
    {
        // P0 看盘：自己的眼不是禁入格（所有者可自拆），照常显示为空格；自己的活棋仍标 "@"。
        string text = Render(RingMatch(), P0);

        Assert.Equal(" . ", CellText(text, 5, 4));
        Assert.Equal(" . ", CellText(text, 5, 6));
        Assert.Equal("1B@", CellText(text, 4, 3));
        Assert.DoesNotContain("x", string.Join('\n', text.Split('\n').Where(l => l.Length > 3 && char.IsDigit(l[2]))), StringComparison.Ordinal);
    }

    [Fact]
    public void 未定棋串不标已活()
    {
        // 只有一个眼的棋串是"未定"，不加 "@"，它的眼也不是禁入格。P0 的 A1–C1–C2... 这里用单子 E5 周围四子围出的单眼：D5、F5、E4、E6 各为独立单子，
        // 每枚都只贴一个封闭空区 E5（眼值 1）→ 未定。
        MatchFlow match = MatchFixtures.Started().AtRound(5).Stones(P0, "D5", "F5", "E4", "E6");
        string text = Render(match, P1);

        Assert.Equal("1B ", CellText(text, 5, 3));
        Assert.Equal(" . ", CellText(text, 5, 4));
    }

    [Fact]
    public void 图例含禁入与已活符号()
    {
        string text = Render(RingMatch(), P1);

        Assert.Contains("xN=玩家N活形的眼(你禁入)", text, StringComparison.Ordinal);
        Assert.Contains("@=已活棋串", text, StringComparison.Ordinal);
    }

    [Fact]
    public void 信物格同为禁入时信物标记优先()
    {
        // life-shape 裁决 R13：禁入格与未揭示信物重叠时，格上显示信物标记（信物更少见、信息量更大），
        // 被盖住的禁入格改由图例下方一行列出（格 + 所有者），不丢信息。
        // P0 的眼 E5 上放一枚未揭示信物，G5 没有：P1 看盘时 E5 为 " ? "，G5 仍为 "x1 "。
        MatchFlow match = MatchFixtures.Started(relics: [("E5", RelicFixtures.Command())])
            .AtRound(5, [P1, P0, MatchFixtures.P2, MatchFixtures.P3])
            .Stones(P0, BatchDeployment.非法批次必须给出可定位的原因Tests.RingE5G5);
        string text = Render(match, P1);

        Assert.Equal(" ? ", CellText(text, 5, 4));
        Assert.Equal("x1 ", CellText(text, 5, 6));

        // 盘面上的 x 集合 = 禁入格 − 信物格（测试侧独立取数）；被盖住的 E5 出现在图例下方的说明行里，并带所有者。
        MatchPublicView view = match.Publish();
        Coord[] relicCells = [.. view.Relics.Select(r => r.Coord)];
        Coord[] marked = [.. view.Board.AllCoords().Where(c => CellText(text, c.Y + 1, c.X).StartsWith('x')).Order()];
        Assert.Equal(view.LifeShape.ForbiddenCellsFor(P1).Except(relicCells).Order(), marked);
        Assert.Contains("信物格同为禁入：E5(x1)", text, StringComparison.Ordinal);

        // 反面：没有重叠时不出这一行（说明行不是恒写）。
        Assert.DoesNotContain("信物格同为禁入", Render(RingMatch(), P1), StringComparison.Ordinal);
    }

    [Fact]
    public void 脚本对局里出现禁入与已活标示()
    {
        // 脚本化终端测试：v5 图、种子 31，人类坐 1 号位、选 1 号区后每个小回合都 Pass；AI（Standard）按规则落子。
        // life-shape 段 B 实测种子 31 第 4 大回合盘上已有活形棋串（v5 贴地形的小空区很容易成活，R8）。
        // 样本口径下界：输出的棋盘行里必须真的出现 "@" 与 "x" 标记，并且图例同步出现——否则上面的逐格断言只证明了渲染函数，不证明终端对局真的走到了它。
        var script = new System.Text.StringBuilder("1\n");
        for (int i = 0; i < 8; i++)
        {
            script.Append("\npass\n");
        }

        var output = new StringWriter();
        int exit = PlayCommand.Run(31, 4, 1, AiDifficulty.Standard, new StringReader(script.ToString()), output, flagRisk: 0, contentSet: ContentSet.V1);   // flag-contest：脚本依赖出生区与走法，写死冒险概率 0；more-pieces-relics：写死内容集 v1
        string text = output.ToString();

        Assert.Equal(0, exit);
        string[] boardRows = [.. text.Split('\n').Where(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^\s{0,2}\d{1,2} ( [#~.?+] |\d[BFLMSA][ @]|x\d | \d |\*[BFLMSA] | [pcdove] )+ \d+\s*$"))];
        Assert.True(boardRows.Length >= 20, $"只找到 {boardRows.Length} 行棋盘");
        Assert.Contains(boardRows, l => System.Text.RegularExpressions.Regex.IsMatch(l, @"\d[BFLMSA]@"));
        Assert.Contains(boardRows, l => System.Text.RegularExpressions.Regex.IsMatch(l, @"x\d "));
        Assert.Contains("@=已活棋串", text, StringComparison.Ordinal);
    }
}
