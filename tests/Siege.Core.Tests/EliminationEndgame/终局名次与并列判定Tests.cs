using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.EliminationEndgame;

/// <summary>规格：elimination-endgame —— Requirement: 终局名次与并列判定（design.md D7 纯函数）</summary>
public class 终局名次与并列判定Tests
{
    private static StandingInput Active(PlayerId p, long power, int relics = 0, int exclusive = 0, int stones = 0) =>
        new(p, PlayerStatus.Active, power, relics, exclusive, stones, null);

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
        // 设计文档 §12.3：势力、信物、独占空格均相同，棋子数 11 与 8 → 11 者名次更高。
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
}
