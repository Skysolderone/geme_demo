using System.Numerics;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.EliminationEndgame;

/// <summary>规格：elimination-endgame —— Requirement: 主动弃赛</summary>
/// <remarks>
/// retire-legacy-rules 第 1 组：载体由 9×9 旧模式夹具换为平坦 8×8 链接档图（<c>LinkModeFixtures.PlainStarted</c>，四人同锁棋盘 0），
/// 坐标平移到棋盘 0：整体 (+2, +2)；用到 9×9 东 / 北边角的用例改为 (+1, +1) 或四角各贴棋盘 0 的四角（split），逐条见迁移清单第 6 节。
/// </remarks>
public class 主动弃赛Tests
{
    [Fact]
    public void 弃赛后停止行动()
    {
        // 设计文档 §12.2：D 在第 6 大回合弃赛 → 第 7 大回合起不再获得小回合、不再征募、不进先手值明细（relic-redesign W1：先锋先手修正随旧信物删除）。
        // 变异验证 M-E8：Resign 不改 Status → 红 1（本测试）。
        MatchFlow match = LinkModeFixtures.PlainStarted()
            .AtRound(6, [TestMaps.P0, TestMaps.P1, TestMaps.P2, TestMaps.P3])
            .Stones(TestMaps.P3, "G7");

        match.Resign(TestMaps.P3);
        Assert.Equal(PlayerStatus.Resigned, match.StateOf(TestMaps.P3).Status);
        Assert.Equal(6, match.StateOf(TestMaps.P3).ResignedInMajorRound);
        Assert.False(match.Hands.PublicView(TestMaps.P3).IsActing);

        match.PlayTurn("D4");
        match.PlayTurn("K4");
        match.PlayTurn("D10");
        Assert.Equal(7, match.MajorRound);
        Assert.DoesNotContain(TestMaps.P3, match.ActionOrder);
        Assert.DoesNotContain(TestMaps.P3, match.InitiativeReports.Single().Entries.Select(e => e.Player));
        Assert.Equal(0, match.Events.Count(e => e.Kind == FlowEventKind.TurnStarted && e.Player == TestMaps.P3));
    }

    [Fact]
    public void 保护期内允许弃赛()
    {
        // 裁决记录 2：第 2 大回合主动弃赛 → 接受；遗留棋子照常产生覆盖与势力；不再获得小回合。
        // 变异验证 M-E10：Resign 在 MajorRound <= 3 时抛出 → 红 1（本测试）。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(2, [TestMaps.P0, TestMaps.P1, TestMaps.P2, TestMaps.P3])
            .Stones(TestMaps.P3, "J9");
        match.Resign(TestMaps.P3);

        Assert.Equal(PlayerStatus.Resigned, match.StateOf(TestMaps.P3).Status);
        PlayerPower power = match.Scoreboard.Latest!.Of(TestMaps.P3);
        Assert.Equal(PlayerStatus.Resigned, power.Status);
        Assert.Equal(5, power.Total);   // 段 A 重算：原 1 → 5 = 军势 1 + J9 四邻独占 4（J9 不贴棋盘 0 的边）
        Assert.Contains(TestMaps.At("J9"), match.Board.GroupsOf(TestMaps.P3).Single().Stones);

        match.PlayTurn("C3");
        match.PlayTurn("J3");
        match.PlayTurn("C9");
        Assert.Equal(3, match.MajorRound);
        Assert.DoesNotContain(TestMaps.P3, match.ActionOrder);
    }

    [Fact]
    public void 遗留棋子继续生效()
    {
        // 设计文档 §12.2：弃赛者 D 与参赛者 A 的棋子同时覆盖某空格 → 争议格，A 不获得该格的领地分。
        // 本测试保留流程层名册接线这条腿；覆盖判定那条腿见 coverage-territory「弃赛者遗留棋子制造争议」。
        // 变异验证 M-E11：Roster 把 Resigned 报为 Eliminated 也不影响；真正的守门是计分层不过滤——把 RecalculateDerived 的名册过滤掉弃赛者 → 抛出 → 红 1（本测试）。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5, [TestMaps.P0, TestMaps.P1, TestMaps.P2, TestMaps.P3])
            .Stones(TestMaps.P3, "G7")
            .Stones(TestMaps.P0, "G9");
        match.Resign(TestMaps.P3);

        Assert.Equal(OwnershipKind.Contested, match.Scoreboard.Latest!.Coverage.OwnershipOf(TestMaps.At("G8")).Kind);
        Assert.DoesNotContain(TestMaps.At("G8"), match.Scoreboard.Latest.Of(TestMaps.P0).ExclusiveCells);
        Assert.Equal(3, match.Scoreboard.Latest.Of(TestMaps.P0).ExclusiveCells.Length);   // G9 的 4 邻格中 G8 争议
        Assert.Equal(4, match.Scoreboard.Latest.Of(TestMaps.P0).Total);   // 段 A 重算：原 1 → 4 = 军势 1 + 独占 3（G8 争议不计）
    }

