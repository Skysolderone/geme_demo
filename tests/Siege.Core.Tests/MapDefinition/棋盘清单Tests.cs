using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Presentation.Visibility;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>规格：board-map / map-definition —— Requirement: 棋盘清单</summary>
/// <remarks>
/// retire-legacy-maps 1.6：载体由棋盘档夹具（BoardMapFixtures，随棋盘档删除）改为 link:1 / 手写链接档图与 9×9 手写夹具；
/// 「旧地图没有棋盘清单」（七张内置 / 历史地图文件）随 maps/ 在第 2 组删除，由新的规格 Scenario「空清单不写出」（手写夹具）接替；
/// 「标准档与边疆档地图带棋盘清单被拒绝」随旧两档在第 2 组删除。
/// </remarks>
public class 棋盘清单Tests
{
    private static readonly PlayerId P0 = TestMaps.P0;
    private static readonly PlayerId P1 = TestMaps.P1;

    [Fact]
    public void 空清单不写出()
    {
        // 规格 Scenario：导出一张没有棋盘清单的手写测试夹具地图再读回 → 导出文件不含棋盘清单字段，读回后清单为空。
        // retire-legacy-maps 1.6 新增：接替「旧地图没有棋盘清单」（读入七张旧地图文件再导出；maps/ 随旧地图删除）。
        MapData map = MatchFixtures.Map("E5");
        string json = MapFile.ToJson(map);
        MapData restored = MapFile.FromJson(json);

        Assert.DoesNotContain("\"Boards\"", json, StringComparison.Ordinal);
        Assert.False(restored.Boards.IsDefault);
        Assert.Empty(restored.Boards);
        Assert.Equal(json, MapFile.ToJson(restored));

        // 反面：清单非空时确实写出（否则"不写出"可能只是这个字段从来不写）。
        Assert.Contains("\"Boards\"", MapFile.ToJson(LinkMapFixtures.Initial()), StringComparison.Ordinal);
    }

    [Fact]
    public void 出生区是出生棋盘的核心区()
    {
        // Scenario（link-map-terrain map-definition「棋盘清单」，原「出生棋盘即出生区」）：出生区 i 恰是第 i 块出生棋盘中央的 3×3 核心区（宽或高为偶数时偏西 / 偏南）；
        // 棋盘内核心区之外的格不属于任何出生区。期望值用测试内独立算式（西起第 ⌊(W−3)/2⌋+1 列、南起第 ⌊(H−3)/2⌋+1 行起 3×3）。
        MapData map = MapFile.FromJson(MapFile.ToJson(MapCatalog.Resolve("link:1")));
        BoardPlate[] births = [.. map.Boards.Where(b => b.Kind == BoardPlateKind.Birth)];
        Assert.Equal(map.BirthZones.Length, births.Length);
        for (int i = 0; i < births.Length; i++)
        {
            BoardPlate b = births[i];
            Coord[] core = [.. from dy in Enumerable.Range(0, 3) from dx in Enumerable.Range(0, 3)
                               select new Coord(b.Origin.X + ((b.Width - 3) / 2) + dx, b.Origin.Y + ((b.Height - 3) / 2) + dy)];
            Assert.Equal(core.Order(), map.BirthZones[i].Order());
            Assert.All(b.Cells(), c => Assert.Equal(core.Contains(c) ? (int?)i : null, map.BirthZoneOf(c)));
        }

        // link:1 的第 2 块：左下角 AQ3、6×6 → 出生区 AR4–AT6（西起第 2–4 列、南起第 2–4 行）。
        Assert.Equal("AQ3", births[1].Origin.ToNotation());
        Assert.Equal(["AR4", "AT6"], new[] { map.BirthZones[1].Min(), map.BirthZones[1].Max() }.Select(c => c.ToNotation()));

        // 规格算例：宽 7、高 8 的第 2 块出生棋盘 → 出生区恰是西起第 3–5 列、南起第 3–5 行这 9 格。在 link:1 起的种子里找一张第 2 块是 7×8 的图。
        MapData sample = Enumerable.Range(1, 400).Select(s => LinkMapGenerator.Generate((ulong)s)).First(m => m.Boards[1] is { Width: 7, Height: 8 });
        BoardPlate seven = sample.Boards[1];
        HashSet<(int Column, int Row)> relative = [.. sample.BirthZones[1].Select(c => (c.X - seven.Origin.X + 1, c.Y - seven.Origin.Y + 1))];
        Assert.True(relative.SetEquals(from r in new[] { 3, 4, 5 } from c in new[] { 3, 4, 5 } select (c, r)), $"{sample.Id}：{string.Join(" ", relative)}");
    }

