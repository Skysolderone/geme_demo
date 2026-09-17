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
    [Theory]
    [InlineData("null", "不是合法 JSON")]
    [InlineData("{}", "无法解析为围棋记法坐标")]                              // 缺 CentralEntrance
    [InlineData("{\"CentralEntrance\":\"I5\"}", "\"I5\"")]                    // I 不是合法列字母
    [InlineData("{\"CentralEntrance\":\"A0\"}", "\"A0\"")]                    // 行号自 1 起
    [InlineData("{\"CentralEntrance\":\"ZZ99\"}", "\"ZZ99\"")]
    [InlineData("{\"CentralEntrance\":\"F6\",\"Obstacles\":[null]}", "<null>")]
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
        string json = $"{{\"CentralEntrance\":\"F6\",\"{field}\":null}}";

        FormatException ex = Assert.Throws<FormatException>(() => MapFile.FromJson(json));

        Assert.Contains(field, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"CentralEntrance\":\"F6\",\"Width\":\"eleven\"}")]
    [InlineData("{\"CentralEntrance\":\"F6\",\"RelicCells\":{\"D4\":{\"Zone\":\"Mystery\",\"Budget\":\"High\"}}}")]
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
        MapData degenerate = MapFile.FromJson("{\"CentralEntrance\":\"F6\",\"MaxPlayers\":4}");

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
        // 这里钉住：折叠后信物格数量变了，人数适配预算会报出来——不会带着"丢了一个信物格"的地图开局。
        MapData folded = MapFile.FromJson(
            "{\"CentralEntrance\":\"F6\",\"Width\":11,\"Height\":11,\"MaxPlayers\":4,"
            + "\"RelicCells\":{\"D4\":{\"Zone\":\"Contested\",\"Budget\":\"High\"},"
            + "\"d4\":{\"Zone\":\"Contested\",\"Budget\":\"High\"}}}");

        Assert.Single(folded.RelicCells);
        Assert.Contains(
            MapValidator.Validate(folded).Failures, f => f.Code == "RELIC_COUNT_OUT_OF_RANGE");
    }

    [Fact]
    public void 磁盘上的基准地图文件与代码一致()
    {
        // maps/siege-4p-base-v4.json（文件名随 Id）是设计师维护的那一份；它若与代码里的基准图漂移，
        // 就会出现"代码跑的图和设计师看的图不是同一张"。
        // 文件名跟着地图 Id 走（denser-map 裁决 3），旧的 v1 / v2 / v3 留在 maps/ 只作对照，不参与本断言。
        string path = Path.Combine(RepoRoot(), "maps", $"{FourPlayerBaseMap.Create().Id}.json");
        MapData onDisk = MapFile.FromJson(File.ReadAllText(path));

        Assert.Equal(MapFile.ToJson(FourPlayerBaseMap.Create()), MapFile.ToJson(onDisk));
        Assert.True(MapValidator.Validate(onDisk).IsValid, MapValidator.Validate(onDisk).ToString());
    }

    [Fact]
    public void 全小写键名的地图能正确加载()
    {
        // 设计师手写的键名不必与 DTO 的大小写一致。把基准图的全部 JSON 键名压成小写，
        // 往返后 MUST 与原图逐字节一致——包括嵌套的 zone / budget 与豁免理由。
        string canonical = MapFile.ToJson(FourPlayerBaseMap.Create());
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
        // PropertyNameCaseInsensitive 只管键名，枚举值走 JsonStringEnumConverter；
        // 它读取时同样不区分大小写。这里把 Zone / Budget 的全部枚举值压成小写钉住这一点，
        // 免得日后换转换器或加 JsonNamingPolicy 时静默变成"看起来能用的坏地图"。
        string canonical = MapFile.ToJson(FourPlayerBaseMap.Create());
        string lowerValues = canonical
            .Replace("\"Zone\": \"BirthZone\"", "\"Zone\": \"birthzone\"", StringComparison.Ordinal)
            .Replace("\"Zone\": \"Contested\"", "\"Zone\": \"contested\"", StringComparison.Ordinal)
            .Replace("\"Budget\": \"Birth\"", "\"Budget\": \"birth\"", StringComparison.Ordinal)
            .Replace("\"Budget\": \"Standard\"", "\"Budget\": \"standard\"", StringComparison.Ordinal)
            .Replace("\"Budget\": \"High\"", "\"Budget\": \"high\"", StringComparison.Ordinal);
        Assert.NotEqual(canonical, lowerValues);
        Assert.DoesNotContain("\"BirthZone\"", lowerValues, StringComparison.Ordinal);
        Assert.DoesNotContain("\"High\"", lowerValues, StringComparison.Ordinal);

        MapData loaded = MapFile.FromJson(lowerValues);

        Assert.Equal(canonical, MapFile.ToJson(loaded));
    }

    [Fact]
    public void 未知枚举值的地图拒绝加载()
    {
        // 大小写宽容不等于任意字符串宽容：拼错的分区名 MUST 报错，不能落成默认值 BirthZone。
        string broken = MapFile.ToJson(FourPlayerBaseMap.Create())
            .Replace("\"Zone\": \"Contested\"", "\"Zone\": \"Contest\"", StringComparison.Ordinal);

        Assert.ThrowsAny<System.Text.Json.JsonException>(() => MapFile.FromJson(broken));
    }

    [Fact]
    public void 含未知字段的地图能正确加载()
    {
        // 地图文件里允许写 _comment 之类的说明字段：顶层与嵌套对象都不报错、不影响数据。
        string canonical = MapFile.ToJson(FourPlayerBaseMap.Create());
        string withComments = canonical
            .Replace("{\n  \"Id\":", "{\n  \"_comment\": \"设计师备注：中央区与咽喉承担高预算\",\n  \"Id\":", StringComparison.Ordinal)
            .Replace("\"Zone\": \"Contested\",", "\"_comment\": \"公共区\",\n      \"Zone\": \"Contested\",", StringComparison.Ordinal);
        Assert.NotEqual(canonical, withComments);
        Assert.Contains("_comment", withComments, StringComparison.Ordinal);

        MapData loaded = MapFile.FromJson(withComments);

        Assert.Equal(canonical, MapFile.ToJson(loaded));
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
        // v2 文件（历史存档，已不能通过校验）没有地形字段 → 全 h=0、全草地、无桥无栅；可落子格仍是 85
        string path = Path.Combine(RepoRoot(), "maps", "siege-4p-base-v2.json");
        string text = File.ReadAllText(path);
        Assert.DoesNotContain("\"Heights\"", text, StringComparison.Ordinal);

        MapData onDisk = MapFile.FromJson(text);

        Assert.Empty(onDisk.TerrainData.Heights);
        Assert.Empty(onDisk.TerrainData.Surfaces);
        Assert.Empty(onDisk.TerrainData.Bridges);
        Assert.Empty(onDisk.TerrainData.Fences);
        Assert.Equal(85, onDisk.PlayableCount);
    }

    [Theory]
    [InlineData("\"Heights\":[\"000\"]", "Heights")]
    [InlineData("\"Surfaces\":[\"GGG\",\"GGG\",\"GG\"]", "Surfaces")]
    [InlineData("\"Heights\":[\"000\",\"000\",\"003\"]", "3")]
    [InlineData("\"Surfaces\":[\"GGG\",\"GGG\",\"GGX\"]", "X")]
    [InlineData("\"Fences\":[\"A1B1\"]", "A1B1")]
    public void 畸形地形字段被指名报出(string fragment, string expectedFragment)
    {
        string json = "{\"Id\":\"t\",\"Width\":3,\"Height\":3,\"MaxPlayers\":2,\"CentralEntrance\":\"B2\"," + fragment + "}";

        var ex = Assert.Throws<FormatException>(() => MapFile.FromJson(json));

        Assert.Contains(expectedFragment, ex.Message, StringComparison.Ordinal);
    }

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
}
