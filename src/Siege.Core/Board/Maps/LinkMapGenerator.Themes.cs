using System.Collections.Immutable;
using Siege.Core.Determinism;

namespace Siege.Core.Board.Maps;

/// <summary>
/// 链接档出生棋盘的主题地形（link-map-terrain D3 / D4，裁决 M1–M8、Q1–Q4）：五个主题各一个生成函数，输入（棋盘外接矩形、核心区、随机流），
/// 输出该棋盘的地形与"难到达格"候选；生成后立即做棋盘级自检，不通过就在同一条随机流上重抽该块，至多 <see cref="MaxBoardRedraws"/> 次。
/// 全部随机只来自调用方传入的布局流（<c>MapRandom.ForLinkLayout</c>），整数运算，不读时钟与环境。
/// </summary>
/// <remarks>
/// <para><b>分圈</b>（<see cref="BoardPlate.OnRim"/> / <see cref="BoardPlate.InCore"/> / <see cref="BoardPlate.InMiddleBand"/>）：外圈只放林地（裁决 Q1），核心区什么都不放，其余地形只在中间带；
/// 栅栏只立在两端都在中间带、或一端中间带一端核心区的边上（Q4）。</para>
/// <para><b>按面积缩放</b>（D4 末段）：每个计数区间 [下限, 上限] 的实际上限按中间带面积在 7 格（6×6）到 27 格（8×8）之间线性插值、向下取整，
/// 再在 [下限, 插值上限] 里均匀抽取——6×6 恒取下限，8×8 才能取到上限。</para>
/// <para><b>邻接</b>：只用 <see cref="Adjacency.AreAdjacent"/> 判两格是否几何相邻、用 <see cref="Adjacency.LibertyNeighbors"/> 走气边，不自己枚举四邻；
/// 棋盘内连通性读 <see cref="Battlefields"/>（几何四邻分量的唯一实现）。</para>
/// </remarks>
public static partial class LinkMapGenerator
{
    /// <summary>棋盘级自检失败时在同一条随机流上重抽该块的最多次数（D3）；仍不通过即 <see cref="MapGenerationException"/>。</summary>
    public const int MaxBoardRedraws = 16;

    /// <summary>可改造目标（林地格 + 未架桥深水格）的下限（裁决 M7），生成器自检与校验器同一口径。</summary>
    internal const int MinEditTargets = 2;

    /// <summary>中间带面积的两端：6×6 与 8×8（边长区间两端各去外圈与核心区）。</summary>
    private const int MinMiddleArea = ((BirthMinSide - 2) * (BirthMinSide - 2)) - (BoardPlate.CoreSide * BoardPlate.CoreSide);
    private const int MaxMiddleArea = ((BirthMaxSide - 2) * (BirthMaxSide - 2)) - (BoardPlate.CoreSide * BoardPlate.CoreSide);

    /// <summary>
    /// 一块出生棋盘生成好的地形（坐标为全图坐标）：高度、地表、预置桥、栅栏、岩石，按主题定义的难到达格（核心区之外、可落子，按坐标升序），这块棋盘的重抽次数，
    /// 以及新地表叠加（link-map-surfaces D3，<c>LinkMapGenerator.Surfaces.cs</c>）实际放下的格数——<see cref="DraftBoard"/> 的产物恒为 0，叠加之后才写入。
    /// </summary>
    internal sealed record BoardTerrain(
        BoardPlate Plate,
        ImmutableDictionary<Coord, int> Heights,
        ImmutableDictionary<Coord, Surface> Surfaces,
        ImmutableHashSet<Coord> Bridges,
        ImmutableHashSet<FenceEdge> Fences,
        ImmutableHashSet<Coord> Rocks,
        ImmutableArray<Coord> HardCells,
        int Redraws,
        int SpecialSurfaces = 0)
    {
        /// <summary>只含这一块棋盘的预览地图（画布 = 棋盘东 / 北边界外各留 <see cref="Margin"/> 格，棋盘之外与岩石为障碍）。自检与单元测试共用。</summary>
        internal MapData Preview() => PreviewOf(Plate, Heights, Surfaces, Bridges, Fences, Rocks);
    }

