using System.Numerics;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.EliminationEndgame;

/// <summary>
/// 规格：elimination-endgame —— Requirement: 三类终局条件（restore-go-core-rules 裁决 #4）。
/// 只剩一名参赛玩家 &gt; 棋盘填满 &gt; 整轮 Pass；MUST NOT 有大回合上限，MUST NOT 因势力领先幅度提前结束。
/// </summary>
public class 三类终局条件Tests
{
    [Fact]
    public void 唯一参赛者获胜()
    {
        // Scenario：4 人局中 2 人出局、1 人弃赛 → 剩余的唯一参赛玩家直接获胜，对局立即结束。
        // 出局改走新判据：P1（A1）与 P2（J9）先有子（标记置位），被 P0 一个批次同时提光 → 同时出局。
        MatchFlow match = MatchFixtures.Started().AtRound(5, MatchFixtures.All)
            .Stones(MatchFixtures.P1, "A1")
            .Stones(MatchFixtures.P2, "J9")
            .Stones(MatchFixtures.P0, "B1", "H9")
            .Stones(MatchFixtures.P3, "E5");

        match.PlayTurn("A2", "J8");   // P0 结算 → P1、P2 出局
        Assert.Equal(2, match.ActiveCount);
        Assert.Equal(MatchPhase.InProgress, match.Phase);

        match.Resign(MatchFixtures.P3);
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.LastPlayerStanding, match.Result!.Reason);
        Assert.Equal([MatchFixtures.P0], match.Result.Winners);
        Assert.Equal(StandingGroup.Resigned, match.Result.Of(MatchFixtures.P3).Group);
        Assert.Equal(2, match.Result.Of(MatchFixtures.P3).Rank);
        Assert.Throws<SiegeRuleException>(() => match.BeginTurn());
    }

    [Fact]
    public void 整轮Pass()
    {
        // Scenario：某大回合中全部 3 名参赛玩家依次都确认 0 落子 → 对局在该大回合结束时终止，按当前势力值排名。
        MatchFlow match = MatchFixtures.Started().AtRound(5, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3])
            .Stones(MatchFixtures.P0, "E5", "B2")
            .Stones(MatchFixtures.P1, "H2")
            .Stones(MatchFixtures.P2, "B8");
        match.Resign(MatchFixtures.P3);

        match.PassTurn();
        match.PassTurn();
        Assert.Equal(2, match.PassStreak);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        match.PassTurn();
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.AllPassed, match.Result!.Reason);
        Assert.Equal(5, match.Result.MajorRound);
        Assert.Equal(new[] { MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3 }, match.Result.Standings.Select(s => s.Player));
        Assert.Equal(new[] { 1, 2, 2, 4 }, match.Result.Standings.Select(s => s.Rank));   // P1 与 P2 势力同为 5 且四项全同 → 并列
    }

    [Fact]
    public void 棋盘填满()
    {
        // Scenario：一次结算后棋盘不存在任何可落子的空格 → 立即结束，按当前势力值排名。
        // 注：按围棋规则，最后一个空格的落子要么无气（自杀手被拒）要么提子腾出空格，此条件在合法落子路径上不可达；
        // 这里用测试接缝直接填满盘面，再由一次 Pass 结算触发检查。P2、P3 从未落子（标记未置位）→ 该次检查里不出局。
        MatchFlow match = FullBoard();

        match.PassTurn();
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.BoardFull, match.Result!.Reason);
        Assert.Equal(MatchFixtures.P1, match.Result.Winners.Single());   // 45 子 > 36 子
        Assert.Equal(StandingGroup.Finisher, match.Result.Of(MatchFixtures.P2).Group);
    }

    [Fact]
    public void Pass计数跨回合重置()
    {
        // Scenario：某大回合中前两名玩家 Pass、第三名玩家落子 1 枚 → 整轮 Pass 的计数被重置，对局继续。
        MatchFlow match = MatchFixtures.Started().AtRound(5, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3]);
        match.PassTurn();
        match.PassTurn();
        Assert.Equal(2, match.PassStreak);
        match.PlayTurn("E5");
        Assert.Equal(0, match.PassStreak);
        match.PassTurn();
        Assert.Equal(1, match.PassStreak);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Equal(6, match.MajorRound);
    }

    [Fact]
    public void 势力悬殊不提前结束()
    {
        // Scenario：第 9 大回合参赛玩家势力为 A=210、B=80、C=70、D=55（A ≥ 其余之和 205），且三类终局条件均未满足 → 对局继续。
        // 规格数值只是示意"领先者势力不小于其余之和"（旧势力碾压的成立式）。这里摆出同一关系：P0 占满 A、B 两列，其余各一子，
        // 断言 P0 ≥ 其余之和后跑完整个第 9 大回合，对局进入第 10 大回合。旧实现（碾压起始 7）在此会以 PowerDominance 终局。
        MatchFlow match = MatchFixtures.Started().AtRound(9, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3])
            .Stones(MatchFixtures.P0, "A1", "A2", "A3", "A4", "A5", "A6", "A7", "A8", "A9", "B1", "B2", "B3", "B4", "B5", "B6", "B7", "B8", "B9")
            .Stones(MatchFixtures.P1, "J1")
            .Stones(MatchFixtures.P2, "J9")
            .Stones(MatchFixtures.P3, "E5");
        PowerSnapshot power = match.Scoreboard.Latest!;
        BigInteger others = power.Of(MatchFixtures.P1).Total + power.Of(MatchFixtures.P2).Total + power.Of(MatchFixtures.P3).Total;
        Assert.True(power.Of(MatchFixtures.P0).Total >= others, $"P0 {power.Of(MatchFixtures.P0).Total} 应 ≥ 其余之和 {others}");

        match.PlayTurn("D5");   // P0
        match.PlayTurn("H1");   // P1
        match.PlayTurn("H9");   // P2
        match.PlayTurn("F5");   // P3 → 第 9 大回合结束

        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Null(match.Result);
        Assert.Equal(10, match.MajorRound);
    }

    [Fact]
    public void 轮数再多也不结束()
    {
        // Scenario：对局进行到第 40 大回合且三类终局条件均未满足 → 对局继续。
        // 旧实现（大回合上限 15）在第 40 大回合结束时会以 MajorRoundLimit 终局；本局跑完整个第 40 大回合后进入第 41 大回合。
        MatchFlow match = MatchFixtures.Started().AtRound(40, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3]);

        match.PlayTurn("B2");   // P0
        match.PlayTurn("H2");   // P1
        match.PlayTurn("B8");   // P2
        match.PlayTurn("H8");   // P3 → 第 40 大回合结束

        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Null(match.Result);
        Assert.Equal(41, match.MajorRound);
    }

    [Fact]
    public void 棋盘填满优先于整轮Pass()
    {
        // 优先级：同一时刻棋盘填满与整轮 Pass → 原因记棋盘填满。
        MatchFlow match = FullBoard();
        match.Debug.SetPassStreak(3);
        match.PassTurn();

        Assert.Equal(4, match.PassStreak);
        Assert.Equal(EndReason.BoardFull, match.Result!.Reason);
    }

    [Fact]
    public void 只剩一名参赛玩家优先于棋盘填满()
    {
        // 优先级：只剩一名参赛玩家 &gt; 棋盘填满。先让 P2、P3 弃赛（此时盘面未满，对局继续），再用测试接缝填满盘面（不触发检查），
        // 最后 P1 弃赛 → 同一检查点上"只剩一人"与"棋盘填满"同时成立 → 原因记 LastPlayerStanding。
        MatchFlow match = MatchFixtures.Started().AtRound(5, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3]);
        match.Resign(MatchFixtures.P2);
        match.Resign(MatchFixtures.P3);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Fill(match);

        match.Resign(MatchFixtures.P1);

        Assert.False(match.Board.HasPlayableEmptyCell());
        Assert.Equal(EndReason.LastPlayerStanding, match.Result!.Reason);
        Assert.Equal([MatchFixtures.P0], match.Result.Winners);
    }

    /// <summary>棋盘填满摆盘：P0 占 A–D 列（36 子）、P1 占其余（45 子），P2、P3 盘面为空。</summary>
    private static MatchFlow FullBoard()
    {
        MatchFlow match = MatchFixtures.Started().AtRound(5, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3]);
        Fill(match);
        Assert.False(match.Board.HasPlayableEmptyCell());
        return match;
    }

    private static void Fill(MatchFlow match)
    {
        foreach (Coord c in match.Board.AllCoords())
        {
            if (match.Board[c].IsPlayableEmpty)
            {
                match.Board.Place(c, c.X < 4 ? MatchFixtures.P0 : MatchFixtures.P1, PieceType.Basic);
            }
        }

        match.Debug.Recalculate();
    }
}
