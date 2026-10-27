using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.PieceRelics;
using Siege.Core.Preview;
using Siege.Core.Recruit;
using Siege.Sim.Play;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>
/// more-pieces-relics tasks 3.6：终端（<see cref="BoardRenderer"/> / <see cref="ConsoleController"/>）的四种新棋子字母与名称、中文快捷输入、图例、
/// 信物字母与匠人改造目标的文字列出。字母取 D9：旗手 N、铁链 C、哨兵 T、界碑 K。
/// relic-redesign W1：旧十类信物的字母、驿站来源与工坊"隔一格"随旧信物删除；挂在棋子上的五种信物字母为战鼓 d、号令 o、望台 w、百变 v、军旗 g（终端最小版本）。
/// 本段是终端表现，没有 openspec Scenario；方法名按 tasks 条目命名。
/// retire-legacy-rules 1.6：局面载体为平坦 8×8 链接档图上四人同锁棋盘 0，原 9×9 坐标平移 (+2, +2)；链接终端只画本战场外接矩形（从棋盘 0 的 C 列起），<c>CellText</c> 的列号按视口左缘计（平移后不变），行号随平移 +2。
/// </summary>
public class 终端新棋子与新信物Tests
{
    private static readonly PlayerId Me = TestMaps.P0;

    /// <summary>取某一行（围棋记法行号）里第 <paramref name="column"/> 列（A = 0）的 3 字符格（同 终端活形与禁入标示Tests）。</summary>
    private static string CellText(string output, int row, int column)
    {
        string line = output.Split('\n').First(l => l.StartsWith($"{row,3} ", StringComparison.Ordinal));
        return line.Substring(4 + (3 * column), 3);
    }

    [Fact]
    public void 棋子与信物字母两两不同()
    {
        // 守门：对枚举的全部值各有一个字母、两两不同、不是兜底的 '?'；名称两两不同；字母 / 单字 / 全名三种快捷输入都能解析回原类型。
        // 变异 MC-R1（界碑字母 K → T，与哨兵撞字母）应红。
        PieceType[] pieces = Enum.GetValues<PieceType>();
        Assert.Equal(pieces.Length, pieces.Select(BoardRenderer.Letter).Distinct().Count());
        Assert.DoesNotContain('?', pieces.Select(BoardRenderer.Letter));
        Assert.Equal(pieces.Length, pieces.Select(BoardRenderer.Name).Distinct().Count());
        Assert.Equal("NCTK", new string([.. new[] { PieceType.Bannerman, PieceType.Chain, PieceType.Sentry, PieceType.Boundary }.Select(BoardRenderer.Letter)]));
        Assert.Equal(["旗手", "铁链", "哨兵", "界碑"], new[] { PieceType.Bannerman, PieceType.Chain, PieceType.Sentry, PieceType.Boundary }.Select(BoardRenderer.Name));

        // 中文单字快捷输入：原六种取名称首字，新四种按 D9 取"旗 / 链 / 哨 / 碑"（铁链的首字"铁"、界碑的首字"界"不作快捷输入）。
        string[] quick = ["普", "堡", "连", "倍", "协", "匠", "旗", "链", "哨", "碑"];
        Assert.Equal(pieces.Length, quick.Length);
        foreach (PieceType type in pieces)
        {
            string name = BoardRenderer.Name(type);
            foreach (string text in new[] { BoardRenderer.Letter(type).ToString(), BoardRenderer.Letter(type).ToString().ToLowerInvariant(), quick[(int)type], name })
            {
                Assert.True(BoardRenderer.TryParseType(text, out PieceType parsed), $"{text} 解析失败");
                Assert.Equal(type, parsed);
            }
        }

        RelicKind[] relics = Enum.GetValues<RelicKind>();
        Assert.Equal(relics.Length, relics.Select(BoardRenderer.RelicLetter).Distinct().Count());
        Assert.Equal("dowvg", new string([.. RelicKinds.Order.Select(BoardRenderer.RelicLetter)]));

        // 信物字母与棋子字母不在同一位置出现（信物占格中间、棋子带玩家号），但仍要求小写。
        Assert.All(relics.Select(BoardRenderer.RelicLetter), c => Assert.True(char.IsLower(c)));
    }

