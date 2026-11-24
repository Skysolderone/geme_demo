using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Simul;
using static Siege.Core.Tests.Simul.SimulFixtures;

namespace Siege.Core.Tests.Simul;

/// <summary>
/// openspec/specs/simultaneous-round —— Requirement: 建局与插旗（Q16、K4）。走生产入口：<see cref="SimulFlow.Create"/>（地图经校验）+ <see cref="SimulFlow.PlantPrototype"/>（flag-pick 盲选）。
/// </summary>
public sealed class 建局与插旗Tests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void 链接小图可以开局并跑完一回合(int players)
    {
        MapData map = MapCatalog.Resolve($"link:1:{players}");
        SimulFlow flow = SimulFlow.Create(map, Seed, PlayersOf(players));
        Assert.Equal(SimulPhase.FlagPlanting, flow.Phase);
        flow.PlantPrototype();

        Assert.Equal(SimulPhase.InProgress, flow.Phase);
        Assert.Equal(1, flow.Round);
        Assert.Equal(players, flow.Order.Length);
        Assert.Equal(players, flow.Players.Select(p => flow.BirthZoneOf(p)).Distinct().Count());

        foreach (PlayerId p in flow.Order)
        {
            SimulPrep prep = flow.PrepFor(p);
            Coord cell = map.BirthBoard(flow.BirthZoneOf(p)!.Value).Cells().First(c =>
            {
                prep.Clear();
                prep.Stage(c, PieceType.Basic);
                return prep.Check().IsValid;
            });
            Assert.True(prep.Submit().IsValid, $"{p} 在 {cell.ToNotation()} 的部署应当合法");
        }

        SimulRoundReport report = flow.ResolveRound();
        Assert.Null(report.EndReason);
        Assert.Equal(2, flow.Round);
        Assert.Equal(players, report.Landed.Length);
        Assert.All(flow.Players, p => Assert.Single(flow.Board.GroupsOf(p)));
    }

    [Fact]
    public void 大图与人数不符拒绝建局()
    {
        MapData small = MapCatalog.Resolve("link:1:4");
        Assert.Throws<ArgumentException>(() => SimulFlow.Create(small, Seed, PlayersOf(2)));

        MapData large = MapCatalog.Resolve("link:1:4:l");
        Assert.Equal(MapTier.Large, large.Tier);
        ArgumentException e = Assert.Throws<ArgumentException>(() => SimulFlow.Create(large, Seed, PlayersOf(4)));
        Assert.Contains("小图", e.Message, StringComparison.Ordinal);
    }
}
