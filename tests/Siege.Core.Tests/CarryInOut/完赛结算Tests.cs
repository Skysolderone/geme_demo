using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Carry;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.CarryInOut;

/// <summary>规格：carry-in-out —— Requirement: 完赛结算（段 A 只测结算纯函数；写档案与清除在途记录在段 B）</summary>
public class 完赛结算Tests
{
    [Fact]
    public void 完赛第1名()
    {
        // 规格 Scenario：4 人局本机玩家带入备用子，以整轮 Pass 终局，最终名次第 1 → 补给点 +24，备用子不返还。
        ImmutableSortedDictionary<PlayerId, CarryOutResult> settled = CarryOutSettlement.Settle(
            CarryFixtures.Result(
                CarryFixtures.Input(0, PlayerStatus.Active, 90), CarryFixtures.Input(1, PlayerStatus.Active, 30),
                CarryFixtures.Input(2, PlayerStatus.Active, 20), CarryFixtures.Input(3, PlayerStatus.Active, 10)),
            [],
            new Dictionary<PlayerId, CarryIn> { [new PlayerId(0)] = CarryFixtures.Spare },
            CarryFixtures.Four);

        CarryOutResult p0 = settled[new PlayerId(0)];
        Assert.Equal(new CarryOutResult(CarryOutcome.Finished, 1, 24, CarryFixtures.Spare, Returned: false), p0);
        Assert.Equal(new CarryOutResult(CarryOutcome.Finished, 2, 16, null, Returned: false), settled[new PlayerId(1)]);

        // 单人口径的同一函数。
        Assert.Equal(p0, CarryOutSettlement.Finished(4, 1, CarryFixtures.Spare));
    }

    [Fact]
    public void 只剩一名参赛玩家()
    {
        // 规格 Scenario：4 人局两名 AI 出局、一名 AI 弃赛，本机玩家为唯一参赛者 → 本机玩家为第 1 名，补给点 +24。
        // 弃赛的 P3 按其弃赛快照结算（第 6 大回合、弃赛时第 2 名 → ⌊16 / 2⌋ = 8），出局者 0。
        ImmutableSortedDictionary<PlayerId, CarryOutResult> settled = CarryOutSettlement.Settle(
            CarryFixtures.Result(
                CarryFixtures.Input(0, PlayerStatus.Active, 12),
                CarryFixtures.Input(1, PlayerStatus.Eliminated, 0, eliminationOrder: 1),
                CarryFixtures.Input(2, PlayerStatus.Eliminated, 0, eliminationOrder: 2),
                CarryFixtures.Input(3, PlayerStatus.Resigned, 40)),
            [CarryFixtures.Resignation(new PlayerId(3), 6, 2)],
            ImmutableSortedDictionary<PlayerId, CarryIn>.Empty,
            CarryFixtures.Four);

        Assert.Equal(CarryOutcome.Finished, settled[new PlayerId(0)].Outcome);
        Assert.Equal(1, settled[new PlayerId(0)].Rank);
        Assert.Equal(24, settled[new PlayerId(0)].Points);
        Assert.Equal(CarryOutcome.Resigned, settled[new PlayerId(3)].Outcome);
        Assert.Equal(8, settled[new PlayerId(3)].Points);
        Assert.Equal(CarryOutcome.Eliminated, settled[new PlayerId(1)].Outcome);
        Assert.Equal(0, settled[new PlayerId(2)].Points);
    }
}
