using Siege.Core.Board;
using Siege.Core.Carry;

namespace Siege.Core.Tests.CarryInOut;

/// <summary>规格：carry-in-out —— Requirement: 开局带入与在途记录</summary>
public class 开局带入与在途记录Tests
{
    [Fact]
    public void 带入扣库存()
    {
        // 规格 Scenario：换型令库存为 2，玩家带入换型令（指定堡垒子）开局 → 开局后换型令库存为 1，在途记录为"本局标识 / 换型令 / 堡垒子"。
        using var temp = new TempProfile();
        temp.Write(TempProfile.Json(points: 5, spare: 0, draft: 0, commission: 2));
        CarryProfileStore store = temp.Store();
        store.Load();

        store.Begin("match-7", CarryFixtures.Commission(PieceType.Fortress));

        Assert.Equal(1, store.Current.StockOf(SupplyKind.Commission));
        Assert.Equal(5, store.Current.Points);
        Assert.Equal(new CarryInFlight("match-7", new CarryIn(SupplyKind.Commission, PieceType.Fortress)), store.Current.InFlight);

        // 已写入档案：另起一个存取读回，逐项相同。
        CarryProfileStore reread = temp.Store();
        reread.Load();
        Assert.Equal(1, reread.Current.StockOf(SupplyKind.Commission));
        Assert.Equal(new CarryInFlight("match-7", new CarryIn(SupplyKind.Commission, PieceType.Fortress)), reread.Current.InFlight);
    }

    [Fact]
    public void 不带入也有在途记录()
    {
        // 规格 Scenario：带入带出开启，玩家选择不带入 → 库存不变，在途记录的补给为空，局终照常按名次结算补给点。
        using var temp = new TempProfile();
        temp.Write(TempProfile.Json(points: 5, spare: 1, draft: 1, commission: 1));
        CarryProfileStore store = temp.Store();
        store.Load();

        store.Begin("match-8", null);

        Assert.Equal(new CarryInFlight("match-8", null), store.Current.InFlight);
        Assert.All(Supplies.Order, k => Assert.Equal(1, store.Current.StockOf(k)));

        // 局终：4 人局完赛第 3 名 → +12（规格点数表），在途记录清除。
        Assert.True(store.Settle("match-8", CarryOutSettlement.Finished(4, 3, null)));
        Assert.Equal(17, store.Current.Points);
        Assert.Null(store.Current.InFlight);
        Assert.All(Supplies.Order, k => Assert.Equal(1, store.Current.StockOf(k)));
    }

    [Fact]
    public void 库存为0不能带()
    {
        // 规格 Scenario：备用子库存为 0 → 选择界面不提供带入备用子。
        // 选择界面列出的可带补给取自 CarryProfile.Carriable；存取层同样拒绝带入库存为 0 的补给，档案不变。
        using var temp = new TempProfile();
        string original = TempProfile.Json(points: 9, spare: 0, draft: 1, commission: 0);
        temp.Write(original);
        CarryProfileStore store = temp.Store();
        store.Load();

        Assert.Equal([SupplyKind.DraftLot], store.Current.Carriable);
        Assert.Throws<InvalidOperationException>(() => store.Begin("m", CarryFixtures.Spare));
        Assert.Equal(original, temp.Read());
        Assert.Null(store.Current.InFlight);
    }

    [Fact]
    public void 残留在途记录未处理时不得开局()
    {
        // 规格正文：开局时档案已有未结算的在途记录的，SHALL 先按「中途退出与截断」处理掉——存取层不替入口吞掉这一步，未处理即拒绝。
        using var temp = new TempProfile();
        temp.Write(TempProfile.Json(points: 9, spare: 1, draft: 0, commission: 0, inFlight: "{\"matchId\":\"old\",\"supply\":\"SpareStone\",\"type\":null}"));
        CarryProfileStore store = temp.Store();
        store.Load();

        Assert.Throws<InvalidOperationException>(() => store.Begin("new", null));
        Assert.Equal(new CarryInFlight("old", CarryFixtures.Spare), store.Current.InFlight);
    }
}
