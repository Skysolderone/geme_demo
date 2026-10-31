using System.Collections.Immutable;
using Siege.Core.Determinism;

namespace Siege.Core.Board.Maps;

/// <summary>
/// 链接档浮空岛的岸线与内陆要素（map-coastline，负责人 2026-10-31 裁决 1–3）：岛形不再是"矩形切四角"，而是一张<b>岛形掩码</b>——矩形先切四角（直角边 4–7），
/// 再沿四条边按子流咬出一个个海湾（向内 1–4 格、沿边 3–9 格、轮廓带整数噪声）；掩码之外都是虚空（障碍）。内陆另放湖泊（未架桥深水团 4–9 格）与岩区（岩石团 3–6 格），
/// 河道加宽到 1–2 格，直到非可落子格占岛面（= 掩码内格数）约三成。全部随机只来自岸线流 <see cref="MapRandom.ForLinkCoast"/>，整数运算，不读时钟与环境。
/// </summary>
/// <remarks>
/// <para><b>先落平台、后啃岸线</b>：平台落位 / 主题 / 平台内部 / 坡道都在布局流上按矩形算（<see cref="TryLayout"/>），岸线在其后只读岸线流——布局流的消费顺序一步不动，
/// 同种子的平台清单与平台内地形与引入岸线之前逐格相同；掩码每次布局只啃一次，低地重抽只重抽填充。</para>
/// <para><b>保护区</b>（裁决 1）：座位外接矩形 + 外 <see cref="EdgeGap"/> 格、中立平台外接矩形 + 外 <see cref="PlateGap"/> 格、坡道朝外格，海湾一格都不得咬进去；
/// 内陆要素（湖泊 / 岩区）避开平台格、坡道朝外格与座位外 <see cref="EdgeGap"/> 格那一圈（map-coastline 裁决 F1：出生区周边保持可落子、开局有地可下）；
/// 中立平台外 <see cref="PlateGap"/> 格那一圈对它们不设防（连这一圈一起禁，4 人图的低地不够填到目标占比，生成失败）。候选变少，投放尝试上限随之由 1500 提到 3000（见 <see cref="MaxClusterAttempts"/>），耗时验收见裁决 F2。海湾只从岛缘向内逐列连续咬，咬掉的格必然与岛外连通，岛内不会出现虚空；掩码另做 4 邻连通自检（<see cref="Battlefields"/>，几何四邻分量的唯一实现），
/// 不通即在同一条流上重咬，仍不通退到只切角。</para>
/// <para><b>切角与座位角的几何</b>（实施修订）：座位贴 <see cref="EdgeGap"/> 落在矩形角，座位角格到矩形角的曼哈顿距离恰为 4，切角 ≥ 5 就会咬掉座位格；
/// 原固定切角 3 本就切进了座位 2 格边距环的角块。所以有平台（座位或贴边中立平台）占住的角切角封顶在不碰平台格与坡道朝外格的最大值（座位角恒为 4），
/// 无平台的角才在 4–7 里抽；边距环本身不对切角设防，只对海湾设防。</para>
/// <para><b>外接矩形</b>：校验器 <c>ISLAND_EXTENT</c> 量的是可落子格外接矩形 ≥ W×H，所以每条边另随机保留一段 <see cref="ReservedShore"/> 格的原岸线不咬
/// （座位边距环贴边的边本就有保留段，2 人图的南北边靠它）。</para>
/// <para><b>湖泊 / 岩区不破坏连通</b>：一团只放在"切比雪夫距离 1 的一圈陆地格仍沿几何四邻连成一环"的位置——任何穿过这一团的路都能改走环上，去掉它不改变其余可落子格的连通；
/// 这是局部判定，不必每放一团就整图泛洪；低地收尾仍做一次全图气边连通自检（<see cref="MapValidator.LibertySplit"/>）。</para>
/// </remarks>
public static partial class LinkMapGenerator
{
    /// <summary>岛四角切去的直角三角的直角边长区间（裁决 1：原固定 3 改为 4–7 随机；有平台占住的角封顶，见类注释）。</summary>
    public const int CornerCutMin = 4;
    public const int CornerCutMax = 7;

