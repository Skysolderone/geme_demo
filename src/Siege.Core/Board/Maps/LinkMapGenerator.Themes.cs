using System.Collections.Immutable;
using Siege.Core.Determinism;

namespace Siege.Core.Board.Maps;

/// <summary>
/// 链接档平台的主题地形（link-map-terrain D3 / D4，裁决 M1–M8、Q1–Q4；map-expand-25 D3：四个主题，高台随 R3 去掉）：每个主题一个生成函数，输入（平台外接矩形、核心区、随机流），
/// 输出该平台<b>抬升前</b>的地形（外圈与核心区高度 0，抬升与坡道由 <c>LinkMapGenerator.cs</c> 统一做）；生成后立即做平台级自检，不通过就在同一条随机流上重抽该块，至多 <see cref="MaxBoardRedraws"/> 次。
/// 全部随机只来自调用方传入的布局流（<c>MapRandom.ForLinkLayout</c>），整数运算，不读时钟与环境。
/// </summary>
/// <remarks>
/// <para><b>分圈</b>（<see cref="BoardPlate.OnRim"/> / <see cref="BoardPlate.InCore"/> / <see cref="BoardPlate.InMiddleBand"/>）：外圈只放林地（裁决 Q1），核心区什么都不放，其余地形只在中间带；
/// 例外是河流的隔断线（terrain-barriers 裁决 H1–H4、实施修订 J1–J3）：取中间带的一整列 / 一整行，两端各占外圈 1 格。
/// 栅栏只立在两端都在中间带、或一端中间带一端核心区的边上（Q4）。</para>
/// <para><b>按面积缩放</b>（D4 末段）：每个计数区间 [下限, 上限] 的实际上限按中间带面积在 7 格（6×6）到 16 格（7×7）之间线性插值、向下取整，
/// 再在 [下限, 插值上限] 里均匀抽取——6×6 恒取下限，7×7 及更大（map-expand-25 起平台边长 6–8，8 边长按 7×7 封顶）取得到上限。</para>
/// <para><b>邻接</b>：只用 <see cref="Adjacency.AreAdjacent"/> 判两格是否几何相邻、用 <see cref="Adjacency.LibertyNeighbors"/> 走气边，不自己枚举四邻；
/// 棋盘内连通性读 <see cref="Battlefields"/>（几何四邻分量的唯一实现）。</para>
/// </remarks>
public static partial class LinkMapGenerator
{
    /// <summary>棋盘级自检失败时在同一条随机流上重抽该块的最多次数（D3）；仍不通过即 <see cref="MapGenerationException"/>。</summary>
    public const int MaxBoardRedraws = 16;

    /// <summary>可改造目标（林地格 + 未架桥深水格）的下限（裁决 M7），生成器自检与校验器同一口径。</summary>
    internal const int MinEditTargets = 2;

    /// <summary>湖泊 / 岩区一团的格数区间（terrain-barriers 裁决 H5）；中间带 ≤ <see cref="SmallMiddleBand"/> 格的小棋盘不放（实施修订 J4）。</summary>
    internal const int MinCluster = 3;
    internal const int MaxCluster = 5;

    /// <summary>面积缩放的两端边长：6×6 取下限、7×7 起取上限（terrain-density 的口径，map-expand-25 平台边长放到 8 后不变）。</summary>
    private const int ScaleMinSide = 6;
    private const int ScaleMaxSide = 7;

    /// <summary>中间带面积的两端：6×6 与 7×7（边长两端各去外圈与核心区）。</summary>
    private const int MinMiddleArea = ((ScaleMinSide - 2) * (ScaleMinSide - 2)) - (BoardPlate.CoreSide * BoardPlate.CoreSide);
    private const int MaxMiddleArea = ((ScaleMaxSide - 2) * (ScaleMaxSide - 2)) - (BoardPlate.CoreSide * BoardPlate.CoreSide);

    /// <summary>
    /// 一块平台生成好的抬升前地形（坐标为全图坐标）：高度、地表、预置桥、栅栏、岩石，
    /// 湖泊 / 岩区一团的格（terrain-barriers D3，按坐标升序；湖泊格也在 <see cref="Surfaces"/> 里、岩区格也在 <see cref="Rocks"/> 里），这块平台的重抽次数，
    /// 以及新地表叠加（link-map-surfaces D3，<c>LinkMapGenerator.Surfaces.cs</c>）实际放下的格数——<see cref="DraftBoard"/> 的产物恒为 0，叠加之后才写入。
    /// </summary>
    internal sealed record BoardTerrain(
        BoardPlate Plate,
        ImmutableDictionary<Coord, int> Heights,
        ImmutableDictionary<Coord, Surface> Surfaces,
        ImmutableHashSet<Coord> Bridges,
        ImmutableHashSet<FenceEdge> Fences,
        ImmutableHashSet<Coord> Rocks,
        ImmutableArray<Coord> Cluster,
        int Redraws,
        int SpecialSurfaces = 0)
    {
        /// <summary>只含这一块棋盘的预览地图（画布 = 棋盘东 / 北边界外各留 <see cref="Margin"/> 格，棋盘之外与岩石为障碍）。自检与单元测试共用。</summary>
        internal MapData Preview() => PreviewOf(Plate, Heights, Surfaces, Bridges, Fences, Rocks);
    }

