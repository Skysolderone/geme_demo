using System.Numerics;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Relics;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.EliminationEndgame;

/// <summary>规格：elimination-endgame —— Requirement: 主动弃赛</summary>
public class 主动弃赛Tests
{
    [Fact]
    public void 弃赛后停止行动()
    {
        // 设计文档 §12.2：D 在第 6 大回合弃赛 → 第 7 大回合起不再获得小回合、不再征募，其先锋信物不再提供先手修正。
        // 变异验证 M-E8：Resign 不改 Status → 红 1（本测试）；M-E9：EndMajorRound 的 bonuses 用无名册重载 → 红 1（本测试：P3 出现在修正表）。
        MatchFlow match = MatchFixtures.Started(relics: [("E5", RelicFixtures.Vanguard())])
            .AtRound(6, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3])
            .Stones(MatchFixtures.P3, "E5");
        Assert.Equal(1, match.Relics.ReadInitiativeBonuses(match.Board, match.Roster)[MatchFixtures.P3]);

        match.Resign(MatchFixtures.P3);
        Assert.Equal(PlayerStatus.Resigned, match.StateOf(MatchFixtures.P3).Status);
        Assert.Equal(6, match.StateOf(MatchFixtures.P3).ResignedInMajorRound);
        Assert.False(match.Hands.PublicView(MatchFixtures.P3).IsActing);
        Assert.Equal(RelicControlKind.Blocked, match.Relics.ControlOf(TestMaps.At("E5")).Kind);
        Assert.False(match.Relics.ReadInitiativeBonuses(match.Board, match.Roster).ContainsKey(MatchFixtures.P3));

