using System.Collections.Concurrent;
using System.Collections.Immutable;
using Siege.Core.Determinism;

namespace Siege.Core.Board.Maps;

/// <summary>
/// 链接档地图生成器（规格 link-map-generation；map-expand-25 D1–D5 / 裁决 Q3–Q6、R1–R6；seat-gap-25 E1–E3 / 裁决 S1–S6）。唯一入口 <see cref="Generate(ulong, int)"/>：由地图种子与人数生成一整座<b>长方形浮空岛</b>——
/// 恰 n 个 8×8 出生座位（位置只由人数决定），任意两座位外接矩形间距 ≥ <see cref="SeatGap"/>，座位之间的战场里散布 n–(n+1) 块 6–8 边长的中立平台；
/// 平台（座位与中立平台一视同仁）为 h=2 高台、四周崖壁、各有 2–3 个 h=1 坡道；平台之间是 h=0 低地，夹 n 条深水河道（各 2 座预置桥），岛缘点缀林地与少量岩石。
/// 座位是出生棋盘（<see cref="BoardPlateKind.Birth"/>），中立平台是公共棋盘（<see cref="BoardPlateKind.Public"/>，带主题、不可出生）；地图在对局中不变（<see cref="MapData.MapVersion"/> 恒为 0）。
/// 全程整数运算，随机只来自布局流（<see cref="MapRandom.ForLinkLayout"/>）与新地表叠加流（<see cref="MapRandom.ForLinkSurfaces"/>），两条流互不扰动；不读时钟与环境，也拿不到对局种子。
/// </summary>
/// <remarks>
/// <para><b>几何</b>（seat-gap-25 E1）：岛为 W×H 长方形（<see cref="IslandSize"/>：2 人 45×33，3 / 4 人 45×45），占 [M, M+W)×[M, M+H)，画布 (W+2M)×(H+2M)，
/// 留白 M = <see cref="Margin"/>；岛四角各切去直角边 <see cref="CornerCut"/> 的三角（障碍）。平台距岛边 ≥ <see cref="EdgeGap"/>、两两外接矩形间隔 ≥ <see cref="PlateGap"/>。</para>
/// <para><b>座位</b>（E1）：8×8、距岛边 <see cref="EdgeGap"/>；2 人西、东各一（纵向居中），3 人西南、东南两角 + 北边横向居中，4 人四角。座位位置不消费随机。</para>
/// <para><b>编号</b>：<see cref="MapData.Boards"/> 座位在前、按左下角先行后列（自南向北、自西向东）排序，第 i 个座位即出生棋盘 i、出生区 i（核心 3×3）；中立平台在后，同样按左下角先行后列。</para>
/// </remarks>
public static partial class LinkMapGenerator
{
    /// <summary>岛宽（东西向格数，三档相同：2 个座位 8 + 2 × 岛边距 2 + 座位间距 25 = 45）。</summary>
    public const int IslandWidth = 45;

    /// <summary>2 人岛的高（南北向格数）：东西对置时纵向不需要再放座位，取 33（座位纵向居中，上下各 12.5 格低地）。</summary>
    public const int IslandHeightTwoPlayers = 33;

    /// <summary>画布留白：岛四周的空格数（障碍）。</summary>
    public const int Margin = 2;

    /// <summary>岛四角切去的直角三角的直角边长（直角边 3 时每角 6 格）。</summary>
    public const int CornerCut = 3;

    /// <summary>平台外接矩形距岛边的最少格数（其间是低地，坡道朝外格与岛缘林带都在这里）；座位恰好贴着这条边距线。</summary>
    public const int EdgeGap = 2;

    /// <summary>两块平台外接矩形之间的最少间隔（低地走廊宽度）。</summary>
    public const int PlateGap = 3;

    /// <summary>出生座位边长（固定 8×8，座位之间公平）。</summary>
    public const int SeatSide = 8;

    /// <summary>任意两座位外接矩形之间的最小间距（裁决 S1：两矩形相隔的格数取横纵较大者，≥ 25 即中间能完整放下 25×25 战场）。</summary>
    public const int SeatGap = 25;

