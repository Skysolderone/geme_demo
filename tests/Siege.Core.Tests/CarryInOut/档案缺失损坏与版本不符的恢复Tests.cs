using Siege.Core.Carry;

namespace Siege.Core.Tests.CarryInOut;

/// <summary>规格：carry-in-out —— Requirement: 档案缺失、损坏与版本不符的恢复</summary>
public class 档案缺失损坏与版本不符的恢复Tests
{
    private static readonly string EmptyJson = TempProfile.Json(points: 0, spare: 0, draft: 0, commission: 0);

    [Fact]
    public void 缺失时新建()
    {
        // 规格 Scenario：没有档案，玩家启动对局 → 提示已新建档案（含路径），补给点 0、库存为空，可以不带入照常开始。
        using var temp = new TempProfile();
        CarryProfileStore store = temp.Store();

        CarryProfileLoad load = store.Load();

        Assert.Equal(CarryProfileLoadStatus.Created, load.Status);
        Assert.Contains("已新建档案", load.Message, StringComparison.Ordinal);
        Assert.Contains(temp.FilePath, load.Message, StringComparison.Ordinal);
        Assert.False(store.IsReadOnly);
        Assert.Equal(0, store.Current.Points);
        Assert.All(Supplies.Order, k => Assert.Equal(0, store.Current.StockOf(k)));
        Assert.Null(store.Current.InFlight);
        Assert.Equal(EmptyJson, temp.Read());

        // 不带入照常开始：在途记录写入，库存不变。
        store.Begin("m-1", null);
        Assert.Equal(new CarryInFlight("m-1", null), store.Current.InFlight);
    }

    [Fact]
    public void 缺失时连同目录一起新建()
    {
        // 真实首跑：用户数据目录下还没有 Siege 目录（缺省路径的父目录不存在）→ 连目录一起新建，不抛异常。
        using var temp = new TempProfile();
        string path = Path.Combine(temp.Directory, "Siege", "profile.json");

        CarryProfileLoad load = new CarryProfileStore(path, TempProfile.Clock).Load();

        Assert.Equal(CarryProfileLoadStatus.Created, load.Status);
        Assert.Equal(EmptyJson, File.ReadAllText(path));
    }

    [Fact]
    public void 损坏时备份并重置()
    {
        // 规格 Scenario：档案内容为截断的半个 JSON，玩家在 2026-09-26 11:40:00 启动对局
        // → 原文件被改名为 profile.json.corrupt-20260926-114000，系统以空档案继续并提示备份路径。
        using var temp = new TempProfile();
        const string half = "{\"version\":1,\"points\":17,\"inven";
        temp.Write(half);

        CarryProfileLoad load = temp.Store().Load();

        string backup = Path.Combine(temp.Directory, "profile.json.corrupt-20260926-114000");
        Assert.Equal(CarryProfileLoadStatus.CorruptReset, load.Status);
        Assert.Equal(backup, load.BackupPath);
        Assert.Equal(half, File.ReadAllText(backup));
        Assert.Equal(EmptyJson, temp.Read());
        Assert.Contains("档案损坏，已备份并重置", load.Message, StringComparison.Ordinal);
        Assert.Contains(backup, load.Message, StringComparison.Ordinal);
        Assert.Equal(["profile.json", "profile.json.corrupt-20260926-114000"], temp.Files());
    }

    [Fact]
    public void 负数视为损坏()
    {
        // 规格 Scenario：档案中补给点为 −5 → 按损坏处理：备份、重置为空档案、提示用户。
        using var temp = new TempProfile();
        string negative = TempProfile.Json(points: -5, spare: 1, draft: 0, commission: 0);
        temp.Write(negative);
        CarryProfileStore store = temp.Store();

        CarryProfileLoad load = store.Load();

        Assert.Equal(CarryProfileLoadStatus.CorruptReset, load.Status);
        Assert.Equal(negative, File.ReadAllText(load.BackupPath!));
        Assert.Equal(0, store.Current.Points);
        Assert.Equal(0, store.Current.StockOf(SupplyKind.SpareStone));
        Assert.Equal(EmptyJson, temp.Read());
    }

