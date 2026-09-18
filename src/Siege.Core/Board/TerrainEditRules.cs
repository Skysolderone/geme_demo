using System.Collections.Immutable;

namespace Siege.Core.Board;

/// <summary>
/// 改造合法性：对局中"某个落点的匠人能不能做这次改造"的<b>唯一</b>实现
/// （artisan-terrain-edit 接口契约「改造合法性（新，唯一实现）」）。预演、结算、AI 与界面全部经这里。
/// </summary>
/// <remarks>
/// <para><b>目标口径是几何四邻</b>（裁决 T-2）：格目标必须是匠人落点的 <see cref="Adjacency.Neighbors"/> 之一，
/// 边目标是匠人落点与某个几何四邻格之间的 <see cref="FenceEdge"/>。匠人<b>不能</b>改自己脚下那格——四邻不含自身。
/// MUST NOT 改用气边口径：深水没有气边，搭桥会变成不可能。</para>
/// <para><b>没有逆向动作</b>（R-5）：已架桥的深水格、已有栅栏的边、已是草地的格都不是合法目标。</para>
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
    public static ImmutableArray<TerrainEdit> LegalTargets(MapData map, Coord artisanCell)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (!map.Contains(artisanCell))
        {
            return [];
        }

        var found = new List<TerrainEdit>();
        foreach (Coord n in Adjacency.Neighbors(map.Width, map.Height, artisanCell))
        {
            if (map.TerrainData.IsUnbridgedDeepWater(n))
            {
                found.Add(TerrainEdit.Bridge(n));
            }

            if (map.SurfaceAt(n) == Surface.Forest)
            {
                found.Add(TerrainEdit.Burn(n));
            }

            if (!map.HasFence(artisanCell, n))
            {
                found.Add(TerrainEdit.Fence(artisanCell, n));
            }
        }

        return [.. found.OrderBy(e => e.Kind).ThenBy(e => e.ToString(), StringComparer.Ordinal)];
    }

    /// <summary>该改造是否是 <paramref name="artisanCell"/> 上匠人的合法目标。等价于出现在 <see cref="LegalTargets"/> 里。</summary>
    public static bool IsLegal(MapData map, Coord artisanCell, TerrainEdit edit) => Reject(map, artisanCell, edit) is null;

    /// <summary>
    /// 拒绝理由；合法时为 <c>null</c>。判定与 <see cref="LegalTargets"/> 同源——本方法只负责把"为什么不在合法集合里"说清楚，
    /// MUST NOT 与 <see cref="LegalTargets"/> 出现口径分歧（守门：<c>改造合法性Tests.拒绝理由与合法目标集合一致</c>）。
    /// </summary>
    public static string? Reject(MapData map, Coord artisanCell, TerrainEdit edit)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (!map.Contains(artisanCell))
        {
            return $"匠人落点不在棋盘范围内：{artisanCell.ToNotation()}。";
        }

        ImmutableArray<Coord> neighbors = Adjacency.Neighbors(map.Width, map.Height, artisanCell);
        switch (edit.Kind)
        {
            case TerrainEditKind.Bridge:
                if (!neighbors.Contains(edit.Cell))
                {
                    return $"改造目标与匠人落点不是几何四邻：{artisanCell.ToNotation()} 与 {edit.Cell.ToNotation()}。";
                }

                if (map.SurfaceAt(edit.Cell) != Surface.DeepWater)
                {
                    return $"搭桥的目标必须是深水格：{edit.Cell.ToNotation()} 不是深水。";
                }

                return map.HasBridge(edit.Cell)
                    ? $"该目标已被改造过：{edit.Cell.ToNotation()} 已架桥。"
                    : null;

            case TerrainEditKind.Burn:
                if (!neighbors.Contains(edit.Cell))
                {
                    return $"改造目标与匠人落点不是几何四邻：{artisanCell.ToNotation()} 与 {edit.Cell.ToNotation()}。";
                }

                return map.SurfaceAt(edit.Cell) != Surface.Forest
                    ? $"烧林的目标必须是林地格：{edit.Cell.ToNotation()} 不是林地（已是草地即已被改造过）。"
                    : null;

            case TerrainEditKind.Fence:
                Coord other = edit.Edge.A == artisanCell ? edit.Edge.B : edit.Edge.A;
                if (!edit.Edge.Connects(artisanCell, other) || !neighbors.Contains(other))
                {
                    return $"改造目标与匠人落点不是几何四邻：立栅的边 {edit.Edge} 不以 {artisanCell.ToNotation()} 为一端。";
                }

                return map.HasFence(edit.Edge.A, edit.Edge.B)
                    ? $"该目标已被改造过：{edit.Edge} 已有栅栏。"
                    : null;

            default:
                throw new ArgumentOutOfRangeException(nameof(edit), edit.Kind, "未知改造类型。");
        }
    }
}
