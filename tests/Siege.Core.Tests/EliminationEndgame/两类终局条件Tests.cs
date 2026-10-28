using System.Numerics;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.EliminationEndgame;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/elimination-endgame —— Requirement: 两类终局条件（链接模式；裁决 S4 新增原因"最后战场结算"）。
/// retire-legacy-rules：旧模式的「三类终局条件」随旧模式删除（原 <c>三类终局条件Tests</c> 整文件删，其中仍成立的条目已迁入本类）。
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
        // Scenario：4 人局中 2 人以势力归零出局、1 人弃赛 → 剩余的唯一参赛玩家直接获胜，对局立即结束（原因"只剩一名参赛玩家"，优先于最后战场结算）。
        // open-map：唯一战场上"战场落败"即终局，两名出局者改由势力归零（提光）产生——局面同「只剩一名优先于最后战场结算」。
        MatchFlow match = PlainStarted().AtRound(5, All)
            .Stones(P1, "C3")
            .Stones(P2, "K10")
            .Stones(P0, "D3", "J10");
        match.PlayTurn("C4", "K9");   // P0 一个批次同时提掉 C3 与 K10 → P1、P2 势力归零出局
        Assert.Equal(2, match.ActiveCount);
        Assert.Equal(MatchPhase.InProgress, match.Phase);

        match.Resign(P3);

        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.LastPlayerStanding, match.Result!.Reason);
        Assert.Equal(P0, match.Result.Winners.Single());
        Assert.Equal(StandingGroup.Resigned, match.Result.Of(P3).Group);
        Assert.Equal(4, match.Result.Of(P3).Rank);   // 弃赛者排最后
        // retire-legacy-rules 质检：原「三类终局条件Tests.唯一参赛者获胜」（清单 (j) 合并入本条）的"对局立即结束"后置断言随合并补回。
        Assert.Throws<SiegeRuleException>(() => match.BeginTurn());
    }

    [Fact]
    public void 只剩一名优先于最后战场结算()
    {
        // 规格「两类终局条件」：同一时刻满足多条时，终局原因按"只剩一名参赛玩家 > 最后战场结算"记录（设计文档 §12.4）。
        // retire-legacy-rules 质检补测 Q1（接替已删的旧「只剩一名参赛玩家优先于棋盘填满」）：四人同锁棋盘 0，P0 一个批次提光 P1、P2（势力归零出局），
        // P3 随后在小回合边界弃赛 → 棋盘 0 因减员只剩 P0、当场结算——"只剩一个含在局玩家的战场且已结算"与"只剩一名参赛玩家"在同一次调用里同时成立，原因记只剩一名。
        // 大回合末由战场结算本身终局的路径（「四人同区一次终局」「决赛结算即终局」）原因记最后战场结算，不在本条。
        // 变异验证 M-Q1（质检实跑）：Resign 减员结算后先判最后战场结算（`_pendingEnd = LastBattlefieldSettled`）→ 本条、「唯一参赛者获胜」与「出局判定Tests.同时归零同时出局」红 3。
        MatchFlow match = PlainStarted().AtRound(5, All)
            .Stones(P1, "C3")
            .Stones(P2, "K10")
            .Stones(P0, "D3", "J10");
        match.PlayTurn("C4", "K9");   // P0 一个批次同时提掉 C3 与 K10 → P1、P2 势力归零出局
        Assert.Equal(2, match.ActiveCount);
        Assert.False(match.IsBattlefieldSettled(0));
        Assert.Empty(match.Settlements);

        match.Resign(P3);

        Assert.True(match.IsBattlefieldSettled(0));
        Assert.Single(match.OccupiedBattlefields());
        BattlefieldSettlementRecord settled = Assert.Single(match.Settlements);
        Assert.Equal((0, SettlementReason.Attrition, P0), (settled.Battlefield, settled.Reason, settled.Winner));
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.LastPlayerStanding, match.Result!.Reason);
        Assert.Equal([P0], match.Result.Winners);
        Assert.Equal(5, match.Result.MajorRound);
        Assert.Equal(5, match.MajorRound);
    }

    [Fact]
    public void 构筑期内整轮Pass即终局()
    {
        // Scenario（open-map battlefield「阶段推进」）：第 1 大回合 4 名玩家全部 Pass → 第 1 大回合结束时唯一战场结算，对局终局（最后战场结算），名次按势力与判定链。
        // 原「非最后战场的整轮Pass不终局」（多战场）随 open-map 反向。
        MatchFlow match = Started([0, 0, 3, 2]).PassRound();
        Assert.Single(match.Settlements);
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.LastBattlefieldSettled, match.Result!.Reason);
        Assert.Equal(1, match.Result.MajorRound);
        Assert.Equal(4, match.Result.Standings.Length);
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
    }

    [Fact]
    public void 上限结算即终局()
    {
        // Scenario（cycle-stop）：大回合上限 60，第 60 大回合结束时仍有 3 名在局玩家且本大回合有人落子 → 战场按势力强制结算，对局终止，
        // 终局原因"最后战场结算"（不新增终局原因），结算原因"上限"。对照：上限 0 时同一局面第 60 大回合照常继续（「轮数再多也不结束」的口径不变）。
        // 变异 M-RC1（JudgeAtRoundEnd 去掉第 (c) 条）→ 本条红。
        MatchFlow capped = Started([0, 0, 3, 2], options: Options with { RoundCap = 60 }).PlayRound(P0, P1).PlayRound(P0);
        capped.Resign(P3);
        capped.AtRound(60, [P0, P1, P2]).PlayRound(P2);

        Assert.Equal(MatchPhase.Ended, capped.Phase);
        Assert.Equal(EndReason.LastBattlefieldSettled, capped.Result!.Reason);
        Assert.Equal(SettlementReason.RoundCap, Assert.Single(capped.Settlements).Reason);
        Assert.Equal(1, capped.ActiveCount);
        Assert.Throws<SiegeRuleException>(() => capped.BeginTurn());

        MatchFlow uncapped = Started([0, 0, 3, 2], options: Options with { RoundCap = 0 }).PlayRound(P0, P1).PlayRound(P0);
        uncapped.Resign(P3);
        uncapped.AtRound(60, [P0, P1, P2]).PlayRound(P2);
        Assert.Equal(MatchPhase.InProgress, uncapped.Phase);
        Assert.Equal(61, uncapped.MajorRound);
    }

    [Fact]
    public void 轮数再多也不结束()
    {
        // Scenario：对局进行到第 40 大回合且两类终局条件均未满足 → 对局继续。
        MatchFlow match = Started([0, 0, 3, 2]).PlayRound(P0).AtRound(40, [P0, P1, P2, P3]).PlayRound(P1);
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
}
