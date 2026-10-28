using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Core.Scoring;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.Battlefield;

/// <summary>
/// 规格：openspec/changes/open-map/specs/battlefield —— Requirement: 阶段推进（MODIFIED：插旗 → 构筑 → 交战 → 终局，没有配对 / 链接 / 等待者 / 决赛）
/// 与 map-definition「地图整局不变」。open-map 段 B（裁决 B3）：原 <c>阶段推进Tests</c> 的配对 / 决赛用例与原 <c>链接模式端到端Tests</c> 的拼接版随代码删除，
/// 段 A 质检补的三条 Scenario 与开放地图的全流程端到端搬到本类。出生棋盘编号用 0 起的内部索引。
/// </summary>
/// <remarks>
/// 变异（open-map 段 A 质检实跑 M-C-D3）：<c>PublicRules.LegalRange</c> 的构筑期分支恒不成立 → 「四人同区_开放地图」「真对局走一遍_四步全通过」等红 8。
/// </remarks>
public class 开放地图阶段推进Tests
{
    [Fact]
    public void 第4大回合不发生任何事件()
    {
        // Scenario（open-map）：四人四区，第 3 大回合结束 → 对局直接进入第 4 大回合，没有配对、链接或等待者事件；行动顺序照常按势力生成；地图与开局逐格相同、版本号 0。
        // P0 每个大回合落 1 子、其余 Pass（不触发整轮 Pass 结算）。
        MatchFlow match = Started([0, 1, 3, 2]).PlayRounds(3, P0);

        Assert.Equal(4, match.MajorRound);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Empty(match.Settlements);
        // relic-redesign W1：大回合开始时的信物预告 / 落位（D2）属于信物层，不是配对 / 链接事件，也不改地图——放进允许名单。
        Assert.DoesNotContain(match.Events, e => e.Kind != FlowEventKind.FlagsLocked && e.Kind != FlowEventKind.TurnStarted && e.Kind != FlowEventKind.StageEntered
            && e.Kind != FlowEventKind.TurnEnded && e.Kind != FlowEventKind.MajorRoundEnded
            && e.Kind != FlowEventKind.RelicsForecast && e.Kind != FlowEventKind.RelicsLanded);
        Assert.NotNull(match.CurrentPlayer);
        Assert.Equal(4, match.ActionOrder.Length);
        Assert.Equal(P0, match.ActionOrder[0]);   // 只有 P0 有势力 → 势力名次第一、先手
        Assert.Equal(0, match.Map.MapVersion);
        Assert.Same(Map, match.Board.BaseMap);
        Assert.Equal(MapFile.ToJson(Map), MapFile.ToJson(match.Map));   // 地图整局不变（没有改造时逐格相同）
        Assert.Equal(1, match.Board.Battlefields.Count);

        // 第 4 大回合照常进行：P0 落子、其余 Pass → 第 5 大回合，仍没有任何事件。
        match.PlayRound(P0);
        Assert.Equal(5, match.MajorRound);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Empty(match.Settlements);
        Assert.Equal(1, match.Board.Battlefields.Count);
    }

