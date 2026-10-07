using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Match;

namespace Siege.Core.Tests.MatchSetup;

/// <summary>
/// 规格：openspec/changes/map-generator/specs/match-setup —— Requirement: 对局配置公开完整地图标识（tasks 2.3）；
/// map-generation「地图种子与确定性」留给段 B 的两条 Scenario：对局种子不影响地图、地图种子不扰动对局随机。
/// retire-legacy-maps 段 A2：生成图由 <c>gen:</c>（边疆档）改为 <c>board:</c>（棋盘档；带参数的写法 <c>board:12345:n9</c> 对应原 <c>gen:12345:p7</c>，
/// 缺省参数省略的规范化对应原 <c>gen:42:p6</c> → <c>gen:42</c>），内置图由 v5 / 边疆图 v2 改为 4 人内置棋盘图。
/// </summary>
public class 对局配置公开完整地图标识Tests
{
    private static readonly PlayerId[] Four = [new(0), new(1), new(2), new(3)];

    [Fact]
    public void 插旗阶段可见地图标识与对局种子_二者分开()
    {
        // 变异 MB-8：Publish 把 MapId 写成空串 → 本测试红。
        var seed = new GameSeed(20260920);
        MatchFlow match = MatchFlow.Create(MapCatalog.Resolve("board:12345:n9"), seed, Four, MatchOptions.Immediate);

        MatchPublicView view = match.Publish();

        Assert.Equal(MatchPhase.FlagPlanting, view.Phase);
        Assert.Equal("board:12345:n9", view.MapId);
        Assert.Equal(seed.ToString(), view.Seed);
        Assert.DoesNotContain("12345", view.Seed, StringComparison.Ordinal);      // 没有合并成一个数
        Assert.DoesNotContain(view.Seed, view.MapId, StringComparison.Ordinal);

        // 凭公开的标识，任何人都能重新得到同一张地图。
        Assert.Equal(MapFile.ToJson(match.Board.BaseMap), MapFile.ToJson(MapCatalog.Resolve(view.MapId)));

        // 内置图：标识就是内置图的标识；规范化写法（缺省人数与棋盘数省略）。
        Assert.Equal(SimFixtures.Board4, MatchFixturesOnBuiltin(SimFixtures.Board4).Publish().MapId);
        Assert.Equal("board:42", MatchFlow.Create(MapCatalog.Resolve("board:42:p4:n7"), seed, Four, MatchOptions.Immediate).Publish().MapId);
    }

    [Fact]
    public void 存档只存地图标识_在生成图上保存后按标识重建地图恢复_逐项相同()
    {
        // 现状核实：存档不含整张地图（盘面段只有棋子与本局改造），恢复时地图由调用方提供。取法：调用方读存档里的完整标识 →
        // MapCatalog 按标识重新生成 → Restore。这里的"恢复方"手里没有保存方的任何对象，只有那段 JSON。
        // 变异 MB-9：Serialize 把地图标识写成不带参数的生成图标识 → 本测试红（重建出缺省棋盘数的图，标识不符被拒）。
        MatchFlow match = MatchFlow.Create(MapCatalog.Resolve("board:12345:n9"), new GameSeed(11), Four, MatchOptions.Immediate);
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
        Assert.Equal("board:12345:n9", MatchFlow.SavedMapId(json));

        MapData rebuilt = MapCatalog.Resolve(MatchFlow.SavedMapId(json));
        MatchFlow restored = MatchFlow.Restore(rebuilt, json);

        Assert.Equal("board:12345:n9", restored.Publish().MapId);
        Assert.Equal(MapFile.ToJson(match.Board.BaseMap), MapFile.ToJson(restored.Board.BaseMap));
        Assert.Equal(MapFile.ToJson(match.Map), MapFile.ToJson(restored.Map));
        Assert.Equal(match.Board.Serialize(), restored.Board.Serialize());
        Assert.Equal(match.Publish().Seed, restored.Publish().Seed);
        Assert.Equal(json, restored.Serialize());

        // 给错图（同种子、缺省棋盘数）响亮失败，不静默在另一张图上恢复。
        Assert.Throws<FormatException>(() => MatchFlow.Restore(MapCatalog.Resolve("board:12345"), json));
    }

    [Theory]
    [InlineData("board:12345:n9")]
    [InlineData(SimFixtures.Board4)]
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
        MatchFlow match = MatchFlow.Create(MapCatalog.Resolve("board:12345"), new GameSeed(11), Four, MatchOptions.Immediate);
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
        // retire-legacy-maps 段 A2：内置图由边疆图 v2 改为 4 人内置棋盘图——它是 board: 生成图的别名，存档里记的仍是内置名而不是 board: 标识。
        MatchFlow match = MatchFixturesOnBuiltin(SimFixtures.Board4);
        match.PlantPrototype();
        string json = match.Serialize();

