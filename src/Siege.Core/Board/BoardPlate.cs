namespace Siege.Core.Board;

/// <summary>棋盘的类别（board-map D2）。</summary>
public enum BoardPlateKind
{
    /// <summary>出生棋盘：与出生区一一对应，第 i 块出生棋盘的格子集合恰等于出生区 i。</summary>
    Birth = 0,

    /// <summary>公共棋盘：不属于任何出生区。</summary>
    Public = 1,
}

/// <summary>
/// 棋盘清单的一项（board-map D2）：一块轴对齐矩形棋盘的外接矩形（左下角坐标、宽、高）与类别。
/// 清单只用于静态校验与呈现，MUST NOT 参与气、提子、覆盖、保护期、计分与信物的任何结算——
/// 对局规则、AI 与结算流程都不读它。通道不入清单。
/// </summary>
/// <remarks>规格：openspec/changes/board-map/specs/map-definition —— Requirement: 棋盘清单</remarks>
/// <param name="Origin">左下角格。</param>
/// <param name="Width">宽（列数）。</param>
/// <param name="Height">高（行数）。</param>
/// <param name="Kind">类别。</param>
public readonly record struct BoardPlate(Coord Origin, int Width, int Height, BoardPlateKind Kind)
{
    /// <summary>该格是否在外接矩形内。</summary>
    public bool Contains(Coord c) =>
        c.X >= Origin.X && c.X < Origin.X + Width && c.Y >= Origin.Y && c.Y < Origin.Y + Height;

    /// <summary>按确定性顺序（先行后列，自下而上）枚举外接矩形内的全部格子。</summary>
    public IEnumerable<Coord> Cells()
    {
        for (int y = Origin.Y; y < Origin.Y + Height; y++)
        {
            for (int x = Origin.X; x < Origin.X + Width; x++)
            {
                yield return new Coord(x, y);
            }
        }
    }
}
