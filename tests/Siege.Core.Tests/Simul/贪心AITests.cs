using System.Reflection;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Simul;
using static Siege.Core.Tests.Simul.SimulFixtures;

namespace Siege.Core.Tests.Simul;

/// <summary>openspec/specs/simultaneous-round —— Requirement: 贪心 AI（G6；design J4）。</summary>
/// <remarks>变异记录（A2 实跑）：M17 可部署格不扣活形内部格 → 红 1（可部署格与单枚校验一致）；M18 推演不提走对手无气整体 → 补本类"只有自己落下的推演按结算口径提子"之前全绿（假绿），补后红 1；M19 AI 一枚都不部署 → 红 3。</remarks>
public sealed class 贪心AITests
{
    /// <summary>在 link:1:2 上开局，两名 AI 跑 <paramref name="rounds"/> 回合，返回对局与每回合的决策。</summary>
    private static (SimulFlow Flow, List<List<SimulAiDecision>> Decisions) Run(int rounds)
    {
        SimulFlow flow = SimulFlow.Create(MapCatalog.Resolve("link:1:2"), Seed, PlayersOf(2));
        flow.PlantPrototype();
        var decisions = new List<List<SimulAiDecision>>();
        for (int r = 0; r < rounds && flow.Phase == SimulPhase.InProgress; r++)
        {
            decisions.Add([.. flow.Order.Select(p => SimulGreedyAi.Play(flow.PublicView(), flow.PrepFor(p)))]);
            flow.ResolveRound();
        }

        return (flow, decisions);
    }

    [Fact]
    public void 小图两人跑三回合每回合有人部署()
    {
        (SimulFlow flow, List<List<SimulAiDecision>> decisions) = Run(3);

        Assert.Equal(3, flow.Reports.Count);
        Assert.Equal(SimulPhase.InProgress, flow.Phase);
        Assert.All(flow.Reports, r => Assert.True(r.SubmittedCount > 0, $"第 {r.Round} 回合无人部署"));
        Assert.All(flow.Reports, r => Assert.All(r.Batches, b => Assert.InRange(b.Placements.Length, 0, SimulRules.DeployLimit)));
        Assert.All(decisions.SelectMany(d => d), d => Assert.True(d.ExpectedGain >= 0));
        Assert.All(flow.Players, p => Assert.NotEmpty(flow.Board.GroupsOf(p)));
    }

    [Fact]
    public void 同种子两局决策相同且不消费随机()
    {
        (SimulFlow a, List<List<SimulAiDecision>> da) = Run(3);
        (SimulFlow b, List<List<SimulAiDecision>> db) = Run(3);

        Assert.Equal(da.Select(r => string.Join(";", r.Select(d => $"{d.Player}:{string.Join(",", d.Placements)}|{string.Join(",", d.Recruited)}"))),
            db.Select(r => string.Join(";", r.Select(d => $"{d.Player}:{string.Join(",", d.Placements)}|{string.Join(",", d.Recruited)}"))));
        Assert.Equal(a.Board.Serialize(), b.Board.Serialize());
    }

    [Fact]
    public void AI只读公开视图与本人句柄()
    {
        // 结构守门：AI 的入口参数只有公开视图与本人准备句柄，拿不到对局本体（也就拿不到别人的暂放与账本）。
        MethodInfo[] entries = [.. typeof(SimulGreedyAi).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.DeclaringType == typeof(SimulGreedyAi))];
        MethodInfo play = Assert.Single(entries);
        Assert.Equal([typeof(SimulPublicView), typeof(SimulPrep)], play.GetParameters().Select(p => p.ParameterType));

        // 别人已暂放的内容不影响 AI：P1 先暂放 D4 与否，P0 的决策相同。
        SimulFlow quiet = Started(2, order: [P0, P1]);
        SimulAiDecision alone = SimulGreedyAi.Play(quiet.PublicView(), quiet.PrepFor(P0));
        SimulFlow busy = Started(2, order: [P0, P1]);
        busy.PrepFor(P1).Stage(Unit(alone.Placements[0].Coord.ToNotation()));
        SimulAiDecision crowded = SimulGreedyAi.Play(busy.PublicView(), busy.PrepFor(P0));
        Assert.Equal(alone.Placements, crowded.Placements);
    }

    [Fact]
    public void 只有自己落下的推演按结算口径提子()
    {
        // P1 的 C3 只剩一口气 C4（D3 是 P0）；F7 四邻全是 P1 的单子。
        SimulFlow flow = Started(2, f =>
        {
            f.Debug.Stones(P1, "C3", "F6", "E7", "G7", "F8");
            f.Debug.Stones(P0, "D3");
        });
        GameBoard s0 = flow.RoundStartBoard;

        GameBoard capture = SimulRules.ProjectAlone(s0, P0, [Unit("C4")]);
        Assert.Null(capture[Coord.Parse("C3")].Occupant);
        Assert.Equal(P0, capture[Coord.Parse("C4")].Occupant!.Value.Owner);

        GameBoard suicide = SimulRules.ProjectAlone(s0, P0, [Unit("F7")]);
        Assert.Null(suicide[Coord.Parse("F7")].Occupant);
        Assert.All(new[] { "F6", "E7", "G7", "F8" }, c => Assert.Equal(P1, suicide[Coord.Parse(c)].Occupant!.Value.Owner));

        Assert.Equal(P1, s0[Coord.Parse("C3")].Occupant!.Value.Owner);   // 零副作用
        Assert.Equal(P1, flow.OwnerAt("C3"));
    }

    [Fact]
    public void 可部署格与单枚校验一致()
    {
        // 第 1 回合（只限出生岛）与第 4 回合（全图）各比一次：可部署格 ⇔ 只暂放这一格时校验通过。局面含 P0 的活形（C3、E3 两只眼）与若干单位。
        foreach (int round in new[] { 1, 4 })
        {
            SimulFlow flow = Started(2, f =>
            {
                f.Debug.Stones(P0, 部署校验Tests.AliveWithEyesC3E3);
                f.Debug.Stones(P1, "H8", "P5");
            }, round: round);
            foreach (PlayerId p in new[] { P0, P1 })
            {
                SimulPrep prep = flow.PrepFor(p);
                HashSet<Coord> deployable = [.. prep.DeployableCells()];
                Assert.NotEmpty(deployable);
                foreach (Coord c in flow.Map.AllCoords())
                {
                    prep.Clear();
                    prep.Stage(new Placement(c, PieceType.Basic));
                    Assert.Equal(deployable.Contains(c), prep.Check().IsValid);
                }

                prep.Clear();
            }
        }
    }
}
