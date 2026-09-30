using Siege.Core.Determinism;

namespace Siege.Core.Board.Maps;

/// <summary>
/// 棋盘档一次摆放尝试的工作态与构造（board-map D3–D5、D8）：摆棋盘 → 连通道 → 裁出图面 → 布信物。填场景（其余格一律障碍）在灌成地图数据时完成。
/// </summary>
/// <remarks>
/// <para><b>摆法</b>：从第一块公共棋盘（11×11）出发向外生长——每块新棋盘贴在一块已摆好的公共棋盘某一边的外侧，
/// 间隔 2–4 格、投影重叠不小于通道最小宽度，两者之间随即开一条直通道。于是"全部棋盘连成一片""出生棋盘直通公共棋盘"
/// "通道是直条且两端各贴一块棋盘"都由构造保证，不是摆完再去连。公共棋盘先摆，出生棋盘后摆。</para>
/// <para><b>第二条出路</b>（2026-09-30 裁决：出生棋盘 2–4 条通道且通向至少两块不同棋盘）：摆出生棋盘时优先挑"贴着宿主公共棋盘、
/// 另一边又正对某块已摆棋盘（间隔 2–4、投影重叠不小于通道最小宽度、通道放得下）"的位置，两条通道一起开；
/// 没有这样的位置才退回单通道摆法。全部摆完后，对仍不足两块不同邻盘的出生棋盘再找一次正对的棋盘补开通道，补不上即本次尝试作废。
/// 通道 3–4 格宽，两条通道之间至少隔 1 格，5–7 边长的出生棋盘一条边只放得下一条通道，所以两条出路必然在不同方向。</para>
/// <para><b>确定性</b>（<c>.trellis/spec/core/determinism.md</c>）：工作态全部是按 <c>[列, 行]</c> 下标的二维数组与按构造次序排列的列表，
/// 遍历一律双循环或列表下标序；不使用任何按散列次序遍历的容器；随机只来自构造时传入的那一条序列；全程整数运算。
/// 候选先按固定次序（宿主下标 → 方向 → 间隔 → 偏移；对方棋盘下标 → 通道宽 → 位置）收集成列表，再用随机数取下标——并列由枚举次序打破。</para>
/// <para><b>尺寸</b>（D5，2026-09-29 裁决）：棋盘先摆在固定大小的工作区里；摆完后取全部棋盘与通道的实际外接范围，四周各留
/// <see cref="EdgeMargin"/> 格场景裁出图面，列数与行数各自独立。裁切之后本类的全部坐标（<see cref="Grid"/>、<see cref="Boards"/>、
/// <see cref="Links"/>、<see cref="Relics"/>、<see cref="Entrance"/>）都在图面坐标系里。</para>
/// <para>任何一步走不下去（工作区放不下、补不出第二条出路、裁出的尺寸越界）就返回 <c>false</c> 并说明原因，由调用方换下一次尝试；不产出半张图。</para>
/// </remarks>
internal sealed class BoardMapLayout
{
    /// <summary>棋盘离地图外缘至少 2 格；裁出图面时四周所留的场景也是这个数（最靠外的棋盘离外缘恰 2 格）。</summary>
    internal const int EdgeMargin = 2;

    /// <summary>摆放工作区的边长。</summary>
    internal const int WorkArea = 50;

    /// <summary>裁出的图面列数与行数各自的区间。</summary>
    internal const int MinMapSide = 20;
    internal const int MaxMapSide = 50;

    /// <summary>棋盘的外接矩形两两至少隔 2 格。</summary>
    internal const int BoardGap = 2;

    /// <summary>通道长度（= 它连接的两块棋盘之间的间隔）区间。</summary>
    internal const int MinCorridorLength = 2;
    internal const int MaxCorridorLength = 4;

    /// <summary>通道宽度区间（2026-09-30 裁决：原 1–2 格几乎全被堵死，加宽到 3–4）。</summary>
    internal const int MinCorridorWidth = 3;
    internal const int MaxCorridorWidth = 4;

