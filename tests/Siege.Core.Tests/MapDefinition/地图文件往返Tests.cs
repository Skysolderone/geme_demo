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

        Assert.Contains("\"F6\"", json, StringComparison.Ordinal);
        Assert.Contains("\"A6\"", json, StringComparison.Ordinal);
    }
}