    /// <summary>
    /// 生成一块出生棋盘的主题地形（D3 / D4）：按 <paramref name="plate"/> 的主题调用对应的生成函数，自检不通过即在同一条流上重抽，至多 <see cref="MaxBoardRedraws"/> 次。
    /// </summary>
    internal static BoardTerrain DraftBoard(BoardPlate plate, RandomStream rng)
    {
        BoardTheme theme = plate.Theme ?? throw new ArgumentException($"出生棋盘 {plate.Origin.ToNotation()} 没有主题，无法生成地形。", nameof(plate));
        for (int redraw = 0; redraw <= MaxBoardRedraws; redraw++)
        {
            var draft = new Draft(plate, rng);
            switch (theme)
            {
                case BoardTheme.Plateau:
                    draft.Plateau();
                    break;
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
                    throw new ArgumentOutOfRangeException(nameof(plate), theme, "未知主题。");
            }

            draft.ScatterRocks();
            if (draft.Finish(redraw) is { } done)
            {
                return done;
            }
        }

        throw new MapGenerationException(
            $"出生棋盘 {plate.Origin.ToNotation()}（{plate.Width}×{plate.Height}，主题{theme.DisplayName()}）重抽 {MaxBoardRedraws} 次仍未通过棋盘级自检（生成器缺陷）。");
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

        /// <summary>主题定义的难到达格（未过滤岩石与核心区，<see cref="Finish"/> 时过滤）。</summary>
        private readonly HashSet<Coord> _hard = [];

        /// <summary>主题要素是否都放下了（数量达到下限）；放不下即本次尝试失败、重抽。</summary>
        private bool _ok = true;

        /// <summary>高台主题的平台格（自检要求都能从高度 0 沿气边走上去）。</summary>
        private readonly List<Coord> _tops = [];

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

        // ---------- 五个主题 ----------

        /// <summary>
        /// 高台：高度 2 的平台 2–4 格（连成一片），平台在中间带里的四邻全部是高度 1 的坡；平台格优先取不与外圈相邻的中间带格。
        /// 待裁决（ws 汇报 #1）：边长都 ≤ 7 的棋盘没有"不与外圈相邻"的中间带格，此时放宽为全部中间带格（外圈仍是高度 0，平台与外圈之间是崖壁）。
        /// 难到达格 = 高度 2 的格；另散布 2–3 格林地（中间带空格或外圈）作改造目标。
        /// </summary>
        internal void Plateau()
        {
            int want = Draw(2, 4);
            List<Coord> inner = [.. Middle.Where(c => !Rim.Any(r => Adjacency.AreAdjacent(r, c)))];
            List<Coord> pool = inner.Count > 0 ? inner : Middle;
            _tops.Add(Pick(pool));
            while (_tops.Count < want)
            {
                List<Coord> grow = [.. pool.Where(c => !_tops.Contains(c) && _tops.Any(t => Adjacency.AreAdjacent(t, c)))];
                if (grow.Count == 0)
                {
                    break;
                }

                _tops.Add(Pick(grow));
            }

            foreach (Coord top in _tops)
            {
                _heights[top] = 2;
                _hard.Add(top);
            }

            foreach (Coord c in Middle.Where(c => !_tops.Contains(c) && _tops.Any(t => Adjacency.AreAdjacent(t, c))))
            {
                _heights[c] = 1;
            }

            _ok &= _tops.Count >= 2;
            ScatterForest(Draw(2, 3));
        }

        /// <summary>
        /// 河流：宽 1 的深水贯穿中间带（直线或一次折弯，两端抵到外圈内侧），其中 1 格预置桥。
        /// 难到达格 = 与核心区隔着深水的一侧（河道两端各向外圈延长一格后把棋盘分成两侧，取不含核心区的那一侧）。改造目标 = 未架桥的深水格。
        /// </summary>
        internal void River()
        {
            List<(List<Coord> Cells, HashSet<Coord> Far)> courses = RiverCourses();
            (List<Coord> cells, HashSet<Coord> far) = Pick(courses);
            foreach (Coord c in cells)
            {
                _surfaces[c] = Surface.DeepWater;
            }

            _bridges.Add(Pick(cells));
            _hard.UnionWith(far);
        }

        /// <summary>林地：2–3 团林地，每团 2–4 格，团与团互不相邻；从中间带起团，可以延伸到外圈（Q1）。难到达格 = 至少两面挨着林地的格（核心区之外、非林地、可落子；负责人 2026-10-14 裁决 T2 = A，原为三面）。</summary>
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
            List<Coord> forest = [.. _surfaces.Where(kv => kv.Value == Surface.Forest).Select(kv => kv.Key)];
            foreach (Coord c in Plate.Cells().Where(c => !Plate.InCore(c) && !forest.Contains(c)))
            {
                if (forest.Count(f => Adjacency.AreAdjacent(f, c)) >= 2)
                {
                    _hard.Add(c);
                }
            }
        }

