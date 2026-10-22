using System.Collections.Concurrent;
using System.Collections.Immutable;
using Siege.Core.Determinism;

namespace Siege.Core.Board.Maps;

/// <summary>
/// 链接档地图生成器（规格 link-map-generation；open-map 负责人 2026-10-19 裁决 O3 = A）。唯一入口 <see cref="Generate(ulong)"/>：由地图种子生成<b>初始布局</b>——
/// 5 块出生棋盘建局时即边对边紧贴成一张连通地图（3+2 两行，<see cref="InitialLayout"/>），地图在对局中不再变化（<see cref="MapData.MapVersion"/> 恒为 0）。
/// 对局中的拼接（<c>Link</c> / 链接序列穷举）已随 open-map 段 B 删除。
/// 全程整数运算，随机只在初始布局（<see cref="MapRandom.ForLinkLayout"/>）与新地表叠加（<see cref="MapRandom.ForLinkSurfaces"/>，link-map-surfaces D1，两条流互不扰动），布局几何不消费随机；不读时钟与环境，也拿不到对局种子。
/// </summary>
/// <remarks>
/// <para><b>初始布局</b>（link-map-terrain D3 / open-map D1）：5 块出生棋盘边长各 6–8、次序由地图种子决定；5 个主题两两不同、由地图种子排列；每块按主题生成地形（<c>LinkMapGenerator.Themes.cs</c>），
/// 出生区 = 中央 3×3 核心区，核心区内恰 2 个出生区档信物格（互不相邻、尽量不同行不同列），另在主题的难到达格上至多 1 个公共区档信物格；
/// 布局按次序把 5 块一次性贴成 3+2 两行（第一行底边对齐东贴，后两块北贴，共享边最长、偏西优先、放不下即换锚），画布按布局四周各留 <see cref="Margin"/> 格裁出一次。没有槽位、白名单、公共棋盘与通道。</para>
/// </remarks>
public static partial class LinkMapGenerator
{
    /// <summary>出生棋盘数，固定。</summary>
    public const int BirthBoards = 5;

    /// <summary>出生棋盘边长区间（link-map-terrain 裁决 M9；terrain-density 裁决 K1 = A 由 6–8 改为 6–7，抽取次数与顺序不变）。</summary>
    public const int BirthMinSide = 6;
    public const int BirthMaxSide = 7;

    /// <summary>每块出生棋盘的出生区档信物格数（裁决 J2），都在核心区内（link-map-terrain D1）。</summary>
    public const int RelicsPerBirthBoard = 2;

    /// <summary>画布留白：初始布局四周的空格数。</summary>
    public const int Margin = 2;

    private static readonly ConcurrentDictionary<ulong, MapData> Cache = new();

    /// <summary>按完整标识（<c>link:&lt;种子&gt;</c>）生成初始地图。</summary>
    public static MapData Generate(string mapId) => Generate(LinkMapId.Parse(mapId));

    /// <summary>生成初始地图。<see cref="MapData.Id"/> 为规范化标识，<see cref="MapData.MapVersion"/> 为 0。结果按种子缓存（地图不可变）。</summary>
    public static MapData Generate(ulong mapSeed) => Cache.GetOrAdd(mapSeed, static seed => Layout(seed, overlaySurfaces: true).Map);

    /// <summary>生成初始地图并给出每块出生棋盘（按编号）生成好的地形（terrain-barriers 测试用：湖泊 / 岩区、隔断线的逐块核对；不缓存）。</summary>
    internal static (MapData Map, ImmutableArray<BoardTerrain> Boards) GenerateWithBoards(ulong mapSeed)
    {
        (MapData map, _, _, ImmutableArray<BoardTerrain> boards) = Layout(mapSeed, overlaySurfaces: true);
        return (map, boards);
    }

    /// <summary>生成初始地图并给出每块出生棋盘（按编号）的重抽次数（tasks 2.3 的分布统计用；不缓存）。</summary>
    internal static (MapData Map, ImmutableArray<int> Redraws) GenerateWithRedraws(ulong mapSeed)
    {
        (MapData map, ImmutableArray<int> redraws, _, _) = Layout(mapSeed, overlaySurfaces: true);
        return (map, redraws);
    }

    /// <summary>生成初始地图并给出每块出生棋盘（按编号）实际放下的新地表格数（link-map-surfaces D3：测试与分布统计用；不缓存）。</summary>
    internal static (MapData Map, ImmutableArray<int> SpecialSurfaces) GenerateWithSurfaceCounts(ulong mapSeed)
    {
        (MapData map, _, ImmutableArray<int> surfaces, _) = Layout(mapSeed, overlaySurfaces: true);
        return (map, surfaces);
    }

