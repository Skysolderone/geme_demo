using System.Text;
using Siege.Core.Board;
using Siege.Core.Recruit;
using Siege.Core.Simul;
using static Siege.Core.Tests.Simul.SimulFixtures;

namespace Siege.Core.Tests.Simul;

/// <summary>openspec/specs/simultaneous-round —— Requirement: 可复现（determinism.md；K5）。</summary>
/// <remarks>变异记录（A1 实跑）：M5 征召候选改回打开面板时才抽（SimulPrep.EnterRecruit 调现有 EnterRecruit）→ 红 1（打开征召面板的先后不影响候选）。</remarks>
public sealed class 可复现Tests
{
    /// <summary>
    /// 固定的三回合决策（含并列冲突与掷骰）：第 1 回合两人都投 E5、C7（1 : 1 掷骰）；第 2 回合各投 J9 / G9 / D9 与 E9（E9 冲突）；
    /// 第 3 回合 P0 征召并投 H3，P1 投 H3 与 K3。
    /// </summary>
    private static string Play(SimulFlow flow)
    {
        var log = new StringBuilder();
        flow.Submit(P0, "E5", "C7");
        flow.Submit(P1, "E5", "C7");
        log.Append(Digest(flow, flow.ResolveRound()));

        flow.Submit(P0, "J9", "E9");
        flow.Submit(P1, "G9", "E9");
        log.Append(Digest(flow, flow.ResolveRound()));

        SimulPrep prep = flow.PrepFor(P0);
        prep.EnterRecruit();
        prep.Pick(1);
        prep.Stage(Unit("H3"));
        Assert.True(prep.Submit().IsValid);
        flow.Submit(P1, "H3", "K3");
        log.Append(Digest(flow, flow.ResolveRound()));
        return log.ToString();
    }

    private static string Digest(SimulFlow flow, SimulRoundReport r) =>
        $"R{r.Round} order={string.Join(">", r.Order)} " +
        $"conflicts=[{string.Join("; ", r.Conflicts.Select(c => c.Describe()))}] " +
        $"landed=[{string.Join(",", r.Landed.Select(l => $"{l.Player}@{l.Placement}"))}] " +
        $"removed=[{string.Join(",", r.Captures.Select(s => $"{s.By?.ToString() ?? "iso"}:{string.Join("/", s.Removed)}"))}] " +
        $"power=[{string.Join(",", r.Power.Players.Select(p => $"{p.Player}={p.Total}"))}] " +
        $"hands=[{string.Join(",", flow.Players.Select(p => flow.Hands.PrivateViewOf(p)))}] " +
        $"board={flow.Board.Serialize()}\n";

    [Fact]
    public void 同种子同决策逐回合相同()
    {
        SimulFlow a = Started(2);
        SimulFlow b = Started(2);
        string first = Play(a);
        string second = Play(b);

        Assert.Equal(first, second);
        Assert.Equal(3, a.Reports.Count);
        Assert.True(a.Reports.Sum(r => r.DiceRolled) > 0, "样本里须有掷骰。");
        Assert.True(a.Reports.Sum(r => r.Conflicts.Length) >= 3, "样本里须有冲突。");
        Assert.Equal(a.DiceConsumed, b.DiceConsumed);
    }

    [Fact]
    public void 打开征召面板的先后不影响候选()
    {
        // 一局 P0 先打开、再 P1；另一局只有 P1 打开、P0 不打开。若候选在打开面板时才从共享的 recruit 子流抽，第二局 P1 会拿到第一局 P0 的那组。
        SimulFlow a = Started(2);
        RecruitPanelView a0 = a.PrepFor(P0).EnterRecruit();
        RecruitPanelView a1 = a.PrepFor(P1).EnterRecruit();

        SimulFlow b = Started(2);
        RecruitPanelView b1 = b.PrepFor(P1).EnterRecruit();
        Assert.Equal(a1.Candidates.Select(c => c.Type), b1.Candidates.Select(c => c.Type));
        Assert.Equal(a.Hands.RecruitStreamConsumed, b.Hands.RecruitStreamConsumed);   // 回合开始时全员一次抽完，打开面板不再消费

        RecruitPanelView b0 = b.PrepFor(P0).EnterRecruit();
        Assert.Equal(a0.Candidates.Select(c => c.Type), b0.Candidates.Select(c => c.Type));

        // 第 2 回合：上回合一人征召、一人没打开面板，本回合的候选照样与操作无关。
        a.Submit(P0, "D4");
        a.SubmitRestEmpty();
        a.ResolveRound();
        b.Submit(P0, "D4");
        b.SubmitRestEmpty();
        b.ResolveRound();
        Assert.Equal(a.PrepFor(P1).EnterRecruit().Candidates.Select(c => c.Type), b.PrepFor(P1).EnterRecruit().Candidates.Select(c => c.Type));
    }
}
