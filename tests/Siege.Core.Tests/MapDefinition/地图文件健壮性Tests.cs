using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>
/// 地图文件是设计师手工维护的，畸形输入是常态而不是例外。
/// 这里钉住的是：坏文件 MUST 抛出说得出问题在哪的异常，
/// MUST NOT 静默产出一个"看起来能用"的坏 <see cref="MapData"/>。
/// </summary>
/// <remarks>规格：map-definition —— Requirement: 地图为设计师固定的静态数据</remarks>
public class 地图文件健壮性Tests
{
    /// <summary>
    /// 坐标键字典与嵌套对象读写的载体：link:1:4 手工补 2 条必死口袋豁免（出生区 0 核心区的前两格，各带理由）。
    /// relic-redesign W1：原载体 RelicCarrier（手工补信物格）随静态信物格删除；本类钉的大小写宽容、未知字段、重复键折叠改由豁免表（坐标键字典）
    /// 与棋盘清单（嵌套对象 + 枚举 Kind）承担，断言不变（不过校验器——只走序列化往返）。
    /// </summary>
    private static MapData ExemptionCarrier(string secondReason = "测试豁免二")
    {
        MapData link = MapCatalog.Resolve("link:1:4");
        Coord[] core = [.. link.BirthZones[0].Order()];
        return link with
        {
            PocketExemptions = [core[0], core[1]],
            PocketExemptionReasons = ImmutableDictionary<Coord, string>.Empty.Add(core[0], "测试豁免一").Add(core[1], secondReason),
        };
    }

    // retire-legacy-maps 4.1（L7）：规格档字段改为必填（缺失即报"已移除"），本类手写的残缺 / 畸形 JSON 一律补上 "Profile":"Link"，
    // 使各条仍落在它原本要测的那一处错误上；断言不变。
    [Theory]
    [InlineData("null", "不是合法 JSON")]
    [InlineData("{\"Profile\":\"Link\"}", "无法解析为围棋记法坐标")]          // 缺 CentralEntrance
    [InlineData("{\"Profile\":\"Link\",\"CentralEntrance\":\"I5\"}", "\"I5\"")]                    // I 不是合法列字母
    [InlineData("{\"Profile\":\"Link\",\"CentralEntrance\":\"A0\"}", "\"A0\"")]                    // 行号自 1 起
    [InlineData("{\"Profile\":\"Link\",\"CentralEntrance\":\"ZZZ99\"}", "\"ZZZ99\"")]              // 列标至多两个字母（board-map D6 起 ZZ99 是合法坐标）
    [InlineData("{\"Profile\":\"Link\",\"CentralEntrance\":\"F6\",\"Obstacles\":[null]}", "<null>")]
    public void 畸形字段抛出可诊断的FormatException(string json, string expectedFragment)
    {
        FormatException ex = Assert.Throws<FormatException>(() => MapFile.FromJson(json));

        Assert.Contains(expectedFragment, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Obstacles")]
    [InlineData("BirthZones")]
    [InlineData("RelicCells")]
    [InlineData("ChokePoints")]
    [InlineData("PocketExemptions")]
    [InlineData("Id")]
    public void 显式写成null的字段被指名报出(string field)
    {
        string json = $"{{\"Profile\":\"Link\",\"CentralEntrance\":\"F6\",\"{field}\":null}}";

        FormatException ex = Assert.Throws<FormatException>(() => MapFile.FromJson(json));

        Assert.Contains(field, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"Profile\":\"Link\",\"CentralEntrance\":\"F6\",\"Width\":\"eleven\"}")]
    [InlineData("{\"Profile\":\"Link\",\"CentralEntrance\":\"F6\",\"Boards\":[{\"Origin\":\"A1\",\"Width\":5,\"Height\":5,\"Kind\":\"Mystery\"}]}")]
    [InlineData("not json at all")]
    public void 类型或枚举不认识时抛JsonException(string json)
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => MapFile.FromJson(json));
    }