    /// <summary>中立平台的宽、高区间（D2：主题生成器已验证的区间）。</summary>
    public const int PlateMinSide = 6;
    public const int PlateMaxSide = 8;

    /// <summary>每块平台的坡道数区间（D3）。</summary>
    public const int MinRamps = 2;
    public const int MaxRamps = 3;

    /// <summary>河道最短格数：去掉两端后至少 4 格，总能放下两座不相邻的桥（D4：每条河道恰 2 座预置桥，见 <see cref="TryLowland"/>）。</summary>
    internal const int MinRiverLength = 6;

    /// <summary>整张布局（平台落位 → 主题地形 → 坡道）在同一条流上的最多重抽次数（D2）；超出即 <see cref="MapGenerationException"/>。</summary>
    public const int MaxLayoutRedraws = 50;

    /// <summary>低地（河道 + 岛缘点缀）在同一条流上的最多重抽次数（D4）；超出即整张布局重抽。</summary>
    public const int MaxLowlandRedraws = 50;

    /// <summary>平台主题（D3 / 裁决 R3）：河流 / 林地 / 栅栏阵 / 混合四选一、可重复；不再出现高台。</summary>
    internal static readonly ImmutableArray<BoardTheme> IslandThemes = [BoardTheme.River, BoardTheme.Forest, BoardTheme.Palisade, BoardTheme.Mixed];

    private static readonly ConcurrentDictionary<(ulong Seed, int Players), MapData> Cache = new();

    /// <summary>四个朝向（南、北、西、东）的坐标增量：只做坐标算术（坡道朝外、河道前进），不用来枚举邻居。</summary>
    private static readonly ImmutableArray<(int Dx, int Dy)> Directions = [(0, -1), (0, 1), (-1, 0), (1, 0)];

    /// <summary>人数为 <paramref name="players"/> 时岛的宽与高（E1：2 人 45×33，3 / 4 人 45×45；不含留白）。</summary>
    public static (int Width, int Height) IslandSize(int players)
    {
        LinkMapId.RequirePlayers(players);
        return (IslandWidth, players == 2 ? IslandHeightTwoPlayers : IslandWidth);
    }

    /// <summary>人数为 <paramref name="players"/> 时的中立平台数区间（裁决 S4：2 / 3 / 4 人 2–3 / 3–4 / 4–5）。</summary>
    public static (int Min, int Max) NeutralPlateCount(int players)
    {
        LinkMapId.RequirePlayers(players);
        return (players, players + 1);
    }

    /// <summary>按完整标识（<c>link:&lt;种子&gt;:&lt;人数&gt;</c>）生成初始地图。</summary>
    public static MapData Generate(string mapId)
    {
        (ulong seed, int players) = LinkMapId.Parse(mapId);
        return Generate(seed, players);
    }

    /// <summary>生成初始地图。<see cref="MapData.Id"/> 为规范化标识，<see cref="MapData.MaxPlayers"/> 为人数，<see cref="MapData.MapVersion"/> 为 0。结果按 (种子, 人数) 缓存（地图不可变）。</summary>
    public static MapData Generate(ulong mapSeed, int players)
    {
        LinkMapId.RequirePlayers(players);
        return Cache.GetOrAdd((mapSeed, players), static key => Layout(key.Seed, key.Players, overlaySurfaces: true).Map);
    }

    /// <summary>
    /// 一次生成的全部产物（测试与分布统计用，不缓存）：地图、各平台叠加后<b>抬升前</b>的地形（按编号）、全部坡道格、整张布局与低地（最后一次布局里）的重抽次数。
    /// </summary>
    internal sealed record IslandGeneration(
        MapData Map,
        ImmutableArray<BoardTerrain> Boards,
        ImmutableHashSet<Coord> Ramps,
        int LayoutRedraws,
        int LowlandRedraws);

    /// <summary>生成初始地图并给出中间产物（测试用：逐平台核对、重抽次数分布；不缓存）。</summary>
    internal static IslandGeneration GenerateDetailed(ulong mapSeed, int players)
    {
        LinkMapId.RequirePlayers(players);
        return Layout(mapSeed, players, overlaySurfaces: true);
    }

