using Siege.Core.Determinism;

namespace Siege.Core.Board.Maps;

/// <summary>
/// 棋盘档一次摆放尝试的工作态与构造（board-isolated-gen D2 / D3）：摆棋盘 → 裁出图面 → 布信物。填场景（其余格一律障碍）在灌成地图数据时完成。
/// 棋盘之间没有通道：每块棋盘是独立的围棋盘，棋盘外接矩形两两至少隔 2 格场景，不同棋盘的格子之间不存在四邻接。
/// </summary>
/// <remarks>
/// <para><b>摆法</b>（无通道打包）：第一块公共棋盘（主战场）放在工作区中央附近；其余公共棋盘、再出生棋盘依次贴到某块已摆棋盘某一边的外侧，
/// 间隔 <see cref="MinGap"/>–<see cref="MaxGap"/> 格、投影与对方至少重叠 1 格——重叠只为让棋盘群紧凑、图面不至于稀疏拉长，不产生任何连通。
/// 公共棋盘只贴公共棋盘；出生棋盘优先贴公共棋盘（构图上"出生棋盘靠着公共棋盘"），贴不上才贴已摆的出生棋盘。这只是构图，没有规则含义。</para>
/// <para><b>确定性</b>（<c>.trellis/spec/core/determinism.md</c>）：工作态全部是按 <c>[列, 行]</c> 下标的二维数组与按构造次序排列的列表，
/// 遍历一律双循环或列表下标序；不使用任何按散列次序遍历的容器；随机只来自构造时传入的那一条序列；全程整数运算。
/// 候选先按固定次序（宿主下标 → 方向 → 间隔 → 偏移）收集成列表，再用随机数取下标——并列由枚举次序打破。</para>
/// <para><b>尺寸</b>：棋盘先摆在固定大小的工作区里；摆完后取全部棋盘的实际外接范围，四周各留 <see cref="EdgeMargin"/> 格场景裁出图面，
/// 列数与行数各自独立。裁切之后本类的全部坐标（<see cref="Grid"/>、<see cref="Boards"/>、<see cref="Relics"/>、<see cref="Entrance"/>）都在图面坐标系里。</para>
/// <para>任何一步走不下去（工作区放不下、裁出的尺寸越界、信物放不下）就返回 <c>false</c> 并说明原因，由调用方换下一次尝试；不产出半张图。</para>
/// </remarks>
internal sealed class BoardMapLayout
{
    /// <summary>棋盘离地图外缘至少 2 格；裁出图面时四周所留的场景也是这个数（最靠外的棋盘离外缘恰 2 格）。</summary>
    internal const int EdgeMargin = 2;

    /// <summary>摆放工作区的边长：等于图面上限，裁出的图面因此不会超过上限。</summary>
    internal const int WorkArea = 60;

    /// <summary>裁出的图面列数与行数各自的区间。</summary>
    internal const int MinMapSide = 15;
    internal const int MaxMapSide = 60;

    /// <summary>贴放时与宿主棋盘之间的间隔区间；棋盘外接矩形两两至少隔 <see cref="MinGap"/> 格（摆放时向四周各扩这么多格不碰任何棋盘）。</summary>
    internal const int MinGap = 2;
    internal const int MaxGap = 4;

    /// <summary>出生棋盘与公共棋盘的边长区间（负责人裁决 2026-10-06 第 2 条：出生 5–7、公共 7–15）。</summary>
    internal const int BirthMinSide = 5;
    internal const int BirthMaxSide = 7;
    internal const int PublicMinSide = 7;
    internal const int PublicMaxSide = 15;

    /// <summary>主战场（第一块公共棋盘）的宽与高都不小于此值；面积最大的公共棋盘也必须满足（规格「棋盘档布局规则」）。</summary>
    internal const int MainMinSide = 11;

    /// <summary>
    /// 公共棋盘按较短边分档放信物（design D3）：较短边 ≤ <see cref="OneRelicSide"/> 放 1 个，≤ <see cref="TwoRelicSide"/> 放 2 个，更大放 3 个。
    /// </summary>
    internal const int OneRelicSide = 8;
    internal const int TwoRelicSide = 10;

