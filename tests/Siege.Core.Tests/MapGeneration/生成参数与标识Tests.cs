using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.MapGeneration;

/// <summary>
/// 规格：openspec/changes/map-generator/specs/map-generation —— Requirement: 生成参数 / 生成图的地图标识；
/// specs/map-definition —— Scenario: 标准档不可生成。
/// </summary>
public class 生成参数与标识Tests
{
    [Theory]
    [InlineData(4)]
    [InlineData(9)]
    [InlineData(0)]
    [InlineData(-1)]
    public void 平台数越界报错并指出合法范围(int platforms)
    {
        // Scenario: 平台数越界——报错，MUST NOT 静默夹取。
        // 变异验证 MG-11：把 EnsureValid 的上界判断写成 `> 9` → 本测试红（平台数 9）。
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => FrontierMapGenerator.Generate(1, new MapGenParameters { PlatformCount = platforms }));
        Assert.Contains("5–8", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 缺省平台数为六()
    {
        // Scenario: 缺省平台数。
        Assert.Equal(6, MapGenParameters.Default.PlatformCount);
        Assert.Equal(6, FrontierMapGenerator.Generate(12345).BirthZones.Length);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void 平台数即出生区数(int platforms)
    {
        Assert.Equal(platforms, MapGenFixtures.Generated(3, platforms).Map.BirthZones.Length);
    }

    [Fact]
    public void 标准档不可生成()
    {
        // map-definition Scenario: 标准档不可生成。
        var ex = Assert.Throws<NotSupportedException>(
            () => FrontierMapGenerator.Generate(1, new MapGenParameters { Profile = MapProfile.Standard }));
        Assert.Contains("只有边疆档支持由地图种子生成", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("gen:42:p6", "gen:42")]
    [InlineData("gen:42", "gen:42")]
    [InlineData("gen:987654321:p7", "gen:987654321:p7")]
    [InlineData("gen:0:p5", "gen:0:p5")]
    [InlineData("gen:18446744073709551615:p8", "gen:18446744073709551615:p8")]
    [InlineData(" gen:007 ", "gen:7")]
    public void 标识规范化(string input, string expected)
    {
        // 变异验证 MG-9：Format 里"缺省平台数省略该段"的判断改成恒假 → 本测试红。
        Assert.Equal(expected, GeneratedMapId.Normalize(input));
        (ulong seed, MapGenParameters parameters) = GeneratedMapId.Parse(input);
        Assert.Equal(expected, GeneratedMapId.Format(seed, parameters));
    }

    [Fact]
    public void 标识往返()
    {
        // Scenario: 标识往返。
        Assert.Equal("gen:987654321:p7", FrontierMapGenerator.Generate("gen:987654321:p7").Id);
        MapData byId = FrontierMapGenerator.Generate("gen:42:p6");
        Assert.Equal("gen:42", byId.Id);
        Assert.Equal(MapFile.ToJson(FrontierMapGenerator.Generate(42)), MapFile.ToJson(byId));
    }

    [Theory]
    [InlineData("gen:abc")]
    [InlineData("gen:1:p99")]
    [InlineData("gen:1:p4")]
    [InlineData("gen:1:p")]
    [InlineData("gen:1:7")]
    [InlineData("gen:1:p7:x")]
    [InlineData("gen:")]
    [InlineData("gen")]
    [InlineData("gen:-1")]
    [InlineData("gen:+1")]
    [InlineData("gen:1 2")]
    [InlineData("gen:0x10")]
    [InlineData("gen:18446744073709551616")]
    [InlineData("GEN:1")]
    [InlineData("generated:1")]
    [InlineData("siege-frontier-v1")]
    [InlineData("")]
    [InlineData(null)]
    public void 非法标识报错并说明格式与范围(string? id)
    {
        // Scenario: 非法标识。裸 gen 也不是完整标识：取种子只发生在入口最外层。
        var ex = Assert.Throws<FormatException>(() => GeneratedMapId.Parse(id));
        Assert.Contains("gen:<地图种子>[:p<平台数>]", ex.Message, StringComparison.Ordinal);
        Assert.Contains("5–8", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 识别生成图标识一族与裸请求()
    {
        Assert.True(GeneratedMapId.IsGenerated("gen"));
        Assert.True(GeneratedMapId.IsGenerated("gen:1"));
        Assert.True(GeneratedMapId.IsGenerated("gen:abc"));          // 属于这一族，交给 Parse 去报格式错，而不是被当成文件路径
        Assert.False(GeneratedMapId.IsGenerated("generated"));
        Assert.False(GeneratedMapId.IsGenerated("siege-frontier-v1"));
        Assert.False(GeneratedMapId.IsGenerated(null));

        Assert.True(GeneratedMapId.IsBareRequest("gen"));
        Assert.True(GeneratedMapId.IsBareRequest(" gen "));
        Assert.False(GeneratedMapId.IsBareRequest("gen:1"));
    }
}
