using Siege.Core.PieceRelics;

namespace Siege.Core.Tests.PieceRelics;

/// <summary>
/// relic-redesign RQ6 / AC6（design.md D7，R18 / R20）：档案 v2；v1 读入后补给按原价（备用子 3、征召签 2、换型令 4）折点、version 写为 2；
/// 新档案含 3 件普通信物（战鼓 / 号令 / 望台各 1）；8 点兑换 1 件普通、稀有不可兑换。缺失 / 损坏 / 高版本沿用现行口径。
/// 期望 JSON 一律在测试内手写（<see cref="TempRelicProfile.JsonV2"/>），不调用被测的写出。
/// 变异验证（加法段实跑）：M10 v1 迁移不折补给（折点乘 0）→ 红 2。
/// </summary>
public class 信物档案Tests
{
    [Fact]
    public void 新档案含三件普通信物()
    {
        using var temp = new TempRelicProfile();
        RelicProfileStore store = temp.Store();

        RelicProfileLoad load = store.Load();

        Assert.Equal(RelicProfileLoadStatus.Created, load.Status);
        Assert.Equal(TempRelicProfile.JsonV2(0, drum: 1, command: 1, watchtower: 1), temp.Read());
        Assert.Equal(1, store.Current.StockOf(RelicKind.WarDrum));
        Assert.Equal(1, store.Current.StockOf(RelicKind.Command));
        Assert.Equal(1, store.Current.StockOf(RelicKind.Watchtower));
        Assert.Equal(0, store.Current.StockOf(RelicKind.Chameleon));
        Assert.Equal(0, store.Current.StockOf(RelicKind.Banner));
        Assert.Equal(2, store.Current.Version);
    }

    [Fact]
    public void v1档案读入后补给按原价折点且写回版本二()
    {
        // 5 点 + 备用子 1 × 3 + 征召签 2 × 2 + 换型令 1 × 4 = 16 点；信物库存 0；v1 档案不是新档案，不送 3 件。
        using var temp = new TempRelicProfile();
        temp.Write(TempRelicProfile.JsonV1(5, spare: 1, draft: 2, commission: 1));
        RelicProfileStore store = temp.Store();

        RelicProfileLoad load = store.Load();

        Assert.Equal(RelicProfileLoadStatus.Migrated, load.Status);
        Assert.Equal(16, store.Current.Points);
        Assert.Equal(TempRelicProfile.JsonV2(16), temp.Read());
        Assert.Equal(RelicProfileLoadStatus.Loaded, temp.Store().Load().Status);
    }

    [Fact]
    public void v1残留在途记录按中途退出清除()
    {
        // 残留在途记录里的换型令开局时已从库存扣除：按中途退出丢失、带出 0，迁移后不折点。
        using var temp = new TempRelicProfile();
        temp.Write(TempRelicProfile.JsonV1(2, spare: 0, draft: 0, commission: 1,
            inFlight: "{\"matchId\":\"old\",\"supply\":\"Commission\",\"type\":\"Fortress\"}"));
        RelicProfileStore store = temp.Store();

        store.Load();

        Assert.Equal(2 + 4, store.Current.Points);
        Assert.Null(store.Current.InFlight);
        Assert.Equal(TempRelicProfile.JsonV2(6), temp.Read());
    }

    [Fact]
    public void 八点兑换一件普通信物()
    {
        using var temp = new TempRelicProfile();
        temp.Write(TempRelicProfile.JsonV2(8));
        RelicProfileStore store = temp.Store();
        store.Load();

        Assert.True(store.TryExchange(RelicKind.Chameleon, out string? refusal));

        Assert.Null(refusal);
        Assert.Equal(TempRelicProfile.JsonV2(0, chameleon: 1), temp.Read());

        Assert.False(store.TryExchange(RelicKind.WarDrum, out refusal));
        Assert.Contains("补给点不足（需要 8，现有 0）", refusal);
        Assert.Equal(TempRelicProfile.JsonV2(0, chameleon: 1), temp.Read());
    }

    [Fact]
    public void 稀有信物不可兑换()
    {
        using var temp = new TempRelicProfile();
        temp.Write(TempRelicProfile.JsonV2(100));
        RelicProfileStore store = temp.Store();
        store.Load();

        Assert.False(store.TryExchange(RelicKind.Banner, out string? refusal));

        Assert.Contains("稀有", refusal);
        Assert.Equal(TempRelicProfile.JsonV2(100), temp.Read());
    }

    [Fact]
    public void 档案往返含在途记录()
    {
        string json = TempRelicProfile.JsonV2(17, drum: 1, watchtower: 2, banner: 1,
            inFlight: "{\"matchId\":\"m3\",\"carried\":[{\"kind\":\"Banner\",\"insured\":true}]}");

        RelicProfile profile = RelicProfile.Parse(json);

        Assert.Equal(json, profile.ToJson());
        Assert.Equal(new RelicInFlight("m3", [new(RelicKind.Banner, true)]), profile.InFlight);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"version\":2,\"points\":1,\"inventory\":{\"WarDrum\":1},\"inFlight\":null,\"level\":3}")]
    [InlineData("{\"version\":2,\"points\":-1,\"inventory\":{},\"inFlight\":null}")]
    [InlineData("{\"version\":2,\"points\":1,\"inventory\":{\"Dragon\":1},\"inFlight\":null}")]
    [InlineData("{\"version\":2,\"points\":1,\"inventory\":{},\"inFlight\":{\"matchId\":\"m\",\"carried\":[{\"kind\":\"WarDrum\",\"insured\":true},{\"kind\":\"WarDrum\",\"insured\":true},{\"kind\":\"WarDrum\",\"insured\":true}]}}")]
    [InlineData("{\"version\":0,\"points\":1,\"inventory\":{},\"inFlight\":null}")]
    public void 损坏档案先备份再重置为新档案(string corrupt)
    {
        using var temp = new TempRelicProfile();
        temp.Write(corrupt);
        RelicProfileStore store = temp.Store();

        RelicProfileLoad load = store.Load();

        Assert.Equal(RelicProfileLoadStatus.CorruptReset, load.Status);
        Assert.Equal(["profile.json", "profile.json.corrupt-20261027-093000"], temp.Files());
        Assert.Equal(corrupt, File.ReadAllText(load.BackupPath!));
        Assert.Equal(TempRelicProfile.JsonV2(0, drum: 1, command: 1, watchtower: 1), temp.Read());
    }

    [Fact]
    public void 高版本档案只读不写回()
    {
        using var temp = new TempRelicProfile();
        string newer = TempRelicProfile.JsonV2(5, version: 3);
        temp.Write(newer);
        RelicProfileStore store = temp.Store();

        RelicProfileLoad load = store.Load();

        Assert.Equal(RelicProfileLoadStatus.NewerVersion, load.Status);
        Assert.Equal(3, load.FileVersion);
        Assert.True(store.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => store.TryExchange(RelicKind.WarDrum, out _));
        Assert.Equal(newer, temp.Read());
        Assert.Equal(["profile.json"], temp.Files());
    }

    [Fact]
    public void 替换前中断时原档案保持写入前的状态()
    {
        using var temp = new TempRelicProfile();
        temp.Write(TempRelicProfile.JsonV2(8));
        var store = new RelicProfileStore(temp.FilePath, TempRelicProfile.Clock, beforeReplace: _ => throw new IOException("模拟替换前进程终止"));
        store.Load();

        Assert.Throws<IOException>(() => store.TryExchange(RelicKind.WarDrum, out _));

        Assert.Equal(TempRelicProfile.JsonV2(8), temp.Read());
        Assert.Equal(8, store.Current.Points);
    }
}
