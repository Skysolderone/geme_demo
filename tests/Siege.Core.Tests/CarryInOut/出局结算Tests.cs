using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Carry;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.CarryInOut;

/// <summary>规格：carry-in-out —— Requirement: 出局结算（段 A 只测纯函数）；另含「中途退出与截断」中截断局不结算的纯函数部分。</summary>
public class 出局结算Tests
{
    [Fact]
    public void 出局丢失()
    {
        // 规格 Scenario：本机玩家带入换型令，在第 7 大回合出局 → 换型令不返还，补给点不增加。
        CarryIn commission = CarryFixtures.Commission(PieceType.Line);
        Assert.Equal(new CarryOutResult(CarryOutcome.Eliminated, null, 0, commission, Returned: false), CarryOutSettlement.Eliminated(commission));

        // 整局结算：出局者无论出局组名次多少，都是 0 且丢失。
        ImmutableSortedDictionary<PlayerId, CarryOutResult> settled = CarryOutSettlement.Settle(
            CarryFixtures.Result(
                CarryFixtures.Input(0, PlayerStatus.Active, 30), CarryFixtures.Input(1, PlayerStatus.Active, 20),
                CarryFixtures.Input(2, PlayerStatus.Active, 10), CarryFixtures.Input(3, PlayerStatus.Eliminated, 0, eliminationOrder: 1)),
            [], new Dictionary<PlayerId, CarryIn> { [new PlayerId(3)] = commission }, CarryFixtures.Four);
        Assert.Equal(new CarryOutResult(CarryOutcome.Eliminated, null, 0, commission, Returned: false), settled[new PlayerId(3)]);
    }

    [Fact]
    public void 未带入者出局()
    {
        // 规格 Scenario：未带入补给的本机玩家出局 → 补给点与库存都不变。
        Assert.Equal(new CarryOutResult(CarryOutcome.Eliminated, null, 0, null, Returned: false), CarryOutSettlement.Eliminated(null));
    }

    [Fact]
    public void 截断局不结算()
    {
        // carry-in-out「中途退出与截断」Scenario 的纯函数部分：以 turn_limit 截断的对局没有名次（结果为 null）→ 每名玩家都是"未结算"，不带出、不返还。
        CarryIn commission = CarryFixtures.Commission(PieceType.Fortress);
        ImmutableSortedDictionary<PlayerId, CarryOutResult> settled = CarryOutSettlement.Settle(
            null, [], new Dictionary<PlayerId, CarryIn> { [new PlayerId(1)] = commission }, CarryFixtures.Four);

        Assert.Equal(4, settled.Count);
        Assert.All(settled.Values, r => Assert.Equal(CarryOutcome.Unsettled, r.Outcome));
        Assert.All(settled.Values, r => Assert.Equal(0, r.Points));
        Assert.All(settled.Values, r => Assert.False(r.Returned));
        Assert.All(settled.Values, r => Assert.Null(r.Rank));
        Assert.Equal(commission, settled[new PlayerId(1)].CarryIn);
        Assert.Equal(new CarryOutResult(CarryOutcome.Unsettled, null, 0, commission, Returned: false), CarryOutSettlement.Unsettled(commission));
    }
}
