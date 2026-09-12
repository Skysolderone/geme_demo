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
        // maps/siege-4p-base-v1.json 是设计师维护的那一份；它若与代码里的基准图漂移，
        // 就会出现"代码跑的图和设计师看的图不是同一张"。
        string path = Path.Combine(RepoRoot(), "maps", "siege-4p-base-v1.json");
        MapData onDisk = MapFile.FromJson(File.ReadAllText(path));

        Assert.Equal(MapFile.ToJson(FourPlayerBaseMap.Create()), MapFile.ToJson(onDisk));
        Assert.True(MapValidator.Validate(onDisk).IsValid, MapValidator.Validate(onDisk).ToString());
    }

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
}