    /// <summary>同一块棋盘上两个信物格至少相隔几格（两个方向上距离的较大者）。</summary>
    private const int RelicSpacing = 3;

    /// <summary>网格里的矩形：西南角（0 基列 / 行索引）与宽高。</summary>
    internal readonly record struct Rect(int X, int Y, int W, int H)
    {
        internal int X1 => X + W - 1;

        internal int Y1 => Y + H - 1;

        internal int Area => W * H;

        internal Rect Grow(int by) => new(X - by, Y - by, W + (2 * by), H + (2 * by));
    }

    private readonly RandomStream _rng;
    private readonly int _size = WorkArea;
    private readonly BoardSize[] _sizes;
    private readonly int _births;
    private readonly bool[] _placed;

    /// <summary>
    /// <paramref name="births"/> 与 <paramref name="publics"/> 是各棋盘的宽高；棋盘下标 = 出生棋盘在前（下标即出生区编号）、公共棋盘在后。
    /// </summary>
    internal BoardMapLayout(RandomStream rng, BoardSize[] births, BoardSize[] publics)
    {
        _rng = rng;
        _births = births.Length;
        _sizes = [.. births, .. publics];
        _placed = new bool[_sizes.Length];
        Boards = new Rect[_sizes.Length];
        Grid = new int[_size, _size];
        Width = _size;
        Height = _size;
    }

    /// <summary>每格的归属：0 = 场景，<c>i + 1</c> = 第 i 块棋盘。</summary>
    internal int[,] Grid { get; private set; }

    /// <summary>信物格；裁出图面之后才有。</summary>
    internal RelicCellSpec?[,] Relics { get; private set; } = new RelicCellSpec?[0, 0];

    /// <summary>图面的列数与行数；裁切之前是工作区的边长。</summary>
    internal int Width { get; private set; }

    internal int Height { get; private set; }

    internal Rect[] Boards { get; }

    /// <summary>中央入口：面积最大的公共棋盘的中心格。</summary>
    internal (int X, int Y) Entrance { get; private set; }

    internal bool IsBirth(int board) => board < _births;

    /// <summary>公共棋盘按较短边应放几个信物格（design D3）。</summary>
    internal static int PublicRelicsFor(int shorterSide) =>
        shorterSide <= OneRelicSide ? 1 : shorterSide <= TwoRelicSide ? 2 : 3;

    /// <summary>
    /// 一次尝试内抽边长的重抽上限：全部边长按原分布整组重抽，至多这么多组；仍没有一组落进目标带且主战场合格，本次尝试作废。
    /// </summary>
    internal const int MaxSizeDraws = 200;

