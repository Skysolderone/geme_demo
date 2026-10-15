using System.Numerics;
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
    public void 势力悬殊不提前结束()
    {
        // Scenario：第 9 大回合参赛玩家势力为 A=210、B=80、C=70、D=55（A ≥ 其余之和 205），且终局条件均未满足 → 对局继续。
        // 规格数值只是示意"领先者势力不小于其余之和"（旧势力碾压的成立式）。这里摆出同一关系后跑完整个第 9 大回合，对局进入第 10 大回合。
        // retire-legacy-rules 1.5：由「三类终局条件Tests」迁来（原文件整删）。载体为平坦 8×8 链接档图（四人同锁棋盘 0，本大回合有人落子 → 不结算）；
        // 原 9×9 上 P0 占满 A、B 两列（18 子）、P1 / P2 各占东侧一角、P3 居中，改为 P0 占满棋盘 0 西侧 C、D 两列（16 子）、P1 / P2 占东侧两角 K3 / K10、P3 居中 G7，
        // 关系由前提断言钉住。旧实现（碾压起始 7）在此会以 PowerDominance 终局。
        MatchFlow match = PlainStarted().AtRound(9, [P0, P1, P2, P3])
            .Stones(P0, "C3", "C4", "C5", "C6", "C7", "C8", "C9", "C10", "D3", "D4", "D5", "D6", "D7", "D8", "D9", "D10")
            .Stones(P1, "K3")
            .Stones(P2, "K10")
            .Stones(P3, "G7");
        PowerSnapshot power = match.Scoreboard.Latest!;
        BigInteger others = power.Of(P1).Total + power.Of(P2).Total + power.Of(P3).Total;
        Assert.True(power.Of(P0).Total >= others, $"P0 {power.Of(P0).Total} 应 ≥ 其余之和 {others}");

        match.PlayTurn("F7");   // P0
        match.PlayTurn("J3");   // P1
        match.PlayTurn("J10");  // P2
        match.PlayTurn("H7");   // P3 → 第 9 大回合结束

        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Null(match.Result);
        Assert.Equal(10, match.MajorRound);
    }

    [Fact]
    public void Pass计数跨回合重置()
    {
        // Scenario：某大回合中前两名玩家 Pass、第三名玩家落子 1 枚 → 连续 Pass 的计数被重置，对局继续。
        // retire-legacy-rules 1.5：由「三类终局条件Tests」迁来（原文件整删）。全局连续 Pass 计数不再决定终局，但仍写进公开快照、存档、日志与终端（design D4 保留），
        // 这里只断言计数的增减与清零、对局继续。载体为平坦 8×8 链接档图（四人同锁棋盘 0），原 9×9 的 E5 平移为 G7。
        MatchFlow match = PlainStarted().AtRound(5, [P0, P1, P2, P3]);
        match.PassTurn();
        match.PassTurn();
        Assert.Equal(2, match.PassStreak);
        match.PlayTurn("G7");
        Assert.Equal(0, match.PassStreak);
        match.PassTurn();
        Assert.Equal(1, match.PassStreak);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Equal(6, match.MajorRound);
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