    /// <summary>
    /// 测试接缝（link-map-surfaces D5 / 规格 Scenario「新地表不改其余地形」）：同一标识、不做新地表叠加的地图。不缓存，不经 <see cref="MapCatalog"/>，任何入口都拿不到它。
    /// </summary>
    internal static MapData GenerateWithoutSurfaces(ulong mapSeed, int players)
    {
        LinkMapId.RequirePlayers(players);
        return Layout(mapSeed, players, overlaySurfaces: false).Map;
    }

    /// <summary>
    /// 一次生成（map-expand-25 D2–D5；seat-gap-25 E1–E3 / E7）。座位几何只由人数决定，不消费随机。布局流 <see cref="MapRandom.ForLinkLayout"/> 的消费顺序固定：
    /// 中立平台数 → 逐块中立平台（宽、高 → 落位）→ 按编号（座位在前、中立平台在后）抽主题 → 按编号逐块生成平台内部（<see cref="DraftBoard"/>，自检失败在同一条流上重抽该块）→ 按编号逐块坡道（数量 → 边的次序 → 每边一格）；
    /// 任何一步放不下即整张布局在同一条流上重抽（至多 <see cref="MaxLayoutRedraws"/> 次）。之后低地：n 条河道（起点 → 朝向 → 逐步前进 → 两座桥）→ 岛缘林地（逐格）→ 岛缘岩石，
    /// 全图可落子格沿气边不连通即重抽低地（至多 <see cref="MaxLowlandRedraws"/> 次，仍不行即整张布局重抽）。座位不做镜像 / 旋转（E1 留作可选，未实施），也就不为它消费随机。
    /// 不放静态信物格（D5：依据 relic-redesign R29，2026-10-26 用户裁决；relic-redesign 接线段起地图数据不再有信物格字段），布局流也不为信物消费随机。
    /// 最后（link-map-surfaces D1）用独立子流 <see cref="MapRandom.ForLinkSurfaces"/> 按编号逐块（座位与中立平台一视同仁）叠加新地表（读抬升前的高度，D3），组装时把平台抬到 h=2、坡道 h=1。
    /// 叠加只改平台中间带的草地，不影响前面任何一步的判定，所以 <paramref name="overlaySurfaces"/> 为 <c>false</c> 的对照图与正式图只差新地表。
    /// 成品过一遍链接档静态校验，不通过即生成器缺陷（抛 <see cref="MapGenerationException"/>）。
    /// </summary>
    private static IslandGeneration Layout(ulong mapSeed, int players, bool overlaySurfaces)
    {
        string id = LinkMapId.Format(mapSeed, players);
        (int width, int height) = IslandSize(players);
        var island = new Island(width, height);
        RandomStream rng = MapRandom.ForLinkLayout(mapSeed, players);
        for (int layoutRedraw = 0; layoutRedraw <= MaxLayoutRedraws; layoutRedraw++)
        {
            if (TryLayout(island, players, rng) is not { } layout)
            {
                continue;
            }

            for (int lowlandRedraw = 0; lowlandRedraw <= MaxLowlandRedraws; lowlandRedraw++)
            {
                if (TryLowland(island, players, layout, rng) is not { } lowland)
                {
                    continue;
                }

                BoardTerrain[] boards = overlaySurfaces
                    ? OverlaySurfaces(layout.Boards, MapRandom.ForLinkSurfaces(mapSeed, players))
                    : [.. layout.Boards];
                MapData map = Assemble(id, players, island, layout, boards, lowland);
                MapValidationResult validation = MapValidator.Validate(map);
                if (!validation.IsValid)
                {
                    throw new MapGenerationException($"地图 {id} 未通过链接档静态校验（生成器缺陷）：{string.Join("；", validation.Failures)}");
                }

                return new IslandGeneration(map, [.. boards], layout.Ramps, layoutRedraw, lowlandRedraw);
            }
        }

        throw new MapGenerationException($"地图 {id} 的布局重抽 {MaxLayoutRedraws} 次仍未得到合规的浮空岛（生成器缺陷）。");
    }

