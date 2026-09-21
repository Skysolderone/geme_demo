using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Relics;

namespace Siege.Presentation.Visibility;

/// <summary>默认棋盘上的信物标记。未发现的信物统一为 <see cref="Unknown"/>，不区分强度预算分区（tactical-ui 裁决 5）。</summary>
public enum RelicMarker
{
    /// <summary>不是信物格。</summary>
    None,

    /// <summary>未知信物。</summary>
    Unknown,

    /// <summary>已揭示信物。</summary>
    Revealed,
}

/// <summary>
/// 默认棋盘的一格：地形（可落子 / 障碍、高度、地表、桥）、出生区、正式盘面上的棋子与信物标记。
/// 结构上<b>没有</b>分区 / 预算档位字段——分区只在信物层可见；也没有暂放字段——暂放由本人的预演呈现单独叠加。
/// </summary>
/// <param name="Terrain">可落子 / 障碍（未架桥深水按障碍报，见 <see cref="MapData.TerrainAt"/>）。</param>
/// <param name="Height">高度 0–2（terrain-model）：Godot 按它堆叠地砖与抬升落点，不自己推。</param>
/// <param name="Surface">地表：草地 / 土路 / 林地 / 深水。</param>
/// <param name="HasBridge">
/// 深水格上<b>当前</b>是否有桥（有桥即可落子）。地图预置的桥与本局架起的桥在这里没有区别——
/// visual-style-baseline「新旧设施同形」要求二者外观一致，information-visibility「改造结果公开」要求不标记改造者。
/// </param>
/// <param name="RevealedType">已揭示信物的类型；未揭示或非信物格为 <c>null</c>。</param>
public sealed record BoardCellView(
    Coord Coord,
    Terrain Terrain,
    int Height,
    Surface Surface,
    bool HasBridge,
    int? BirthZone,
    Occupant? Occupant,
    RelicMarker Relic,
    RelicType? RevealedType);

/// <summary>默认棋盘（不打开任何信息层时）。</summary>
/// <param name="Fences">
/// <b>当前</b>全部栅栏边（无序格对，terrain-model 边属性）：Godot 沿两格公共边立起，不占任一格的落点。
/// 预置栅栏与本局立起的栅栏混在一起，不可区分——这是 visual-style-baseline「新旧设施同形」要的结果。
/// </param>
/// <param name="Edits">
/// 本局<b>已完成</b>的改造（<see cref="GameBoard.TerrainEdits"/> 的投影，无归属、不含改造者，R-3）。
/// 只有一个用途：让渲染层能认出"这一帧刚多出来的那条改造"，给一次落成反馈（visual-style-baseline「改造的可视表现」）。
/// MUST NOT 据此把新设施画得与预置设施不同——设施本身一律从 <see cref="Fences"/> / <see cref="BoardCellView.HasBridge"/> /
/// <see cref="BoardCellView.Surface"/> 读，那三处根本分不出新旧。
/// </param>
public sealed record DefaultBoardView(
    int Width,
    int Height,
    ImmutableArray<BoardCellView> Cells,
    ImmutableArray<FenceEdge> Fences,
    ImmutableArray<TerrainEdit> Edits)
{
    /// <summary>某格。</summary>
    public BoardCellView CellAt(Coord coord) =>
        Cells.FirstOrDefault(c => c.Coord == coord) ?? throw new ArgumentOutOfRangeException(nameof(coord), coord.ToNotation(), "坐标超出棋盘范围。");

    /// <summary>从公开世界构建：只读盘面格子视图与信物公开状态。</summary>
    public static DefaultBoardView From(PublicWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        GameBoard board = world.View.Board;
        var relics = world.View.Relics.ToDictionary(r => r.Coord);
        ImmutableArray<BoardCellView> cells =
        [
            .. board.AllCoords().Select(c =>
            {
                Cell cell = board[c];
                RelicMarker marker = RelicMarker.None;
                RelicType? revealed = null;
                if (relics.TryGetValue(c, out RelicPublicState? state))
                {
                    marker = state.IsRevealed ? RelicMarker.Revealed : RelicMarker.Unknown;
                    revealed = state.IsRevealed ? state.Content!.Value.Type : null;
                }

                return new BoardCellView(
                    c, cell.Terrain, board.Map.HeightAt(c), board.Map.SurfaceAt(c), board.Map.HasBridge(c),
                    cell.BirthZone, cell.Occupant, marker, revealed);
            }),
        ];

        return new DefaultBoardView(
            board.Width, board.Height, cells,
            [.. board.Map.TerrainData.Fences.OrderBy(f => f.A).ThenBy(f => f.B)],
            board.TerrainEdits);
    }
}
