namespace Siege.Core.Board;

/// <summary>
/// 地图规格档（frontier-map D1）：只决定加载时用哪一套规模预算与静态校验（<see cref="MapValidator"/> 的声明表），
/// MUST NOT 被任何对局规则、AI 或表现层读取——气、连珠、覆盖、保护期与信物的结算在各档下完全相同。
/// </summary>
/// <remarks>
/// retire-legacy-maps 4.1（裁决 L1 / L7）：标准档（0）、边疆档（1）、棋盘档（2）随旧地图删除，只剩链接档；
/// 数值 3 不变（地图文件里写数字 3 的仍读作链接档），已移除的三个值在读入时报"已移除"（<see cref="MapFile"/>）。
/// 规格：openspec/specs/map-definition —— Requirement: 地图规格档
/// </remarks>
public enum MapProfile
{
    /// <summary>
    /// 链接档（board-link-mode D2 → open-map O3）：5 块出生棋盘建局时即边对边紧贴成一张连通地图，对局中不再增长（<see cref="MapData.MapVersion"/> 恒为 0）。
    /// 只定 4 人；连通性按连通分量校验（恰 1 个），不要求到中央入口可达。
    /// </summary>
    Link = 3,
}
