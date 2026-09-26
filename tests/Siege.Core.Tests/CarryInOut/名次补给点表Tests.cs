using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Carry;
using Siege.Core.Match;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.CarryInOut;

/// <summary>规格：carry-in-out —— Requirement: 名次补给点表</summary>
public class 名次补给点表Tests
{
    [Fact]
    public void 四人局查表()
    {
        // 规格 Scenario「4 人局查表」：完赛名次第 1–4 → 带出 24、16、12、10。另钉 3 人 24 / 16 / 10、2 人 24 / 10（design.md D3）。
        Assert.Equal([24, 16, 12, 10], Enumerable.Range(1, 4).Select(r => CarryPoints.For(4, r)));
        Assert.Equal([24, 16, 10], CarryPoints.TableOf(3));
        Assert.Equal([24, 10], CarryPoints.TableOf(2));
        Assert.Equal([24, 16, 12, 10], CarryPoints.TableOf(4));
        Assert.False(CarryPoints.Supports(1));
        Assert.False(CarryPoints.Supports(5));
        Assert.True(CarryPoints.Supports(2));
        Assert.ThrowsAny<ArgumentException>(() => CarryPoints.For(4, 5));
        Assert.ThrowsAny<ArgumentException>(() => CarryPoints.For(4, 0));

        // 四名完赛者名次 1–4 的整局结算与查表一致。
        ImmutableSortedDictionary<PlayerId, CarryOutResult> settled = CarryOutSettlement.Settle(
            CarryFixtures.Result(
                CarryFixtures.Input(0, PlayerStatus.Active, 40), CarryFixtures.Input(1, PlayerStatus.Active, 30),
                CarryFixtures.Input(2, PlayerStatus.Active, 20), CarryFixtures.Input(3, PlayerStatus.Active, 10)),
            [], ImmutableSortedDictionary<PlayerId, CarryIn>.Empty, CarryFixtures.Four);
        Assert.Equal([24, 16, 12, 10], settled.Values.Select(r => r.Points));
        Assert.All(settled.Values, r => Assert.Equal(CarryOutcome.Finished, r.Outcome));
    }

    [Fact]
    public void 并列共享()
    {
        // 规格 Scenario：4 人局两名完赛者并列第 2 名、另一名为第 4 名 → 并列者各带出 16，第 4 名带出 10。
        // 名次取自终局名次比较链的唯一实现（四级全同即并列，竞争名次 1 / 2 / 2 / 4），结算读共享名次，不读排位位置。
        MatchResult result = CarryFixtures.Result(
            CarryFixtures.Input(0, PlayerStatus.Active, 50), CarryFixtures.Input(1, PlayerStatus.Active, 30),
            CarryFixtures.Input(2, PlayerStatus.Active, 30), CarryFixtures.Input(3, PlayerStatus.Active, 10));
        Assert.Equal([1, 2, 2, 4], result.Standings.Select(s => s.Rank));

        ImmutableSortedDictionary<PlayerId, CarryOutResult> settled = CarryOutSettlement.Settle(
            result, [], ImmutableSortedDictionary<PlayerId, CarryIn>.Empty, CarryFixtures.Four);

        Assert.Equal(24, settled[new PlayerId(0)].Points);
        Assert.Equal(16, settled[new PlayerId(1)].Points);
        Assert.Equal(16, settled[new PlayerId(2)].Points);
        Assert.Equal(10, settled[new PlayerId(3)].Points);
        Assert.Equal(2, settled[new PlayerId(2)].Rank);
    }

    [Fact]
    public void 完赛优于同名次弃赛()
    {
        // 规格 Scenario：4 人局第 4 名带入换型令（价 4）→ 完赛净收益 10 − 4 = 6，大于弃赛净收益 ⌊10 / 2⌋ = 5（补给返还，不计价）。
        CarryIn commission = CarryFixtures.Commission(PieceType.Fortress);
        CarryOutResult finished = CarryOutSettlement.Finished(4, 4, commission);
        CarryOutResult resigned = CarryOutSettlement.Resigned(4, 4, 6, commission);

        Assert.False(finished.Returned);
        Assert.True(resigned.Returned);
        Assert.Equal(6, finished.Points - Supplies.PriceOf(SupplyKind.Commission));
        Assert.Equal(5, resigned.Points);
        Assert.True(finished.Points - Supplies.PriceOf(SupplyKind.Commission) > resigned.Points);
    }

    [Fact]
    public void 任一名次半数向上取整大于任一价格()
    {
        // 规格正文（MUST）：任一名次表值的一半（向上取整）大于任一补给的价格——同名次下完赛（补给已消耗）的净收益严格高于弃赛（补给返还、点数减半）。
        // 守门：改表或改价使任何一格违反时本测试红。口径下界：2–4 人三张表共 9 格 × 3 种补给。
        int checkedCells = 0;
        foreach (int players in new[] { 2, 3, 4 })
        {
            for (int rank = 1; rank <= players; rank++)
            {
                int points = CarryPoints.For(players, rank);
                foreach (SupplyKind kind in Supplies.Order)
                {
                    Assert.True((points + 1) / 2 > Supplies.PriceOf(kind), $"{players} 人第 {rank} 名 {points} 点，{kind} 价 {Supplies.PriceOf(kind)}");
                    Assert.True(points - Supplies.PriceOf(kind) > CarryOutSettlement.ResignShare(points), $"{players} 人第 {rank} 名 {kind}");
                    checkedCells++;
                }
            }
        }

        Assert.Equal(27, checkedCells);
    }

    [Fact]
    public void 补给价格()
    {
        // 规格表：备用子 3 / 征召签 2 / 换型令 4（未校准初值）；三种补给按固定次序。
        Assert.Equal([SupplyKind.SpareStone, SupplyKind.DraftLot, SupplyKind.Commission], Supplies.Order);
        Assert.Equal([3, 2, 4], Supplies.Order.Select(Supplies.PriceOf));
        Assert.Equal(3, Supplies.UncalibratedSpareStonePrice);
        Assert.Equal(2, Supplies.UncalibratedDraftLotPrice);
        Assert.Equal(4, Supplies.UncalibratedCommissionPrice);
    }
}
