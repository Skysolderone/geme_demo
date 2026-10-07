using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Tests;

/// <summary>
/// 测试用棋盘档地图（board-map 段 A / B，board-isolated-gen 段 A 起棋盘之间不再有通道）：在测试内用代码构造。
/// </summary>
internal static class BoardMapFixtures
{
    /// <summary>0 号出生棋盘：左下角 <c>C5</c>，5×5。</summary>
    internal static readonly BoardPlate Birth0 = new(new Coord(2, 4), 5, 5, BoardPlateKind.Birth);

    /// <summary>1 号出生棋盘：左下角 <c>X5</c>，5×5，最右两列是双字母列 <c>AA</c>、<c>AB</c>。</summary>
    internal static readonly BoardPlate Birth1 = new(new Coord(22, 4), 5, 5, BoardPlateKind.Birth);

    /// <summary>公共棋盘：左下角 <c>L3</c>，9×9。</summary>
    internal static readonly BoardPlate Public = new(new Coord(10, 2), 9, 9, BoardPlateKind.Public);

    /// <summary>中央入口：公共棋盘的中心格 <c>P7</c>。</summary>
    internal static readonly Coord Entrance = new(14, 6);

    /// <summary>
    /// 29×13 的 2 人棋盘档小图：两块 5×5 出生棋盘夹一块 9×9 公共棋盘，各隔 3 格场景、互不连通，其余全部是障碍。
    /// 宽 29 列是有意的：1 号出生棋盘落在第 23–27 列，文件与对局里都会出现双字母列标。可落子 25 + 25 + 81 = 131。
    /// 只走 Unvalidated 入口：13 行不足棋盘档的 15 行下限（出生棋盘数也不是人数 + 1），它只用来测清单与文件格式。
    /// </summary>
    internal static MapData Map()
    {
        ImmutableArray<BoardPlate> boards = [Birth0, Birth1, Public];
        ImmutableHashSet<Coord> playable = [.. boards.SelectMany(b => b.Cells())];
        return new MapData
        {
            Id = "test-board-3",
            Width = 29,
            Height = 13,
            MaxPlayers = 2,
            Profile = MapProfile.Board,
            Boards = boards,
            Obstacles = [.. TestMaps.Rect(0, 0, 29, 13).Where(c => !playable.Contains(c))],
            BirthZones = [[.. Birth0.Cells()], [.. Birth1.Cells()]],
            RelicCells = new (int X, int Y, RelicZone Zone, BudgetTier Budget)[]
            {
                (4, 6, RelicZone.BirthZone, BudgetTier.Birth),
                (24, 6, RelicZone.BirthZone, BudgetTier.Birth),
                (14, 6, RelicZone.Contested, BudgetTier.High),
                (12, 4, RelicZone.Contested, BudgetTier.Standard),
                (16, 8, RelicZone.Contested, BudgetTier.Standard),
            }.ToImmutableDictionary(r => new Coord(r.X, r.Y), r => new RelicCellSpec(r.Zone, r.Budget)),
            ChokePoints = [],
            CentralEntrance = Entrance,
        };
    }

    // ---------- 4 人合规图（「棋盘档预算与校验」的手写最小数据） ----------

    /// <summary>公共棋盘 A：左下角 (8, 8)，9×9；中央入口在它的中心。</summary>
    internal static readonly BoardPlate FourPublicA = new(new Coord(8, 8), 9, 9, BoardPlateKind.Public);

    /// <summary>公共棋盘 B：左下角 (20, 8)，9×9；与 A 隔 3 列。</summary>
    internal static readonly BoardPlate FourPublicB = new(new Coord(20, 8), 9, 9, BoardPlateKind.Public);

    /// <summary>五块 5×5 出生棋盘：A 的西 / 北 / 南各一块，B 的东 / 北各一块（各隔 2 格场景）；下标即出生区编号。</summary>
    internal static readonly ImmutableArray<BoardPlate> FourBirths =
    [
        new(new Coord(1, 10), 5, 5, BoardPlateKind.Birth),
        new(new Coord(31, 10), 5, 5, BoardPlateKind.Birth),
        new(new Coord(10, 19), 5, 5, BoardPlateKind.Birth),
        new(new Coord(22, 19), 5, 5, BoardPlateKind.Birth),
        new(new Coord(10, 1), 5, 5, BoardPlateKind.Birth),
    ];

    /// <summary>4 人合规图的中央入口：公共棋盘 A 的中心格 (12, 12)。</summary>
    internal static readonly Coord FourEntrance = new(12, 12);

    /// <summary>4 人合规图的九个信物格：五块出生棋盘各 1；A 上 1 个高档（中央入口）+ 1 个标准；B 上 2 个标准。</summary>
    internal static readonly ImmutableDictionary<Coord, RelicCellSpec> FourRelics = new (int X, int Y, RelicZone Zone, BudgetTier Budget)[]
    {
        (3, 12, RelicZone.BirthZone, BudgetTier.Birth),
        (33, 12, RelicZone.BirthZone, BudgetTier.Birth),
        (12, 21, RelicZone.BirthZone, BudgetTier.Birth),
        (24, 21, RelicZone.BirthZone, BudgetTier.Birth),
        (12, 3, RelicZone.BirthZone, BudgetTier.Birth),
        (12, 12, RelicZone.Contested, BudgetTier.High),
        (10, 14, RelicZone.Contested, BudgetTier.Standard),
        (24, 12, RelicZone.Contested, BudgetTier.Standard),
        (26, 10, RelicZone.Contested, BudgetTier.Standard),
    }.ToImmutableDictionary(r => new Coord(r.X, r.Y), r => new RelicCellSpec(r.Zone, r.Budget));

