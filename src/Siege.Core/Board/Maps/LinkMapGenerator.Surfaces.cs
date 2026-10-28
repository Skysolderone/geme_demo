using System.Collections.Immutable;
using Siege.Core.Determinism;

namespace Siege.Core.Board.Maps;

/// <summary>
/// 链接档平台（座位与中立平台，seat-gap-25 E2）的新地表叠加（link-map-surfaces D1–D3，负责人 2026-10-20 裁决 E1–E5，实施裁决 S1–S4）：平台主题地形与低地全部生成之后，
/// 用独立子流 <see cref="MapRandom.ForLinkSurfaces"/> 按平台编号逐块把若干中间带草地改为荒漠 / 沼泽 / 岩台 / 浅滩。
/// 只改各块 <see cref="BoardTerrain.Surfaces"/> 里若干格的取值，不碰高度、林地、深水、桥、栅栏与岩石，也不消费布局流——不含新地表的部分与叠加前逐格相同（E4）。
/// </summary>
/// <remarks>
/// <para><b>基础候选</b>（D2 / S1）：该块中间带里高度 0、地表草地、非岩石、且不与核心区任一格几何相邻的格（核心区四邻不放，保证起手条件不因地表而不对等）；
/// 中间带 ≤ <see cref="SmallMiddleBand"/> 格的小棋盘免除"不与核心区相邻"这一条（实施后裁决 F1）。</para>
/// <para><b>主题配对</b>（E1 / D2 表）：河流 → 浅滩（与深水格相邻，未架桥与桥格同为深水地表）；林地 → 沼泽（与林地格相邻）；
/// 栅栏阵 → 荒漠（至少一条边有栅栏，或与这样的格相邻）；混合 → 四种按 浅滩 → 岩台 → 沼泽 → 荒漠（S4）各至多 1 格，某种按条件找不到时从基础候选补该种（实施后裁决 F2）。</para>
/// <para><b>数量</b>（D3 / S3）：中间带 ≤ <see cref="SmallMiddleBand"/> 格时目标 2，否则单一地表的主题在 2–3、混合在 2–4 里均匀抽（先抽目标，再抽格，消费顺序与地形无关）。
/// 单一地表的主题：主题候选抽完退到基础候选补足同一地表，基础候选也抽完即少放（S2），不重抽。候选按坐标字典序排好后不放回地抽。</para>
/// <para><b>邻接</b>：只用 <see cref="Adjacency.AreAdjacent"/> 判两格是否几何相邻，不自己枚举四邻。</para>
/// </remarks>
public static partial class LinkMapGenerator
{
    /// <summary>每块平台（座位与中立平台）新地表格数的上限（D4；校验器链接行 <c>MaxSpecialSurfaces</c> 同值）。混合主题的目标上限即此值。</summary>
    internal const int MaxSpecialSurfacesPerBoard = 4;

    /// <summary>中间带格数不超过此值时目标格数固定为 2（D3 / S3：6×6 的 7 格、6×7 的 11 格）。</summary>
    internal const int SmallMiddleBand = 12;

    /// <summary>混合主题的尝试次序（S4）。</summary>
    private static readonly ImmutableArray<Surface> MixedOrder = [Surface.Shallows, Surface.Crag, Surface.Marsh, Surface.Desert];

    /// <summary>
    /// 按编号逐块叠加新地表（D1），返回改写后的各块地形（<see cref="BoardTerrain.SpecialSurfaces"/> 记实际放下的格数）。
    /// </summary>
    internal static BoardTerrain[] OverlaySurfaces(IReadOnlyList<BoardTerrain> boards, RandomStream rng)
    {
        var result = new BoardTerrain[boards.Count];
        for (int zone = 0; zone < boards.Count; zone++)
        {
            result[zone] = OverlayBoard(boards[zone], rng);
        }

        return result;
    }