    [Fact]
    public void 清单不参与结算()
    {
        // Scenario：给一张手写测试夹具地图补上与出生区一致的棋盘清单，前后两次在相同种子下执行同一组批次 → 每一步的盘面、提子与势力值逐项相同。
        // 第一段是摆好的提子局面，逐步比对局指纹；第二段是两名 AI 的真实跑局，逐小回合比日志快照与事件。
        // retire-legacy-maps 1.6：载体由棋盘档夹具（清空清单对照）改为 9×9 手写夹具（补上四角出生棋盘对照），断言不变。
        MapData cleared = MatchFixtures.Map();
        MapData full = cleared with
        {
            Boards = [.. new[] { (0, 0), (6, 0), (0, 6), (6, 6) }.Select(o => new BoardPlate(new Coord(o.Item1, o.Item2), 3, 3, BoardPlateKind.Birth))],
        };
        Assert.All(Enumerable.Range(0, 4), i => Assert.Equal(full.BirthZones[i].Order(), full.Boards[i].Cells().Order()));
        Assert.NotEmpty(full.Boards);

        string[] withBoards = ScriptedSteps(full);
        string[] withoutBoards = ScriptedSteps(cleared);

        Assert.Equal(3, withBoards.Length);
        Assert.Equal(withBoards, withoutBoards);

        MatchLog logWith = AiRun(full);
        MatchLog logWithout = AiRun(cleared);

        Assert.False(logWith.IsFailed);
        Assert.NotEmpty(logWith.Turns.SelectMany(t => t.Placements));   // 样本口径：确实落了子
        Assert.Equal(SimFixtures.TurnTexts(logWith.Turns), SimFixtures.TurnTexts(logWithout.Turns));
        Assert.Equal(
            logWith.Events.Select(e => System.Text.Json.JsonSerializer.Serialize(e)),
            logWithout.Events.Select(e => System.Text.Json.JsonSerializer.Serialize(e)));
    }

    [Fact]
    public void 默认棋盘视图模型透传棋盘清单()
    {
        // D2 / D10：渲染用的棋盘清单全部取自视图模型，Godot 不自行推断；没有清单的图（手写夹具）清单为空，画面不变。
        // retire-legacy-maps 1.6：载体由棋盘档夹具改为手写链接档初始图（link-map-terrain 起 46×10），断言不变。
        MapData map = LinkMapFixtures.Initial();

        DefaultBoardView view = DefaultBoardView.From(Started(map).PublicWorldOf());

        Assert.Equal(map.Boards.AsEnumerable(), view.Boards.AsEnumerable());
        Assert.Equal((46, 10), (view.Width, view.Height));

        DefaultBoardView legacy = DefaultBoardView.From(MatchFixtures.Started().PublicWorldOf());
        Assert.False(legacy.Boards.IsDefault);
        Assert.Empty(legacy.Boards);
    }

