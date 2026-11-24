using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Core.Scoring;
using Siege.Core.Simul;
using static Siege.Core.Tests.Simul.SimulFixtures;

namespace Siege.Core.Tests.Simul;

/// <summary>openspec/specs/simultaneous-round —— Requirement: 全灭不判负与认输（v0.1 §10.1；Q11、G2）。算例 E8 取自 design.md。</summary>
public sealed class 全灭不判负与认输Tests
{
    [Fact]
    public void 全灭不判负()
    {
        // E8：P0 唯一的单位 C3（角上，气 D3、C4），P1 已占 D3、本回合堵 C4 → C3 被 P1 的顺位提走，P0 场上没有单位。
        SimulFlow flow = Started(2, f =>
        {
            f.Debug.Stones(P0, "C3");
            f.Debug.Stones(P1, "D3");
        }, order: [P0, P1]);
        flow.SubmitRestEmpty();
        flow.PrepFor(P1).Withdraw();
        flow.PrepFor(P1).Stage(Unit("C4"));
        Assert.True(flow.PrepFor(P1).Submit().IsValid);
        SimulRoundReport first = flow.ResolveRound();

        Assert.Equal(["C3"], first.Removed.Select(s => s.Coord.ToNotation()));
        Assert.Empty(flow.Board.GroupsOf(P0));
        Assert.Null(first.EndReason);
        Assert.Equal(PlayerStatus.Active, flow.StatusOf(P0));
        Assert.Equal(PlayerStatus.Active, first.Power.Of(P0).Status);
        Assert.Contains(P0, flow.Order);

        // 下一回合照常征召、部署并落下。
        SimulPrep prep = flow.PrepFor(P0);
        RecruitPanelView panel = prep.EnterRecruit();
        Assert.Equal(EffectSnapshot.BaseRevealCount, panel.Candidates.Length);
        prep.Stage(Unit("F6"));
        Assert.True(prep.Submit().IsValid);
        flow.SubmitRestEmpty();
        flow.ResolveRound();
        Assert.Equal(P0, flow.OwnerAt("F6"));
        Assert.All(flow.Players, p => Assert.Equal(PlayerStatus.Active, flow.StatusOf(p)));
    }

    [Fact]
    public void 认输者的暂放作废不扣牌()
    {
        SimulFlow flow = Started(3, f => f.Debug.Stones(P1, "J9"));
        flow.Submit(P1, "D4");
        flow.Resign(P1);

        Assert.Equal(PlayerStatus.Resigned, flow.StatusOf(P1));
        Assert.Equal(20, flow.HandCount(P1));
        Assert.DoesNotContain(P1, flow.Order);
        Assert.Throws<SiegeRuleException>(() => flow.PrepFor(P1));

        flow.Submit(P0, "G4");
        flow.SubmitRestEmpty();
        SimulRoundReport report = flow.ResolveRound();
        Assert.DoesNotContain(report.Batches, b => b.Player == P1);
        Assert.Null(flow.OwnerAt("D4"));
        Assert.Equal(P1, flow.OwnerAt("J9"));
        Assert.Equal(20, flow.HandCount(P1));
        Assert.DoesNotContain(P1, flow.Order);
    }

    [Fact]
    public void 认输者的单位可被围杀()
    {
        SimulFlow flow = Started(3, f =>
        {
            f.Debug.Stones(P1, "C3");
            f.Debug.Stones(P0, "D3");
        }, order: [P0, P1, P2]);
        flow.Resign(P1);
        flow.Submit(P0, "C4");
        flow.SubmitRestEmpty();
        SimulRoundReport report = flow.ResolveRound();

        Assert.Equal([P0, P2], report.Order);
        Assert.Equal(P0, report.Captures[0].By);
        Assert.Equal(["C3"], report.Captures[0].Removed.Select(s => s.Coord.ToNotation()));
        Assert.Equal(P1, report.Captures[0].Removed[0].Owner);
        Assert.Null(flow.OwnerAt("C3"));
    }

    [Fact]
    public void 认输后只剩一人直接终局()
    {
        SimulFlow flow = Started(2);
        flow.Submit(P0, "D4");
        flow.Resign(P1);

        Assert.Equal(SimulPhase.Ended, flow.Phase);
        SimulResult result = flow.Result!;
        Assert.Equal(SimulEndReason.LastPlayerStanding, result.Reason);
        Assert.Equal([P0], result.Winners);
        Assert.Equal(StandingGroup.Resigned, result.Of(P1).Group);
        Assert.Equal(2, result.Of(P1).Rank);
        Assert.Throws<SiegeRuleException>(() => flow.ResolveRound());
    }
}