    /// <summary>主题对应的单一地表（混合主题没有单一地表，见 <see cref="MixedOrder"/>）。</summary>
    private static Surface SurfaceOf(BoardTheme theme) => theme switch
    {
        BoardTheme.River => Surface.Shallows,
        BoardTheme.Forest => Surface.Marsh,
        BoardTheme.Palisade => Surface.Desert,
        _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, "混合主题没有单一的新地表。"),
    };

    private static BoardTerrain OverlayBoard(BoardTerrain board, RandomStream rng)
    {
        BoardPlate plate = board.Plate;
        BoardTheme theme = plate.Theme ?? throw new ArgumentException($"平台 {plate.Origin.ToNotation()} 没有主题，无法投放新地表。", nameof(board));
        List<Coord> middle = [.. plate.Cells().Where(plate.InMiddleBand)];
        bool small = middle.Count <= SmallMiddleBand;   // "小棋盘"的唯一判断：S3 的目标格数与 F1 的免除共用
        int most = theme == BoardTheme.Mixed ? MaxSpecialSurfacesPerBoard : 3;
        int target = small ? 2 : 2 + rng.NextInt(most - 2 + 1);

        // F1：中间带 ≤ SmallMiddleBand 的小棋盘（6×6 / 6×7）免除 S1——否则 6×6 只有 1 格不贴核心区，放不到目标数。
        Coord[] core = small ? [] : [.. plate.CoreCells()];
        List<Coord> basis = [.. middle.Where(c => IsBaseCandidate(board, core, c)).Order()];
        var placed = new Dictionary<Coord, Surface>();
        if (theme == BoardTheme.Mixed)
        {
            foreach (Surface surface in MixedOrder)
            {
                if (placed.Count >= target)
                {
                    break;
                }

                List<Coord> pool = [.. basis.Where(c => !placed.ContainsKey(c) && Fits(board, surface, c))];
                if (pool.Count == 0)
                {
                    pool = [.. basis.Where(c => !placed.ContainsKey(c))];   // F2：该种的主题候选找不到，从基础候选补该种（仍至多 1 格）
                }

                if (pool.Count > 0)
                {
                    placed[pool[rng.NextInt(pool.Count)]] = surface;
                }
            }
        }
        else
        {
            Surface surface = SurfaceOf(theme);
            while (placed.Count < target)
            {
                List<Coord> pool = [.. basis.Where(c => !placed.ContainsKey(c) && Fits(board, surface, c))];
                if (pool.Count == 0)
                {
                    pool = [.. basis.Where(c => !placed.ContainsKey(c))];   // S2：主题候选不足，退到基础候选补足同一地表
                }

                if (pool.Count == 0)
                {
                    break;   // S2：基础候选也不足，少放，不重抽
                }

                placed[pool[rng.NextInt(pool.Count)]] = surface;
            }
        }

        return board with { Surfaces = board.Surfaces.SetItems(placed), SpecialSurfaces = placed.Count };
    }

    /// <summary>基础候选（D2 / S1）：中间带、高度 0、草地（地表表里没有这一格或标为草地）、非岩石、不与 <paramref name="core"/> 任一格几何相邻（F1：小棋盘传空数组）。</summary>
    private static bool IsBaseCandidate(BoardTerrain board, Coord[] core, Coord c) =>
        board.Plate.InMiddleBand(c)
        && board.Heights.GetValueOrDefault(c) == 0
        && (!board.Surfaces.TryGetValue(c, out Surface s) || s == Surface.Grass)
        && !board.Rocks.Contains(c)
        && !board.Bridges.Contains(c)
        && !core.Any(k => Adjacency.AreAdjacent(k, c));

    /// <summary>主题候选的条件（D2 表）：只读叠加前的地形（深水 / 高度 / 林地 / 栅栏），已放下的新地表不改变任何一格的条件。</summary>
    private static bool Fits(BoardTerrain board, Surface surface, Coord c) => surface switch
    {
        Surface.Shallows => board.Surfaces.Any(kv => kv.Value == Surface.DeepWater && Adjacency.AreAdjacent(kv.Key, c)),
        Surface.Crag => board.Heights.Any(kv => kv.Value >= 1 && Adjacency.AreAdjacent(kv.Key, c)),
        Surface.Marsh => board.Surfaces.Any(kv => kv.Value == Surface.Forest && Adjacency.AreAdjacent(kv.Key, c)),
        Surface.Desert => board.Fences.Any(e => e.A == c || e.B == c || Adjacency.AreAdjacent(e.A, c) || Adjacency.AreAdjacent(e.B, c)),
        _ => throw new ArgumentOutOfRangeException(nameof(surface), surface, "不是新地表。"),
    };
}