    [Fact]
    public void 结构残缺的文件过不了校验()
    {
        // 反序列化本身宽容（缺字段取默认值），但这样的地图 MUST NOT 通过静态校验：
        // 根因（尺寸为 0）要能在失败列表里被直接看到，而不是只剩"可落子格为 0"这种症状。
        MapData degenerate = MapFile.FromJson("{\"Profile\":\"Link\",\"CentralEntrance\":\"F6\",\"MaxPlayers\":4}");

        MapValidationResult result = MapValidator.Validate(degenerate);

        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, f => f.Code == "MAP_DIMENSION_INVALID");
        Assert.Contains(result.Failures, f => f.Code == "MAP_ID_MISSING");
        Assert.Throws<MapValidationException>(() => GameBoard.Load(degenerate));
    }

    [Fact]
    public void 同一坐标重复写会被合并而不是静默取一个()
    {
        // JSON 对象的重复键由序列化器"后者胜出"地吞掉，坐标记法的大小写差异同样会折叠成同一格。
        // 这里钉住：折叠后豁免数量变了（合并，而不是静默保留两个之一的原样）。
        // relic-redesign W1：原载体是信物格表（随静态信物格删除），改为豁免表（同样是坐标键字典），把第二条豁免的键改写成第一条的小写，断言不变。
        // 两条豁免理由取相同文本（原信物格样本两格的内容也相同）——折叠只合并同值；值不同时由字典构造响亮抛错，不在本条口径内。
        string canonical = MapFile.ToJson(ExemptionCarrier(secondReason: "测试豁免一"));
        MapData parsed = MapFile.FromJson(canonical);
        Coord[] keys = [.. parsed.PocketExemptions.Order()];
        Assert.Equal(2, keys.Length);   // 样本口径
        string json = canonical.Replace($"\"{keys[1].ToNotation()}\":", $"\"{keys[0].ToNotation().ToLowerInvariant()}\":", StringComparison.Ordinal);
        Assert.NotEqual(canonical, json);

        MapData folded = MapFile.FromJson(json);

        Assert.Single(folded.PocketExemptions);
        Assert.DoesNotContain(keys[1], folded.PocketExemptions);
    }

    [Fact]
    public void 全小写键名的地图能正确加载()
    {
        // 设计师手写的键名不必与 DTO 的大小写一致。把基准图的全部 JSON 键名压成小写，
        // 往返后 MUST 与原图逐字节一致——包括嵌套的 zone / budget 与豁免理由。
        // retire-legacy-maps 1.6：载体由 v5 改为 link:1 初始图（本类各条同），断言不变。
        string canonical = MapFile.ToJson(MapCatalog.Resolve("link:1:4"));
        string lowerKeys = System.Text.RegularExpressions.Regex.Replace(
            canonical, "\"([A-Za-z0-9_]+)\":", m => $"\"{m.Groups[1].Value.ToLowerInvariant()}\":");
        Assert.NotEqual(canonical, lowerKeys);
        Assert.Contains("\"maxplayers\":", lowerKeys, StringComparison.Ordinal);

        MapData loaded = MapFile.FromJson(lowerKeys);

        Assert.Equal(canonical, MapFile.ToJson(loaded));
        Assert.True(MapValidator.Validate(loaded).IsValid, MapValidator.Validate(loaded).ToString());
    }

    [Fact]
    public void 全小写枚举值的地图能正确加载()
    {
        // PropertyNameCaseInsensitive 只管键名，枚举值走 JsonStringEnumConverter；它读取时同样不区分大小写。
        // 这里把棋盘清单的 Kind 枚举值压成小写钉住这一点，免得日后换转换器或加 JsonNamingPolicy 时静默变成"看起来能用的坏地图"。
        // relic-redesign W1：原载体是信物格的 Zone / Budget 枚举（随静态信物格删除），改为棋盘清单的 Kind，断言不变。
        string canonical = MapFile.ToJson(ExemptionCarrier());
        string lowerValues = canonical.Replace("\"Kind\": \"Birth\"", "\"Kind\": \"birth\"", StringComparison.Ordinal);
        Assert.NotEqual(canonical, lowerValues);
        Assert.DoesNotContain("\"Kind\": \"Birth\"", lowerValues, StringComparison.Ordinal);

        MapData loaded = MapFile.FromJson(lowerValues);

        Assert.Equal(canonical, MapFile.ToJson(loaded));
    }

    [Fact]
    public void 未知枚举值的地图拒绝加载()
    {
        // 大小写宽容不等于任意字符串宽容：拼错的枚举名 MUST 报错，不能落成默认值。
        // relic-redesign W1：原把信物格分区 BirthZone 写成 BirthZon（随静态信物格删除），改为把棋盘类别 Birth 写成 Birt。
        string broken = MapFile.ToJson(ExemptionCarrier())
            .Replace("\"Kind\": \"Birth\"", "\"Kind\": \"Birt\"", StringComparison.Ordinal);
        Assert.Contains("Birt\"", broken, StringComparison.Ordinal);

        Assert.ThrowsAny<System.Text.Json.JsonException>(() => MapFile.FromJson(broken));
    }

    [Fact]
    public void 含未知字段的地图能正确加载()
    {
        // 地图文件里允许写 _comment 之类的说明字段：顶层与嵌套对象都不报错、不影响数据。
        // relic-redesign W1：嵌套对象里的说明字段原挂在信物格上（随静态信物格删除），改挂在棋盘清单的出生棋盘上。
        string canonical = MapFile.ToJson(ExemptionCarrier());
        string withComments = canonical
            .Replace("{\n  \"Id\":", "{\n  \"_comment\": \"设计师备注：中央区与咽喉承担高预算\",\n  \"Id\":", StringComparison.Ordinal)
            .Replace("\"Kind\": \"Birth\",", "\"_comment\": \"出生平台\", \"Kind\": \"Birth\",", StringComparison.Ordinal);
        Assert.Contains("\"_comment\": \"出生平台\"", withComments, StringComparison.Ordinal);
        Assert.NotEqual(canonical, withComments);

        MapData loaded = MapFile.FromJson(withComments);

        Assert.Equal(canonical, MapFile.ToJson(loaded));
    }

    [Fact]
    public void 含已废弃信物格字段的地图拒绝加载()
    {
        // relic-redesign W1（D4）：静态信物格整体移除，地图文件里写 RelicCells 即响亮拒绝并点名（与据点 Sites 同一机制），MUST NOT 静默忽略。
        // 变异：MapFile.RetiredFields 去掉 RelicCells → 本测试红（字段落进 Unknown 被静默放过）。
        string json = MapFile.ToJson(MapCatalog.Resolve("link:1:4")).Replace("\"Id\":", "\"RelicCells\": {},\n  \"Id\":", StringComparison.Ordinal);
        Assert.Contains("RelicCells", json, StringComparison.Ordinal);

        FormatException ex = Assert.Throws<FormatException>(() => MapFile.FromJson(json));

        Assert.Contains("RelicCells", ex.Message, StringComparison.Ordinal);
        Assert.Contains("静态信物格", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 地形字段往返逐字段相等()
    {
        // 高度 / 地表 / 桥 / 栅栏一起写出再读入，逐字段相等；再写一次 JSON 逐字节相同。
        // A1 显式 h=0、A2 显式草地：文件只写非缺省项，TerrainData 构造期把显式缺省项归一化掉，否则这两条 Assert.Equal 恒假（check 修订）。
        TerrainData terrain = TestMaps.Terrain(
            heights: [("A1", 0), ("B2", 1), ("C3", 2), ("G7", 1)],
            surfaces: [("A2", Surface.Grass), ("D4", Surface.Forest), ("E5", Surface.DeepWater), ("E6", Surface.DeepWater), ("F2", Surface.Road)],
            bridges: ["E6"],
            fences: [("B2", "B3"), ("H8", "H9")]);
        MapData original = TestMaps.Synthetic(size: 9, maxPlayers: 4, obstacles: [TestMaps.At("A9")], terrain: terrain);

        string json = MapFile.ToJson(original);
        MapData loaded = MapFile.FromJson(json);

        Assert.Equal(3, original.TerrainData.Heights.Count);
        Assert.Equal(4, original.TerrainData.Surfaces.Count);
        Assert.Equal(original.TerrainData.Heights, loaded.TerrainData.Heights);
        Assert.Equal(original.TerrainData.Surfaces, loaded.TerrainData.Surfaces);
        Assert.Equal(original.TerrainData.Bridges, loaded.TerrainData.Bridges);
        Assert.Equal(original.TerrainData.Fences, loaded.TerrainData.Fences);
        Assert.Equal(json, MapFile.ToJson(loaded));
        Assert.Contains("\"Bridges\"", json, StringComparison.Ordinal);
        Assert.Contains("\"B2-B3\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void 缺地形字段的旧文件按平地读入()
    {
        // 没有地形字段的旧文件 → 全 h=0、全草地、无桥无栅。
        // retire-legacy-maps 1.6：原样本是 maps/siege-4p-base-v2.json（可落子 85）；maps/ 目录随旧地图删除，改用手写的最小地图文本（3×3、A1 为障碍 → 可落子 8）。
        const string text = "{\"Id\":\"old\",\"Width\":3,\"Height\":3,\"MaxPlayers\":4,\"Profile\":\"Link\",\"Obstacles\":[\"A1\"],\"BirthZones\":[],\"ChokePoints\":[],\"CentralEntrance\":\"B2\"}";
        Assert.DoesNotContain("\"Heights\"", text, StringComparison.Ordinal);

        MapData onDisk = MapFile.FromJson(text);

        Assert.Empty(onDisk.TerrainData.Heights);
        Assert.Empty(onDisk.TerrainData.Surfaces);
        Assert.Empty(onDisk.TerrainData.Bridges);
        Assert.Empty(onDisk.TerrainData.Fences);
        Assert.Equal(8, onDisk.PlayableCount);
    }

    [Theory]
    [InlineData("\"Heights\":[\"000\"]", "Heights")]
    [InlineData("\"Surfaces\":[\"GGG\",\"GGG\",\"GG\"]", "Surfaces")]
    [InlineData("\"Heights\":[\"000\",\"000\",\"003\"]", "3")]
    [InlineData("\"Surfaces\":[\"GGG\",\"GGG\",\"GGX\"]", "X")]
    [InlineData("\"Fences\":[\"A1B1\"]", "A1B1")]
    public void 畸形地形字段被指名报出(string fragment, string expectedFragment)
    {
        string json = "{\"Profile\":\"Link\",\"Id\":\"t\",\"Width\":3,\"Height\":3,\"MaxPlayers\":2,\"CentralEntrance\":\"B2\"," + fragment + "}";

        var ex = Assert.Throws<FormatException>(() => MapFile.FromJson(json));

        Assert.Contains(expectedFragment, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 新地表码往返()
    {
        // terrain-surfaces design D5：D 荒漠 / M 沼泽 / P 岩台 / S 浅滩；8 种地表全部写出再读入逐字段相等，再写一次逐字节相同。
        // 变异验证 M-S0b（实跑）：SurfaceCode 把 Crag 写成 'D' → 本测试与「非法地表码列出全部八个码」各红 1。
        TerrainData terrain = TestMaps.Terrain(
            heights: [("B2", 2)],
            surfaces:
            [
                ("A1", Surface.Road), ("B1", Surface.Forest), ("C1", Surface.DeepWater),
                ("A2", Surface.Desert), ("B2", Surface.Marsh), ("C2", Surface.Crag), ("D2", Surface.Shallows),
            ]);
        MapData original = TestMaps.Synthetic(size: 9, maxPlayers: 4, terrain: terrain);

        string json = MapFile.ToJson(original);
        MapData loaded = MapFile.FromJson(json);

        Assert.Equal(7, original.TerrainData.Surfaces.Count);
        Assert.Equal(original.TerrainData.Surfaces.OrderBy(kv => kv.Key), loaded.TerrainData.Surfaces.OrderBy(kv => kv.Key));
        Assert.Equal(json, MapFile.ToJson(loaded));
        Assert.Contains("\"DMPS", json, StringComparison.Ordinal);
    }

    [Fact]
    public void 非法地表码列出全部八个码()
    {
        string json = "{\"Profile\":\"Link\",\"Id\":\"t\",\"Width\":3,\"Height\":3,\"MaxPlayers\":2,\"CentralEntrance\":\"B2\",\"Surfaces\":[\"GGG\",\"GGG\",\"GGX\"]}";

        var ex = Assert.Throws<FormatException>(() => MapFile.FromJson(json));

        Assert.Contains("C1", ex.Message, StringComparison.Ordinal);
        foreach (string code in new[] { "G（草地）", "R（土路）", "F（林地）", "W（深水）", "D（荒漠）", "M（沼泽）", "P（岩台）", "S（浅滩）" })
        {
            Assert.Contains(code, ex.Message, StringComparison.Ordinal);
        }
    }
}
