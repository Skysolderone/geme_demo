using Siege.Core.Board.Maps;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Sim.Play;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>
/// life-shape tasks 3.3：终端版文本盘面用独立符号标出禁入格与已活棋串，图例同步。
/// 禁入格记作 <c>xN</c>（N = 活形所有者的玩家号），已活棋串的子在类型字母后加 <c>@</c>（如 <c>1B@</c>）；二者都不与既有符号冲突。
/// retire-legacy-rules 1.6：局面载体为平坦 8×8 链接档图上四人同锁棋盘 0；环形活形用 RingF7H7（原 RingE5G5 平移 (+1, +2)），其余平移 (+2, +2)；链接终端只画本战场外接矩形，列号按视口左缘计。
/// </summary>
public class 终端活形与禁入标示Tests
{
    private static readonly PlayerId P0 = TestMaps.P0;
    private static readonly PlayerId P1 = TestMaps.P1;

    /// <summary>P0 的环形活形（单格眼 F7、H7），第 5 大回合。</summary>
    private static MatchFlow RingMatch() =>
        LinkModeFixtures.PlainStarted().AtRound(5, [P1, P0, TestMaps.P2, TestMaps.P3])
            .Stones(P0, BatchDeployment.非法批次必须给出可定位的原因Tests.RingF7H7);

    /// <summary>链接终端只画本座位所在战场（四人同锁的棋盘 0）的外接矩形：可画出的格与视口左缘的列号。</summary>
    private static readonly Coord[] Viewport = [.. LinkModeFixtures.PlainBoards[0].Cells()];

    private static readonly int ViewportLeft = LinkModeFixtures.PlainBoards[0].Origin.X;

    /// <summary>取某一行（围棋记法行号）里视口第 <paramref name="column"/> 列（视口左缘 = 0；retire-legacy-rules 前为全图 A = 0）的 3 字符格。行首是 <c>{行号,3} </c>，每格 3 字符。</summary>
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
        // P1 看盘：P0 的眼 F7、H7 是 P1 的禁入格 → "x1"（玩家1 = P0 的活形）；环上每枚子都是已活棋串的子 → "1B@"。
        string text = Render(RingMatch(), P1);

        Assert.Equal("x1 ", CellText(text, 7, 3));
        Assert.Equal("x1 ", CellText(text, 7, 5));
        Assert.Equal("1B@", CellText(text, 6, 2));
        Assert.Equal("1B@", CellText(text, 8, 6));
        Assert.Equal(" . ", CellText(text, 7, 0));

