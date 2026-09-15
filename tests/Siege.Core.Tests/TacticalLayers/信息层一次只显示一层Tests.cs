using Siege.Presentation.Layers;

namespace Siege.Core.Tests.TacticalLayers;

/// <summary>规格：tactical-layers —— Requirement: 信息层一次只显示一层</summary>
public class 信息层一次只显示一层Tests
{
    [Fact]
    public void 切换即互斥()
    {
        // 裁决 3：已打开领地层时切换到气层 → 领地层自动退出，只显示气层。按住与点击两种模式都成立（D5：同一状态机）。
        // 按住模式下先松开的是已被替换的领地键，不应把气层关掉。
        // 变异验证 M-X1：TacticalLayerState.Press 改为 `Active ??= layer`（已有层时不替换）→ 本测试红 1。
        var hold = new TacticalLayerState(LayerInputMode.HoldToShow);
        hold.Press(TacticalLayer.Territory);
        Assert.Equal(TacticalLayer.Territory, hold.Active);
        hold.Press(TacticalLayer.Liberties);
        Assert.Equal(TacticalLayer.Liberties, hold.Active);
        hold.Release(TacticalLayer.Territory);
        Assert.Equal(TacticalLayer.Liberties, hold.Active);
        hold.Release(TacticalLayer.Liberties);
        Assert.Null(hold.Active);

        var toggle = new TacticalLayerState(LayerInputMode.ClickToToggle);
        toggle.Click(TacticalLayer.Territory);
        toggle.Click(TacticalLayer.Liberties);
        Assert.Equal(TacticalLayer.Liberties, toggle.Active);
        toggle.Click(TacticalLayer.Power);
        Assert.Equal(TacticalLayer.Power, toggle.Active);

        // Active 是单值：结构上不存在"多层可见"的表示
        Assert.Equal(typeof(TacticalLayer?), typeof(TacticalLayerState).GetProperty(nameof(TacticalLayerState.Active))!.PropertyType);
    }
}
