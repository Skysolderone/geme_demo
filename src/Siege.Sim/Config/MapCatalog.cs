using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Sim.Config;

/// <summary>地图解析：内置基准图按 Id 取；其他视为 JSON 文件路径（或 <c>maps/&lt;id&gt;.json</c>）。</summary>
public static class MapCatalog
{
    public static MapData Resolve(string mapId)
    {
        ArgumentNullException.ThrowIfNull(mapId);
        MapData builtin = FourPlayerBaseMap.Create();
        if (mapId == builtin.Id)
        {
            return builtin;
        }

        string path = File.Exists(mapId) ? mapId : Path.Combine("maps", mapId + ".json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"找不到地图 {mapId}：既不是内置地图 {builtin.Id}，也不是存在的文件。", path);
        }

        return MapFile.FromJson(File.ReadAllText(path));
    }
}
