using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Preview;
using Siege.Core.Recruit;
using Siege.Core.Relics;
using Siege.Sim.Play;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>
/// more-pieces-relics tasks 3.6：终端（<see cref="BoardRenderer"/> / <see cref="ConsoleController"/>）的四种新棋子字母与名称、中文快捷输入、图例、
/// 新信物名称与字母、驿站来源与工坊目标（含隔一格）的文字列出。字母取 D9：旗手 N、铁链 C、哨兵 T、界碑 K；连营 y、犄角 j、驿站 r、工坊 w。
/// 本段是终端表现，没有 openspec Scenario；方法名按 tasks 条目命名。
/// </summary>
public class 终端新棋子与新信物Tests
{
    private static readonly PlayerId Me = MatchFixtures.P0;

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

        RelicType[] relics = Enum.GetValues<RelicType>();
        Assert.Equal(relics.Length, relics.Select(BoardRenderer.RelicLetter).Distinct().Count());
        Assert.DoesNotContain('?', relics.Select(BoardRenderer.RelicLetter));
        Assert.Equal(relics.Length, relics.Select(BoardRenderer.RelicName).Distinct().Count());
        Assert.Equal("yjrw", new string([.. new[] { RelicType.Encampment, RelicType.Pincer, RelicType.Relay, RelicType.Workshop }.Select(BoardRenderer.RelicLetter)]));

