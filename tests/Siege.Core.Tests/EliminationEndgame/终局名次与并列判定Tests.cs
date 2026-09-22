using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.EliminationEndgame;

/// <summary>规格：elimination-endgame —— Requirement: 终局名次与并列判定（design.md D7 纯函数；restore-go-core-rules D4 并列链：势力 → 信物数 → 独占空格数 → 棋子数）</summary>
public class 终局名次与并列判定Tests
{
    private static StandingInput Active(PlayerId p, long power, int relics = 0, int cells = 0, int stones = 0) =>
        new(p, PlayerStatus.Active, power, relics, cells, stones, null);

    private static StandingInput Resigned(PlayerId p, long powerAtResign) => new(p, PlayerStatus.Resigned, powerAtResign, 0, 0, 0, null);

    private static StandingInput Eliminated(PlayerId p, int order) => new(p, PlayerStatus.Eliminated, 0, 0, 0, 0, order);

    private static int[] Ranks(ImmutableArray<Standing> standings, params PlayerId[] players) =>
        [.. players.Select(p => standings.Single(s => s.Player == p).Rank)];

    [Fact]
    public void 势力相同比信物数()
    {
        // 设计文档 §12.3：势力均为 42，信物数 3 与 1 → 3 者名次更高。
        // 变异验证 M-E17：FinisherComparer 删除信物级 → 红 1（本测试）。
        ImmutableArray<Standing> s = FinalStandings.Compute([Active(MatchFixtures.P0, 42, relics: 1), Active(MatchFixtures.P1, 42, relics: 3)]);
        Assert.Equal(new[] { 2, 1 }, Ranks(s, MatchFixtures.P0, MatchFixtures.P1));
    }

    [Fact]
    public void 逐级比较到棋子数()
    {
        // 设计文档 §12.3：势力、信物、独占空格数均相同，棋子数 11 与 8 → 11 者名次更高。
        // 变异验证 M-E18：FinisherComparer 删除棋子数级（少一级）→ 红 1（本测试：二者并列）。
        ImmutableArray<Standing> s = FinalStandings.Compute([Active(MatchFixtures.P0, 42, 2, 7, stones: 8), Active(MatchFixtures.P1, 42, 2, 7, stones: 11)]);
        Assert.Equal(new[] { 2, 1 }, Ranks(s, MatchFixtures.P0, MatchFixtures.P1));
    }

    [Fact]
    public void 完全相同则并列()
    {
        // 设计文档 §12.3：四项全同 → 共享同一名次；其后的名次跳号（竞争名次）。
        // 变异验证 M-E19：Append 不判并列、名次恒为位置 → 红 1（本测试）。
        ImmutableArray<Standing> s = FinalStandings.Compute([Active(MatchFixtures.P0, 42, 2, 7, 9), Active(MatchFixtures.P1, 42, 2, 7, 9), Active(MatchFixtures.P2, 10)]);
        Assert.Equal(new[] { 1, 1, 3 }, Ranks(s, MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2));
    }

    [Fact]
    public void 弃赛者排在完赛者之后()
    {
        // 设计文档 §12.3：弃赛玩家 D 势力 60，完赛玩家 A 势力 20 → A 高于 D。
        // 变异验证 M-E20：Compute 把弃赛者并入完赛者按势力排 → 红 1（本测试）。
        ImmutableArray<Standing> s = FinalStandings.Compute([Resigned(MatchFixtures.P3, 60), Active(MatchFixtures.P0, 20)]);
        Assert.Equal(new[] { 1, 2 }, Ranks(s, MatchFixtures.P0, MatchFixtures.P3));
        Assert.Equal(StandingGroup.Resigned, s.Single(x => x.Player == MatchFixtures.P3).Group);
    }

    [Fact]
    public void 多名弃赛者互比()
    {
        // 设计文档 §12.3：D 弃赛时势力 60，E 弃赛时 35 → 弃赛者组内 D 高于 E。
        ImmutableArray<Standing> s = FinalStandings.Compute([Active(MatchFixtures.P0, 20), Resigned(MatchFixtures.P2, 35), Resigned(MatchFixtures.P3, 60)]);
        Assert.Equal(new[] { 1, 3, 2 }, Ranks(s, MatchFixtures.P0, MatchFixtures.P2, MatchFixtures.P3));
    }

    [Fact]
    public void 出局者倒序()
    {
        // 设计文档 §12.3：F 第 5 大回合出局（第 1 个）、G 第 8 大回合出局（第 2 个）→ G 高于 F；出局者排在弃赛者之后。
        // 变异验证 M-E21：出局者按 EliminationOrder 升序 → 红 1（本测试）。
        ImmutableArray<Standing> s = FinalStandings.Compute([Eliminated(MatchFixtures.P1, 1), Eliminated(MatchFixtures.P2, 2), Resigned(MatchFixtures.P3, 5), Active(MatchFixtures.P0, 30)]);
        Assert.Equal(new[] { 1, 4, 3, 2 }, Ranks(s, MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3));
    }

