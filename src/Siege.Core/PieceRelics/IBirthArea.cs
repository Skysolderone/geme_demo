using Siege.Core.Board;

namespace Siege.Core.PieceRelics;

/// <summary>
/// 出生区域查询（relic-redesign D2 / R32）：信物刷新与掉落不落在出生区域内。抽象成接口，是因为"出生区域"的口径随地图而定；
/// 现行实现见 <see cref="BirthBoardArea"/>（浮空岛的出生平台）。
/// </summary>
public interface IBirthArea
{
    /// <summary>该格是否属于出生区域。</summary>
    bool IsBirthArea(Coord c);
}

/// <summary>
/// 现行出生区域 = 出生平台（R32，map-expand-25 合回后的岛图口径）：格落在任一出生平台（<see cref="MapData.BirthBoards"/>，棋盘清单里的出生棋盘）的
/// 外接矩形内（<see cref="MapData.BirthBoardAt"/>）即属于出生区域。出生区是出生平台的核心区，平台已含出生区，不再另取并集。
/// </summary>
/// <remarks>
/// 只有没有棋盘清单的手写地图（规则层测试夹具，如 <c>TestMaps.NineByNine</c>）才退回到出生区本身（<see cref="MapData.BirthZoneOf"/>）：
/// 它们没有平台可取，退回是为了不把整张盘当作"没有出生区域"。对局与生成图一律带棋盘清单，走平台口径。
/// </remarks>
public sealed class BirthBoardArea : IBirthArea
{
    private readonly MapData _map;
    private readonly bool _hasBirthBoards;

    public BirthBoardArea(MapData map)
    {
        ArgumentNullException.ThrowIfNull(map);
        _map = map;
        _hasBirthBoards = map.BirthBoards().Any();
    }

    /// <inheritdoc/>
    public bool IsBirthArea(Coord c) => _hasBirthBoards ? _map.BirthBoardAt(c) is not null : _map.BirthZoneOf(c) is not null;
}
