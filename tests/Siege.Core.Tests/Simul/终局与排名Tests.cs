using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;
using Siege.Core.Simul;
using static Siege.Core.Tests.Simul.SimulFixtures;

namespace Siege.Core.Tests.Simul;

/// <summary>openspec/specs/simultaneous-round —— Requirement: 终局与排名（v0.1 §10；Q07、Q08、Q10、Q11）。算例 E9 取自 design.md。</summary>
/// <remarks>变异记录（A1 实跑）：M4 占满时把活形格算成空 → 红 1（剩余空格全是活形内部格即占满）；M11 4 人局名额改 1 → 红 2；M12 关闭无部署终局判定 → 红 5。</remarks>
public sealed class 终局与排名Tests
{
    [Fact]
    public void 剩余空格全是活形内部格即占满()
    {
        // E9(a)：P0 的一条整体铺满整张图，只留 C3、E3 两只单格眼（已确定活形）。全员提交 0 枚：先判占满，不是无人部署。
        Coord c3 = Coord.Parse("C3");
        Coord e3 = Coord.Parse("E3");
        SimulFlow flow = Started(2, f => f.Debug.Place(P0, PieceType.Basic,
            [.. f.Map.AllCoords().Where(c => f.Map.IsPlayable(c) && c != c3 && c != e3)]));
        LifeShapeReport life = LifeShapeReport.Analyze(flow.Board);
        Assert.True(SimulRules.IsLocked(life, c3));
        Assert.True(SimulRules.IsLocked(life, e3));
        Assert.True(flow.Board[c3].IsPlayableEmpty);

        flow.SubmitRestEmpty();
        SimulRoundReport report = flow.ResolveRound();

        Assert.Equal(SimulEndReason.BoardFull, report.EndReason);
        Assert.Equal(SimulPhase.Ended, flow.Phase);
        Assert.Equal(SimulEndReason.BoardFull, flow.Result!.Reason);
        Assert.Equal([P0], flow.Result.Winners);
    }

    [Fact]
    public void 全员零部署即终局()
    {
        SimulFlow flow = Started(2, f => f.Debug.Stones(P1, "D4"));
        flow.SubmitRestEmpty();
        SimulRoundReport report = flow.ResolveRound();

        Assert.Equal(SimulEndReason.NoDeployRound, report.EndReason);
        Assert.Equal(SimulPhase.Ended, flow.Phase);
        Assert.Equal(1, flow.Result!.Round);
        Assert.Equal([P1], flow.Result.Winners);
        Assert.Equal([P1, P0], flow.Result.Placing);
    }

    [Fact]
    public void 有人部署就不触发无部署终局()
    {
        SimulFlow flow = Started(2);
        flow.Submit(P0, "D4");
        flow.SubmitRestEmpty();
        SimulRoundReport report = flow.ResolveRound();

        Assert.Null(report.EndReason);
        Assert.Equal(SimulPhase.InProgress, flow.Phase);
        Assert.Equal(2, flow.Round);
        Assert.Null(flow.Result);
    }

    [Fact]
    public void 达到大回合上限终局()
    {
        SimulFlow flow = Started(2, options: new SimulOptions { RoundCap = 2 });
        Assert.Equal(2, flow.RoundCap);
        flow.Submit(P0, "D4");
        flow.SubmitRestEmpty();
        Assert.Null(flow.ResolveRound().EndReason);

        flow.Submit(P0, "G7");
        flow.SubmitRestEmpty();
        SimulRoundReport report = flow.ResolveRound();
        Assert.Equal(SimulEndReason.RoundCap, report.EndReason);
        Assert.Equal(2, flow.Result!.Round);
    }

    [Fact]
    public void 四人局前两名为胜者且认输者排末位()
    {
        // E9(c)：彼此不相邻、覆盖不重叠的单子——P3 四个、P0 三个、P1 两个、P2 一个；P3 认输（势力最高），其余全员零部署终局。
        SimulFlow flow = Started(4, f =>
        {
            f.Debug.Stones(P3, "D4", "G4", "D7", "G7");
            f.Debug.Stones(P0, "P4", "S4", "P7");
            f.Debug.Stones(P1, "AA4", "AD4");
            f.Debug.Stones(P2, "AA7");
        });
        var before = flow.Power!;
        Assert.True(before.Of(P3).Total > before.Of(P0).Total);
        Assert.True(before.Of(P0).Total > before.Of(P1).Total);
        Assert.True(before.Of(P1).Total > before.Of(P2).Total);

        flow.Resign(P3);
        Assert.Equal(SimulPhase.InProgress, flow.Phase);
        flow.SubmitRestEmpty();
        flow.ResolveRound();

        SimulResult result = flow.Result!;
        Assert.Equal(SimulEndReason.NoDeployRound, result.Reason);
        Assert.Equal([P0, P1, P2, P3], result.Placing);
        Assert.Equal([P0, P1], result.Winners);
        Standing last = result.Of(P3);
        Assert.Equal(4, last.Rank);
        Assert.Equal(StandingGroup.Resigned, last.Group);
        Assert.Equal(before.Of(P3).Total, last.Input.Power);
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    public void 全员同分按种子定胜者名额(int players, int slots)
    {
        SimulFlow flow = Started(players);
        flow.SubmitRestEmpty();
        flow.ResolveRound();

        SimulResult result = flow.Result!;
        Assert.Equal(SimulEndReason.NoDeployRound, result.Reason);
        Assert.All(result.Standings, s => Assert.Equal(1, s.Rank));
        PlayerId[] bySeed = [.. PlayersOf(players).OrderBy(flow.Debug.SeedRankOf)];
        Assert.Equal(bySeed, result.Placing);
        Assert.Equal(slots, result.Winners.Length);
        Assert.Equal(bySeed.Take(slots), result.Winners);
        Assert.Equal(slots, SimulRules.WinnerSlots(players));
    }
}
