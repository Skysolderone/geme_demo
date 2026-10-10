using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Core.Scoring;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.Battlefield;

/// <summary>
/// 段 B 验收：链接模式 4 人、脚本固定配对，经 <see cref="MatchRunner"/> 从插旗跑到终局，五种开局各一局（battlefield「阶段推进」的五个开局 Scenario 的全流程版）。
/// 控制器是确定性的简单控制器：前 <c>placeRounds</c> 个大回合每小回合落 1 子（合法范围内坐标序第一个能确认的空格），之后一律 Pass。
/// 每局断言终局原因、名次完整、链接与结算次数，并在中途存档、恢复后挂上同样的配对来源与控制器续跑，终局与原局逐项相同。
/// </summary>
public class 链接模式端到端Tests
{
    [Theory]
    [InlineData("四人四区", new[] { 0, 1, 3, 4 }, 2, 3, 3)]
    [InlineData("两人同区加两人独占", new[] { 1, 1, 3, 4 }, 2, 2, 3)]
    [InlineData("提前打完的同区棋盘不进配对", new[] { 1, 1, 3, 4 }, 0, 2, 3)]
    [InlineData("三人同区加一人独占", new[] { 0, 0, 0, 2 }, 2, 1, 2)]
    [InlineData("四人同区", new[] { 0, 0, 0, 0 }, 2, 0, 1)]
    public void 从插旗跑到终局(string 开局, int[] zones, int placeRounds, int links, int settlements)
    {
        (MatchFlow match, MatchRunner runner) = Build(zones, placeRounds);

        // 跑到第 4 大回合开始（或已终局）时存档，恢复后挂同样的来源与控制器。
        while (match.Phase == MatchPhase.InProgress && match.MajorRound < 4)
        {
            runner.RunTurn();
        }

        MatchFlow restored = MatchFlow.Restore(Map, match.Serialize());
        restored.PairingSource = new ScriptedPairingSource();
        MatchRunner restoredRunner = Attach(restored, placeRounds);
        Assert.Equal(match.Serialize(), restored.Serialize());

        MatchResult result = match.Phase == MatchPhase.Ended ? match.Result! : runner.RunToEnd(500);
        MatchResult again = restored.Phase == MatchPhase.Ended ? restored.Result! : restoredRunner.RunToEnd(500);

        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.LastBattlefieldSettled, result.Reason);
        Assert.Equal(links, match.Links.Count);
        Assert.Equal(settlements, match.Settlements.Count(s => s.Reason != SettlementReason.Attrition));
        Assert.Equal(All.Order(), result.Standings.Select(s => s.Player).Order());
        Assert.Equal(1, result.Standings.Count(s => s.Rank == 1));
        Assert.Equal(StandingGroup.Finisher, result.Standings[0].Group);
        Assert.All(result.Standings.Skip(1), s => Assert.Equal(StandingGroup.Eliminated, s.Group));
        Assert.All(result.Standings.Skip(1), s => Assert.NotNull(s.Input.Reason));   // 战场落败或势力归零（同区落子可能提光对方）
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
        match.PairingSource = new ScriptedPairingSource();
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
