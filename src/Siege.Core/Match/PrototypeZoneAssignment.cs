using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Determinism;

namespace Siege.Core.Match;

/// <summary>
/// 盲选插旗下"每名玩家最终坐哪个出生座位"的唯一实现（frontier-map D4 / 裁决 10；board-link-mode 实现裁决 B2；seat-gap-25 E4 / 裁决 S2）。
/// 正式插旗的锁定（<see cref="FlagPlanting.LockAll"/>）与原型路径（<see cref="AssignBlind"/>，批量跑局、终端版与图形版三个入口都经 <see cref="MatchFlow.PlantPrototype"/> 到这里）共用，MUST NOT 各写一份。
/// </summary>
/// <remarks>
/// retire-legacy-maps 3.1（裁决 L1）：旧模式的选区（冒险概率 flag-contest D1、标准档顺排、边疆档 zone-pick 种子选区，原 <c>Assign</c> 与 <c>RequireValidFlagRisk</c>）
/// 随旧地图删除；原型路径只剩盲选。seat-gap-25 E4：座位互斥——撞座位的后插旗者与未插旗者改派到空座位（原"每人在全部出生棋盘里各抽一次、可与他人同区"作废）。
/// 规格：openspec/specs/match-setup —— Requirement: 原型插旗替代路径
/// </remarks>
public static class PrototypeZoneAssignment
{
    /// <summary>
    /// 锁定时的座位分配（seat-gap-25 E4，match-setup「匿名同时插旗」）：<paramref name="planted"/> 是已插的旗，按插旗先后排列。
    /// ① 按插旗先后逐个处理：座位还空着即坐下；已被更早插旗者占用即记为撞座位。② 撞座位者与 <paramref name="players"/> 里未插旗的玩家合在一起、按玩家编号升序，
    /// 各从子流 <see cref="GameSeed.FlagPick"/> 抽一次 <c>NextInt(当前空座位数)</c>，在按编号升序排好的空座位里取一个坐下（坐下后该座位不再空）。
    /// 结果各不相同（座位数不少于人数时）；同种子、同插旗先后与同选择结果相同。只有撞座位者与未插旗者消费随机，没有需要改派的玩家时不消费。
    /// 返回每名玩家的座位，顺序同 <paramref name="players"/>。
    /// </summary>
    /// <exception cref="SiegeRuleException">没有出生座位，或需要改派时已没有空座位（座位数少于人数）。</exception>
    public static ImmutableArray<int> BlindPicks(
        GameSeed seed, IReadOnlyList<PlayerId> players, int zoneCount, IReadOnlyList<(PlayerId Player, int Zone)> planted)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(planted);
        if (zoneCount <= 0)
        {
            throw new SiegeRuleException("没有出生座位，无法盲选。");
        }

        var seats = new Dictionary<PlayerId, int>();
        var occupied = new SortedSet<int>();
        var pending = new List<PlayerId>();
        foreach ((PlayerId player, int zone) in planted)
        {
            if (occupied.Add(zone))
            {
                seats[player] = zone;
            }
            else
            {
                pending.Add(player);
            }
        }

        pending.AddRange(players.Where(p => !planted.Any(x => x.Player == p)));
        pending.Sort();
        if (pending.Count > 0)
        {
            RandomStream pick = seed.Stream(GameSeed.FlagPick);
            foreach (PlayerId player in pending)
            {
                int[] empty = [.. Enumerable.Range(0, zoneCount).Where(z => !occupied.Contains(z))];
                if (empty.Length == 0)
                {
                    throw new SiegeRuleException($"出生座位只有 {zoneCount} 个，没有空座位可改派给玩家 {player}。");
                }

                int zone = empty[pick.NextInt(empty.Length)];
                occupied.Add(zone);
                seats[player] = zone;
            }
        }

        return [.. players.Select(p => seats[p])];
    }

    /// <summary>
    /// 链接模式的原型插旗路径：<paramref name="manual"/> 是人工指定的那一名玩家及其座位编号（0 起），其余玩家经 <see cref="BlindPicks"/> 改派到空座位（seat-gap-25 E4）。
    /// 产出即最终座位（各不相同），随后依次插旗时不再撞座位、锁定时不再消费随机。顺序与 <paramref name="players"/> 相同。
    /// </summary>
    public static ImmutableArray<(PlayerId Player, int Zone)> AssignBlind(
        MapData map, GameSeed seed, IReadOnlyList<PlayerId> players, (PlayerId Player, int Zone)? manual = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(players);
        int zoneCount = map.BirthZones.Length;
        if (manual is { } m && (m.Zone < 0 || m.Zone >= zoneCount || !players.Contains(m.Player)))
        {
            throw new SiegeRuleException($"人工选区不合法：{BirthZoneLabel.Of(m.Zone)}，地图 {map.Id} 共 {zoneCount} 个出生座位。");
        }

        ImmutableArray<int> picks = BlindPicks(seed, players, zoneCount, manual is { } human ? [human] : []);
        return [.. players.Select((p, i) => (p, picks[i]))];
    }
}
