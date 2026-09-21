using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>规格：map-definition —— Requirement: 地图为设计师固定的静态数据（文件格式往返）</summary>
public class 地图文件往返Tests
{
    [Fact]
    public void 往返读写无信息丢失()
    {
        MapData original = FourPlayerBaseMap.Create() with
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
        string json = MapFile.ToJson(FourPlayerBaseMap.Create())
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
        string json = MapFile.ToJson(FourPlayerBaseMap.Create())
            .Replace("  \"ChokePoints\":", $"  \"_comment\": \"设计师批注\",{Environment.NewLine}  \"ChokePoints\":", StringComparison.Ordinal);

        MapData restored = MapFile.FromJson(json);

        Assert.Equal("siege-4p-base-v5", restored.Id);
        Assert.True(MapValidator.Validate(restored).IsValid);
    }

    [Fact]
    public void v3历史文件仍可读入并通过校验()
    {
        // maps/siege-4p-base-v3.json 是历史存档，没有 Sites 字段（v4 才加的）。据点校验取消后它不再因规则 8 被拒——
        // 它与 v5 的差别只剩 Id。本条同时钉住"缺省地图是 v5"。
        string path = Path.Combine(RepoRoot(), "maps", "siege-4p-base-v3.json");
        string text = File.ReadAllText(path);
        Assert.DoesNotContain("\"Sites\"", text, StringComparison.Ordinal);

        MapData v3 = MapFile.FromJson(text);

        Assert.Equal("siege-4p-base-v3", v3.Id);
        Assert.True(MapValidator.Validate(v3).IsValid);
        Assert.Equal("siege-4p-base-v5", new Siege.Sim.Config.RunConfig().MapId);
        Assert.Equal("siege-4p-base-v5", FourPlayerBaseMap.Create().Id);
    }

    [Fact]
    public void 往返后仍然通过校验()
    {
        MapData restored = MapFile.FromJson(MapFile.ToJson(FourPlayerBaseMap.Create()));

        Assert.True(MapValidator.Validate(restored).IsValid);
    }

    [Fact]
    public void 序列化是确定性的()
    {
        MapData map = FourPlayerBaseMap.Create();

        Assert.Equal(MapFile.ToJson(map), MapFile.ToJson(map));
        Assert.Equal(MapFile.ToJson(map), MapFile.ToJson(MapFile.FromJson(MapFile.ToJson(map))));
    }

    [Fact]
    public void 文件里用围棋记法()
    {
        string json = MapFile.ToJson(FourPlayerBaseMap.Create());

        // v3：G7 是中央入口兼信物格，B2 是出生区信物格，G4 是桥；三处各走不同字段。
        Assert.Contains("\"G7\"", json, StringComparison.Ordinal);
        Assert.Contains("\"B2\"", json, StringComparison.Ordinal);
        Assert.Contains("\"G4\"", json, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "siege.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("找不到仓库根目录（siege.sln）。");
    }
}
