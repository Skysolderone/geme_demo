using Siege.Presentation.Layers;
using Siege.Presentation.Preview;
using Siege.Presentation.Style;

namespace Siege.Core.Tests.VisualStyleBaseline;

/// <summary>规格：visual-style-baseline —— Requirement: 方格边界与合法落点始终清晰</summary>
public class 方格边界与合法落点始终清晰Tests
{
    [Fact]
    public void 装饰不遮挡判读()
    {
        // 设计文档 §20 / 裁决 7：可自动化的部分只断言数据层——承载判读信息的渲染层（网格、归属、合法落点、信息层、棋子、预览）与全部预览高亮
        // 都排在装饰层之上；打开任一信息层时装饰对比被压低。树木 / 高低差旁的实际可读性归阶段 B + 人工检查清单。
        // 变异验证 M-G1：VisualLayering.LayerOf(WillReveal) 改为 RenderLayer.Decoration → 本测试红 1。
        Assert.Equal(7, VisualLayering.ReadabilityLayers.Length);
        Assert.All(VisualLayering.ReadabilityLayers, layer => Assert.True(layer > RenderLayer.Decoration, layer.ToString()));
        Assert.Contains(RenderLayer.GridLines, VisualLayering.ReadabilityLayers);
        Assert.Contains(RenderLayer.LegalPlacementMarkers, VisualLayering.ReadabilityLayers);
        Assert.Contains(RenderLayer.TerritoryTint, VisualLayering.ReadabilityLayers);

        Assert.All(Enum.GetValues<HighlightKind>(), kind => Assert.True(VisualLayering.LayerOf(kind) > RenderLayer.Decoration, kind.ToString()));
        Assert.All(Enum.GetValues<TacticalLayer>(), layer => Assert.True(LayerVisuals.For(layer).DecorationContrastPercent < 100, layer.ToString()));
        Assert.Equal(Enum.GetValues<RenderLayer>().Length - 2, VisualLayering.ReadabilityLayers.Distinct().Count());
    }
}
