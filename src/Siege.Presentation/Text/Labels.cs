using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Relics;
using Siege.Core.Scoring;
using Siege.Presentation.Style;

namespace Siege.Presentation.Text;

/// <summary>
/// 面向玩家的名称与文案片段。只做"值 → 文字"的映射，不做任何规则判断。
/// 坐标一律经 <see cref="Coord.ToNotation"/> 输出围棋记法，本类不含第二套坐标映射。
/// </summary>
public static class Labels
{
    /// <summary>两种读法差集的地形原因（tactical-layers「差集可由地形解释」）。</summary>
    public static string TerrainReason(Layers.TerrainReason reason) => reason switch
    {
        Layers.TerrainReason.Cliff => "崖壁",
        Layers.TerrainReason.Fence => "栅栏",
        Layers.TerrainReason.AcrossWater => "隔岸",
        Layers.TerrainReason.Forest => "林地",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "未知地形原因。"),
    };

    /// <summary>棋子类型名称。</summary>
    public static string Piece(PieceType type) => type switch
    {
        PieceType.Basic => "普通子",
        PieceType.Fortress => "堡垒子",
        PieceType.Line => "连珠子",
        PieceType.Multiplier => "倍增子",
        PieceType.Synergy => "协同子",
        PieceType.Artisan => "匠人",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "未知棋子类型。"),
    };

    /// <summary>
    /// 一次地形改造的文案，如「搭桥 D4」「立栅 E5–E6」「烧林 F4」。动作名取 <see cref="Core.Board.TerrainEdit.DisplayName"/>（唯一一份），
    /// 目标用围棋记法；本方法不判断该改造是否合法。
    /// </summary>
    public static string TerrainEdit(TerrainEdit edit) =>
        $"{Core.Board.TerrainEdit.DisplayName(edit.Kind)} {(edit.Kind == TerrainEditKind.Fence ? Edge(edit.Edge) : edit.Cell.ToNotation())}";

    /// <summary>一条边的文案，如「E5–E6」。两端顺序取 <see cref="FenceEdge"/> 归一后的顺序。</summary>
    public static string Edge(FenceEdge edge) => $"{edge.A.ToNotation()}–{edge.B.ToNotation()}";

    /// <summary>信物类型名称。</summary>
    public static string Relic(RelicType type) => type switch
    {
        RelicType.Prospecting => "探勘",
        RelicType.Conscription => "征召",
        RelicType.Depot => "兵站",
        RelicType.Command => "军令",
        RelicType.Vanguard => "先锋",
        RelicType.SchoolEmblem => "流派徽记",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "未知信物类型。"),
    };

    /// <summary>已揭示信物的内容文案，如「军令 +1」「流派徽记·倍增子 ×2」。</summary>
    public static string RelicContent(RelicContent content) =>
        content.Type == RelicType.SchoolEmblem
            ? $"{Relic(content.Type)}·{Piece(content.EmblemPiece!.Value)} ×{content.Magnitude}"
            : $"{Relic(content.Type)} +{content.Magnitude}";

    /// <summary>信物强度预算分区名称（只在信物层出现，tactical-ui 裁决 5）。</summary>
    public static string Zone(RelicZone zone) => zone switch
    {
        RelicZone.BirthZone => "出生区",
        RelicZone.Contested => "公共争夺区",
        _ => throw new ArgumentOutOfRangeException(nameof(zone), zone, "未知分区。"),
    };

    /// <summary>预算档位名称。</summary>
    public static string Budget(BudgetTier tier) => tier switch
    {
        BudgetTier.Birth => "出生档",
        BudgetTier.Standard => "标准档",
        BudgetTier.High => "高档",
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "未知档位。"),
    };

    /// <summary>玩家显示名：阵营名 + 编号，如「红方(P0)」。阵营映射只在 <see cref="FactionTable"/> 一处。</summary>
    public static string Player(PlayerId player) => $"{FactionTable.For(player).Name}({player})";

    /// <summary>参赛状态文案；参赛中为 <c>null</c>。</summary>
    public static string? Status(PlayerStatus status) => status switch
    {
        PlayerStatus.Active => null,
        PlayerStatus.Resigned => "已弃赛 · 不再行动",
        PlayerStatus.Eliminated => "已出局 · 不再行动",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "未知状态。"),
    };

    /// <summary>坐标列表，围棋记法，顿号分隔。</summary>
    public static string Coords(IEnumerable<Coord> coords) => string.Join("、", coords.Select(c => c.ToNotation()));

    /// <summary>同 <see cref="Coords(IEnumerable{Coord})"/>，便于直接传 <see cref="ImmutableArray{T}"/>。</summary>
    public static string Coords(ImmutableArray<Coord> coords) => Coords(coords.AsEnumerable());
}