    /// <summary>岛的几何（E1）：宽、高、画布、切角与岛缘判定，全是坐标算术。</summary>
    private sealed class Island(int width, int height)
    {
        internal int Width { get; } = width;

        internal int Height { get; } = height;

        internal int CanvasWidth => Width + (2 * Margin);

        internal int CanvasHeight => Height + (2 * Margin);

        /// <summary>某格到最近的东西边、南北边的格数（岛外为负）。</summary>
        private (int Dx, int Dy) EdgeDistance(Coord c) =>
            (Math.Min(c.X - Margin, Margin + Width - 1 - c.X), Math.Min(c.Y - Margin, Margin + Height - 1 - c.Y));

        /// <summary>该格是否是岛上的格（在 [M, M+W)×[M, M+H) 内且不在切角里）。</summary>
        internal bool Contains(Coord c)
        {
            (int dx, int dy) = EdgeDistance(c);
            return dx >= 0 && dy >= 0 && dx + dy >= CornerCut;
        }

        /// <summary>该格是否在岛缘一圈：岛上、且紧挨岛外或切角。</summary>
        internal bool OnEdge(Coord c)
        {
            (int dx, int dy) = EdgeDistance(c);
            return Contains(c) && (dx == 0 || dy == 0 || dx + dy == CornerCut);
        }

        /// <summary>按先行后列枚举岛上的全部格。</summary>
        internal IEnumerable<Coord> Cells()
        {
            for (int y = Margin; y < Margin + Height; y++)
            {
                for (int x = Margin; x < Margin + Width; x++)
                {
                    var c = new Coord(x, y);
                    if (Contains(c))
                    {
                        yield return c;
                    }
                }
            }
        }

        /// <summary>
        /// 出生座位（E1，不消费随机）：8×8、距岛边 <see cref="EdgeGap"/>，按左下角先行后列。2 人西、东各一（纵向居中，偏南）；3 人西南、东南 + 北边横向居中（偏西）；4 人四角。
        /// 三档的东西两座位横向相隔 W − 2·EdgeGap − 2·8 = 25 格，3 / 4 人南北两座位纵向相隔 H − 2·EdgeGap − 2·8 = 25 格，间距恰为 <see cref="SeatGap"/>。
        /// </summary>
        internal ImmutableArray<BoardPlate> Seats(int players)
        {
            int west = Margin + EdgeGap;
            int east = Margin + Width - EdgeGap - SeatSide;
            int south = Margin + EdgeGap;
            int north = Margin + Height - EdgeGap - SeatSide;
            Coord[] origins = players switch
            {
                2 => [new(west, Margin + ((Height - SeatSide) / 2)), new(east, Margin + ((Height - SeatSide) / 2))],
                3 => [new(west, south), new(east, south), new(Margin + ((Width - SeatSide) / 2), north)],
                _ => [new(west, south), new(east, south), new(west, north), new(east, north)],
            };
            return [.. origins.Select(o => new BoardPlate(o, SeatSide, SeatSide, BoardPlateKind.Birth))];
        }
    }

    /// <summary>平台一侧的布局结果：平台（按编号）、各平台抬升前的地形、坡道格与坡道朝外格。</summary>
    private sealed record PlateLayout(
        ImmutableArray<BoardPlate> Plates,
        ImmutableArray<BoardTerrain> Boards,
        ImmutableHashSet<Coord> Ramps,
        ImmutableHashSet<Coord> RampOutlets)
    {
        /// <summary>该格是否在某块平台的外接矩形内。</summary>
        internal bool InPlate(Coord c) => Plates.Any(p => p.Contains(c));
    }

    /// <summary>低地一侧的结果：河道深水格、河道上的预置桥、岛缘林地与岩石。</summary>
    private sealed record Lowland(
        ImmutableHashSet<Coord> Water,
        ImmutableHashSet<Coord> Bridges,
        ImmutableHashSet<Coord> Forest,
        ImmutableHashSet<Coord> Rocks);

