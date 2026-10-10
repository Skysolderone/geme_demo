using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.EliminationEndgame;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/elimination-endgame —— Requirement: 两类终局条件（链接模式；裁决 S4 新增原因"最后战场结算"）。
/// 旧模式的「三类终局条件」由既有测试原样守住。
/// </summary>
/// <remarks>
/// 变异验证（段 B 后半实跑）：M-B13 大回合结束的"最后战场结算"分支改记 <c>EndReason.LastPlayerStanding</c> → 「决赛结算即终局」「四人同区一次终局」
/// 与「链接模式端到端Tests」5 行，共红 7。
/// </remarks>
public class 两类终局条件Tests
{
    [Fact]
    public void 唯一参赛者获胜()
    {
        // Scenario：4 人局中 2 人以战场落败出局、1 人弃赛 → 剩余的唯一参赛玩家直接获胜，对局立即结束（原因"只剩一名参赛玩家"，优先于最后战场结算）。
        MatchFlow match = Started([0, 0, 3, 3]).PassRound();
        Assert.Equal(2, match.Settlements.Count);
        PlayerId[] winners = [.. match.Settlements.Select(s => s.Winner)];
        Assert.Equal(MatchPhase.InProgress, match.Phase);

        match.Resign(winners[0]);

        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.LastPlayerStanding, match.Result!.Reason);
        Assert.Equal(winners[1], match.Result.Winners.Single());
        Assert.Equal(StandingGroup.Resigned, match.Result.Of(winners[0]).Group);
        Assert.Equal(4, match.Result.Of(winners[0]).Rank);   // 弃赛者排最后
    }

    [Fact]
    public void 决赛结算即终局()
    {
        // Scenario：决赛战场内两名玩家在同一大回合都 Pass，地图上另有无人的出生棋盘 → 该大回合结束时终止，原因"最后战场结算"。
        MatchFlow match = Started([0, 1, 3, 4], new ScriptedPairingSource()).PassRounds(4);
        Assert.Equal(LinkStage.Final, match.LinkStage);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Equal(5, match.MajorRound);

        match.PassRound();

        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.LastBattlefieldSettled, match.Result!.Reason);
        Assert.Equal(5, match.Result.MajorRound);
        Assert.Equal(5, match.MajorRound);
        Assert.Single(match.OccupiedBattlefields());
        Assert.Equal(4, match.Result.Standings.Length);
        Assert.Equal(match.Settlements[^1].Winner, match.Result.Winners.Single());
    }

    [Fact]
    public void 非最后战场的整轮Pass不终局()
    {
        // Scenario：尚有两个以上含在局玩家的战场，其中一个战场内全员 Pass → 该战场结算，对局继续。
        MatchFlow match = Started([0, 0, 3, 4]).PassRound();
        Assert.Single(match.Settlements);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Null(match.Result);
    }

    [Fact]
    public void 四人同区一次终局()
    {
        // Scenario：4 名玩家锁定同一出生棋盘，该战场结算 → 对局立即结束，胜者第 1，其余三人以战场落败出局；其余 4 块无人棋盘不影响终局。
        MatchFlow match = Started([0, 0, 0, 0]).PlayRound(P0, P1).PassRound();

        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.LastBattlefieldSettled, match.Result!.Reason);
        Assert.Equal(2, match.Result.MajorRound);
        PlayerId winner = match.Settlements.Single().Winner;
        Assert.Equal(winner, match.Result.Winners.Single());
        Assert.All(All.Where(p => p != winner), p => Assert.Equal(EliminationReason.BattlefieldDefeat, match.StateOf(p).EliminationReason));
        Assert.Empty(match.Pairings);
    }

    [Fact]
    public void 轮数再多也不结束()
    {
        // Scenario：对局进行到第 40 大回合且两类终局条件均未满足 → 对局继续。
        MatchFlow match = Started([0, 0, 3, 4], new ScriptedPairingSource()).PlayRound(P0).AtRound(40, [P0, P1, P2, P3]).PlayRound(P1);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Equal(41, match.MajorRound);
    }

    [Fact]
    public void 旧模式终局原因不含最后战场结算()
    {
        // S4：新成员只在链接模式使用；旧模式整轮 Pass 仍按原规则终局。
        MatchFlow legacy = MatchFixtures.Started();
        for (int i = 0; i < 4; i++)
        {
            legacy.PassTurn();
        }

        Assert.Equal(EndReason.AllPassed, legacy.Result!.Reason);
    }
}
