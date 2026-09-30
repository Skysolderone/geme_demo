using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Presentation.Visibility;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>规格：board-map / map-definition —— Requirement: 棋盘清单</summary>
public class 棋盘清单Tests
{
    private static readonly PlayerId P0 = TestMaps.P0;
    private static readonly PlayerId P1 = TestMaps.P1;

    [Theory]
    [InlineData("siege-2p-base-v1", true)]
    [InlineData("siege-3p-base-v1", true)]
    [InlineData("siege-4p-base-v1", false)]
    [InlineData("siege-4p-base-v2", false)]
    [InlineData("siege-4p-base-v3", false)]
    [InlineData("siege-4p-base-v5", true)]
    [InlineData("siege-frontier-v2", true)]
    public void 旧地图没有棋盘清单(string id, bool diskIsExport)
    {
        // Scenario：读入旧地图文件再导出 → 棋盘清单为空，导出文件与引入棋盘清单之前逐字节相同。
        // 七张内置图全过一遍。其中四张的磁盘文件就是导出结果（"引入之前"的字节），直接与磁盘比；
        // v1–v3 是手工保留的历史存档（带 _comment、v1 没有地形字段），磁盘文件本来就不等于导出结果，只比"导出里没有该字段且再往返稳定"。
        string disk = File.ReadAllText(Path.Combine(FrontierFixtures.RepoRoot(), "maps", id + ".json"));
        Assert.DoesNotContain("\"Boards\"", disk, StringComparison.Ordinal);

        MapData map = MapFile.FromJson(disk);
        string json = MapFile.ToJson(map);

        Assert.False(map.Boards.IsDefault);
        Assert.Empty(map.Boards);
        Assert.DoesNotContain("\"Boards\"", json, StringComparison.Ordinal);
        Assert.Equal(json, MapFile.ToJson(MapFile.FromJson(json)));
        if (diskIsExport)
        {
            Assert.Equal(Normalize(disk), Normalize(json));
        }
    }

    [Fact]
    public void 出生棋盘即出生区()
    {
        // Scenario：第 2 块出生棋盘外接矩形内的全部格子恰是出生区 2 的全部格子（编号自 1 起，即下标 1）。
        MapData map = MapFile.FromJson(MapFile.ToJson(BoardMapFixtures.Map()));

        BoardPlate[] births = [.. map.Boards.Where(b => b.Kind == BoardPlateKind.Birth)];
        Assert.Equal(map.BirthZones.Length, births.Length);
        for (int i = 0; i < births.Length; i++)
        {
            Assert.Equal(map.BirthZones[i].Order(), births[i].Cells().Order());
            Assert.All(births[i].Cells(), c => Assert.Equal(i, map.BirthZoneOf(c)));
        }

        // 第 2 块：左下角 X5、右上角 AB9，25 格；外接矩形的判定与格子枚举是同一份。
        BoardPlate second = births[1];
        Assert.Equal(["X5", "AB9"], new[] { second.Cells().Min(), second.Cells().Max() }.Select(c => c.ToNotation()));
        Assert.Equal(25, second.Cells().Count());
        Assert.All(map.AllCoords(), c => Assert.Equal(map.BirthZones[1].Contains(c), second.Contains(c)));

        // 公共棋盘不属于任何出生区。
        BoardPlate open = Assert.Single(map.Boards, b => b.Kind == BoardPlateKind.Public);
        Assert.All(open.Cells(), c => Assert.Null(map.BirthZoneOf(c)));
    }

    [Fact]
    public void 清单不参与结算()
    {
        // Scenario：把棋盘清单清空后在相同种子下重放同一份对局 → 每一步的盘面、提子与势力值逐项相同。
        // 第一段是摆好的提子局面（含双字母列上的落点），逐步比对局指纹；第二段是两名 AI 的真实跑局，逐小回合比日志快照与事件。
        MapData full = BoardMapFixtures.Map();
        MapData cleared = full with { Boards = [] };
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
    public void 标准档与边疆档地图带棋盘清单被拒绝()
    {
        // 「标准档与边疆档地图的棋盘清单 MUST 为空」：清单只属于棋盘档。
        BoardPlate plate = new(new Coord(0, 0), 5, 5, BoardPlateKind.Public);
        foreach (MapData map in new[] { Siege.Core.Board.Maps.FourPlayerBaseMap.Create(), FrontierFixtures.Map() })
        {
            Assert.True(MapValidator.Validate(map).IsValid);

            MapValidationResult result = MapValidator.Validate(map with { Boards = [plate] });

            MapValidationFailure failure = Assert.Single(result.Failures);
            Assert.Equal("BOARDS_NOT_ALLOWED", failure.Code);
            Assert.Contains("棋盘清单", failure.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void 默认棋盘视图模型透传棋盘清单()
    {
        // D2 / D10：渲染用的棋盘清单全部取自视图模型，Godot 不自行推断；旧图的清单为空，画面不变。
        MapData map = BoardMapFixtures.Map();

        DefaultBoardView view = DefaultBoardView.From(Started(map).PublicWorldOf());

        Assert.Equal(map.Boards.AsEnumerable(), view.Boards.AsEnumerable());
        Assert.Equal((29, 13), (view.Width, view.Height));

        DefaultBoardView legacy = DefaultBoardView.From(MatchFixtures.Started().PublicWorldOf());
        Assert.False(legacy.Boards.IsDefault);
        Assert.Empty(legacy.Boards);
    }

    [Fact]
    public void 棋盘清单项的文件写法()
    {
        // 每项：左下角坐标（围棋记法）、宽、高、类别；类别写成名字不写数字。
        string json = MapFile.ToJson(BoardMapFixtures.Map());

        MapData edited = MapFile.FromJson(json.Replace("\"Origin\": \"X5\"", "\"Origin\": \"x5\"", StringComparison.Ordinal));

        Assert.Contains("\"Origin\": \"X5\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Kind\": \"Birth\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Kind\": \"Public\"", json, StringComparison.Ordinal);
        Assert.Equal(BoardMapFixtures.Birth1, edited.Boards[1]);

        // 类别写成未定义的数字、宽高不是正数：指名报出，不读成一块坏棋盘。
        Assert.Contains("Boards", Assert.Throws<FormatException>(
            () => MapFile.FromJson(json.Replace("\"Kind\": \"Public\"", "\"Kind\": 9", StringComparison.Ordinal))).Message, StringComparison.Ordinal);
        Assert.Contains("Boards", Assert.Throws<FormatException>(
            () => MapFile.FromJson(json.Replace("\"Width\": 9,", "\"Width\": 0,", StringComparison.Ordinal))).Message, StringComparison.Ordinal);
    }

    /// <summary>摆好的提子局面：P0 三面围住公共棋盘中心的 P1 子，第 5 大回合 P0 落第四面提子，P1 在双字母列上应一手，P0 再落一手。每步记对局指纹。</summary>
    private static string[] ScriptedSteps(MapData map)
    {
        MatchFlow match = Started(map).AtRound(5, [P0, P1]);
        match.Stones(P1, "P7", "AA6");
        match.Stones(P0, "O7", "Q7", "P6", "D6");
        var steps = new List<string>();

        match.PlayTurn("P8");
        Assert.Null(match.Board[Coord.Parse("P7")].Occupant);   // 样本口径：这一步确实提了子
        steps.Add(PresentationFixtures.Fingerprint(match));

        match.PlayTurn("AA7");
        Assert.Equal(P1, match.Board[Coord.Parse("AA7")].Occupant?.Owner);
        steps.Add(PresentationFixtures.Fingerprint(match));

        match.PlayTurn("P7");
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

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
}
