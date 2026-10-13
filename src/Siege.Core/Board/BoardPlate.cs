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
/// 出生棋盘的主题（link-map-terrain 裁决 M2 / D2）：决定该块棋盘中间带的地形。同一张链接档地图的 5 块出生棋盘主题两两不同。
/// 主题只用于生成、校验与呈现，不进入任何结算；地图文件里按枚举名读写，未识别的取值拒绝加载。
/// </summary>
public enum BoardTheme
{
    /// <summary>高台。</summary>
    Plateau = 0,

    /// <summary>河流。</summary>
    River = 1,

    /// <summary>林地。</summary>
    Forest = 2,

    /// <summary>栅栏阵。</summary>
    Palisade = 3,

    /// <summary>混合。</summary>
    Mixed = 4,
}

/// <summary>主题的中文显示名（错误信息、图例、终端输出共用的唯一映射）。</summary>
public static class BoardThemeNames
{
    /// <summary>该主题的中文名。</summary>
    public static string DisplayName(this BoardTheme theme) => theme switch
    {
        BoardTheme.Plateau => "高台",
        BoardTheme.River => "河流",
        BoardTheme.Forest => "林地",
        BoardTheme.Palisade => "栅栏阵",
        BoardTheme.Mixed => "混合",
        _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, "未知主题。"),
    };
}

/// <summary>
/// 棋盘清单的一项（board-map D2）：一块轴对齐矩形棋盘的外接矩形（左下角坐标、宽、高）、类别与主题。
/// 清单只用于静态校验与呈现，MUST NOT 参与气、提子、覆盖、保护期、计分与信物的任何结算——
/// 对局规则、AI 与结算流程都不读它。通道不入清单。
/// </summary>
/// <remarks>规格：openspec/changes/board-map/specs/map-definition —— Requirement: 棋盘清单</remarks>
/// <param name="Origin">左下角格。</param>
/// <param name="Width">宽（列数）。</param>
/// <param name="Height">高（行数）。</param>
/// <param name="Kind">类别。</param>
/// <param name="Theme">主题（link-map-terrain D2）：链接档的出生棋盘 MUST 带主题，其他类别 MUST NOT 带；手写测试夹具可以缺省（<c>null</c>）。拼接平移经 <c>with</c> 原样保留。</param>
public readonly record struct BoardPlate(Coord Origin, int Width, int Height, BoardPlateKind Kind, BoardTheme? Theme = null)
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
