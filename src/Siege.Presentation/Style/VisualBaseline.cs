using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Presentation.Layers;
using Siege.Presentation.Preview;
using Siege.Presentation.Visibility;

namespace Siege.Presentation.Style;

/// <summary>8 位 RGBA 颜色。整数表示，不经过浮点；Godot 层自行换算为引擎颜色。</summary>
public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)
{
    /// <summary>整数亮度（Rec.601 权重 ×1000 后整除），0–255。只用于风格基准的数据断言。</summary>
    public int Luma => ((299 * R) + (587 * G) + (114 * B)) / 1000;

    /// <summary>十六进制 <c>#RRGGBBAA</c>。</summary>
    public string Hex => $"#{R:X2}{G:X2}{B:X2}{A:X2}";
}

/// <summary>四方主色（设计文档 §20）。</summary>
public enum FactionColor
{
    Red,
    Blue,
    Gold,
    Purple,
}

/// <summary>旗帜图案（tactical-ui 裁决 6）：去色条件下区分阵营的第二通道。</summary>
public enum BannerEmblem
{
    Triangle,
    Tower,
    Sun,
    Lotus,
}

/// <summary>一方阵营的视觉标识：主色 + 旗帜图案 + 名称。</summary>
public sealed record FactionStyle(PlayerId Player, FactionColor Color, Rgba Primary, BannerEmblem Emblem, string Name);

/// <summary>
/// PlayerId 0..3 → 阵营的<b>唯一</b>映射（tactical-ui 裁决 6）。阵营辨识不得只依赖颜色（§20）：每方同时有独立的旗帜图案。
/// </summary>
public static class FactionTable
{
    /// <summary>四方，下标即 PlayerId.Value。</summary>
    public static readonly ImmutableArray<FactionStyle> All =
    [
        new(new PlayerId(0), FactionColor.Red, new Rgba(196, 58, 48), BannerEmblem.Triangle, "红方"),
        new(new PlayerId(1), FactionColor.Blue, new Rgba(52, 104, 196), BannerEmblem.Tower, "蓝方"),
        new(new PlayerId(2), FactionColor.Gold, new Rgba(214, 170, 52), BannerEmblem.Sun, "金方"),
        new(new PlayerId(3), FactionColor.Purple, new Rgba(128, 72, 176), BannerEmblem.Lotus, "紫方"),
    ];

    /// <summary>某玩家的阵营标识；超出 0..3 抛出（原型地图最多 4 人）。</summary>
    public static FactionStyle For(PlayerId player) =>
        player.Value >= 0 && player.Value < All.Length
            ? All[player.Value]
            : throw new ArgumentOutOfRangeException(nameof(player), player, $"阵营表只定义 P0–P{All.Length - 1}。");
}

/// <summary>棋子的程序化几何轮廓标识（tactical-ui 裁决 6）。Godot 层按标识生成低多边形几何体。</summary>
public enum PieceSilhouette
{
    /// <summary>圆头兵。</summary>
    RoundPawn,

    /// <summary>塔楼。</summary>
    Tower,

    /// <summary>双球连杆。</summary>
    TwinOrbBar,

    /// <summary>金字塔。</summary>
    Pyramid,

    /// <summary>多瓣水晶。</summary>
    CrystalCluster,

    /// <summary>支架（匠人）。artisan-terrain-edit Open Question 3：正式轮廓由段 C 定稿并出截图，本段先占位以保证六种类型各有一条标识。</summary>
    Scaffold,

    /// <summary>竖杆方旗（旗手子，more-pieces-relics D11）。段 A 只占位标识，Godot 几何在段 D 定稿。</summary>
    Pennant,

    /// <summary>双环相扣（铁链子）。段 A 只占位标识。</summary>
    ChainLinks,

    /// <summary>交叉双矛（哨兵子）。段 A 只占位标识。</summary>
    CrossedSpears,

    /// <summary>矮宽石碑（界碑子）。段 A 只占位标识。</summary>
    Stele,
}

/// <summary>规格要求的轮廓语言（visual-style-baseline：简洁圆润 / 塔楼体块 / 连接关系 / 放射状 / 多节点聚合）。</summary>
public enum SilhouetteLanguage
{
    Rounded,
    TowerMass,
    Connection,
    Radial,
    MultiNode,

