namespace Siege.Core.Board;

/// <summary>
/// 地图规格档（frontier-map D1）：只决定加载时用哪一套规模预算与静态校验（<see cref="MapValidator"/> 的声明表），
/// MUST NOT 被任何对局规则、AI 或表现层读取——气、连珠、覆盖、保护期与信物的结算在各档下完全相同。
/// retire-legacy-maps 段 C 起只有棋盘档可以加载；边疆档（原取值 1）的枚举成员已删除，地图文件写着它时读入即报"已删除"。
/// </summary>
/// <remarks>规格：openspec/changes/board-map、retire-legacy-maps/specs/map-definition —— Requirement: 地图规格档</remarks>
public enum MapProfile
{
    /// <summary>
    /// 标准档（已于 retire-legacy-maps 删除）：只作 <see cref="MapData.Profile"/> 的缺省值保留——测试里经 Unvalidated 入口构造的合成盘面不必写档位，
    /// 缺 <c>Profile</c> 字段的旧地图文件按它读入；校验器对它报"标准档已删除"并拒绝加载。
    /// </summary>
    Standard = 0,

    /// <summary>
    /// 棋盘档（board-map D1、board-isolated-gen）：全部可落子格都属于某块棋盘（见 <see cref="MapData.Boards"/>），棋盘之间只隔场景、互不连通。
    /// 定 2 / 3 / 4 人；预算与校验见校验器声明表的棋盘档一行。
    /// </summary>
    Board = 2,
}