        // 标出的禁入格恰好就是活形查询给 P1 的禁入格集合（测试侧独立取数；终端不另判规则）。
        MatchPublicView view = RingMatch().Publish();
        Coord[] marked =
        [
            .. Viewport.Where(c => CellText(text, c.Y + 1, c.X - ViewportLeft).StartsWith('x')).Order(),
        ];
        Assert.Equal(view.LifeShape.ForbiddenCellsFor(P1).Order(), marked);
    }

    [Fact]
    public void 所有者看自己的眼不标禁入()
    {
        // P0 看盘：自己的眼不是禁入格（所有者可自拆），照常显示为空格；自己的活棋仍标 "@"。
        string text = Render(RingMatch(), P0);

        Assert.Equal(" . ", CellText(text, 7, 3));
        Assert.Equal(" . ", CellText(text, 7, 5));
        Assert.Equal("1B@", CellText(text, 6, 2));
        Assert.DoesNotContain("x", string.Join('\n', text.Split('\n').Where(l => l.Length > 3 && char.IsDigit(l[2]))), StringComparison.Ordinal);
    }

    [Fact]
    public void 未定棋串不标已活()
    {
        // 只有一个眼的棋串是"未定"，不加 "@"，它的眼也不是禁入格。P0 的 C3–E3–E4... 这里用单子 G7 周围四子围出的单眼：F7、H7、G6、G8 各为独立单子，
        // 每枚都只贴一个封闭空区 G7（眼值 1）→ 未定。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5).Stones(P0, "F7", "H7", "G6", "G8");
        string text = Render(match, P1);

        Assert.Equal("1B ", CellText(text, 7, 3));
        Assert.Equal(" . ", CellText(text, 7, 4));
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
        // P0 的眼 F7 上放一枚未揭示信物，H7 没有：P1 看盘时 F7 为 " ? "，H7 仍为 "x1 "。
        MatchFlow match = LinkModeFixtures.PlainStarted(relics: [("F7", RelicFixtures.Command())])
            .AtRound(5, [P1, P0, TestMaps.P2, TestMaps.P3])
            .Stones(P0, BatchDeployment.非法批次必须给出可定位的原因Tests.RingF7H7);
        string text = Render(match, P1);

        Assert.Equal(" ? ", CellText(text, 7, 3));
        Assert.Equal("x1 ", CellText(text, 7, 5));

        // 盘面上的 x 集合 = 禁入格 − 信物格（测试侧独立取数）；被盖住的 F7 出现在图例下方的说明行里，并带所有者。
        MatchPublicView view = match.Publish();
        Coord[] relicCells = [.. view.Relics.Select(r => r.Coord)];
        Coord[] marked = [.. Viewport.Where(c => CellText(text, c.Y + 1, c.X - ViewportLeft).StartsWith('x')).Order()];
        Assert.Equal(view.LifeShape.ForbiddenCellsFor(P1).Except(relicCells).Order(), marked);
        Assert.Contains("信物格同为禁入：F7(x1)", text, StringComparison.Ordinal);

        // 反面：没有重叠时不出这一行（说明行不是恒写）。
        Assert.DoesNotContain("信物格同为禁入", Render(RingMatch(), P1), StringComparison.Ordinal);
    }

    [Fact]
    public void 脚本对局里出现禁入与已活标示()
    {
        // 脚本化终端测试：link:1、种子 31（内置固定配对脚本），人类坐 1 号位、选 2 号出生棋盘后每个小回合都 Pass；AI（Standard）按规则落子。
        // retire-legacy-maps 1.8：原为 v5、选 1 号区、Pass 8 次（第 4 大回合盘上已有活形棋串）。改到 link:1 后人类只看得见自己的战场，
        // 探针（种子 1–40 × 棋盘 1–5）选同棋盘有 AI、构筑期内即出现活形与禁入的组合：种子 31 选 2 号棋盘；Pass 改为 2 次（停在第 3 大回合、链接之前——
        // 终端对局在链接后的小回合播报会越界崩溃，见 migration.md 待决 L15）。断言不变。
        // 样本口径下界：输出的棋盘行里必须真的出现 "@" 与 "x" 标记，并且图例同步出现——否则上面的逐格断言只证明了渲染函数，不证明终端对局真的走到了它。
        // link-map-terrain 裁决 T1 = A：原为单一种子 31（探针挑出），改图后该局前 3 大回合没有已活棋串。
        // "x"是别家（AI）活形的眼，脚本落子控制不了 AI，而 "@" 与 "x" 要在同一份终端输出里出现，所以改为固定种子区间：
        // 种子 1–10（写死，不针对单个种子挑）、人类选 2 号出生棋盘、每小回合 Pass，区间内至少一局的棋盘行同时出现 "@" 与 "x" 标记，且图例同步出现。
        // 每局各自的断言（退出码 0、棋盘行 ≥ 20）照旧逐局检查。每局约 0.2 s。
        bool found = false;
        for (ulong seed = 1; seed <= 10 && !found; seed++)
        {
            var script = new System.Text.StringBuilder("2\n");
            for (int i = 0; i < 2; i++)
            {
                script.Append("\npass\n");
            }

            var output = new StringWriter();
            int exit = PlayCommand.Run(seed, 4, 1, AiDifficulty.Standard, new StringReader(script.ToString()), output, contentSet: ContentSet.V1, map: MapCatalog.Resolve("link:1"), pairingSource: new ScriptedPairingSource());   // more-pieces-relics：写死内容集 v1
            string text = output.ToString();

            Assert.Equal(0, exit);
            string[] boardRows = [.. text.Split('\n').Where(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^\s{0,2}\d{1,2} ( [#~.?+] |\d[BFLMSA][ @]|x\d | \d |\*[BFLMSA] | [pcdove] )+ \d+\s*$"))];
            Assert.True(boardRows.Length >= 20, $"种子 {seed} 只找到 {boardRows.Length} 行棋盘");
            found = boardRows.Any(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"\d[BFLMSA]@"))
                && boardRows.Any(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"x\d "))
                && text.Contains("@=已活棋串", StringComparison.Ordinal);
        }

        Assert.True(found, "种子 1–10 里没有一局的终端棋盘同时出现已活标示与禁入标示");
    }
}