    [Fact]
    public void 遗留棋子可被围杀()
    {
        // 设计文档 §12.2：参赛玩家 B 使弃赛者 D 的一条棋串无气 → 照常提走。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5, [TestMaps.P1, TestMaps.P0, TestMaps.P2, TestMaps.P3])
            .Stones(TestMaps.P3, "C3");
        match.Resign(TestMaps.P3);

        match.PlayTurn("D3", "C4");
        Assert.Null(match.Board[TestMaps.At("C3")].Occupant);
        Assert.Equal(0, match.StoneCount(TestMaps.P3));
    }

    [Fact]
    public void 弃赛快照可记录()
    {
        // 裁决记录 5：弃赛时记录盘面序列化（含信物层）、手牌两段账与效果快照（relic-redesign W1：控制信物列表随旧信物删除）。
        // 变异验证 M-E12：Resign 在 Hands.Resign 之后才取 PrivateView → 本轮新增被撤销、两段账失真 → 红 1（本测试的 Gained 断言）。
        MatchFlow match = LinkModeFixtures.PlainStarted()
            .AtRound(5, [TestMaps.P3, TestMaps.P0, TestMaps.P1, TestMaps.P2])
            .Stones(TestMaps.P3, "J9", "K10");
        BigInteger powerBefore = match.Scoreboard.Latest!.Of(TestMaps.P3).Total;

        // P3 在自己的征募阶段中途弃赛：本轮新增尚未提交，快照应保留两段账原样
        match.BeginTurn();
        Siege.Core.Recruit.PlayerHandAccess hand = match.CurrentHand();
        match.EnterRecruit();
        hand.Pick(hand.Panel().Candidates.First(c => c.IsSelectable).Index);
        string boardBefore = match.Board.Serialize();
        match.Resign(TestMaps.P3);

        ResignationSnapshot snapshot = match.Resignations.Single();
        Assert.Equal(TestMaps.P3, snapshot.Player);
        Assert.Equal(5, snapshot.MajorRound);
        Assert.Equal(boardBefore, snapshot.Board);
        Assert.Equal(50, snapshot.Hand.EntryOf(PieceType.Basic).Carried);   // 回合前基数原样
        Assert.Equal(1, snapshot.Hand.PendingGained);                        // 本轮新增 1 枚仍在账上，未被弃赛撤销
        Assert.Equal(51, snapshot.Hand.TotalCount);
        Assert.Equal(TestMaps.P3, snapshot.Effects.Player);
        Assert.Equal(4, snapshot.Effects.DeployLimit);   // 第 5 大回合基础 4（growth-pass-1；relic-redesign W1：原"控制军令 +1"随旧信物删除）
        Assert.Equal(powerBefore, snapshot.Power);
        Assert.Equal(powerBefore, match.StateOf(TestMaps.P3).PowerAtResign);
        Assert.Equal(1, snapshot.RankAtResign);   // carry-in-out：快照含弃赛时势力名次（盘面上只有 P3 有子，第 1）

        Assert.Equal(TurnStage.Idle, match.Stage);
        Assert.Equal(TestMaps.P0, match.CurrentPlayer);
    }

