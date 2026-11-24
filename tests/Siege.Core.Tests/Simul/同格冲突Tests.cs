using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Simul;
using static Siege.Core.Tests.Simul.SimulFixtures;

namespace Siege.Core.Tests.Simul;

/// <summary>openspec/specs/simultaneous-round —— Requirement: 同格冲突（v0.1 §9；Q06、Q14、G1）。算例 E1–E4 取自 design.md。</summary>
/// <remarks>变异记录（A1 实跑，只跑 Simul 测试）：M1 冲突改读落下后的盘面 → 红 1（本新部署不支援）；M6 冲突格改先列后行消费骰子 → 红 1（掷骰按冲突格坐标字典序消费）。</remarks>
public sealed class 同格冲突Tests
{
    /// <summary>E4 用的对局种子：<c>simul-dice</c> 子流前四次 <c>NextInt(6) + 1</c> 依次为 3、3、5、2（穷举种子得到，见 <see cref="E4的种子给出3352"/>）。</summary>
    internal static readonly GameSeed DiceSeed = new(4096);

    /// <summary>
    /// E1 局面（2 人局，顺位 P0、P1）：X = E5。S0 上 P0 在 E5 四邻有 D5、E4 两个单位，P1 有 F5 一个；两人都在 E5 部署普通单位，均已提交。
    /// </summary>
    internal static SimulFlow E1()
    {
        SimulFlow flow = Started(2, f =>
        {
            f.Debug.Stones(P0, "D5", "E4");
            f.Debug.Stones(P1, "F5");
        }, order: [P0, P1]);
        flow.Submit(P0, "E5");
        flow.Submit(P1, "E5");
        return flow;
    }

    [Fact]
    public void 冲突比战力()
    {
        // E1：P0 战力 1 + 2 = 3，P1 战力 1 + 1 = 2，P0 胜。
        SimulFlow flow = E1();
        SimulConflict conflict = Assert.Single(flow.ResolveRound().Conflicts);

        Assert.Equal(Coord.Parse("E5"), conflict.Cell);
        Assert.Equal((1, 2, 3), (conflict.EntrantOf(P0).BasePower, conflict.EntrantOf(P0).Support, conflict.EntrantOf(P0).Power));
        Assert.Equal((1, 1, 2), (conflict.EntrantOf(P1).BasePower, conflict.EntrantOf(P1).Support, conflict.EntrantOf(P1).Power));
        Assert.Equal(P0, conflict.Winner);
        Assert.Empty(conflict.Rolls);
        Assert.Equal(P0, flow.OwnerAt("E5"));
        Assert.Equal("E5：P0 3 : P1 2，P0 胜", conflict.Describe());
    }

    [Fact]
    public void 本回合新部署不支援()
    {
        // E2（跨区版本）：八邻禁落让同一区内 P1 的第二个落点不可能与 X 四邻相接，所以 Y 取在另一个区——
        // 第 4 回合，X = K5（出生岛 0 东缘），Y = L5（公共平台 0）。S0 上 P0 在 X 四邻有 J5、K4，P1 有 K6。
        // P1 同回合在 Y 成功落下，Y 与 X 四邻相接，但 P1 在 X 的战力仍是 1 + 1 = 2（不是 3），P0 以 3 胜、不掷骰。
        SimulFlow flow = Started(2, f =>
        {
            f.Debug.Stones(P0, "J5", "K4");
            f.Debug.Stones(P1, "K6");
        }, round: 4, order: [P0, P1]);
        flow.Submit(P0, "K5");
        flow.Submit(P1, "K5", "L5");
        SimulRoundReport report = flow.ResolveRound();

        SimulConflict conflict = Assert.Single(report.Conflicts);
        Assert.Equal(1, conflict.EntrantOf(P1).Support);
        Assert.Equal(2, conflict.EntrantOf(P1).Power);
        Assert.Equal(3, conflict.EntrantOf(P0).Power);
        Assert.Equal(P0, conflict.Winner);
        Assert.Empty(conflict.Rolls);
        Assert.Equal(0, flow.DiceConsumed);
        Assert.Equal(P1, flow.OwnerAt("L5"));
        Assert.Equal(P0, flow.OwnerAt("K5"));
    }

