using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Core.Relics;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.MatchFlowRegression;

/// <summary>implement 1.1 / 8.2：对局顶层状态在小回合边界的持久化与恢复。</summary>
public class 对局持久化Tests
{
    [Fact]
    public void 小回合边界存档恢复后状态完全一致()
    {
        // 阶段、顺序、大回合、保护状态、Pass 计数、出局 / 弃赛状态、已提交盘面历史、信物揭示、手牌、弃赛快照、先手明细全部往返；
        // 恢复后继续同样的决策（含征募抽取）产出逐字节相同的存档——三条随机子流的消费位置也被正确恢复。
        // 变异验证 M-P1：Serialize 不写 PassStreak → 红 1；M-P2：HandLedger.Restore 不推进 recruit 子流 → 红 1（续跑后面板不同）。
        // check 修正：原用例让 P2 在第 1 大回合也落了 B8，之后 P0 落 E5/C3 碰不到 B8，P2 盘面非空、按 spec「两个条件都满足才出局」
        // 不该出局，于是 P2 仍占第 5 大回合的行动位、大回合停在 5——是用例前提错，不是推进逻辑错。改为 P2 首轮 Pass（盘面始终为空）。
        MatchFlow match = MatchFixtures.Started(relics: [("E5", RelicFixtures.Command()), ("C3", RelicFixtures.Vanguard())]);
        string?[] cell = ["B2", "H2", null, "H8"];
        for (int i = 0; i < 4; i++)
        {
            string? c = cell[match.CurrentPlayer!.Value.Value];
            _ = c is null ? match.PassTurn() : match.PlayTurn(c);
        }

        match.AtRound(5, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3]);
        match.Debug.SeedHand(MatchFixtures.P2);
        match.Resign(MatchFixtures.P3);
        match.PlayTurn("E5", "C3");   // P0：控制军令与先锋；结算后出局检查让盘面与手牌皆空、保护已解除的 P2 出局
        Assert.Equal(PlayerStatus.Eliminated, match.StateOf(MatchFixtures.P2).Status);
        match.PassTurn();             // P1 → 出局 / 弃赛者被跳过，大回合结束，第 6 大回合顺序 [P0, P1]
        Assert.Equal(6, match.MajorRound);
        Assert.Equal(1, match.PassStreak);

        string json = match.Serialize();
        MatchFlow restored = MatchFlow.RestoreUnvalidated(match.Map, match.Relics.Generation, json);

        Assert.Equal(match.Phase, restored.Phase);
        Assert.Equal(match.MajorRound, restored.MajorRound);
        Assert.Equal(TurnStage.Idle, restored.Stage);
        Assert.Equal(match.ActionOrder, restored.ActionOrder);
        Assert.Equal(match.CurrentPlayer, restored.CurrentPlayer);
        Assert.Equal(match.PassStreak, restored.PassStreak);
        Assert.Equal(match.PlayerStates, restored.PlayerStates);
        Assert.Equal(match.Board.Serialize(), restored.Board.Serialize());
        Assert.Equal(match.History.Serialize(), restored.History.Serialize());
        Assert.Equal(match.Relics.PublicStates(), restored.Relics.PublicStates());
        Assert.Equal(match.Hands.PublicViews(), restored.Hands.PublicViews());
        Assert.Equal(match.Hands.RecruitStreamConsumed, restored.Hands.RecruitStreamConsumed);
        foreach (PlayerId p in MatchFixtures.All)
        {
            Assert.Equal(match.Hands.Debug.PrivateViewOf(p), restored.Hands.Debug.PrivateViewOf(p));
        }

        Assert.Equal(match.Resignations.Select(r => (r.Player, r.Board, r.Hand, r.Effects, r.Power, r.ControlledRelics.Notations())),
            restored.Resignations.Select(r => (r.Player, r.Board, r.Hand, r.Effects, r.Power, r.ControlledRelics.Notations())));
        Assert.Equal(match.InitiativeReports.Select(r => r.NextOrder), restored.InitiativeReports.Select(r => r.NextOrder));
        Assert.Equal(match.InitiativeReports.SelectMany(r => r.Entries), restored.InitiativeReports.SelectMany(r => r.Entries));
        // PlayerPower 含 ImmutableArray 字段，record 相等退化为数组引用比较，须按值投影比较。
        Assert.Equal(PowerText(match), PowerText(restored));
        Assert.Equal(json, restored.Serialize());

        // 续跑：同样的决策 → 同样的面板、同样的存档
        foreach (MatchFlow m in new[] { match, restored })
        {
            m.BeginTurn();
            Assert.Equal(4, m.CurrentSnapshot!.DeployLimit);
            RecruitPanelView panel = m.EnterRecruit();
            m.CurrentHand().Pick(panel.Candidates.First(c => c.IsSelectable).Index);
            Assert.Null(m.EnterDeploy().Stage(TestMaps.At("F6"), PieceType.Basic));
            Assert.True(m.Confirm().Confirmed);
        }

        Assert.Equal(match.Hands.Debug.PrivateViewOf(MatchFixtures.P0), restored.Hands.Debug.PrivateViewOf(MatchFixtures.P0));
        Assert.Equal(match.Serialize(), restored.Serialize());
    }

    private static string PowerText(MatchFlow m)
    {
        Scoring.PowerSnapshot s = m.Scoreboard.Latest!;
        string players = string.Join(";", s.Players.Select(p =>
            $"{p.Player}:{p.Status}:{p.Total}:[{string.Join(",", p.ExclusiveCells.Notations())}]:[{string.Join("|", p.Groups.Select(g => $"{string.Join(",", g.Stones.Notations())}={g.Power}"))}]"));
        string ranking = string.Join(";", s.Ranking.Select(r => $"{r.Rank}:{r.Power}:{string.Join(",", r.Players.Order())}"));
        return players + "\n" + ranking;
    }

    [Fact]
    public void 已结束对局与终局结果可恢复()
    {
        MatchFlow match = MatchFixtures.Started().AtRound(5, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3])
            .Stones(MatchFixtures.P0, "E5");
        match.Debug.SeedHand(MatchFixtures.P1);
        match.Debug.SeedHand(MatchFixtures.P2);
        match.Debug.SeedHand(MatchFixtures.P3);
        match.PlayTurn("B2");
        Assert.Equal(EndReason.LastPlayerStanding, match.Result!.Reason);

        MatchFlow restored = MatchFlow.RestoreUnvalidated(match.Map, match.Relics.Generation, match.Serialize());
        Assert.Equal(MatchPhase.Ended, restored.Phase);
        Assert.Equal(match.Result.Reason, restored.Result!.Reason);
        Assert.Equal(match.Result.Standings, restored.Result.Standings);
        Assert.Equal(match.Result.Winners, restored.Result.Winners);
    }

    [Fact]
    public void 小回合进行中不能存档()
    {
        MatchFlow match = MatchFixtures.Started();
        match.BeginTurn();
        SiegeRuleException ex = Assert.Throws<SiegeRuleException>(() => match.Serialize());
        Assert.Contains("小回合边界", ex.Message);
    }

    [Fact]
    public void 存档地图不符即拒绝()
    {
        MatchFlow match = MatchFixtures.Started();
        MapData other = MatchFixtures.Map() with { Id = "another-map" };
        Assert.Throws<FormatException>(() => MatchFlow.RestoreUnvalidated(other, match.Relics.Generation, match.Serialize()));
    }
}
