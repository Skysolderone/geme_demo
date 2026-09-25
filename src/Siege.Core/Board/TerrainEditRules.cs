using System.Collections.Immutable;

namespace Siege.Core.Board;

/// <summary>
/// 改造合法性：对局中"某个落点的匠人能不能做这次改造"的<b>唯一</b>实现
/// （artisan-terrain-edit 接口契约「改造合法性（新，唯一实现）」）。预演、结算、AI 与界面全部经这里。
/// </summary>
/// <remarks>
/// <para><b>格目标口径是几何四邻</b>（裁决 T-2）：搭桥、烧林的目标必须是匠人落点的 <see cref="Adjacency.Neighbors"/> 之一。
/// 匠人<b>不能</b>改自己脚下那格——四邻不含自身。
/// MUST NOT 改用气边口径：深水没有气边，搭桥会变成不可能。</para>
/// <para><b>边目标口径放宽一圈</b>（裁决 T-11，design D-B′）：立栅的边只要求<b>至少一端</b>是匠人落点的几何四邻格，
/// 因此既含匠人格与四邻之间的 4 条边，也含四邻与更外一格之间的 12 条边（棋盘内共至多 16 条）。
/// 旧口径（边必以匠人落点为一端）下立栅<b>永远不可能提子</b>——那条边必有一端是匠人自己刚落的子，
/// 既不是敌串的气也切不断敌串（段 B 实测 20 局 97 次改造致提子 0 次）；放宽后 T-3「改造先于提子」才真正成立。
/// 边的两端本身仍 MUST 几何相邻且都在棋盘内。</para>
/// <para><b>没有逆向动作</b>（R-5）：已架桥的深水格、已有栅栏的边、已是草地的格都不是合法目标。</para>
/// <para><b>工坊</b>（more-pieces-relics D5）：本小回合快照标记工坊生效时，格目标另含与落点同行 / 同列、直线距离恰为 2 的格（"隔一格"，至多 4 格），
/// 中间那格不设任何条件；斜向与直线距离 3 及以上的格仍不是格目标。边目标的范围不变。格目标候选只在 <see cref="CellTargets"/> 一处给出，
/// 枚举与拒绝理由共用它。</para>
/// <para><b>批内唯一与"不链式"不在本类</b>：本类只按传入的这一份地形判定单次改造，
/// 而"按批次开始前的地形判定"与"同一目标批内唯一"是批次层的事（<c>BatchRehearsal</c>）。</para>
/// </remarks>
public static class TerrainEditRules
{
    /// <summary>只有匠人能携带改造（terrain-edit「匠人落子即改造」）。</summary>
    public const PieceType EditorType = PieceType.Artisan;

    /// <summary>
    /// 枚举一枚落在 <paramref name="artisanCell"/> 的匠人此刻的<b>全部</b>合法改造目标（确定性顺序：先按动作类型，再按目标记法）。
    /// AI 的候选枚举、界面的可改造目标高亮与合法性判定共用这一份，MUST NOT 各自再遍历一遍四邻。
    /// 没有任何合法目标时返回空——此时匠人仍然可以落子（裁决 T-6）。
    /// </summary>
    public static ImmutableArray<TerrainEdit> LegalTargets(MapData map, Coord artisanCell) => LegalTargets(map, artisanCell, workshop: false);

    /// <summary>
    /// 同 <see cref="LegalTargets(MapData, Coord)"/>，<paramref name="workshop"/> 为本小回合快照的工坊标记（more-pieces-relics D5）：
    /// 为真时格目标另含与落点同行 / 同列、直线距离恰为 2 的格；边目标不变。
    /// </summary>
    public static ImmutableArray<TerrainEdit> LegalTargets(MapData map, Coord artisanCell, bool workshop)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (!map.Contains(artisanCell))
        {
            return [];
        }

        var found = new List<TerrainEdit>();
        foreach (Coord n in CellTargets(map, artisanCell, workshop))
        {
            if (map.TerrainData.IsUnbridgedDeepWater(n))
            {
                found.Add(TerrainEdit.Bridge(n));
            }

            if (map.SurfaceAt(n) == Surface.Forest)
            {
                found.Add(TerrainEdit.Burn(n));
            }
        }

        foreach (Coord n in Adjacency.Neighbors(map.Width, map.Height, artisanCell))
        {
            // 边目标（T-11）：以该四邻格 n 为一端的全部边——m == artisanCell 给出原口径的 4 条内圈边，
            // 其余给出 12 条外圈边。不同的 n 之间不会撞车：两个四邻格互不相邻，外圈端点离落点是 2 格。
            foreach (Coord m in Adjacency.Neighbors(map.Width, map.Height, n))
            {
                if (!map.HasFence(n, m))
                {
                    found.Add(TerrainEdit.Fence(n, m));
                }
            }
        }

