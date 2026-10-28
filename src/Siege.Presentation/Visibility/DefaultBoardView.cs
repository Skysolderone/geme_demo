using System.Collections.Immutable;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.PieceRelics;

namespace Siege.Presentation.Visibility;

/// <summary>
/// 默认棋盘上的信物标记（relic-redesign R22 / R25：信物层全公开，三种标记）：地图信物（空格上）、持物棋子（挂在棋子上）、预告（下一批的落位格）。
/// 种类在 <see cref="BoardCellView.RelicKind"/>，持物件是否保险在 <see cref="BoardCellView.RelicInsured"/>，预告的落位大回合在 <see cref="DefaultBoardView.RelicForecastRound"/>。
/// </summary>
public enum RelicMarker
{
    /// <summary>该格没有信物，也不是预告格。</summary>
    None,

    /// <summary>地图信物：空格上的信物，落子即拾取。</summary>
    OnMap,

    /// <summary>持物棋子：挂在该格棋子上的信物。</summary>
    Held,

    /// <summary>预告：下一批将在该格落位（被占则跳过）。</summary>
    Forecast,
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
/// <param name="RelicKind">该格信物（或预告）的种类；<see cref="RelicMarker.None"/> 时为 <c>null</c>。</param>
/// <param name="LifeForbiddenBy">
/// 该格若是<b>当前行动玩家</b>的禁入格（他人已确定活形棋串的眼空间，life-shape D3 / D6），为那条活形的所有者；否则 <c>null</c>。
/// 所有者本人看自己的眼空间为 <c>null</c>（可以自拆）。集合原样取自公开视图的 <see cref="LifeShapeReport.ForbiddenCellsFor"/>，
/// 所有者取自 <see cref="LifeShapeReport.EyeSpaceAt"/>——本层不判活形、不重算。没有当前行动玩家（插旗阶段、终局）时全为 <c>null</c>。
/// </param>
public sealed record BoardCellView(
    Coord Coord,
    Terrain Terrain,
    int Height,
    Surface Surface,
    bool HasBridge,
    int? BirthZone,
    Occupant? Occupant,
    RelicMarker Relic,
    RelicKind? RelicKind,
    PlayerId? LifeForbiddenBy)
{
    /// <summary>
    /// 该格对<b>观察者本人</b>是不是单子禁手（forbidden-marks：自杀手 / 同形 / 破坏活形）及其原因；不是为 <c>null</c>。
    /// 只在观察者自己的默认棋盘上、轮到他行动时才有（<see cref="ViewerWorld.Board"/> 叠加）——它依赖本人的暂放批次，
    /// 从公开世界构建的默认棋盘上恒为 <c>null</c>。值原样取自 Core 的单子禁手查询，本层不判气、提子、同形与活形。
    /// </summary>
    public ForbiddenMove? Forbidden { get; init; }

    /// <summary>
    /// 该格信物是不是<b>保险件</b>（relic-redesign R15：保险标记公开；R25：持物棋子的标记上加保险记号）。只对 <see cref="RelicMarker.OnMap"/> / <see cref="RelicMarker.Held"/> 有意义，
    /// 预告与无信物格恒为 <c>false</c>。值原样取自盘面信物层的 <see cref="Core.PieceRelics.RelicToken.Insured"/>。
    /// </summary>
    public bool RelicInsured { get; init; }

    /// <summary>
    /// 该格为什么落不下（对当前行动玩家）。超出合法落子范围不在此列——范围来自合法落子范围契约，默认棋盘不推断它。
    /// 先后：地形 → 活棋禁入 → 单子禁手（禁入格不在合法落子范围里，不会同时是单子禁手）。
    /// </summary>
    public PlacementBlock Block =>
        Terrain == Terrain.Obstacle ? PlacementBlock.Terrain
        : LifeForbiddenBy is not null ? PlacementBlock.LifeForbidden
        : Forbidden is { } move ? Style.PlacementBlocks.Of(move.Kind)
        : PlacementBlock.None;
}

/// <summary>
/// 默认棋盘上一格"落不下"的原因类别（tactical-layers「活形与禁入格的标示」）。与"超出当前合法落子范围"在外观上可区分：
/// 范围外的格在这里是 <see cref="None"/>、不加任何标记，合法落点另由合法落子范围契约点亮。
/// </summary>
public enum PlacementBlock
{
    /// <summary>无阻断（空格、棋子格，或只是范围外）。</summary>
    None,

    /// <summary>地形不可落子（岩石 / 未架桥深水）。</summary>
    Terrain,