    /// <summary>工具 / 支架感（匠人）。</summary>
    Tooling,

    /// <summary>旗帜 / 竖杆（旗手子，more-pieces-relics D11）。</summary>
    Banner,

    /// <summary>环扣相连（铁链子）。</summary>
    Interlock,

    /// <summary>交叉兵刃（哨兵子）。</summary>
    Crossed,

    /// <summary>低矮碑体（界碑子）。</summary>
    Slab,
}

/// <summary>一种棋子的视觉标识。</summary>
public sealed record PieceStyle(PieceType Type, PieceSilhouette Silhouette, SilhouetteLanguage Language);

/// <summary>六种棋子 → 轮廓的<b>唯一</b>映射。</summary>
public static class PieceStyleTable
{
    public static readonly ImmutableArray<PieceStyle> All =
    [
        new(PieceType.Basic, PieceSilhouette.RoundPawn, SilhouetteLanguage.Rounded),
        new(PieceType.Fortress, PieceSilhouette.Tower, SilhouetteLanguage.TowerMass),
        new(PieceType.Line, PieceSilhouette.TwinOrbBar, SilhouetteLanguage.Connection),
        new(PieceType.Multiplier, PieceSilhouette.Pyramid, SilhouetteLanguage.Radial),
        new(PieceType.Synergy, PieceSilhouette.CrystalCluster, SilhouetteLanguage.MultiNode),
        new(PieceType.Artisan, PieceSilhouette.Scaffold, SilhouetteLanguage.Tooling),
        // more-pieces-relics 段 A 占位：v2 局会出现新棋子，本表查不到即抛；标识按 D11 取，Godot 几何与灰度可辨在段 D 定稿。
        new(PieceType.Bannerman, PieceSilhouette.Pennant, SilhouetteLanguage.Banner),
        new(PieceType.Chain, PieceSilhouette.ChainLinks, SilhouetteLanguage.Interlock),
        new(PieceType.Sentry, PieceSilhouette.CrossedSpears, SilhouetteLanguage.Crossed),
        new(PieceType.Boundary, PieceSilhouette.Stele, SilhouetteLanguage.Slab),
    ];

    public static PieceStyle For(PieceType type) =>
        All.FirstOrDefault(s => s.Type == type) ?? throw new ArgumentOutOfRangeException(nameof(type), type, "未知棋子类型。");
}

/// <summary>
/// 渲染层级，枚举值即绘制顺序（小者先画、在下）。§20：树木、遗迹、水面与高低差只作装饰，
/// MUST NOT 遮挡合法落点、气与领地归属——凡承载判读信息的层都排在 <see cref="Decoration"/> 之上。
/// </summary>
public enum RenderLayer
{
    Terrain = 0,
    Decoration = 1,
    GridLines = 2,
    TerritoryTint = 3,
    LegalPlacementMarkers = 4,
    TacticalOverlay = 5,
    Pieces = 6,
    PreviewHighlights = 7,
    StagedPieces = 8,
}

/// <summary>预览高亮的视觉手法（§20：暂放 = 半透明发光；预计提子 = 清楚但克制的虚线 / 轮廓）。</summary>
public enum HighlightStyle
{
    TranslucentGlow,
    DashedOutline,
    WarningOutline,
    RevealBadge,
    FailureOutline,

    /// <summary>可改造目标：半透明虚线的短栏 / 轮廓，贴在目标格或目标边上，明显比已选目标弱。</summary>
    EditTargetHint,

    /// <summary>已选改造目标：实心亮色的短栏 / 轮廓，与候选在明度和实虚上都分得开。</summary>
    EditChosenMark,

    /// <summary>失败说明里涉及的已确定活形棋串（life-shape）：已活色实线环 + 悬浮"眼"徽记（与棋串读法的已活标记同形），与失败焦点的叉号、自杀手的双警示环在形状上都不同。</summary>
    LifeOutline,
}

