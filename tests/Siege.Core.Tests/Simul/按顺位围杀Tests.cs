using Siege.Core.Board;
using Siege.Core.Simul;
using static Siege.Core.Tests.Simul.SimulFixtures;

namespace Siege.Core.Tests.Simul;

/// <summary>
/// openspec/specs/simultaneous-round —— Requirement: 按顺位围杀（v0.1 §7.2；Q03）。算例 E6、E7 取自 design.md。
/// E6 局面（出生岛 0 左下角，西、南两侧是墙）：GA = P0 的 C3-D3，只剩一口气 E3；GB = P1 的 C4-D4，只剩一口气 E4；
/// P0 的 C5-D5 在上方压住 GB。P0 堵 E4，P1 堵 E3：落下后 GA、GB 都无气，P1 的 E3 还有气 F3，P0 的 E4 还有气 F4、E5。
/// </summary>
/// <remarks>变异记录（A1 实跑）：M2 围杀顺位反转（order.Reverse()）→ 红 4（本类 3 条 + 认输者的单位可被围杀）。</remarks>
public sealed class 按顺位围杀Tests
{
    private static SimulRoundReport MutualAtari(PlayerId[] order, out SimulFlow flow)
    {
        flow = Started(2, f =>
        {
            f.Debug.Stones(P0, "C3", "D3", "C5", "D5");
            f.Debug.Stones(P1, "C4", "D4");
        }, order: order);
        flow.Submit(P0, "E4");
        flow.Submit(P1, "E3");
        return flow.ResolveRound();
    }

    [Fact]
    public void 互相堵死按顺位()
    {
        SimulRoundReport report = MutualAtari([P0, P1], out SimulFlow flow);

        Assert.Equal(3, report.Captures.Length);
        Assert.Equal(P0, report.Captures[0].By);
        Assert.Equal(["C4", "D4"], report.Captures[0].Removed.Select(s => s.Coord.ToNotation()));
        Assert.All(report.Captures[0].Removed, s => Assert.Equal(P1, s.Owner));
        Assert.Equal(P1, report.Captures[1].By);
        Assert.Empty(report.Captures[1].Removed);
        Assert.True(report.Captures[2].IsIsolation);
        Assert.Empty(report.Captures[2].Removed);

        Assert.Equal(P0, flow.OwnerAt("C3"));
        Assert.Equal(P0, flow.OwnerAt("D3"));
        Assert.Null(flow.OwnerAt("C4"));
        Assert.Equal(P1, flow.OwnerAt("E3"));
    }

    [Fact]
    public void 顺位对调结果相反()
    {
        SimulRoundReport report = MutualAtari([P1, P0], out SimulFlow flow);

        Assert.Equal(P1, report.Captures[0].By);
        Assert.Equal(["C3", "D3"], report.Captures[0].Removed.Select(s => s.Coord.ToNotation()));
        Assert.Empty(report.Captures[1].Removed);
        Assert.Null(flow.OwnerAt("C3"));
        Assert.Equal(P1, flow.OwnerAt("C4"));
        Assert.Equal(P1, flow.OwnerAt("D4"));
    }

    [Fact]
    public void 自杀式部署结算后移除()
    {
        // E7：F7 四邻 F6 / E7 / G7 / F8 全是 P1 的单子（各自另有气）。P0 落 F7：不提任何子，结算后被 P1 的顺位提走。
        SimulFlow flow = Started(2, f => f.Debug.Stones(P1, "F6", "E7", "G7", "F8"), order: [P0, P1]);
        flow.Submit(P0, "F7");
        flow.SubmitRestEmpty();
        SimulRoundReport report = flow.ResolveRound();

        Assert.Empty(report.Captures[0].Removed);
        Assert.Equal(P1, report.Captures[1].By);
        Assert.Equal(["F7"], report.Captures[1].Removed.Select(s => s.Coord.ToNotation()));
        Assert.Null(flow.OwnerAt("F7"));
        Assert.Equal(19, flow.HandCount(P0));
        Assert.All(new[] { "F6", "E7", "G7", "F8" }, c => Assert.Equal(P1, flow.OwnerAt(c)));
    }

    [Fact]
    public void 孤立自绝移除仍然无气的整体()
    {
        // 同 E7 局面，但测试接缝把顺位只留 P0：没有任何顺位会提走 P0 的 F7，孤立自绝这一步把它移除。
        // （正常对局里参赛中玩家 ≥ 2 时，任一无气整体都会在某个非其所有者的顺位上被提走；这一步是规则表述的兜底。）
        SimulFlow flow = Started(2, f => f.Debug.Stones(P1, "F6", "E7", "G7", "F8"), order: [P0]);
        flow.Submit(P0, "F7");
        flow.SubmitRestEmpty();
        SimulRoundReport report = flow.ResolveRound();

        Assert.Equal(2, report.Captures.Length);
        Assert.Empty(report.Captures[0].Removed);
        Assert.True(report.Captures[1].IsIsolation);
        Assert.Equal(["F7"], report.Captures[1].Removed.Select(s => s.Coord.ToNotation()));
        Assert.Null(flow.OwnerAt("F7"));
    }
}
