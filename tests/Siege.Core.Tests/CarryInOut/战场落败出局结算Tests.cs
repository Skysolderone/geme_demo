using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Carry;
using Siege.Core.Match;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.CarryInOut;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/carry-in-out —— Requirement: 出局结算（无论势力归零还是战场落败：补给丢失、带出 0）。
/// 链接模式的落败者在终局名次里属出局组，经 <see cref="CarryOutSettlement.Settle"/> 按出局结算。
/// </summary>
public class 战场落败出局结算Tests
{
    [Fact]
    public void 战场落败同样丢失()
    {
        // Scenario：带入备用子的玩家在战场结算中落败出局 → 备用子不返还，补给点不增加。
        ImmutableSortedDictionary<PlayerId, CarryIn> carries = ImmutableSortedDictionary<PlayerId, CarryIn>.Empty
            .Add(P0, new CarryIn(SupplyKind.SpareStone))
            .Add(P1, new CarryIn(SupplyKind.SpareStone));
        MatchFlow match = MatchFlow.Create(Map, TestMaps.Seed, All, Options with { CarryInOut = true, CarryIns = carries });
        match.PairingSource = new ScriptedPairingSource();
        match.PlantSequentially(All.Select((p, i) => (p, new[] { 0, 0, 3, 3 }[i])));
        match.PlayRound(P0, P2).PassRound();          // 两个双人战场同批结算：P0、P2 胜
        Assert.Equal(EliminationReason.BattlefieldDefeat, match.StateOf(P1).EliminationReason);
        for (int i = 0; i < 10 && match.Phase == MatchPhase.InProgress; i++)
        {
            match.PassRound();
        }

        Assert.Equal(EndReason.LastBattlefieldSettled, match.Result!.Reason);
        ImmutableSortedDictionary<PlayerId, CarryOutResult> settled = CarryOutSettlement.Settle(match.Result, match.Resignations, match.CarryIns, match.Players);

        CarryOutResult loser = settled[P1];
        Assert.Equal(CarryOutcome.Eliminated, loser.Outcome);
        Assert.Equal(0, loser.Points);
        Assert.False(loser.Returned);
        Assert.Null(loser.Rank);

        PlayerId winner = match.Result.Winners.Single();
        Assert.Equal(CarryOutcome.Finished, settled[winner].Outcome);
        Assert.True(settled[winner].Points > 0);
        // 决赛落败者也是战场落败出局：带出 0。
        PlayerId finalist = match.Result.Standings.Single(s => s.Rank == 2).Player;
        Assert.Equal(CarryOutcome.Eliminated, settled[finalist].Outcome);
        Assert.Equal(0, settled[finalist].Points);
    }
}