    /// <summary>海湾向内咬的深度区间与沿边长度区间（裁决 1）。</summary>
    public const int BayMinDepth = 1;
    public const int BayMaxDepth = 4;
    public const int BayMinLength = 3;
    public const int BayMaxLength = 9;

    /// <summary>相邻两个海湾之间保留的原岸线格数区间。</summary>
    internal const int BayGapMin = 1;
    internal const int BayGapMax = 3;

    /// <summary>每条边随机保留、不咬的一段原岸线长度（保住可落子格外接矩形 = W×H）。</summary>
    internal const int ReservedShore = 3;

    /// <summary>掩码 4 邻不连通时在同一条岸线流上重咬的最多次数；仍不通退到只切角。</summary>
    internal const int MaxCoastRedraws = 8;

    /// <summary>湖泊（未架桥深水团）与岩区（岩石团）的格数区间（裁决 3）。</summary>
    public const int LakeMin = 4;
    public const int LakeMax = 9;
    public const int RockClusterMin = 3;
    public const int RockClusterMax = 6;

    /// <summary>
    /// 非可落子格占岛面的目标区间（裁决 2：30% ± 5；每张图在 27–29 里抽一个目标百分比，填到不低于目标为止）与硬上限。
    /// 实测湖泊 / 岩区在"同类不相接、四周陆地成环"的约束下最多填到约 31%，目标再往上就要靠大量重抽低地凑，所以目标带取在饱和点之下。
    /// </summary>
    public const int TargetNonPlayableMin = 27;
    public const int TargetNonPlayableMax = 29;
    public const int MaxNonPlayablePercent = 35;

    /// <summary>
    /// 一次低地尝试里湖泊 / 岩区的最多投放尝试次数（含长不够、破坏连通而放弃的）；用完仍不到目标即重抽低地。
    /// map-coastline 裁决 F1：座位外 2 格不放团后候选变少，原 1500 次时 4 人图种子 1–20 有 15 张因投放尝试用完重抽低地、单张最多 18 次；
    /// 放到 3000 后投放不再是重抽原因（剩下的重抽全是河道画不通，F1 之前就有），最多 6 次；再加到 6000 重抽次数不再下降，只多耗在饱和的尝试上。
    /// </summary>
    internal const int MaxClusterAttempts = 3000;

    /// <summary>岸线流在一次布局里的消费顺序：四角切角 → 四条边各一段保留岸线 → 逐边逐湾（间隔 → 长度 → 深度 → 逐列噪声）；掩码不通即整段重来。</summary>
    private static Island Carve(Island rect, PlateLayout layout, RandomStream coast)
    {
        bool[] guarded = GuardedCells(rect, layout);
        for (int attempt = 0; attempt <= MaxCoastRedraws; attempt++)
        {
            Island island = DraftCoast(rect, layout, guarded, coast, bays: true);
            if (Battlefields.Of(island.Preview()).Count == 1)
            {
                return island;
            }
        }

        return DraftCoast(rect, layout, guarded, coast, bays: false);
    }

    /// <summary>保护区（裁决 1）：座位 + 外 <see cref="EdgeGap"/>、中立平台 + 外 <see cref="PlateGap"/>、坡道朝外格；按岛矩形索引。</summary>
    private static bool[] GuardedCells(Island rect, PlateLayout layout)
    {
        var guarded = new bool[rect.Width * rect.Height];
        foreach (BoardPlate plate in layout.Plates)
        {
            int gap = plate.Kind == BoardPlateKind.Birth ? EdgeGap : PlateGap;
            for (int y = plate.Origin.Y - gap; y < plate.Origin.Y + plate.Height + gap; y++)
            {
                for (int x = plate.Origin.X - gap; x < plate.Origin.X + plate.Width + gap; x++)
                {
                    if (x >= 0 && y >= 0 && rect.InRectangle(new Coord(x, y)))
                    {
                        guarded[rect.Index(new Coord(x, y))] = true;
                    }
                }
            }
        }

        foreach (Coord outlet in layout.RampOutlets)
        {
            guarded[rect.Index(outlet)] = true;
        }

        return guarded;
    }