    /// <summary>
    /// 36×25 的 4 人棋盘档图，规格「棋盘档预算与校验」合规数据：5 块出生棋盘 + 2 块公共棋盘，互不连通，其余全部是障碍，不标咽喉。
    /// 可落子 5×25 + 2×81 = 287；信物 5 + 2 + 2 = 9。
    /// </summary>
    internal static MapData FourPlayerMap() =>
        Build("test-board-4p", 36, 25, [.. FourBirths, FourPublicA, FourPublicB], []);

    /// <summary>
    /// 按棋盘清单与棋盘外的可落子格（故意放错的格）拼一张棋盘档图：其余格一律障碍，出生区 = 出生棋盘，
    /// 信物取 <see cref="FourRelics"/> 中落在可落子格上的那些，中央入口固定为 <see cref="FourEntrance"/>，不标咽喉。
    /// <paramref name="obstaclesInside"/> 是棋盘之内另外挖掉的格。
    /// </summary>
    internal static MapData Build(
        string id, int width, int height, ImmutableArray<BoardPlate> boards, ImmutableArray<Coord> outside, params Coord[] obstaclesInside) =>
        Build(id, 4, width, height, boards, outside, obstaclesInside);

    /// <summary>同上，指定人数上限。</summary>
    internal static MapData Build(
        string id, int players, int width, int height, ImmutableArray<BoardPlate> boards, ImmutableArray<Coord> outside, params Coord[] obstaclesInside)
    {
        ImmutableHashSet<Coord> playable = [.. boards.SelectMany(b => b.Cells()), .. outside];
        playable = playable.Except(obstaclesInside);
        return new MapData
        {
            Id = id,
            Width = width,
            Height = height,
            MaxPlayers = players,
            Profile = MapProfile.Board,
            Boards = boards,
            Obstacles = [.. TestMaps.Rect(0, 0, width, height).Where(c => !playable.Contains(c))],
            BirthZones = [.. boards.Where(b => b.Kind == BoardPlateKind.Birth).Select(b => (ImmutableHashSet<Coord>)[.. b.Cells()])],
            RelicCells = FourRelics.Where(kv => playable.Contains(kv.Key)).ToImmutableDictionary(),
            ChokePoints = [],
            CentralEntrance = FourEntrance,
        };
    }

    // ---------- 2 人合规图 ----------

    /// <summary>
    /// 28×21 的 2 人棋盘档图（规格 Scenario「合法 2 人棋盘图」）：3 块 5×5 出生棋盘（公共棋盘的西 / 东 / 南各一块，各隔 2 格）+ 1 块 11×11 公共棋盘，
    /// 互不连通。可落子 3×25 + 121 = 196；信物 3 + 3 = 6；中央入口 = 公共棋盘中心 (13, 13)。
    /// </summary>
    internal static MapData TwoPlayerMap()
    {
        ImmutableArray<BoardPlate> boards =
        [
            new(new Coord(1, 11), 5, 5, BoardPlateKind.Birth),
            new(new Coord(21, 11), 5, 5, BoardPlateKind.Birth),
            new(new Coord(11, 1), 5, 5, BoardPlateKind.Birth),
            new(new Coord(8, 8), 11, 11, BoardPlateKind.Public),
        ];
        ImmutableHashSet<Coord> playable = [.. boards.SelectMany(b => b.Cells())];
        return new MapData
        {
            Id = "test-board-2p",
            Width = 28,
            Height = 21,
            MaxPlayers = 2,
            Profile = MapProfile.Board,
            Boards = boards,
            Obstacles = [.. TestMaps.Rect(0, 0, 28, 21).Where(c => !playable.Contains(c))],
            BirthZones = [.. boards.Where(b => b.Kind == BoardPlateKind.Birth).Select(b => (ImmutableHashSet<Coord>)[.. b.Cells()])],
            RelicCells = new (int X, int Y, RelicZone Zone, BudgetTier Budget)[]
            {
                (3, 13, RelicZone.BirthZone, BudgetTier.Birth),
                (23, 13, RelicZone.BirthZone, BudgetTier.Birth),
                (13, 3, RelicZone.BirthZone, BudgetTier.Birth),
                (13, 13, RelicZone.Contested, BudgetTier.High),
                (10, 10, RelicZone.Contested, BudgetTier.Standard),
                (16, 16, RelicZone.Contested, BudgetTier.Standard),
            }.ToImmutableDictionary(r => new Coord(r.X, r.Y), r => new RelicCellSpec(r.Zone, r.Budget)),
            ChokePoints = [],
            CentralEntrance = new Coord(13, 13),
        };
    }
}