    [Fact]
    public void 构筑期内整轮Pass即终局()
    {
        // Scenario（open-map）：第 2 大回合 4 名玩家全部 Pass → 第 2 大回合结束时唯一战场结算，对局终局，名次按势力与判定链（P0 第 1 大回合落了 1 子 → 胜者）。
        MatchFlow match = Started([0, 1, 3, 2]).PlayRound(P0);
        Assert.Equal(2, match.MajorRound);
        match.PassRound();

        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.LastBattlefieldSettled, match.Result!.Reason);
        Assert.Equal(2, match.Result.MajorRound);
        BattlefieldSettlementRecord settled = Assert.Single(match.Settlements);
        Assert.Equal((2, 0, SettlementReason.AllPassed, P0), (settled.MajorRound, settled.Battlefield, settled.Reason, settled.Winner));
        Assert.Equal(4, settled.Entries.Length);
        Assert.Equal(P0, match.Result.Standings.Single(s => s.Rank == 1).Player);
        Assert.Equal(3, match.Result.Standings.Count(s => s.Input.Reason == EliminationReason.BattlefieldDefeat));
    }

    [Fact]
    public void 四人同区_开放地图()
    {
        // Scenario（open-map「四人同区」）：4 名玩家都锁定棋盘 1（内部索引 0）→ 构筑期内四人共享棋盘 1 的合法落子范围；第 4 大回合起全图可落；终局条件与四人四区相同。
        MatchFlow match = Started([0, 0, 0, 0]);
        HashSet<Coord> board0 = [.. match.Map.BirthBoard(0).Cells().Where(match.Board.Map.IsPlayable)];
        Assert.Equal(board0, match.LegalRangeFor(P0));
        Assert.All(All, p => Assert.Equal(match.LegalRangeFor(P0), match.LegalRangeFor(p)));   // 同区共享

        match.PlayRounds(3, P0);
        Assert.Equal(4, match.MajorRound);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        IReadOnlySet<Coord> unlocked = match.LegalRangeFor(P1);
        Assert.True(unlocked.Count > board0.Count);
        Assert.Contains(BirthCell(match.Map, 3, 0, 0), unlocked);   // 无人锁定的棋盘 4 也可落
        Assert.Empty(match.Settlements);

        // 第 4 大回合整轮 Pass → 唯一战场结算即终局：胜者第 1，其余三人以战场落败出局（与四人四区相同的判定）。
        match.PassRound();
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.LastBattlefieldSettled, match.Result!.Reason);
        Assert.Single(match.Settlements);
        Assert.Equal(P0, match.Result.Standings.Single(s => s.Rank == 1).Player);
        Assert.Equal(3, match.PlayerStates.Count(s => s.Status == PlayerStatus.Eliminated));
        Assert.Equal(3, match.Result.Standings.Count(s => s.Input.Reason == EliminationReason.BattlefieldDefeat));
    }

    /// <summary>
    /// 开放地图端到端（段 A 质检补；原链接模式端到端的新流程版）：同一确定性控制器从插旗跑到终局——没有配对 / 链接，唯一战场结算一次即终局；
    /// 第 4 大回合开始时（或已终局时）存档、恢复后挂同样的控制器续跑，终局原因、大回合、名次与再存档逐项相同。
    /// 控制器：前 <c>placeRounds</c> 个大回合每小回合落 1 子（合法范围内坐标序第一个能确认的空格），之后一律 Pass。
    /// </summary>
    [Theory]
    [InlineData("四人四区", new[] { 0, 1, 3, 2 }, 5)]
    [InlineData("两人同区加两人独占", new[] { 1, 1, 3, 2 }, 5)]
    [InlineData("四人同区", new[] { 1, 1, 1, 1 }, 5)]   // map-expand-25 段 A2：link:1:4 棋盘 0 是 E5 起的河流平台（F 列河道、第 6 行岩石），坐标序落子会把人提光（LastPlayerStanding），改锁棋盘 1（Q5 林地平台）
    [InlineData("构筑期内整轮Pass", new[] { 0, 1, 3, 2 }, 1)]
    public void 从插旗跑到终局_开放地图(string 开局, int[] zones, int placeRounds)
    {
        (MatchFlow match, MatchRunner runner) = Build(zones, placeRounds);

        while (match.Phase == MatchPhase.InProgress && match.MajorRound < 4)
        {
            runner.RunTurn();
        }

        MatchFlow restored = MatchFlow.Restore(Map, match.Serialize());
        MatchRunner restoredRunner = Attach(restored, placeRounds);
        Assert.Equal(match.Serialize(), restored.Serialize());

        MatchResult result = match.Phase == MatchPhase.Ended ? match.Result! : runner.RunToEnd(500);
        MatchResult again = restored.Phase == MatchPhase.Ended ? restored.Result! : restoredRunner.RunToEnd(500);

        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.LastBattlefieldSettled, result.Reason);
        Assert.Equal(placeRounds + 1, result.MajorRound);   // 最后一个落子大回合之后的第一个整轮 Pass 大回合结算
        Assert.Equal(0, match.Map.MapVersion);
        BattlefieldSettlementRecord settled = Assert.Single(match.Settlements);
        Assert.Equal((0, SettlementReason.AllPassed), (settled.Battlefield, settled.Reason));
        Assert.Equal(All.Order(), result.Standings.Select(s => s.Player).Order());
        Assert.Equal(1, result.Standings.Count(s => s.Rank == 1));
        Assert.Equal(StandingGroup.Finisher, result.Standings[0].Group);
        Assert.All(result.Standings.Skip(1), s => Assert.Equal(StandingGroup.Eliminated, s.Group));
        Assert.Contains(result.Standings, s => s.Input.Reason == EliminationReason.BattlefieldDefeat);
        Assert.True(result.Standings.Select(s => s.Rank).SequenceEqual(result.Standings.Select(s => s.Rank).Order()), 开局);
        Assert.Single(match.ActivePlayers);

        Assert.Equal(result.Reason, again.Reason);
        Assert.Equal(result.MajorRound, again.MajorRound);
        Assert.Equal(result.Standings.Select(s => (s.Rank, s.Player, s.Input.Power)), again.Standings.Select(s => (s.Rank, s.Player, s.Input.Power)));
        Assert.Equal(match.Serialize(), restored.Serialize());
    }

    private static (MatchFlow Match, MatchRunner Runner) Build(int[] zones, int placeRounds)
    {
        MatchFlow match = Create();
        foreach (PlayerId p in All)
        {
            match.Debug.SeedHand(p, (PieceType.Basic, 50));
        }

        match.PlantSequentially(All.Select((p, i) => (p, zones[i])));
        return (match, Attach(match, placeRounds));
    }

    private static MatchRunner Attach(MatchFlow match, int placeRounds)
    {
        var runner = new MatchRunner(match);
        foreach (PlayerId p in All)
        {
            runner.SetController(p, new EarlyPlacer(match, placeRounds));
        }

        return runner;
    }

    /// <summary>前 <paramref name="placeRounds"/> 个大回合落 1 子、之后 Pass 的确定性控制器。不征募（开局手牌由夹具给 50 枚普通子）。</summary>
    private sealed class EarlyPlacer(MatchFlow match, int placeRounds) : ITurnController
    {
        public void OrganizeHand(PlayerHandAccess hand, int overflow)
        {
        }

        public void Recruit(PlayerHandAccess hand, RecruitPanelView panel)
        {
        }

        public void Deploy(StagedBatch batch, Func<RehearsalResult> rehearse)
        {
            if (match.MajorRound > placeRounds || batch.Context.Stock.All(kv => kv.Value == 0))
            {
                return;
            }

            PieceType type = batch.Context.Stock.Where(kv => kv.Value > 0).Select(kv => kv.Key).Order().First();
            foreach (Coord cell in batch.Context.LegalRange.Order())
            {
                if (match.Board[cell].Occupant is null && batch.Stage(cell, type) is null)
                {
                    if (rehearse().IsLegal)
                    {
                        return;
                    }

                    batch.Clear();
                }
            }
        }

        public bool OnRejected(StagedBatch batch, BatchFailure failure) => false;
    }
}
