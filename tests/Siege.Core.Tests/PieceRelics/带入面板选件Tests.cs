using System.Collections.Immutable;
using Siege.Core.PieceRelics;
using Siege.Presentation.Hud;

namespace Siege.Core.Tests.PieceRelics;

/// <summary>
/// relic-redesign 段 C2（R25 / AC4 图形版口径）：带入面板的选件状态 <see cref="CarryPicker"/>——逐件加入、移除、勾保险，产出清单；
/// 超限（6 件、保险 3 件、超库存）的即时提示与"能否再加 / 再勾"全部委托 Core 的 <see cref="RelicLoadout.RefusalAgainst"/> / <see cref="RelicLoadout.ShapeRefusal"/>，本类不另写规则。
/// </summary>
/// <remarks>
/// 变异验证（C2 实跑，均已恢复）：P1 <c>CarryPicker.Refusal</c> 改为恒返回 <c>null</c> → 红 3（六件、保险三件、超库存）；
/// P2 <c>CanAdd</c> 不含新加的那一件（只看当前清单）→ 红 2（五件时不能再加、库存用尽不能再加）；P3 <c>ToggleInsured</c> 不翻转 → 红 2（勾保险产出清单、保险三件）。
/// </remarks>
public class 带入面板选件Tests
{
    private static RelicProfile Stocked(int each = 3) =>
        RelicProfile.Empty with { Inventory = RelicKinds.Order.ToImmutableSortedDictionary(k => k, _ => each) };

    [Fact]
    public void 逐件加入勾保险与移除产出清单()
    {
        var picker = new CarryPicker();
        picker.Add(RelicKind.WarDrum);
        picker.Add(RelicKind.Command);
        picker.Add(RelicKind.Watchtower);
        Assert.True(picker.ToggleInsured(0));
        Assert.True(picker.Remove(1));

        Assert.Equal("战鼓（保险）、望台", picker.Loadout.ToString());
        Assert.Equal((2, 1, 1, 0), (picker.Count, picker.InsuredCount, picker.CountOf(RelicKind.WarDrum), picker.CountOf(RelicKind.Command)));
        Assert.Null(picker.Refusal(Stocked()));
        Assert.True(picker.ToggleInsured(0));
        Assert.Equal(0, picker.InsuredCount);
    }

    [Fact]
    public void 空清单即不带入()
    {
        var picker = new CarryPicker();

        Assert.Equal(RelicLoadout.Empty, picker.Loadout);
        Assert.Null(picker.Refusal(RelicProfile.Empty));
        Assert.False(picker.Remove(0));
        Assert.False(picker.ToggleInsured(0));
        Assert.False(picker.CanToggleInsured(0));
    }

    [Fact]
    public void 六件被拒且五件时不能再加()
    {
        var picker = new CarryPicker();
        foreach (RelicKind kind in new[] { RelicKind.WarDrum, RelicKind.WarDrum, RelicKind.Command, RelicKind.Command, RelicKind.Watchtower })
        {
            picker.Add(kind);
        }

        Assert.Null(picker.Refusal(Stocked()));
        Assert.False(picker.CanAdd(RelicKind.Watchtower, Stocked()));

        picker.Add(RelicKind.Watchtower);

        string? refusal = picker.Refusal(Stocked());
        Assert.NotNull(refusal);
        Assert.Equal(picker.Loadout.RefusalAgainst(Stocked()), refusal);   // 原因原样来自 Core
        Assert.Contains("至多带入 5 件", refusal);
    }

    [Fact]
    public void 保险三件被拒且两件时第三件不能再勾()
    {
        var picker = new CarryPicker();
        picker.Add(RelicKind.WarDrum);
        picker.Add(RelicKind.Command);
        picker.Add(RelicKind.Watchtower);
        picker.ToggleInsured(0);
        picker.ToggleInsured(1);

        Assert.Null(picker.Refusal(Stocked()));
        Assert.False(picker.CanToggleInsured(2));
        Assert.True(picker.CanToggleInsured(0), "已勾的件总可以取消");

        picker.ToggleInsured(2);

        Assert.Contains("至多 2 件可勾为保险", picker.Refusal(Stocked()));
    }

    [Fact]
    public void 超库存被拒且库存用尽不能再加()
    {
        RelicProfile profile = RelicProfile.Empty.WithStock(RelicKind.Banner, 1);
        var picker = new CarryPicker();
        picker.Add(RelicKind.Banner);

        Assert.Null(picker.Refusal(profile));
        Assert.False(picker.CanAdd(RelicKind.Banner, profile));
        Assert.False(picker.CanAdd(RelicKind.WarDrum, profile), "库存 0 的种类不能加");

        picker.Add(RelicKind.Banner);

        Assert.Contains("军旗库存不足", picker.Refusal(profile));
        picker.Clear();
        Assert.Equal(0, picker.Count);
    }
}