        Assert.Equal(SimFixtures.Board4, MatchFlow.SavedMapId(json));
        MatchFlow restored = MatchFlow.Restore(MapCatalog.Resolve(MatchFlow.SavedMapId(json)), json);
        Assert.Equal(SimFixtures.Board4, restored.Publish().MapId);
        Assert.Equal(json, restored.Serialize());
    }

    [Theory]
    [InlineData(1UL, 2UL)]
    [InlineData(20260920UL, 99UL)]
    public void 对局种子不影响地图(ulong gameSeedA, ulong gameSeedB)
    {
        // 规格 Scenario：同一地图种子、两个不同的对局种子各开一局 → 两局的地图数据逐项相同（而对局里的随机确实不同）。
        MatchFlow a = MatchFlow.Create(MapCatalog.Resolve("board:777"), new GameSeed(gameSeedA), Four, MatchOptions.Immediate);
        MatchFlow b = MatchFlow.Create(MapCatalog.Resolve("board:777"), new GameSeed(gameSeedB), Four, MatchOptions.Immediate);

        Assert.Equal(MapFile.ToJson(a.Map), MapFile.ToJson(b.Map));
        Assert.Equal(a.Publish().MapId, b.Publish().MapId);
        Assert.NotEqual(a.Publish().Seed, b.Publish().Seed);
        Assert.NotEqual(a.Relics.Generation.Serialize(), b.Relics.Generation.Serialize());
    }

    [Fact]
    public void 地图种子不扰动对局随机()
    {
        // 规格 Scenario：在内置图上用某对局种子开局，信物内容与首回合顺序与引入生成器之前逐项相同。
        // retire-legacy-maps 段 A2：内置图由边疆图 v2 改为 4 人内置棋盘图。原黄金值（提交 71761d6、生成器出现之前、边疆图 v2 种子 42）里，
        // 首回合顺序 3,0,2,1 只由对局种子与人数决定，照用；选区 1,0,5,3（6 区）与信物摘要 CF10E3AB…（边疆图的信物格）随图作废，
        // 改为测试内独立复算：选区按 zone-pick 子流在升序空闲表里等概率抽取（与「原型插旗替代路径Tests」同一算式，不调被测实现），
        // 信物 = 直接以 relic-gen 子流对这张图生成的结果。结构性对照不变：先生成若干张图再开局，结果不变——生成器不与对局共享任何随机状态。
        MapData builtin = MapCatalog.Resolve(SimFixtures.Board4);
        (string Relics, string Order, string Zones) Open()
        {
            // flag-contest：选区钉在引入冒险概率之前，写死 p = 0。more-pieces-relics：写死内容集 v1。
            MatchFlow match = MatchFlow.Create(builtin, new GameSeed(42), Four, MatchOptions.Immediate with { FlagRisk = 0, ContentSet = ContentSet.V1 });
            var choices = match.PlantPrototype();
            return (match.Relics.Generation.Serialize(), string.Join(",", match.ActionOrder.Select(p => p.Value)), string.Join(",", choices.Select(c => c.Zone)));
        }

        (string relics, string order, string zones) = Open();
        for (ulong s = 1; s <= 5; s++)
        {
            _ = MapCatalog.Resolve($"board:{s}");
        }

        Assert.Equal((relics, order, zones), Open());
        Assert.Equal(GoldenFrontierOrder, order);
        Assert.Equal(ExpectedZones(builtin.BirthZones.Length, 42), zones);
        Assert.Equal(Siege.Core.Relics.RelicGenerator.Generate(builtin, new GameSeed(42), ContentSet.V1).Serialize(), relics);
        Assert.Contains($"map={SimFixtures.Board4}", relics, StringComparison.Ordinal);

        // 同一对局种子、换一张生成图：首回合顺序的随机序列不因地图种子而变（顺序子流只由对局种子决定）。
        static string OrderOn(string mapId)
        {
            MatchFlow match = MatchFlow.Create(MapCatalog.Resolve(mapId), new GameSeed(42), Four, MatchOptions.Immediate);
            match.PlantSequentially(Four.Select((p, i) => (p, i)));
            return string.Join(",", match.ActionOrder.Select(p => p.Value));
        }

        Assert.Equal(OrderOn("board:12345"), OrderOn("board:12346"));
        Assert.Equal(OrderOn("board:12345"), OrderOn("board:99:n9"));
    }

    /// <summary>首回合顺序黄金值（提交 71761d6、生成器出现之前；只由对局种子 42 与人数 4 决定，与地图无关）。</summary>
    private const string GoldenFrontierOrder = "3,0,2,1";

    /// <summary>区数多于人数、无人工选择、冒险概率 0 时的选区独立复算：P0..P3 依次从 zone-pick 子流在升序空闲表里等概率取一个。</summary>
    private static string ExpectedZones(int zoneCount, ulong seed)
    {
        Siege.Core.Determinism.RandomStream pick = new GameSeed(seed).Stream("zone-pick");
        List<int> free = [.. Enumerable.Range(0, zoneCount)];
        var zones = new List<int>();
        for (int p = 0; p < 4; p++)
        {
            int index = pick.NextInt(free.Count);
            zones.Add(free[index]);
            free.RemoveAt(index);
        }

        return string.Join(",", zones);
    }

    private static MatchFlow MatchFixturesOnBuiltin(string mapId) =>
        MatchFlow.Create(MapCatalog.Resolve(mapId), new GameSeed(3), Four, MatchOptions.Immediate);
}
