using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests;

/// <summary>
/// 测试用链接档地图（board-link-mode 段 A，段 F 改为拼接版）：手写的最小合规数据，不经生成器，便于单独验证校验器、战场导出、改造规则与拼接几何。
/// 画布口径与生成器相同（裁决 J4 / J7）：原点固定，棋盘底边在第 2 行、最西列从第 2 列起，东 / 北边界外留 2 格。
/// </summary>
internal static class LinkMapFixtures
{
    /// <summary>
    /// 五块 6×6 出生棋盘一字排开：x = 2 / 11 / 20 / 29 / 38，y = 2，间距 3。下标即出生区编号。
    /// 各带一个主题（link-map-terrain 1.1：链接档出生棋盘读入时缺主题被拒，夹具经文件往返的测试因此要带上）；主题只用于生成、校验与呈现，不影响这里的任何几何。
    /// link-map-terrain 第 2 组：边长由 5 改 6（裁决 M9 的下限），出生区改为核心区（左下角起 (1,1)–(3,3)），中间带各放 2 格林地凑足"至少 2 个可改造目标"（裁决 M7）。
    /// </summary>
    internal static readonly ImmutableArray<BoardPlate> Births =
    [
        new(new Coord(2, 2), 6, 6, BoardPlateKind.Birth, BoardTheme.Plateau),
        new(new Coord(11, 2), 6, 6, BoardPlateKind.Birth, BoardTheme.River),
        new(new Coord(20, 2), 6, 6, BoardPlateKind.Birth, BoardTheme.Forest),
        new(new Coord(29, 2), 6, 6, BoardPlateKind.Birth, BoardTheme.Palisade),
        new(new Coord(38, 2), 6, 6, BoardPlateKind.Birth, BoardTheme.Mixed),
    ];

    /// <summary>46×10 的链接档初始图：只有 5 块出生棋盘，各 2 个出生区档信物格（核心区对角）与 2 格林地，其余全部障碍；可落子 180、信物 10。</summary>
    internal static MapData Initial() => Build("test-link-5", Births);

    /// <summary>
    /// 手写的"链接 0 与 1 之后"（东贴，版本 1）的物化状态：1 号棋盘平移到 0 号东侧紧贴（x = 8），其余三块按原次序排到合并战场东侧（x = 17 / 26 / 35，间距 3）；画布 43×10。
    /// </summary>
    internal static MapData Linked01() => Build(
        "test-link-5",
        [
            Births[0],
            Births[1] with { Origin = new Coord(8, 2) },
            Births[2] with { Origin = new Coord(17, 2) },
            Births[3] with { Origin = new Coord(26, 2) },
            Births[4] with { Origin = new Coord(35, 2) },
        ],
        mapVersion: 1);

    /// <summary>
    /// 出生棋盘 <paramref name="plate"/> 的两格林地：中间带按先行后列的第一格与最后一格（6×6 时是左下角起 (4,1) 与 (4,4)，东列两端、不相邻）。
    /// 没有中间带的尺寸（5×5 等越界夹具）为空。
    /// </summary>
    internal static ImmutableArray<Coord> ForestOf(BoardPlate plate)
    {
        Coord[] middle = [.. plate.Cells().Where(plate.InMiddleBand)];
        return middle.Length < 2 ? [] : [middle[0], middle[^1]];
    }

    /// <summary>
    /// 按棋盘清单拼一张链接档图：画布 = 清单外接矩形的东 / 北边界各外扩 2 格（原点固定），棋盘之外一律障碍，出生区 = 出生棋盘的核心区（<see cref="BoardPlate.CoreCells"/>），
    /// 每块出生棋盘的信物格固定在核心区对角（核心区左下角与右上角：不相邻、不同行不同列），林地见 <see cref="ForestOf"/>。
    /// </summary>
    internal static MapData Build(string id, ImmutableArray<BoardPlate> boards, int mapVersion = 0)
    {
        int width = boards.Max(b => b.Origin.X + b.Width) + LinkMapGenerator.Margin;
        int height = boards.Max(b => b.Origin.Y + b.Height) + LinkMapGenerator.Margin;
        ImmutableHashSet<Coord> playable = [.. boards.SelectMany(b => b.Cells())];
        ImmutableDictionary<Coord, RelicCellSpec>.Builder relics = ImmutableDictionary.CreateBuilder<Coord, RelicCellSpec>();
        var surfaces = new List<(string, Surface)>();
        foreach (BoardPlate birth in boards.Where(b => b.Kind == BoardPlateKind.Birth))
        {
            Coord core = birth.CoreOrigin;
            relics[core] = new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth);
            relics[new Coord(core.X + 2, core.Y + 2)] = new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth);
            surfaces.AddRange(ForestOf(birth).Select(c => (c.ToNotation(), Surface.Forest)));
        }

        return new MapData
        {
            Id = id,
            Width = width,
            Height = height,
            MaxPlayers = 4,
            Profile = MapProfile.Link,
            Boards = boards,
            MapVersion = mapVersion,
            Obstacles = [.. TestMaps.Rect(0, 0, width, height).Where(c => !playable.Contains(c))],   // retire-legacy-maps：Rect 由已删除的 FrontierFixtures 原样迁到 TestMaps
            TerrainData = TestMaps.Terrain(surfaces: [.. surfaces]),
            BirthZones = [.. boards.Where(b => b.Kind == BoardPlateKind.Birth).Select(b => (ImmutableHashSet<Coord>)[.. b.CoreCells()])],
            RelicCells = relics.ToImmutable(),
            ChokePoints = [],
            CentralEntrance = new Coord(boards[0].Origin.X + (boards[0].Width / 2), boards[0].Origin.Y + (boards[0].Height / 2)),
        };
    }

    /// <summary>棋盘外接矩形之间的间隔（两个方向上空格数的较大者，投影重叠的方向为负），与校验器 <c>ValidatePlateGaps</c> 同口径。</summary>
    internal static int Gap(BoardPlate a, BoardPlate b)
    {
        int dx = Math.Max(a.Origin.X - (b.Origin.X + b.Width), b.Origin.X - (a.Origin.X + a.Width));
        int dy = Math.Max(a.Origin.Y - (b.Origin.Y + b.Height), b.Origin.Y - (a.Origin.Y + a.Height));
        return Math.Max(dx, dy);
    }
}
