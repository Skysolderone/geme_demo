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
/// <para><b>场景格不可改造</b>（board-link-mode 实现裁决 A5，规格 battlefield「战场的定义与规则作用域」Scenario「场景格不可改造」）：
/// 链接档地图上战场之间的场景格全是障碍格；搭桥 / 烧林的目标必须是深水 / 林地，障碍格天然被拒绝；三种动作都没有"把不可落子格变成可落子格"的能力
/// （<see cref="TerrainWriter"/> 只加桥 / 加栅 / 烧林，不碰障碍），立栅的边即便一端落在障碍格上也只是一道切不到任何气边的无效栅栏——
/// 于是没有任何改造能让战场分裂或合并（open-map 起整张地图本就是唯一战场）。本类<b>不</b>按规格档分流（下游不得读规格档），也不为链接档另加"障碍端点立栅即拒绝"：
/// 那会改变 v5 / board:1 上 AI 的合法改造候选，钉死的决策序列基线整批失效（board-link-mode 段 A 实测 14 条基线红）。守门见 <c>战场的定义与规则作用域Tests.场景格不可改造</c>。</para>
/// <para>格目标恒为几何四邻（relic-redesign D4：旧信物"工坊"的"隔一格"扩展随旧信物系统删除）。</para>
/// <para><b>改造范围</b>（open-map D4 / 裁决 O6，terrain-edit「构筑期内的改造范围」）：带 <c>scope</c> 的重载按流程层给的改造范围（<c>PublicRules.EditScope</c>：
/// 构筑期为自己的出生棋盘，第 4 大回合起为全图）过滤——搭桥 / 烧林的目标格 ∈ 范围，立栅的边两端都 ∈ 范围；枚举与拒绝理由共用同一判定，
/// 理由文案固定为 <see cref="OutOfScopeReason"/>。<c>scope</c> 为 <c>null</c> 即不限（改动前的行为）。</para>
/// <para><b>批内唯一与"不链式"不在本类</b>：本类只按传入的这一份地形判定单次改造，
/// 而"按批次开始前的地形判定"与"同一目标批内唯一"是批次层的事（<c>BatchRehearsal</c>）。</para>
/// </remarks>
public static class TerrainEditRules
{
    /// <summary>只有匠人能携带改造（terrain-edit「匠人落子即改造」）。</summary>
    public const PieceType EditorType = PieceType.Artisan;

    /// <summary>改造目标越出改造范围的拒绝理由（terrain-edit「构筑期内的改造范围」规定的文案）。</summary>
    public const string OutOfScopeReason = "构筑期内改造目标须在自己的出生棋盘内";

    /// <summary>
    /// 枚举一枚落在 <paramref name="artisanCell"/> 的匠人此刻的<b>全部</b>合法改造目标（确定性顺序：先按动作类型，再按目标记法）。
    /// AI 的候选枚举、界面的可改造目标高亮与合法性判定共用这一份，MUST NOT 各自再遍历一遍四邻。
    /// 没有任何合法目标时返回空——此时匠人仍然可以落子（裁决 T-6）。
    /// </summary>
    public static ImmutableArray<TerrainEdit> LegalTargets(MapData map, Coord artisanCell) => LegalTargets(map, artisanCell, scope: null);

    /// <summary>
    /// 同 <see cref="LegalTargets(MapData, Coord)"/>，再按改造范围 <paramref name="scope"/>（open-map D4）过滤：格目标 ∈ 范围、边目标两端都 ∈ 范围；<c>null</c> 不限。
    /// </summary>
    public static ImmutableArray<TerrainEdit> LegalTargets(MapData map, Coord artisanCell, IReadOnlySet<Coord>? scope)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (!map.Contains(artisanCell))
        {
            return [];
        }

        var found = new List<TerrainEdit>();
        foreach (Coord n in Adjacency.Neighbors(map.Width, map.Height, artisanCell))
        {
            if (!InScope(scope, n))
            {
                continue;
            }

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
                if (!InScope(scope, n) || !InScope(scope, m))
                {
                    continue;
                }

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

    /// <summary>同上，再带改造范围（open-map D4）。</summary>
    public static bool IsLegal(MapData map, Coord artisanCell, TerrainEdit edit, IReadOnlySet<Coord>? scope) => Reject(map, artisanCell, edit, scope) is null;

    /// <summary>
    /// 拒绝理由；合法时为 <c>null</c>。判定与 <see cref="LegalTargets"/> 同源——本方法只负责把"为什么不在合法集合里"说清楚，
    /// MUST NOT 与 <see cref="LegalTargets"/> 出现口径分歧（守门：<c>改造合法性Tests.拒绝理由与合法目标集合一致</c>）。
    /// </summary>
    public static string? Reject(MapData map, Coord artisanCell, TerrainEdit edit) => Reject(map, artisanCell, edit, scope: null);

    /// <summary>同上，再带改造范围 <paramref name="scope"/>（open-map D4）：目标格 / 边两端越出范围即以 <see cref="OutOfScopeReason"/> 拒绝；<c>null</c> 不限。</summary>
    public static string? Reject(MapData map, Coord artisanCell, TerrainEdit edit, IReadOnlySet<Coord>? scope)
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
                if (!InScope(scope, edit.Cell))
                {
                    return $"{OutOfScopeReason}：{edit.Cell.ToNotation()}。";
                }

                if (!neighbors.Contains(edit.Cell))
                {
                    return OutOfCellRange(artisanCell, edit.Cell);
                }

                if (map.SurfaceAt(edit.Cell) != Surface.DeepWater)
                {
                    return $"搭桥的目标必须是深水格：{edit.Cell.ToNotation()} 不是深水。";
                }

                return map.HasBridge(edit.Cell)
                    ? $"该目标已被改造过：{edit.Cell.ToNotation()} 已架桥。"
                    : null;

            case TerrainEditKind.Burn:
                if (!InScope(scope, edit.Cell))
                {
                    return $"{OutOfScopeReason}：{edit.Cell.ToNotation()}。";
                }

                if (!neighbors.Contains(edit.Cell))
                {
                    return OutOfCellRange(artisanCell, edit.Cell);
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

                if (!InScope(scope, edit.Edge.A) || !InScope(scope, edit.Edge.B))
                {
                    return $"{OutOfScopeReason}：{edit.Edge}。";
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

    /// <summary>
    /// 改造范围的唯一判定（open-map D4）：<paramref name="scope"/> 为 <c>null</c> 即不限；否则格须在范围内。格目标直接用它，边目标对两端各用一次。
    /// <see cref="LegalTargets(MapData, Coord, IReadOnlySet{Coord})"/> 与 <see cref="Reject(MapData, Coord, TerrainEdit, IReadOnlySet{Coord})"/> 共用，不各写一份条件。
    /// </summary>
    private static bool InScope(IReadOnlySet<Coord>? scope, Coord cell) => scope is null || scope.Contains(cell);

    private static string OutOfCellRange(Coord artisanCell, Coord target) =>
        $"改造目标与匠人落点不是几何四邻：{artisanCell.ToNotation()} 与 {target.ToNotation()}。";
}