        // 信物字母与棋子字母不在同一位置出现（信物占格中间、棋子带玩家号），但仍要求小写、与"未揭示 ?"不撞。
        Assert.All(relics.Select(BoardRenderer.RelicLetter), c => Assert.True(char.IsLower(c)));
    }

    [Fact]
    public void 新棋子与已揭示新信物的渲染快照()
    {
        // v2 局：P0 在 C5 落哨兵子（显示 1T）、P1 在 E5 落界碑子（2K）；已揭示的驿站 H5（被 P0 的 H4 覆盖，格上显示 " r "）。
        // 图例按内容集列出新四种棋子与新四类信物；状态栏的已揭示信物用新名称。
        MatchFlow match = MatchFixtures.Started(relics: [("H5", RelicFixtures.Relay())]).AtRound(5, MatchFixtures.All);
        match.Board.Place(TestMaps.At("C5"), Me, PieceType.Sentry);
        match.Board.Place(TestMaps.At("H4"), Me, PieceType.Basic);
        match.Board.Place(TestMaps.At("E5"), MatchFixtures.P1, PieceType.Boundary);
        match.Relics.Reveal(match.Board, 5);
        match.Debug.Recalculate();

        var output = new StringWriter();
        var render = new BoardRenderer(output);
        render.Board(match.Publish(), Me);
        render.Status(match.Publish(), Me);
        string text = output.ToString();

        Assert.Equal("1T ", CellText(text, 5, 2));
        Assert.Equal("2K ", CellText(text, 5, 4));
        Assert.Equal(" r ", CellText(text, 5, 7));
        Assert.Contains("N旗手 C铁链 T哨兵 K界碑", text, StringComparison.Ordinal);
        Assert.Contains("y连营 j犄角 r驿站 w工坊", text, StringComparison.Ordinal);
        Assert.Contains("H5" + BoardRenderer.RelicName(RelicType.Relay) + "+1→玩家1(你)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void 内容集v1的图例不变()
    {
        // v1 局没有新内容：图例与引入新棋子之前逐字相同（不列 N / C / T / K 与 y / j / r / w）。
        MatchFlow match = MatchFixtures.Started(options: MatchOptions.Immediate with { ContentSet = ContentSet.V1 }).AtRound(5, MatchFixtures.All);
        var output = new StringWriter();
        new BoardRenderer(output).Board(match.Publish(), Me);
        string text = output.ToString();

        Assert.Contains("  图例：1B=玩家1的普通子  B普通 F堡垒 L连珠 M倍增 S协同  *=你暂放  +=可落子  ?=未揭示信物  #=岩石  ~=深水" + Environment.NewLine, text, StringComparison.Ordinal);
        Assert.Contains("        已揭示信物：p探勘 c征召 d兵站 o军令 v先锋 e徽记" + Environment.NewLine, text, StringComparison.Ordinal);
        Assert.DoesNotContain("旗手", text, StringComparison.Ordinal);
        Assert.DoesNotContain("驿站", text, StringComparison.Ordinal);
    }

    [Fact]
    public void 驿站来源逐枚列出且隐藏零加成()
    {
        // 征募阶段打印展示数的来源拆分（取 Core 公开补充载荷的结构参数来源，终端不重算）：驿站逐枚列出并标注计入的其他受控信物枚数；
        // 只控制驿站本身时它的加成是 +0，终端不列这一条（段 B 待决 4；图形面板的做法留段 D）。
        // P0 占据驿站 H5 与先锋 B8（均已揭示）→ 展示数 5 + 1：驿站计入 1 枚其他信物。
        string withVanguard = RecruitOutput(("H5", RelicFixtures.Relay()), ("B8", RelicFixtures.Vanguard()));
        Assert.Contains("展示数 6 = 基础 5 + 驿站 H5 +1（控制 1 枚其他信物）", withVanguard, StringComparison.Ordinal);

        // 只有驿站本身：来源里那条 +0 不列（也就没有拆分行）。
        string alone = RecruitOutput(("H5", RelicFixtures.Relay()));
        Assert.DoesNotContain("驿站 H5", alone, StringComparison.Ordinal);
        Assert.DoesNotContain("展示数 5 =", alone, StringComparison.Ordinal);

        // 文案函数本身：探勘与驿站并列、+0 的驿站被略去、各来源之和 = 展示数。
        var parameter = new StructureParameter(5, 8,
        [
            new ParameterSource(TestMaps.At("D4"), RelicType.Prospecting, 1),
            new ParameterSource(TestMaps.At("E5"), RelicType.Relay, 2),
            new ParameterSource(TestMaps.At("F5"), RelicType.Relay, 0),
        ]);
        Assert.Equal("展示数 8 = 基础 5 + 探勘 D4 +1 + 驿站 E5 +2（控制 2 枚其他信物）", BoardRenderer.RevealSourcesText(parameter));
    }

    [Fact]
    public void 工坊目标含隔一格的文字列出()
    {
        // 本小回合快照标记工坊生效、手里只有匠人：暂放 "E5 A B:E7"（隔一格搭桥）被接受，预演列出该匠人的全部合法改造目标（来自 Core 富预演，
        // 即改造合法性唯一实现），隔一格的目标带"（隔一格）"标记，并提示工坊生效；确认后暂放带着该改造。
        // 快照未标记工坊时同一输入被拒（拒绝理由来自唯一实现），预演目标里没有 E7。
        (string text, StagedBatch batch) = DeployScript(workshop: true, "E5 A B:E7", "v", "ok");
        Assert.Contains("工坊生效", text, StringComparison.Ordinal);
        Assert.Contains("搭桥 E7（隔一格）", text, StringComparison.Ordinal);
        Assert.Contains("立栅 E5-E6", text, StringComparison.Ordinal);
        Placement staged = Assert.Single(batch.Placements);
        Assert.Equal((TestMaps.At("E5"), PieceType.Artisan, (TerrainEdit?)TerrainEdit.Bridge(TestMaps.At("E7"))), (staged.Coord, staged.Type, staged.Edit));

        (string without, StagedBatch rejected) = DeployScript(workshop: false, "E5 A B:E7", "E5 A", "v", "ok");
        Assert.DoesNotContain("搭桥 E7", without, StringComparison.Ordinal);
        Assert.DoesNotContain("工坊生效", without, StringComparison.Ordinal);
        Assert.Contains(TerrainEditRules.Reject(rejected.Board.Map, TestMaps.At("E5"), TerrainEdit.Bridge(TestMaps.At("E7")))!, without, StringComparison.Ordinal);
        Assert.Null(Assert.Single(rejected.Placements).Edit);
    }

    /// <summary>v2、第 5 大回合、P0 先行；打开征募并让终端控制者读一行空输入（不征募），返回输出。</summary>
    private static string RecruitOutput(params (string Cell, RelicContent Content)[] relics)
    {
        MatchFlow match = MatchFixtures.Started(relics: relics).AtRound(5, MatchFixtures.All);
        foreach ((string cell, _) in relics)
        {
            match.Board.Place(TestMaps.At(cell), Me, PieceType.Basic);
        }

        match.Relics.Reveal(match.Board, 5);
        match.Debug.Recalculate();
        match.BeginTurn();
        match.EnterRecruit();
        PlayerHandAccess hand = match.CurrentHand();

        var output = new StringWriter();
        var controller = new ConsoleController(Me, match.Publish, match.PublishSupplement, match.PreviewCurrentBatch, new StringReader("\n"), output);
        controller.Recruit(hand, hand.Panel());
        return output.ToString();
    }

    /// <summary>E7 深水的 v2 局，P0 手里只有匠人；<paramref name="workshop"/> 经测试专用的快照改写标记工坊生效。按脚本走完部署，返回输出与部署批次。</summary>
    private static (string Text, StagedBatch Batch) DeployScript(bool workshop, params string[] lines)
    {
        MatchFlow match = MatchFixtures.Started(TestMaps.Terrain(surfaces: [("E7", Surface.DeepWater)])).AtRound(5, MatchFixtures.All);
        match.Debug.SeedHand(Me, (PieceType.Artisan, 3));
        if (workshop)
        {
            match.Debug.SetSnapshotTransform(s => new EffectSnapshot(
                s.Player, s.MajorRound, s.RevealCount, s.FreePickCount, s.TypeSlots, s.DeployLimit, s.EmblemCounts, s.HeldTypeCount, s.RelaySources, workshopActive: true));
        }

        match.BeginTurn();
        match.EnterRecruit();
        StagedBatch batch = match.EnterDeploy();
        Assert.Equal(workshop, batch.Context.WorkshopActive);

        var output = new StringWriter();
        var controller = new ConsoleController(Me, match.Publish, match.PublishSupplement, match.PreviewCurrentBatch,
            new StringReader(string.Join("\n", lines) + "\n"), output);
        controller.Deploy(batch, match.Rehearse);
        return (output.ToString(), batch);
    }
}