        /// <summary>
        /// 栅栏阵：2–3 段预置栅栏，每段 2–3 条共线相连的边，只立在中间带内部或中间带与核心区之间（Q4），段与段不共线相接；外圈散布 2 格林地作改造目标。
        /// 难到达格 = 栅栏围出来的口袋格（至少两条边被栅栏挡住的格）。
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

            foreach (Coord c in Plate.Cells().Where(c => !Plate.InCore(c)))
            {
                if (_fences.Count(e => e.A == c || e.B == c) >= 2)
                {
                    _hard.Add(c);
                }
            }
        }

        /// <summary>
        /// 混合：1 格高度 1 的台、1 段 2 格的深水（其中 1 格预置桥）、1 团林地（2–3 格）、1 段栅栏（2–3 条边）。
        /// 难到达格 = 台格，或隔水格（与某个未架桥深水格相邻、且隔着这格深水正对核心区的格）。改造目标 = 林地 + 未架桥深水。
        /// </summary>
        internal void Mixed()
        {
            Coord hill = Pick(Middle);
            _heights[hill] = 1;
            _hard.Add(hill);

            List<(Coord A, Coord B)> pairs = [.. Middle.Where(Free).SelectMany(a => Middle.Where(b => a < b && Free(b) && Adjacency.AreAdjacent(a, b)).Select(b => (a, b)))];
            Coord? unbridged = null;
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
                unbridged = first ? b : a;
            }

            _ok &= GrowClump(Draw(2, 3)) >= 2;
            _ok &= PlaceFence(Draw(2, 3));

            if (unbridged is { } w)
            {
                foreach (Coord c in Plate.Cells().Where(c => !Plate.InCore(c) && c != w && Adjacency.AreAdjacent(c, w)))
                {
                    var across = new Coord((2 * w.X) - c.X, (2 * w.Y) - c.Y);
                    if (Plate.InCore(across))
                    {
                        _hard.Add(c);
                    }
                }
            }
        }

        /// <summary>岩石：每块 0–2 格（按面积缩放，6×6 取 0），只放在中间带的空格上。</summary>
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

        /// <summary>在中间带空格或外圈空格上散布若干格林地（不进核心区）。</summary>
        private void ScatterForest(int count)
        {
            for (int k = 0; k < count; k++)
            {
                List<Coord> spots = [.. Plate.Cells().Where(c => !Plate.InCore(c) && Free(c))];
                if (spots.Count == 0)
                {
                    _ok = false;
                    return;
                }

                _surfaces[Pick(spots)] = Surface.Forest;
            }
        }

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
        /// 全部合规的河道（先竖直线、再横直线、再一次折弯）与各自"隔着深水的一侧"。河道每格都在中间带；直线贯穿内圈全长，折弯的两臂各至少 2 格（含拐角）、各自抵到外圈内侧，
        /// 且两臂朝向核心区、把核心区围在夹出的象限里（两臂背向核心区只是切掉一个角，不算贯穿）。
        /// 隔着深水的一侧：直线取与核心区不同侧的半平面；折弯取象限之外（去掉河道与两端延长到外圈的格）。
        /// </summary>
        private List<(List<Coord> Cells, HashSet<Coord> Far)> RiverCourses()
        {
            var courses = new List<(List<Coord>, HashSet<Coord>)>();
            int x0 = Plate.Origin.X + 1, x1 = Plate.Origin.X + Plate.Width - 2;
            int y0 = Plate.Origin.Y + 1, y1 = Plate.Origin.Y + Plate.Height - 2;
            Coord core = Plate.CoreOrigin;
            List<int> columns = [.. Enumerable.Range(x0, x1 - x0 + 1).Where(x => Enumerable.Range(y0, y1 - y0 + 1).All(y => Plate.InMiddleBand(new Coord(x, y))))];
            List<int> rows = [.. Enumerable.Range(y0, y1 - y0 + 1).Where(y => Enumerable.Range(x0, x1 - x0 + 1).All(x => Plate.InMiddleBand(new Coord(x, y))))];

            foreach (int x in columns)
            {
                int side = Math.Sign(core.X - x);
                courses.Add(([.. Enumerable.Range(y0, y1 - y0 + 1).Select(y => new Coord(x, y))], [.. Plate.Cells().Where(c => Math.Sign(c.X - x) == -side)]));
            }

            foreach (int y in rows)
            {
                int side = Math.Sign(core.Y - y);
                courses.Add(([.. Enumerable.Range(x0, x1 - x0 + 1).Select(x => new Coord(x, y))], [.. Plate.Cells().Where(c => Math.Sign(c.Y - y) == -side)]));
            }

            foreach (int x in columns)
            {
                foreach (int y in rows)
                {
                    foreach (int dy in new[] { -1, 1 })
                    {
                        foreach (int dx in new[] { -1, 1 })
                        {
                            int endY = dy < 0 ? y0 : y1;
                            int endX = dx < 0 ? x0 : x1;
                            if ((endY - y) * dy < 1 || (endX - x) * dx < 1)
                            {
                                continue;   // 某一臂只有拐角一格
                            }

                            bool InQuadrant(Coord c) => Math.Sign(c.X - x) == dx && Math.Sign(c.Y - y) == dy;
                            if (!InQuadrant(core))
                            {
                                continue;   // 两臂背向核心区时只是切掉一个角，不算"贯穿中间带"
                            }

                            List<Coord> cells = [.. Enumerable.Range(0, ((endY - y) * dy) + 1).Select(k => new Coord(x, y + (k * dy)))];
                            cells.AddRange(Enumerable.Range(1, (endX - x) * dx).Select(k => new Coord(x + (k * dx), y)));
                            var extension = new HashSet<Coord> { new(x, endY + dy), new(endX + dx, y) };
                            courses.Add((cells, [.. Plate.Cells().Where(c => !InQuadrant(c) && !cells.Contains(c) && !extension.Contains(c))]));
                        }
                    }
                }
            }

            return courses;
        }

        // ---------- 自检 ----------

        /// <summary>
        /// 棋盘级自检（D3）：主题要素都放下了；棋盘内可落子格沿几何四邻连通（只有一个战场分量）；可改造目标 ≥ <see cref="MinEditTargets"/>；
        /// 高台的每个高度 2 格都能从核心区沿气边走上去。通过返回成品，否则 <c>null</c>（调用方重抽）。
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

            if (_tops.Count > 0)
            {
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

                if (!_tops.All(reached.Contains))
                {
                    return null;
                }
            }

            ImmutableArray<Coord> hard = [.. _hard.Where(c => Plate.Contains(c) && !Plate.InCore(c) && preview.IsPlayable(c)).Order()];
            return new BoardTerrain(Plate, heights, surfaces, bridges, fences, rocks, hard, redraws);
        }
    }
}