    /// <summary>
    /// 抽各棋盘的边长（design D2"预算抽样"）：出生棋盘宽高各在 5–7、主战场（第一块公共棋盘）各在 11–15、其余公共棋盘各在 7–15，均匀独立抽取；
    /// Σ 面积不在目标带、或面积最大的公共棋盘宽高不都 ≥ <see cref="MainMinSide"/>，就在同一条随机序列上整组重抽——不缩边、不放边，
    /// 被接受的那一组边长服从原分布在条件下的分布，公共棋盘不会被压向下限。重抽 <see cref="MaxSizeDraws"/> 组仍不合格即本次尝试作废。
    /// 棋盘之间没有通道，可落子格就是 Σ 棋盘面积。<paramref name="draws"/> 是实际抽了几组（只供诊断）。
    /// </summary>
    internal static bool TrySampleSizes(
        RandomStream rng, int birthCount, int publicCount, int minPlayable, int maxPlayable,
        out BoardSize[] births, out BoardSize[] publics, out int draws, out string reason)
    {
        var rolled = new int[2 * (birthCount + publicCount)];
        var floor = new int[rolled.Length];
        var ceiling = new int[rolled.Length];
        for (int i = 0; i < rolled.Length; i++)
        {
            int board = i / 2;
            bool birth = board < birthCount;
            floor[i] = birth ? BirthMinSide : board == birthCount ? MainMinSide : PublicMinSide;
            ceiling[i] = birth ? BirthMaxSide : PublicMaxSide;
        }

        for (draws = 1; draws <= MaxSizeDraws; draws++)
        {
            // 抽样次序：公共棋盘在前（先定大局），出生棋盘在后；每块先宽后高。
            for (int i = 2 * birthCount; i < rolled.Length; i++)
            {
                rolled[i] = floor[i] + rng.NextInt(ceiling[i] - floor[i] + 1);
            }

            for (int i = 0; i < 2 * birthCount; i++)
            {
                rolled[i] = floor[i] + rng.NextInt(ceiling[i] - floor[i] + 1);
            }

            int area = Area(rolled);
            if (area < minPlayable || area > maxPlayable || !MainIsLargest(rolled, birthCount))
            {
                continue;
            }

            births = new BoardSize[birthCount];
            publics = new BoardSize[publicCount];
            for (int board = 0; board < births.Length + publics.Length; board++)
            {
                var size = new BoardSize(rolled[2 * board], rolled[(2 * board) + 1]);
                if (board < births.Length)
                {
                    births[board] = size;
                }
                else
                {
                    publics[board - births.Length] = size;
                }
            }

            reason = string.Empty;
            return true;
        }

        draws = MaxSizeDraws;
        births = [];
        publics = [];
        reason = $"重抽 {MaxSizeDraws} 组边长都没有落进目标带 {minPlayable}–{maxPlayable}（且面积最大的公共棋盘宽高都不小于 {MainMinSide}）。";
        return false;
    }

    /// <summary>面积最大的公共棋盘（并列取靠前者，主战场在最前）宽高都 ≥ <see cref="MainMinSide"/>。</summary>
    private static bool MainIsLargest(int[] rolled, int birthCount)
    {
        int largest = 2 * birthCount;
        for (int i = largest + 2; i < rolled.Length; i += 2)
        {
            if (rolled[i] * rolled[i + 1] > rolled[largest] * rolled[largest + 1])
            {
                largest = i;
            }
        }

        return rolled[largest] >= MainMinSide && rolled[largest + 1] >= MainMinSide;
    }

    private static int Area(int[] sides)
    {
        int area = 0;
        for (int i = 0; i < sides.Length; i += 2)
        {
            area += sides[i] * sides[i + 1];
        }

        return area;
    }

    internal bool TryBuild(out string reason) =>
        PlaceBoards(out reason) && Crop(out reason) && PlaceRelics(out reason);

