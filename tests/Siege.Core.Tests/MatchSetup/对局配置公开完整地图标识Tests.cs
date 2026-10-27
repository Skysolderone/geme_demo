using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Core.PieceRelics;

namespace Siege.Core.Tests.MatchSetup;

/// <summary>
/// 规格：openspec/changes/map-generator/specs/match-setup —— Requirement: 对局配置公开完整地图标识（tasks 2.3）；
/// map-generation「地图种子与确定性」留给段 B 的两条 Scenario：对局种子不影响地图、地图种子不扰动对局随机。
/// </summary>
// retire-legacy-maps 1.8：本类原在 gen: / 内置图上以 Legacy 模式建局、经内部口 MatchFlow.RestoreLegacy 恢复；
// 改到链接图 link:（链接模式建局，挂内置固定配对脚本）、经玩家入口 MatchFlow.Restore 恢复。所守规则与断言不变；
// 只属于 gen: 参数（平台数、规范化省略缺省平台数）与内置图的半句随旧地图删除，各处注明。
public class 对局配置公开完整地图标识Tests
{
    private static readonly PlayerId[] Four = [new(0), new(1), new(2), new(3)];

    /// <summary>链接图上建局（插旗阶段），挂内置固定配对脚本。</summary>
    private static MatchFlow CreateLink(string mapId, GameSeed seed, ContentSet? contentSet = null)
    {
        MatchOptions options = MatchOptions.Immediate;
        MatchFlow match = MatchFlow.Create(MapCatalog.Resolve(mapId), seed, Four, contentSet is { } set ? options with { ContentSet = set } : options);
        return match;
    }

    [Fact]
    public void 插旗阶段可见地图标识与对局种子_二者分开()
    {
        // 变异 MB-8：Publish 把 MapId 写成空串 → 本测试红。
        var seed = new GameSeed(20260920);
        MatchFlow match = CreateLink("link:12345:4", seed);

        MatchPublicView view = match.Publish();

        Assert.Equal(MatchPhase.FlagPlanting, view.Phase);
        Assert.Equal("link:12345:4", view.MapId);
        Assert.Equal(seed.ToString(), view.Seed);
        Assert.DoesNotContain("12345", view.Seed, StringComparison.Ordinal);      // 没有合并成一个数
        Assert.DoesNotContain(view.Seed, view.MapId, StringComparison.Ordinal);

        // 凭公开的标识，任何人都能重新得到同一张地图。
        Assert.Equal(MapFile.ToJson(match.Board.BaseMap), MapFile.ToJson(MapCatalog.Resolve(view.MapId)));

        // 规范化写法：前导零按数值。retire-legacy-maps：原另有"内置图的标识就是内置图的标识""gen: 省略缺省平台数"两句，随旧地图删除。
        Assert.Equal("link:42:4", CreateLink("link:0042:4", seed).Publish().MapId);
    }

    [Fact]
    public void 存档只存地图标识_在生成图上保存后按标识重建地图恢复_逐项相同()
    {
        // 现状核实：存档不含整张地图（盘面段只有棋子与本局改造），恢复时地图由调用方提供。取法：调用方读存档里的完整标识 →
        // MapCatalog 按标识重新生成 → Restore。这里的"恢复方"手里没有保存方的任何对象，只有那段 JSON。
        // 变异 MB-9：Serialize 写错地图标识 → 本测试红（按错标识重建出另一张图，标识或摘要不符被拒）。
        MatchFlow match = CreateLink("link:12345:4", new GameSeed(11));
        match.PlantPrototype();
        var runner = new MatchRunner(match);
        foreach (PlayerId p in Four)
        {
            runner.SetController(p, HeuristicAi.Create(match, p, AiDifficulty.Easy, config: AiSearchConfig.ForMap(AiDifficulty.Easy, match.Map.PlayableCount)));
        }

        for (int i = 0; i < 6; i++)
        {
            runner.RunTurn();
        }

        string json = match.Serialize();
        Assert.DoesNotContain("\"Heights\"", json, StringComparison.Ordinal);      // 存档里没有地图数据
        Assert.Equal("link:12345:4", MatchFlow.SavedMapId(json));

        MapData rebuilt = MapCatalog.Resolve(MatchFlow.SavedMapId(json));
        MatchFlow restored = MatchFlow.Restore(rebuilt, json);

        Assert.Equal("link:12345:4", restored.Publish().MapId);
        Assert.Equal(MapFile.ToJson(match.Board.BaseMap), MapFile.ToJson(restored.Board.BaseMap));
        Assert.Equal(MapFile.ToJson(match.Map), MapFile.ToJson(restored.Map));
        Assert.Equal(match.Board.Serialize(), restored.Board.Serialize());
        Assert.Equal(match.Publish().Seed, restored.Publish().Seed);
        Assert.Equal(json, restored.Serialize());

        // 给错图（相邻地图种子）响亮失败，不静默在另一张图上恢复。
        Assert.Throws<FormatException>(() => MatchFlow.Restore(MapCatalog.Resolve("link:12346:4"), json));
    }