    /// <summary>活棋禁入：他人已确定活形棋串的眼空间。</summary>
    LifeForbidden,

    /// <summary>单子禁手 · 破坏活形：在当前暂放之上再落这一子，整批会使他人的已确定活形棋串失去活形。</summary>
    BreaksLife,

    /// <summary>单子禁手 · 自杀手：落下后（整批提子之后）己方仍有棋串无气。</summary>
    Suicide,

    /// <summary>单子禁手 · 同形：落下后盘面与某次历史提交的盘面重复。</summary>
    Superko,
}

/// <summary>
/// 一块出生棋盘的占位（board-link-mode D4 / D9；规格 match-setup「开局信息公开范围」、visual-style-baseline「未展开区域的可视表现」）：
/// 编号（0 起，面向人的编号经 <see cref="BirthZoneLabel"/>）、外接矩形（左下角原点与边长）、对本座位是否已展开、是否是本座位所在的那块，以及主题（link-map-terrain 裁决 M3）。
/// 未展开的占位只有外接矩形与主题，MUST NOT 带内部布局（格、信物格）；渲染层只能画边框、编号与主题名。全部原样取自按座位视图的 <see cref="BoardPlaceholder"/>，表现层不另算。
/// </summary>
public sealed record BoardPlaceholderView(int Zone, Coord Origin, int Width, int Height, bool Expanded, bool Own, BoardTheme? Theme)
{
    /// <summary>外接矩形内的全部格（渲染占位台面与拾取用；不是棋盘布局——出生棋盘本就是实心矩形）。</summary>
    public IEnumerable<Coord> Cells() =>
        from y in Enumerable.Range(Origin.Y, Height)
        from x in Enumerable.Range(Origin.X, Width)
        select new Coord(x, y);

    /// <summary>
    /// 主题的中文名（link-map-terrain 6.1 / 裁决 M3）：图形版画在占位编号下方。唯一来源 <see cref="BoardThemeNames.DisplayName"/>，表现层与 Godot 不另写映射；
    /// 没有主题（手写夹具）为 <c>null</c>，渲染层不画这一行。
    /// </summary>
    public string? ThemeName => Theme?.DisplayName();
}

/// <summary>
/// 不可落子格的造型类别（visual-style-baseline「棋盘台面的可视表现」，link-map-terrain 6.1）：由棋盘清单决定，渲染层不自行推断。
/// </summary>
public enum ObstacleArt
{
    /// <summary>场景格（棋盘之外）：只作装饰，铺满整格、不留格缝，装饰件按坐标散列挑选。</summary>
    Scenery,

    /// <summary>棋盘内的岩石（链接档中间带，D4）：本战场的一格地形，按棋盘格画（地砖 + 岩石部件）。</summary>
    BoardRock,
}

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
/// <param name="Boards">
/// 棋盘清单（board-map D2 / D10）：原样取自地图数据，供渲染层画棋盘台面的边框与格线、区分出生棋盘与公共棋盘；Godot 不自行推断。
/// 旧地图的清单为空，画面不变。清单是静态数据，不随对局变化。
/// </param>
public sealed record DefaultBoardView(
    int Width,
    int Height,
    ImmutableArray<BoardCellView> Cells,
    ImmutableArray<FenceEdge> Fences,
    ImmutableArray<TerrainEdit> Edits,
    ImmutableArray<BoardPlate> Boards)
{
    /// <summary>
    /// 各块出生棋盘（平台）的占位（board-link-mode）：由 <see cref="ForSeat"/> 填入；全知视图为空。
    /// 未展开（<see cref="BoardPlaceholderView.Expanded"/> 为假，只在插旗阶段出现——插旗阶段结束后全部一起展开，open-map 裁决 O4）的占位在 <see cref="Cells"/> 里没有任何格。
    /// </summary>
    public ImmutableArray<BoardPlaceholderView> Placeholders { get; init; } = [];

    /// <summary>
    /// 当前预告批次的落位大回合（relic-redesign R11 / R25：预告格标记写"种类 + 落位大回合"）；没有预告时为 <c>null</c>。
    /// 全部 <see cref="RelicMarker.Forecast"/> 格同属这一批（Core 一次只有一批预告），所以放在棋盘上而不是逐格重复。原样取自公开快照的 <c>RelicForecast.LandingRound</c>。
    /// </summary>
    public int? RelicForecastRound { get; init; }

    /// <summary>
    /// 布局指纹（引擎层"要不要整体重搭"的唯一判据，board-link-mode F.6 / 裁决 J12-1）：画布尺寸、每格的<b>坐标</b>与可落子 / 高度 / 地表 / 桥、全部栅栏边、
    /// 棋盘清单与各块占位（编号、原点、展开、本机）。插旗锁定会把占位展开——必须让指纹变；只落子 / 提子不变。
    /// 预置设施与本局改造在这里本来就分不出——指纹变了只说明"要重搭"，不说明是谁改的。
    /// </summary>
    public string LayoutKey()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(Width).Append('x').Append(Height).Append('|');
        foreach (BoardCellView cell in Cells)
        {
            sb.Append(cell.Coord.X).Append(',').Append(cell.Coord.Y).Append(':')
                .Append((int)cell.Terrain).Append(cell.Height).Append((int)cell.Surface).Append(cell.HasBridge ? '1' : '0').Append(';');
        }

        sb.Append('|');
        foreach (FenceEdge fence in Fences.IsDefault ? [] : Fences)
        {
            sb.Append(fence).Append(';');
        }

        sb.Append('|');
        foreach (BoardPlate plate in Boards.IsDefault ? [] : Boards)
        {
            sb.Append(plate.Origin.X).Append(',').Append(plate.Origin.Y).Append(':').Append(plate.Width).Append('x').Append(plate.Height).Append((int)plate.Kind).Append(';');
        }

        sb.Append('|');
        foreach (BoardPlaceholderView p in Placeholders)
        {
            sb.Append(p.Zone).Append('@').Append(p.Origin.X).Append(',').Append(p.Origin.Y).Append(p.Expanded ? 'E' : '-').Append(p.Own ? 'O' : '-').Append(';');
        }

        return sb.ToString();
    }

