using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>规格：map-definition —— Requirement: 地图为设计师固定的静态数据（文件格式往返）</summary>
/// <remarks>retire-legacy-maps 1.6：载体由 v5（<c>FourPlayerBaseMap</c>）改为 <c>link:1</c> 初始图，断言不变（坐标样本随图改）；
/// 「v3历史文件仍可读入并通过校验」随 <c>maps/</c> 目录在第 2 组删除。</remarks>
public class 地图文件往返Tests
{
    private static MapData Link1() => MapCatalog.Resolve("link:1");

    [Fact]
    public void 往返读写无信息丢失()
    {
        MapData original = Link1() with
        {
            DistanceTolerance = 2,
            ToleranceRelaxReason = "测试用：验证理由字段随文件往返",
            PocketExemptions = [Coord.Parse("A1")],
            PocketExemptionReasons = ImmutableDictionary<Coord, string>.Empty
                .Add(Coord.Parse("A1"), "测试用：装饰性死角"),
        };

        MapData restored = MapFile.FromJson(MapFile.ToJson(original));

        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.Width, restored.Width);
        Assert.Equal(original.Height, restored.Height);
        Assert.Equal(original.MaxPlayers, restored.MaxPlayers);
        Assert.Equal(original.Obstacles, restored.Obstacles);
        Assert.Equal(original.Boards.AsEnumerable(), restored.Boards.AsEnumerable());
        Assert.Equal(original.BirthZones.Length, restored.BirthZones.Length);
        for (int i = 0; i < original.BirthZones.Length; i++)
        {
            Assert.Equal(original.BirthZones[i], restored.BirthZones[i]);
        }

        Assert.Equal(original.RelicCells.OrderBy(kv => kv.Key), restored.RelicCells.OrderBy(kv => kv.Key));
        Assert.Equal(original.ChokePoints, restored.ChokePoints);
        Assert.Equal(original.CentralEntrance, restored.CentralEntrance);
        Assert.Equal(original.DistanceTolerance, restored.DistanceTolerance);
        Assert.Equal(original.ToleranceRelaxReason, restored.ToleranceRelaxReason);
        Assert.Equal(original.MinTwoEyeArea, restored.MinTwoEyeArea);
        Assert.Equal(original.PocketExemptions, restored.PocketExemptions);
        Assert.Equal(
            original.PocketExemptionReasons.OrderBy(kv => kv.Key),
            restored.PocketExemptionReasons.OrderBy(kv => kv.Key));
    }

    [Theory]
    [InlineData("Sites")]
    [InlineData("sites")]
    [InlineData("SITES")]
    public void 含据点字段的旧地图被拒绝(string field)
    {
        // 规格 Scenario「含据点字段的旧地图被拒绝」：读入一张仍带据点字段的旧地图文件 → 拒绝加载并指出该字段已废弃，MUST NOT 静默忽略。
        // 三种大小写都要拒：MapFile 的 JsonSerializerOptions 开着 PropertyNameCaseInsensitive，写成 "sites" 同样会落进扩展字段。
        // 变异验证 M-B1（段 B，实跑红 3）：MapFile.FromJson 去掉 RejectRetiredFields 调用（退回静默忽略）→ 本测试红 3。
        string json = MapFile.ToJson(Link1())
            .Replace("  \"ChokePoints\":", $"  \"{field}\": {{{Environment.NewLine}    \"J2\": \"Campfire\"{Environment.NewLine}  }},{Environment.NewLine}  \"ChokePoints\":", StringComparison.Ordinal);
        Assert.Contains($"\"{field}\"", json, StringComparison.Ordinal);

        FormatException ex = Assert.Throws<FormatException>(() => MapFile.FromJson(json));

        Assert.Contains("Sites", ex.Message, StringComparison.Ordinal);
        Assert.Contains("废弃", ex.Message, StringComparison.Ordinal);
        Assert.Contains("据点", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 其余未知字段仍然宽容()
    {
        // 反面：废弃字段是<b>点名</b>拒绝，不是"任何未知字段都拒绝"——设计师手写的 _comment 之类照旧读得进来。
        // 没有这一条，上面那条测试在"把未知字段一律拒绝"的实现下同样会绿，挡不住过度收紧。
        string json = MapFile.ToJson(Link1())
            .Replace("  \"ChokePoints\":", $"  \"_comment\": \"设计师批注\",{Environment.NewLine}  \"ChokePoints\":", StringComparison.Ordinal);

        MapData restored = MapFile.FromJson(json);

        Assert.Equal("link:1", restored.Id);
        Assert.True(MapValidator.Validate(restored).IsValid);
    }

    [Fact]
    public void 往返后仍然通过校验()
    {
        MapData restored = MapFile.FromJson(MapFile.ToJson(Link1()));

        Assert.True(MapValidator.Validate(restored).IsValid);
    }

    [Fact]
    public void 序列化是确定性的()
    {
        MapData map = Link1();

        Assert.Equal(MapFile.ToJson(map), MapFile.ToJson(map));
        Assert.Equal(MapFile.ToJson(map), MapFile.ToJson(MapFile.FromJson(MapFile.ToJson(map))));
    }

    [Fact]
    public void 文件里用围棋记法()
    {
        string json = MapFile.ToJson(Link1());

        // link:1：U6 是中央入口，M5 是出生区信物格，J10 是 2 号出生棋盘的左下角（记法跳过 I）；三处各走不同字段。
        // retire-legacy-maps 1.6：原样本为 v3 的 G7（入口兼信物）/ B2（出生区信物）/ G4（桥）；第三处取棋盘清单的双字母列坐标。
        // link-map-terrain 第 2 组：link:1 随生成器改变（出生棋盘 6–8、带地形），三处坐标重取；再加一处桥（F8，河流主题的预置桥）。
        // open-map 1.1：link:1 改为 3+2 紧贴布局（24×18，没有双字母列），三处坐标重取（原 AA6 / P5 / AQ3），桥 F8 不变（河流棋盘仍在 C3）。
        Assert.Contains("\"CentralEntrance\": \"U6\"", json, StringComparison.Ordinal);
        Assert.Contains("\"M5\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Origin\": \"J10\"", json, StringComparison.Ordinal);
        // terrain-barriers 3.1（地图变了）：河流棋盘的河道改为横穿棋盘的第 4 行（C4–H4），预置桥由 F8 移到 G4。
        Assert.Contains("\"G4\"", json, StringComparison.Ordinal);
    }
}
