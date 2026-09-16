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
        // 规格 Scenario：4 人地图的可落子格为 130 → 拒绝并报告超出 80–95（denser-map 裁决 5）。
        // 12×12 = 144 外接，只留 14 格障碍 → 可落子恰好 130，与规格算例逐字对上。
        MapData baseMap = FourPlayerBaseMap.Create();
        MapData oversized = baseMap with
        {
            Width = 12,
            Height = 12,
            Obstacles = [.. baseMap.Obstacles.Order().Take(14)],
        };

        MapValidationResult result = MapValidator.Validate(oversized);

        MapValidationFailure failure = Assert.Single(
            result.Failures, f => f.Code == "PLAYABLE_COUNT_OUT_OF_RANGE");
        Assert.Contains("可落子格为 130", failure.Message, StringComparison.Ordinal);
        Assert.Contains("80–95", failure.Message, StringComparison.Ordinal);
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
        Assert.Contains("少于下限 13", failure.Message, StringComparison.Ordinal);
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
    public void 两人预算区间()
    {
        // 设计文档 §3.2：2 人 → 可落子 50–65、出生区 2、信物 7–9
        // 规格 Scenario「信物格数不足」：2 人地图只有 5 个信物格 → 报告少于 7
        MapData twoPlayer = TestMaps.Synthetic(size: 8, maxPlayers: 2, relics: FiveContestedRelics());

        MapValidationResult result = MapValidator.Validate(twoPlayer);

        Assert.DoesNotContain(result.Failures, f => f.Code == "PLAYABLE_COUNT_OUT_OF_RANGE"); // 64 ∈ 50–65
        MapValidationFailure relics = Assert.Single(result.Failures, f => f.Code == "RELIC_COUNT_OUT_OF_RANGE");
        Assert.Contains("信物格为 5", relics.Message, StringComparison.Ordinal);
        Assert.Contains("少于下限 7", relics.Message, StringComparison.Ordinal);
        Assert.Contains("7–9", relics.Message, StringComparison.Ordinal);

        // 49 格低于下限 50
        MapValidationFailure tooSmall = Assert.Single(
            MapValidator.Validate(TestMaps.Synthetic(size: 7, maxPlayers: 2)).Failures,
            f => f.Code == "PLAYABLE_COUNT_OUT_OF_RANGE");
        Assert.Contains("50–65", tooSmall.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 三人预算区间()
    {
        // 设计文档 §3.2：3 人 → 可落子 75–90、出生区 3、信物 10–12
        MapData threePlayer = TestMaps.Synthetic(size: 10, maxPlayers: 3);

        MapValidationResult result = MapValidator.Validate(threePlayer);

        MapValidationFailure playable = Assert.Single(result.Failures, f => f.Code == "PLAYABLE_COUNT_OUT_OF_RANGE");
        Assert.Contains("可落子格为 100", playable.Message, StringComparison.Ordinal);
        Assert.Contains("75–90", playable.Message, StringComparison.Ordinal);

        MapValidationFailure relics = Assert.Single(result.Failures, f => f.Code == "RELIC_COUNT_OUT_OF_RANGE");
        Assert.Contains("10–12", relics.Message, StringComparison.Ordinal);

        // 9×9 = 81 ∈ 75–90
        Assert.DoesNotContain(
            MapValidator.Validate(TestMaps.Synthetic(size: 9, maxPlayers: 3)).Failures,
            f => f.Code == "PLAYABLE_COUNT_OUT_OF_RANGE");
    }

    [Fact]
    public void 不支持的人数被拒绝()
    {
        MapData map = FourPlayerBaseMap.Create() with { MaxPlayers = 5 };

        Assert.Contains(MapValidator.Validate(map).Failures, f => f.Code == "UNSUPPORTED_PLAYER_COUNT");
    }

    private static KeyValuePair<Coord, RelicCellSpec>[] FiveContestedRelics() =>
        [.. new[] { "B2", "C3", "D4", "E5", "F6" }.Select(n => KeyValuePair.Create(
            Coord.Parse(n), new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard)))];
}