    /// <summary>
    /// 某个不可落子格（未架桥深水除外，深水另有画法）该画成什么（link-map-terrain 6.1）：落在本视图棋盘清单的某块外接矩形内 → <see cref="ObstacleArt.BoardRock"/>，
    /// 否则 → <see cref="ObstacleArt.Scenery"/>。清单为空（手写夹具）时一律是场景装饰。只读清单的外接矩形，不判规则。
    /// </summary>
    public ObstacleArt ObstacleArtAt(Coord coord) =>
        !Boards.IsDefault && Boards.Any(b => b.Contains(coord)) ? ObstacleArt.BoardRock : ObstacleArt.Scenery;

    /// <summary>某格。不在本视图里（插旗阶段未展开的占位格、场景格）抛出——拾取 / 悬停请先用 <see cref="CellOrNull"/>。</summary>
    public BoardCellView CellAt(Coord coord) =>
        CellOrNull(coord) ?? throw new ArgumentOutOfRangeException(nameof(coord), coord.ToNotation(), "坐标不在本视图的格里（超出棋盘范围或未展开）。");

    /// <summary>某格；不在本视图里为 <c>null</c>（插旗阶段未展开的占位格、场景格）。</summary>
    public BoardCellView? CellOrNull(Coord coord) => Cells.FirstOrDefault(c => c.Coord == coord);

    /// <summary>
    /// 按座位过滤（board-link-mode 实现裁决 B4 在表现层的镜像，information-visibility「其他战场不可见」「数据层过滤」）：
    /// 只保留 <paramref name="seat"/> 可见的格（<see cref="SeatPublicView.Cells"/>，即本座位所在战场；插旗阶段为本座位已选的那块、未选时为空），
    /// 棋盘清单只留整块可见的台面（棋盘内的<b>可落子格</b>全部可见，link-map-terrain 第 1 组裁决④；G1 起战斗阶段的可见格已含本战场棋盘内的岩石 / 未架桥深水，判据不变），
    /// 栅栏两端可见、改造的目标格 / 边可见才保留；<see cref="Placeholders"/> 取各块出生棋盘的占位，
    /// 已展开 = 外接矩形内有可见格（出生棋盘整块一起展开；不假设左下角格可落子），本座位所在 = 编号等于 <paramref name="ownZone"/>，主题原样透传。
    /// 本方法不读地图、不判规则，只按座位视图给出的坐标做集合过滤。
    /// </summary>
    public DefaultBoardView ForSeat(SeatPublicView seat, int? ownZone)
    {
        ArgumentNullException.ThrowIfNull(seat);
        HashSet<Coord> visible = [.. seat.Cells.Select(c => Coord.Parse(c.Coord))];
        HashSet<Coord> playable = [.. Cells.Where(c => c.Terrain == Terrain.Playable).Select(c => c.Coord)];
        ImmutableArray<BoardPlaceholderView> placeholders =
        [
            .. seat.Placeholders.Select(p =>
            {
                Coord origin = Coord.Parse(p.Origin);
                var rect = new BoardPlate(origin, p.Width, p.Height, BoardPlateKind.Birth);
                return new BoardPlaceholderView(p.Zone, origin, p.Width, p.Height, rect.Cells().Any(visible.Contains), ownZone == p.Zone, p.Theme);
            }),
        ];
        return this with
        {
            Cells = [.. Cells.Where(c => visible.Contains(c.Coord))],
            Fences = [.. Fences.Where(f => visible.Contains(f.A) && visible.Contains(f.B))],
            Edits = [.. Edits.Where(e => e.Kind == TerrainEditKind.Fence ? visible.Contains(e.Edge.A) && visible.Contains(e.Edge.B) : visible.Contains(e.Cell))],
            Boards = [.. Boards.Where(b => b.Cells().Where(playable.Contains).All(visible.Contains))],
            Placeholders = placeholders,
        };
    }

