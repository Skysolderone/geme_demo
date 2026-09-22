using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Core.Scoring;
using Xunit.Abstractions;

namespace Siege.Core.Tests.MatchFlowRegression;

/// <summary>implement 8.4：占位随机策略（种子驱动）端到端连跑 100 局 4 人对局，无死锁、无非法状态。</summary>
public class 百局端到端Tests(ITestOutputHelper output)
{
    [Fact]
    public void 连续一百局四人对局无死锁无非法状态()
    {
        MapData map = FourPlayerBaseMap.Create();
        PlayerId[] players = [new(0), new(1), new(2), new(3)];
        var reasons = new SortedDictionary<EndReason, int>();
        var winners = new SortedDictionary<PlayerId, int>();
        var rounds = new List<int>();
        int firstPositionWins = 0;
        int leaderAtRound3Wins = 0;
        int leaderAtRound3Samples = 0;

        for (ulong s = 1; s <= 100; s++)
        {
            var seed = new GameSeed(s);
            MatchFlow match = MatchFlow.Create(map, seed, players, MatchOptions.Immediate);
            match.PlantSequentially(players.Select((p, i) => (p, i)));
            var runner = new MatchRunner(match);
            foreach (PlayerId p in players)
            {
                runner.SetController(p, new RandomTurnController(seed.Stream($"ai-{p}")));
            }

            PlayerId firstMover = match.ActionOrder[0];
            MatchResult result = runner.RunToEnd(maxTurns: 4000);
            AssertInvariants(match, result);

            rounds.Add(result.MajorRound);
            reasons[result.Reason] = reasons.TryGetValue(result.Reason, out int n) ? n + 1 : 1;
            foreach (PlayerId w in result.Winners)
            {
                winners[w] = winners.TryGetValue(w, out int m) ? m + 1 : 1;
            }

            if (result.Winners.Contains(firstMover))
            {
                firstPositionWins++;
            }

            InitiativeReport? third = match.InitiativeReports.FirstOrDefault(r => r.CompletedMajorRound == 3);
            if (third is not null && !result.HasTies)
            {
                leaderAtRound3Samples++;
                if (result.Winners.Single() == third.NextOrder[0])
                {
                    leaderAtRound3Wins++;
                }
            }
        }

        output.WriteLine($"100 局：平均大回合数 {rounds.Average():F1}（最短 {rounds.Min()}，最长 {rounds.Max()}）");
        output.WriteLine($"终局原因：{string.Join("，", reasons.Select(kv => $"{kv.Key}={kv.Value}"))}");
        output.WriteLine($"获胜分布：{string.Join("，", winners.Select(kv => $"{kv.Key}={kv.Value}"))}；首回合先手者获胜 {firstPositionWins} 局");
        output.WriteLine($"第 3 大回合领先者最终获胜：{leaderAtRound3Wins}/{leaderAtRound3Samples}（§16 目标 ≤ 50%，随机策略仅作管线验证）");
        Assert.Equal(100, rounds.Count);
        Assert.True(rounds.Average() > 4, "随机策略的对局不应在保护期内就结束");
    }

    private static void AssertInvariants(MatchFlow match, MatchResult result)
    {
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(TurnStage.Idle, match.Stage);
        Assert.Equal(4, result.Standings.Length);
        Assert.Equal(1, result.Standings[0].Rank);
        Assert.NotEmpty(result.Winners);
        Assert.Equal(1, match.CountEvents(FlowEventKind.MatchEnded));

        // 每个开始的小回合都结束了；出局 / 弃赛者之后再无小回合
        Assert.Equal(match.CountEvents(FlowEventKind.TurnStarted), match.CountEvents(FlowEventKind.TurnEnded));
        foreach (PlayerFlowState state in match.PlayerStates)
        {
            if (state.Status == PlayerStatus.Eliminated)
            {
                int elim = match.Events.ToList().FindIndex(e => e.Kind == FlowEventKind.PlayerEliminated && e.Player == state.Player);
                Assert.DoesNotContain(match.Events.Skip(elim + 1), e => e.Kind == FlowEventKind.TurnStarted && e.Player == state.Player);
                // restore-go-core-rules D3：出局者必定曾建立正势力（保护期不再豁免，故不再断言出局大回合晚于保护期）。
                Assert.True(state.HasEstablishedPower);
            }
        }

        // 每次大回合结束的顺序只含参赛者，先手值公式逐项成立
        foreach (InitiativeReport report in match.InitiativeReports)
        {
            Assert.Equal(report.Entries.Length, report.ActiveCount);
            Assert.All(report.Entries, e => Assert.Equal((report.ActiveCount - e.Rank) + e.Bonus, e.Value));
        }

        // 存档往返
        Assert.Equal(match.Serialize(), MatchFlow.Restore(match.Board.BaseMap, match.Serialize()).Serialize());
    }
}
