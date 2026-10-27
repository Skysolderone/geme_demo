using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Board;
using Siege.Core.Carry;
using Siege.Core.PieceRelics;
using Siege.Core.Recruit;
using Siege.Core.Scoring;

namespace Siege.Core.Match;

/// <summary>
/// 一块出生棋盘的占位（编号、整块棋盘的外接矩形与主题）：始终公开，不含内部布局（match-setup「开局信息公开范围」，link-map-terrain 裁决 M3）。
/// <see cref="Theme"/> 取自棋盘清单；棋盘没有主题时为 <c>null</c>。
/// </summary>
public sealed record BoardPlaceholder(int Zone, string Origin, int Width, int Height, BoardTheme? Theme);

/// <summary>
/// 座位可见的一格（information-visibility 第 1–4 项）：坐标（围棋记法）、地形 / 高度 / 地表 / 桥、占用（所属与类型）、格子归属。该格的信物见 <see cref="SeatPublicView.Relics"/>。
/// 设施没有改造者字段。<see cref="Ownership"/> 是空格归属三态（独占及独占者 / 争议 / 中立；被占据的格为"占据"、障碍为"障碍"），
/// 原样取自势力快照里的覆盖表（全项目唯一的归属实现，与领地分同一份），本文件不另算覆盖（裁决 S8）。尚无势力快照（插旗阶段）时为 <c>null</c>。
/// </summary>
public sealed record SeatCellView(
    string Coord, Terrain Terrain, int Height, Surface Surface, bool Bridge, PlayerId? Owner, PieceType? Piece, CellOwnership? Ownership);

/// <summary>
/// 可见的一件信物（relic-redesign R22：地图信物与挂出的信物全公开）：坐标、本局编号、种类、带入者（地图刷出为 <c>null</c>）、保险标记，
/// 以及持有者（该格棋子的主人；空格上的地图信物为 <c>null</c>）。
/// </summary>
public sealed record SeatRelicView(string Coord, int Id, RelicKind Kind, PlayerId? Origin, bool Insured, PlayerId? Holder);

/// <summary>可见的一条棋串（第 5、6 项）：所属、棋子坐标、军势、活形状态与眼空间。</summary>
public sealed record SeatGroupView(PlayerId Owner, ImmutableArray<string> Stones, BigInteger Power, LifeState Life, ImmutableArray<ImmutableArray<string>> EyeSpaces);

/// <summary>
/// 座位看到的一名玩家：状态全局公开（第 10 项）；与本座位同战场时另有势力与明细（第 5 项）、手牌类型与禁入格（第 6 项），否则这些全为空——
/// 他人战场的势力只给在局状态（「其他战场不可见」）。
/// </summary>
public sealed record SeatPlayerView(
    PlayerId Player,
    PlayerStatus Status,
    bool SameBattlefield,
    BigInteger? Power,
    int? TerritoryScore,
    ImmutableArray<string> ExclusiveCells,
    ImmutableSortedSet<PieceType>? HandTypes,
    ImmutableArray<string> ForbiddenCells);

/// <summary>一个已结算战场的公开结果（第 10 项）：战场标识、结算大回合、胜者。不含盘面与势力。</summary>
public sealed record SeatSettlementView(int Battlefield, int MajorRound, PlayerId Winner);

/// <summary>一名玩家的带入（第 8 项，全局公开）。</summary>
public sealed record SeatCarryInView(PlayerId Player, CarryIn CarryIn);

/// <summary>
/// 按座位过滤的公开视图（board-link-mode 实现裁决 B4，information-visibility「始终公开的信息」「其他战场不可见」）。
/// 数据层过滤：结构里只有本座位所在战场的格、棋子、设施、归属、信物、棋串与活形，以及同战场玩家的势力明细与手牌类型；
/// 其他战场的格根本不在 <see cref="Cells"/> 里（呈现为"未展开"，<see cref="IsExpanded"/> 为 <c>false</c>）。
/// 全局公开项（配置、地图标识与种子、带入、行动顺序、各玩家状态、战场结算与胜者、占位）不过滤。open-map 起锁定后整张地图是唯一战场；map-expand-25 段 B 起插旗阶段之后的可见格即整张图的全部格。
/// 全知视图 <see cref="MatchFlow.Publish"/> 原样保留（批量分析、遥测与回放仍用它）。
/// </summary>
public sealed record SeatPublicView(
    PlayerId Seat,
    string MapId,
    string Seed,
    MatchPhase Phase,
    int MajorRound,
    int ArtisanWeight,
    ContentSet ContentSet,
    TurnStage Stage,
    PlayerId? CurrentPlayer,
    ImmutableArray<PlayerId> ActionOrder,
    bool CarryInOut,
    ImmutableArray<SeatCarryInView> CarryIns,
    int? Battlefield,
    ImmutableArray<BoardPlaceholder> Placeholders,
    ImmutableArray<SeatCellView> Cells,
    ImmutableArray<string> Fences,
    ImmutableArray<SeatRelicView> Relics,
    ImmutableArray<SeatGroupView> Groups,
    ImmutableArray<SeatPlayerView> Players,
    ImmutableArray<SeatSettlementView> Settlements,
    MatchResult? Result)
{
    /// <summary>尚未落位的信物预告（relic-redesign R11 / R22，全公开）；没有时为 <c>null</c>。不是位置参数。</summary>
    public RelicForecast? RelicForecast { get; init; }

    /// <summary>该格是否对本座位展开（在 <see cref="Cells"/> 里）。未展开的格不提供任何地形或占用。</summary>
    public bool IsExpanded(Coord c) => Cells.Any(cell => cell.Coord == c.ToNotation());

    /// <summary>查询一格：展开的返回其可见数据，未展开的返回 <c>null</c>（"未展开"）。</summary>
    public SeatCellView? CellAt(Coord c) => Cells.FirstOrDefault(cell => cell.Coord == c.ToNotation());
}