    [Fact]
    public void 棋盘清单项的文件写法()
    {
        // 每项：左下角坐标（围棋记法）、宽、高、类别；类别写成名字不写数字。
        // retire-legacy-maps 1.6：载体由棋盘档夹具改为手写链接档初始图（只有出生棋盘，原"公共棋盘写成 Public"一处随棋盘档删除），断言不变。
        string json = MapFile.ToJson(LinkMapFixtures.Initial());

        MapData edited = MapFile.FromJson(json.Replace("\"Origin\": \"M3\"", "\"Origin\": \"m3\"", StringComparison.Ordinal));

        Assert.Contains("\"Origin\": \"M3\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Kind\": \"Birth\"", json, StringComparison.Ordinal);
        Assert.Equal(LinkMapFixtures.Births[1], edited.Boards[1]);

        // retire-legacy-maps（check 补回覆盖）：类别"公共"仍是文件格式的一部分（map-definition「棋盘清单」类别为 出生 / 公共，BoardPlateKind.Public 按 L10 保留），
        // 原载体（棋盘档夹具）里的公共棋盘随旧档删除后"写成 Public"一处无人守。这里在手写链接档图的清单末尾补一块公共棋盘，只做文件往返、不经校验。
        BoardPlate open = new(new Coord(2, 2), 5, 5, BoardPlateKind.Public);
        string withPublic = MapFile.ToJson(LinkMapFixtures.Initial() with { Boards = [.. LinkMapFixtures.Births, open] });
        Assert.Contains("\"Kind\": \"Public\"", withPublic, StringComparison.Ordinal);
        Assert.Equal(open, MapFile.FromJson(withPublic).Boards[^1]);

        // 类别写成未定义的数字、宽高不是正数：指名报出，不读成一块坏棋盘。
        Assert.Contains("Boards", Assert.Throws<FormatException>(
            () => MapFile.FromJson(json.Replace("\"Kind\": \"Birth\"", "\"Kind\": 9", StringComparison.Ordinal))).Message, StringComparison.Ordinal);
        Assert.Contains("Boards", Assert.Throws<FormatException>(
            () => MapFile.FromJson(json.Replace("\"Width\": 6,", "\"Width\": 0,", StringComparison.Ordinal))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 主题读写往返()
    {
        // 规格 Scenario（link-map-terrain map-definition「棋盘清单」）：把 link:1 的初始地图导出为文件再读入 → 5 块出生棋盘的主题逐块相同。
        // 样本口径：主题确实存在且两两不同（全是 null 时"逐块相同"恒真）。
        // 变异验证（link-map-terrain 1.1 实跑，只跑本类）：M-F1 ToJson 不写 Theme → 本类红 8（本测试，及凡经 link:1 / 带主题夹具文件往返的 7 条：读回缺主题被拒）；
        // M-F2 ParseBoards 读入时丢掉主题（恒取 null）→ 本类红 2（本测试、棋盘清单项的文件写法）。
        MapData map = MapCatalog.Resolve("link:1");
        BoardTheme?[] themes = [.. map.Boards.Select(b => b.Theme)];
        Assert.Equal(5, themes.Length);
        Assert.All(themes, t => Assert.NotNull(t));
        Assert.Equal(5, themes.Distinct().Count());

        string json = MapFile.ToJson(map);
        Assert.Contains($"\"Theme\": \"{themes[2]}\"", json, StringComparison.Ordinal);   // 主题写成枚举名
        MapData restored = MapFile.FromJson(json);
        Assert.Equal(themes, restored.Boards.Select(b => b.Theme));
        Assert.Equal(map.Boards, restored.Boards);
        Assert.Equal(json, MapFile.ToJson(restored));
    }

    [Fact]
    public void 缺少主题被拒()
    {
        // 规格 Scenario：读入一张链接档地图文件，其某块出生棋盘没有主题字段 → 拒绝加载并指出该棋盘；MUST NOT 静默取缺省主题。
        // 变异验证（link-map-terrain 1.1 实跑，只跑本类）：M-F3 ParseBoards 删去"链接档出生棋盘缺主题"的拒绝 → 本测试红 1。
        JsonObject root = JsonNode.Parse(MapFile.ToJson(MapCatalog.Resolve("link:1")))!.AsObject();
        Assert.True(root["Boards"]![2]!.AsObject().Remove("Theme"));

        string message = Assert.Throws<FormatException>(() => MapFile.FromJson(root.ToJsonString())).Message;
        Assert.Contains("Boards[2]", message, StringComparison.Ordinal);
        Assert.Contains("出生棋盘 3", message, StringComparison.Ordinal);
        Assert.Contains("Theme", message, StringComparison.Ordinal);
    }

    [Fact]
    public void 非出生棋盘带主题被拒()
    {
        // 规格（map-definition「棋盘清单」）：链接档的出生棋盘 MUST 带主题；其他类别 MUST NOT 带主题。
        // 变异验证（link-map-terrain 1.1 实跑，只跑本类）：M-F4 ParseBoards 删去"非出生棋盘带主题"的拒绝 → 本测试红 1。
        BoardPlate open = new(new Coord(2, 2), 5, 5, BoardPlateKind.Public, BoardTheme.River);
        string json = MapFile.ToJson(LinkMapFixtures.Initial() with { Boards = [.. LinkMapFixtures.Births, open] });

        string message = Assert.Throws<FormatException>(() => MapFile.FromJson(json)).Message;
        Assert.Contains("Boards[5]", message, StringComparison.Ordinal);
        Assert.Contains("Theme", message, StringComparison.Ordinal);

        // 反面：同一块公共棋盘不带主题即可读回。
        BoardPlate plain = open with { Theme = null };
        Assert.Equal(plain, MapFile.FromJson(MapFile.ToJson(LinkMapFixtures.Initial() with { Boards = [.. LinkMapFixtures.Births, plain] })).Boards[^1]);
    }

    [Theory]
    [InlineData("\"Lava\"")]
    [InlineData("1")]
    [InlineData("\"\"")]
    public void 主题取值无法识别被拒(string raw)
    {
        // 规格：主题取值无法识别 MUST 拒绝加载，MUST NOT 静默取缺省主题；数字不按枚举序号读（只认名字）。
        // 变异验证（link-map-terrain 1.1 实跑，只跑本类）：M-F5 主题改按 Enum.TryParse + IsDefined 解析（接受数字）→ 本类红 1（"1" 一条）。
        string json = MapFile.ToJson(MapCatalog.Resolve("link:1"));
        JsonObject root = JsonNode.Parse(json)!.AsObject();
        root["Boards"]![3]!["Theme"] = JsonNode.Parse(raw);

        string message = Assert.Throws<FormatException>(() => MapFile.FromJson(root.ToJsonString())).Message;
        Assert.Contains("Boards[3]", message, StringComparison.Ordinal);
        Assert.Contains("出生棋盘 4", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 摆好的提子局面（retire-legacy-maps 1.6：改在 9×9 手写夹具上）：P0 三面围住中央 E5 的 P1 子（P1 另有 J9），第 5 大回合 P0 落第四面 E6 提子，
    /// P1 在 A9 应一手，P0 再落 E5。每步记对局指纹。
    /// </summary>
    private static string[] ScriptedSteps(MapData map)
    {
        MatchFlow match = Started(map).AtRound(5, [P0, P1]);
        match.Stones(P1, "E5", "J9");   // J9：P1 另有一子，提走 E5 后不出局、对局继续
        match.Stones(P0, "D5", "F5", "E4");
        var steps = new List<string>();

        match.PlayTurn("E6");
        Assert.Null(match.Board[Coord.Parse("E5")].Occupant);   // 样本口径：这一步确实提了子
        steps.Add(PresentationFixtures.Fingerprint(match));

        match.PlayTurn("A9");
        Assert.Equal(P1, match.Board[Coord.Parse("A9")].Occupant?.Owner);
        steps.Add(PresentationFixtures.Fingerprint(match));

        match.PlayTurn("E5");
        steps.Add(PresentationFixtures.Fingerprint(match));
        return [.. steps];
    }

    private static MatchFlow Started(MapData map)
    {
        MatchFlow match = MatchFlow.CreateUnvalidated(
            map, MatchFixtures.Seed, [P0, P1], MatchFixtures.Relics(map), MatchOptions.Immediate with { ContentSet = ContentSet.V1 });
        match.Debug.SeedHand(P0, (PieceType.Basic, 50));
        match.Debug.SeedHand(P1, (PieceType.Basic, 50));
        match.PlantSequentially([(P0, 0), (P1, 1)]);
        return match;
    }

    /// <summary>两名 Easy AI 在夹具图上跑 3 个大回合；权重写死（testing.md：依赖走法的断言不读默认权重）。</summary>
    private static MatchLog AiRun(MapData map)
    {
        MatchFlow match = MatchFlow.CreateUnvalidated(
            map, MatchFixtures.Seed, [P0, P1], MatchFixtures.Relics(map), MatchOptions.Immediate with { ContentSet = ContentSet.V1 });
        match.PlantSequentially([(P0, 0), (P1, 1)]);
        MatchSession session = MatchSession.ForMatch(match, SimFixtures.PinPreCalibration(SimFixtures.Config(turnLimit: 6, players: 2)));
        return MatchLog.Parse(session.Run().FullText());
    }
}