    /// <summary>一次布局尝试（D2 / D3）：平台落位 → 主题 → 平台内部 → 坡道。放不下返回 <c>null</c>（调用方在同一条流上重抽）。</summary>
    private static PlateLayout? TryLayout(Island island, int players, RandomStream rng)
    {
        if (PlacePlates(island, players, rng) is not { } rects)
        {
            return null;
        }

        BoardPlate[] plates = [.. rects.Select(r => r with { Theme = IslandThemes[rng.NextInt(IslandThemes.Length)] })];
        var boards = new BoardTerrain[plates.Length];
        for (int zone = 0; zone < plates.Length; zone++)
        {
            boards[zone] = DraftBoard(plates[zone], rng);
        }

        // 坡道（D3）：每块 2–3 个，各在不同的一条边上（四条边的次序 Fisher–Yates 打乱后依次取，某边没有候选即跳过），落在外圈非角格。
        var ramps = ImmutableHashSet.CreateBuilder<Coord>();
        var outlets = ImmutableHashSet.CreateBuilder<Coord>();
        foreach (BoardTerrain board in boards)
        {
            int count = MinRamps + rng.NextInt(MaxRamps - MinRamps + 1);
            int[] sides = [0, 1, 2, 3];
            for (int i = sides.Length - 1; i > 0; i--)
            {
                int j = rng.NextInt(i + 1);
                (sides[i], sides[j]) = (sides[j], sides[i]);
            }

            int placed = 0;
            foreach (int side in sides)
            {
                if (placed == count)
                {
                    break;
                }

                List<Coord> spots = [.. RampSpots(board, side)];
                if (spots.Count == 0)
                {
                    continue;
                }

                Coord ramp = spots[rng.NextInt(spots.Count)];
                ramps.Add(ramp);
                outlets.Add(Step(ramp, Directions[side], 1));
                placed++;
            }

            if (placed < MinRamps)
            {
                return null;
            }
        }

        return new PlateLayout([.. plates], [.. boards], ramps.ToImmutable(), outlets.ToImmutable());
    }

    /// <summary>
    /// 平台落位（seat-gap-25 E1 / E2；贴紧式候选沿用 map-expand-25 段 A1 实施修订）：n 个座位固定（<see cref="Island.Seats"/>，不消费随机）；中立平台数在区间里抽一次，
    /// 逐块抽宽、高（6–8），在全部合法原点里只保留"贴紧"的——横纵两个方向都恰好顶在岛边距线上、或恰好与某块已放平台隔 <see cref="PlateGap"/>——按先行后列排好后均匀抽一个。
    /// 抽到的尺寸没有候选时退到 6×6 再找（不消费随机）；仍没有就停止追加。中立平台放够下限即成功，否则返回 <c>null</c>。
    /// 结果座位在前（按左下角先行后列）、中立平台在后（同样排序），保证 <see cref="MapData.Boards"/> 里出生棋盘 i 即出生区 i。
    /// </summary>
    private static List<BoardPlate>? PlacePlates(Island island, int players, RandomStream rng)
    {
        (int min, int max) = NeutralPlateCount(players);
        int target = min + rng.NextInt(max - min + 1);
        List<BoardPlate> plates = [.. island.Seats(players)];
        var neutral = new List<BoardPlate>();
        while (neutral.Count < target)
        {
            int width = PlateMinSide + rng.NextInt(PlateMaxSide - PlateMinSide + 1);
            int height = PlateMinSide + rng.NextInt(PlateMaxSide - PlateMinSide + 1);
            List<BoardPlate> spots = PlateSpots(island, plates, width, height);
            if (spots.Count == 0 && (width, height) != (PlateMinSide, PlateMinSide))
            {
                spots = PlateSpots(island, plates, PlateMinSide, PlateMinSide);
            }

            if (spots.Count == 0)
            {
                break;
            }

            BoardPlate chosen = spots[rng.NextInt(spots.Count)];
            plates.Add(chosen);
            neutral.Add(chosen);
        }

        return neutral.Count >= min
            ? [.. plates.Take(players).OrderBy(p => p.Origin.Y).ThenBy(p => p.Origin.X), .. neutral.OrderBy(p => p.Origin.Y).ThenBy(p => p.Origin.X)]
            : null;
    }

