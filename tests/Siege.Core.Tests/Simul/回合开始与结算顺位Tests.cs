using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Simul;
using static Siege.Core.Tests.Simul.SimulFixtures;

namespace Siege.Core.Tests.Simul;

/// <summary>openspec/specs/simultaneous-round —— Requirement: 回合开始与结算顺位（v0.1 §6、§7.1；Q05）。</summary>
/// <remarks>变异记录（A1 实跑）：M13 第 1 回合也看上回合顺位（Round ≥ 1）→ 红 2（本类两条同势力用例）；M14 从不看上回合顺位（Round ≥ 99）→ 红 1（第二回合起同势力上回合更晚者先）。</remarks>
public sealed class 回合开始与结算顺位Tests
{
    [Fact]
    public void 首回合顺位由种子决定()
    {
        SimulFlow a = Started(4);
        SimulFlow b = Started(4);
        Assert.Equal(a.Order, b.Order);
        Assert.Equal(Four.Order(), a.Order.Order());
        Assert.Equal(1, a.Round);
    }

    [Fact]
    public void 之后按上回合势力排名()
    {
        // P2 三个分散单位、P1 两个、P0 零个（互不相邻、覆盖不重叠）：势力 P2 > P1 > P0。P0 第 1 回合在出生岛中央部署 1 个（与前述覆盖不重叠）。
        SimulFlow flow = Started(3, f =>
        {
            f.Debug.Stones(P2, "D4", "H4", "D8");
            f.Debug.Stones(P1, "H8", "S4");
        }, order: [P0, P1, P2]);
        flow.Submit(P0, "F6");
        flow.SubmitRestEmpty();
        SimulRoundReport report = flow.ResolveRound();

        Assert.Null(report.EndReason);
        Assert.True(report.Power.Of(P2).Total > report.Power.Of(P1).Total);
        Assert.True(report.Power.Of(P1).Total > report.Power.Of(P0).Total);
        Assert.Equal([P2, P1, P0], flow.Order);
    }

    /// <summary>
    /// 3 人局，第 1 回合顺位取种子兜底顺序本身（不是编号顺序），三人各在出生岛 0 部署一个覆盖互不重叠的单子 → 势力相同。
    /// 第 1 回合的顺位是随机的、不算"上回合顺位"（现有 <c>InitiativeOrder</c> 口径），所以第 2 回合仍按种子兜底；
    /// 若错用"上回合更晚者先"会得到它的倒序，若按编号会得到 P0、P1、P2。
    /// </summary>
    /// <summary>种子兜底顺序为 P1、P2、P0 的对局种子（既不是编号顺序，也不是它的倒序）。</summary>
    private static readonly GameSeed TieSeed = new(1);

    private static (SimulFlow Flow, PlayerId[] BySeed) TiedFirstRound()
    {
        SimulFlow probe = Started(3, seed: TieSeed);
        PlayerId[] bySeed = [.. PlayersOf(3).OrderBy(probe.Debug.SeedRankOf)];
        Assert.NotEqual(PlayersOf(3), bySeed);   // 夹具种子下种子顺序 ≠ 编号顺序，否则本用例分辨不出两种口径

        SimulFlow flow = Started(3, order: bySeed, seed: TieSeed);
        flow.Submit(bySeed[0], "D4");
        flow.Submit(bySeed[1], "J9");
        flow.Submit(bySeed[2], "D9");
        SimulRoundReport report = flow.ResolveRound();
        Assert.Single(report.Power.Players.Select(p => p.Total).Distinct());
        return (flow, bySeed);
    }

    [Fact]
    public void 同势力首回合后按种子兜底()
    {
        (SimulFlow flow, PlayerId[] bySeed) = TiedFirstRound();
        Assert.Equal(bySeed, flow.Order);
    }

    [Fact]
    public void 第二回合起同势力上回合更晚者先()
    {
        // 接上例：第 2 回合三人再各部署一个覆盖互不重叠的单子，势力仍相同 → 第 3 回合顺位是第 2 回合的倒序。
        (SimulFlow flow, PlayerId[] bySeed) = TiedFirstRound();
        flow.Submit(bySeed[0], "G4");
        flow.Submit(bySeed[1], "G7");
        flow.Submit(bySeed[2], "J6");
        SimulRoundReport report = flow.ResolveRound();
        Assert.Single(report.Power.Players.Select(p => p.Total).Distinct());
        Assert.Equal(bySeed.Reverse(), flow.Order);
    }
}
