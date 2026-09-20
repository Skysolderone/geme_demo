namespace Siege.Core.Board.Maps;

/// <summary>
/// "地图标识 → 地图"的唯一解析（frontier-map D5）：批量跑局、终端版与图形版三个入口共用这一份，MUST NOT 各自维护地图清单。
/// 内置图按标识直接给；其余视为地图文件路径（或 <c>maps/&lt;标识&gt;.json</c>）。解析不了就报错并列出可用标识，MUST NOT 静默回落到缺省地图。
/// </summary>
/// <remarks>
/// 放在规则内核里是因为图形版（<c>src/godot</c>）只引用 Core / Presentation，不得依赖批量项目（<c>.trellis/spec/core/boundaries.md</c>）。
/// 这里只做解析，不做校验——校验在 <see cref="GameBoard.Load"/>（对局创建时）。
/// 规格：openspec/changes/frontier-map/specs/simulation-harness —— Requirement: 各入口按地图标识选图
/// </remarks>
public static class MapCatalog
{
    /// <summary>内置地图表：标识 → 生成器。新增内置图只在这里加一行。</summary>
    private static readonly (string Id, Func<MapData> Create)[] Builtins =
    [
        (FourPlayerBaseMap.Id, FourPlayerBaseMap.Create),
        (FrontierMapV1.Id, FrontierMapV1.Create),
    ];

    /// <summary>缺省地图标识：任何入口未给地图选项时加载它。</summary>
    public const string DefaultId = FourPlayerBaseMap.Id;

    /// <summary>可用的内置地图标识，按登记顺序。</summary>
    public static IReadOnlyList<string> BuiltinIds { get; } = [.. Builtins.Select(b => b.Id)];

    /// <summary>
    /// 解析地图：<c>null</c> / 空白 → 缺省地图；内置标识 → 内置图；否则按文件路径、再按 <c>maps/&lt;标识&gt;.json</c> 读入。
    /// 都不是则抛 <see cref="FileNotFoundException"/>，消息列出全部可用标识。
    /// </summary>
    public static MapData Resolve(string? mapId)
    {
        string id = string.IsNullOrWhiteSpace(mapId) ? DefaultId : mapId.Trim();
        foreach ((string builtinId, Func<MapData> create) in Builtins)
        {
            if (id == builtinId)
            {
                return create();
            }
        }

        string path = File.Exists(id) ? id : Path.Combine("maps", id + ".json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"找不到地图 {id}：既不是内置地图，也不是存在的地图文件。可用的地图标识：{string.Join("、", BuiltinIds)}；也可以给地图文件（.json）的路径。",
                path);
        }

        return MapFile.FromJson(File.ReadAllText(path));
    }
}
