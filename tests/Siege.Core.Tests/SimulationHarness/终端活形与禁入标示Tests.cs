using System.Collections.Immutable;
using Siege.Core.Board.Maps;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Sim.Play;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>
/// life-shape tasks 3.3：终端版文本盘面用独立符号标出禁入格与已活棋串，图例同步。
/// 禁入格记作 <c>xN</c>（N = 活形所有者的玩家号），已活棋串的子在类型字母后加 <c>@</c>（如 <c>1B@</c>）；二者都不与既有符号冲突。
/// retire-legacy-rules 1.6：局面载体为平坦 8×8 链接档图上四人同锁棋盘 0；环形活形用 RingF7H7（原 9×9 眼 E5、G5 的环平移 (+1, +2)），其余平移 (+2, +2)；链接终端只画本战场外接矩形，列号按视口左缘计。
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
        // 脚本化终端测试：link:1，人类坐 1 号位、选 2 号出生棋盘后每个小回合都 Pass；AI（Standard）按规则落子。
        // 样本口径下界：输出的棋盘行里必须真的出现 "@" 与 "x" 标记，并且图例同步出现——否则上面的逐格断言只证明了渲染函数，不证明终端对局真的走到了它。
        // 历史：原为单一种子 31（探针挑出）；link-map-terrain 裁决 T1 = A 改为固定种子区间 1–10（"x"是别家 AI 活形的眼，脚本落子控制不了 AI）。
        // fence-eye-rule 2.2：栅栏不再造眼后，前 3 大回合里 AI 成活的棋串极少（探针：种子 1–20 × Pass 2 / 4 / 6 次只有 0 / 1 / 1 局满足；1–40 × Pass 2 次为 0），
        // 区间法守不住。改为不依赖 AI 走法：经 PlayCommand 的测试接缝 arrange 在插旗锁定后、第一个小回合前给 P1（AI）摆一条两眼活形——
        // 左下角 C4–D4–E4–F4 + D3 + F3，眼 C3（西、南是岩石）与 E3（南是岩石），全在高度 0、无栅栏；他人不得破坏、P1 的活形硬约束不拆自己的活形，
        // 所以人类看到的每一份棋盘都有 P1 的 "@" 与它两眼上的 "x"。逐格断言、退出码、棋盘行数下界照旧。
        // map-expand-25 段 A2：手写紧贴图在浮空岛校验器下恒不合规（终端入口经 MatchFlow.Create 校验），载体改为生成图 link:1:4；
        // 活形摆在 8 号平台 F30–L37（林地主题）的东南角，与原形左右镜像：L31–K31–J31–H31 + K30 + H30（H30 是坡道，高 1），眼 L30 与 J30——
        // 平台高 2、四周低地高 0，东、南两侧是崖壁（不通气），眼不漏到低地；摆子都在核心区之外，不压信物格。
        var script = new System.Text.StringBuilder("2\n");
        for (int i = 0; i < 2; i++)
        {
            script.Append("\npass\n");
        }

        LifeState? arrangedLife = null;
        void Arrange(MatchFlow match)
        {
            foreach (string cell in new[] { "L31", "K31", "J31", "H31", "K30", "H30" })
            {
                match.Board.Place(Coord.Parse(cell), TestMaps.P1, PieceType.Basic);
            }

            match.Debug.Recalculate();
            arrangedLife = LifeShapeReport.Analyze(match.Board).GroupLifeAt(Coord.Parse("L31"))!.Life;
        }

        var output = new StringWriter();
        // terrain-barriers 3.1 起载体曾为手写平坦图 FlatMap（link:90003）；map-expand-25 段 A2 改为生成图 link:1:4（见上）。
        MapData map = MapCatalog.Resolve("link:1:4");
        BoardPlate eighth = map.Boards[7];
        Assert.Equal(("F30", 6, 8), (eighth.Origin.ToNotation(), eighth.Width, eighth.Height));
        Assert.All(new[] { "L31", "K31", "J31", "H31", "K30", "L30", "J30" }, c => Assert.Equal(2, map.HeightAt(Coord.Parse(c))));
        Assert.Equal(1, map.HeightAt(Coord.Parse("H30")));
        Assert.DoesNotContain(map.RelicCells.Keys, c => c.Y is 29 or 30 && eighth.Contains(c));   // 第 30 / 31 行（Y 从 0 起）
        Assert.True(MapValidator.Validate(map).IsValid, MapValidator.Validate(map).ToString());
        int exit = PlayCommand.Run(1, 4, 1, AiDifficulty.Standard, new StringReader(script.ToString()), output, map, null, null, null, ContentSet.V1, null, null, Arrange);   // more-pieces-relics：写死内容集 v1
        string text = output.ToString();

        Assert.Equal(LifeState.Alive, arrangedLife);   // 前提：摆出来的确是已确定活形
        Assert.Equal(0, exit);
        string[] boardRows = [.. text.Split('\n').Where(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^\s{0,2}\d{1,2} ( [#~.?+] |\d[BFLMSA][ @]|x\d | \d |\*[BFLMSA] | [pcdove] )+ \d+\s*$"))];
        Assert.True(boardRows.Length >= 20, $"只找到 {boardRows.Length} 行棋盘");
        Assert.Contains(boardRows, l => System.Text.RegularExpressions.Regex.IsMatch(l, @"\d[BFLMSA]@"));   // 终端按看盘者编号显示玩家：P1 显示为 2
        Assert.Contains(boardRows, l => System.Text.RegularExpressions.Regex.IsMatch(l, @"x\d "));
        Assert.Contains("@=已活棋串", text, StringComparison.Ordinal);
    }
}