/// <summary>视觉层级与高亮手法的数据基准（tactical-ui 裁决 7：可自动化的部分只断言数据层）。</summary>
public static class VisualLayering
{
    /// <summary>承载判读信息（落点 / 气 / 归属 / 棋子 / 预览）的层。</summary>
    public static readonly ImmutableArray<RenderLayer> ReadabilityLayers =
    [
        RenderLayer.GridLines,
        RenderLayer.TerritoryTint,
        RenderLayer.LegalPlacementMarkers,
        RenderLayer.TacticalOverlay,
        RenderLayer.Pieces,
        RenderLayer.PreviewHighlights,
        RenderLayer.StagedPieces,
    ];

    /// <summary>预览高亮类别 → 视觉手法。</summary>
    public static HighlightStyle StyleOf(HighlightKind kind) => kind switch
    {
        HighlightKind.Staged => HighlightStyle.TranslucentGlow,
        HighlightKind.PredictedCapture => HighlightStyle.DashedOutline,
        HighlightKind.SuicideRisk => HighlightStyle.WarningOutline,
        HighlightKind.WillReveal => HighlightStyle.RevealBadge,
        HighlightKind.FailureFocus => HighlightStyle.FailureOutline,
        HighlightKind.EditTarget => HighlightStyle.EditTargetHint,
        HighlightKind.ChosenEdit => HighlightStyle.EditChosenMark,
        HighlightKind.LifeGroup => HighlightStyle.LifeOutline,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知高亮类别。"),
    };

    /// <summary>预览高亮类别 → 渲染层。暂放棋子单独一层，其余预览提示在棋子之上。</summary>
    public static RenderLayer LayerOf(HighlightKind kind) => kind switch
    {
        HighlightKind.Staged => RenderLayer.StagedPieces,
        // 可改造目标与已选目标同在预览层：tactical-layers 要求它们在<b>默认棋盘</b>上就能看到，MUST NOT 依赖打开任何信息层。
        HighlightKind.PredictedCapture or HighlightKind.SuicideRisk or HighlightKind.WillReveal or HighlightKind.FailureFocus
            or HighlightKind.EditTarget or HighlightKind.ChosenEdit or HighlightKind.LifeGroup
            => RenderLayer.PreviewHighlights,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知高亮类别。"),
    };
}

/// <summary>
/// 棋串读法里一条棋串的标记形状（tactical-layers「活形与禁入格的标示」：已活 MUST 与危险、普通可区分，且 MUST NOT 只依赖颜色）。
/// 形状是第一通道，颜色（阵营色 / 危险色 / 已活色）只是第二通道：去色之后三种标记仍各不相同。
/// </summary>
public enum GroupMarkShape
{
    /// <summary>普通棋串：阵营色虚线环。</summary>
    DashedRing,

    /// <summary>危险棋串：危险色实线环。</summary>
    SolidRing,

    /// <summary>已活棋串：已活色实线环 + 每枚子上方悬浮一枚环形"眼"徽记（危险棋串没有这个立体徽记，去色后仍可分）。</summary>
    SolidRingWithEyeBadge,
}

/// <summary>棋串读法标记 → 形状的<b>唯一</b>映射。Godot 按形状选图元，不自行按活形或气数分支。</summary>
public static class GroupMarks
{
    public static GroupMarkShape ShapeOf(GroupMark mark) => mark switch
    {
        GroupMark.Normal => GroupMarkShape.DashedRing,
        GroupMark.Danger => GroupMarkShape.SolidRing,
        GroupMark.Alive => GroupMarkShape.SolidRingWithEyeBadge,
        _ => throw new ArgumentOutOfRangeException(nameof(mark), mark, "未知棋串标记。"),
    };
}

/// <summary>
/// 默认棋盘上"落不下"的三类在外观上的手法（tactical-layers「活形与禁入格的标示」）。
/// 地形不可落子由地形本身（岩石 / 深水）表达，不加标记；超出合法落子范围不加标记（合法落点另有标记，范围外即"没有合法标记"）；
/// 活棋禁入加一枚禁入印记（叉 + 所有者阵营色的方框），三者互不相同。
/// </summary>
public enum PlacementMarkStyle
{
    /// <summary>地形本身即表达（岩石体块 / 深水水面）。</summary>
    TerrainSurface,

