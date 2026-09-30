using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Tests;

/// <summary>
/// 测试用棋盘档地图（board-map 段 A）：在测试内用代码构造，只走 Unvalidated 入口——棋盘档的预算与校验在段 B 才实现。
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
    /// 29×13 的 2 人棋盘档小图：两块 5×5 出生棋盘夹一块 9×9 公共棋盘，各由一条 3 格宽、3 格长的通道（第 6–8 行）相连，其余全部是障碍。
    /// 宽 29 列是有意的：1 号出生棋盘落在第 23–27 列，文件与对局里都会出现双字母列标。可落子 25 + 25 + 81 + 18 = 149。
    /// </summary>
    internal static MapData Map()
    {
        Coord[] corridors = [.. FrontierFixtures.Rect(7, 5, 3, 3), .. FrontierFixtures.Rect(19, 5, 3, 3)];
        ImmutableArray<BoardPlate> boards = [Birth0, Birth1, Public];
        ImmutableHashSet<Coord> playable = [.. boards.SelectMany(b => b.Cells()), .. corridors];
        return new MapData
        {
            Id = "test-board-3",
            Width = 29,
            Height = 13,
            MaxPlayers = 2,
            Profile = MapProfile.Board,
            Boards = boards,
            Obstacles = [.. FrontierFixtures.Rect(0, 0, 29, 13).Where(c => !playable.Contains(c))],
            BirthZones = [[.. Birth0.Cells()], [.. Birth1.Cells()]],
            RelicCells = new (int X, int Y, RelicZone Zone, BudgetTier Budget)[]
            {
                (4, 6, RelicZone.BirthZone, BudgetTier.Birth),
                (24, 6, RelicZone.BirthZone, BudgetTier.Birth),
                (14, 6, RelicZone.Contested, BudgetTier.High),
                (12, 4, RelicZone.Contested, BudgetTier.Standard),
                (16, 8, RelicZone.Contested, BudgetTier.Standard),
            }.ToImmutableDictionary(r => new Coord(r.X, r.Y), r => new RelicCellSpec(r.Zone, r.Budget)),
            ChokePoints = [.. corridors],
            CentralEntrance = Entrance,
        };
    }

    // ---------- 4 人合规图（board-map 段 B：「棋盘档预算与校验」的手写最小数据） ----------

    /// <summary>公共棋盘 A：左下角 (8, 8)，9×9；中央入口在它的中心。</summary>
    internal static readonly BoardPlate FourPublicA = new(new Coord(8, 8), 9, 9, BoardPlateKind.Public);

    /// <summary>公共棋盘 B：左下角 (20, 8)，9×9；与 A 隔 3 列。</summary>
    internal static readonly BoardPlate FourPublicB = new(new Coord(20, 8), 9, 9, BoardPlateKind.Public);

    /// <summary>五块 5×5 出生棋盘：A 的西 / 北 / 南各一块，B 的东 / 北各一块；下标即出生区编号。</summary>
    internal static readonly ImmutableArray<BoardPlate> FourBirths =
    [
        new(new Coord(1, 10), 5, 5, BoardPlateKind.Birth),
        new(new Coord(31, 10), 5, 5, BoardPlateKind.Birth),
        new(new Coord(10, 19), 5, 5, BoardPlateKind.Birth),
        new(new Coord(22, 19), 5, 5, BoardPlateKind.Birth),
        new(new Coord(10, 1), 5, 5, BoardPlateKind.Birth),
    ];

    /// <summary>A–B 之间的通道：第 12–14 行（y = 11–13），3 格宽、3 格长。</summary>
    internal static readonly ImmutableArray<Coord> FourMainCorridor = [.. FrontierFixtures.Rect(17, 11, 3, 3)];

    /// <summary>五条出生棋盘通道，各 3 格宽、2 格长（横向覆盖 y = 12、竖向覆盖 x = 12 / 24），次序同 <see cref="FourBirths"/>。</summary>
    internal static readonly ImmutableArray<Coord> FourBirthCorridors =
    [
        .. FrontierFixtures.Rect(6, 11, 2, 3),
        .. FrontierFixtures.Rect(29, 11, 2, 3),
        .. FrontierFixtures.Rect(11, 17, 3, 2),
        .. FrontierFixtures.Rect(24, 17, 3, 2),
        .. FrontierFixtures.Rect(11, 6, 3, 2),
    ];

    /// <summary>4 人合规图的中央入口：公共棋盘 A 的中心格 (12, 12)。</summary>
    internal static readonly Coord FourEntrance = new(12, 12);

    /// <summary>
    /// 36×25 的 4 人棋盘档图，规格 Scenario「合法棋盘图」的数据：5 块出生棋盘 + 2 块公共棋盘，六条 3 格宽的通道（2026-09-30 裁决：通道 3–4 格宽），其余全部是障碍。
    /// 可落子 5×25 + 2×81 + 9 + 5×6 = 326；信物 5 + 2 + 2 = 9；通道格全部标为咽喉。
    /// </summary>
    internal static MapData FourPlayerMap() =>
        Build("test-board-4p", 36, 25, [.. FourBirths, FourPublicA, FourPublicB], [.. FourMainCorridor, .. FourBirthCorridors]);

    /// <summary>
    /// 按棋盘清单与棋盘外的可落子格（通道 / 故意放错的格）拼一张 4 人棋盘档图：其余格一律障碍，出生区 = 出生棋盘，信物与入口固定。
    /// <paramref name="obstaclesInside"/> 是棋盘之内另外挖掉的格。
    /// </summary>
    internal static MapData Build(
        string id, int width, int height, ImmutableArray<BoardPlate> boards, ImmutableArray<Coord> outside, params Coord[] obstaclesInside)
    {
        ImmutableHashSet<Coord> playable = [.. boards.SelectMany(b => b.Cells()), .. outside];
        playable = playable.Except(obstaclesInside);
        return new MapData
        {
            Id = id,
            Width = width,
            Height = height,
            MaxPlayers = 4,
            Profile = MapProfile.Board,
            Boards = boards,
            Obstacles = [.. FrontierFixtures.Rect(0, 0, width, height).Where(c => !playable.Contains(c))],
            BirthZones = [.. boards.Where(b => b.Kind == BoardPlateKind.Birth).Select(b => (ImmutableHashSet<Coord>)[.. b.Cells()])],
            RelicCells = new (int X, int Y, RelicZone Zone, BudgetTier Budget)[]
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
            }.ToImmutableDictionary(r => new Coord(r.X, r.Y), r => new RelicCellSpec(r.Zone, r.Budget)),
            ChokePoints = [.. outside],
            CentralEntrance = FourEntrance,
        };
    }
}
