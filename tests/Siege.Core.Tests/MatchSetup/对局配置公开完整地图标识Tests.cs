using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Match;

namespace Siege.Core.Tests.MatchSetup;

/// <summary>
/// 规格：openspec/changes/map-generator/specs/match-setup —— Requirement: 对局配置公开完整地图标识（tasks 2.3）；
/// map-generation「地图种子与确定性」留给段 B 的两条 Scenario：对局种子不影响地图、地图种子不扰动对局随机。
/// </summary>
public class 对局配置公开完整地图标识Tests
{
    private static readonly PlayerId[] Four = [new(0), new(1), new(2), new(3)];

    [Fact]
    public void 插旗阶段可见地图标识与对局种子_二者分开()
    {
        // 变异 MB-8：Publish 把 MapId 写成空串 → 本测试红。
        var seed = new GameSeed(20260920);
        MatchFlow match = MatchFlow.Create(MapCatalog.Resolve("gen:12345:p7"), seed, Four, MatchOptions.Immediate);

        MatchPublicView view = match.Publish();

        Assert.Equal(MatchPhase.FlagPlanting, view.Phase);
        Assert.Equal("gen:12345:p7", view.MapId);
        Assert.Equal(seed.ToString(), view.Seed);
        Assert.DoesNotContain("12345", view.Seed, StringComparison.Ordinal);      // 没有合并成一个数
        Assert.DoesNotContain(view.Seed, view.MapId, StringComparison.Ordinal);

        // 凭公开的标识，任何人都能重新得到同一张地图。
        Assert.Equal(MapFile.ToJson(match.Board.BaseMap), MapFile.ToJson(MapCatalog.Resolve(view.MapId)));

        // 内置图：标识就是内置图的标识；规范化写法（缺省平台数省略）。
        Assert.Equal(FourPlayerBaseMap.Id, MatchFixturesOnBuiltin(FourPlayerBaseMap.Id).Publish().MapId);
        Assert.Equal("gen:42", MatchFlow.Create(MapCatalog.Resolve("gen:42:p6"), seed, Four, MatchOptions.Immediate).Publish().MapId);
    }

    [Fact]
    public void 存档只存地图标识_在生成图上保存后按标识重建地图恢复_逐项相同()
    {
        // 现状核实：存档不含整张地图（盘面段只有棋子与本局改造），恢复时地图由调用方提供。取法：调用方读存档里的完整标识 →
        // MapCatalog 按标识重新生成 → Restore。这里的"恢复方"手里没有保存方的任何对象，只有那段 JSON。
        // 变异 MB-9：Serialize 把地图标识写成不带参数的 gen:<种子> → 本测试红（重建出 6 平台的图，标识不符被拒）。
        MatchFlow match = MatchFlow.Create(MapCatalog.Resolve("gen:12345:p7"), new GameSeed(11), Four, MatchOptions.Immediate);
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
        Assert.Equal("gen:12345:p7", MatchFlow.SavedMapId(json));

        MapData rebuilt = MapCatalog.Resolve(MatchFlow.SavedMapId(json));
        MatchFlow restored = MatchFlow.Restore(rebuilt, json);

        Assert.Equal("gen:12345:p7", restored.Publish().MapId);
        Assert.Equal(MapFile.ToJson(match.Board.BaseMap), MapFile.ToJson(restored.Board.BaseMap));
        Assert.Equal(MapFile.ToJson(match.Map), MapFile.ToJson(restored.Map));
        Assert.Equal(match.Board.Serialize(), restored.Board.Serialize());
        Assert.Equal(match.Publish().Seed, restored.Publish().Seed);
        Assert.Equal(json, restored.Serialize());

        // 给错图（同种子、缺省平台数）响亮失败，不静默在另一张图上恢复。
        Assert.Throws<FormatException>(() => MatchFlow.Restore(MapCatalog.Resolve("gen:12345"), json));
    }

