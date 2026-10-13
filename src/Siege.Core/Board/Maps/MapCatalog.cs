namespace Siege.Core.Board.Maps;

/// <summary>
/// "地图标识 → 地图"的唯一解析（frontier-map D5）：批量跑局、终端版与图形版三个入口共用这一份，MUST NOT 各自维护地图清单。
/// <see cref="Resolve"/> <b>只</b>认链接图 <c>link:&lt;地图种子&gt;</c>（<see cref="LinkMapGenerator"/>），缺省地图为 <see cref="DefaultId"/>（<c>link:1</c>）。
/// 旧标识族（内置图、<c>gen:</c>、<c>board:</c>、地图文件路径）已随 retire-legacy-maps 删除（负责人 2026-10-13 裁决 L1）：一律报错并说明只接受 <c>link:&lt;地图种子&gt;</c>，
/// 解析不了就报错，MUST NOT 静默回落到缺省地图。
/// </summary>
/// <remarks>
/// 放在规则内核里是因为图形版（<c>src/godot</c>）只引用 Core / Presentation，不得依赖批量项目（<c>.trellis/spec/core/boundaries.md</c>）。
/// 这里只做解析，不做校验——校验在 <see cref="GameBoard.Load"/>（对局创建时）。
/// 规格：openspec/specs/simulation-harness —— Requirement: 各入口按地图标识选图；map-definition —— 地图为设计师固定的静态数据
/// </remarks>
public static class MapCatalog
{
    /// <summary>缺省地图标识（board-link-mode Q4 / 规格 map-selection「批量入口缺省」）：批量与终端入口未给地图选项时加载 <c>link:1</c>；图形版未给时进入新局界面。</summary>
    public const string DefaultId = LinkMapId.Prefix + ":1";

    /// <summary>
    /// 解析地图：只接受 <c>link:&lt;地图种子&gt;</c> → 链接档初始地图（<see cref="LinkMapGenerator"/>）；<c>null</c> / 空白 → <see cref="DefaultId"/>。
    /// 其余一切标识（已删除的内置图名、<c>gen:</c>、<c>board:</c>、文件路径、写错的标识）抛 <see cref="FormatException"/>，消息说明只接受 <c>link:&lt;地图种子&gt;</c>。
    /// </summary>
    /// <remarks>
    /// 裸 <c>link</c>（"随机取一个地图种子"）不是完整标识：这里不读时钟，遇到即抛 <see cref="FormatException"/>。
    /// 取种子只在入口的最外层做——入口用 <see cref="LinkMapId.IsBareRequest"/> 识别、取到种子后用 <see cref="LinkMapId.Format"/> 拼出完整标识再交到这里。
    /// </remarks>
    public static MapData Resolve(string? mapId)
    {
        string id = string.IsNullOrWhiteSpace(mapId) ? DefaultId : mapId.Trim();
        if (!LinkMapId.IsLinkMap(id))
        {
            throw new FormatException(
                $"地图标识 {id} 不受支持：玩家入口只接受 {LinkMapId.Prefix}:<地图种子>（例如 {DefaultId}、{LinkMapId.Prefix}:12345）。"
                + "内置图、gen: 随机图、board: 棋盘图与地图文件已删除（retire-legacy-maps），不再能加载。");
        }

        if (LinkMapId.IsBareRequest(id))
        {
            throw new FormatException(
                $"地图标识 {LinkMapId.Prefix} 没有带地图种子：规则内核不读时钟，随机取种子由入口完成。请给完整标识，例如 {DefaultId} 或 {LinkMapId.Prefix}:12345。");
        }

        return LinkMapGenerator.Generate(id);
    }
}
