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
/// 默认棋盘的一格：地形、出生区、正式盘面上的棋子与信物标记。
/// 结构上<b>没有</b>分区 / 预算档位字段——分区只在信物层可见；也没有暂放字段——暂放由本人的预演呈现单独叠加。
/// </summary>
/// <param name="RevealedType">已揭示信物的类型；未揭示或非信物格为 <c>null</c>。</param>
public sealed record BoardCellView(
    Coord Coord,
    Terrain Terrain,
    int? BirthZone,
    Occupant? Occupant,
    RelicMarker Relic,
    RelicType? RevealedType);

/// <summary>默认棋盘（不打开任何信息层时）。</summary>
public sealed record DefaultBoardView(int Width, int Height, ImmutableArray<BoardCellView> Cells)
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

                return new BoardCellView(c, cell.Terrain, cell.BirthZone, cell.Occupant, marker, revealed);
            }),
        ];

        return new DefaultBoardView(board.Width, board.Height, cells);
    }
}