    [Fact]
    public void 堡垒基础战力()
    {
        // E3：P0 投堡垒（4）、E5 四邻无己方单位；P1 投普通单位、四邻有 D5、E4（1 + 2 = 3）。P0 胜。
        SimulFlow flow = Started(2, f => f.Debug.Stones(P1, "D5", "E4"), order: [P0, P1], extra: [(PieceType.Fortress, 1)]);
        flow.Submit(P0, Unit("E5", PieceType.Fortress));
        flow.Submit(P1, "E5");
        SimulConflict conflict = Assert.Single(flow.ResolveRound().Conflicts);

        Assert.Equal((4, 0, 4), (conflict.EntrantOf(P0).BasePower, conflict.EntrantOf(P0).Support, conflict.EntrantOf(P0).Power));
        Assert.Equal(3, conflict.EntrantOf(P1).Power);
        Assert.Equal(P0, conflict.Winner);
        Assert.Equal(PieceType.Fortress, flow.Board[Coord.Parse("E5")].Occupant!.Value.Type);
    }

    [Fact]
    public void 多人并列掷骰()
    {
        // E4：P0、P1、P2 争 E5，战力 2 / 2 / 1（P0 有 D5、P1 有 E4 相接，P2 没有）。P2 先出局；P0、P1 掷骰 3 : 3，重掷 5 : 2，P0 胜。
        SimulFlow flow = Started(3, f =>
        {
            f.Debug.Stones(P0, "D5");
            f.Debug.Stones(P1, "E4");
        }, order: [P0, P1, P2], seed: DiceSeed);
        flow.Submit(P0, "E5");
        flow.Submit(P1, "E5");
        flow.Submit(P2, "E5");
        SimulConflict conflict = Assert.Single(flow.ResolveRound().Conflicts);

        Assert.Equal([2, 2, 1], conflict.Entrants.Select(e => e.Power));
        Assert.Equal(2, conflict.Rolls.Length);
        Assert.Equal([new SimulDieRoll(P0, 3), new SimulDieRoll(P1, 3)], conflict.Rolls[0]);
        Assert.Equal([new SimulDieRoll(P0, 5), new SimulDieRoll(P1, 2)], conflict.Rolls[1]);
        Assert.Equal(P0, conflict.Winner);
        Assert.Equal(4, flow.DiceConsumed);
        Assert.Equal(P0, flow.OwnerAt("E5"));
        Assert.Equal("E5：P0 2 : P1 2 : P2 1，掷骰 3 : 3 → 5 : 2，P0 胜", conflict.Describe());
    }

    [Fact]
    public void 没有并列时不掷骰()
    {
        SimulFlow flow = E1();
        flow.ResolveRound();
        Assert.Equal(0, flow.DiceConsumed);
    }

    [Fact]
    public void 掷骰按冲突格坐标字典序消费()
    {
        // 两格都是 1 : 1 并列：E5（第 5 行）与 C7（第 7 行、但列更小）。坐标字典序先行后列 → E5 先掷。
        // 期望值由测试内独立派生的同名子流给出：E5 第一轮的两颗骰子就是子流的前两次。
        SimulFlow flow = Started(2, order: [P0, P1]);
        flow.Submit(P0, "E5", "C7");
        flow.Submit(P1, "E5", "C7");
        SimulRoundReport report = flow.ResolveRound();

        Assert.Equal(["E5", "C7"], report.Conflicts.Select(c => c.Cell.ToNotation()));
        RandomStream reference = Seed.Stream("simul-dice");
        int first = reference.NextInt(6) + 1;
        int second = reference.NextInt(6) + 1;
        Assert.Equal([new SimulDieRoll(P0, first), new SimulDieRoll(P1, second)], report.Conflicts[0].Rolls[0]);
        Assert.Equal(report.DiceRolled, flow.DiceConsumed);
    }

    [Fact]
    public void E4的种子给出3352()
    {
        // 钉住 E4 的种子：子流 simul-dice 前四次骰点。
        RandomStream dice = DiceSeed.Stream(SimulRules.DiceStream);
        Assert.Equal([3, 3, 5, 2], Enumerable.Range(0, 4).Select(_ => dice.NextInt(6) + 1));
    }
}
