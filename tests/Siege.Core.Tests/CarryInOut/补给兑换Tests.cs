using Siege.Core.Carry;

namespace Siege.Core.Tests.CarryInOut;

/// <summary>规格：carry-in-out —— Requirement: 补给兑换</summary>
public class 补给兑换Tests
{
    [Fact]
    public void 兑换成功()
    {
        // 规格 Scenario：补给点为 17，玩家兑换 1 件换型令 → 补给点变为 13，换型令库存 +1，档案已写入。
        using var temp = new TempProfile();
        temp.Write(TempProfile.Json(points: 17, spare: 0, draft: 0, commission: 0));
        CarryProfileStore store = temp.Store();
        store.Load();

        Assert.True(store.TryExchange(SupplyKind.Commission, out string? refusal));

        Assert.Null(refusal);
        Assert.Equal((13, 1), (store.Current.Points, store.Current.StockOf(SupplyKind.Commission)));
        Assert.Equal(TempProfile.Json(points: 13, spare: 0, draft: 0, commission: 1), temp.Read());
    }

    [Theory]
    [InlineData(SupplyKind.SpareStone, 3)]
    [InlineData(SupplyKind.DraftLot, 2)]
    [InlineData(SupplyKind.Commission, 4)]
    public void 每次兑换一件并按价扣点(SupplyKind kind, int price)
    {
        // 规格正文：补给只能用补给点按价格兑换，每次兑换 1 件。价格取规格表（测试内独立写出，不读实现的价格常量）。
        using var temp = new TempProfile();
        temp.Write(TempProfile.Json(points: 10, spare: 0, draft: 0, commission: 0));
        CarryProfileStore store = temp.Store();
        store.Load();

        Assert.True(store.TryExchange(kind, out _));
        Assert.True(store.TryExchange(kind, out _));

        Assert.Equal(10 - 2 * price, store.Current.Points);
        Assert.Equal(2, store.Current.StockOf(kind));
        Assert.All(Supplies.Order.Where(k => k != kind), k => Assert.Equal(0, store.Current.StockOf(k)));
    }

    [Fact]
    public void 补给点不足()
    {
        // 规格 Scenario：补给点为 1，玩家兑换备用子 → 系统拒绝并提示"补给点不足（需要 3，现有 1）"，档案不变。
        using var temp = new TempProfile();
        string original = TempProfile.Json(points: 1, spare: 0, draft: 0, commission: 0);
        temp.Write(original);
        CarryProfileStore store = temp.Store();
        store.Load();

        Assert.False(store.TryExchange(SupplyKind.SpareStone, out string? refusal));

        Assert.Equal("补给点不足（需要 3，现有 1）", refusal);
        Assert.Equal(original, temp.Read());
        Assert.Equal((1, 0), (store.Current.Points, store.Current.StockOf(SupplyKind.SpareStone)));
    }

    [Fact]
    public void 补给点恰等于价格可以兑换()
    {
        // 边界：补给点不少于价格即可兑换（带等号的比较先拿退化局面算一遍，testing.md）。
        using var temp = new TempProfile();
        temp.Write(TempProfile.Json(points: 4, spare: 0, draft: 0, commission: 0));
        CarryProfileStore store = temp.Store();
        store.Load();

        Assert.True(store.TryExchange(SupplyKind.Commission, out _));
        Assert.Equal((0, 1), (store.Current.Points, store.Current.StockOf(SupplyKind.Commission)));
    }
}
