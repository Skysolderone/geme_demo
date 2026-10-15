using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Relics;

namespace Siege.Core.Tests;

/// <summary>拓扑测试用的小盘面工厂。这些地图刻意不满足人数预算，只用 <see cref="GameBoard.LoadUnvalidated"/> 构造。</summary>
internal static class TestMaps
{
    internal static readonly PlayerId P0 = new(0);
    internal static readonly PlayerId P1 = new(1);
    internal static readonly PlayerId P2 = new(2);
    internal static readonly PlayerId P3 = new(3);
    internal static readonly PlayerId[] All = [P0, P1, P2, P3];

    /// <summary>测试公共种子（retire-legacy-rules 1.2：原 <c>MatchFixtures.Seed</c>，随 9×9 流程夹具删除迁到这里，值不变）。</summary>
    internal static readonly GameSeed Seed = new(0x5EED_0912_2026UL);

    /// <summary>
    /// 9×9 合成地图（retire-legacy-rules 1.2：原 <c>MatchFixtures.Map</c>，数据原样迁来）：四个 3×3 角落出生区（0 左下、1 右下、2 左上、3 右上），
    /// 无棋盘清单，信物格由用例指定。只供规则层（<see cref="GameBoard.LoadUnvalidated"/>）与地图数据层测试使用——对局只在链接档图上建局。
    /// </summary>
    internal static MapData NineByNine(params string[] relicCells) => NineByNine(null, relicCells);

    /// <summary>同上，另可指定地形。</summary>
    internal static MapData NineByNine(TerrainData? terrain, params string[] relicCells)
    {
        var spec = new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard);
        return new MapData
        {
            TerrainData = terrain ?? TerrainData.Flat,
            Id = "test-match-9x9",
            Width = 9,
            Height = 9,
            MaxPlayers = 4,
            Obstacles = [],
            BirthZones = [Corner(0, 0), Corner(6, 0), Corner(0, 6), Corner(6, 6)],
            RelicCells = relicCells.ToImmutableDictionary(Coord.Parse, _ => spec),
            ChokePoints = [Coord.Parse("E5")],
            CentralEntrance = Coord.Parse("E5"),
        };
    }

    private static ImmutableHashSet<Coord> Corner(int x0, int y0) => [.. Rect(x0, y0, 3, 3)];

    /// <summary>手工信物分布（原 <c>MatchFixtures.Relics</c>）：给定格与内容，一律记为公共区标准档；生成记录的种子取 <see cref="Seed"/>。</summary>
    internal static RelicGenerationRecord Relics(MapData map, params (string Cell, RelicContent Content)[] relics)
    {
        var spec = new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard);
        ImmutableArray<RelicPlacement> placements = [.. relics.Select(r => new RelicPlacement(Coord.Parse(r.Cell), r.Content, spec)).OrderBy(p => p.Coord)];
        return new RelicGenerationRecord(Seed, map.Id, placements, Converged: true, Rerolls: 0);
    }

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

    /// <summary>
    /// 左下角 (<paramref name="x0"/>, <paramref name="y0"/>)、宽 <paramref name="w"/> 高 <paramref name="h"/> 的矩形格集
    /// （retire-legacy-maps：原 <c>FrontierFixtures.Rect</c>，随旧地图夹具删除而原样迁到这里）。
    /// </summary>
    internal static IEnumerable<Coord> Rect(int x0, int y0, int w, int h) =>
        from y in Enumerable.Range(y0, h)
        from x in Enumerable.Range(x0, w)
        select new Coord(x, y);
}
