using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Tests;

/// <summary>
/// 测试用链接档地图（board-link-mode 段 A）：手写的最小合规数据，不经生成器，便于单独验证校验器、战场导出与改造规则。
/// </summary>
internal static class LinkMapFixtures
{
    /// <summary>五块 5×5 出生棋盘：第一行 x = 0 / 20 / 40，第二行 x = 0 / 20（y = 20）；两两 Chebyshev 间隔恰 15。下标即出生区编号。</summary>
    internal static readonly ImmutableArray<BoardPlate> Births =
    [
        new(new Coord(0, 0), 5, 5, BoardPlateKind.Birth),
        new(new Coord(20, 0), 5, 5, BoardPlateKind.Birth),
        new(new Coord(40, 0), 5, 5, BoardPlateKind.Birth),
        new(new Coord(0, 20), 5, 5, BoardPlateKind.Birth),
        new(new Coord(20, 20), 5, 5, BoardPlateKind.Birth),
    ];

    internal const int Width = 45;
    internal const int Height = 25;

    /// <summary>45×25 的链接档初始图：只有 5 块出生棋盘，各 1 个信物格（中心），其余全部障碍；可落子 125、信物 5。</summary>
    internal static MapData Initial() => Build("test-link-5", Births, [], []);

    /// <summary>
    /// 按棋盘清单、棋盘外的可落子格（通道）与公共棋盘信物拼一张链接档图：其余格一律障碍，出生区 = 出生棋盘，出生棋盘信物固定在中心。
    /// </summary>
    internal static MapData Build(
        string id, ImmutableArray<BoardPlate> boards, ImmutableArray<Coord> corridors, ImmutableArray<(Coord Cell, BudgetTier Budget)> publicRelics, int mapVersion = 0)
    {
        ImmutableHashSet<Coord> playable = [.. boards.SelectMany(b => b.Cells()), .. corridors];
        ImmutableDictionary<Coord, RelicCellSpec>.Builder relics = ImmutableDictionary.CreateBuilder<Coord, RelicCellSpec>();
        foreach (BoardPlate birth in boards.Where(b => b.Kind == BoardPlateKind.Birth))
        {
            relics[new Coord(birth.Origin.X + (birth.Width / 2), birth.Origin.Y + (birth.Height / 2))] = new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth);
        }

        foreach ((Coord cell, BudgetTier budget) in publicRelics)
        {
            relics[cell] = new RelicCellSpec(RelicZone.Contested, budget);
        }

        return new MapData
        {
            Id = id,
            Width = Width,
            Height = Height,
            MaxPlayers = 4,
            Profile = MapProfile.Link,
            Boards = boards,
            MapVersion = mapVersion,
            Obstacles = [.. FrontierFixtures.Rect(0, 0, Width, Height).Where(c => !playable.Contains(c))],
            BirthZones = [.. boards.Where(b => b.Kind == BoardPlateKind.Birth).Select(b => (ImmutableHashSet<Coord>)[.. b.Cells()])],
            RelicCells = relics.ToImmutable(),
            ChokePoints = [.. corridors],
            CentralEntrance = new Coord(2, 2),
        };
    }

    /// <summary>公共棋盘 9×9，放在 0 号与 1 号出生棋盘之间（x = 8–16，y = 0–8），两条 3 宽 3 长的通道（x = 5–7 与 17–19，y = 0–2）。</summary>
    internal static readonly BoardPlate PublicBetween01 = new(new Coord(8, 0), 9, 9, BoardPlateKind.Public);

    internal static readonly ImmutableArray<Coord> CorridorsBetween01 =
        [.. FrontierFixtures.Rect(5, 0, 3, 3), .. FrontierFixtures.Rect(17, 0, 3, 3)];

    /// <summary>手写的"链接 0 与 1 之后"的物化状态（版本 1）：公共棋盘 2 个信物格。</summary>
    internal static MapData Linked01() => Build(
        "test-link-5", [.. Births, PublicBetween01], CorridorsBetween01,
        [(new Coord(12, 4), BudgetTier.High), (new Coord(9, 7), BudgetTier.Standard)], mapVersion: 1);
}