    /// <summary>
    /// 生成一块平台的主题地形（D3 / D4）：按 <paramref name="plate"/> 的主题调用对应的生成函数，自检不通过即在同一条流上重抽，至多 <see cref="MaxBoardRedraws"/> 次。
    /// map-expand-25 D3：平台互不相邻，没有接缝自检；高台主题不再生成（抛 <see cref="ArgumentOutOfRangeException"/>）。
    /// </summary>
    internal static BoardTerrain DraftBoard(BoardPlate plate, RandomStream rng)
    {
        BoardTheme theme = plate.Theme ?? throw new ArgumentException($"平台 {plate.Origin.ToNotation()} 没有主题，无法生成地形。", nameof(plate));
        for (int redraw = 0; redraw <= MaxBoardRedraws; redraw++)
        {
            var draft = new Draft(plate, rng);
            switch (theme)
            {
                case BoardTheme.River:
                    draft.River();
                    break;
                case BoardTheme.Forest:
                    draft.Forest();
                    break;
                case BoardTheme.Palisade:
                    draft.Palisade();
                    break;
                case BoardTheme.Mixed:
                    draft.Mixed();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(plate), theme, "平台主题只能是河流、林地、栅栏阵或混合（map-expand-25 R3：高台主题已去掉）。");
            }

            draft.ScatterRocks();
            draft.PlaceCluster();
            if (draft.Finish(redraw) is { } done)
            {
                return done;
            }
        }