    /// <summary>
    /// 第 6 大回合、顺序 P0 → P3 的局面：P2 在 C10 的单子被 P0 落 C9 / D10 提走而出局（曾建立正势力、势力归零），P1 与 P3 各有一个角上单子。
    /// 返回时 P0 的小回合已结束，停在小回合边界。
    /// </summary>
    private static MatchFlow WithEliminatedC()
    {
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(6, TestMaps.All)
            .Stones(TestMaps.P2, "C10")
            .Stones(TestMaps.P1, "K3")
            .Stones(TestMaps.P3, "K10");
        match.PlayTurn("C9", "D10");
        Assert.Equal(PlayerStatus.Eliminated, match.StateOf(TestMaps.P2).Status);   // 前提：C 已出局
        return match;
    }

    private static BigInteger PowerOf(MatchFlow match, PlayerId player) => match.Scoreboard.Latest!.Of(player).Total;

    [Fact]
    public void 弃赛时势力名次()
    {
        // 规格 Scenario：C 已出局，D 以总势力 30 弃赛，A = 50、B = 30 → D 的弃赛时势力名次为第 2（只有 A 严格更高，与 B 并列共享）。
        // 数值不同、结构相同的真实局面：A（P0）严格高于 D（P3），B（P1）与 D 相等，C（P2）已出局。算式本身的算例见 carry-in-out「弃赛结算」纯函数测试。
        MatchFlow match = WithEliminatedC();
        Assert.True(PowerOf(match, TestMaps.P0) > PowerOf(match, TestMaps.P3), "前提：A 严格高于 D");
        Assert.Equal(PowerOf(match, TestMaps.P1), PowerOf(match, TestMaps.P3));

        match.Resign(TestMaps.P3);

        Assert.Equal(2, match.Resignations.Single().RankAtResign);
    }

    [Fact]
    public void 此前已弃赛者计入弃赛名次()
    {
        // 规格正文：弃赛时名次在未出局玩家中排，此前已弃赛者也计入。B（P1）先以 3 子弃赛、之后 D（P3）以 1 子弃赛：
        // B 此刻势力仍高于 D → D 第 2（只在参赛者中排会得出第 1：P0 / P2 盘面无子，势力 0）。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(6, TestMaps.All)
            .Stones(TestMaps.P1, "H3", "J3", "K3")
            .Stones(TestMaps.P3, "K10");
        match.Resign(TestMaps.P1);
        Assert.True(PowerOf(match, TestMaps.P1) > PowerOf(match, TestMaps.P3), "前提：已弃赛的 B 此刻势力高于 D");

        match.Resign(TestMaps.P3);

        Assert.Equal(1, match.Resignations[0].RankAtResign);
        Assert.Equal(2, match.Resignations[1].RankAtResign);
    }

    [Fact]
    public void 弃赛名次不影响最终名次()
    {
        // 规格 Scenario：D 以弃赛时势力名次第 1 弃赛，其余三名玩家都完赛 → 最终名次中 D 为第 4，排在三名完赛者之后。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5, TestMaps.All)
            .Stones(TestMaps.P3, "H10", "J10", "K10", "K9")
            .Stones(TestMaps.P0, "C3");
        match.Resign(TestMaps.P3);
        Assert.Equal(1, match.Resignations.Single().RankAtResign);

        match.PassTurn();
        match.PassTurn();
        match.PassTurn();   // 三名参赛者整轮 Pass → 终局

        Assert.Equal(MatchPhase.Ended, match.Phase);
        Standing d = match.Result!.Of(TestMaps.P3);
        Assert.Equal(StandingGroup.Resigned, d.Group);
        Assert.Equal(4, d.Rank);
        Assert.All(match.Result.Standings.Where(s => s.Player != TestMaps.P3), s => Assert.True(s.Rank < d.Rank));
    }

    [Fact]
    public void 弃赛名次随存档往返()
    {
        // 规格 Scenario：保存一局已有玩家弃赛的对局并恢复 → 该弃赛者的弃赛时势力名次与保存时相同。
        // 样本名次为 2（非空、非 1），往返同时证伪"写入路径漏写"（缺字段恢复为空）。
        MatchFlow match = WithEliminatedC();
        match.Resign(TestMaps.P3);
        Assert.Equal(2, match.Resignations.Single().RankAtResign);
        string json = match.Serialize();

        MatchFlow restored = MatchFlow.RestoreUnvalidated(match.Board.BaseMap, json);

        Assert.Equal(match.Resignations.Single().RankAtResign, restored.Resignations.Single().RankAtResign);
        Assert.Equal(json, restored.Serialize());
    }
}
