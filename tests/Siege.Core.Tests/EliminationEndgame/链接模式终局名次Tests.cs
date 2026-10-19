using System.Numerics;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.EliminationEndgame;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/elimination-endgame —— Requirement: 终局名次与并列判定（链接模式，<see cref="FinalStandings.Compute"/>）。
/// 算例取自该 Requirement 的 Scenario。retire-legacy-rules 第 3 组：旧模式名次实现删除，原链接版 <c>ComputeLink</c> 更名为唯一的 <c>Compute</c>。
/// </summary>
/// <remarks>
/// 变异验证（段 B 后半实跑）：M-B14 <c>DefeatComparer</c> 恒返回 0 → 「同一战场多名落败者按势力排」「信物相同比独占空格数」「逐级比较到棋子数」「决赛胜者第一」红 4
/// （「信物相同」首次 0 红——编号兜底恰好与期望同序，已把编号大者设为应在前者后重跑）；
/// M-B15 <c>MatchFlow.Finish</c> 的模式门反转 → 「两类终局条件Tests.唯一参赛者获胜」红（链接模式弃赛者须排在出局者之后）；旧模式名次测试 0 红（见实施报告）。
/// </remarks>
public class 链接模式终局名次Tests
{
    private static readonly PlayerId A = new(0);
    private static readonly PlayerId B = new(1);
    private static readonly PlayerId C = new(2);
    private static readonly PlayerId D = new(3);

    [Fact]
    public void 决赛胜者第一()
    {
        // Scenario：决赛战场结算，A 势力 120、C 势力 90；更早 B 以势力 42 落败、D 以势力 55 落败（同一大回合结束时两个战场同批结算）→ 名次 A、C、D、B。
        var result = FinalStandings.Compute([
            Finisher(A, 120),
            Defeated(C, 90, order: 2),
            Defeated(B, 42, order: 1),
            Defeated(D, 55, order: 1),
        ]);
        Assert.Equal([A, C, D, B], result.Select(s => s.Player));
        Assert.Equal([1, 2, 3, 4], result.Select(s => s.Rank));
        Assert.Equal(StandingGroup.Finisher, result[0].Group);
        Assert.All(result.Skip(1), s => Assert.Equal(StandingGroup.Eliminated, s.Group));
    }

    [Fact]
    public void 更早出局者按出局先后倒序()
    {
        // B 先落败（序号 1，势力 99），D 后落败（序号 2，势力 10）→ 越晚出局名次越高：D 在 B 前，与势力无关。
        var result = FinalStandings.Compute([Finisher(A, 50), Defeated(B, 99, order: 1), Defeated(D, 10, order: 2)]);
        Assert.Equal([A, D, B], result.Select(s => s.Player));
    }

    [Fact]
    public void 同一战场多名落败者按势力排()
    {
        // Scenario：三人战场结算，落败者 B 势力 55、C 势力 55，B 控制信物 2 枚、C 控制 1 枚 → B 的名次高于 C（不共享）。
        var result = FinalStandings.Compute([
            Finisher(A, 80),
            Defeated(C, 55, order: 1, relics: 1),
            Defeated(B, 55, order: 1, relics: 2),
        ]);
        Assert.Equal([A, B, C], result.Select(s => s.Player));
        Assert.Equal([1, 2, 3], result.Select(s => s.Rank));
    }

    [Fact]
    public void 信物相同比独占空格数()
    {
        // Scenario：两名玩家势力值均为 60、控制信物数量均为 2，独占空格数分别为 14 与 9 → 独占空格数 14 的玩家名次更高。
        var result = FinalStandings.Compute([
            Finisher(A, 100),
            Defeated(B, 60, order: 1, relics: 2, exclusive: 9),
            Defeated(C, 60, order: 1, relics: 2, exclusive: 14),
        ]);
        Assert.Equal([A, C, B], result.Select(s => s.Player));   // 编号大者在前：排除"按编号兜底"巧合
        Assert.Equal([1, 2, 3], result.Select(s => s.Rank));
    }

