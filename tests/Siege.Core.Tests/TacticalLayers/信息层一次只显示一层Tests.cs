using Siege.Presentation.Layers;

namespace Siege.Core.Tests.TacticalLayers;

/// <summary>规格：tactical-layers —— Requirement: 信息层一次只显示一层</summary>
public class 信息层一次只显示一层Tests
{
    [Fact]
    public void 切换即互斥()
    {
        // 裁决 3：已打开盘面层时切换到势力层 → 盘面层自动退出，只显示势力层。按住与点击两种模式都成立（D5：同一状态机）。
        // 按住模式下先松开的是已被替换的盘面键，不应把势力层关掉。
        // 变异验证 M-X1：TacticalLayerState.Press 改为 `Active ??= layer`（已有层时不替换）→ 本测试红 1。
        // merge-board-layer：原先用领地层与气层验证互斥，二者合并后不再是两个层，改用盘面层与势力层——
        // 验证的仍是"切换到另一层则当前层退出"这同一条规则，不是换成更弱的算例。
        var hold = new TacticalLayerState(LayerInputMode.HoldToShow);
        hold.Press(TacticalLayer.Board);
        Assert.Equal(TacticalLayer.Board, hold.Active);
        hold.Press(TacticalLayer.Power);
        Assert.Equal(TacticalLayer.Power, hold.Active);
        hold.Release(TacticalLayer.Board);
        Assert.Equal(TacticalLayer.Power, hold.Active);
        hold.Release(TacticalLayer.Power);
        Assert.Null(hold.Active);

        var toggle = new TacticalLayerState(LayerInputMode.ClickToToggle);
        toggle.Click(TacticalLayer.Board);
        toggle.Click(TacticalLayer.Power);
        Assert.Equal(TacticalLayer.Power, toggle.Active);
        toggle.Click(TacticalLayer.Relics);
        Assert.Equal(TacticalLayer.Relics, toggle.Active);

        // 盘面层的读法切换不得被误当成"换了一层"：它在层显示期间推进，Active 必须一直是盘面层。
        toggle.Click(TacticalLayer.Relics);
        toggle.Click(TacticalLayer.Board);
        toggle.Click(TacticalLayer.Board);
        Assert.Equal(TacticalLayer.Board, toggle.Active);
        Assert.Equal(BoardReading.Groups, toggle.Reading);

        // Active 是单值：结构上不存在"多层可见"的表示
        Assert.Equal(typeof(TacticalLayer?), typeof(TacticalLayerState).GetProperty(nameof(TacticalLayerState.Active))!.PropertyType);
    }
}