public sealed partial class MatchFlow
{
    /// <summary>
    /// 按座位发布公开视图（实现裁决 B4）：插旗阶段只展开本座位已选的那块出生棋盘（未选为空）；插旗阶段结束后展开整张图的全部格（map-expand-25 段 B：含平台之间的低地与画布留白）。
    /// </summary>
    public SeatPublicView PublishFor(PlayerId seat)
    {
        Require(seat);
        int? zone = Phase == MatchPhase.FlagPlanting ? Flags.FlagOf(seat) : _records[seat].BirthZone;
        int? battlefield = zone is null || Phase == MatchPhase.FlagPlanting ? null : PublicRules.BattlefieldOf(Board, zone.Value).Id;

        ImmutableHashSet<Coord> visible = zone is null ? []
            // 插旗阶段锁定后只公开这一块出生棋盘的布局：取整块棋盘（棋盘清单），不是出生区——链接档的出生区是核心区（link-map-terrain D1）。
            : Phase == MatchPhase.FlagPlanting ? [.. Board.Map.BirthBoard(zone.Value).Cells()]
            // 插旗阶段结束（全员锁定）之后（map-expand-25 段 B；原 G1 = 本战场各棋盘外接矩形）：整张图的全部格——平台、平台之间的低地（河道、桥、岩石）与画布留白。
            // 浮空岛的低地不在任何棋盘外接矩形里，按棋盘取会让低地整片不可见（第 4 大回合起能落子却看不见）；open-map 起锁定后整张图是唯一战场。
            : [.. Board.Map.AllCoords()];
        bool SameField(PlayerId p) => battlefield is { } id && _records[p].BirthZone is { } z && PublicRules.BattlefieldOf(Board, z).Id == id;

        bool started = Phase != MatchPhase.FlagPlanting;
        LifeShapeReport life = LifeShapeReport.Analyze(Board);
        PowerSnapshot? power = Scoreboard.Latest;
        ImmutableArray<HandPublicView> hands = Hands.PublicViews();
        // 格子归属（第 3 项，裁决 S8）：只对本座位可见的格逐格读势力快照的覆盖表，不另算一份；其他战场的格不在 visible 里，不给出。
        ImmutableArray<SeatCellView> cells = [.. visible.Order().Select(c =>
        {
            Occupant? occupant = Board[c].Occupant;
            return new SeatCellView(c.ToNotation(), Board[c].Terrain, Board.Map.HeightAt(c), Board.Map.SurfaceAt(c), Board.Map.HasBridge(c),
                occupant?.Owner, occupant?.Type, power?.Coverage.OwnershipOf(c));
        })];
        // 栅栏取地形数据里的栅栏边（含预置与对局中立的，形式相同、没有立栅者），两端都展开才给出。
        ImmutableArray<string> fences = [.. Board.Map.TerrainData.Fences
            .Where(f => visible.Contains(f.A) && visible.Contains(f.B))
            .Select(f => f.ToString())
            .Order(StringComparer.Ordinal)];
        ImmutableArray<SeatRelicView> relics = [.. Board.RelicsOnBoard().Where(r => visible.Contains(r.Cell)).Select(r =>
            new SeatRelicView(r.Cell.ToNotation(), r.Token.Id, r.Token.Kind, r.Token.Origin, r.Token.Insured, Board[r.Cell].Occupant?.Owner))];
        ImmutableArray<SeatGroupView> groups = !started ? [] : [.. life.Groups.Where(g => g.Group.Stones.All(visible.Contains)).Select(g =>
            new SeatGroupView(g.Group.Owner, [.. g.Group.Stones.Select(s => s.ToNotation())],
                power?.Of(g.Group.Owner).Groups.FirstOrDefault(p => p.Stones.Contains(g.Group.Stones[0]))?.Power ?? BigInteger.Zero,
                g.Life, [.. g.EyeSpaces.Select(e => e.Cells.Select(c => c.ToNotation()).ToImmutableArray())]))];
        ImmutableArray<SeatPlayerView> players = [.. _players.Select(p =>
        {
            bool same = started && SameField(p);
            PlayerPower? detail = same ? power?.Of(p) : null;
            return new SeatPlayerView(p, _records[p].Status, same, detail?.Total, detail?.TerritoryScore,
                detail is null ? [] : [.. detail.ExclusiveCells.Select(c => c.ToNotation())],
                same ? hands.FirstOrDefault(h => h.Player == p)?.Types : null,
                same ? [.. life.ForbiddenCellsFor(p).Where(visible.Contains).Order().Select(c => c.ToNotation())] : []);
        })];

        return new SeatPublicView(
            seat, Board.BaseMap.Id, Seed.ToString(), Phase, MajorRound, ArtisanWeight, ContentSet, Stage, CurrentPlayer, _order,
            CarryInOut, [.. CarryIns.Select(kv => new SeatCarryInView(kv.Key, kv.Value))], battlefield,
            // 占位取地图的出生棋盘（open-map 起地图整局不变）。
            // 取棋盘清单的整块外接矩形与主题（match-setup「占位尺寸取整块棋盘」，link-map-terrain D1 / D7），不取出生区（核心区）。
            [.. Board.Map.BirthBoards().Select((plate, z) => new BoardPlaceholder(z, plate.Origin.ToNotation(), plate.Width, plate.Height, plate.Theme))],
            cells, fences, relics, groups, players,
            [.. _settlements.Select(s => new SeatSettlementView(s.Battlefield, s.MajorRound, s.Winner))],
            Result)
        {
            RelicForecast = PendingRelicForecast,
        };
    }
}