    [Theory]
    [InlineData("link:12345:4")]
    [InlineData("link:7:4")]
    public void 存档记地图内容摘要_标识相同而内容不同的地图_恢复时报地图不一致(string mapId)
    {
        // 段 B 检查（负责人裁决 4）：存档只存标识，生成器改版之后按标识会重建出另一张图——与日志同一风险（design D5），同样响亮失败。
        // 摘要与日志首部同一算法（MapFile.Digest），取开局地图。
        // 变异 CB-1：Serialize 不写 MapDigest → 本测试红；CB-2：RequireMap 去掉摘要比对 → 本测试红（改过一格的图被静默接受或在后面报别的错）。
        MapData map = MapCatalog.Resolve(mapId);
        MatchFlow match = CreateLink(mapId, new GameSeed(11));
        match.PlantPrototype();
        string json = match.Serialize();

        Assert.Contains($"\"MapDigest\": \"{MapFile.Digest(map)}\"", json, StringComparison.Ordinal);
        MatchFlow restored = MatchFlow.Restore(MapCatalog.Resolve(MatchFlow.SavedMapId(json)), json);
        Assert.False(restored.MapDigestBackfilled);
        Assert.Equal(json, restored.Serialize());

        // 同一标识、改了一格（从岩石表里拿掉坐标序第一个岩石格）：恢复在地图这一关就停下，消息指明是地图不一致并给出两个摘要。
        Coord victim = map.Obstacles.Order().First();
        string mapJson = MapFile.ToJson(map);
        string cell = $"\"{victim.ToNotation()}\",";
        Assert.Contains(cell, mapJson, StringComparison.Ordinal);
        MapData tampered = MapFile.FromJson(mapJson.Remove(mapJson.IndexOf(cell, StringComparison.Ordinal), cell.Length));
        Assert.Equal(map.Id, tampered.Id);

        FormatException mismatch = Assert.Throws<FormatException>(() => MatchFlow.Restore(tampered, json));
        Assert.Contains("地图不一致", mismatch.Message, StringComparison.Ordinal);
        Assert.Contains(mapId, mismatch.Message, StringComparison.Ordinal);
        Assert.Contains(MapFile.Digest(map), mismatch.Message, StringComparison.Ordinal);
        Assert.Contains(MapFile.Digest(tampered), mismatch.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 旧存档缺地图摘要_跳过比对并留痕_再存档补写()
    {
        // 照既有回填口径（ZoneCount、匠人权重……）：旧存档读入不抛；MapDigestBackfilled 为 true 供调用方查知"这张图的内容没有核对过"。
        // 变异 CB-3：把"存档没有摘要"当成不一致 → 本测试红。
        MatchFlow match = CreateLink("link:12345:4", new GameSeed(11));
        match.PlantPrototype();
        string json = match.Serialize();
        System.Text.Json.Nodes.JsonNode node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        Assert.True(node.AsObject().Remove("MapDigest"));
        string legacy = node.ToJsonString();
        Assert.DoesNotContain("MapDigest", legacy, StringComparison.Ordinal);

        MatchFlow fromLegacy = MatchFlow.Restore(MapCatalog.Resolve(MatchFlow.SavedMapId(legacy)), legacy);

        Assert.True(fromLegacy.MapDigestBackfilled);
        Assert.False(match.MapDigestBackfilled);
        Assert.Equal(json, fromLegacy.Serialize());   // 再存档按当前地图补写摘要，其余逐字节相同
    }

    [Theory]
    [InlineData(1UL, 2UL)]
    [InlineData(20260920UL, 99UL)]
    public void 对局种子不影响地图(ulong gameSeedA, ulong gameSeedB)
    {
        // 规格 Scenario：同一地图种子、两个不同的对局种子各开一局 → 两局的地图数据逐项相同（而对局里的随机确实不同）。
        MatchFlow a = CreateLink("link:777:4", new GameSeed(gameSeedA));
        MatchFlow b = CreateLink("link:777:4", new GameSeed(gameSeedB));

        Assert.Equal(MapFile.ToJson(a.Map), MapFile.ToJson(b.Map));
        Assert.Equal(a.Publish().MapId, b.Publish().MapId);
        Assert.NotEqual(a.Publish().Seed, b.Publish().Seed);
        Assert.NotEqual(Forecast(a), Forecast(b));   // relic-redesign W1：对局里的随机改看信物刷新（开局生成随旧信物删除）
    }

    [Fact]
    public void 地图种子不扰动对局随机()
    {
        // 规格 Scenario：在某张图上用某对局种子开局，信物刷新、首回合顺序与插旗结果不因此前生成过别的地图而变——生成器不与对局共享任何随机状态。
        // retire-legacy-maps 1.8：原在内置边疆图上（黄金值取自生成器出现之前的提交 71761d6，冒险概率写死 0）；内置图与 gen: 生成器删除后改到 link:1、
        // 夹在中间的改为链接档生成器 LinkMapGenerator；结构性对照（夹生成前后逐项相同、换地图种子不改首回合顺序）不变。
        // 原黄金值属于边疆图、随之删除；这里改钉 link:1 / 种子 42 / v1 的首回合顺序与盲选结果（删除旧图之前按当时实现取下）。
        static (string Relics, string Order, string Zones) Open()
        {
            MatchFlow match = CreateLink("link:1:4", new GameSeed(42), ContentSet.V1);
            var choices = match.PlantPrototype();
            return (Forecast(match), string.Join(",", match.ActionOrder.Select(p => p.Value)), string.Join(",", choices.Select(c => c.Zone)));
        }

        (string relics, string order, string zones) = Open();
        for (ulong s = 1; s <= 5; s++)
        {
            LinkMapGenerator.Generate(s, 4);
        }

        Assert.Equal((relics, order, zones), Open());
        Assert.False(string.IsNullOrEmpty(relics));
        Assert.Equal(GoldenLinkOrder, order);
        Assert.Equal(GoldenLinkZones, zones);

        // 同一对局种子、换一张生成图：首回合顺序的随机序列不因地图种子而变（顺序子流只由对局种子决定）。
        static string OrderOn(string mapId)
        {
            MatchFlow match = CreateLink(mapId, new GameSeed(42));
            match.PlantSequentially(Four.Select((p, i) => (p, i)));
            return string.Join(",", match.ActionOrder.Select(p => p.Value));
        }

        Assert.Equal(OrderOn("link:12345:4"), OrderOn("link:12346:4"));
        Assert.Equal(OrderOn("link:12345:4"), OrderOn("link:99:4"));
    }

    private const string GoldenLinkOrder = "3,0,2,1";
    // map-expand-25 段 A2：link:1:4 浮空岛 10 块平台，flag-pick 子流的同一组抽取按 10 取模（原 5 块时为 "4,3,1,0"，逐项 ≡ mod 5）；首回合顺序不变。
    private const string GoldenLinkZones = "9,8,1,5";

    /// <summary>该局开局盘面上第 3 大回合的信物预告（relic-redesign W1：对局随机的代表，子流 relic-spawn:4）。</summary>
    private static string Forecast(MatchFlow match) =>
        string.Join(",", RelicSpawner.Forecast(match.Board, new BirthBoardArea(match.Board.Map), match.Seed, 3, match.Players.Length, 0)!.Slots);
}
