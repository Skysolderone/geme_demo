using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Tests;

/// <summary>拓扑测试用的小盘面工厂。这些地图刻意不满足人数预算，只用 <see cref="GameBoard.LoadUnvalidated"/> 构造。</summary>
internal static class TestMaps
{
    internal static readonly PlayerId P0 = new(0);
    internal static readonly PlayerId P1 = new(1);

    /// <summary>构造一个 <paramref name="size"/>×<paramref name="size"/> 的空盘，指定格为障碍。全平地。</summary>
    internal static GameBoard Blank(int size = 11, params string[] obstacles) =>
        Blank(TerrainData.Flat, size, obstacles);

    /// <summary>构造一个带地形的 <paramref name="size"/>×<paramref name="size"/> 空盘，指定格为障碍。</summary>
    internal static GameBoard Blank(TerrainData terrain, int size = 11, params string[] obstacles) =>
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
            TerrainData = terrain,
        });

    /// <summary>
    /// 用围棋记法拼一份 <see cref="TerrainData"/>：高度、地表、桥、栅栏均可省略。
    /// 构造期校验（桥须在深水、栅栏须相邻）由 <see cref="TerrainData"/> 自己做，这里不兜底。
    /// </summary>
    internal static TerrainData Terrain(
        (string Cell, int Height)[]? heights = null,
        (string Cell, Surface Surface)[]? surfaces = null,
        string[]? bridges = null,
        (string A, string B)[]? fences = null) =>
        new(
            (heights ?? []).ToImmutableDictionary(h => At(h.Cell), h => h.Height),
            (surfaces ?? []).ToImmutableDictionary(s => At(s.Cell), s => s.Surface),
            (bridges ?? []).Select(At).ToImmutableHashSet(),
            (fences ?? []).Select(f => new FenceEdge(At(f.A), At(f.B))).ToImmutableHashSet());

    /// <summary>
    /// 构造一张只用于校验测试的合成地图：<paramref name="size"/>×<paramref name="size"/>，
    /// 默认无障碍、无出生区、无信物。用它来单独触发某一条校验规则，
    /// 而不是拿 4 人基准图改——基准图的其他属性会连带触发一堆无关失败。
    /// </summary>
    internal static MapData Synthetic(
        int size,
        int maxPlayers,
        IEnumerable<Coord>? obstacles = null,
        IEnumerable<KeyValuePair<Coord, RelicCellSpec>>? relics = null,
        TerrainData? terrain = null) =>
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
            TerrainData = terrain ?? TerrainData.Flat,
        };

    /// <summary>
    /// 带信物格的 <paramref name="size"/>×<paramref name="size"/> 空盘（more-pieces-relics 旗手子测试）：信物格只是地图静态位置，
    /// 分区与档位对计分无影响，一律记为公共区标准档。
    /// </summary>
    internal static GameBoard WithRelicCells(string[] relicCells, TerrainData? terrain = null, int size = 11) =>
        GameBoard.LoadUnvalidated(Synthetic(size, maxPlayers: 4,
            relics: relicCells.Select(c => KeyValuePair.Create(At(c), new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard))),
            terrain: terrain));

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