    /// <summary>
    /// 测试接缝（link-map-surfaces D5 / 规格 Scenario「新地表不改其余地形」）：同一地图种子、不做新地表叠加的地图，即引入叠加之前的生成结果。不缓存，不经 <see cref="MapCatalog"/>，任何入口都拿不到它。
    /// </summary>
    internal static MapData GenerateWithoutSurfaces(ulong mapSeed) => Layout(mapSeed, overlaySurfaces: false).Map;

    /// <summary>
    /// 一次布局（link-map-terrain D3），布局与五主题地形的全部随机数来自 <see cref="MapRandom.ForLinkLayout"/> 这一条流，顺序固定：
    /// 次序 → 宽高（6–8）→ 主题排列（Fisher–Yates）→ 按编号逐块生成地形（主题地形含隔断线 → 散布岩石 → 湖泊 / 岩区，terrain-barriers D4；自检失败在同一条流上重抽该块）→ 出生区信物 → 公共信物。
    /// 之后（link-map-surfaces D1）用独立子流 <see cref="MapRandom.ForLinkSurfaces"/> 按编号逐块叠加新地表（<see cref="OverlaySurfaces"/>），布局流的消费顺序与次数不变；
    /// <paramref name="overlaySurfaces"/> 为 <c>false</c> 只供 <see cref="GenerateWithoutSurfaces"/> 的对照。
    /// 布局最后过一遍链接档静态校验，不通过即生成器缺陷（抛 <see cref="MapGenerationException"/>）；叠加不做重抽。
    /// </summary>
    private static (MapData Map, ImmutableArray<int> Redraws, ImmutableArray<int> SpecialSurfaces, ImmutableArray<BoardTerrain> Boards) Layout(ulong mapSeed, bool overlaySurfaces)
    {
        string id = LinkMapId.Format(mapSeed);
        RandomStream rng = MapRandom.ForLinkLayout(mapSeed);

        // 排列次序（Fisher–Yates）：order[k] = 从西到东第 k 块是哪个出生棋盘编号。
        int[] order = [.. Enumerable.Range(0, BirthBoards)];
        for (int i = order.Length - 1; i > 0; i--)
        {
            int j = rng.NextInt(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        // 各棋盘宽高按编号顺序抽取，再按次序摆放。
        var sizes = new (int W, int H)[BirthBoards];
        for (int zone = 0; zone < BirthBoards; zone++)
        {
            sizes[zone] = (BirthMinSide + rng.NextInt(BirthMaxSide - BirthMinSide + 1), BirthMinSide + rng.NextInt(BirthMaxSide - BirthMinSide + 1));
        }

        // 主题排列（Fisher–Yates，在宽高之后）：themes[z] = 出生棋盘 z 的主题，5 块两两不同（裁决 M2）。
        BoardTheme[] themes = [BoardTheme.Plateau, BoardTheme.River, BoardTheme.Forest, BoardTheme.Palisade, BoardTheme.Mixed];
        for (int i = themes.Length - 1; i > 0; i--)
        {
            int j = rng.NextInt(i + 1);
            (themes[i], themes[j]) = (themes[j], themes[i]);
        }

        // open-map D1：按次序把 5 块一次性边对边紧贴成一张连通地图（3+2 两行，InitialLayout 的纯几何，不消费随机）。
        var plates = new BoardPlate[BirthBoards];
        ImmutableArray<BoardPlate> arranged = InitialLayout.Arrange(
            [.. order.Select(zone => new BoardPlate(new Coord(0, 0), sizes[zone].W, sizes[zone].H, BoardPlateKind.Birth, themes[zone]))], Margin);
        for (int k = 0; k < order.Length; k++)
        {
            plates[order[k]] = arranged[k];
        }

        // 按编号逐块生成地形（自检失败在同一条流上重抽该块，D3）；布局已排好，自检含与已生成各块之间的接缝气边（terrain-barriers 实施修订 J2）。
        var boards = new BoardTerrain[BirthBoards];
        for (int zone = 0; zone < BirthBoards; zone++)
        {
            boards[zone] = DraftBoard(plates[zone], rng, [.. boards.Take(zone)]);
        }

        // 出生区信物：按编号顺序每块 2 个，在核心区内；第二个与第一个不相邻，尽量不同行不同列（没有这样的格时退到"不相邻"）。
        var relics = ImmutableDictionary.CreateBuilder<Coord, RelicCellSpec>();
        for (int zone = 0; zone < BirthBoards; zone++)
        {
            List<Coord> core = [.. plates[zone].CoreCells()];
            Coord first = core[rng.NextInt(core.Count)];
            List<Coord> apart = [.. core.Where(c => !Adjacency.AreAdjacent(c, first) && c != first)];
            List<Coord> preferred = [.. apart.Where(c => c.X != first.X && c.Y != first.Y)];
            List<Coord> candidates = preferred.Count > 0 ? preferred : apart;
            Coord second = candidates[rng.NextInt(candidates.Count)];
            relics[first] = new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth);
            relics[second] = new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth);
        }

        // 公共信物（D5 / 裁决 M8 / Q3）：按编号顺序，每块从主题的难到达格里取 1 个，档位 (Contested, Standard)；没有难到达格的这块放 0 个。
        for (int zone = 0; zone < BirthBoards; zone++)
        {
            ImmutableArray<Coord> hard = boards[zone].HardCells;
            if (hard.Length > 0)
            {
                relics[hard[rng.NextInt(hard.Length)]] = new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard);
            }
        }

