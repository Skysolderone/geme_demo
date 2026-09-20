using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Determinism;

namespace Siege.Core.Match;

/// <summary>
/// 原型插旗路径下"未由人指定的玩家占哪个出生区"的唯一实现（frontier-map D4 / 裁决 10）。
/// 批量跑局、终端版与图形版三个入口都经 <see cref="MatchFlow.PlantPrototype"/> 到这里，MUST NOT 各写一份。
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>出生区数 ≤ 地图人数上限（标准档：相等）→ 按编号顺排：玩家按编号依次取下一个区号，取到人选的那个区就跳过。
/// 这与引入本类之前三处入口的写法逐项相同——批量侧无人工选择时即 P<i>i</i> → 区 <i>i</i>——既有种子的对局因此逐步不变。</description></item>
/// <item><description>出生区数 &gt; 地图人数上限（边疆档）→ 由对局种子派生独立子流 <see cref="GameSeed.ZonePick"/>，
/// 按玩家编号顺序在尚未被占用的区里均匀抽取，互不同区，也不与人选的区重复（多人同区只由人工选择产生）。</description></item>
/// </list>
/// 判据用的是<b>地图人数上限</b>而不是实际参赛人数：v4 上的 2 / 3 人局区数（4）多于参赛人数，若按参赛人数判会从顺排变成随机，
/// 破坏既有对局的可复现。本类不读规格档——对局规则不感知规格档（D1）。
/// 规格：openspec/changes/frontier-map/specs/match-setup —— Requirement: 原型插旗替代路径
/// </remarks>
public static class PrototypeZoneAssignment
{
    /// <summary>
    /// 给出全部玩家的出生区选择，顺序与 <paramref name="players"/> 相同。<paramref name="manual"/> 是人工指定的那一名玩家及其区号（0 起），
    /// 批量跑局没有人工选择，传 <c>null</c>。
    /// </summary>
    public static ImmutableArray<(PlayerId Player, int Zone)> Assign(
        MapData map, GameSeed seed, IReadOnlyList<PlayerId> players, (PlayerId Player, int Zone)? manual = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(players);
        int zoneCount = map.BirthZones.Length;
        if (zoneCount == 0)
        {
            throw new SiegeRuleException($"地图 {map.Id} 没有出生区，无法插旗。");
        }

        if (manual is { } m && (m.Zone < 0 || m.Zone >= zoneCount || !players.Contains(m.Player)))
        {
            throw new SiegeRuleException($"人工选区不合法：{BirthZoneLabel.Of(m.Zone)}，地图 {map.Id} 共 {zoneCount} 个出生区。");
        }

        return zoneCount > map.MaxPlayers
            ? Seeded(zoneCount, seed, players, manual)
            : Sequential(zoneCount, players, manual);
    }

    private static ImmutableArray<(PlayerId Player, int Zone)> Sequential(
        int zoneCount, IReadOnlyList<PlayerId> players, (PlayerId Player, int Zone)? manual)
    {
        var choices = ImmutableArray.CreateBuilder<(PlayerId Player, int Zone)>(players.Count);
        int next = 0;
        foreach (PlayerId player in players)
        {
            if (manual is { } m && player == m.Player)
            {
                choices.Add(m);
                continue;
            }

            if (manual is { } taken && next == taken.Zone)
            {
                next++;
            }

            choices.Add((player, next % zoneCount));
            next++;
        }

        return choices.ToImmutable();
    }

    private static ImmutableArray<(PlayerId Player, int Zone)> Seeded(
        int zoneCount, GameSeed seed, IReadOnlyList<PlayerId> players, (PlayerId Player, int Zone)? manual)
    {
        RandomStream stream = seed.Stream(GameSeed.ZonePick);
        List<int> free = [.. Enumerable.Range(0, zoneCount).Where(z => manual?.Zone != z)];
        var choices = ImmutableArray.CreateBuilder<(PlayerId Player, int Zone)>(players.Count);
        foreach (PlayerId player in players)
        {
            if (manual is { } m && player == m.Player)
            {
                choices.Add(m);
                continue;
            }

            // 区数 > 人数上限 ≥ 参赛人数，所以空闲区不会抽干；候选按区号升序，抽取只依赖子流。
            int index = stream.NextInt(free.Count);
            choices.Add((player, free[index]));
            free.RemoveAt(index);
        }

        return choices.ToImmutable();
    }
}
