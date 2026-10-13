using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Determinism;

namespace Siege.Core.Match;

/// <summary>
/// 原型插旗路径下"未由人指定的玩家占哪个出生区"的唯一实现（frontier-map D4 / 裁决 10；board-link-mode 实现裁决 B2）。
/// 批量跑局、终端版与图形版三个入口都经 <see cref="MatchFlow.PlantPrototype"/> 到这里，MUST NOT 各写一份。
/// </summary>
/// <remarks>
/// retire-legacy-maps 3.1（裁决 L1）：旧模式的选区（冒险概率 flag-contest D1、标准档顺排、边疆档 zone-pick 种子选区，原 <c>Assign</c> 与 <c>RequireValidFlagRisk</c>）
/// 随旧地图删除；原型路径只剩盲选（<see cref="AssignBlind"/>），正式插旗时限到时的兜底同用 <see cref="BlindPicks"/>。
/// 规格：openspec/specs/match-setup —— Requirement: 原型插旗替代路径
/// </remarks>
public static class PrototypeZoneAssignment
{
    /// <summary>
    /// 链接模式的盲选（board-link-mode 实现裁决 B2，match-setup「原型插旗替代路径」）：<paramref name="players"/> 中每名玩家按给定次序
    /// 各从独立子流 <see cref="GameSeed.FlagPick"/> 抽一次 <c>[0, <paramref name="zoneCount"/>)</c>。不看任何已插的旗，结果可能与他人或人工选择相同。
    /// 每人恰抽一次、与是否被人工指定无关：某名玩家的结果只取决于（对局种子, 他在名单里的位置），改变人工选择不改变任何 AI 的结果。
    /// 正式插旗时限到时的兜底（<see cref="FlagPlanting.LockAll"/>）与原型路径（<see cref="AssignBlind"/>）共用这一处。
    /// </summary>
    public static ImmutableArray<int> BlindPicks(GameSeed seed, IReadOnlyList<PlayerId> players, int zoneCount)
    {
        ArgumentNullException.ThrowIfNull(players);
        if (zoneCount <= 0)
        {
            throw new SiegeRuleException("没有出生棋盘，无法盲选。");
        }

        RandomStream pick = seed.Stream(GameSeed.FlagPick);
        ImmutableArray<int>.Builder zones = ImmutableArray.CreateBuilder<int>(players.Count);
        for (int i = 0; i < players.Count; i++)
        {
            zones.Add(pick.NextInt(zoneCount));
        }

        return zones.MoveToImmutable();
    }

    /// <summary>
    /// 链接模式的原型插旗路径：<paramref name="manual"/> 是人工指定的那一名玩家及其棋盘编号（0 起），其余玩家取 <see cref="BlindPicks"/> 的结果。
    /// 顺序与 <paramref name="players"/> 相同。
    /// </summary>
    public static ImmutableArray<(PlayerId Player, int Zone)> AssignBlind(
        MapData map, GameSeed seed, IReadOnlyList<PlayerId> players, (PlayerId Player, int Zone)? manual = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(players);
        int zoneCount = map.BirthZones.Length;
        if (manual is { } m && (m.Zone < 0 || m.Zone >= zoneCount || !players.Contains(m.Player)))
        {
            throw new SiegeRuleException($"人工选区不合法：{BirthZoneLabel.Of(m.Zone)}，地图 {map.Id} 共 {zoneCount} 块出生棋盘。");
        }

        ImmutableArray<int> picks = BlindPicks(seed, players, zoneCount);
        return [.. players.Select((p, i) => manual is { } human && human.Player == p ? human : (p, picks[i]))];
    }
}
