using Siege.Core.Board;
using Siege.Core.Carry;
using Siege.Core.Match;

namespace Siege.Core.Tests.CarryInOut;

/// <summary>规格：carry-in-out —— Requirement: 中途退出与截断</summary>
/// <remarks>「截断局不结算」的日志部分（"未结算"写入日志）在段 C；本段钉住结算纯函数与档案存取两层。终端的退出脚本见 <see cref="终端的带入选择弃赛与结算显示Tests"/>。</remarks>
public class 中途退出与截断Tests
{
    [Fact]
    public void 退出后下次开局()
    {
        // 规格 Scenario：本机玩家带入备用子后按 q 并确认退出，随后再次启动
        // → 系统提示上一局中途退出、备用子已丢失，补给点不变，在途记录清除后再进入补给选择。
        using var temp = new TempProfile();
        temp.Write(TempProfile.Json(points: 6, spare: 1, draft: 0, commission: 0));
        CarryProfileStore first = temp.Store();
        first.Load();
        first.Begin("quit-me", CarryFixtures.Spare);
        Assert.Equal(0, first.Current.StockOf(SupplyKind.SpareStone));

        // 中途退出：进程不经结算就结束，在途记录留在档案里。下一次启动另起一个存取。
        CarryProfileStore next = temp.Store();
        next.Load();
        Assert.Equal(new CarryInFlight("quit-me", CarryFixtures.Spare), next.Current.InFlight);

        CarryInFlight? abandoned = next.SettleAbandoned();

        Assert.Equal(new CarryInFlight("quit-me", CarryFixtures.Spare), abandoned);
        Assert.Equal(0, next.Current.StockOf(SupplyKind.SpareStone));
        Assert.Equal(6, next.Current.Points);
        Assert.Null(next.Current.InFlight);
        Assert.Equal(TempProfile.Json(points: 6, spare: 0, draft: 0, commission: 0), temp.Read());

        // 没有残留时什么都不做。
        Assert.Null(next.SettleAbandoned());
    }

    [Fact]
    public void 截断局不结算()
    {
        // 规格 Scenario：以 turn_limit 截断的对局没有名次 → 每名玩家的带出结算为"未结算"，不计入任何带出。
        // 本段守两层：结算纯函数给出全员未结算、0 点、不返还；存取层拒绝把"未结算"写进档案（批量跑局本就不读写档案）。
        var carries = new Dictionary<PlayerId, CarryIn> { [CarryFixtures.Four[0]] = CarryFixtures.Spare };
        var settled = CarryOutSettlement.Settle(null, [], carries, CarryFixtures.Four);
        Assert.All(settled.Values, r => Assert.Equal((CarryOutcome.Unsettled, (int?)null, 0, false), (r.Outcome, r.Rank, r.Points, r.Returned)));

        using var temp = new TempProfile();
        temp.Write(TempProfile.Json(points: 6, spare: 0, draft: 0, commission: 0, inFlight: "{\"matchId\":\"cut\",\"supply\":\"SpareStone\",\"type\":null}"));
        string before = temp.Read();
        CarryProfileStore store = temp.Store();
        store.Load();

        Assert.Throws<ArgumentException>(() => store.Settle("cut", settled[CarryFixtures.Four[0]]));
        Assert.Equal(before, temp.Read());
    }

    [Fact]
    public void 重复结算被忽略()
    {
        // 规格 Scenario：同一局的完赛结算被请求两次 → 补给点只增加一次。结算以在途记录的对局标识为准，标识不匹配也忽略。
        using var temp = new TempProfile();
        temp.Write(TempProfile.Json(points: 0, spare: 1, draft: 0, commission: 0));
        CarryProfileStore store = temp.Store();
        store.Load();
        store.Begin("m-1", CarryFixtures.Spare);
        CarryOutResult first = CarryOutSettlement.Finished(4, 1, CarryFixtures.Spare);

        Assert.False(store.Settle("other", first));
        Assert.Equal(0, store.Current.Points);

        Assert.True(store.Settle("m-1", first));
        Assert.False(store.Settle("m-1", first));

        Assert.Equal(24, store.Current.Points);
        Assert.Equal(24, CarryProfile.Parse(temp.Read()).Points);
    }
}

/// <summary>带出结算写入档案（carry-in-out「完赛结算」「弃赛结算」「出局结算」的档案一侧：点数加入档案、按结算返还或丢失补给、清除在途记录）。</summary>
public class 带出结算写入档案Tests
{
    private static CarryProfileStore Begun(TempProfile temp, CarryIn? carry, int points = 0)
    {
        temp.Write(TempProfile.Json(points: points, spare: 1, draft: 1, commission: 1));
        CarryProfileStore store = temp.Store();
        store.Load();
        store.Begin("m", carry);
        return store;
    }

    [Fact]
    public void 完赛第1名写入档案且不返还()
    {
        // 规格「完赛结算」Scenario：带入备用子、终局第 1 → 补给点 +24，备用子不返还，在途记录清除。
        using var temp = new TempProfile();
        CarryProfileStore store = Begun(temp, CarryFixtures.Spare);

        Assert.True(store.Settle("m", CarryOutSettlement.Finished(4, 1, CarryFixtures.Spare)));

        Assert.Equal((24, 0, (CarryInFlight?)null), (store.Current.Points, store.Current.StockOf(SupplyKind.SpareStone), store.Current.InFlight));
    }

    [Fact]
    public void 弃赛写入档案并返还()
    {
        // 规格「弃赛结算」Scenario「弃赛 50%」：弃赛名次第 2、第 6 大回合、带入换型令 → 带出 ⌊16 / 2⌋ = 8，换型令库存 +1。
        using var temp = new TempProfile();
        CarryProfileStore store = Begun(temp, CarryFixtures.Commission(PieceType.Line));
        Assert.Equal(0, store.Current.StockOf(SupplyKind.Commission));

        Assert.True(store.Settle("m", CarryOutSettlement.Resigned(4, 2, 6, CarryFixtures.Commission(PieceType.Line))));

        Assert.Equal((8, 1, (CarryInFlight?)null), (store.Current.Points, store.Current.StockOf(SupplyKind.Commission), store.Current.InFlight));
    }

    [Fact]
    public void 出局写入档案且丢失()
    {
        // 规格「出局结算」Scenario「出局丢失」：带入换型令出局 → 换型令不返还，补给点不增加，在途记录清除。
        using var temp = new TempProfile();
        CarryProfileStore store = Begun(temp, CarryFixtures.Commission(PieceType.Line), points: 3);

        Assert.True(store.Settle("m", CarryOutSettlement.Eliminated(CarryFixtures.Commission(PieceType.Line))));

        Assert.Equal((3, 0, (CarryInFlight?)null), (store.Current.Points, store.Current.StockOf(SupplyKind.Commission), store.Current.InFlight));
    }

    [Fact]
    public void 结算的补给与在途记录不一致即拒绝()
    {
        // 结算结果里的带入必须就是在途记录里的那一件：不一致说明入口接线错了，响亮失败，档案不变。
        using var temp = new TempProfile();
        CarryProfileStore store = Begun(temp, CarryFixtures.Spare);
        string before = temp.Read();

        Assert.Throws<ArgumentException>(() => store.Settle("m", CarryOutSettlement.Resigned(4, 1, 6, CarryFixtures.Commission(PieceType.Line))));
        Assert.Throws<ArgumentException>(() => store.Settle("m", CarryOutSettlement.Finished(4, 1, null)));
        Assert.Equal(before, temp.Read());
    }
}
