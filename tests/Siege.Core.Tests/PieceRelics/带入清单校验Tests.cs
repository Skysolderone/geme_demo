using System.Collections.Immutable;
using Siege.Core.PieceRelics;

namespace Siege.Core.Tests.PieceRelics;

/// <summary>
/// relic-redesign RQ4 / AC4（校验部分）：带入至多 5 件、其中至多 2 件保险、每种不超过库存（design.md D5）；带入 6 件或保险 3 件被拒。
/// "未挂出的带入信物不产生效果"见 <see cref="五种信物的计分Tests.未挂出的带入信物不产生效果"/>；"保护期内可挂出"属于部署挂出，接线段做。
/// 变异验证（加法段实跑）：M9 保险件数上限放宽到 12 → 红 4（本类三条 + 档案损坏用例里的在途保险 3 件）。
/// </summary>
public class 带入清单校验Tests
{
    private static RelicProfile Stocked(int each = 3) =>
        RelicProfile.Empty with { Inventory = RelicKinds.Order.ToImmutableSortedDictionary(k => k, _ => each) };

    private static RelicLoadout Loadout(params (RelicKind Kind, bool Insured)[] items) =>
        new([.. items.Select(i => new CarriedRelic(i.Kind, i.Insured))]);

    [Fact]
    public void 五件含两件保险合规()
    {
        RelicLoadout loadout = Loadout(
            (RelicKind.WarDrum, true), (RelicKind.Command, true), (RelicKind.Watchtower, false), (RelicKind.Chameleon, false), (RelicKind.Banner, false));

        Assert.Null(loadout.ShapeRefusal());
        Assert.Null(loadout.RefusalAgainst(Stocked()));
        Assert.Equal(5, loadout.Count);
        Assert.Equal(2, loadout.InsuredCount);
    }

    [Fact]
    public void 带入六件被拒()
    {
        RelicLoadout loadout = Loadout(
            (RelicKind.WarDrum, false), (RelicKind.WarDrum, false), (RelicKind.Command, false),
            (RelicKind.Command, false), (RelicKind.Watchtower, false), (RelicKind.Watchtower, false));

        Assert.Contains("至多带入 5 件", loadout.ShapeRefusal());
        Assert.NotNull(loadout.RefusalAgainst(Stocked()));
    }

    [Fact]
    public void 保险三件被拒()
    {
        RelicLoadout loadout = Loadout((RelicKind.WarDrum, true), (RelicKind.Command, true), (RelicKind.Watchtower, true));

        Assert.Contains("至多 2 件可勾为保险", loadout.ShapeRefusal());
        Assert.NotNull(loadout.RefusalAgainst(Stocked()));
    }

    [Fact]
    public void 库存不足被拒()
    {
        RelicLoadout loadout = Loadout((RelicKind.Banner, false), (RelicKind.Banner, false));
        RelicProfile oneBanner = RelicProfile.Empty with { Inventory = ImmutableSortedDictionary<RelicKind, int>.Empty.Add(RelicKind.Banner, 1) };

        Assert.Null(loadout.ShapeRefusal());
        Assert.Contains("军旗库存不足", loadout.RefusalAgainst(oneBanner));
        Assert.Null(loadout.RefusalAgainst(Stocked(2)));
    }

    [Fact]
    public void 不带入总是合规()
    {
        Assert.Null(RelicLoadout.Empty.RefusalAgainst(RelicProfile.Empty));
        Assert.Empty(RelicLoadout.Empty.ToTokens(TestMaps.P0, firstId: 0));
    }

    [Fact]
    public void 清单按次序变成本局信物()
    {
        RelicLoadout loadout = Loadout((RelicKind.Banner, true), (RelicKind.WarDrum, false));

        ImmutableArray<RelicToken> tokens = loadout.ToTokens(TestMaps.P2, firstId: 7);

        Assert.Equal([RelicToken.Carried(7, RelicKind.Banner, TestMaps.P2, insured: true), RelicToken.Carried(8, RelicKind.WarDrum, TestMaps.P2, insured: false)], tokens);
    }

    [Fact]
    public void 不合规清单不能变成本局信物()
    {
        RelicLoadout loadout = Loadout((RelicKind.WarDrum, true), (RelicKind.Command, true), (RelicKind.Watchtower, true));

        Assert.Throws<InvalidOperationException>(() => loadout.ToTokens(TestMaps.P0, firstId: 0));
    }

    [Fact]
    public void 开局校验不过时档案不变()
    {
        using var temp = new TempRelicProfile();
        RelicProfileStore store = temp.Store();
        store.Load();
        string before = temp.Read();

        Assert.Throws<InvalidOperationException>(() => store.Begin("m1", Loadout((RelicKind.Banner, false))));
        Assert.Throws<InvalidOperationException>(() => store.Begin("m1", Loadout((RelicKind.WarDrum, true), (RelicKind.Command, true), (RelicKind.Watchtower, true))));

        Assert.Equal(before, temp.Read());
        Assert.Null(store.Current.InFlight);
    }

    [Fact]
    public void 开局扣除全部带入件并写在途记录()
    {
        using var temp = new TempRelicProfile();
        RelicProfileStore store = temp.Store();
        store.Load();

        store.Begin("m1", Loadout((RelicKind.WarDrum, true), (RelicKind.Watchtower, false)));

        Assert.Equal(0, store.Current.StockOf(RelicKind.WarDrum));
        Assert.Equal(1, store.Current.StockOf(RelicKind.Command));
        Assert.Equal(0, store.Current.StockOf(RelicKind.Watchtower));
        Assert.Equal(new RelicInFlight("m1", [new(RelicKind.WarDrum, true), new(RelicKind.Watchtower, false)]), store.Current.InFlight);
        Assert.Equal(
            TempRelicProfile.JsonV2(0, drum: 0, command: 1, watchtower: 0,
                inFlight: "{\"matchId\":\"m1\",\"carried\":[{\"kind\":\"WarDrum\",\"insured\":true},{\"kind\":\"Watchtower\",\"insured\":false}]}"),
            temp.Read());
    }
}
