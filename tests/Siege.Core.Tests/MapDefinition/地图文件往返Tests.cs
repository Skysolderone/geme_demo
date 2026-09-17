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
        Assert.Equal(12, original.Sites.Count);
        Assert.Equal(original.Sites.OrderBy(kv => kv.Key), restored.Sites.OrderBy(kv => kv.Key));
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

    [Fact]
    public void 缺据点字段的旧文件读入为无据点()
    {
        // scoring-sites：v3 文件（历史存档）没有 Sites 字段 → 按无据点读入；它因此通不过规则 8，不再被默认加载。
        string path = Path.Combine(RepoRoot(), "maps", "siege-4p-base-v3.json");
        string text = File.ReadAllText(path);
        Assert.DoesNotContain("\"Sites\"", text, StringComparison.Ordinal);

        MapData v3 = MapFile.FromJson(text);

        Assert.Equal("siege-4p-base-v3", v3.Id);
        Assert.Empty(v3.Sites);
        MapValidationResult result = MapValidator.Validate(v3);
        MapValidationFailure failure = Assert.Single(result.Failures);
        Assert.Equal("SITE_COUNT_OUT_OF_RANGE", failure.Code);
        Assert.Contains("据点为 0 个", failure.Message, StringComparison.Ordinal);
        Assert.Throws<MapValidationException>(() => GameBoard.Load(v3));

        // 默认不再加载 v3：跑局默认地图与内置基准图都是 v4。
        Assert.Equal("siege-4p-base-v4", new Siege.Sim.Config.RunConfig().MapId);
        Assert.Equal("siege-4p-base-v4", FourPlayerBaseMap.Create().Id);
    }

    [Fact]
    public void 据点缺档位的文件被指名报出()
    {
        string json = MapFile.ToJson(FourPlayerBaseMap.Create())
            .Replace("\"J2\": \"Campfire\"", "\"J2\": null", StringComparison.Ordinal);
        Assert.Contains("\"J2\": null", json, StringComparison.Ordinal);

        var ex = Assert.Throws<FormatException>(() => MapFile.FromJson(json));
        Assert.Contains("J2", ex.Message, StringComparison.Ordinal);
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
