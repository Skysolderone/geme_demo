namespace Siege.Core.Board.Maps;

/// <summary>
/// "地图标识 → 地图"的唯一解析（frontier-map D5）：批量跑局、终端版与图形版三个入口共用这一份，MUST NOT 各自维护地图清单。
/// 内置图按标识直接给；<c>gen:&lt;地图种子&gt;[:p&lt;平台数&gt;]</c> 交给生成器（map-generator D3）；其余视为地图文件路径（或 <c>maps/&lt;标识&gt;.json</c>）。
/// 解析不了就报错并列出可用标识，MUST NOT 静默回落到缺省地图。
/// </summary>
/// <remarks>
/// 放在规则内核里是因为图形版（<c>src/godot</c>）只引用 Core / Presentation，不得依赖批量项目（<c>.trellis/spec/core/boundaries.md</c>）。
/// 这里只做解析，不做校验——校验在 <see cref="GameBoard.Load"/>（对局创建时）。
/// 规格：openspec/changes/frontier-map/specs/simulation-harness —— Requirement: 各入口按地图标识选图
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

    /// <summary>缺省地图标识：任何入口未给地图选项时加载它。</summary>
    public const string DefaultId = FourPlayerBaseMap.Id;

    /// <summary>可用的内置地图标识，按登记顺序。</summary>
    public static IReadOnlyList<string> BuiltinIds { get; } = [.. Builtins.Select(b => b.Id)];

    /// <summary>内置地图的标识与面向人的显示名，按登记顺序（与 <see cref="BuiltinIds"/> 同序）。显示名只用于界面，不参与解析。</summary>
    public static IReadOnlyList<BuiltinMapInfo> BuiltinMaps { get; } = [.. Builtins.Select(b => new BuiltinMapInfo(b.Id, b.Title))];

    /// <summary>
    /// 解析地图：<c>null</c> / 空白 → 缺省地图；内置标识 → 内置图；生成图标识 → 按其中的地图种子与参数生成；
    /// 否则按文件路径、再按 <c>maps/&lt;标识&gt;.json</c> 读入。都不是则抛 <see cref="FileNotFoundException"/>，消息列出全部可用标识。
    /// </summary>
    /// <remarks>
    /// 裸 <c>gen</c>（"随机取一个地图种子"）不是完整标识：这里不读时钟，遇到即抛 <see cref="FormatException"/>。
    /// 取种子只在三个入口的最外层做——入口用 <see cref="GeneratedMapId.IsBareRequest"/> 识别、取到种子后用
    /// <see cref="GeneratedMapId.Format"/> 拼出完整标识、打印给用户，再交到这里。
    /// </remarks>
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

        string path = File.Exists(id) ? id : Path.Combine("maps", id + ".json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"找不到地图 {id}：既不是内置地图，也不是存在的地图文件。可用的地图标识：{string.Join("、", BuiltinIds)}；随机生成图写作 {GeneratedMapId.Prefix}:<地图种子>[:p<平台数 {MapGenParameters.MinPlatforms}–{MapGenParameters.MaxPlatforms}>]（如 {GeneratedMapId.Prefix}:12345）；也可以给地图文件（.json）的路径。",
                path);
        }

        return MapFile.FromJson(File.ReadAllText(path));
    }
}

/// <summary>内置地图的一项登记：地图标识与面向人的显示名（选图界面的选项标题）。</summary>
public sealed record BuiltinMapInfo(string Id, string Title);
