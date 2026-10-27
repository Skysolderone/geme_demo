using Siege.Core.Board;

namespace Siege.Core.PieceRelics;

/// <summary>
/// 出生区域查询（relic-redesign D2 / R32）：信物刷新与掉落不落在出生区域内。抽象成接口，是因为"出生区域"的口径会随地图换：
/// 现行实现 = 出生棋盘（<see cref="BirthBoardArea"/>）；map-expand-25 合回 main 后改为平台。
/// </summary>
public interface IBirthArea
{
    /// <summary>该格是否属于出生区域。</summary>
    bool IsBirthArea(Coord c);
}

/// <summary>
/// 现行出生区域 = 出生棋盘（R32）：格落在任一出生棋盘的外接矩形内（<see cref="MapData.BirthBoardAt"/>）即属于出生区域。
/// 没有棋盘清单的手写地图（测试夹具）退回到出生区本身（<see cref="MapData.BirthZoneOf"/>）——两者取并，不会漏掉任何出生格。
/// </summary>
public sealed class BirthBoardArea : IBirthArea
{
    private readonly MapData _map;

    public BirthBoardArea(MapData map)
    {
        ArgumentNullException.ThrowIfNull(map);
        _map = map;
    }

    /// <inheritdoc/>
    public bool IsBirthArea(Coord c) => _map.BirthBoardAt(c) is not null || _map.BirthZoneOf(c) is not null;
}
