namespace Siege.Core.Board.Maps;

/// <summary>
/// "地图标识 → 地图"的唯一解析（frontier-map D5）：批量跑局、终端版与图形版三个入口共用这一份，MUST NOT 各自维护地图清单。
/// 自 board-link-mode 起（D8 / 实现裁决 D7）<see cref="Resolve"/> <b>只</b>认新模式的 <c>link:&lt;地图种子&gt;</c>（<see cref="LinkMapGenerator"/>），
/// 缺省地图为 <see cref="DefaultId"/>（<c>link:1</c>，Q4）；旧标识族（内置图、<c>gen:</c>、<c>board:</c>、地图文件路径）一律报错并说明只接受 <c>link:&lt;地图种子&gt;</c>，
/// 只能经 <see cref="Legacy"/> 到达（Core 测试夹具与 <c>Siege.Sim map</c> 调试子命令）。解析不了就报错，MUST NOT 静默回落到缺省地图。
/// </summary>
/// <remarks>
/// 放在规则内核里是因为图形版（<c>src/godot</c>）只引用 Core / Presentation，不得依赖批量项目（<c>.trellis/spec/core/boundaries.md</c>）。
/// 这里只做解析，不做校验——校验在 <see cref="GameBoard.Load"/>（对局创建时）。
/// 规格：openspec/changes/board-link-mode/specs/simulation-harness —— Requirement: 各入口按地图标识选图；map-definition —— 旧地图降级为测试夹具
/// </remarks>
public static class MapCatalog
{
    /// <summary>
    /// 内置地图表：标识、面向人的显示名、生成器。新增内置图只在这里加一行。
    /// 显示名也登记在这里（map-generator 裁决 3）：选图界面从本表读，界面层不得另带"标识 → 名字"的对照表。
    /// </summary>
    private static readonly (string Id, string Title, Func<MapData> Create)[] Builtins =
    [
        (FourPlayerBaseMap.Id, "标准图 13×13", FourPlayerBaseMap.Create),
        (TwoPlayerBaseMap.Id, "双人图 9×9", TwoPlayerBaseMap.Create),
        (ThreePlayerBaseMap.Id, "三人图 11×11", ThreePlayerBaseMap.Create),
        (FrontierMapV2.Id, "边疆图 25×30（手工）", FrontierMapV2.Create),
    ];

    /// <summary>缺省地图标识（board-link-mode Q4 / 规格 map-selection「批量入口缺省」）：批量与终端入口未给地图选项时加载 <c>link:1</c>；图形版未给时进入新局界面。</summary>
    public const string DefaultId = LinkMapId.Prefix + ":1";

    /// <summary>可用的内置地图标识，按登记顺序。</summary>
    public static IReadOnlyList<string> BuiltinIds { get; } = [.. Builtins.Select(b => b.Id)];

    /// <summary>内置地图的标识与面向人的显示名，按登记顺序（与 <see cref="BuiltinIds"/> 同序）。显示名只用于界面，不参与解析。</summary>
    public static IReadOnlyList<BuiltinMapInfo> BuiltinMaps { get; } = [.. Builtins.Select(b => new BuiltinMapInfo(b.Id, b.Title))];