    /// <summary>出生棋盘与公共棋盘的边长区间；第一块公共棋盘的宽与高都恰为 <see cref="AnchorSide"/>（2026-09-30 裁决：公共棋盘上限 15 → 11）。</summary>
    internal const int BirthMinSide = 5;
    internal const int BirthMaxSide = 7;
    internal const int PublicMinSide = 9;
    internal const int PublicMaxSide = 11;
    internal const int AnchorSide = 11;

    /// <summary>每块出生棋盘 2–4 条通道，每块公共棋盘 1–6 条。</summary>
    internal const int BirthMinLinks = 2;
    internal const int BirthMaxLinks = 4;
    internal const int PublicMaxLinks = 6;

    /// <summary>公共棋盘上边长（宽高较小者）不超过此值的放 2 个信物格，更大的放 3 个。</summary>
    internal const int SmallPublicSide = 10;

    /// <summary>同一块棋盘上两个信物格至少相隔几格（两个方向上距离的较大者）。</summary>
    private const int RelicSpacing = 3;

    /// <summary>
    /// 抽样收敛时给每条通道留的余量：宽 3–4 × 长 2–4 的期望约 10 格，多留一点让摆完后的实际规模更少落到目标带之外。
    /// 通道条数按"公共棋盘连成树 + 每块出生棋盘两条"估：<c>公共棋盘数 − 1 + 2 × 5</c>。
    /// </summary>
    private const int CorridorReserve = 12;

    /// <summary>网格里的矩形：西南角（0 基列 / 行索引）与宽高。</summary>
    internal readonly record struct Rect(int X, int Y, int W, int H)
    {
        internal int X1 => X + W - 1;

        internal int Y1 => Y + H - 1;

        internal int Area => W * H;

        internal bool Contains(int x, int y) => x >= X && x <= X1 && y >= Y && y <= Y1;

        internal Rect Grow(int by) => new(X - by, Y - by, W + (2 * by), H + (2 * by));
    }

    /// <summary>一条通道：两端棋盘的下标与通道的格子。</summary>
    internal readonly record struct Link(int From, int To, Rect Cells);

    /// <summary>一个摆放位置：贴着 <paramref name="Host"/> 的哪一侧（横向 = 东西相对）、投影重叠的区间。也用来描述"已摆的两块棋盘正对"。</summary>
    private readonly record struct Placement(int Host, Rect Board, bool Horizontal, int OverlapLow, int OverlapHigh);

    private const int Corridor = -1;

    private readonly RandomStream _rng;
    private readonly int _size = WorkArea;
    private readonly BoardSize[] _sizes;
    private readonly int _births;
    private readonly int[] _degree;
    private readonly bool[] _placed;

    /// <summary>
    /// <paramref name="births"/> 与 <paramref name="publics"/> 是各棋盘的宽高；棋盘下标 = 出生棋盘在前（下标即出生区编号）、公共棋盘在后。
    /// </summary>
    internal BoardMapLayout(RandomStream rng, BoardSize[] births, BoardSize[] publics)
    {
        _rng = rng;
        _births = births.Length;
        _sizes = [.. births, .. publics];
        _degree = new int[_sizes.Length];
        _placed = new bool[_sizes.Length];
        Boards = new Rect[_sizes.Length];
        Grid = new int[_size, _size];
        Width = _size;
        Height = _size;
    }

    /// <summary>每格的归属：0 = 场景，<c>i + 1</c> = 第 i 块棋盘，−1 = 通道。</summary>
    internal int[,] Grid { get; private set; }

    /// <summary>信物格；裁出图面之后才有。</summary>
    internal RelicCellSpec?[,] Relics { get; private set; } = new RelicCellSpec?[0, 0];

    /// <summary>图面的列数与行数；裁切之前是工作区的边长。</summary>
    internal int Width { get; private set; }

    internal int Height { get; private set; }

    internal Rect[] Boards { get; }

    internal List<Link> Links { get; } = [];

    /// <summary>中央入口：面积最大的公共棋盘的中心格。</summary>
    internal (int X, int Y) Entrance { get; private set; }

    internal bool IsBirth(int board) => board < _births;

    /// <summary>该棋盘至多几条通道。</summary>
    internal int MaxLinks(int board) => IsBirth(board) ? BirthMaxLinks : PublicMaxLinks;