        throw new MapGenerationException(
            $"平台 {plate.Origin.ToNotation()}（{plate.Width}×{plate.Height}，主题{theme.DisplayName()}）重抽 {MaxBoardRedraws} 次仍未通过平台级自检（生成器缺陷）。");
    }

    private static MapData PreviewOf(
        BoardPlate plate,
        ImmutableDictionary<Coord, int> heights,
        ImmutableDictionary<Coord, Surface> surfaces,
        ImmutableHashSet<Coord> bridges,
        ImmutableHashSet<FenceEdge> fences,
        ImmutableHashSet<Coord> rocks)
    {
        int width = plate.Origin.X + plate.Width + Margin;
        int height = plate.Origin.Y + plate.Height + Margin;
        var obstacles = ImmutableHashSet.CreateBuilder<Coord>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var c = new Coord(x, y);
                if (!plate.Contains(c) || rocks.Contains(c))
                {
                    obstacles.Add(c);
                }
            }
        }

        return new MapData
        {
            Id = "link-board-preview",
            Width = width,
            Height = height,
            MaxPlayers = 4,
            Profile = MapProfile.Link,
            Obstacles = obstacles.ToImmutable(),
            TerrainData = new TerrainData(heights, surfaces, bridges, fences),
            BirthZones = [[.. plate.CoreCells()]],
            Boards = [plate],
            RelicCells = ImmutableDictionary<Coord, RelicCellSpec>.Empty,
            ChokePoints = [],
            CentralEntrance = plate.CoreOrigin,
        };
    }

    /// <summary>一次尝试的可变草稿。</summary>
    private sealed class Draft
    {
        private readonly RandomStream _rng;
        private readonly Dictionary<Coord, int> _heights = [];
        private readonly Dictionary<Coord, Surface> _surfaces = [];
        private readonly HashSet<Coord> _bridges = [];
        private readonly HashSet<FenceEdge> _fences = [];
        private readonly HashSet<Coord> _rocks = [];

        /// <summary>主题要素是否都放下了（数量达到下限）；放不下即本次尝试失败、重抽。</summary>
        private bool _ok = true;

        /// <summary>湖泊 / 岩区一团的格（terrain-barriers D3）。</summary>
        private readonly List<Coord> _cluster = [];

        internal Draft(BoardPlate plate, RandomStream rng)
        {
            Plate = plate;
            _rng = rng;
            Middle = [.. plate.Cells().Where(plate.InMiddleBand)];
            Rim = [.. plate.Cells().Where(plate.OnRim)];
        }

        private BoardPlate Plate { get; }

        /// <summary>中间带格，先行后列。</summary>
        private List<Coord> Middle { get; }

        /// <summary>外圈格，先行后列。</summary>
        private List<Coord> Rim { get; }

        /// <summary>该格还没有放任何地形（高度 0、草地、不是岩石）。</summary>
        private bool Free(Coord c) => !_heights.ContainsKey(c) && !_surfaces.ContainsKey(c) && !_rocks.Contains(c);

        private bool NextToForest(Coord c) => _surfaces.Any(kv => kv.Value == Surface.Forest && (kv.Key == c || Adjacency.AreAdjacent(kv.Key, c)));

        /// <summary>计数区间的上限按中间带面积缩放后，在 [下限, 缩放上限] 里均匀抽一个。</summary>
        private int Draw(int min, int max)
        {
            int area = Math.Clamp(Middle.Count, MinMiddleArea, MaxMiddleArea);
            int top = min + ((max - min) * (area - MinMiddleArea) / (MaxMiddleArea - MinMiddleArea));
            return min + _rng.NextInt(top - min + 1);
        }

        private T Pick<T>(IReadOnlyList<T> list) => list[_rng.NextInt(list.Count)];

        // ---------- 四个主题 ----------

        /// <summary>
        /// 河流（terrain-barriers D1，实施修订 J1 / J2）：抽一条隔断线（<see cref="DrawBarrier"/>），线上全部格为深水，再抽 1 格预置桥。改造目标 = 未架桥的深水格。
        /// </summary>
        internal void River()
        {
            List<Coord> line = DrawBarrier();
            foreach (Coord c in line)
            {
                _surfaces[c] = Surface.DeepWater;
            }

            _bridges.Add(Pick(line));
        }

        /// <summary>林地：2–3 团林地，每团 2–4 格，团与团互不相邻；从中间带起团，可以延伸到外圈（Q1）。</summary>
        internal void Forest()
        {
            int clumps = Draw(2, 3);
            int placed = 0;
            for (int k = 0; k < clumps; k++)
            {
                if (GrowClump(Draw(2, 4)) >= 2)
                {
                    placed++;
                }
            }

            _ok &= placed >= 2;
        }

        /// <summary>
        /// 栅栏阵：2–3 段预置栅栏，每段 2–3 条共线相连的边，只立在中间带内部或中间带与核心区之间（Q4），段与段不共线相接；外圈散布 2 格林地作改造目标。
        /// </summary>
        internal void Palisade()
        {
            int segments = Draw(2, 3);
            int placed = 0;
            for (int k = 0; k < segments; k++)
            {
                if (PlaceFence(Draw(2, 3)))
                {
                    placed++;
                }
            }

            _ok &= placed >= 2;
            List<Coord> rim = [.. Rim];
            for (int k = 0; k < 2; k++)
            {
                Coord c = Pick(rim);
                rim.Remove(c);
                _surfaces[c] = Surface.Forest;
            }
        }

        /// <summary>
        /// 混合：1 格高度 1 的台、1 段 2 格的深水（其中 1 格预置桥）、1 团林地（2–3 格）、1 段栅栏（2–3 条边）。改造目标 = 林地 + 未架桥深水。
        /// 台格在平台抬升时被抹平（map-expand-25 D3，可接受），这里仍抽它：它决定新地表叠加的岩台候选。
        /// </summary>
        internal void Mixed()
        {
            Coord hill = Pick(Middle);
            _heights[hill] = 1;

            List<(Coord A, Coord B)> pairs = [.. Middle.Where(Free).SelectMany(a => Middle.Where(b => a < b && Free(b) && Adjacency.AreAdjacent(a, b)).Select(b => (a, b)))];
            if (pairs.Count == 0)
            {
                _ok = false;
            }
            else
            {
                (Coord a, Coord b) = Pick(pairs);
                _surfaces[a] = Surface.DeepWater;
                _surfaces[b] = Surface.DeepWater;
                bool first = _rng.NextInt(2) == 0;
                _bridges.Add(first ? a : b);
            }

            _ok &= GrowClump(Draw(2, 3)) >= 2;
            _ok &= PlaceFence(Draw(2, 3));
        }

        /// <summary>岩石：每块 0–2 格（按面积缩放，6×6 / 6×7 取 0、7×7 取 0–2），只放在中间带的空格上。</summary>
        internal void ScatterRocks()
        {
            int rocks = Draw(0, 2);
            for (int k = 0; k < rocks; k++)
            {
                List<Coord> free = [.. Middle.Where(Free)];
                if (free.Count == 0)
                {
                    return;
                }

                _rocks.Add(Pick(free));
            }
        }

        // ---------- 要素 ----------

        /// <summary>从一个中间带空格起长一团林地，至多 <paramref name="size"/> 格，可以长进外圈；不与已有林地相邻。返回实际格数（0 = 没有起点）。</summary>
        private int GrowClump(int size)
        {
            List<Coord> seeds = [.. Middle.Where(c => Free(c) && !NextToForest(c))];
            if (seeds.Count == 0)
            {
                return 0;
            }

            List<Coord> clump = [Pick(seeds)];
            while (clump.Count < size)
            {
                List<Coord> grow = [.. Plate.Cells().Where(c => !Plate.InCore(c) && Free(c) && !clump.Contains(c) && !NextToForest(c) && clump.Any(t => Adjacency.AreAdjacent(t, c)))];
                if (grow.Count == 0)
                {
                    break;
                }

                clump.Add(Pick(grow));
            }

            foreach (Coord c in clump)
            {
                _surfaces[c] = Surface.Forest;
            }

            return clump.Count;
        }

        /// <summary>立一段 <paramref name="length"/> 条共线相连的栅栏：每条边两端都不在外圈、且不同时在核心区；与已有栅栏不重叠、不共线相接。放不下返回 <c>false</c>。</summary>
        private bool PlaceFence(int length)
        {
            List<FenceEdge[]> candidates = [.. FenceRuns(length).Where(run => !run.Any(e => _fences.Contains(e)) && !run.Any(TouchesFence))];
            if (candidates.Count == 0)
            {
                return false;
            }

            _fences.UnionWith(Pick(candidates));
            return true;
        }

        /// <summary>该边是否与已有栅栏共线相接（同一条墙线上紧挨着）。</summary>
        private bool TouchesFence(FenceEdge e)
        {
            bool vertical = e.A.Y == e.B.Y;   // 东西相邻两格之间的边 = 竖墙
            foreach (FenceEdge f in _fences)
            {
                bool sameLine = vertical
                    ? f.A.Y == f.B.Y && f.A.X == e.A.X && Math.Abs(f.A.Y - e.A.Y) <= 1
                    : f.A.X == f.B.X && f.A.Y == e.A.Y && Math.Abs(f.A.X - e.A.X) <= 1;
                if (sameLine)
                {
                    return true;
                }
            }

            return false;
        }

        private bool FenceAllowed(Coord a, Coord b) => !Plate.OnRim(a) && !Plate.OnRim(b) && !(Plate.InCore(a) && Plate.InCore(b));

        /// <summary>全部合规的一段栅栏（先竖墙后横墙、按墙线与起点升序）。</summary>
        private IEnumerable<FenceEdge[]> FenceRuns(int length)
        {
            int ox = Plate.Origin.X, oy = Plate.Origin.Y, w = Plate.Width, h = Plate.Height;
            for (int x = ox; x < ox + w - 1; x++)
            {
                for (int y0 = oy; y0 + length <= oy + h; y0++)
                {
                    FenceEdge[] run = [.. Enumerable.Range(y0, length).Select(y => new FenceEdge(new Coord(x, y), new Coord(x + 1, y)))];
                    if (run.All(e => FenceAllowed(e.A, e.B)))
                    {
                        yield return run;
                    }
                }
            }

            for (int y = oy; y < oy + h - 1; y++)
            {
                for (int x0 = ox; x0 + length <= ox + w; x0++)
                {
                    FenceEdge[] run = [.. Enumerable.Range(x0, length).Select(x => new FenceEdge(new Coord(x, y), new Coord(x, y + 1)))];
                    if (run.All(e => FenceAllowed(e.A, e.B)))
                    {
                        yield return run;
                    }
                }
            }
        }

        /// <summary>
        /// 隔断线候选（terrain-barriers D1，实施修订 J1 / J2）：中间带的整列（先，自西向东）与整行（后，自南向北）——不是外圈那一列 / 行，也不穿过核心区（可以紧贴核心区）。
        /// 边长 6 只有相对第 4 列 / 行一条，边长 7 有第 1、5 两条，边长 8 有第 1、5、6 三条。
        /// </summary>
        private List<(bool Column, int Index)> BarrierLines()
        {
            Coord core = Plate.CoreOrigin;
            var lines = new List<(bool, int)>();
            for (int x = Plate.Origin.X + 1; x < Plate.Origin.X + Plate.Width - 1; x++)
            {
                if (x < core.X || x >= core.X + BoardPlate.CoreSide)
                {
                    lines.Add((true, x));
                }
            }

            for (int y = Plate.Origin.Y + 1; y < Plate.Origin.Y + Plate.Height - 1; y++)
            {
                if (y < core.Y || y >= core.Y + BoardPlate.CoreSide)
                {
                    lines.Add((false, y));
                }
            }

            return lines;
        }

        /// <summary>
        /// 抽一条隔断线（D1 / D4：方向与候选合在一起在 <see cref="BarrierLines"/> 里均匀抽 1 次），返回线上的格（横穿整块平台、两端各占外圈 1 格），先行后列。
        /// </summary>
        private List<Coord> DrawBarrier()
        {
            (bool column, int index) = Pick(BarrierLines());
            return [.. Plate.Cells().Where(c => (column ? c.X : c.Y) == index)];
        }

        /// <summary>
        /// 湖泊 / 岩区（terrain-barriers D3，裁决 H5）：主题地形与散布岩石之后，在中间带剩余的空格里放 1 团：先抽种类（湖泊 = 未架桥深水 / 岩区 = 岩石），
        /// 再抽目标格数 3–5（不按面积缩放），从一格空的中间带格起沿几何四邻在空的中间带格里生长；长不到 3 格即本次尝试失败、重抽该块。
        /// 中间带 ≤ <see cref="SmallMiddleBand"/> 格的小棋盘（6×6 / 6×7 / 7×6）不放、也不消费随机（实施修订 J4）。
        /// </summary>
        internal void PlaceCluster()
        {
            if (Middle.Count <= SmallMiddleBand)
            {
                return;
            }

            bool lake = _rng.NextInt(2) == 0;
            int size = MinCluster + _rng.NextInt(MaxCluster - MinCluster + 1);
            List<Coord> seeds = [.. Middle.Where(Free)];
            if (seeds.Count == 0)
            {
                _ok = false;
                return;
            }

            _cluster.Add(Pick(seeds));
            while (_cluster.Count < size)
            {
                List<Coord> grow = [.. Middle.Where(c => Free(c) && !_cluster.Contains(c) && _cluster.Any(t => Adjacency.AreAdjacent(t, c)))];
                if (grow.Count == 0)
                {
                    break;
                }

                _cluster.Add(Pick(grow));
            }

            _ok &= _cluster.Count >= MinCluster;
            foreach (Coord c in _cluster)
            {
                if (lake)
                {
                    _surfaces[c] = Surface.DeepWater;
                }
                else
                {
                    _rocks.Add(c);
                }
            }
        }

        // ---------- 自检 ----------

        /// <summary>
        /// 平台级自检（D3）：主题要素都放下了；平台内可落子格沿几何四邻连通（只有一个战场分量）；可改造目标 ≥ <see cref="MinEditTargets"/>；
        /// 平台内每个可落子格都能从核心区沿气边走到（map-expand-25：原只对有隔断线的河流 / 高台查，现对全部主题查——平台四周是崖壁，
        /// 被栅栏或深水关在平台里的格再也绕不出去，整岛"可落子格沿气边连通"要求平台内先连通）。通过返回成品，否则 <c>null</c>（调用方重抽）。
        /// </summary>
        internal BoardTerrain? Finish(int redraws)
        {
            if (!_ok)
            {
                return null;
            }

            ImmutableDictionary<Coord, int> heights = _heights.ToImmutableDictionary();
            ImmutableDictionary<Coord, Surface> surfaces = _surfaces.ToImmutableDictionary();
            ImmutableHashSet<Coord> bridges = [.. _bridges];
            ImmutableHashSet<FenceEdge> fences = [.. _fences];
            ImmutableHashSet<Coord> rocks = [.. _rocks];
            MapData preview = PreviewOf(Plate, heights, surfaces, bridges, fences, rocks);
            if (Battlefields.Of(preview).Count != 1)
            {
                return null;
            }

            int targets = Plate.Cells().Count(c => preview.SurfaceAt(c) == Surface.Forest || preview.TerrainData.IsUnbridgedDeepWater(c));
            if (targets < MinEditTargets)
            {
                return null;
            }

            var reached = new HashSet<Coord>(Plate.CoreCells());
            var queue = new Queue<Coord>(reached.Order());
            while (queue.Count > 0)
            {
                foreach (Coord n in Adjacency.LibertyNeighbors(preview, queue.Dequeue()))
                {
                    if (Plate.Contains(n) && reached.Add(n))
                    {
                        queue.Enqueue(n);
                    }
                }
            }

            if (Plate.Cells().Any(c => preview.IsPlayable(c) && !reached.Contains(c)))
            {
                return null;
            }

            return new BoardTerrain(Plate, heights, surfaces, bridges, fences, rocks, [.. _cluster.Order()], redraws);
        }
    }
}
