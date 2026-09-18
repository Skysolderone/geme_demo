using Siege.Core.Board;
using Siege.Presentation.Style;

namespace Siege.Core.Tests.VisualStyleBaseline;

/// <summary>规格：visual-style-baseline —— Requirement: 五种棋子的轮廓语言（artisan-terrain-edit 段 A：类型增至六种）</summary>
public class 六种棋子的轮廓语言Tests
{
    [Fact]
    public void 轮廓可辨()
    {
        // 设计文档 §20 / 裁决 6：普通子简洁圆润（圆头兵）、堡垒子塔楼体块（塔楼）、连珠子表现连接（双球连杆）、倍增子放射状（金字塔）、协同子多节点聚合（多瓣水晶）。
        // 数据层：每种棋子恰有一条标识，几何标识与轮廓语言各自两两不同。去色小尺寸缩略图可辨归阶段 B + 人工检查清单。
        // 变异验证 M-PS1：PieceStyleTable 中协同子的轮廓改为 PieceSilhouette.Pyramid → 本测试红 1。
        Assert.Equal(Enum.GetValues<PieceType>().Order(), PieceStyleTable.All.Select(s => s.Type).Order());
        Assert.Equal(6, PieceStyleTable.All.Select(s => s.Silhouette).Distinct().Count());
        Assert.Equal(6, PieceStyleTable.All.Select(s => s.Language).Distinct().Count());

        Assert.Equal((PieceSilhouette.RoundPawn, SilhouetteLanguage.Rounded), Style(PieceType.Basic));
        Assert.Equal((PieceSilhouette.Tower, SilhouetteLanguage.TowerMass), Style(PieceType.Fortress));
        Assert.Equal((PieceSilhouette.TwinOrbBar, SilhouetteLanguage.Connection), Style(PieceType.Line));
        Assert.Equal((PieceSilhouette.Pyramid, SilhouetteLanguage.Radial), Style(PieceType.Multiplier));
        Assert.Equal((PieceSilhouette.CrystalCluster, SilhouetteLanguage.MultiNode), Style(PieceType.Synergy));
        // artisan-terrain-edit Open Question 3：匠人轮廓建议"工具或支架状"，本段先落占位标识（Scaffold / Tooling），正式外观与截图属于段 C。
        Assert.Equal((PieceSilhouette.Scaffold, SilhouetteLanguage.Tooling), Style(PieceType.Artisan));
    }

    private static (PieceSilhouette, SilhouetteLanguage) Style(PieceType type)
    {
        PieceStyle style = PieceStyleTable.For(type);
        return (style.Silhouette, style.Language);
    }
}
