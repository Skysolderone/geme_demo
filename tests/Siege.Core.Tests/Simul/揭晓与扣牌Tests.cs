using Siege.Core.Board;
using Siege.Core.Recruit;
using Siege.Core.Simul;
using static Siege.Core.Tests.Simul.SimulFixtures;

namespace Siege.Core.Tests.Simul;

/// <summary>openspec/specs/simultaneous-round —— Requirement: 揭晓与扣牌（v0.1 §8.2；G2）。</summary>
/// <remarks>变异记录（A1 实跑）：M8 零部署改走现有 OnPass（撤销征召）→ 红 1（零部署不撤销本回合征召）。</remarks>
public sealed class 揭晓与扣牌Tests
{
    [Fact]
    public void 冲突落败照样扣牌()
    {
        // E1 局面：P1 在 E5 落败。
        SimulFlow flow = 同格冲突Tests.E1();
        SimulRoundReport report = flow.ResolveRound();

        Assert.Equal(P0, Assert.Single(report.Conflicts).Winner);
        Assert.Equal(P0, flow.OwnerAt("E5"));
        Assert.DoesNotContain(report.Landed, l => l.Player == P1);
        Assert.Equal(19, flow.HandCount(P1));
        Assert.Equal(19, flow.HandCount(P0));
        Assert.Equal([P0, P1], report.Batches.Select(b => b.Player));
        Assert.All(report.Batches, b => Assert.Equal(["E5"], b.Placements.Select(p => p.Coord.ToNotation())));
    }

    [Fact]
    public void 零部署不撤销本回合征召()
    {
        SimulFlow flow = Started(2);
        SimulPrep prep = flow.PrepFor(P0);
        RecruitPanelView panel = prep.EnterRecruit();
        PieceType picked = panel.Candidates[0].Type;
        int before = flow.Hands.PrivateViewOf(P0).CountOf(picked);
        prep.Pick(0);
        Assert.True(prep.Submit().IsValid);
        flow.Submit(P1, "J9");

        SimulRoundReport report = flow.ResolveRound();
        Assert.Null(report.EndReason);
        Assert.Empty(report.Batches.Single(b => b.Player == P0).Placements);
        HandPrivateView hand = flow.Hands.PrivateViewOf(P0);
        Assert.Equal(before + 1, hand.CountOf(picked));
        Assert.Equal(0, hand.PendingGained);   // 已折进基数
        Assert.Equal(21, hand.TotalCount);
    }
}
