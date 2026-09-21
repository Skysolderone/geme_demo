using System.Collections.Immutable;

namespace Siege.Core.Board;

/// <summary>
/// 地形写入口：对局内改造地形的<b>唯一</b>实现（artisan-terrain-edit 接口契约「地形写入口（新，唯一实现）」）。
/// 输入一份地形快照与若干改造，产出新的快照；输入不变（<see cref="TerrainData"/> / <see cref="MapData"/> 都是不可变值）。
/// </summary>
/// <remarks>
/// <para><b>为什么要唯一</b>：改造之后气边、覆盖、棋串、气与信物控制全部要按新地形重算，
/// 而这些派生量都从 <see cref="MapData"/> 实时导出。只要地形写入集中在这里，"改造后重算"就不需要第二处通知机制。
/// 规则层之外（含 <c>src/godot</c>、表现层）MUST NOT 自行 <c>new TerrainData(...)</c> 造出带不同设施的地形——
/// 守门：<c>地形写入口Tests.地形写入口之外不得构造改造后的地形</c>。</para>
/// <para><b>不可逆</b>：三种动作都只做加法（加桥 / 加栅 / 林地→草地），没有拆桥、拆栅、草地→林地的入口。
/// 本类也 MUST NOT 改高度、障碍与信物（R-4）。</para>
/// <para>本类只做"目标形状对不对"的最低限度校验（搭桥必须落在未架桥的深水格等），
/// "谁能改、能改哪里"是 <see cref="TerrainEditRules"/> 的事——本类是写入原语，不是规则。</para>
/// </remarks>
public static class TerrainWriter
{
    /// <summary>对地形快照应用一次改造，返回新快照。目标不满足动作前提时抛 <see cref="SiegeRuleException"/>。</summary>
    public static TerrainData Apply(TerrainData terrain, TerrainEdit edit)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        switch (edit.Kind)
        {
            case TerrainEditKind.Bridge:
                if (terrain.SurfaceAt(edit.Cell) != Surface.DeepWater)
                {
                    throw new SiegeRuleException($"搭桥的目标必须是深水格：{edit.Cell.ToNotation()}。");
                }

                if (terrain.HasBridge(edit.Cell))
                {
                    throw new SiegeRuleException($"该深水格已架桥：{edit.Cell.ToNotation()}。");
                }

                return new TerrainData(terrain.Heights, terrain.Surfaces, terrain.Bridges.Add(edit.Cell), terrain.Fences);

            case TerrainEditKind.Fence:
                if (terrain.Fences.Contains(edit.Edge))
                {
                    throw new SiegeRuleException($"该边已有栅栏：{edit.Edge}。");
                }

                return new TerrainData(terrain.Heights, terrain.Surfaces, terrain.Bridges, terrain.Fences.Add(edit.Edge));

            case TerrainEditKind.Burn:
                if (terrain.SurfaceAt(edit.Cell) != Surface.Forest)
                {
                    throw new SiegeRuleException($"烧林的目标必须是林地格：{edit.Cell.ToNotation()}。");
                }

                // 只改地表；高度、障碍、桥与栅栏原样带过去（R-4）。草地是缺省值，TerrainData 构造时会把它归一化掉。
                return new TerrainData(
                    terrain.Heights, terrain.Surfaces.SetItem(edit.Cell, Surface.Grass), terrain.Bridges, terrain.Fences);

            default:
                throw new ArgumentOutOfRangeException(nameof(edit), edit.Kind, "未知改造类型。");
        }
    }

    /// <summary>
    /// 对地图应用若干改造，返回新地图。<b>同时生效</b>：全部改造都按传入的这份地形判定前提，
    /// 因此结果与应用顺序无关（terrain-edit「多个改造同时生效」）——批次内不链式（D-D）也是同一件事。
    /// </summary>
    public static MapData ApplyAll(MapData map, IEnumerable<TerrainEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(edits);
        ImmutableArray<TerrainEdit> list = [.. edits];
        if (list.IsEmpty)
        {
            return map;
        }

        TerrainData before = map.TerrainData;
        TerrainData terrain = before;
        foreach (TerrainEdit edit in list)
        {
            // 前提按 before（本批开始前的地形）判，写入累加到 terrain：因此结果与顺序无关。
            // 同一目标出现两次时前提都过得去（before 里它还没被改），由 Apply 按累加后的 terrain 抛出。
            RequirePrecondition(before, edit);
            terrain = Apply(terrain, edit);
        }

        return map with { TerrainData = terrain };
    }

    /// <summary>按给定地形校验一次改造的动作前提；不通过即抛 <see cref="SiegeRuleException"/>。</summary>
    private static void RequirePrecondition(TerrainData terrain, TerrainEdit edit)
    {
        switch (edit.Kind)
        {
            case TerrainEditKind.Bridge when terrain.SurfaceAt(edit.Cell) != Surface.DeepWater:
                throw new SiegeRuleException($"搭桥的目标必须是深水格：{edit.Cell.ToNotation()}。");
            case TerrainEditKind.Bridge when terrain.HasBridge(edit.Cell):
                throw new SiegeRuleException($"该深水格已架桥：{edit.Cell.ToNotation()}。");
            case TerrainEditKind.Fence when terrain.Fences.Contains(edit.Edge):
                throw new SiegeRuleException($"该边已有栅栏：{edit.Edge}。");
            case TerrainEditKind.Burn when terrain.SurfaceAt(edit.Cell) != Surface.Forest:
                throw new SiegeRuleException($"烧林的目标必须是林地格：{edit.Cell.ToNotation()}。");
            default:
                return;
        }
    }
}
