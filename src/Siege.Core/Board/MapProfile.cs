namespace Siege.Core.Board;

/// <summary>
/// 地图规格档（frontier-map D1）：只决定加载时用哪一套规模预算与静态校验（<see cref="MapValidator"/> 的声明表），
/// MUST NOT 被任何对局规则、AI 或表现层读取——气、连珠、覆盖、保护期与信物的结算在两档下完全相同。
/// </summary>
/// <remarks>规格：openspec/changes/frontier-map/specs/map-definition —— Requirement: 地图规格档</remarks>
public enum MapProfile
{
    /// <summary>标准档：出生区数 = 人数、等大出生区、距离均衡为硬约束。地图数据未写明时的缺省。</summary>
    Standard = 0,

    /// <summary>边疆档：平台（= 出生区）数多于人数、大小不一；距离均衡只报告不拒绝。</summary>
    Frontier = 1,
}