    /// <summary>
    /// 叠上观察者本人的单子禁手（forbidden-marks D4 / D6）：禁手格的 <see cref="BoardCellView.Forbidden"/> 取查询结果，其余格不变。
    /// 查询结果里的格 MUST 都是棋盘上的空格，否则抛出——查询与盘面不是同一时刻的。
    /// </summary>
    public DefaultBoardView WithForbidden(ForbiddenMoveReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.Moves.IsDefaultOrEmpty)
        {
            return this;
        }

        var moves = report.Moves.ToDictionary(m => m.Coord);
        ImmutableArray<BoardCellView> cells =
        [
            .. Cells.Select(cell =>
            {
                if (!moves.Remove(cell.Coord, out ForbiddenMove? move))
                {
                    return cell;
                }

                if (cell.Occupant is not null || cell.Terrain != Terrain.Playable)
                {
                    throw new ArgumentException($"禁手格 {cell.Coord.ToNotation()} 在默认棋盘上不是空的可落子格：查询与盘面不是同一时刻的。", nameof(report));
                }

                return cell with { Forbidden = move };
            }),
        ];
        if (moves.Count > 0)
        {
            throw new ArgumentException($"禁手格 {moves.Keys.Order().First().ToNotation()} 不在棋盘上。", nameof(report));
        }

        return this with { Cells = cells };
    }

    /// <summary>从公开世界构建：只读盘面格子视图、盘面信物层与信物预告。</summary>
    public static DefaultBoardView From(PublicWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        GameBoard board = world.View.Board;
        Dictionary<Coord, RelicKind> forecast = world.View.RelicForecast is { } f ? f.Slots.ToDictionary(s => s.Cell, s => s.Kind) : [];
        LifeShapeReport life = world.View.LifeShape;
        Dictionary<Coord, PlayerId> forbidden = world.View.CurrentPlayer is { } current
            ? life.ForbiddenCellsFor(current).ToDictionary(
                c => c,
                c => life.EyeSpaceAt(c)?.Owner ?? throw new InvalidOperationException($"禁入格 {c.ToNotation()} 不在任何眼空间里：活形报告自相矛盾。"))
            : [];
        ImmutableArray<BoardCellView> cells =
        [
            .. board.AllCoords().Select(c =>
            {
                Cell cell = board[c];
                RelicMarker marker = RelicMarker.None;
                RelicKind? kind = null;
                bool insured = false;
                if (board.RelicAt(c) is { } token)
                {
                    marker = cell.Occupant is null ? RelicMarker.OnMap : RelicMarker.Held;
                    kind = token.Kind;
                    insured = token.Insured;
                }
                else if (forecast.TryGetValue(c, out RelicKind forecastKind))
                {
                    marker = RelicMarker.Forecast;
                    kind = forecastKind;
                }

                return new BoardCellView(
                    c, cell.Terrain, board.Map.HeightAt(c), board.Map.SurfaceAt(c), board.Map.HasBridge(c),
                    cell.BirthZone, cell.Occupant, marker, kind, forbidden.TryGetValue(c, out PlayerId owner) ? owner : null)
                {
                    RelicInsured = insured,
                };
            }),
        ];

        return new DefaultBoardView(
            board.Width, board.Height, cells,
            [.. board.Map.TerrainData.Fences.OrderBy(f => f.A).ThenBy(f => f.B)],
            board.TerrainEdits,
            board.Map.Boards.IsDefault ? [] : board.Map.Boards)
        {
            RelicForecastRound = world.View.RelicForecast?.LandingRound,
        };
    }
}