        return [.. found.OrderBy(e => e.Kind).ThenBy(e => e.ToString(), StringComparer.Ordinal)];
    }

    /// <summary>该改造是否是 <paramref name="artisanCell"/> 上匠人的合法目标。等价于出现在 <see cref="LegalTargets"/> 里。</summary>
    public static bool IsLegal(MapData map, Coord artisanCell, TerrainEdit edit) => Reject(map, artisanCell, edit) is null;

    /// <summary>同 <see cref="IsLegal(MapData, Coord, TerrainEdit)"/>，带本小回合的工坊标记。</summary>
    public static bool IsLegal(MapData map, Coord artisanCell, TerrainEdit edit, bool workshop) => Reject(map, artisanCell, edit, workshop) is null;

    /// <summary>
    /// 拒绝理由；合法时为 <c>null</c>。判定与 <see cref="LegalTargets"/> 同源——本方法只负责把"为什么不在合法集合里"说清楚，
    /// MUST NOT 与 <see cref="LegalTargets"/> 出现口径分歧（守门：<c>改造合法性Tests.拒绝理由与合法目标集合一致</c>）。
    /// </summary>
    public static string? Reject(MapData map, Coord artisanCell, TerrainEdit edit) => Reject(map, artisanCell, edit, workshop: false);

    /// <summary>同 <see cref="Reject(MapData, Coord, TerrainEdit)"/>，<paramref name="workshop"/> 为本小回合快照的工坊标记。</summary>
    public static string? Reject(MapData map, Coord artisanCell, TerrainEdit edit, bool workshop)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (!map.Contains(artisanCell))
        {
            return $"匠人落点不在棋盘范围内：{artisanCell.ToNotation()}。";
        }

        ImmutableArray<Coord> neighbors = Adjacency.Neighbors(map.Width, map.Height, artisanCell);
        ImmutableArray<Coord> cellTargets = CellTargets(map, artisanCell, workshop);
        switch (edit.Kind)
        {
            case TerrainEditKind.Bridge:
                if (!cellTargets.Contains(edit.Cell))
                {
                    return OutOfCellRange(artisanCell, edit.Cell, workshop);
                }

                if (map.SurfaceAt(edit.Cell) != Surface.DeepWater)
                {
                    return $"搭桥的目标必须是深水格：{edit.Cell.ToNotation()} 不是深水。";
                }

                return map.HasBridge(edit.Cell)
                    ? $"该目标已被改造过：{edit.Cell.ToNotation()} 已架桥。"
                    : null;

            case TerrainEditKind.Burn:
                if (!cellTargets.Contains(edit.Cell))
                {
                    return OutOfCellRange(artisanCell, edit.Cell, workshop);
                }

                return map.SurfaceAt(edit.Cell) != Surface.Forest
                    ? $"烧林的目标必须是林地格：{edit.Cell.ToNotation()} 不是林地（已是草地即已被改造过）。"
                    : null;

            case TerrainEditKind.Fence:
                if (!map.Contains(edit.Edge.A) || !map.Contains(edit.Edge.B))
                {
                    return $"立栅的边不在棋盘范围内：{edit.Edge}。";
                }

                if (!Adjacency.AreAdjacent(edit.Edge.A, edit.Edge.B))
                {
                    return $"立栅的边两端不是几何四邻：{edit.Edge}。";
                }

                if (!neighbors.Contains(edit.Edge.A) && !neighbors.Contains(edit.Edge.B))
                {
                    return $"该边两端都不是匠人落点的几何四邻格：{artisanCell.ToNotation()} 与 {edit.Edge}。";
                }

                return map.HasFence(edit.Edge.A, edit.Edge.B)
                    ? $"该目标已被改造过：{edit.Edge} 已有栅栏。"
                    : null;

            default:
                throw new ArgumentOutOfRangeException(nameof(edit), edit.Kind, "未知改造类型。");
        }
    }

    /// <summary>工坊的"隔一格"偏移：同行 / 同列、直线距离恰为 2（不含斜向，不含更远）。</summary>
    private static readonly (int Dx, int Dy)[] WorkshopOffsets = [(0, -2), (-2, 0), (2, 0), (0, 2)];

    /// <summary>
    /// 格目标（搭桥 / 烧林）的候选格：几何四邻；<paramref name="workshop"/> 为真时再加棋盘内同行 / 同列直线距离 2 的格（D5）。
    /// <see cref="LegalTargets(MapData, Coord, bool)"/> 与 <see cref="Reject(MapData, Coord, TerrainEdit, bool)"/> 共用这一份。
    /// </summary>
    private static ImmutableArray<Coord> CellTargets(MapData map, Coord artisanCell, bool workshop)
    {
        ImmutableArray<Coord> neighbors = Adjacency.Neighbors(map.Width, map.Height, artisanCell);
        if (!workshop)
        {
            return neighbors;
        }

        ImmutableArray<Coord>.Builder cells = neighbors.ToBuilder();
        foreach ((int dx, int dy) in WorkshopOffsets)
        {
            int x = artisanCell.X + dx;
            int y = artisanCell.Y + dy;
            if (x >= 0 && y >= 0 && x < map.Width && y < map.Height)
            {
                cells.Add(new Coord(x, y));
            }
        }

        return cells.ToImmutable();
    }

    private static string OutOfCellRange(Coord artisanCell, Coord target, bool workshop) => workshop
        ? $"改造目标超出工坊扩展后的范围：{artisanCell.ToNotation()} 与 {target.ToNotation()}（工坊生效时格目标为几何四邻或同行 / 同列隔一格）。"
        : $"改造目标与匠人落点不是几何四邻：{artisanCell.ToNotation()} 与 {target.ToNotation()}。";
}