        match.PlayTurn("B2");
        match.PlayTurn("H2");
        match.PlayTurn("B8");
        Assert.Equal(7, match.MajorRound);
        Assert.DoesNotContain(MatchFixtures.P3, match.ActionOrder);
        Assert.DoesNotContain(MatchFixtures.P3, match.InitiativeReports.Single().Entries.Select(e => e.Player));
        Assert.Equal(0, match.Events.Count(e => e.Kind == FlowEventKind.TurnStarted && e.Player == MatchFixtures.P3));
    }

    [Fact]
    public void 保护期内允许弃赛()
    {
        // 裁决记录 2：第 2 大回合主动弃赛 → 接受；遗留棋子照常产生覆盖与势力；不再获得小回合。
        // 变异验证 M-E10：Resign 在 MajorRound <= 3 时抛出 → 红 1（本测试）。
        MatchFlow match = MatchFixtures.Started().AtRound(2, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3])
            .Stones(MatchFixtures.P3, "H8");
        match.Resign(MatchFixtures.P3);

        Assert.Equal(PlayerStatus.Resigned, match.StateOf(MatchFixtures.P3).Status);
        PlayerPower power = match.Scoreboard.Latest!.Of(MatchFixtures.P3);
        Assert.Equal(PlayerStatus.Resigned, power.Status);
        Assert.Equal(5, power.Total);   // 段 A 重算：原 1 → 5 = 军势 1 + H8 四邻独占 4（9×9 图，H8 不贴边）
        Assert.Contains(TestMaps.At("H8"), match.Board.GroupsOf(MatchFixtures.P3).Single().Stones);

        match.PlayTurn("B2");
        match.PlayTurn("H2");
        match.PlayTurn("B8");
        Assert.Equal(3, match.MajorRound);
        Assert.DoesNotContain(MatchFixtures.P3, match.ActionOrder);
    }

    [Fact]
    public void 遗留棋子继续生效()
    {
        // 设计文档 §12.2：弃赛者 D 与参赛者 A 的棋子同时覆盖某空格 → 争议格，A 不获得该格的领地分。
        // 本测试保留流程层名册接线这条腿；覆盖判定那条腿见 coverage-territory「弃赛者遗留棋子制造争议」。
        // 变异验证 M-E11：Roster 把 Resigned 报为 Eliminated 也不影响；真正的守门是计分层不过滤——把 RecalculateDerived 的名册过滤掉弃赛者 → 抛出 → 红 1（本测试）。
        MatchFlow match = MatchFixtures.Started().AtRound(5, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3])
            .Stones(MatchFixtures.P3, "E5")
            .Stones(MatchFixtures.P0, "E7");
        match.Resign(MatchFixtures.P3);

        Assert.Equal(OwnershipKind.Contested, match.Scoreboard.Latest!.Coverage.OwnershipOf(TestMaps.At("E6")).Kind);
        Assert.DoesNotContain(TestMaps.At("E6"), match.Scoreboard.Latest.Of(MatchFixtures.P0).ExclusiveCells);
        Assert.Equal(3, match.Scoreboard.Latest.Of(MatchFixtures.P0).ExclusiveCells.Length);   // E7 的 4 邻格中 E6 争议
        Assert.Equal(4, match.Scoreboard.Latest.Of(MatchFixtures.P0).Total);   // 段 A 重算：原 1 → 4 = 军势 1 + 独占 3（E6 争议不计）
    }

    [Fact]
    public void 遗留棋子可被围杀()
    {
        // 设计文档 §12.2：参赛玩家 B 使弃赛者 D 的一条棋串无气 → 照常提走。
        MatchFlow match = MatchFixtures.Started().AtRound(5, [MatchFixtures.P1, MatchFixtures.P0, MatchFixtures.P2, MatchFixtures.P3])
            .Stones(MatchFixtures.P3, "A1");
        match.Resign(MatchFixtures.P3);

        match.PlayTurn("B1", "A2");
        Assert.Null(match.Board[TestMaps.At("A1")].Occupant);
        Assert.Equal(0, match.StoneCount(MatchFixtures.P3));
    }

    [Fact]
    public void 弃赛快照可记录()
    {
        // 裁决记录 5：弃赛时记录盘面序列化、手牌两段账、效果快照与控制信物列表。
        // 变异验证 M-E12：Resign 在 Hands.Resign 之后才取 PrivateView → 本轮新增被撤销、两段账失真 → 红 1（本测试的 Gained 断言）。
        MatchFlow match = MatchFixtures.Started(relics: [("H8", RelicFixtures.Command())])
            .AtRound(5, [MatchFixtures.P3, MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2])
            .Stones(MatchFixtures.P3, "H8", "J9");
        BigInteger powerBefore = match.Scoreboard.Latest!.Of(MatchFixtures.P3).Total;

        // P3 在自己的征募阶段中途弃赛：本轮新增尚未提交，快照应保留两段账原样
        match.BeginTurn();
        Siege.Core.Recruit.PlayerHandAccess hand = match.CurrentHand();
        match.EnterRecruit();
        hand.Pick(hand.Panel().Candidates.First(c => c.IsSelectable).Index);
        string boardBefore = match.Board.Serialize();
        match.Resign(MatchFixtures.P3);

        ResignationSnapshot snapshot = match.Resignations.Single();
        Assert.Equal(MatchFixtures.P3, snapshot.Player);
        Assert.Equal(5, snapshot.MajorRound);
        Assert.Equal(boardBefore, snapshot.Board);
        Assert.Equal(50, snapshot.Hand.EntryOf(PieceType.Basic).Carried);   // 回合前基数原样
        Assert.Equal(1, snapshot.Hand.PendingGained);                        // 本轮新增 1 枚仍在账上，未被弃赛撤销
        Assert.Equal(51, snapshot.Hand.TotalCount);
        Assert.Equal(MatchFixtures.P3, snapshot.Effects.Player);
        Assert.Equal(5, snapshot.Effects.DeployLimit);   // 第 5 大回合基础 4 + 控制军令 +1（growth-pass-1：原基础 3 → 4）
        Assert.Equal(new[] { "H8" }, snapshot.ControlledRelics.Notations());
        Assert.Equal(powerBefore, snapshot.Power);
        Assert.Equal(powerBefore, match.StateOf(MatchFixtures.P3).PowerAtResign);
        Assert.Equal(1, snapshot.RankAtResign);   // carry-in-out：快照含弃赛时势力名次（盘面上只有 P3 有子，第 1）

        Assert.Equal(TurnStage.Idle, match.Stage);
        Assert.Equal(MatchFixtures.P0, match.CurrentPlayer);
    }

    /// <summary>
    /// 第 6 大回合、顺序 P0 → P3 的局面：P2 在 A9 的单子被 P0 落 A8 / B9 提走而出局（曾建立正势力、势力归零），P1 与 P3 各有一个角上单子。
    /// 返回时 P0 的小回合已结束，停在小回合边界。
    /// </summary>
    private static MatchFlow WithEliminatedC()
    {
        MatchFlow match = MatchFixtures.Started().AtRound(6, MatchFixtures.All)
            .Stones(MatchFixtures.P2, "A9")
            .Stones(MatchFixtures.P1, "J1")
            .Stones(MatchFixtures.P3, "J9");
        match.PlayTurn("A8", "B9");
        Assert.Equal(PlayerStatus.Eliminated, match.StateOf(MatchFixtures.P2).Status);   // 前提：C 已出局
        return match;
    }

    private static BigInteger PowerOf(MatchFlow match, PlayerId player) => match.Scoreboard.Latest!.Of(player).Total;

    [Fact]
    public void 弃赛时势力名次()
    {
        // 规格 Scenario：C 已出局，D 以总势力 30 弃赛，A = 50、B = 30 → D 的弃赛时势力名次为第 2（只有 A 严格更高，与 B 并列共享）。
        // 数值不同、结构相同的真实局面：A（P0）严格高于 D（P3），B（P1）与 D 相等，C（P2）已出局。算式本身的算例见 carry-in-out「弃赛结算」纯函数测试。
        MatchFlow match = WithEliminatedC();
        Assert.True(PowerOf(match, MatchFixtures.P0) > PowerOf(match, MatchFixtures.P3), "前提：A 严格高于 D");
        Assert.Equal(PowerOf(match, MatchFixtures.P1), PowerOf(match, MatchFixtures.P3));

        match.Resign(MatchFixtures.P3);

        Assert.Equal(2, match.Resignations.Single().RankAtResign);
    }

    [Fact]
    public void 此前已弃赛者计入弃赛名次()
    {
        // 规格正文：弃赛时名次在未出局玩家中排，此前已弃赛者也计入。B（P1）先以 3 子弃赛、之后 D（P3）以 1 子弃赛：
        // B 此刻势力仍高于 D → D 第 2（只在参赛者中排会得出第 1：P0 / P2 盘面无子，势力 0）。
        MatchFlow match = MatchFixtures.Started().AtRound(6, MatchFixtures.All)
            .Stones(MatchFixtures.P1, "G1", "H1", "J1")
            .Stones(MatchFixtures.P3, "J9");
        match.Resign(MatchFixtures.P1);
        Assert.True(PowerOf(match, MatchFixtures.P1) > PowerOf(match, MatchFixtures.P3), "前提：已弃赛的 B 此刻势力高于 D");

        match.Resign(MatchFixtures.P3);

        Assert.Equal(1, match.Resignations[0].RankAtResign);
        Assert.Equal(2, match.Resignations[1].RankAtResign);
    }

    [Fact]
    public void 弃赛名次不影响最终名次()
    {
        // 规格 Scenario：D 以弃赛时势力名次第 1 弃赛，其余三名玩家都完赛 → 最终名次中 D 为第 4，排在三名完赛者之后。
        MatchFlow match = MatchFixtures.Started().AtRound(5, MatchFixtures.All)
            .Stones(MatchFixtures.P3, "G9", "H9", "J9", "J8")
            .Stones(MatchFixtures.P0, "A1");
        match.Resign(MatchFixtures.P3);
        Assert.Equal(1, match.Resignations.Single().RankAtResign);

        match.PassTurn();
        match.PassTurn();
        match.PassTurn();   // 三名参赛者整轮 Pass → 终局

        Assert.Equal(MatchPhase.Ended, match.Phase);
        Standing d = match.Result!.Of(MatchFixtures.P3);
        Assert.Equal(StandingGroup.Resigned, d.Group);
        Assert.Equal(4, d.Rank);
        Assert.All(match.Result.Standings.Where(s => s.Player != MatchFixtures.P3), s => Assert.True(s.Rank < d.Rank));
    }

    [Fact]
    public void 弃赛名次随存档往返()
    {
        // 规格 Scenario：保存一局已有玩家弃赛的对局并恢复 → 该弃赛者的弃赛时势力名次与保存时相同。
        // 样本名次为 2（非空、非 1），往返同时证伪"写入路径漏写"（缺字段恢复为空）。
        MatchFlow match = WithEliminatedC();
        match.Resign(MatchFixtures.P3);
        Assert.Equal(2, match.Resignations.Single().RankAtResign);
        string json = match.Serialize();

        MatchFlow restored = MatchFlow.RestoreUnvalidated(match.Board.BaseMap, match.Relics.Generation, json);

        Assert.Equal(match.Resignations.Single().RankAtResign, restored.Resignations.Single().RankAtResign);
        Assert.Equal(json, restored.Serialize());
    }
}