    /// <summary>
    /// 抽各棋盘的边长，并朝目标带收敛：Σ 面积超出上界（目标带上限减去通道余量）时逐格缩最长的边，不足下界时逐格放最短的边；
    /// 仍落不进目标带则本次尝试作废。
    /// </summary>
    internal static bool TrySampleSizes(
        RandomStream rng, int publicCount, int minPlayable, int maxPlayable, out BoardSize[] births, out BoardSize[] publics, out string reason)
    {
        var rolled = new int[2 * (BoardMapParameters.BirthBoards + publicCount)];
        var floor = new int[rolled.Length];
        var ceiling = new int[rolled.Length];
        for (int i = 0; i < rolled.Length; i++)
        {
            int board = i / 2;
            bool birth = board < BoardMapParameters.BirthBoards;
            floor[i] = birth ? BirthMinSide : board == BoardMapParameters.BirthBoards ? AnchorSide : PublicMinSide;
            ceiling[i] = birth ? BirthMaxSide : board == BoardMapParameters.BirthBoards ? AnchorSide : PublicMaxSide;
        }

        // 抽样次序：公共棋盘在前（先定大局），出生棋盘在后；每块先宽后高。
        for (int i = 2 * BoardMapParameters.BirthBoards; i < rolled.Length; i++)
        {
            rolled[i] = floor[i] + rng.NextInt(ceiling[i] - floor[i] + 1);
        }

        for (int i = 0; i < 2 * BoardMapParameters.BirthBoards; i++)
        {
            rolled[i] = floor[i] + rng.NextInt(ceiling[i] - floor[i] + 1);
        }

        int links = publicCount - 1 + (BirthMinLinks * BoardMapParameters.BirthBoards);
        int upper = maxPlayable - (links * CorridorReserve);
        int lower = minPlayable;
        while (Area(rolled) > upper)
        {
            // 缩最长的边；公共棋盘优先（下标大的一半），并列取下标靠前者。
            int pick = -1;
            for (int pass = 0; pass < 2 && pick < 0; pass++)
            {
                int from = pass == 0 ? 2 * BoardMapParameters.BirthBoards : 0;
                int to = pass == 0 ? rolled.Length : 2 * BoardMapParameters.BirthBoards;
                for (int i = from; i < to; i++)
                {
                    if (rolled[i] > floor[i] && (pick < 0 || rolled[i] > rolled[pick]))
                    {
                        pick = i;
                    }
                }
            }

            if (pick < 0)
            {
                births = [];
                publics = [];
                reason = $"棋盘边长取到下限仍有 {Area(rolled)} 格，超出目标带上界 {upper}（已扣通道余量）。";
                return false;
            }

            rolled[pick]--;
        }

        while (Area(rolled) < lower)
        {
            // 放最短的边；公共棋盘优先，并列取下标靠前者。
            int pick = -1;
            for (int pass = 0; pass < 2 && pick < 0; pass++)
            {
                int from = pass == 0 ? 2 * BoardMapParameters.BirthBoards : 0;
                int to = pass == 0 ? rolled.Length : 2 * BoardMapParameters.BirthBoards;
                for (int i = from; i < to; i++)
                {
                    if (rolled[i] < ceiling[i] && (pick < 0 || rolled[i] < rolled[pick]))
                    {
                        pick = i;
                    }
                }
            }

            if (pick < 0)
            {
                births = [];
                publics = [];
                reason = $"棋盘边长取到上限仍只有 {Area(rolled)} 格，不足目标带下界 {lower}。";
                return false;
            }

            rolled[pick]++;
        }

        births = new BoardSize[BoardMapParameters.BirthBoards];
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

    /// <summary>可落子格总数：棋盘格 + 通道格。</summary>
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

    /// <summary>该棋盘经通道直接相邻的不同棋盘数。</summary>
    internal int DistinctNeighbors(int board)
    {
        var seen = new bool[_sizes.Length];
        int count = 0;
        for (int i = 0; i < Links.Count; i++)
        {
            int other = Links[i].From == board ? Links[i].To : Links[i].To == board ? Links[i].From : -1;
            if (other >= 0 && !seen[other])
            {
                seen[other] = true;
                count++;
            }
        }

        return count;
    }

    // ---------- 摆棋盘、连通道 ----------

    private bool PlaceBoards(out string reason)
    {
        // 第一块公共棋盘放在工作区中央附近（各方向至多偏 2 格）。
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

        // 其余公共棋盘（各接一条通道到已摆的公共棋盘），再是出生棋盘（下标序，尽量两条通道一起开）。
        for (int board = anchor + 1; board < _sizes.Length; board++)
        {
            if (!Attach(board, out reason))
            {
                return false;
            }
        }

        for (int board = 0; board < _births; board++)
        {
            if (!AttachBirth(board, out reason))
            {
                return false;
            }
        }

        // 修补：仍不足两块不同邻盘的出生棋盘，向正对的棋盘补开一条通道。
        for (int board = 0; board < _births; board++)
        {
            if (DistinctNeighbors(board) >= BirthMinLinks)
            {
                continue;
            }

            List<(int Board, Rect Corridor)> partners = Partners(board, Boards[board], pending: null);
            if (partners.Count == 0)
            {
                reason = $"第 {board + 1} 块出生棋盘找不到第二条出路：没有正对着它、隔 {MinCorridorLength}–{MaxCorridorLength} 格且通道放得下的别的棋盘。";
                return false;
            }

            (int partner, Rect corridor) = partners[_rng.NextInt(partners.Count)];
            Connect(partner, board, corridor);
        }

        reason = string.Empty;
        return true;
    }

    private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

    /// <summary>把一块公共棋盘贴到某块已摆好的公共棋盘外侧，并开出两者之间的通道。</summary>
    private bool Attach(int board, out string reason)
    {
        List<Placement> placements = Placements(board);
        while (placements.Count > 0)
        {
            int pick = _rng.NextInt(placements.Count);
            Placement placement = placements[pick];
            List<Rect> corridors = Corridors(placement, pending: null);
            if (corridors.Count == 0)
            {
                placements.RemoveAt(pick);
                continue;
            }

            Put(board, placement.Board);
            Connect(placement.Host, board, corridors[_rng.NextInt(corridors.Count)]);
            reason = string.Empty;
            return true;
        }

        reason = NoRoom(board);
        return false;
    }

    /// <summary>
    /// 把一块出生棋盘贴到某块公共棋盘外侧。优先挑另一边还正对着别的已摆棋盘的位置，两条通道一起开；没有这样的位置才只开一条。
    /// </summary>
    private bool AttachBirth(int board, out string reason)
    {
        List<Placement> placements = Placements(board);
        var paired = new List<Placement>();
        for (int i = 0; i < placements.Count; i++)
        {
            if (Partners(board, placements[i].Board, pending: null, exclude: placements[i].Host).Count > 0)
            {
                paired.Add(placements[i]);
            }
        }

        while (paired.Count > 0)
        {
            int pick = _rng.NextInt(paired.Count);
            Placement placement = paired[pick];
            List<Rect> corridors = Corridors(placement, pending: null);
            while (corridors.Count > 0)
            {
                int which = _rng.NextInt(corridors.Count);
                Rect corridor = corridors[which];
                List<(int Board, Rect Corridor)> partners = Partners(board, placement.Board, pending: corridor, exclude: placement.Host);
                if (partners.Count == 0)
                {
                    corridors.RemoveAt(which);
                    continue;
                }

                Put(board, placement.Board);
                Connect(placement.Host, board, corridor);
                (int partner, Rect second) = partners[_rng.NextInt(partners.Count)];
                Connect(partner, board, second);
                reason = string.Empty;
                return true;
            }

            paired.RemoveAt(pick);
        }

        return Attach(board, out reason);
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

    /// <summary>在两块已摆好的棋盘之间画出通道并记账。</summary>
    private void Connect(int from, int to, Rect corridor)
    {
        for (int y = corridor.Y; y <= corridor.Y1; y++)
        {
            for (int x = corridor.X; x <= corridor.X1; x++)
            {
                Grid[x, y] = Corridor;
            }
        }

        Links.Add(new Link(from, to, corridor));
        _degree[from]++;
        _degree[to]++;
    }

    /// <summary>全部合法的摆放位置（宿主只取公共棋盘），按宿主下标 → 方向（东、西、北、南）→ 间隔 → 偏移的次序。</summary>
    private List<Placement> Placements(int board)
    {
        BoardSize size = _sizes[board];
        var found = new List<Placement>();
        for (int host = _births; host < _sizes.Length; host++)
        {
            if (!_placed[host] || _degree[host] >= PublicMaxLinks)
            {
                continue;
            }

            Rect h = Boards[host];
            for (int side = 0; side < 4; side++)
            {
                bool horizontal = side < 2;
                for (int gap = MinCorridorLength; gap <= MaxCorridorLength; gap++)
                {
                    // 沿宿主这条边滑动：投影至少重叠通道的最小宽度。
                    int span = horizontal ? size.Height : size.Width;
                    int hostLow = horizontal ? h.Y : h.X;
                    int hostHigh = horizontal ? h.Y1 : h.X1;
                    for (int low = hostLow - span + MinCorridorWidth; low + MinCorridorWidth - 1 <= hostHigh; low++)
                    {
                        Rect rect = side switch
                        {
                            0 => new Rect(h.X1 + 1 + gap, low, size.Width, size.Height),
                            1 => new Rect(h.X - gap - size.Width, low, size.Width, size.Height),
                            2 => new Rect(low, h.Y1 + 1 + gap, size.Width, size.Height),
                            _ => new Rect(low, h.Y - gap - size.Height, size.Width, size.Height),
                        };
                        if (!Fits(rect))
                        {
                            continue;
                        }

                        int overlapLow = low > hostLow ? low : hostLow;
                        int overlapHigh = low + span - 1 < hostHigh ? low + span - 1 : hostHigh;
                        found.Add(new Placement(host, rect, horizontal, overlapLow, overlapHigh));
                    }
                }
            }
        }

        return found;
    }

    /// <summary>
    /// 棋盘 <paramref name="board"/>（位于 <paramref name="rect"/>，可以尚未落格）能另开一条通道通向的全部已摆棋盘与通道位置：
    /// 对方不是 <paramref name="exclude"/>、尚未与之相连、通道数未满，与 <paramref name="rect"/> 正对（东西或南北相隔 2–4 格、投影重叠不小于通道最小宽度），
    /// 且通道放得下（<paramref name="pending"/> 是将要一起画上的另一条通道，视同已占）。按对方棋盘下标 → 通道宽 → 位置的次序。
    /// </summary>
    private List<(int Board, Rect Corridor)> Partners(int board, Rect rect, Rect? pending, int exclude = -1)
    {
        var found = new List<(int Board, Rect Corridor)>();
        for (int other = 0; other < _sizes.Length; other++)
        {
            if (other == board || other == exclude || !_placed[other] || _degree[other] >= MaxLinks(other) || Linked(board, other))
            {
                continue;
            }

            Rect q = Boards[other];
            bool horizontal;
            int gap;
            if (q.X > rect.X1)
            {
                horizontal = true;
                gap = q.X - rect.X1 - 1;
            }
            else if (rect.X > q.X1)
            {
                horizontal = true;
                gap = rect.X - q.X1 - 1;
            }
            else if (q.Y > rect.Y1)
            {
                horizontal = false;
                gap = q.Y - rect.Y1 - 1;
            }
            else
            {
                horizontal = false;
                gap = rect.Y - q.Y1 - 1;
            }

            if (gap < MinCorridorLength || gap > MaxCorridorLength)
            {
                continue;
            }

            int overlapLow = horizontal ? (q.Y > rect.Y ? q.Y : rect.Y) : (q.X > rect.X ? q.X : rect.X);
            int overlapHigh = horizontal ? (q.Y1 < rect.Y1 ? q.Y1 : rect.Y1) : (q.X1 < rect.X1 ? q.X1 : rect.X1);
            if (overlapHigh - overlapLow + 1 < MinCorridorWidth)
            {
                continue;
            }

            List<Rect> corridors = Corridors(new Placement(other, rect, horizontal, overlapLow, overlapHigh), pending);
            for (int i = 0; i < corridors.Count; i++)
            {
                found.Add((other, corridors[i]));
            }
        }

        return found;
    }

    private bool Linked(int a, int b)
    {
        for (int i = 0; i < Links.Count; i++)
        {
            if ((Links[i].From == a && Links[i].To == b) || (Links[i].From == b && Links[i].To == a))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>棋盘放得下：离外缘 ≥ 2 格；向四周各扩 2 格不碰任何棋盘；向四周各扩 1 格不碰任何通道。</summary>
    private bool Fits(Rect rect)
    {
        if (rect.X < EdgeMargin || rect.Y < EdgeMargin || rect.X1 > _size - 1 - EdgeMargin || rect.Y1 > _size - 1 - EdgeMargin)
        {
            return false;
        }

        Rect wide = rect.Grow(BoardGap);
        Rect near = rect.Grow(1);
        for (int y = wide.Y; y <= wide.Y1; y++)
        {
            for (int x = wide.X; x <= wide.X1; x++)
            {
                if (x < 0 || y < 0 || x >= _size || y >= _size)
                {
                    continue;
                }

                if (Grid[x, y] > 0 || (Grid[x, y] == Corridor && near.Contains(x, y)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>这个摆放位置下全部合法的通道，按宽度（3、4）→ 位置的次序。<paramref name="pending"/> 视同已占的格。</summary>
    private List<Rect> Corridors(Placement placement, Rect? pending)
    {
        Rect host = Boards[placement.Host];
        Rect board = placement.Board;
        var found = new List<Rect>();
        for (int width = MinCorridorWidth; width <= MaxCorridorWidth; width++)
        {
            for (int low = placement.OverlapLow; low + width - 1 <= placement.OverlapHigh; low++)
            {
                Rect corridor;
                if (placement.Horizontal)
                {
                    int x0 = (host.X1 < board.X ? host.X1 : board.X1) + 1;
                    int x1 = (host.X1 < board.X ? board.X : host.X) - 1;
                    corridor = new Rect(x0, low, x1 - x0 + 1, width);
                }
                else
                {
                    int y0 = (host.Y1 < board.Y ? host.Y1 : board.Y1) + 1;
                    int y1 = (host.Y1 < board.Y ? board.Y : host.Y) - 1;
                    corridor = new Rect(low, y0, width, y1 - y0 + 1);
                }

                if (CorridorFits(corridor, placement.Host, board, pending))
                {
                    found.Add(corridor);
                }
            }
        }

        return found;
    }

    /// <summary>
    /// 通道放得下：自身的格全是场景；向四周各扩 1 格（含对角）只碰得到场景、宿主棋盘与新棋盘——
    /// 不贴别的棋盘，也不与别的通道（含 <paramref name="pending"/>）相邻。
    /// </summary>
    private bool CorridorFits(Rect corridor, int host, Rect board, Rect? pending)
    {
        Rect near = corridor.Grow(1);
        for (int y = near.Y; y <= near.Y1; y++)
        {
            for (int x = near.X; x <= near.X1; x++)
            {
                if (x < 0 || y < 0 || x >= _size || y >= _size || board.Contains(x, y))
                {
                    continue;
                }

                if (pending is { } taken && taken.Contains(x, y))
                {
                    return false;
                }

                int cell = Grid[x, y];
                if (corridor.Contains(x, y) ? cell != 0 : cell != 0 && cell != host + 1)
                {
                    return false;
                }
            }
        }

        return true;
    }

    // ---------- 裁出图面 ----------

    /// <summary>
    /// 取全部棋盘与通道的实际外接范围，四周各留 <see cref="EdgeMargin"/> 格裁出图面，并把全部坐标平移到图面坐标系。
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

        for (int i = 0; i < Links.Count; i++)
        {
            Rect cells = Links[i].Cells;
            Links[i] = Links[i] with { Cells = cells with { X = cells.X - dx, Y = cells.Y - dy } };
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
            int want = (rect.W < rect.H ? rect.W : rect.H) <= SmallPublicSide ? 2 : 3;
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
