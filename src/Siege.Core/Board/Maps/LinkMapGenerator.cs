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
/// <para><b>几何</b>（seat-gap-25 E1；map-coastline 裁决 1）：岛的外接矩形为 W×H（<see cref="IslandSize"/>：2 人 45×33，3 / 4 人 45×45），占 [M, M+W)×[M, M+H)，画布 (W+2M)×(H+2M)，
/// 留白 M = <see cref="Margin"/>；岛形是矩形切四角（直角边 <see cref="CornerCutMin"/>–<see cref="CornerCutMax"/>）再沿边咬出海湾的掩码（<c>LinkMapGenerator.Coast.cs</c>），掩码外都是障碍。
/// 平台距矩形边 ≥ <see cref="EdgeGap"/>、两两外接矩形间隔 ≥ <see cref="PlateGap"/>，海湾不进平台的边距 / 间隔与坡道朝外格。</para>
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

    /// <summary>平台外接矩形距岛外接矩形边的最少格数（其间是低地，坡道朝外格与岛缘林带都在这里）；座位恰好贴着这条边距线。map-coastline 裁决 1：座位外这 2 格也是海湾的禁区。</summary>
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

    /// <summary>河道折线（1 格宽的主线）最短格数：去掉两端后至少 4 格，总能放下两座不相邻的桥（D4：每条河道恰 2 座预置桥，见 <see cref="TryLowland"/>）。</summary>
    internal const int MinRiverLength = 6;

    /// <summary>河道主线走满这么多格之前碰到平台崖下不停、绕过去接着走（map-coastline 裁决 3"蜿蜒"：原来一出岸就撞崖停下，河道只有 6–9 格）；碰到岛缘仍按 <see cref="MinRiverLength"/> 停。</summary>
    internal const int RiverReach = 18;

    /// <summary>河道主线每格加宽（在固定一侧补一格深水）的概率：每格抽 NextInt(<see cref="RiverWidenOdds"/>)，非 0 即加宽（map-coastline 裁决 3：河道 1–2 格宽蜿蜒；3 即约三分之二的格加宽）。</summary>
    internal const int RiverWidenOdds = 3;

    /// <summary>一条河道走投无路时在同一条流上重抽这一条的最多次数（map-coastline）；仍不行即重抽整段低地。</summary>
    internal const int MaxRiverRedraws = 6;

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
    /// 一次生成的全部产物（测试与分布统计用，不缓存）：地图、各平台叠加后<b>抬升前</b>的地形（按编号）、全部坡道格、整张布局与低地（最后一次布局里）的重抽次数；
    /// map-coastline：岛形掩码（岛面，先行后列）、四角切角（西南、东南、西北、东北）、海湾咬掉的岛缘格数、湖泊格与岩区格。
    /// </summary>
    internal sealed record IslandGeneration(
        MapData Map,
        ImmutableArray<BoardTerrain> Boards,
        ImmutableHashSet<Coord> Ramps,
        int LayoutRedraws,
        int LowlandRedraws,
        ImmutableArray<Coord> IslandCells,
        ImmutableArray<int> CornerCuts,
        int BayCells,
        ImmutableHashSet<Coord> Lakes,
        ImmutableHashSet<Coord> RockClusters);

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
    /// 任何一步放不下即整张布局在同一条流上重抽（至多 <see cref="MaxLayoutRedraws"/> 次）。布局定下后先用岸线流 <see cref="MapRandom.ForLinkCoast"/> 啃出岛形掩码（map-coastline 裁决 1，
    /// 消费顺序见 <c>LinkMapGenerator.Coast.cs</c>；平台落位按矩形算，所以布局流的消费顺序与取值不因岸线而变）。之后低地：n 条河道（起点 → 朝向 → 逐步前进 → 两座桥，布局流；
    /// 加宽侧与逐格加宽走岸线流）→ 湖泊 / 岩区填到目标占比（岸线流）→ 岛缘林地（逐格）→ 岛缘岩石（布局流），
    /// 全图可落子格沿气边不连通或占比填不到即重抽低地（至多 <see cref="MaxLowlandRedraws"/> 次，仍不行即整张布局重抽）。座位不做镜像 / 旋转（E1 留作可选，未实施），也就不为它消费随机。
    /// 不放静态信物格（D5：依据 relic-redesign R29，2026-10-26 用户裁决；relic-redesign 接线段起地图数据不再有信物格字段），布局流也不为信物消费随机。
    /// 最后（link-map-surfaces D1）用独立子流 <see cref="MapRandom.ForLinkSurfaces"/> 按编号逐块（座位与中立平台一视同仁）叠加新地表（读抬升前的高度，D3），组装时把平台抬到 h=2、坡道 h=1。
    /// 叠加只改平台中间带的草地，不影响前面任何一步的判定，所以 <paramref name="overlaySurfaces"/> 为 <c>false</c> 的对照图与正式图只差新地表。
    /// 成品过一遍链接档静态校验，不通过即生成器缺陷（抛 <see cref="MapGenerationException"/>）。
    /// </summary>
    private static IslandGeneration Layout(ulong mapSeed, int players, bool overlaySurfaces)
    {
        string id = LinkMapId.Format(mapSeed, players);
        (int width, int height) = IslandSize(players);
        Island rect = Island.Rectangle(width, height);
        RandomStream rng = MapRandom.ForLinkLayout(mapSeed, players);
        RandomStream coast = MapRandom.ForLinkCoast(mapSeed, players);
        for (int layoutRedraw = 0; layoutRedraw <= MaxLayoutRedraws; layoutRedraw++)
        {
            if (TryLayout(rect, players, rng) is not { } layout)
            {
                continue;
            }

            Island island = Carve(rect, layout, coast);
            for (int lowlandRedraw = 0; lowlandRedraw <= MaxLowlandRedraws; lowlandRedraw++)
            {
                if (TryLowland(island, players, layout, rng, coast) is not { } lowland)
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

                return new IslandGeneration(map, [.. boards], layout.Ramps, layoutRedraw, lowlandRedraw, island.Cells(), island.CornerCuts, island.BayCells, lowland.Lakes, lowland.RockClusters);
            }
        }

        throw new MapGenerationException($"地图 {id} 的布局重抽 {MaxLayoutRedraws} 次仍未得到合规的浮空岛（生成器缺陷）。");
    }

    /// <summary>
    /// 岛的几何（E1；map-coastline 裁决 1 起按岛形掩码判定）：外接矩形的宽、高与画布只是坐标算术；岛上 / 岛缘按掩码判（<see cref="Rectangle"/> 是啃岸线之前的整块矩形，
    /// 只供平台落位读尺寸；<see cref="FromMask"/> 是 <c>LinkMapGenerator.Coast.cs</c> 啃出的成品）。
    /// </summary>
    private sealed class Island
    {
        private readonly bool[] _mask;
        private readonly ImmutableArray<Coord> _cells;

        private Island(int width, int height, bool[] mask, ImmutableArray<int> cornerCuts, int bayCells)
        {
            Width = width;
            Height = height;
            _mask = mask;
            CornerCuts = cornerCuts;
            BayCells = bayCells;
            var cells = ImmutableArray.CreateBuilder<Coord>();
            for (int y = Margin; y < Margin + Height; y++)
            {
                for (int x = Margin; x < Margin + Width; x++)
                {
                    var c = new Coord(x, y);
                    if (mask[Index(c)])
                    {
                        cells.Add(c);
                    }
                }
            }

            _cells = cells.ToImmutable();
        }

        /// <summary>整块 W×H 矩形（未切角、未咬湾）。</summary>
        internal static Island Rectangle(int width, int height)
        {
            var mask = new bool[width * height];
            Array.Fill(mask, true);
            return new Island(width, height, mask, [], 0);
        }

        /// <summary>按掩码（<see cref="Index"/> 次序）建岛；另记四角切角与海湾咬掉的格数。</summary>
        internal static Island FromMask(int width, int height, bool[] mask, ImmutableArray<int> cornerCuts, int bayCells) => new(width, height, mask, cornerCuts, bayCells);

        internal int Width { get; }

        internal int Height { get; }

        internal int CanvasWidth => Width + (2 * Margin);

        internal int CanvasHeight => Height + (2 * Margin);

        /// <summary>四角切角的直角边（西南、东南、西北、东北）；矩形为空。</summary>
        internal ImmutableArray<int> CornerCuts { get; }

        /// <summary>海湾咬掉的岛缘格数（切角不计）。</summary>
        internal int BayCells { get; }

        /// <summary>岛面：掩码内格数。</summary>
        internal int Count => _cells.Length;

        /// <summary>该格是否在岛的外接矩形 [M, M+W)×[M, M+H) 内。</summary>
        internal bool InRectangle(Coord c) => c.X >= Margin && c.X < Margin + Width && c.Y >= Margin && c.Y < Margin + Height;

        /// <summary>矩形内格的掩码下标（先行后列）。</summary>
        internal int Index(Coord c) => ((c.Y - Margin) * Width) + (c.X - Margin);

        /// <summary>该格是否是岛上的格（在外接矩形内且在掩码里）。</summary>
        internal bool Contains(Coord c) => InRectangle(c) && _mask[Index(c)];

        /// <summary>该格是否在岛缘一圈：岛上、且四邻之一不在岛上（岛外、切角或海湾）。岛上的格离画布边至少 <see cref="Margin"/> 格，四邻坐标不会越界。</summary>
        internal bool OnEdge(Coord c) => Contains(c) && Directions.Any(d => !Contains(Step(c, d, 1)));

        /// <summary>岛上的全部格，先行后列。</summary>
        internal ImmutableArray<Coord> Cells() => _cells;

        /// <summary>只含岛形的预览图（掩码外为障碍、掩码内全是 h=0 草地）：供掩码 4 邻连通自检走 <see cref="Battlefields"/>。</summary>
        internal MapData Preview()
        {
            var obstacles = ImmutableHashSet.CreateBuilder<Coord>();
            for (int y = 0; y < CanvasHeight; y++)
            {
                for (int x = 0; x < CanvasWidth; x++)
                {
                    var c = new Coord(x, y);
                    if (!Contains(c))
                    {
                        obstacles.Add(c);
                    }
                }
            }

            return new MapData
            {
                Id = "link-coast-preview",
                Width = CanvasWidth,
                Height = CanvasHeight,
                MaxPlayers = 4,
                Profile = MapProfile.Link,
                Obstacles = obstacles.ToImmutable(),
                BirthZones = [],
                ChokePoints = [],
                CentralEntrance = new Coord(0, 0),
            };
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

    /// <summary>低地一侧的结果：河道深水格（含加宽）、河道上的预置桥、岛缘林地与零星岩石；map-coastline：湖泊（未架桥深水团）与岩区（岩石团）。</summary>
    private sealed record Lowland(
        ImmutableHashSet<Coord> Water,
        ImmutableHashSet<Coord> Bridges,
        ImmutableHashSet<Coord> Forest,
        ImmutableHashSet<Coord> Rocks,
        ImmutableHashSet<Coord> Lakes,
        ImmutableHashSet<Coord> RockClusters);

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
    /// 一次低地尝试（D4；map-coastline 裁决 2 / 3）：n 条河道（各 2 座预置桥，再按岸线流加宽到 1–2 格）→ 湖泊 / 岩区填到目标占比（岸线流，见 <see cref="PlaceClusters"/>）
    /// → 岛缘林地 → 岛缘岩石，然后全图可落子格沿气边须连通。不满足返回 <c>null</c>（调用方在同一条流上重抽低地）。
    /// </summary>
    private static Lowland? TryLowland(Island island, int players, PlateLayout layout, RandomStream rng, RandomStream coast)
    {
        var water = new HashSet<Coord>();
        var bridges = new HashSet<Coord>();
        for (int k = 0; k < players; k++)
        {
            // map-coastline：一条河道画完（主线 → 两座桥 → 加宽）先单独做一次全图气边连通自检——出岸又回岸、或者绕着平台兜圈的河道会把一片低地围成两座桥都够不着的口袋，
            // 这种情况在同一条流上只重抽这一条（至多 MaxRiverRedraws 次），不急着把前面画好的河道一起作废；仍不行才重抽整段低地。
            bool placed = false;
            for (int redraw = 0; !placed && redraw <= MaxRiverRedraws; redraw++)
            {
                if (DrawRiver(island, layout, water, rng) is not { } river)
                {
                    continue;
                }

                // 两座桥：去掉两端的河道格里均匀抽一座，再从与它不相邻的格里抽一座（折线 1 格宽、去掉两端 ≥ 4 格，总有这样的格）。
                // map-coastline：优先抽主线上直行且两岸都是低地的格（前后两格共线、垂直方向两格都在岛上、不是平台也不是水）——拐角格的两个岸邻格在同一侧，
                // 贴崖 / 贴岸的格一侧没有岸，这样的桥连不通两岸；凑不出不相邻的两座才退回全部内格。
                Coord[] inner = [.. river.Skip(1).Take(river.Count - 2)];
                bool Bank(Coord c) => island.Contains(c) && !layout.InPlate(c) && !water.Contains(c);
                Coord[] straight = [.. Enumerable.Range(1, river.Count - 2)
                    .Where(i => river[i - 1].X == river[i + 1].X || river[i - 1].Y == river[i + 1].Y)
                    .Where(i => river[i - 1].X == river[i + 1].X ? Bank(new Coord(river[i].X - 1, river[i].Y)) && Bank(new Coord(river[i].X + 1, river[i].Y)) : Bank(new Coord(river[i].X, river[i].Y - 1)) && Bank(new Coord(river[i].X, river[i].Y + 1)))
                    .Select(i => river[i])];
                Coord[] pool = straight.Any(a => straight.Any(b => b != a && !Adjacency.AreAdjacent(a, b))) ? straight : inner;
                Coord first = pool[rng.NextInt(pool.Length)];
                Coord[] apart = [.. pool.Where(c => c != first && !Adjacency.AreAdjacent(c, first))];
                if (apart.Length == 0)
                {
                    apart = [.. inner.Where(c => c != first && !Adjacency.AreAdjacent(c, first))];
                }

                Coord second = apart[rng.NextInt(apart.Length)];
                HashSet<Coord> widened = WidenRiver(island, layout, water, river, [first, second], coast);
                HashSet<Coord> trialWater = [.. water, .. river, .. widened];
                HashSet<Coord> trialBridges = [.. bridges, first, second];
                MapData trial = Assemble("link-island-preview", players, island, layout, layout.Boards, new Lowland([.. trialWater], [.. trialBridges], [], [], [], []));
                if (!MapValidator.LibertySplit(trial).IsEmpty)
                {
                    continue;
                }

                water = trialWater;
                bridges = trialBridges;
                placed = true;
            }

            if (!placed)
            {
                return null;
            }
        }

        // 平台自带的非可落子格（岩石、未架桥深水）计入占比；湖泊 / 岩区按岸线流填到目标占比，填不到即重抽低地。
        int plateNonPlayable = layout.Boards.Sum(b => b.Rocks.Count + b.Surfaces.Count(kv => kv.Value == Surface.DeepWater && !b.Bridges.Contains(kv.Key)));
        var lakes = new HashSet<Coord>();
        var rockClusters = new HashSet<Coord>();
        if (!PlaceClusters(island, layout, water, bridges, lakes, rockClusters, plateNonPlayable, coast))
        {
            return null;
        }

        // 岛缘点缀（参考图的林带）：岛缘一圈的低地格逐格 1/4 概率为林地；再放 2 – (n+1) 格零星岩石（障碍）。都不占河道与坡道朝外格；
        // 零星岩石不挨着桥（桥的两岸保持可走）也不挨着岩区（岩区按 3–6 格一团计，不与零星岩石粘连），并与湖泊 / 岩区同样要求四周陆地成环（海湾之间的窄颈上不放，见 RingConnected）。
        var rocks = new HashSet<Coord>();
        bool Free(Coord c) => island.OnEdge(c) && !water.Contains(c) && !layout.RampOutlets.Contains(c);
        bool Land(Coord c) => island.Contains(c) && !layout.InPlate(c) && !lakes.Contains(c) && !rockClusters.Contains(c) && !rocks.Contains(c) && (!water.Contains(c) || bridges.Contains(c));
        var forest = new HashSet<Coord>();
        foreach (Coord c in island.Cells().Where(Free))
        {
            if (rng.NextInt(4) == 0)
            {
                forest.Add(c);
            }
        }

        int rockCount = 2 + rng.NextInt(players);
        for (int k = 0; k < rockCount; k++)
        {
            List<Coord> spots = [.. island.Cells().Where(c => Free(c) && !forest.Contains(c) && !rocks.Contains(c)
                && !bridges.Any(b => Adjacency.AreAdjacent(b, c)) && !rockClusters.Any(r => Adjacency.AreAdjacent(r, c)) && RingConnected([c], Land))];
            if (spots.Count == 0)
            {
                break;
            }

            rocks.Add(spots[rng.NextInt(spots.Count)]);
        }

        var lowland = new Lowland([.. water], [.. bridges], [.. forest], [.. rocks], [.. lakes], [.. rockClusters]);
        MapData preview = Assemble("link-island-preview", players, island, layout, layout.Boards, lowland);
        return MapValidator.LibertySplit(preview).IsEmpty ? lowland : null;
    }

    /// <summary>
    /// 河道加宽（map-coastline 裁决 3）：抽一侧（左 / 右，相对前进方向），主线去掉两端后逐格按 1/<see cref="RiverWidenOdds"/> 在该侧补一格深水。
    /// 补的格须是低地（岛上、非岛缘、非平台、非坡道朝外格）、不是水、不在崖下（岸边与崖下那条低地留给绕行的路），不与桥格相邻（桥的两岸保持可走），不与别的河道相邻（河道两两不接），
    /// 除本格的主线格与上一格的补格外不与本河道任何格相邻（宽度恒 ≤ 2、不回绕成环）。桥格本身不加宽。
    /// </summary>
    private static HashSet<Coord> WidenRiver(Island island, PlateLayout layout, HashSet<Coord> others, List<Coord> river, Coord[] bridges, RandomStream coast)
    {
        bool left = coast.NextInt(2) == 0;
        var sides = new HashSet<Coord>();
        Coord? previous = null;
        for (int i = 1; i < river.Count - 1; i++)
        {
            Coord here = river[i];
            int heading = Directions.IndexOf((here.X - river[i - 1].X, here.Y - river[i - 1].Y));
            Coord side = Step(here, Directions[left ? Left(heading) : Right(heading)], 1);
            bool roll = coast.NextInt(RiverWidenOdds) != 0;
            if (!roll || bridges.Contains(here)
                || !island.Contains(side) || island.OnEdge(side) || layout.InPlate(side) || layout.RampOutlets.Contains(side) || others.Contains(side) || river.Contains(side) || sides.Contains(side)
                || layout.Plates.Any(p => UnderCliff(p, side))
                || bridges.Any(b => Adjacency.AreAdjacent(b, side))
                || others.Any(w => Adjacency.AreAdjacent(w, side))
                || river.Any(r => r != here && Adjacency.AreAdjacent(r, side))
                || sides.Any(s => s != previous && Adjacency.AreAdjacent(s, side)))
            {
                previous = null;
                continue;
            }

            sides.Add(side);
            previous = side;
        }

        return sides;
    }

    /// <summary>
    /// 一条河道（D4）：1 格宽的深水折线，从岛缘一格出发朝岛内走，每步直行（4/6）或左右转（各 1/6；走不通依次换直行 / 另两个方向），
    /// 走满 <see cref="MinRiverLength"/> 格后碰到岛缘即停，走满 <see cref="RiverReach"/> 格后碰到某平台崖下也停（map-coastline：不足即绕过崖下接着走）。
    /// 不进平台、海湾 / 切角与坡道朝外格，不挨着自己走过的格（保持 1 格宽），也不挨着别的河道。走投无路时已走满 <see cref="MinRiverLength"/> 格就到此为止，否则返回 <c>null</c>。
    /// </summary>
    private static List<Coord>? DrawRiver(Island island, PlateLayout layout, HashSet<Coord> water, RandomStream rng)
    {
        bool Open(Coord c) => island.Contains(c) && !layout.InPlate(c) && !layout.RampOutlets.Contains(c) && !water.Contains(c)
            && !water.Any(w => Adjacency.AreAdjacent(w, c));
        bool Cliff(Coord c) => layout.Plates.Any(p => UnderCliff(p, c));
        List<Coord> starts = [.. island.Cells().Where(c => island.OnEdge(c) && Open(c) && !Cliff(c))];
        if (starts.Count == 0)
        {
            return null;
        }

        Coord start = starts[rng.NextInt(starts.Count)];
        List<int> inward = [.. Enumerable.Range(0, Directions.Length).Where(d => !island.Contains(Step(start, Directions[d], -1)) && Open(Step(start, Directions[d], 1)) && !island.OnEdge(Step(start, Directions[d], 1)) && !Cliff(Step(start, Directions[d], 1)))];
        if (inward.Count == 0)
        {
            return null;
        }

        int heading = inward[rng.NextInt(inward.Count)];
        List<Coord> path = [start];
        for (int step = 0; step < 2 * (island.Width + island.Height); step++)
        {
            Coord here = path[^1];
            // map-coastline：出岸之后不再贴岸走（走满 MinRiverLength 后碰岸即停），走满 RiverReach 之前不贴崖走（崖下与岸边各留一条低地，河道不把平台与岸之间、
            // 两个海湾之间的窄道堵死——贴岸 / 贴崖的河道会把夹在中间的格围成无桥可达的口袋）；走满之后碰崖即停。
            bool Next(Coord c) => Open(c) && !path.Contains(c) && !path.Take(path.Count - 1).Any(p => Adjacency.AreAdjacent(p, c))
                && (path.Count + 1 >= MinRiverLength || !island.OnEdge(c)) && (path.Count + 1 >= RiverReach || !Cliff(c));
            int roll = rng.NextInt(6);
            int[] order = roll < 4 ? [heading, Left(heading), Right(heading)] : roll == 4 ? [Left(heading), heading, Right(heading)] : [Right(heading), heading, Left(heading)];
            int chosen = Array.FindIndex(order, d => Next(Step(here, Directions[d], 1)));
            if (chosen < 0)
            {
                return path.Count >= MinRiverLength ? path : null;
            }

            heading = order[chosen];
            Coord next = Step(here, Directions[heading], 1);
            path.Add(next);
            if ((path.Count >= MinRiverLength && island.OnEdge(next)) || (path.Count >= RiverReach && Cliff(next)))
            {
                return path;
            }
        }

        return path;
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
        ImmutableHashSet<Coord> rocks = [.. boards.SelectMany(b => b.Rocks), .. lowland.Rocks, .. lowland.RockClusters];
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
                .Concat(lowland.Lakes.Select(c => KeyValuePair.Create(c, Surface.DeepWater)))
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