    [Theory]
    [InlineData("不是 JSON")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"version\":1,\"inventory\":{},\"inFlight\":null}")]                                                        // 缺 points
    [InlineData("{\"points\":3,\"inventory\":{},\"inFlight\":null}")]                                                          // 缺 version
    [InlineData("{\"version\":1,\"points\":3,\"inFlight\":null}")]                                                             // 缺 inventory
    [InlineData("{\"version\":1,\"points\":3,\"inventory\":{\"SpareStone\":-1},\"inFlight\":null}")]                          // 库存为负
    [InlineData("{\"version\":1,\"points\":3,\"inventory\":{\"Multiplier\":1},\"inFlight\":null}")]                           // 未知补给名
    [InlineData("{\"version\":1,\"points\":3.5,\"inventory\":{},\"inFlight\":null}")]                                         // 非整数
    [InlineData("{\"version\":0,\"points\":3,\"inventory\":{},\"inFlight\":null}")]                                           // 非法版本
    [InlineData("{\"version\":1,\"points\":3,\"points\":4,\"inventory\":{},\"inFlight\":null}")]                              // 重复键
    [InlineData("{\"version\":1,\"points\":3,\"inventory\":{},\"inFlight\":null,\"level\":9}")]                               // 未知字段（档案不得有成长 / 等级）
    [InlineData("{\"version\":1,\"points\":3,\"inventory\":{},\"inFlight\":{\"matchId\":\"m\",\"supply\":\"Gold\",\"type\":null}}")]      // 在途补给名未知
    [InlineData("{\"version\":1,\"points\":3,\"inventory\":{},\"inFlight\":{\"matchId\":\"m\",\"supply\":\"Commission\",\"type\":null}}")] // 换型令缺类型
    [InlineData("{\"version\":1,\"points\":3,\"inventory\":{},\"inFlight\":{\"matchId\":\"m\",\"supply\":\"SpareStone\",\"type\":\"Line\"}}")] // 备用子带类型
    [InlineData("{\"version\":1,\"points\":3,\"inventory\":{},\"inFlight\":{\"matchId\":\"\",\"supply\":null,\"type\":null}}")]           // 空对局标识
    public void 非法内容视为损坏(string content)
    {
        // 规格正文：无法解析、缺必需字段、含负数或未知补给名等非法值 → 备份后以空档案开始。
        using var temp = new TempProfile();
        temp.Write(content);

        CarryProfileLoad load = temp.Store().Load();

        Assert.Equal(CarryProfileLoadStatus.CorruptReset, load.Status);
        Assert.Equal(content, File.ReadAllText(load.BackupPath!));
        Assert.Equal(EmptyJson, temp.Read());
    }

    [Fact]
    public void 同一秒内再次损坏不覆盖先前的备份()
    {
        // MUST NOT 在没有备份的情况下覆盖无法解析的档案：备份名已被占用时另取名字，先前的备份原样保留。
        using var temp = new TempProfile();
        temp.Write("第一份坏档");
        temp.Store().Load();
        temp.Write("第二份坏档");

        CarryProfileLoad second = temp.Store().Load();

        string first = Path.Combine(temp.Directory, "profile.json.corrupt-20260926-114000");
        Assert.Equal("第一份坏档", File.ReadAllText(first));
        Assert.NotEqual(first, second.BackupPath);
        Assert.Equal("第二份坏档", File.ReadAllText(second.BackupPath!));
        Assert.Equal(3, temp.Files().Length);
    }

    [Fact]
    public void 新版本档案不覆盖()
    {
        // 规格 Scenario：档案的 version 为 2，而程序只支持 1 → 档案文件保持不变，本局不提供带入、局终不结算，并提示用户档案来自更新的版本。
        using var temp = new TempProfile();
        string newer = "{\"version\":2,\"points\":40,\"inventory\":{\"SpareStone\":1,\"Talisman\":3},\"inFlight\":null,\"streak\":2}";
        temp.Write(newer);
        CarryProfileStore store = temp.Store();

        CarryProfileLoad load = store.Load();

        Assert.Equal(CarryProfileLoadStatus.NewerVersion, load.Status);
        Assert.Equal(2, load.FileVersion);
        Assert.Contains("更新的版本", load.Message, StringComparison.Ordinal);
        Assert.True(store.IsReadOnly);

        // 只读：任何写入都响亮拒绝，不静默；文件逐字节不变，也不产生备份。
        Assert.Throws<InvalidOperationException>(() => store.TryExchange(SupplyKind.SpareStone, out _));
        Assert.Throws<InvalidOperationException>(() => store.Begin("m-1", null));
        Assert.Throws<InvalidOperationException>(() => store.SettleAbandoned());
        Assert.Throws<InvalidOperationException>(() => store.Settle("m-1", CarryOutSettlement.Finished(4, 1, null)));
        Assert.Equal(newer, temp.Read());
        Assert.Equal(["profile.json"], temp.Files());
    }
}
