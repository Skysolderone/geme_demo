using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.EliminationEndgame;

/// <summary>规格：elimination-endgame —— Requirement: 终局名次与并列判定（design.md D7 纯函数）</summary>
public class 终局名次与并列判定Tests
{
    private static StandingInput Active(PlayerId p, long power, int relics = 0, int sites = 0, int stones = 0) =>
        new(p, PlayerStatus.Active, power, relics, sites, stones, null);

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
        // 设计文档 §12.3：势力、信物、控制据点数均相同，棋子数 11 与 8 → 11 者名次更高（scoring-sites：第 3 级由独占空格数改为据点数，参数值不变）。
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
    public void 达上限时按同一规则排名()
    {
        // round-cap：以「达大回合上限」终局时走 Finish → FinalStandings.Compute 的同一路径——势力相同比信物数，控制 3 枚者高于控制 1 枚者。
        // 局面：P0 {B2,B3} 与 P1 {H2,H3} 左右镜像（势力相同），信物格 A2 只被 P0 独占覆盖，G2 / J2 / H1 只被 P1 独占覆盖（信物内容不影响势力）。
        // 变异验证 M-R1：EndMajorRound 删除上限检查 → 红（本测试：对局未结束）；M-R5 同样红。
        // 变异验证 M-R14：Finish 在 MajorRoundLimit 下把 ControlledRelics 传 0 → 红 1（本测试：P0 与 P1 并列）。
        MatchFlow match = MatchFixtures.Started(options: MatchFixtures.DominanceOff, relics: [("A2", RelicFixtures.Depot()), ("G2", RelicFixtures.Depot()), ("J2", RelicFixtures.Depot()), ("H1", RelicFixtures.Depot())])
            .AtRound(15, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3])
            .Stones(MatchFixtures.P0, "B2")
            .Stones(MatchFixtures.P1, "H2");

