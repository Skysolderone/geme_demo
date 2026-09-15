using Siege.Core.Board;
using Siege.Presentation.Layers;
using Siege.Presentation.Preview;
using Siege.Presentation.Style;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.VisualStyleBaseline;

/// <summary>规格：visual-style-baseline —— Requirement: 批次预览与信息层的视觉表现</summary>
public class 批次预览与信息层的视觉表现Tests
{
    [Fact]
    public void 暂放与提子的视觉区分()
    {
        // 设计文档 §20：暂放棋子半透明发光，预计提子用虚线 / 轮廓，二者可区分。
        // 数据层：同一预演里同时出现暂放与预计提子两类高亮，坐标不相交、视觉手法不同、暂放单独一层。发光与虚线的实际渲染归阶段 B + 人工检查清单。
        // 变异验证 M-VP1：VisualLayering.StyleOf(PredictedCapture) 改为 HighlightStyle.TranslucentGlow → 本测试红 1。
        GameBoard board = TestMaps.Blank(size: 9)
            .Place("C1", P1).Place("D1", P1).Place("E1", P1).Place("F1", P1).Place("G1", P1)
            .Place("B1", P0).Place("H1", P0).Place("C2", P0).Place("E2", P0).Place("F2", P0).Place("G2", P0);
        PreviewPresentation shown = PreviewPresentation.Build(
            RichPreview(board, P0, Roster(P0, P1), limit: 3, stock: null, history: null, ("D2", PieceType.Basic)), EmptyHand(P0), LibertyThresholds.Default);

        Coord[] staged = [.. shown.Highlights.Where(h => h.Kind == HighlightKind.Staged).Select(h => h.Coord)];
        Coord[] captured = [.. shown.Highlights.Where(h => h.Kind == HighlightKind.PredictedCapture).Select(h => h.Coord)];
        Assert.Equal(["D2"], staged.Notations());
        Assert.Equal(5, captured.Length);
        Assert.Empty(staged.Intersect(captured));

        Assert.Equal(HighlightStyle.TranslucentGlow, VisualLayering.StyleOf(HighlightKind.Staged));
        Assert.Equal(HighlightStyle.DashedOutline, VisualLayering.StyleOf(HighlightKind.PredictedCapture));
        Assert.Equal(Enum.GetValues<HighlightKind>().Length, Enum.GetValues<HighlightKind>().Select(VisualLayering.StyleOf).Distinct().Count());
        Assert.NotEqual(VisualLayering.LayerOf(HighlightKind.Staged), VisualLayering.LayerOf(HighlightKind.PredictedCapture));
    }

    [Fact]
    public void 信息层降饱和()
    {
        // 设计文档 §20：打开势力层 → 场景饱和度与装饰对比临时压低，势力信息成为焦点；松开后恢复。
        // 数据层：状态机的场景处理随按住 / 松开切换。真实降饱和着色器归阶段 B + 人工检查清单。
        // 变异验证 M-VP2：LayerVisuals.For(TacticalLayer.Power) 改为 SceneTreatment.Default → 本测试红 1。
        var state = new TacticalLayerState();
        Assert.Equal((100, 100), (state.Treatment.SaturationPercent, state.Treatment.DecorationContrastPercent));

        state.Press(TacticalLayer.Power);
        Assert.True(state.Treatment.SaturationPercent < 100);
        Assert.True(state.Treatment.DecorationContrastPercent < 100);
        Assert.Equal(100, state.Treatment.PieceEmphasisPercent);

        state.Release(TacticalLayer.Power);
        Assert.Equal(SceneTreatment.Default, state.Treatment);
    }
}