        // link-map-surfaces D1：公共信物之后、组装之前，用独立子流按编号逐块叠加新地表（不消费布局流）。
        if (overlaySurfaces)
        {
            boards = OverlaySurfaces(boards, relics, MapRandom.ForLinkSurfaces(mapSeed));
        }

        TerrainData terrain = new(
            boards.SelectMany(b => b.Heights).ToImmutableDictionary(),
            boards.SelectMany(b => b.Surfaces).ToImmutableDictionary(),
            [.. boards.SelectMany(b => b.Bridges)],
            [.. boards.SelectMany(b => b.Fences)]);
        ImmutableHashSet<Coord> rocks = [.. boards.SelectMany(b => b.Rocks)];
        MapData map = Assemble(id, [.. plates], relics.ToImmutable(), terrain, rocks, ImmutableHashSet<Coord>.Empty);
        MapValidationResult validation = MapValidator.Validate(map);
        if (!validation.IsValid)
        {
            throw new MapGenerationException($"地图 {id} 的初始布局未通过链接档静态校验（生成器缺陷）：{string.Join("；", validation.Failures)}");
        }

        return (map, [.. boards.Select(b => b.Redraws)], [.. boards.Select(b => b.SpecialSurfaces)], [.. boards]);
    }

    /// <summary>
    /// 把棋盘清单灌成链接档地图数据：画布 = 全部棋盘外接矩形的东 / 北边界各外扩 <see cref="Margin"/> 格（原点固定在 (0,0)），棋盘之外与棋盘内的岩石一律障碍，
    /// 出生区 = 出生棋盘的核心区（link-map-terrain D1），中央入口取第一块棋盘的中心格（落在核心区内），没有咽喉。
    /// </summary>
    private static MapData Assemble(
        string id,
        ImmutableArray<BoardPlate> plates,
        ImmutableDictionary<Coord, RelicCellSpec> relics,
        TerrainData terrain,
        ImmutableHashSet<Coord> rocks,
        ImmutableHashSet<Coord> chokePoints)
    {
        int width = plates.Max(p => p.Origin.X + p.Width) + Margin;
        int height = plates.Max(p => p.Origin.Y + p.Height) + Margin;
        var playable = new HashSet<Coord>();
        foreach (BoardPlate plate in plates)
        {
            playable.UnionWith(plate.Cells());
        }

        playable.ExceptWith(rocks);
        var obstacles = ImmutableHashSet.CreateBuilder<Coord>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var c = new Coord(x, y);
                if (!playable.Contains(c))
                {
                    obstacles.Add(c);
                }
            }
        }

        BoardPlate first = plates[0];
        return new MapData
        {
            Id = id,
            Width = width,
            Height = height,
            MaxPlayers = 4,
            Profile = MapProfile.Link,
            Obstacles = obstacles.ToImmutable(),
            TerrainData = terrain,
            BirthZones = [.. plates.Where(p => p.Kind == BoardPlateKind.Birth).Select(p => (ImmutableHashSet<Coord>)[.. p.CoreCells()])],
            Boards = plates,
            RelicCells = relics,
            ChokePoints = chokePoints,
            CentralEntrance = new Coord(first.Origin.X + (first.Width / 2), first.Origin.Y + (first.Height / 2)),
            MapVersion = 0,
        };
    }
}