    private static Island DraftCoast(Island rect, PlateLayout layout, bool[] guarded, RandomStream coast, bool bays)
    {
        int w = rect.Width, h = rect.Height;
        var mask = new bool[w * h];
        Array.Fill(mask, true);

        // ① 四角切角：先抽 4–7，再封顶到不碰任何平台格与坡道朝外格（座位角恒为 4）。角的次序：西南、东南、西北、东北。
        var cuts = ImmutableArray.CreateBuilder<int>(4);
        for (int corner = 0; corner < 4; corner++)
        {
            int k = CornerCutMin + coast.NextInt(CornerCutMax - CornerCutMin + 1);
            int cap = CornerCutMax;
            foreach (Coord c in layout.Plates.SelectMany(p => p.Cells()).Concat(layout.RampOutlets))
            {
                cap = Math.Min(cap, CornerDistance(rect, corner, c));
            }

            k = Math.Min(k, cap);
            cuts.Add(k);
            foreach (Coord c in rect.Cells())
            {
                if (CornerDistance(rect, corner, c) < k)
                {
                    mask[rect.Index(c)] = false;
                }
            }
        }

        int bitten = 0;
        if (bays)
        {
            // ② 每条边保留一段原岸线（在两端切角之外抽起点）。边的次序：南、北、西、东；边上第 t 列、向内第 j 格见 ShoreCell。
            var reserved = new bool[4][];
            for (int edge = 0; edge < 4; edge++)
            {
                int length = edge < 2 ? w : h;
                (int cutA, int cutB) = edge switch { 0 => (cuts[0], cuts[1]), 1 => (cuts[2], cuts[3]), 2 => (cuts[0], cuts[2]), _ => (cuts[1], cuts[3]) };
                reserved[edge] = new bool[length];
                int start = cutA + coast.NextInt(length - cutA - cutB - ReservedShore + 1);
                for (int t = start; t < start + ReservedShore; t++)
                {
                    reserved[edge][t] = true;
                }
            }

            // ③ 逐边咬湾：沿边扫出"可咬的连续段"（岸格在掩码内、不在保护区、不在保留段），段内依次 间隔 → 湾长 → 湾深 → 逐列（两端渐浅 + 噪声 ±1）。
            for (int edge = 0; edge < 4; edge++)
            {
                int length = edge < 2 ? w : h;
                var depthCap = new int[length];
                for (int t = 0; t < length; t++)
                {
                    if (reserved[edge][t])
                    {
                        continue;
                    }

                    int d = 0;
                    while (d < BayMaxDepth)
                    {
                        int index = rect.Index(ShoreCell(rect, edge, t, d + 1));
                        if (!mask[index] || guarded[index])
                        {
                            break;
                        }

                        d++;
                    }

                    depthCap[t] = d;
                }

                int pos = 0;
                while (pos < length)
                {
                    if (depthCap[pos] == 0)
                    {
                        pos++;
                        continue;
                    }

                    int runEnd = pos;
                    while (runEnd < length && depthCap[runEnd] > 0)
                    {
                        runEnd++;
                    }

                    int cursor = pos;
                    while (runEnd - cursor >= BayGapMin + BayMinLength)
                    {
                        int gap = BayGapMin + coast.NextInt(BayGapMax - BayGapMin + 1);
                        int bayLength = BayMinLength + coast.NextInt(BayMaxLength - BayMinLength + 1);
                        int bayDepth = BayMinDepth + coast.NextInt(BayMaxDepth - BayMinDepth + 1);
                        int start = cursor + gap;
                        if (start + BayMinLength > runEnd)
                        {
                            break;
                        }

                        int end = Math.Min(start + bayLength, runEnd);
                        for (int t = start; t < end; t++)
                        {
                            int toEnd = Math.Min(t - start, end - 1 - t);
                            int noise = coast.NextInt(4) switch { 0 => -1, 3 => 1, _ => 0 };
                            int depth = Math.Clamp(Math.Min(bayDepth, toEnd + 1) + noise, 0, depthCap[t]);
                            for (int j = 1; j <= depth; j++)
                            {
                                int index = rect.Index(ShoreCell(rect, edge, t, j));
                                if (mask[index])
                                {
                                    mask[index] = false;
                                    bitten++;
                                }
                            }
                        }

                        cursor = end;
                    }

                    pos = runEnd;
                }
            }
        }

        return Island.FromMask(w, h, mask, cuts.ToImmutable(), bitten);
    }