    [Fact]
    public void 逐级比较到棋子数()
    {
        // Scenario：势力、信物数量与独占空格数均相同，盘面棋子数分别为 11 与 8 → 11 的名次更高；四级全同才共享名次。
        var result = FinalStandings.Compute([
            Finisher(A, 100),
            Defeated(C, 60, order: 1, relics: 2, exclusive: 9, stones: 8),
            Defeated(B, 60, order: 1, relics: 2, exclusive: 9, stones: 11),
            Defeated(D, 60, order: 1, relics: 2, exclusive: 9, stones: 8),
        ]);
        Assert.Equal([A, B, C, D], result.Select(s => s.Player));
        Assert.Equal([1, 2, 3, 3], result.Select(s => s.Rank));
    }

    [Fact]
    public void 同次势力归零者共享名次()
    {
        // 同一次结算中因势力归零同时出局者共享同一名次（与势力无关）。
        var result = FinalStandings.Compute([
            Finisher(A, 100),
            new StandingInput(B, PlayerStatus.Eliminated, BigInteger.Zero, 0, 0, 0, 1) { Reason = EliminationReason.PowerZero },
            new StandingInput(C, PlayerStatus.Eliminated, BigInteger.Zero, 0, 3, 0, 1) { Reason = EliminationReason.PowerZero },
        ]);
        Assert.Equal([1, 2, 2], result.Select(s => s.Rank));
    }

    [Fact]
    public void 弃赛者最后()
    {
        // 弃赛者排在所有正常完赛与出局玩家之后；多名弃赛者按弃赛时势力。
        var result = FinalStandings.Compute([
            Finisher(A, 10),
            new StandingInput(B, PlayerStatus.Resigned, 500, 0, 0, 0, null),
            Defeated(C, 1, order: 1),
            new StandingInput(D, PlayerStatus.Resigned, 700, 0, 0, 0, null),
        ]);
        Assert.Equal([A, C, D, B], result.Select(s => s.Player));
        Assert.Equal(StandingGroup.Resigned, result[^1].Group);
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 对局里战场落败者按结算时的比较量排名()
    {
        // 端到端：三人同区（P0 两子、P1 与 P2 各一子）在第 3 大回合结束结算，独占者 P3 等待；决赛后终局。
        // 落败者在终局名次里用的是结算时记下的势力（PowerAtDefeat），不是终局时盘面上的值。
        MatchFlow match = LinkModeFixtures.Started([0, 0, 0, 3], new ScriptedPairingSource())
            .PlayRound(LinkModeFixtures.P0, LinkModeFixtures.P1, LinkModeFixtures.P2).PlayRound(LinkModeFixtures.P0).PassRound();
        Assert.Equal(LinkStage.Final, match.LinkStage);
        match.PassRound();

        Assert.Equal(EndReason.LastBattlefieldSettled, match.Result!.Reason);
        Assert.Equal(4, match.Result.Standings.Length);
        foreach (Standing s in match.Result.Standings.Where(s => s.Input.Reason == EliminationReason.BattlefieldDefeat))
        {
            Assert.Equal(match.StateOf(s.Player).PowerAtDefeat, s.Input.Power);
        }

        // 最早结算（三人战场）的两名落败者名次在决赛落败者之后。
        PlayerId finalLoser = match.Settlements[^1].Entries[1].Player;
        Assert.Equal(2, match.Result.Of(finalLoser).Rank);
        Assert.All(match.Settlements[0].Entries.Skip(1), e => Assert.True(match.Result.Of(e.Player).Rank >= 3));
    }

    private static StandingInput Finisher(PlayerId p, int power) => new(p, PlayerStatus.Active, power, 0, 0, 0, null);

    private static StandingInput Defeated(PlayerId p, int power, int order, int relics = 0, int exclusive = 0, int stones = 0) =>
        new(p, PlayerStatus.Eliminated, power, relics, exclusive, stones, order) { Reason = EliminationReason.BattlefieldDefeat };
}
