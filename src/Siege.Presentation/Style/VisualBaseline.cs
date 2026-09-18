using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Presentation.Layers;
using Siege.Presentation.Preview;

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
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知高亮类别。"),
    };

    /// <summary>预览高亮类别 → 渲染层。暂放棋子单独一层，其余预览提示在棋子之上。</summary>
    public static RenderLayer LayerOf(HighlightKind kind) => kind switch
    {
        HighlightKind.Staged => RenderLayer.StagedPieces,
        HighlightKind.PredictedCapture or HighlightKind.SuicideRisk or HighlightKind.WillReveal or HighlightKind.FailureFocus
            => RenderLayer.PreviewHighlights,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知高亮类别。"),
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