    /// <summary>宽 <paramref name="width"/>、高 <paramref name="height"/> 的平台全部"贴紧"的合法落位，先行后列。</summary>
    private static List<BoardPlate> PlateSpots(Island island, List<BoardPlate> placed, int width, int height)
    {
        int low = Margin + EdgeGap;
        int highX = Margin + island.Width - EdgeGap - width;
        int highY = Margin + island.Height - EdgeGap - height;
        HashSet<int> xs = [low, highX, .. placed.Select(p => p.Origin.X + p.Width + PlateGap), .. placed.Select(p => p.Origin.X - PlateGap - width)];
        HashSet<int> ys = [low, highY, .. placed.Select(p => p.Origin.Y + p.Height + PlateGap), .. placed.Select(p => p.Origin.Y - PlateGap - height)];
        var spots = new List<BoardPlate>();
        foreach (int y in ys.Where(y => y >= low && y <= highY).Order())
        {
            foreach (int x in xs.Where(x => x >= low && x <= highX).Order())
            {
                var plate = new BoardPlate(new Coord(x, y), width, height, BoardPlateKind.Public);
                if (placed.All(p => MapValidator.PlatesApart(p, plate, PlateGap)))
                {
                    spots.Add(plate);
                }
            }
        }

        return spots;
    }

    /// <summary>
    /// 平台 <paramref name="board"/> 第 <paramref name="side"/> 条边（<see cref="Directions"/> 的下标）上可作坡道的格，先行后列：外圈非角格、草地、不是岩石，
    /// 且朝内的格不是岩石、不是深水（坡道确实接进平台内部；平台内可落子格沿气边连通由 <see cref="DraftBoard"/> 的自检保证）。朝外的格由落位保证是低地。
    /// </summary>
    private static IEnumerable<Coord> RampSpots(BoardTerrain board, int side)
    {
        BoardPlate plate = board.Plate;
        int left = plate.Origin.X, right = plate.Origin.X + plate.Width - 1, bottom = plate.Origin.Y, top = plate.Origin.Y + plate.Height - 1;
        foreach (Coord c in plate.Cells().Where(plate.OnRim))
        {
            bool onSide = side switch
            {
                0 => c.Y == bottom,
                1 => c.Y == top,
                2 => c.X == left,
                _ => c.X == right,
            };
            bool corner = (c.X == left || c.X == right) && (c.Y == bottom || c.Y == top);
            Coord inward = Step(c, Directions[side], -1);
            if (onSide && !corner
                && !board.Rocks.Contains(c) && !board.Surfaces.ContainsKey(c)   // 主题地形只写非草地地表：不在表里即草地
                && !board.Rocks.Contains(inward) && board.Surfaces.GetValueOrDefault(inward) != Surface.DeepWater)
            {
                yield return c;
            }
        }
    }