    /// <summary>
    /// 解析地图（board-link-mode D8 / 实现裁决 D7）：只接受 <c>link:&lt;地图种子&gt;</c> → 链接档初始地图（<see cref="LinkMapGenerator"/>）；<c>null</c> / 空白 → <see cref="DefaultId"/>。
    /// 其余一切标识（内置图名、<c>gen:</c>、<c>board:</c>、文件路径、写错的标识）抛 <see cref="FormatException"/>，消息说明只接受 <c>link:&lt;地图种子&gt;</c>——旧标识族只能经 <see cref="Legacy"/>。
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
                + "内置图、gen: 随机图、board: 棋盘图与地图文件已降级为测试夹具，不再由任何入口到达。");
        }

        if (LinkMapId.IsBareRequest(id))
        {
            throw new FormatException(
                $"地图标识 {LinkMapId.Prefix} 没有带地图种子：规则内核不读时钟，随机取种子由入口完成。请给完整标识，例如 {DefaultId} 或 {LinkMapId.Prefix}:12345。");
        }

        return LinkMapGenerator.Generate(id);
    }

    /// <summary>
    /// 旧地图族的解析（board-link-mode D8 / 规格 map-definition「旧地图降级为测试夹具」、map-generation「既有生成器降级为测试夹具」）：
    /// 内置图、<c>gen:&lt;地图种子&gt;[:p&lt;平台数&gt;]</c>（边疆档生成器）、<c>board:&lt;地图种子&gt;[:n&lt;棋盘数&gt;]</c>（棋盘档生成器）与地图文件路径。
    /// 只供 Core 测试（围棋内核的回归护栏）与 <c>Siege.Sim map</c> 调试子命令使用，玩家入口不得到达；与引入本 change 之前的解析逐项相同。
    /// </summary>
    public static class Legacy
    {
        /// <summary>旧地图族的缺省地图（v5）：夹具解析 <see cref="Resolve"/> 未给标识时加载它；批次配置 <c>RunConfig.MapId</c> 的内部缺省与 <c>map</c> 调试子命令的缺省也取它（实现裁决 C10，留到 retire-legacy-maps）。</summary>
        public const string DefaultId = FourPlayerBaseMap.Id;

        /// <summary>
        /// 解析旧地图：<c>null</c> / 空白 → <see cref="DefaultId"/>（v5）；内置标识 → 内置图；生成图标识 → 按其中的地图种子与参数生成；
        /// 否则按文件路径、再按 <c>maps/&lt;标识&gt;.json</c> 读入。都不是则抛 <see cref="FileNotFoundException"/>，消息列出全部可用标识。
        /// 裸 <c>gen</c> / <c>board</c> 同样不是完整标识，遇到即抛 <see cref="FormatException"/>。与引入 board-link-mode 之前的 <c>MapCatalog.Resolve</c> 逐项相同。
        /// </summary>
        public static MapData Resolve(string? mapId)
        {
            string id = string.IsNullOrWhiteSpace(mapId) ? DefaultId : mapId.Trim();
            foreach ((string builtinId, _, Func<MapData> create) in Builtins)
            {
                if (id == builtinId)
                {
                    return create();
                }
            }

            if (GeneratedMapId.IsGenerated(id))
            {
                if (GeneratedMapId.IsBareRequest(id))
                {
                    throw new FormatException(
                        $"地图标识 {GeneratedMapId.Prefix} 没有带地图种子：规则内核不读时钟，随机取种子由入口完成。请给完整标识，例如 {GeneratedMapId.Prefix}:12345 或 {GeneratedMapId.Prefix}:12345:p7。");
                }

                return FrontierMapGenerator.Generate(id);
            }

            if (BoardMapId.IsBoardMap(id))
            {
                // 裸 board 同理：不是完整标识，取种子只在三个入口的最外层做。
                if (BoardMapId.IsBareRequest(id))
                {
                    throw new FormatException(
                        $"地图标识 {BoardMapId.Prefix} 没有带地图种子：规则内核不读时钟，随机取种子由入口完成。请给完整标识，例如 {BoardMapId.Prefix}:12345 或 {BoardMapId.Prefix}:12345:n9。");
                }

                return BoardMapGenerator.Generate(id);
            }

            string path = File.Exists(id) ? id : Path.Combine("maps", id + ".json");
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"找不到地图 {id}：既不是内置地图，也不是存在的地图文件。可用的地图标识：{string.Join("、", BuiltinIds)}；随机生成图写作 {GeneratedMapId.Prefix}:<地图种子>[:p<平台数 {MapGenParameters.MinPlatforms}–{MapGenParameters.MaxPlatforms}>]（如 {GeneratedMapId.Prefix}:12345）；随机生成的棋盘图写作 {BoardMapId.Prefix}:<地图种子>[:n<棋盘数 {BoardMapParameters.MinBoards}–{BoardMapParameters.MaxBoards}>]（如 {BoardMapId.Prefix}:12345）；新模式的链接图写作 {LinkMapId.Prefix}:<地图种子>（如 {LinkMapId.Prefix}:1）；也可以给地图文件（.json）的路径。",
                    path);
            }

            return MapFile.FromJson(File.ReadAllText(path));
        }
    }
}

/// <summary>内置地图的一项登记：地图标识与面向人的显示名（选图界面的选项标题）。</summary>
public sealed record BuiltinMapInfo(string Id, string Title);
