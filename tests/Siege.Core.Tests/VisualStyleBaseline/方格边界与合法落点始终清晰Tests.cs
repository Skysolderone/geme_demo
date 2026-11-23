using Siege.Core.Board;
using Siege.Presentation.Layers;
using Siege.Presentation.Preview;
using Siege.Presentation.Style;
using Siege.Presentation.Visibility;

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

    [Fact]
    public void 未架桥深水上的棋子挂水纹环()
    {
        // terrain-bonus 段 B 裁决 6：站在未架桥深水上的棋子挂水纹环；桥格上的棋子、空的深水格、其他地表上的棋子都不挂。判据只在视图模型 BoardCellView.WaterRing。
        // 变异验证（段 B 阶段 2 实跑）：WaterRing 去掉 "!HasBridge" → 本条红 1；去掉 "Occupant is not null" → 本条红 1；Godot 不读 WaterRing（改成恒 false）→ 本条红 1。
        var stone = new Occupant(new PlayerId(0), PieceType.Basic);
        BoardCellView Cell(Surface surface, bool bridge, Occupant? occupant) =>
            new(new Coord(3, 3), Terrain.Playable, 0, surface, bridge, null, occupant, RelicMarker.None, null, null);

        Assert.True(Cell(Surface.DeepWater, bridge: false, stone).WaterRing);
        Assert.False(Cell(Surface.DeepWater, bridge: true, stone).WaterRing);
        Assert.False(Cell(Surface.DeepWater, bridge: false, null).WaterRing);
        Assert.False(Cell(Surface.Rock, bridge: false, stone).WaterRing);
        Assert.False(Cell(Surface.Shallows, bridge: false, stone).WaterRing);

        // Godot 只经视图模型取用（src/godot 不在 sln 里，源码扫描）：棋子同步按 cell.WaterRing 挂环，有无环变了整枚重建。
        string boardView = File.ReadAllText(Path.Combine(TestPaths.RepoRoot(), "src", "godot", "scripts", "BoardView.cs"));
        Assert.Contains("cell.WaterRing);", boardView, StringComparison.Ordinal);
        Assert.Contains("piece.AddChild(WaterRingOf());", boardView, StringComparison.Ordinal);
        Assert.Contains("shown.WaterRing == waterRing", boardView, StringComparison.Ordinal);
    }
}