        match.PlayTurn("B3");   // P0
        match.PlayTurn("H3");   // P1
        match.PassTurn();       // P2
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        match.PassTurn();       // P3：第 15 大回合结束 → 达上限
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.MajorRoundLimit, match.Result!.Reason);

        Standing p0 = match.Result.Of(MatchFixtures.P0);
        Standing p1 = match.Result.Of(MatchFixtures.P1);
        Assert.Equal(p0.Input.Power, p1.Input.Power);   // 镜像局面：势力相同
        Assert.True(p0.Input.Power > 0);
        Assert.Equal((1, 3), (p0.Input.ControlledRelics, p1.Input.ControlledRelics));
        Assert.Equal((2, 1), (p0.Rank, p1.Rank));
        Assert.Equal(StandingGroup.Finisher, p1.Group);
    }

    [Fact]
    public void 碾压获胜者为第1名()
    {
        // dominance-victory 规格算例：以势力碾压终局，获胜者 210，其余 80、70、55 → 获胜者第 1 名，其余按势力依次第 2、3、4 名。
        // 变异验证 M-DV11：Finish 在 PowerDominance 时改为"候选第 1、其余按玩家编号"另排一套 → 红 1（本测试）。
        ImmutableArray<Standing> table = FinalStandings.Compute([
            new StandingInput(new PlayerId(0), PlayerStatus.Active, 210, 0, 0, 0, null),
            new StandingInput(new PlayerId(1), PlayerStatus.Active, 55, 0, 0, 0, null),
            new StandingInput(new PlayerId(2), PlayerStatus.Active, 80, 0, 0, 0, null),
            new StandingInput(new PlayerId(3), PlayerStatus.Active, 70, 0, 0, 0, null)]);
        Assert.Equal([0, 2, 3, 1], table.Select(s => s.Player.Value));
        Assert.Equal([1, 2, 3, 4], table.Select(s => s.Rank));

        // 真实对局走到碾压终局：其余三人的势力高低与玩家编号顺序相反（P3 > P2 > P1），
        // 名次若被另写成"候选第 1、其余按编号 / 按名单顺序"就会与比较链结果不同（裁决 5：不新增第二套排序）。
        MatchFlow match = MatchFixtures.Started(options: MatchFixtures.DominanceOn).AtRound(6, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3]);
        foreach ((string cell, PieceType type) in new[] { ("A1", PieceType.Fortress), ("B1", PieceType.Fortress), ("A2", PieceType.Fortress), ("B2", PieceType.Multiplier), ("C1", PieceType.Multiplier) })
        {
            match.Board.Place(Coord.Parse(cell), MatchFixtures.P0, type);
        }

        match.Stones(MatchFixtures.P1, "H1").Stones(MatchFixtures.P2, "A8", "B8").Stones(MatchFixtures.P3, "H8", "J8", "G8");
        match.PassTurn();
        match.PassTurn();
        match.PassTurn();
        match.PlayTurn("G9");
        Assert.Equal(EndReason.PowerDominance, match.Result!.Reason);

        long[] power = [.. MatchFixtures.All.Select(p => match.Scoreboard.Latest!.Of(p).Total)];
        Assert.True(power[0] > power[3] && power[3] > power[2] && power[2] > power[1], string.Join(" ", power));
        Assert.Equal(new[] { MatchFixtures.P0, MatchFixtures.P3, MatchFixtures.P2, MatchFixtures.P1 }, match.Result.Standings.Select(s => s.Player));
        Assert.Equal([1, 2, 3, 4], match.Result.Standings.Select(s => s.Rank));
        Assert.Equal([MatchFixtures.P0], match.Result.Winners);
        Assert.Equal(power, [.. MatchFixtures.All.Select(p => match.Result.Of(p).Input.Power)]);
    }

    [Fact]
    public void 信物相同比据点数()
    {
        // 规格 Scenario（scoring-sites D-G）：势力均为 60、控制信物均为 2，控制据点数 3 与 1 → 3 者名次更高；独占空格数不参与比较。
        // 变异验证 M-S13（段 A2）：FinisherComparer 删除据点级 → 红 1（本测试：二者比到棋子数，P0 棋子多反而第 1）。
        ImmutableArray<Standing> s = FinalStandings.Compute([Active(MatchFixtures.P0, 60, relics: 2, sites: 1, stones: 20), Active(MatchFixtures.P1, 60, relics: 2, sites: 3, stones: 5)]);
        Assert.Equal(new[] { 2, 1 }, Ranks(s, MatchFixtures.P0, MatchFixtures.P1));
    }

    [Fact]
    public void 终局输入取控制中的据点数量()
    {
        // 接线：MatchFlow 终局时 StandingInput.ControlledSites = 势力明细里该玩家控制的据点个数（不是独占空格数）。
        // P0 占据营帐 B5、唯一覆盖篝火 D5（被 C5 覆盖），另有大量独占空格；其余三人弃赛 → 只剩一名参赛玩家终局。
        // 变异验证 M-S14（段 A2）：MatchFlow.Finish 改回 detail.ExclusiveCells.Length → 红，含本测试。
        MatchFlow match = SiteFixtures.Started(null, ("B5", SiteTier.Tent), ("D5", SiteTier.Campfire)).AtRound(5, MatchFixtures.All)
            .Stones(MatchFixtures.P0, "B5", "C5");
        Assert.True(match.Scoreboard.Latest!.Of(MatchFixtures.P0).ExclusiveCells.Length > 2);   // 前提：独占空格数 ≠ 据点数
        match.Resign(MatchFixtures.P1);
        match.Resign(MatchFixtures.P2);
        match.Resign(MatchFixtures.P3);

        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(2, match.Result!.Of(MatchFixtures.P0).Input.ControlledSites);
        Assert.Equal(5 + 15 + 2, match.Result.Of(MatchFixtures.P0).Input.Power);
    }
}
