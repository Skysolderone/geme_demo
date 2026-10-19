using System.Collections.Immutable;

namespace Siege.Core.Board.Maps;

/// <summary>
/// 建局布局的纯几何（open-map design D1 / 实施裁决 A1 / A2）：矩形贴边、共享边长度、重叠排除。
/// 5 块出生棋盘按种子决定的次序 o0…o4 一次性边对边紧贴成一张连通地图（3+2 两行），每块以一块具体的棋盘为锚：
/// 第一行 o0 / o1 / o2 底边对齐、间距 0 自西向东紧贴；o3 贴 o0 北侧（共享边最长、偏西即西对齐）；o4 贴 o1 北侧（共享边最长、不与已放下的棋盘重叠、偏西）。
/// 布局不消费随机；画布原点固定在 (0,0)，四周留白由调用方的 <c>margin</c> 给出。
/// 现行拼接几何（<see cref="LinkMapGenerator.Link"/>）段 A 不动、段 B 删除，不强求二者共用（A2）。
/// </summary>
public static class InitialLayout
{
    /// <summary>每块第二行棋盘与其锚之间的共享边下限（规格 link-map-generation「初始布局」：MUST ≥ 4）。</summary>
    public const int MinSharedEdge = 4;

    /// <summary>两块外接矩形是否有公共格。</summary>
    public static bool Overlaps(BoardPlate a, BoardPlate b) =>
        a.Origin.X < b.Origin.X + b.Width && b.Origin.X < a.Origin.X + a.Width
        && a.Origin.Y < b.Origin.Y + b.Height && b.Origin.Y < a.Origin.Y + a.Height;

    /// <summary>
    /// 两块外接矩形边对边相触时的共享边长度：隔着接缝互为几何邻居的格对数（外圈全部可落子时即接缝两侧都有可落子格的格数）。
    /// 不相触（含只在角上相触）或重叠为 0。
    /// </summary>
    public static int SharedEdge(BoardPlate a, BoardPlate b)
    {
        if (a.Origin.X + a.Width == b.Origin.X || b.Origin.X + b.Width == a.Origin.X)
        {
            return Math.Max(0, Math.Min(a.Origin.Y + a.Height, b.Origin.Y + b.Height) - Math.Max(a.Origin.Y, b.Origin.Y));
        }

        if (a.Origin.Y + a.Height == b.Origin.Y || b.Origin.Y + b.Height == a.Origin.Y)
        {
            return Math.Max(0, Math.Min(a.Origin.X + a.Width, b.Origin.X + b.Width) - Math.Max(a.Origin.X, b.Origin.X));
        }

        return 0;
    }

    /// <summary>
    /// 把 <paramref name="plate"/>（只用其宽高、类别与主题）贴到 <paramref name="anchor"/> 北侧：最南行 = 锚的最北行 + 1；
    /// 列偏移在"与锚共享边最长、最西列不小于 <paramref name="minX"/>、不与 <paramref name="placed"/> 中任一块重叠"的候选里取偏西者。
    /// 没有任何这样的候选（已放下的棋盘把锚的北侧全部挡住）时返回 <c>null</c>，由 <see cref="Arrange"/> 换锚。
    /// </summary>
    public static BoardPlate? AttachNorth(BoardPlate anchor, BoardPlate plate, IReadOnlyCollection<BoardPlate> placed, int minX)
    {
        ArgumentNullException.ThrowIfNull(placed);
        int y = anchor.Origin.Y + anchor.Height;
        BoardPlate? best = null;
        int bestShared = 0;
        for (int x = anchor.Origin.X - plate.Width + 1; x <= anchor.Origin.X + anchor.Width - 1; x++)
        {
            if (x < minX)
            {
                continue;
            }

            BoardPlate candidate = plate with { Origin = new Coord(x, y) };
            if (placed.Any(p => Overlaps(p, candidate)))
            {
                continue;
            }

            int shared = SharedEdge(anchor, candidate);
            if (shared > bestShared)
            {
                best = candidate;
                bestShared = shared;
            }
        }

        return best;
    }

    /// <summary>
    /// 按 D1 摆放 5 块棋盘：<paramref name="ordered"/> 为种子次序 o0…o4（只用宽高、类别与主题，原点忽略），返回同次序的已定位棋盘。
    /// o0 的左下角在 (<paramref name="margin"/>, <paramref name="margin"/>)；o1 / o2 底边对齐东贴；o3 锚 o0 北贴、o4 锚 o1 北贴（<see cref="AttachNorth"/>）。
    /// 锚的北侧被已放下的棋盘挡死或只剩不足 <see cref="MinSharedEdge"/> 格的缝（o3 比 o0 宽且 o1 比 o0 高；o4 比 o1 宽且 o2 比 o1 高 / 被 o3 挡住）时，
    /// 改锚第一行三块里共享边最长的那块（并列取编号小者）——布局仍是 3+2 两行、每块都北贴第一行的一块，不出现塔（open-map 段 A 实施记录 R-1）。
    /// 三个锚都挡死在 6–8 的边长区间内不会发生（种子 1–2000 实测为 0），真发生即抛 <see cref="MapGenerationException"/>（生成器缺陷，MUST NOT 静默继续）。
    /// </summary>
    public static ImmutableArray<BoardPlate> Arrange(ImmutableArray<BoardPlate> ordered, int margin)
    {
        if (ordered.Length != 5)
        {
            throw new ArgumentException($"3+2 布局恰需 5 块棋盘，实际 {ordered.Length} 块。", nameof(ordered));
        }

        var placed = new List<BoardPlate>(5);
        int x = margin;
        for (int k = 0; k < 3; k++)
        {
            BoardPlate p = ordered[k] with { Origin = new Coord(x, margin) };
            placed.Add(p);
            x += p.Width;
        }

        placed.Add(AttachToFirstRow(ordered[3], 0, placed, margin));
        placed.Add(AttachToFirstRow(ordered[4], 1, placed, margin));
        return [.. placed];
    }

    /// <summary>
    /// 先试 <paramref name="primary"/> 锚：放得下且共享边 ≥ <see cref="MinSharedEdge"/> 即取它；否则在第一行三块里取共享边最长者（并列取编号小者）。
    /// </summary>
    private static BoardPlate AttachToFirstRow(BoardPlate plate, int primary, List<BoardPlate> placed, int margin)
    {
        if (AttachNorth(placed[primary], plate, placed, margin) is { } fitted && SharedEdge(placed[primary], fitted) >= MinSharedEdge)
        {
            return fitted;
        }

        BoardPlate? best = null;
        int bestShared = 0;
        for (int anchor = 0; anchor < 3; anchor++)
        {
            if (AttachNorth(placed[anchor], plate, placed, margin) is { } other && SharedEdge(placed[anchor], other) > bestShared)
            {
                best = other;
                bestShared = SharedEdge(placed[anchor], other);
            }
        }

        if (best is null || bestShared < MinSharedEdge)
        {
            // 规格「初始布局」：每块与其锚之间的共享边 MUST ≥ 4。种子 1–2000 实测换锚后共享边全部 ≥ 4，这里只是把规格写成硬守卫（不改任何种子的摆位）。
            throw new MapGenerationException($"{plate.Width}×{plate.Height} 的棋盘在第一行三块的北侧都放不下或共享边不足 {MinSharedEdge}（最长 {bestShared}）（生成器缺陷）。");
        }

        return best.Value;
    }
}
