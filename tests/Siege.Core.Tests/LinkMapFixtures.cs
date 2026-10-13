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
    /// <summary>五块 5×5 出生棋盘一字排开：x = 2 / 10 / 18 / 26 / 34，y = 2，间距 3。下标即出生区编号。</summary>
    internal static readonly ImmutableArray<BoardPlate> Births =
    [
        new(new Coord(2, 2), 5, 5, BoardPlateKind.Birth),
        new(new Coord(10, 2), 5, 5, BoardPlateKind.Birth),
        new(new Coord(18, 2), 5, 5, BoardPlateKind.Birth),
        new(new Coord(26, 2), 5, 5, BoardPlateKind.Birth),
        new(new Coord(34, 2), 5, 5, BoardPlateKind.Birth),
    ];

    /// <summary>41×9 的链接档初始图：只有 5 块出生棋盘，各 2 个信物格，其余全部障碍；可落子 125、信物 10。</summary>
    internal static MapData Initial() => Build("test-link-5", Births);

    /// <summary>
    /// 手写的"链接 0 与 1 之后"（东贴，版本 1）的物化状态：1 号棋盘平移到 0 号东侧紧贴（x = 7），其余三块按原次序排到合并战场东侧（x = 15 / 23 / 31，间距 3）；画布 38×9。
    /// </summary>
    internal static MapData Linked01() => Build(
        "test-link-5",
        [
            Births[0],
            new(new Coord(7, 2), 5, 5, BoardPlateKind.Birth),
            new(new Coord(15, 2), 5, 5, BoardPlateKind.Birth),
            new(new Coord(23, 2), 5, 5, BoardPlateKind.Birth),
            new(new Coord(31, 2), 5, 5, BoardPlateKind.Birth),
        ],
        mapVersion: 1);

    /// <summary>
    /// 按棋盘清单拼一张链接档图：画布 = 清单外接矩形的东 / 北边界各外扩 2 格（原点固定），棋盘之外一律障碍，出生区 = 出生棋盘，
    /// 每块出生棋盘的信物格固定在左下角起 (1,1) 与 (3,3)（最外一圈以内、不相邻、不同行不同列）。
    /// </summary>
    internal static MapData Build(string id, ImmutableArray<BoardPlate> boards, int mapVersion = 0)
    {
        int width = boards.Max(b => b.Origin.X + b.Width) + LinkMapGenerator.Margin;
        int height = boards.Max(b => b.Origin.Y + b.Height) + LinkMapGenerator.Margin;
        ImmutableHashSet<Coord> playable = [.. boards.SelectMany(b => b.Cells())];
        ImmutableDictionary<Coord, RelicCellSpec>.Builder relics = ImmutableDictionary.CreateBuilder<Coord, RelicCellSpec>();
        foreach (BoardPlate birth in boards.Where(b => b.Kind == BoardPlateKind.Birth))
        {
            relics[new Coord(birth.Origin.X + 1, birth.Origin.Y + 1)] = new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth);
            relics[new Coord(birth.Origin.X + 3, birth.Origin.Y + 3)] = new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth);
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
            BirthZones = [.. boards.Where(b => b.Kind == BoardPlateKind.Birth).Select(b => (ImmutableHashSet<Coord>)[.. b.Cells()])],
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