    [Fact]
    public void 同一次结算出局者共享名次()
    {
        // restore-go-core-rules 裁决 #3：同一次结算中同时出局者共享同一出局序号 → 共享同一名次（竞争名次，其后跳号）。
        // 真实对局路径见 出局判定Tests.同时归零同时出局。
        ImmutableArray<Standing> s = FinalStandings.Compute([Active(MatchFixtures.P0, 30), Eliminated(MatchFixtures.P1, 1), Eliminated(MatchFixtures.P2, 1), Eliminated(MatchFixtures.P3, 2)]);
        Assert.Equal(new[] { 1, 3, 3, 2 }, Ranks(s, MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3));
    }

    [Fact]
    public void 整轮Pass时按同一规则排名()
    {
        // 接线：以「整轮 Pass」终局时走 Finish → FinalStandings.Compute 的同一路径——势力相同比信物数，控制 3 枚者高于控制 1 枚者。
        // （原「达上限时按同一规则排名」：大回合上限终局已删除，改由整轮 Pass 触发同一收尾路径，局面与断言不变。）
        // 局面：P0 {B2,B3} 与 P1 {H2,H3} 左右镜像（势力相同），信物格 A2 只被 P0 独占覆盖，G2 / J2 / H1 只被 P1 独占覆盖（信物内容不影响势力）。
        // 变异验证 M-R14（沿用）：Finish 把 ControlledRelics 传 0 → 本测试红（P0 与 P1 并列）。
        MatchFlow match = MatchFixtures.Started(relics: [("A2", RelicFixtures.Depot()), ("G2", RelicFixtures.Depot()), ("J2", RelicFixtures.Depot()), ("H1", RelicFixtures.Depot())])
            .AtRound(5, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3])
            .Stones(MatchFixtures.P0, "B2")
            .Stones(MatchFixtures.P1, "H2");

        match.PlayTurn("B3");   // P0
        match.PlayTurn("H3");   // P1
        match.PassTurn();       // P2
        match.PassTurn();       // P3：第 5 大回合结束，连续 Pass 计数 2（跨大回合累计）
        match.PassTurn();       // 第 6 大回合第 1 位：计数 3
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        match.PassTurn();       // 第 6 大回合第 2 位：计数 4 ≥ 4 → 整轮 Pass
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.AllPassed, match.Result!.Reason);

        Standing p0 = match.Result.Of(MatchFixtures.P0);
        Standing p1 = match.Result.Of(MatchFixtures.P1);
        Assert.Equal(p0.Input.Power, p1.Input.Power);   // 镜像局面：势力相同
        Assert.True(p0.Input.Power > 0);
        Assert.Equal((1, 3), (p0.Input.ControlledRelics, p1.Input.ControlledRelics));
        Assert.Equal((2, 1), (p0.Rank, p1.Rank));
        Assert.Equal(StandingGroup.Finisher, p1.Group);
    }

    [Fact]
    public void 信物相同比独占空格数()
    {
        // 规格 Scenario（restore-go-core-rules D4）：势力均为 60、控制信物均为 2，独占空格数 14 与 9 → 14 者名次更高（段 C 改用规格算例数值，原为 3 与 1）。
        // 变异验证 M-B13（段 B，实跑红 1）/ M-C（段 C 重跑）：FinisherComparer 删除独占空格级 → 本测试红（二者比到棋子数，P0 棋子多反而第 1）。
        ImmutableArray<Standing> s = FinalStandings.Compute([Active(MatchFixtures.P0, 60, relics: 2, cells: 9, stones: 20), Active(MatchFixtures.P1, 60, relics: 2, cells: 14, stones: 5)]);
        Assert.Equal(new[] { 2, 1 }, Ranks(s, MatchFixtures.P0, MatchFixtures.P1));
    }

    [Fact]
    public void 终局输入取独占空格数()
    {
        // 接线（restore-go-core-rules D4：并列链第三级改回独占空格数）：MatchFlow 终局时 StandingInput.ExclusiveCells
        // = 势力明细里该玩家的独占空格个数。P0 在 B5 / C5 各落一子，其余三人弃赛 → 只剩一名参赛玩家终局。
        // 变异验证 M-B12（段 B，实跑红 1）：MatchFlow.Finish 改成 detail.Groups.Length → 本测试红。
        MatchFlow match = MatchFixtures.Started().AtRound(5, MatchFixtures.All).Stones(MatchFixtures.P0, "B5", "C5");
        PlayerPower detail = match.Scoreboard.Latest!.Of(MatchFixtures.P0);
        Assert.True(detail.ExclusiveCells.Length > 2);   // 前提：独占空格数 ≠ 棋子数 / 棋串数
        match.Resign(MatchFixtures.P1);
        match.Resign(MatchFixtures.P2);
        match.Resign(MatchFixtures.P3);

        Assert.Equal(MatchPhase.Ended, match.Phase);
        StandingInput input = match.Result!.Of(MatchFixtures.P0).Input;
        Assert.Equal(detail.ExclusiveCells.Length, input.ExclusiveCells);
        // 段 B 重算：原夹具是一张 7×7 小图（值 8 = 领地 6 + 军势 2）；本测试改用 MatchFixtures 的标准夹具盘面，
        // 领地分与军势逐项取自同一份势力明细，不再写死数字。
        Assert.Equal(detail.Total, input.Power);
        Assert.Equal(detail.TerritoryScore + detail.Groups.Aggregate(BigInteger.Zero, (s, g) => s + g.Power), input.Power);
    }
}