    /// <summary>可落子格总数：棋盘格（棋盘之外一律是场景）。</summary>
    internal int PlayableCount()
    {
        int count = 0;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (Grid[x, y] != 0)
                {
                    count++;
                }
            }
        }

        return count;
    }

    // ---------- 摆棋盘 ----------

    private bool PlaceBoards(out string reason)
    {
        // 第一块公共棋盘（主战场）放在工作区中央附近（各方向至多偏 2 格）。
        int anchor = _births;
        BoardSize first = _sizes[anchor];
        int room = _size - (2 * EdgeMargin);
        if (first.Width > room || first.Height > room)
        {
            reason = $"{_size}×{_size} 的工作区放不下 {first.Width}×{first.Height} 的公共棋盘。";
            return false;
        }

        int ax = Clamp(((_size - first.Width) / 2) + _rng.NextInt(5) - 2, EdgeMargin, _size - EdgeMargin - first.Width);
        int ay = Clamp(((_size - first.Height) / 2) + _rng.NextInt(5) - 2, EdgeMargin, _size - EdgeMargin - first.Height);
        Put(anchor, new Rect(ax, ay, first.Width, first.Height));

        // 其余公共棋盘（只贴公共棋盘），再是出生棋盘（下标序；先找公共棋盘作宿主，没有位置才找出生棋盘）。
        for (int board = anchor + 1; board < _sizes.Length; board++)
        {
            if (!Attach(board, publicHosts: true, out reason))
            {
                return false;
            }
        }

        for (int board = 0; board < _births; board++)
        {
            if (!Attach(board, publicHosts: true, out reason) && !Attach(board, publicHosts: false, out reason))
            {
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

    /// <summary>把一块棋盘贴到某块已摆好的棋盘外侧：<paramref name="publicHosts"/> 为真只取公共棋盘作宿主，否则只取出生棋盘。</summary>
    private bool Attach(int board, bool publicHosts, out string reason)
    {
        List<Rect> placements = Placements(board, publicHosts);
        if (placements.Count == 0)
        {
            reason = NoRoom(board);
            return false;
        }

        Put(board, placements[_rng.NextInt(placements.Count)]);
        reason = string.Empty;
        return true;
    }

    private string NoRoom(int board)
    {
        BoardSize size = _sizes[board];
        return $"{_size}×{_size} 的工作区里没有位置再贴一块 {size.Width}×{size.Height} 的{(IsBirth(board) ? "出生" : "公共")}棋盘。";
    }

    private void Put(int board, Rect rect)
    {
        Boards[board] = rect;
        _placed[board] = true;
        for (int y = rect.Y; y <= rect.Y1; y++)
        {
            for (int x = rect.X; x <= rect.X1; x++)
            {
                Grid[x, y] = board + 1;
            }
        }
    }

    /// <summary>
    /// 全部合法的摆放位置：宿主为已摆的公共棋盘（<paramref name="publicHosts"/>）或出生棋盘，按宿主下标 → 方向（东、西、北、南）→ 间隔 → 偏移的次序；
    /// 投影与宿主至少重叠 1 格。
    /// </summary>
    private List<Rect> Placements(int board, bool publicHosts)
    {
        BoardSize size = _sizes[board];
        var found = new List<Rect>();
        int hostFrom = publicHosts ? _births : 0;
        int hostTo = publicHosts ? _sizes.Length : _births;
        for (int host = hostFrom; host < hostTo; host++)
        {
            if (!_placed[host])
            {
                continue;
            }

            Rect h = Boards[host];
            for (int side = 0; side < 4; side++)
            {
                bool horizontal = side < 2;
                for (int gap = MinGap; gap <= MaxGap; gap++)
                {
                    // 沿宿主这条边滑动：投影至少重叠 1 格。
                    int span = horizontal ? size.Height : size.Width;
                    int hostLow = horizontal ? h.Y : h.X;
                    int hostHigh = horizontal ? h.Y1 : h.X1;
                    for (int low = hostLow - span + 1; low <= hostHigh; low++)
                    {
                        Rect rect = side switch
                        {
                            0 => new Rect(h.X1 + 1 + gap, low, size.Width, size.Height),
                            1 => new Rect(h.X - gap - size.Width, low, size.Width, size.Height),
                            2 => new Rect(low, h.Y1 + 1 + gap, size.Width, size.Height),
                            _ => new Rect(low, h.Y - gap - size.Height, size.Width, size.Height),
                        };
                        if (Fits(rect))
                        {
                            found.Add(rect);
                        }
                    }
                }
            }
        }

        return found;
    }

    /// <summary>棋盘放得下：离工作区外缘 ≥ 2 格；向四周各扩 <see cref="MinGap"/> 格（含对角）不碰任何棋盘。</summary>
    private bool Fits(Rect rect)
    {
        if (rect.X < EdgeMargin || rect.Y < EdgeMargin || rect.X1 > _size - 1 - EdgeMargin || rect.Y1 > _size - 1 - EdgeMargin)
        {
            return false;
        }

        Rect wide = rect.Grow(MinGap);
        for (int y = wide.Y; y <= wide.Y1; y++)
        {
            for (int x = wide.X; x <= wide.X1; x++)
            {
                if (x >= 0 && y >= 0 && x < _size && y < _size && Grid[x, y] != 0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    // ---------- 裁出图面 ----------

    /// <summary>
    /// 取全部棋盘的实际外接范围，四周各留 <see cref="EdgeMargin"/> 格裁出图面，并把全部坐标平移到图面坐标系。
    /// 列数或行数不在 <see cref="MinMapSide"/>–<see cref="MaxMapSide"/> 之内则本次尝试作废。不消耗随机数。
    /// </summary>
    private bool Crop(out string reason)
    {
        int west = _size;
        int south = _size;
        int east = -1;
        int north = -1;
        for (int y = 0; y < _size; y++)
        {
            for (int x = 0; x < _size; x++)
            {
                if (Grid[x, y] == 0)
                {
                    continue;
                }

                west = x < west ? x : west;
                east = x > east ? x : east;
                south = y < south ? y : south;
                north = y > north ? y : north;
            }
        }

        int width = east - west + 1 + (2 * EdgeMargin);
        int height = north - south + 1 + (2 * EdgeMargin);
        if (width < MinMapSide || width > MaxMapSide || height < MinMapSide || height > MaxMapSide)
        {
            reason = $"裁出的图面为 {width}×{height}，列数与行数须各在 {MinMapSide}–{MaxMapSide} 之内。";
            return false;
        }

        int dx = west - EdgeMargin;
        int dy = south - EdgeMargin;
        var grid = new int[width, height];
        for (int y = south; y <= north; y++)
        {
            for (int x = west; x <= east; x++)
            {
                grid[x - dx, y - dy] = Grid[x, y];
            }
        }

        for (int i = 0; i < Boards.Length; i++)
        {
            Boards[i] = Boards[i] with { X = Boards[i].X - dx, Y = Boards[i].Y - dy };
        }

        Grid = grid;
        Relics = new RelicCellSpec?[width, height];
        Width = width;
        Height = height;
        reason = string.Empty;
        return true;
    }

    // ---------- 布信物 ----------

    private bool PlaceRelics(out string reason)
    {
        // 中央入口：面积最大的公共棋盘的中心格；面积并列取清单里靠前的一块。高档信物就放在这一格。
        int largest = _births;
        for (int board = _births + 1; board < _sizes.Length; board++)
        {
            if (Boards[board].Area > Boards[largest].Area)
            {
                largest = board;
            }
        }

        Rect center = Boards[largest];
        Entrance = (center.X + ((center.W - 1) / 2), center.Y + ((center.H - 1) / 2));
        Relics[Entrance.X, Entrance.Y] = new RelicCellSpec(RelicZone.Contested, BudgetTier.High);

        for (int board = _births; board < _sizes.Length; board++)
        {
            Rect rect = Boards[board];
            int want = PublicRelicsFor(rect.W < rect.H ? rect.W : rect.H);
            for (int placed = board == largest ? 1 : 0; placed < want; placed++)
            {
                if (!PlaceRelic(rect, new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard)))
                {
                    reason = $"第 {board + 1} 块棋盘上放不下第 {placed + 1} 个信物格。";
                    return false;
                }
            }
        }

        for (int board = 0; board < _births; board++)
        {
            if (!PlaceRelic(Boards[board], new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth)))
            {
                reason = $"第 {board + 1} 块出生棋盘上放不下信物格。";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>在棋盘的最外一圈以内随机取一格放信物，与同一块棋盘上已有的信物格至少相隔 <see cref="RelicSpacing"/> 格。</summary>
    private bool PlaceRelic(Rect rect, RelicCellSpec spec)
    {
        var candidates = new List<(int X, int Y)>();
        for (int y = rect.Y + 1; y <= rect.Y1 - 1; y++)
        {
            for (int x = rect.X + 1; x <= rect.X1 - 1; x++)
            {
                if (FarFromRelics(rect, x, y))
                {
                    candidates.Add((x, y));
                }
            }
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        (int px, int py) = candidates[_rng.NextInt(candidates.Count)];
        Relics[px, py] = spec;
        return true;
    }

    private bool FarFromRelics(Rect rect, int x, int y)
    {
        for (int ry = rect.Y; ry <= rect.Y1; ry++)
        {
            for (int rx = rect.X; rx <= rect.X1; rx++)
            {
                if (Relics[rx, ry] is null)
                {
                    continue;
                }

                int dx = rx > x ? rx - x : x - rx;
                int dy = ry > y ? ry - y : y - ry;
                if ((dx > dy ? dx : dy) < RelicSpacing)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
