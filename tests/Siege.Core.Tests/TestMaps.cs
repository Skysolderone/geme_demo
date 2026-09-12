using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Tests;

/// <summary>拓扑测试用的小盘面工厂。这些地图刻意不满足人数预算，只用 <see cref="GameBoard.LoadUnvalidated"/> 构造。</summary>
internal static class TestMaps
{
    internal static readonly PlayerId P0 = new(0);
    internal static readonly PlayerId P1 = new(1);

    /// <summary>构造一个 <paramref name="size"/>×<paramref name="size"/> 的空盘，指定格为障碍。</summary>
    internal static GameBoard Blank(int size = 11, params string[] obstacles) =>
        GameBoard.LoadUnvalidated(new MapData
        {
            Id = "test-blank",
            Width = size,
            Height = size,
            MaxPlayers = 4,
            Obstacles = obstacles.Select(Coord.Parse).ToImmutableHashSet(),
            BirthZones = [],
            RelicCells = ImmutableDictionary<Coord, RelicCellSpec>.Empty,
            ChokePoints = ImmutableHashSet<Coord>.Empty,
            CentralEntrance = new Coord(size / 2, size / 2),
        });

    internal static GameBoard Place(this GameBoard board, string notation, PlayerId owner, PieceType type = PieceType.Basic)
    {
        board.Place(Coord.Parse(notation), owner, type);
        return board;
    }

    internal static Coord At(string notation) => Coord.Parse(notation);

    internal static string[] Notations(this IEnumerable<Coord> coords) =>
        coords.Select(c => c.ToNotation()).ToArray();
}
