using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>规格：map-definition —— Requirement: 地图静态校验规则</summary>
public class 地图静态校验规则Tests
{
    [Fact]
    public void 障碍占比越界()
    {
        MapData sparse = FourPlayerBaseMap.Create() with
        {
            Obstacles = [Coord.Parse("A6"), Coord.Parse("L6"), Coord.Parse("F1"), Coord.Parse("F11")],
        };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(sparse).Failures, f => f.Code == "OBSTACLE_RATIO_TOO_LOW");
        Assert.Contains("低于 8%", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 出生区距离失衡()
    {
        // 把咽喉标注收窄到只剩左下角一处，四个出生区到"最近咽喉"的距离立刻拉开
        MapData lopsided = FourPlayerBaseMap.Create() with { ChokePoints = [Coord.Parse("C6")] };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(lopsided).Failures, f => f.Code == "DISTANCE_IMBALANCE");
        Assert.Contains("出生区 0 =", failure.Message, StringComparison.Ordinal);
        Assert.Contains("超出容差 1", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 必死口袋()
    {
        // 用障碍把出生区左下角封出一块 6 格死角（小于两眼所需的 8 格）
        MapData map = FourPlayerBaseMap.Create();
        MapData pocketed = map with
        {
            Obstacles = map.Obstacles.Union([
                Coord.Parse("C1"), Coord.Parse("C2"), Coord.Parse("C3"),
                Coord.Parse("A4"), Coord.Parse("B4")]),
        };

        MapValidationFailure failure = Assert.Single(
            MapValidator.Validate(pocketed).Failures, f => f.Code == "DEAD_POCKET");
        Assert.Contains("小于形成两眼所需的 8 格", failure.Message, StringComparison.Ordinal);
        Assert.Equal(["A1", "B1", "A2", "B2", "A3", "B3"], failure.Coords.Notations());
    }

    [Fact]
    public void 必死口袋可显式豁免()
    {
        MapData map = FourPlayerBaseMap.Create();
        var walls = new[]
        {
            Coord.Parse("C1"), Coord.Parse("C2"), Coord.Parse("C3"),
            Coord.Parse("A4"), Coord.Parse("B4"),
        };
        MapData exempted = map with
        {
            Obstacles = map.Obstacles.Union(walls),
            PocketExemptions = [Coord.Parse("A1")],
            PocketExemptionReasons = ImmutableDictionary<Coord, string>.Empty
                .Add(Coord.Parse("A1"), "测试用：刻意保留的装饰性死角"),
        };

        Assert.DoesNotContain(MapValidator.Validate(exempted).Failures, f => f.Code == "DEAD_POCKET");
    }

    [Fact]
    public void 豁免必须写明理由()
    {
        MapData map = FourPlayerBaseMap.Create() with { PocketExemptions = [Coord.Parse("A1")] };

        Assert.Contains(
            MapValidator.Validate(map).Failures, f => f.Code == "POCKET_EXEMPTION_WITHOUT_REASON");
    }

    [Fact]
    public void 信物格必须位于可落子格()
    {
        MapData map = FourPlayerBaseMap.Create();
        MapData broken = map with { Obstacles = map.Obstacles.Add(Coord.Parse("D4")) };

        Assert.Contains(
            MapValidator.Validate(broken).Failures,
            f => f.Code == "RELIC_ON_NON_PLAYABLE" && f.Coords.Contains(Coord.Parse("D4")));
    }

    [Fact]
    public void 咽喉必须显式标注()
    {
        MapData map = FourPlayerBaseMap.Create() with { ChokePoints = [] };

        Assert.Contains(MapValidator.Validate(map).Failures, f => f.Code == "CHOKE_NOT_ANNOTATED");
    }

    [Fact]
    public void 放宽容差必须写明理由()
    {
        MapData map = FourPlayerBaseMap.Create();

        Assert.Contains(
            MapValidator.Validate(map with { DistanceTolerance = 3 }).Failures,
            f => f.Code == "TOLERANCE_RELAX_WITHOUT_REASON");
        Assert.DoesNotContain(
            MapValidator.Validate(map with
            {
                DistanceTolerance = 3,
                ToleranceRelaxReason = "3 人图不套方形对称，按 §3.2 逐图放宽",
            }).Failures,
            f => f.Code == "TOLERANCE_RELAX_WITHOUT_REASON");
    }

    [Fact]
    public void 校验不通过则拒绝加载()
    {
        MapData map = FourPlayerBaseMap.Create() with { ChokePoints = [] };

        MapValidationException ex = Assert.Throws<MapValidationException>(() => GameBoard.Load(map));

        Assert.Contains(ex.Result.Failures, f => f.Code == "CHOKE_NOT_ANNOTATED");
    }
}