    [Fact]
    public void 新棋子与信物的渲染快照()
    {
        // v2 局：P0 在 E7 落哨兵子（显示 1T）、P1 在 G7 落界碑子（2K）；K7 空格上一件地图信物战鼓（格上显示 " d "），P0 的 K6 挂着一件军旗。
        // 图例按内容集列出新四种棋子与五种信物；状态栏列出持物棋子（relic-redesign W1 终端最小版本）。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5, TestMaps.All);
        match.Board.Place(TestMaps.At("E7"), Me, PieceType.Sentry);
        match.Board.Place(TestMaps.At("K6"), Me, PieceType.Basic);
        match.Board.Place(TestMaps.At("G7"), TestMaps.P1, PieceType.Boundary);
        match.Board.AttachRelic(TestMaps.At("K7"), RelicToken.Spawned(0, RelicKind.WarDrum));
        match.Board.AttachRelic(TestMaps.At("K6"), RelicToken.Spawned(1, RelicKind.Banner));
        match.Debug.Recalculate();

        var output = new StringWriter();
        var render = new BoardRenderer(output);
        render.Board(match.Publish(), Me);
        render.Status(match.Publish(), Me);
        string text = output.ToString();

        Assert.Equal("1T ", CellText(text, 7, 2));
        Assert.Equal("2K ", CellText(text, 7, 4));
        Assert.Equal(" d ", CellText(text, 7, 7));
        Assert.Contains("N旗手 C铁链 T哨兵 K界碑", text, StringComparison.Ordinal);
        Assert.Contains("d战鼓 o号令 w望台 v百变 g军旗", text, StringComparison.Ordinal);
        Assert.Contains("持物棋子：K6军旗→玩家1(你)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void 内容集v1的图例不变()
    {
        // v1 局没有新棋子：图例不列 N / C / T / K；信物图例一行与内容集无关（relic-redesign W1：五种信物，旧"已揭示信物"一行随旧信物删除）。
        MatchFlow match = LinkModeFixtures.PlainStarted(options: MatchOptions.Immediate with { ContentSet = ContentSet.V1 }).AtRound(5, TestMaps.All);
        var output = new StringWriter();
        new BoardRenderer(output).Board(match.Publish(), Me);
        string text = output.ToString();

        Assert.Contains("  图例：1B=玩家1的普通子  B普通 F堡垒 L连珠 M倍增 S协同  *=你暂放  +=可落子  #=岩石  ~=深水" + Environment.NewLine, text, StringComparison.Ordinal);
        Assert.Contains("        信物（空格上即地图信物，落子拾取）：d战鼓 o号令 w望台 v百变 g军旗  !x=预告下一批落位" + Environment.NewLine, text, StringComparison.Ordinal);
        Assert.DoesNotContain("旗手", text, StringComparison.Ordinal);
        Assert.DoesNotContain("驿站", text, StringComparison.Ordinal);
    }

    [Fact]
    public void 匠人改造目标只列相邻()
    {
        // relic-redesign D4：工坊删除后匠人的格改造范围固定为几何四邻。手里只有匠人：暂放 "G7 A B:G9"（隔一格搭桥）被拒（拒绝理由来自改造合法性唯一实现），
        // 预演列出的合法改造目标里没有 G9，也不再有"工坊生效"提示。
        (string text, StagedBatch batch) = DeployScript("G7 A B:G9", "G7 A", "v", "ok");
        Assert.DoesNotContain("搭桥 G9", text, StringComparison.Ordinal);
        Assert.DoesNotContain("工坊", text, StringComparison.Ordinal);
        Assert.Contains("立栅 G7-G8", text, StringComparison.Ordinal);
        Assert.Contains(TerrainEditRules.Reject(batch.Board.Map, TestMaps.At("G7"), TerrainEdit.Bridge(TestMaps.At("G9")))!, text, StringComparison.Ordinal);
        Assert.Null(Assert.Single(batch.Placements).Edit);
    }

    /// <summary>G9 深水的 v2 局，P0 手里只有匠人。按脚本走完部署，返回输出与部署批次。</summary>
    private static (string Text, StagedBatch Batch) DeployScript(params string[] lines)
    {
        MatchFlow match = LinkModeFixtures.PlainStarted(TestMaps.Terrain(surfaces: [("G9", Surface.DeepWater)])).AtRound(5, TestMaps.All);
        match.Debug.SeedHand(Me, (PieceType.Artisan, 3));
        match.BeginTurn();
        match.EnterRecruit();
        StagedBatch batch = match.EnterDeploy();

        var output = new StringWriter();
        var controller = new ConsoleController(Me, match.Publish, match.PublishSupplement, match.PreviewCurrentBatch,
            new StringReader(string.Join("\n", lines) + "\n"), output);
        controller.Deploy(batch, match.Rehearse);
        return (output.ToString(), batch);
    }
}
