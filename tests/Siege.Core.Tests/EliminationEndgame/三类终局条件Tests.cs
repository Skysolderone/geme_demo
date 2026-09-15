using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.EliminationEndgame;

/// <summary>规格：elimination-endgame —— Requirement: 三类终局条件</summary>
public class 三类终局条件Tests
{
    [Fact]
    public void 唯一参赛者获胜()
    {
        // 设计文档 §12.3 条件 1：4 人局 2 人出局、1 人弃赛 → 剩余唯一参赛玩家直接获胜，对局立即结束。
        // 变异验证 M-E13：CheckEndConditions 的 `active <= 1` 改为 `== 0` → 红 1（本测试）。
        MatchFlow match = MatchFixtures.Started().AtRound(5, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3])
            .Stones(MatchFixtures.P3, "H8");
        match.Debug.SeedHand(MatchFixtures.P1);
        match.Debug.SeedHand(MatchFixtures.P2);

        match.PlayTurn("E5");   // P0 结算 → P1、P2 出局
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
        // 设计文档 §12.3 条件 2：某大回合中全部 3 名参赛玩家依次都确认 0 落子 → 对局在该大回合结束时终止，按当前势力值排名。
        // 变异验证 M-E14：`_passStreak >= active` 改为 `> active` → 红 1（本测试）。
        MatchFlow match = MatchFixtures.Started(options: MatchFixtures.DominanceOff).AtRound(5, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3])
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
        // 设计文档 §12.3 条件 3：一次结算后棋盘不存在任何可落子的空格 → 立即结束，按当前势力值排名。
        // 注：按围棋规则，最后一个空格的落子要么无气（自杀手被拒）要么提子腾出空格，条件 3 在合法落子路径上不可达；
        // 这里用测试接缝直接填满盘面，再由一次 Pass 结算触发检查。
        // 变异验证 M-E15：CheckEndConditions 删除 HasPlayableEmptyCell 分支 → 红 1（本测试）。
        MatchFlow match = MatchFixtures.Started().AtRound(5, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3]);
        foreach (Coord c in match.Board.AllCoords())
        {
            match.Board.Place(c, c.X < 4 ? MatchFixtures.P0 : MatchFixtures.P1, PieceType.Basic);
        }

        match.Debug.Recalculate();
        Assert.False(match.Board.HasPlayableEmptyCell());

        match.PassTurn();
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.BoardFull, match.Result!.Reason);
        Assert.Equal(MatchFixtures.P1, match.Result.Winners.Single());   // 45 子 > 36 子
    }

    [Fact]
    public void Pass计数跨回合重置()
    {
        // 设计文档 §12.3 / design.md D2：前两名玩家 Pass、第三名落子 1 枚 → 计数重置，对局继续。
        // 变异验证 M-E16：OnCheckEndConditions 不在落子时清零 → 红 1（本测试：P3 Pass 后计数为 3 而非 1）。
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

    /// <summary>棋盘填满摆盘：P0 占 A–D 列（36 子）、P1 占其余（45 子），P2、P3 盘面为空；P1 满足碾压式。</summary>
    private static MatchFlow FullBoard(MatchOptions options)
    {
        MatchFlow match = MatchFixtures.Started(options: options).AtRound(5, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3]);
        foreach (Coord c in match.Board.AllCoords())
        {
            match.Board.Place(c, c.X < 4 ? MatchFixtures.P0 : MatchFixtures.P1, PieceType.Basic);
        }

        match.Debug.Recalculate();
        Assert.False(match.Board.HasPlayableEmptyCell());
        return match;
    }

    [Fact]
    public void 碾压优先于棋盘填满与整轮Pass()
    {
        // dominance-victory 规格 / 裁决 4、9：某次结算后碾压成立，且同一时刻棋盘填满（并凑成整轮 Pass）→ 原因记势力碾压，候选获胜。
        // 候选状态用测试接缝摆出"P1 为候选、只差 P0 回应"，P0 的 Pass 同时满足三条。
        // 变异验证 M-DV8：CheckEndConditions 把碾压分支移到棋盘填满之后 → 红 1（本测试）。
        MatchFlow match = FullBoard(MatchFixtures.DominanceOn);
        match.Debug.SetDominance(MatchFixtures.P1, MatchFixtures.P0);
        match.Debug.SetPassStreak(3);
        match.PassTurn();

        Assert.Equal(4, match.PassStreak);                                   // 整轮 Pass 同时成立
        Assert.False(match.Board.HasPlayableEmptyCell());                    // 棋盘填满同时成立
        Assert.True(match.Dominance!.Pending.IsEmpty);                       // 碾压同时成立
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.PowerDominance, match.Result!.Reason);
        Assert.Equal([MatchFixtures.P1], match.Result.Winners);
    }

    [Fact]
    public void 棋盘填满优先于整轮Pass()
    {
        // 裁决 9：同一时刻棋盘填满与整轮 Pass → 原因记棋盘填满（沿用既有代码顺序，规格据此修正）。碾压关闭以隔离这一对。
        // 变异验证 M-DV16：棋盘填满与整轮 Pass 两个分支对调 → 红 1（本测试）。
        MatchFlow match = FullBoard(MatchFixtures.DominanceOff);
        match.Debug.SetPassStreak(3);
        match.PassTurn();

        Assert.Equal(4, match.PassStreak);
        Assert.Equal(EndReason.BoardFull, match.Result!.Reason);
    }

    [Fact]
    public void 只剩一人优先于碾压()
    {
        // 裁决 4：只剩一名参赛玩家 > 势力碾压。P0 为候选、名单只剩 P3；P3 弃赛使名单清空（碾压成立）且只剩 P0 一人。
        // 变异验证 M-DV9：碾压分支移到只剩一人之前 → 红 2（本测试、既有 已结束对局与终局结果可恢复）。
        MatchFlow match = MatchFixtures.Started(options: MatchFixtures.DominanceOn).AtRound(5, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3])
            .Stones(MatchFixtures.P3, "H8");
        match.Debug.SeedHand(MatchFixtures.P1);
        match.Debug.SeedHand(MatchFixtures.P2);
        match.PlayTurn("E5");   // P0 结算 → P1、P2 出局
        Assert.Equal(2, match.ActiveCount);

        match.Debug.SetDominance(MatchFixtures.P0, MatchFixtures.P3);
        match.Resign(MatchFixtures.P3);

        Assert.Equal(MatchFixtures.P0, match.Dominance!.Candidate);           // 碾压同时成立：候选仍在、名单已空
        Assert.True(match.Dominance.Pending.IsEmpty);
        Assert.Equal(EndReason.LastPlayerStanding, match.Result!.Reason);
        Assert.Equal([MatchFixtures.P0], match.Result.Winners);
    }

    [Fact]
    public void 碾压成立优先于达大回合上限()
    {
        // 裁决 4：势力碾压 > 达大回合上限。第 15 大回合（上限 15）最后一名行动者 P3 的 Pass 使碾压成立 → 原因记势力碾压，不走到上限检查。
        // 变异验证：M-DV13（名单不移除刚行动者）→ 红（本测试）；上限检查结构上位于 EndMajorRound、晚于 CompleteTurn 收尾，无更直接的单行变异。
        MatchFlow match = MatchFixtures.Started(options: MatchFixtures.DominanceOn).AtRound(15, [MatchFixtures.P3]);
        foreach (string cell in new[] { "A1", "B1", "A2" })
        {
            match.Board.Place(Coord.Parse(cell), MatchFixtures.P0, PieceType.Fortress);
        }

        match.Stones(MatchFixtures.P3, "H8");
        match.Debug.SetDominance(MatchFixtures.P0, MatchFixtures.P3);
        Assert.Equal(15, match.MaxMajorRounds);
        match.PassTurn();

        Assert.Equal(EndReason.PowerDominance, match.Result!.Reason);
        Assert.Equal(15, match.Result.MajorRound);
        Assert.Equal(15, match.MajorRound);
        Assert.Equal([MatchFixtures.P0], match.Result.Winners);
    }
}
