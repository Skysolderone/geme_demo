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

    /// <summary>
    /// 构造一张只用于校验测试的合成地图：<paramref name="size"/>×<paramref name="size"/>，
    /// 默认无障碍、无出生区、无信物。用它来单独触发某一条校验规则，
    /// 而不是拿 4 人基准图改——基准图的其他属性会连带触发一堆无关失败。
    /// </summary>
    internal static MapData Synthetic(
        int size,
        int maxPlayers,
        IEnumerable<Coord>? obstacles = null,
        IEnumerable<KeyValuePair<Coord, RelicCellSpec>>? relics = null) =>
        new()
        {
            Id = "test-synthetic",
            Width = size,
            Height = size,
            MaxPlayers = maxPlayers,
            Obstacles = (obstacles ?? []).ToImmutableHashSet(),
            BirthZones = [],
            RelicCells = (relics ?? []).ToImmutableDictionary(),
            ChokePoints = [new Coord(0, 0)],
            CentralEntrance = new Coord(size / 2, size / 2),
        };

    /// <summary>取该地图前 <paramref name="count"/> 个格（先行后列），用于凑出精确的障碍占比。</summary>
    internal static Coord[] FirstCells(this MapData map, int count) => [.. map.AllCoords().Take(count)];

    internal static GameBoard Place(this GameBoard board, string notation, PlayerId owner, PieceType type = PieceType.Basic)
    {
        board.Place(Coord.Parse(notation), owner, type);
        return board;
    }

    internal static Coord At(string notation) => Coord.Parse(notation);

    internal static string[] Notations(this IEnumerable<Coord> coords) =>
        coords.Select(c => c.ToNotation()).ToArray();
}
