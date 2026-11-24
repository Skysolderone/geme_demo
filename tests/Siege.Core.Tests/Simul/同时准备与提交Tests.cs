using System.Reflection;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Recruit;
using Siege.Core.Simul;
using static Siege.Core.Tests.Simul.SimulFixtures;

namespace Siege.Core.Tests.Simul;

/// <summary>openspec/specs/simultaneous-round —— Requirement: 同时准备与提交（v0.1 §6、§8；Q09）。</summary>
public sealed class 同时准备与提交Tests
{
    [Fact]
    public void 全员提交后才能揭晓()
    {
        SimulFlow flow = Started(2);
        flow.Submit(P0, "D4");
        Assert.False(flow.AllSubmitted);
        Assert.Throws<SiegeRuleException>(() => flow.ResolveRound());

        flow.Submit(P1, "J9");
        Assert.True(flow.AllSubmitted);
        SimulRoundReport report = flow.ResolveRound();
        Assert.Equal(1, report.Round);
        Assert.Equal(P0, flow.OwnerAt("D4"));
        Assert.Equal(P1, flow.OwnerAt("J9"));
    }

    [Fact]
    public void 提交后锁定撤回后可改()
    {
        SimulFlow flow = Started(2);
        SimulPrep prep = flow.PrepFor(P0);
        prep.Stage(Unit("D4"));
        Assert.True(prep.Submit().IsValid);
        Assert.True(prep.IsSubmitted);

        Assert.Throws<SiegeRuleException>(() => prep.Stage(Unit("F6")));
        Assert.Throws<SiegeRuleException>(() => prep.Unstage(Coord.Parse("D4")));
        Assert.Throws<SiegeRuleException>(() => prep.EnterRecruit());
        Assert.Throws<SiegeRuleException>(() => prep.Discard(PieceType.Basic));

        prep.Withdraw();
        Assert.False(prep.IsSubmitted);
        prep.Stage(Unit("F6"));
        RecruitPanelView panel = prep.EnterRecruit();
        Assert.Equal(EffectSnapshot.BaseRevealCount, panel.Candidates.Length);
        Assert.Equal(["D4", "F6"], prep.Placements.Select(p => p.Coord.ToNotation()));
        Assert.True(prep.Submit().IsValid);
    }

    [Fact]
    public void 非法批次不能提交也不消耗手牌()
    {
        SimulFlow flow = Started(2);
        int before = flow.HandCount(P0);
        SimulPrep prep = flow.PrepFor(P0);
        prep.Stage(Unit("L5"));   // 公共平台：第 1 回合不在可部署范围内
        SimulBatchCheck check = prep.Submit();

        Assert.False(check.IsValid);
        Assert.True(check.Has(SimulRejectKind.OutOfRange));
        Assert.Contains("出生岛", check.FirstReason!, StringComparison.Ordinal);
        Assert.False(prep.IsSubmitted);
        Assert.Equal(before, flow.HandCount(P0));
    }

    [Fact]
    public void 公开视图只显示提交状态()
    {
        SimulFlow flow = Started(2);
        flow.Submit(P0, "D4");
        flow.PrepFor(P1).Stage(Unit("J9"));
        SimulPublicView view = flow.PublicView();

        Assert.True(view.Seats.Single(s => s.Player == P0).Submitted);
        Assert.False(view.Seats.Single(s => s.Player == P1).Submitted);
        Assert.Null(view.Board[Coord.Parse("D4")].Occupant);
        Assert.Null(view.Board[Coord.Parse("J9")].Occupant);

        // 结构上没有暂放：公开视图与座位视图的属性里不出现暂放 / 批次类型。
        Type[] forbidden = [typeof(Placement), typeof(SimulPrep), typeof(IEnumerable<Placement>), typeof(System.Collections.Immutable.ImmutableArray<Placement>)];
        foreach (Type t in new[] { typeof(SimulPublicView), typeof(SimulSeatView) })
        {
            foreach (PropertyInfo p in t.GetProperties())
            {
                Assert.DoesNotContain(p.PropertyType, forbidden);
            }
        }
    }
}
