using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>规格：map-definition —— Requirement: 人数适配预算</summary>
public class 人数适配预算Tests
{
    [Fact]
    public void 格数超出预算()
    {
        // 4 人地图的可落子格必须落在 100–115；这里放大到 12×12 使其变成 132
        MapData oversized = FourPlayerBaseMap.Create() with { Width = 12, Height = 12 };

        MapValidationResult result = MapValidator.Validate(oversized);

        MapValidationFailure failure = Assert.Single(
            result.Failures, f => f.Code == "PLAYABLE_COUNT_OUT_OF_RANGE");
        Assert.Contains("100–115", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 信物格数不足()
    {
        MapData map = FourPlayerBaseMap.Create();
        MapData stripped = map with
        {
            RelicCells = map.RelicCells
                .Where(kv => kv.Value.Zone == RelicZone.Contested)
                .ToImmutableDictionary(kv => kv.Key, kv => kv.Value),
        };

        MapValidationResult result = MapValidator.Validate(stripped);

        MapValidationFailure failure = Assert.Single(
            result.Failures, f => f.Code == "RELIC_COUNT_OUT_OF_RANGE");
        Assert.Contains("13–15", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 出生区数量必须等于最大人数()
    {
        MapData map = FourPlayerBaseMap.Create();
        MapData missingZone = map with { BirthZones = map.BirthZones.RemoveAt(3) };

        MapValidationResult result = MapValidator.Validate(missingZone);

        Assert.Contains(result.Failures, f => f.Code == "BIRTH_ZONE_COUNT_MISMATCH");
    }

    [Fact]
    public void 不支持的人数被拒绝()
    {
        MapData map = FourPlayerBaseMap.Create() with { MaxPlayers = 5 };

        Assert.Contains(MapValidator.Validate(map).Failures, f => f.Code == "UNSUPPORTED_PLAYER_COUNT");
    }
}
