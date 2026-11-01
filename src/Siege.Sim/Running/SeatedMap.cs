using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Sim.Logging;

namespace Siege.Sim.Running;

/// <summary>
/// 回放 / 复盘前按日志首部重做大图落座（big-map D4）：大图首部的 <see cref="LogHeader.MapDigest"/> 是落座图（基图 + 按旗格物化的出生区 / 出生棋盘）的摘要，
/// 比摘要前先用首部的 <see cref="LogHeader.Flags"/> 与 <see cref="LogHeader.Zones"/> 在基图上经 <see cref="FlagRules.Seat"/>（唯一实现）重做落座。小图或首部没有旗格时原样返回。
/// 重建对局仍传基图：盲选按同一种子会重做出同样的旗格。
/// </summary>
internal static class SeatedMap
{
    public static MapData Of(MapData map, LogHeader header)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(header);
        if (map.Tier != MapTier.Large || header.Flags is not { Count: > 0 } flags || !map.BirthZones.IsDefaultOrEmpty)
        {
            return map;
        }

        if (flags.Count != header.Zones.Count || flags.Any(f => f == "-"))
        {
            throw new FormatException($"日志首部的旗格（{flags.Count} 个）与锁定出生区（{header.Zones.Count} 个）对不上，无法重做落座。");
        }

        Coord[] byZone = [.. Enumerable.Range(0, flags.Count).OrderBy(i => header.Zones[i]).Select(i => Coord.Parse(flags[i]))];
        return FlagRules.Seat(map, byZone);
    }
}
