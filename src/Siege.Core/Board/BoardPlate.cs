namespace Siege.Core.Board;

/// <summary>棋盘的类别（board-map D2）。</summary>
public enum BoardPlateKind
{
    /// <summary>出生棋盘：与出生区一一对应，第 i 块出生棋盘的格子集合恰等于出生区 i。</summary>
    Birth = 0,

    /// <summary>公共棋盘：不属于任何出生区（链接档的中立平台，seat-gap-25 E2：带主题、可落子、不可出生）。</summary>
    Public = 1,
}

/// <summary>
/// 出生棋盘的主题（link-map-terrain 裁决 M2 / D2）：决定该块棋盘中间带的地形。map-expand-25 R3：浮空岛的平台主题只取河流 / 林地 / 栅栏阵 / 混合、可重复，高台不再生成（取值保留供读旧文件时报错）。
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
/// 清单用于静态校验与呈现，MUST NOT 参与气、提子、覆盖、计分与信物的任何结算；open-map D2 起唯一例外是构筑期的合法落子范围与改造范围
/// （<c>PublicRules.LegalRange</c> / <c>EditScope</c> 经 <c>MapData.BirthBoard</c> 取整块出生棋盘的格集合作几何基），其余规则、AI 评价与结算流程都不读它。通道不入清单。
/// </summary>
/// <remarks>规格：openspec/changes/board-map/specs/map-definition —— Requirement: 棋盘清单</remarks>
/// <param name="Origin">左下角格。</param>
/// <param name="Width">宽（列数）。</param>
/// <param name="Height">高（行数）。</param>
/// <param name="Kind">类别。</param>
/// <param name="Theme">主题（link-map-terrain D2；seat-gap-25 E2）：链接档的出生棋盘与公共棋盘（中立平台）都 MUST 带主题；手写测试夹具可以缺省（<c>null</c>）。拼接平移经 <c>with</c> 原样保留。</param>
public readonly record struct BoardPlate(Coord Origin, int Width, int Height, BoardPlateKind Kind, BoardTheme? Theme = null)
{
    /// <summary>核心区边长（link-map-terrain D1 / 裁决 M1）：棋盘中央 3×3。</summary>
    public const int CoreSide = 3;

    /// <summary>该格是否在外接矩形内。</summary>
    public bool Contains(Coord c) =>
        c.X >= Origin.X && c.X < Origin.X + Width && c.Y >= Origin.Y && c.Y < Origin.Y + Height;

    /// <summary>
    /// 核心区左下角（link-map-terrain D1）：棋盘原点 + (⌊(W−3)/2⌋, ⌊(H−3)/2⌋)，宽或高为偶数时偏西 / 偏南。
    /// "核心区"的唯一定义：生成器、校验器与呈现都只经 <see cref="CoreOrigin"/> / <see cref="InCore"/> / <see cref="CoreCells"/> 取得，不另算偏移。
    /// </summary>
    public Coord CoreOrigin => new(Origin.X + ((Width - CoreSide) / 2), Origin.Y + ((Height - CoreSide) / 2));

    /// <summary>该格是否在核心区内。</summary>
    public bool InCore(Coord c)
    {
        Coord o = CoreOrigin;
        return c.X >= o.X && c.X < o.X + CoreSide && c.Y >= o.Y && c.Y < o.Y + CoreSide;
    }

    /// <summary>按确定性顺序（先行后列，自下而上）枚举核心区的 9 格。</summary>
    public IEnumerable<Coord> CoreCells()
    {
        Coord o = CoreOrigin;
        for (int y = o.Y; y < o.Y + CoreSide; y++)
        {
            for (int x = o.X; x < o.X + CoreSide; x++)
            {
                yield return new Coord(x, y);
            }
        }
    }

    /// <summary>该格是否在外圈（棋盘最外一圈，link-map-terrain 裁决 M6）。</summary>
    public bool OnRim(Coord c) =>
        Contains(c) && (c.X == Origin.X || c.X == Origin.X + Width - 1 || c.Y == Origin.Y || c.Y == Origin.Y + Height - 1);

    /// <summary>该格是否在中间带：棋盘内、不在外圈、不在核心区。</summary>
    public bool InMiddleBand(Coord c) => Contains(c) && !OnRim(c) && !InCore(c);

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