    /// <summary>无标记（超出合法落子范围）。</summary>
    NoMarker,

    /// <summary>禁入印记：叉 + 所有者阵营色方框。</summary>
    LifeSeal,
}

/// <summary>落点阻断类别的呈现：手法与指向时的原因文案。</summary>
public static class PlacementBlocks
{
    /// <summary>超出合法落子范围（不在 <see cref="PlacementBlock"/> 里：范围来自合法落子范围契约，不由默认棋盘推断）的手法。</summary>
    public const PlacementMarkStyle OutOfRangeStyle = PlacementMarkStyle.NoMarker;

    public static PlacementMarkStyle StyleOf(PlacementBlock block) => block switch
    {
        PlacementBlock.None => PlacementMarkStyle.NoMarker,
        PlacementBlock.Terrain => PlacementMarkStyle.TerrainSurface,
        PlacementBlock.LifeForbidden => PlacementMarkStyle.LifeSeal,
        _ => throw new ArgumentOutOfRangeException(nameof(block), block, "未知阻断类别。"),
    };

    /// <summary>指向该格时的原因（不含所有者；所有者由 <see cref="Camera.HoverReadout"/> 拼上）。</summary>
    public static string ReasonText(PlacementBlock block) => block switch
    {
        PlacementBlock.None => string.Empty,
        PlacementBlock.Terrain => "地形不可落子",
        PlacementBlock.LifeForbidden => "活棋禁入",
        _ => throw new ArgumentOutOfRangeException(nameof(block), block, "未知阻断类别。"),
    };
}

/// <summary>信息层打开时对场景的临时处理（§20：压低饱和度与装饰对比，只突出当前层）。百分比整数，100 = 原样。</summary>
public sealed record SceneTreatment(int SaturationPercent, int DecorationContrastPercent, int PieceEmphasisPercent)
{
    /// <summary>默认棋盘：不做任何处理。</summary>
    public static readonly SceneTreatment Default = new(100, 100, 100);
}

/// <summary>各信息层的场景处理基准。领地层额外弱化棋子（设计文档 §14.2「弱化棋子效果和演出」）。</summary>
public static class LayerVisuals
{
    /// <summary>
    /// 某层（及盘面层的读法）对应的场景处理。两种读法的处理值不同，不能合并：归属读法弱化棋子演出以便看清格子归属，
    /// 棋串读法必须保持棋子清晰（棋串轮廓就画在棋子上），所以棋子强调是 45% 对 100%。
    /// </summary>
    public static SceneTreatment For(TacticalLayer? layer, BoardReading reading = BoardReading.Ownership) => layer switch
    {
        null => SceneTreatment.Default,
        TacticalLayer.Board when reading == BoardReading.Ownership => new SceneTreatment(40, 45, 45),
        TacticalLayer.Board => new SceneTreatment(45, 45, 100),
        TacticalLayer.Power => new SceneTreatment(45, 45, 100),
        TacticalLayer.Relics => new SceneTreatment(45, 45, 80),
        TacticalLayer.Order => new SceneTreatment(45, 45, 80),
        _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, "未知信息层。"),
    };
}

/// <summary>
/// 默认 UI 的风格基准（§20：深色半透明面板、克制金色边框、高对比信息色、PC 策略游戏信息密度）。
/// 尺寸以 1080p 参考分辨率计，Godot 层按实际分辨率缩放。
/// </summary>
public static class UiTheme
{
    public static readonly Rgba PanelFill = new(18, 20, 26, 208);

    public static readonly Rgba PanelBorder = new(184, 146, 72);

    public static readonly Rgba InfoText = new(236, 232, 220);

    public static readonly Rgba DangerText = new(236, 96, 80);

    /// <summary>边框线宽（px）。</summary>
    public const int BorderWidthPx = 1;

    /// <summary>标准按钮高度（px）。</summary>
    public const int ButtonHeightPx = 30;

    /// <summary>正文字号（px）。</summary>
    public const int BodyFontPx = 15;

    /// <summary>手游式大按钮的下限：任何按钮高度 MUST 低于它。</summary>
    public const int MobileStyleButtonHeightPx = 56;
}
