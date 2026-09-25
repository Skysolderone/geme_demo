using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Determinism;

namespace Siege.Core.Match;

/// <summary>
/// 原型插旗路径下"未由人指定的玩家占哪个出生区"的唯一实现（frontier-map D4 / 裁决 10；flag-contest D1）。
/// 批量跑局、终端版与图形版三个入口都经 <see cref="MatchFlow.PlantPrototype"/> 到这里，MUST NOT 各写一份。
/// </summary>
/// <remarks>
/// 未由人指定的玩家按编号顺序依次选区，看得到在它之前已插下的旗——人工选择先于全部 AI 插下，所以编号在人之前的 AI 也看得到人的旗。每名这样的玩家：
/// <list type="bullet">
/// <item><description><b>冒险</b>（flag-contest D1）：此前已有旗时，先用独立子流 <see cref="GameSeed.FlagRisk"/> 抽一次 <c>[0,100)</c>，小于冒险概率 p
/// 就在"已有人的出生区"（按区号升序去重）里用同一子流均匀选一个；此前没有任何旗时不抽、直接走下面的原规则。</description></item>
/// <item><description>否则走<b>原规则</b>占用空闲出生区：
/// 出生区数 ≤ 地图人数上限（标准档：相等）→ 按编号顺排：玩家按编号依次取下一个区号，取到人选的那个区就跳过；
/// 出生区数 &gt; 地图人数上限（边疆档）→ 由对局种子派生独立子流 <see cref="GameSeed.ZonePick"/>，在尚未被占用的区里均匀抽取。</description></item>
/// </list>
/// 原规则的状态（顺排游标、<see cref="GameSeed.ZonePick"/> 子流、空闲表）<b>只在走原规则时推进</b>；冒险只加入已有人的区、不会新占空闲区，
/// 所以 p = 0 时冒险子流虽被抽取、锁定结果与引入冒险概率之前逐项相同——批量侧无人工选择时即 P<i>i</i> → 区 <i>i</i>，既有种子的对局逐步不变。
/// 判据用的是<b>地图人数上限</b>而不是实际参赛人数：v4 上的 2 / 3 人局区数（4）多于参赛人数，若按参赛人数判会从顺排变成随机，
/// 破坏既有对局的可复现。本类不读规格档——对局规则不感知规格档（D1）。
/// 规格：openspec/changes/frontier-map、flag-contest/specs/match-setup —— Requirement: 原型插旗替代路径
/// </remarks>
public static class PrototypeZoneAssignment
{
    /// <summary>冒险抽签的分母：冒险概率是 <c>[0,100]</c> 的整数百分比，抽 <c>[0,100)</c> 小于 p 即冒险。</summary>
    private const int RiskScale = 100;

    /// <summary>冒险概率须在 <c>[0,100]</c>（flag-contest D2）；越界抛 <see cref="ArgumentOutOfRangeException"/>。建局与选区共用这一处判定。</summary>
    public static void RequireValidFlagRisk(int flagRisk)
    {
        if (flagRisk is < 0 or > RiskScale)
        {
            throw new ArgumentOutOfRangeException(nameof(flagRisk), flagRisk, "冒险概率须为 0–100 的整数百分比。");
        }
    }

    /// <summary>
    /// 给出全部玩家的出生区选择，顺序与 <paramref name="players"/> 相同。<paramref name="flagRisk"/> 是本局的冒险概率（0–100，由调用方传入对局配置值）；
    /// <paramref name="manual"/> 是人工指定的那一名玩家及其区号（0 起），批量跑局没有人工选择，传 <c>null</c>。
    /// </summary>
    public static ImmutableArray<(PlayerId Player, int Zone)> Assign(
        MapData map, GameSeed seed, IReadOnlyList<PlayerId> players, int flagRisk, (PlayerId Player, int Zone)? manual = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(players);
        RequireValidFlagRisk(flagRisk);
        int zoneCount = map.BirthZones.Length;
        if (zoneCount == 0)
        {
            throw new SiegeRuleException($"地图 {map.Id} 没有出生区，无法插旗。");
        }

        if (manual is { } m && (m.Zone < 0 || m.Zone >= zoneCount || !players.Contains(m.Player)))
        {
            throw new SiegeRuleException($"人工选区不合法：{BirthZoneLabel.Of(m.Zone)}，地图 {map.Id} 共 {zoneCount} 个出生区。");
        }

        RandomStream risk = seed.Stream(GameSeed.FlagRisk);
        // 原规则的状态：区数 > 人数上限时才派生 zone-pick 子流（抽空闲表），否则用顺排游标。只在走原规则时推进。
        RandomStream? pick = zoneCount > map.MaxPlayers ? seed.Stream(GameSeed.ZonePick) : null;
        List<int> free = [.. Enumerable.Range(0, zoneCount).Where(z => manual?.Zone != z)];
        int next = 0;

        // 已有人的出生区（升序去重）：人工选择先插下。
        var planted = new SortedSet<int>();
        if (manual is { } first)
        {
            planted.Add(first.Zone);
        }

        var choices = ImmutableArray.CreateBuilder<(PlayerId Player, int Zone)>(players.Count);
        foreach (PlayerId player in players)
        {
            if (manual is { } human && player == human.Player)
            {
                choices.Add(human);
                continue;
            }

            int zone;
            if (planted.Count > 0 && risk.NextInt(RiskScale) < flagRisk)
            {
                zone = planted.ElementAt(risk.NextInt(planted.Count));
            }
            else if (pick is not null)
            {
                // 区数 > 人数上限 ≥ 参赛人数，所以空闲区不会抽干；候选按区号升序，抽取只依赖子流。
                int index = pick.NextInt(free.Count);
                zone = free[index];
                free.RemoveAt(index);
            }
            else
            {
                if (manual is { } taken && next == taken.Zone)
                {
                    next++;
                }

                zone = next % zoneCount;
                next++;
            }

            planted.Add(zone);
            choices.Add((player, zone));
        }

        return choices.ToImmutable();
    }
}
