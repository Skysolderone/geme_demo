using Siege.Core.Determinism;

namespace Siege.Core.Board.Maps;

/// <summary>
/// 一次生成尝试的工作态与六步构造（map-generator D1）：摆平台 → 中央广场 → 缓坡 → 主河 → 走廊与桥 → 填充与点缀 → 布点 → 自检。
/// 按步拆成若干 partial 文件（<c>FrontierMapLayout.*.cs</c>）；本文件只放工作态与总流程。
/// </summary>
/// <remarks>
/// <para><b>次序</b>：主河先于走廊——河先在空地里流过去，走廊再在河道笔直处垂直地跨它（桥长 = 走廊宽 ≤ 3）；
/// 走廊全长至少 2 格宽，且没有"一枚子就能堵死"的口子（design 裁决 12）。</para>
/// <para><b>确定性</b>（<c>.trellis/spec/core/determinism.md</c>）：工作态全部是按 <c>[列, 行]</c> 下标的二维数组与按构造次序排列的列表，
/// 遍历一律"先行后列"的双循环或列表下标序；不使用任何按散列次序遍历的容器；随机只来自构造时传入的那一条序列；全程整数运算。
/// 候选集合先按坐标序收集成列表，再用随机数取下标——并列由坐标序打破。</para>
/// <para>任何一步走不下去（放不下平台、走廊不通、桥不足、预算超限……）就返回 <c>false</c> 并说明原因，由调用方换下一次尝试；不产出半张图。</para>
/// </remarks>
internal sealed partial class FrontierMapLayout
{
    internal const int W = 25;
    internal const int H = 30;

    /// <summary>平台离地图外缘至少 2 格。</summary>
    private const int EdgeMargin = 2;

    /// <summary>平台的外接方块两两至少隔 2 格。</summary>
    private const int PlatformGap = 2;

    /// <summary>可落子格总数的目标区间（边疆档预算 300–420 的中段）与硬上限。</summary>
    private const int TargetMin = 340;
    private const int TargetMax = 380;
    private const int BudgetMin = 300;
    private const int BudgetMax = 420;

    /// <summary>一座桥（相邻桥格连成的一块）沿河最多几格 = 走廊的最大宽度；桥格总数上限。</summary>
    internal const int MaxBridgeSpan = 3;
    internal const int MaxBridgeCells = 10;

    /// <summary>主河换端点 / 隘口重来的次数上限（每次只是一两趟最短路）。</summary>
    private const int RiverTries = 16;

    internal enum Cell : byte
    {
        /// <summary>尚未决定（只在构造中途出现）。</summary>
        Fill = 0,
        Rock,
        Water,
        Platform,
        Ramp,
        Ground,
        River,
        Bridge,
    }

    /// <summary>网格内的一格（不经 <see cref="Coord"/>：构造中途要算盘外的邻格）。</summary>
    internal readonly record struct P(int X, int Y);

    private enum Side
    {
        Left = 0,
        Right = 1,
        Bottom = 2,
        Top = 3,
    }

    private sealed record Ramp(int Platform, Side Side, P[] Cells, P[] Mouth);

    private static readonly P[] Dirs = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];

    private readonly RandomStream _rng;
    private readonly int _n;
    private readonly List<Ramp> _ramps = [];

    /// <summary>主河的河道（从入图到出图的次序）与各格在河道上的序号 + 1（0 = 不在河上）。</summary>
    private List<P> _river = [];
    private readonly int[,] _riverIndex = new int[W, H];

    /// <summary>走廊网的结点（0 = 中央入口，其余 = 各缓坡口）与结点所属平台（中央入口为 −1）。</summary>
    private readonly List<P> _nodes = [];
    private readonly List<int> _nodeOwner = [];
    private int[] _sides = [];
    private bool _bandsVertical;
    private int _cx;
    private int _cy;
    private Side[] _smallFacing = [];
    private int[] _smallGap = [];

    internal FrontierMapLayout(RandomStream rng, int platformCount)
    {
        _rng = rng;
        _n = platformCount;
        Platforms = new PlatformRect[platformCount];
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                Zone[x, y] = -1;
            }
        }
    }

    internal Cell[,] Cells { get; } = new Cell[W, H];

    internal int[,] Zone { get; } = new int[W, H];

    internal bool[,] Plaza { get; } = new bool[W, H];

    internal bool[,] Forest { get; } = new bool[W, H];

    internal SiteTier?[,] Sites { get; } = new SiteTier?[W, H];

    internal RelicCellSpec?[,] Relics { get; } = new RelicCellSpec?[W, H];

    internal List<(P A, P B)> Fences { get; } = [];

    internal PlatformRect[] Platforms { get; }

    internal P Entrance => new(_cx, _cy);

    internal bool TryBuild(out string reason) =>
        PlacePlatforms(out reason)
        && CutRamps(out reason)
        && RunRiver(out reason)
        && CarveCorridors(out reason)
        && EnsureBridges(out reason)
        && WidenNarrowCorridors(out reason)
        && FillAndDecorate(out reason)
        && PlaceSitesAndRelics(out reason)
        && CheckSingleRegion(out reason)
        && CheckCorridorWidth(out reason);
}