    [Theory]
    [InlineData("gen:12345:p7")]
    [InlineData("siege-frontier-v2")]
    public void 存档记地图内容摘要_标识相同而内容不同的地图_恢复时报地图不一致(string mapId)
    {
        // 段 B 检查（负责人裁决 4）：存档只存标识，生成器改版 / 内置图被改之后按标识会重建出另一张图——与日志同一风险（design D5），同样响亮失败。
        // 摘要与日志首部同一算法（MapFile.Digest），取开局地图。内置图同样写、同样比。
        // 变异 CB-1：Serialize 不写 MapDigest → 本测试红；CB-2：RequireMap 去掉摘要比对 → 本测试红（改过一格的图被静默接受或在后面报别的错）。
        MapData map = MapCatalog.Resolve(mapId);
        MatchFlow match = MatchFlow.Create(map, new GameSeed(11), Four, MatchOptions.Immediate);
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
        MatchFlow match = MatchFlow.Create(MapCatalog.Resolve("gen:12345"), new GameSeed(11), Four, MatchOptions.Immediate);
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

    [Fact]
    public void 内置图上的存档_地图标识仍是内置图的标识()
    {
        // 规格 Scenario「旧存档不受影响」：引入生成图没有改存档里地图标识的写法；按它解析回来的仍是那张内置图。
        MatchFlow match = MatchFixturesOnBuiltin(FrontierMapV2.Id);
        match.PlantPrototype();
        string json = match.Serialize();

        Assert.Equal(FrontierMapV2.Id, MatchFlow.SavedMapId(json));
        MatchFlow restored = MatchFlow.Restore(MapCatalog.Resolve(MatchFlow.SavedMapId(json)), json);
        Assert.Equal(FrontierMapV2.Id, restored.Publish().MapId);
        Assert.Equal(json, restored.Serialize());
    }

    [Theory]
    [InlineData(1UL, 2UL)]
    [InlineData(20260920UL, 99UL)]
    public void 对局种子不影响地图(ulong gameSeedA, ulong gameSeedB)
    {
        // 规格 Scenario：同一地图种子、两个不同的对局种子各开一局 → 两局的地图数据逐项相同（而对局里的随机确实不同）。
        MatchFlow a = MatchFlow.Create(MapCatalog.Resolve("gen:777"), new GameSeed(gameSeedA), Four, MatchOptions.Immediate);
        MatchFlow b = MatchFlow.Create(MapCatalog.Resolve("gen:777"), new GameSeed(gameSeedB), Four, MatchOptions.Immediate);

        Assert.Equal(MapFile.ToJson(a.Map), MapFile.ToJson(b.Map));
        Assert.Equal(a.Publish().MapId, b.Publish().MapId);
        Assert.NotEqual(a.Publish().Seed, b.Publish().Seed);
        Assert.NotEqual(a.Relics.Generation.Serialize(), b.Relics.Generation.Serialize());
    }

    [Fact]
    public void 地图种子不扰动对局随机()
    {
        // 规格 Scenario：在内置图 siege-frontier-v2 上用某对局种子开局，信物内容与首回合顺序与引入生成器之前逐项相同。
        // 黄金值取自提交 71761d6（生成器出现之前）的干净 worktree、同一算式（见实施记录）；再加一条结构性对照：先生成若干张图再开局，结果不变——
        // 生成器不与对局共享任何随机状态。
        static (string Relics, string Order, string Zones) Open()
        {
            // flag-contest：黄金选区钉在引入冒险概率之前，写死 p = 0。
            // more-pieces-relics 段 B（tasks 2.7，归因：信物分布）：信物黄金摘要是原六类的分布，新局缺省 v2 的千分制十类表使之整体改变；
            // 本测试钉的是"地图生成器不与对局共享随机状态"，与内容集无关——写死 v1（= 改动前的生成），黄金值不重建。
            MatchFlow match = MatchFlow.Create(
                MapCatalog.Resolve(FrontierMapV2.Id), new GameSeed(42), Four, MatchOptions.Immediate with { FlagRisk = 0, ContentSet = ContentSet.V1 });
            var choices = match.PlantPrototype();
            return (match.Relics.Generation.Serialize(), string.Join(",", match.ActionOrder.Select(p => p.Value)), string.Join(",", choices.Select(c => c.Zone)));
        }

        (string relics, string order, string zones) = Open();
        for (ulong s = 1; s <= 5; s++)
        {
            FrontierMapGenerator.Generate(s);
        }

        Assert.Equal((relics, order, zones), Open());
        Assert.Equal(GoldenFrontierOrder, order);
        Assert.Equal(GoldenFrontierZones, zones);
        // 段 B：信物账本的记录里带着地图标识（RelicGenerationRecord.MapId），v1 → v2 使这段文本变了；
        // 信物内容本身逐字节不变——把标识换回 v1 之后，摘要仍是引入生成器之前的那个黄金值，所以黄金值不重建。
        Assert.Contains("siege-frontier-v2", relics, StringComparison.Ordinal);
        string asV1 = relics.Replace("siege-frontier-v2", "siege-frontier-v1", StringComparison.Ordinal);
        Assert.Equal(GoldenFrontierRelicDigest, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(asV1))));

        // 同一对局种子、换一张生成图：首回合顺序的随机序列不因地图种子而变（顺序子流只由对局种子决定）。
        static string OrderOn(string mapId)
        {
            MatchFlow match = MatchFlow.Create(MapCatalog.Resolve(mapId), new GameSeed(42), Four, MatchOptions.Immediate);
            match.PlantSequentially(Four.Select((p, i) => (p, i)));
            return string.Join(",", match.ActionOrder.Select(p => p.Value));
        }

        Assert.Equal(OrderOn("gen:12345"), OrderOn("gen:12346"));
        Assert.Equal(OrderOn("gen:12345"), OrderOn("gen:99:p8"));
    }

    private const string GoldenFrontierOrder = "3,0,2,1";
    private const string GoldenFrontierZones = "1,0,5,3";
    private const string GoldenFrontierRelicDigest = "CF10E3AB10E0C2CE00FE92E1B0CBB7B451943DB96D65E89C99D6EF6D95E00BBC";

    private static MatchFlow MatchFixturesOnBuiltin(string mapId) =>
        MatchFlow.Create(MapCatalog.Resolve(mapId), new GameSeed(3), Four, MatchOptions.Immediate);
}