    /// <summary>格到第 <paramref name="corner"/> 角（西南 0、东南 1、西北 2、东北 3）两条边的距离之和（切角判据：小于直角边即切掉）。</summary>
    private static int CornerDistance(Island rect, int corner, Coord c)
    {
        int dx = corner is 0 or 2 ? c.X - Margin : Margin + rect.Width - 1 - c.X;
        int dy = corner is 0 or 1 ? c.Y - Margin : Margin + rect.Height - 1 - c.Y;
        return dx + dy;
    }

    /// <summary>第 <paramref name="edge"/> 条边（南 0、北 1、西 2、东 3）上沿边第 <paramref name="t"/> 列、从岸向内第 <paramref name="depth"/> 格（1 即岸格）。</summary>
    private static Coord ShoreCell(Island rect, int edge, int t, int depth) => edge switch
    {
        0 => new Coord(Margin + t, Margin + depth - 1),
        1 => new Coord(Margin + t, Margin + rect.Height - depth),
        2 => new Coord(Margin + depth - 1, Margin + t),
        _ => new Coord(Margin + rect.Width - depth, Margin + t),
    };

    /// <summary>
    /// 内陆湖泊与岩区（裁决 2 / 3）：目标百分比在 <see cref="TargetNonPlayableMin"/>–<see cref="TargetNonPlayableMax"/> 里抽一个，然后逐团投放（种类 → 格数 → 起点 → 逐格生长）直到非可落子格不低于目标。
    /// 候选格：低地（岛上、非平台、非坡道朝外格、不在座位外 <see cref="EdgeGap"/> 格那一圈——裁决 F1）、不在岛缘、不是水 / 岩石 / 已有团、且不与河道（含桥）/ 湖泊 / 岩区四邻相邻（团与团、团与河道互不相接，桥的两岸也不会被堵）。
    /// 每团放下前查"切比雪夫距离 1 的一圈陆地格沿四邻连成一环"，不成环即放弃这一团（见类注释）。候选用尽或尝试用完仍不到目标返回 <c>false</c>（调用方重抽低地）。
    /// </summary>
    private static bool PlaceClusters(
        Island island,
        PlateLayout layout,
        HashSet<Coord> water,
        HashSet<Coord> bridges,
        HashSet<Coord> lakes,
        HashSet<Coord> rockClusters,
        int fixedNonPlayable,
        RandomStream coast)
    {
        int face = island.Count;
        int target = TargetNonPlayableMin + coast.NextInt(TargetNonPlayableMax - TargetNonPlayableMin + 1);
        int nonPlayable = fixedNonPlayable + water.Count - bridges.Count;

        // map-coastline 裁决 F1：座位外接矩形外 EdgeGap 格那一圈不放湖泊 / 岩区（座位格本身已由 InPlate 排除）——出生区四周留成可落子地，开局不被水 / 岩堵在门口。
        // 只排座位这一圈：中立平台外 PlateGap 格那一圈允许（一起禁则 4 人图低地不够填到目标占比）。
        var seatRing = new HashSet<Coord>();
        foreach (BoardPlate seat in layout.Plates.Where(p => p.Kind == BoardPlateKind.Birth))
        {
            for (int y = seat.Origin.Y - EdgeGap; y < seat.Origin.Y + seat.Height + EdgeGap; y++)
            {
                for (int x = seat.Origin.X - EdgeGap; x < seat.Origin.X + seat.Width + EdgeGap; x++)
                {
                    seatRing.Add(new Coord(x, y));
                }
            }
        }

        HashSet<Coord> lowland = [.. island.Cells().Where(c => !layout.InPlate(c) && !layout.RampOutlets.Contains(c) && !seatRing.Contains(c))];

        bool Occupied(Coord c) => water.Contains(c) || lakes.Contains(c) || rockClusters.Contains(c);
        bool Land(Coord c) => island.Contains(c) && !layout.InPlate(c) && !lakes.Contains(c) && !rockClusters.Contains(c) && (!water.Contains(c) || bridges.Contains(c));

        // 同类团之间与河道之间不得四邻相接（湖泊挨湖泊会并成一个大湖、挨河道会并进河道，岩区挨岩区同理）；湖泊与岩区之间可以相接（水团与岩团各自按同类分团计数）。
        bool Eligible(Coord c, HashSet<Coord> current, HashSet<Coord> sameKind) =>
            lowland.Contains(c) && !island.OnEdge(c) && !Occupied(c) && !current.Contains(c)
            && Directions.All(d => !water.Contains(Step(c, d, 1)) && !sameKind.Contains(Step(c, d, 1)));

        // 候选起点只在放下一团之后才变，失败的尝试（长不够、不成环）不重算。
        List<Coord>? lakeSeeds = null;
        List<Coord>? rockSeeds = null;
        for (int attempt = 0; attempt < MaxClusterAttempts && nonPlayable * 100 < target * face; attempt++)
        {
            bool lake = coast.NextInt(2) == 0;
            int size = lake ? LakeMin + coast.NextInt(LakeMax - LakeMin + 1) : RockClusterMin + coast.NextInt(RockClusterMax - RockClusterMin + 1);
            HashSet<Coord> sameKind = lake ? lakes : rockClusters;
            var current = new HashSet<Coord>();
            List<Coord> seeds = (lake ? lakeSeeds : rockSeeds) ?? [.. island.Cells().Where(c => Eligible(c, current, sameKind))];
            if (lake)
            {
                lakeSeeds = seeds;
            }
            else
            {
                rockSeeds = seeds;
            }

            if (seeds.Count == 0)
            {
                return false;
            }

            var cluster = new List<Coord> { seeds[coast.NextInt(seeds.Count)] };
            current.Add(cluster[0]);
            while (cluster.Count < size)
            {
                List<Coord> grow = [.. cluster.SelectMany(t => Directions.Select(d => Step(t, d, 1))).Distinct().Where(c => Eligible(c, current, sameKind)).Order()];
                if (grow.Count == 0)
                {
                    break;
                }

                Coord next = grow[coast.NextInt(grow.Count)];
                cluster.Add(next);
                current.Add(next);
            }

            if (cluster.Count < (lake ? LakeMin : RockClusterMin) || !RingConnected(current, Land))
            {
                continue;
            }

            sameKind.UnionWith(cluster);
            nonPlayable += cluster.Count;
            lakeSeeds = null;
            rockSeeds = null;
        }

        return nonPlayable * 100 >= target * face && nonPlayable * 100 <= MaxNonPlayablePercent * face;
    }

    /// <summary>这一团四周切比雪夫距离 1 的陆地格是否沿几何四邻连成一片（非空）。</summary>
    private static bool RingConnected(HashSet<Coord> cluster, Func<Coord, bool> land)
    {
        var ring = new List<Coord>();
        foreach (Coord c in cluster.Order())
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    var n = new Coord(c.X + dx, c.Y + dy);
                    if (!cluster.Contains(n) && land(n) && !ring.Contains(n))
                    {
                        ring.Add(n);
                    }
                }
            }
        }

        if (ring.Count == 0)
        {
            return false;
        }

        var reached = new HashSet<Coord> { ring[0] };
        var queue = new Queue<Coord>(reached);
        while (queue.Count > 0)
        {
            Coord here = queue.Dequeue();
            foreach (Coord n in ring)
            {
                if (!reached.Contains(n) && Adjacency.AreAdjacent(here, n))
                {
                    reached.Add(n);
                    queue.Enqueue(n);
                }
            }
        }

        return reached.Count == ring.Count;
    }
}