    /// <summary>
    /// 一次低地尝试（D4）：n 条河道（各 2 座预置桥）→ 岛缘林地 → 岛缘岩石，然后全图可落子格沿气边须连通。不满足返回 <c>null</c>（调用方在同一条流上重抽低地）。
    /// </summary>
    private static Lowland? TryLowland(Island island, int players, PlateLayout layout, RandomStream rng)
    {
        var water = new HashSet<Coord>();
        var bridges = new HashSet<Coord>();
        for (int k = 0; k < players; k++)
        {
            if (DrawRiver(island, layout, water, rng) is not { } river)
            {
                return null;
            }

            // 两座桥：去掉两端的河道格里均匀抽一座，再从与它不相邻的格里抽一座（折线 1 格宽、去掉两端 ≥ 4 格，总有这样的格）。
            Coord[] inner = [.. river.Skip(1).Take(river.Count - 2)];
            Coord first = inner[rng.NextInt(inner.Length)];
            Coord[] apart = [.. inner.Where(c => c != first && !Adjacency.AreAdjacent(c, first))];
            Coord second = apart[rng.NextInt(apart.Length)];
            water.UnionWith(river);
            bridges.Add(first);
            bridges.Add(second);
        }

        // 岛缘点缀（参考图的林带）：岛缘一圈的低地格逐格 1/4 概率为林地；再放 2 – (n+1) 格岩石（障碍）。都不占河道与坡道朝外格。
        bool Free(Coord c) => island.OnEdge(c) && !water.Contains(c) && !layout.RampOutlets.Contains(c);
        var forest = new HashSet<Coord>();
        foreach (Coord c in island.Cells().Where(Free))
        {
            if (rng.NextInt(4) == 0)
            {
                forest.Add(c);
            }
        }

        var rocks = new HashSet<Coord>();
        int rockCount = 2 + rng.NextInt(players);
        for (int k = 0; k < rockCount; k++)
        {
            List<Coord> spots = [.. island.Cells().Where(c => Free(c) && !forest.Contains(c) && !rocks.Contains(c))];
            if (spots.Count == 0)
            {
                break;
            }

            rocks.Add(spots[rng.NextInt(spots.Count)]);
        }

        var lowland = new Lowland([.. water], [.. bridges], [.. forest], [.. rocks]);
        MapData preview = Assemble("link-island-preview", players, island, layout, layout.Boards, lowland);
        return MapValidator.LibertySplit(preview).IsEmpty ? lowland : null;
    }

    /// <summary>
    /// 一条河道（D4）：1 格宽的深水折线，从岛缘一格出发朝岛内走，每步直行（4/6）或左右转（各 1/6；走不通依次换直行 / 另两个方向），
    /// 走满 <see cref="MinRiverLength"/> 格后碰到岛缘或某平台崖下即停。不进平台、切角与坡道朝外格，不挨着自己走过的格（保持 1 格宽），也不挨着别的河道。走投无路返回 <c>null</c>。
    /// </summary>
    private static List<Coord>? DrawRiver(Island island, PlateLayout layout, HashSet<Coord> water, RandomStream rng)
    {
        bool Open(Coord c) => island.Contains(c) && !layout.InPlate(c) && !layout.RampOutlets.Contains(c) && !water.Contains(c)
            && !water.Any(w => Adjacency.AreAdjacent(w, c));
        List<Coord> starts = [.. island.Cells().Where(c => island.OnEdge(c) && Open(c))];
        if (starts.Count == 0)
        {
            return null;
        }

        Coord start = starts[rng.NextInt(starts.Count)];
        List<int> inward = [.. Enumerable.Range(0, Directions.Length).Where(d => !island.Contains(Step(start, Directions[d], -1)) && Open(Step(start, Directions[d], 1)))];
        if (inward.Count == 0)
        {
            return null;
        }

        int heading = inward[rng.NextInt(inward.Count)];
        List<Coord> path = [start];
        for (int step = 0; step < 2 * (island.Width + island.Height); step++)
        {
            Coord here = path[^1];
            bool Next(Coord c) => Open(c) && !path.Contains(c) && !path.Take(path.Count - 1).Any(p => Adjacency.AreAdjacent(p, c));
            int roll = rng.NextInt(6);
            int[] order = roll < 4 ? [heading, Left(heading), Right(heading)] : roll == 4 ? [Left(heading), heading, Right(heading)] : [Right(heading), heading, Left(heading)];
            int chosen = Array.FindIndex(order, d => Next(Step(here, Directions[d], 1)));
            if (chosen < 0)
            {
                return null;
            }

            heading = order[chosen];
            Coord next = Step(here, Directions[heading], 1);
            path.Add(next);
            if (path.Count >= MinRiverLength && (island.OnEdge(next) || layout.Plates.Any(p => UnderCliff(p, next))))
            {
                return path;
            }
        }

        return null;
    }

    private static Coord Step(Coord c, (int Dx, int Dy) d, int k) => new(c.X + (d.Dx * k), c.Y + (d.Dy * k));

    /// <summary><see cref="Directions"/> 下标的左转 / 右转（南 0、北 1、西 2、东 3）。</summary>
    private static int Left(int d) => d switch { 0 => 3, 1 => 2, 2 => 0, _ => 1 };

    private static int Right(int d) => d switch { 0 => 2, 1 => 3, 2 => 1, _ => 0 };

    /// <summary>该格是否紧挨平台外接矩形的一条边（崖下）：在矩形外、到矩形的横纵距离之和恰为 1。</summary>
    private static bool UnderCliff(BoardPlate p, Coord c) =>
        Math.Max(Math.Max(p.Origin.X - c.X, c.X - (p.Origin.X + p.Width - 1)), 0) + Math.Max(Math.Max(p.Origin.Y - c.Y, c.Y - (p.Origin.Y + p.Height - 1)), 0) == 1;

    /// <summary>
    /// 灌成链接档地图数据：画布 (W+2M)×(H+2M)，岛外、切角与全部岩石为障碍；平台抬升（D3：外接矩形内每格 h=2、坡道 h=1，主题自带的高度——混合主题的 1 格台——一并抹平）、
    /// 平台地表与低地河道 / 林地、平台与河道的预置桥、平台栅栏；出生区 = 各座位（出生棋盘）核心区（按编号，中立平台不是出生区），
    /// 中央入口见 <see cref="CentralEntranceOf"/>，没有咽喉，没有信物格（D5）。
    /// </summary>
    private static MapData Assemble(
        string id,
        int players,
        Island island,
        PlateLayout layout,
        IReadOnlyList<BoardTerrain> boards,
        Lowland lowland)
    {
        ImmutableHashSet<Coord> rocks = [.. boards.SelectMany(b => b.Rocks), .. lowland.Rocks];
        var obstacles = ImmutableHashSet.CreateBuilder<Coord>();
        for (int y = 0; y < island.CanvasHeight; y++)
        {
            for (int x = 0; x < island.CanvasWidth; x++)
            {
                var c = new Coord(x, y);
                if (!island.Contains(c) || rocks.Contains(c))
                {
                    obstacles.Add(c);
                }
            }
        }

        var terrain = new TerrainData(
            layout.Plates.SelectMany(p => p.Cells()).ToImmutableDictionary(c => c, c => layout.Ramps.Contains(c) ? 1 : 2),
            boards.SelectMany(b => b.Surfaces)
                .Concat(lowland.Water.Select(c => KeyValuePair.Create(c, Surface.DeepWater)))
                .Concat(lowland.Forest.Select(c => KeyValuePair.Create(c, Surface.Forest)))
                .ToImmutableDictionary(),
            [.. boards.SelectMany(b => b.Bridges), .. lowland.Bridges],
            [.. boards.SelectMany(b => b.Fences)]);

        var map = new MapData
        {
            Id = id,
            Width = island.CanvasWidth,
            Height = island.CanvasHeight,
            MaxPlayers = players,
            Profile = MapProfile.Link,
            Obstacles = obstacles.ToImmutable(),
            TerrainData = terrain,
            BirthZones = [.. layout.Plates.Where(p => p.Kind == BoardPlateKind.Birth).Select(p => (ImmutableHashSet<Coord>)[.. p.CoreCells()])],
            Boards = layout.Plates,
            ChokePoints = [],
            CentralEntrance = new Coord(0, 0),
            MapVersion = 0,
        };
        return map with { CentralEntrance = CentralEntranceOf(map, island) };
    }

    /// <summary>
    /// 中央入口（E3）：岛的几何中心格（偏西南）；该格不可落子时取离它最近（曼哈顿距离）的可落子低地格（高度 0），并列取先行后列在前的一格。
    /// </summary>
    private static Coord CentralEntranceOf(MapData map, Island island)
    {
        var centre = new Coord(Margin + ((island.Width - 1) / 2), Margin + ((island.Height - 1) / 2));
        if (map.IsPlayable(centre))
        {
            return centre;
        }

        return island.Cells()
            .Where(c => map.IsPlayable(c) && map.HeightAt(c) == 0)
            .OrderBy(c => Math.Abs(c.X - centre.X) + Math.Abs(c.Y - centre.Y))
            .ThenBy(c => c.Y)
            .ThenBy(c => c.X)
            .First();
    }
}
