using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Match;

/// <summary>对局选项。</summary>
public sealed record MatchOptions
{
    /// <summary>联网模式的默认插旗时限（设计文档 §4.1）。</summary>
    public static readonly TimeSpan DefaultFlagTimeLimit = TimeSpan.FromSeconds(15);

    /// <summary>默认选项：15 秒插旗时限。</summary>
    public static readonly MatchOptions Default = new();

    /// <summary>单机 / AI 路径：插旗时限为立即，批量跑局不被插旗阶段阻塞（裁决记录 1）。</summary>
    public static readonly MatchOptions Immediate = new() { FlagTimeLimit = TimeSpan.Zero };

    /// <summary>插旗时限。<see cref="TimeSpan.Zero"/> 表示立即：宿主无需等待即可锁定。规则内核不持有计时器，到时由宿主调用 <see cref="FlagPlanting.LockAll"/>。</summary>
    public TimeSpan FlagTimeLimit { get; init; } = DefaultFlagTimeLimit;
}

/// <summary>插旗阶段的匿名公开视图：每个出生区上有几面旗，<b>没有</b>任何身份字段（设计文档 §4.1）。</summary>
public sealed record FlagPublicView(ImmutableArray<int> FlagCountByZone, bool IsLocked)
{
    /// <summary>某出生区上的旗帜数。</summary>
    public int FlagsAt(int zone) => FlagCountByZone[zone];
}

/// <summary>
/// 匿名同时插旗（设计文档 §4.1）。旗帜只记位置；公开视图只暴露每区旗数，身份只在锁定后进入对局状态。
/// 锁定前可任意更换；允许多人同区；时限可配置，单机与 AI 路径配置为立即。
/// </summary>
/// <remarks>
/// 本类不计时：联网宿主在时限到达时调用 <see cref="LockAll"/>；单机路径直接调用。到时仍未插旗的玩家按确定性规则
/// 分配到旗数最少、编号最小的出生区，避免联网掉线让整局卡死。原型的依次插旗（<see cref="PlantSequentially"/>）
/// 产出与同时插旗完全相同的锁定结果——两条路径只是输入方式不同。
/// </remarks>
public sealed class FlagPlanting
{
    private readonly SortedDictionary<PlayerId, int> _flags = [];
    private readonly ImmutableArray<PlayerId> _players;

    internal FlagPlanting(MapData map, ImmutableArray<PlayerId> players, TimeSpan timeLimit)
    {
        Map = map;
        _players = players;
        TimeLimit = timeLimit;
    }

    /// <summary>地图（地形、出生区、信物格位置全部公开）。</summary>
    public MapData Map { get; }

    /// <summary>配置的时限；<see cref="TimeSpan.Zero"/> 为立即。</summary>
    public TimeSpan TimeLimit { get; }

    /// <summary>是否立即模式（无需等待）。</summary>
    public bool IsImmediate => TimeLimit == TimeSpan.Zero;

    /// <summary>是否已锁定。</summary>
    public bool IsLocked { get; private set; }

    /// <summary>出生区数量。</summary>
    public int ZoneCount => Map.BirthZones.Length;

    /// <summary>匿名公开视图。</summary>
    public FlagPublicView PublicView()
    {
        int[] counts = new int[ZoneCount];
        foreach (int zone in _flags.Values)
        {
            counts[zone]++;
        }

        return new FlagPublicView([.. counts], IsLocked);
    }

    /// <summary>插旗或更换插旗位置。锁定后拒绝。</summary>
    public void Plant(PlayerId player, int zone)
    {
        if (IsLocked)
        {
            throw new SiegeRuleException("插旗已锁定，不能再更换位置。");
        }

        if (!_players.Contains(player))
        {
            throw new SiegeRuleException($"玩家 {player} 不在本局名单中。");
        }

        if (zone < 0 || zone >= ZoneCount)
        {
            throw new ArgumentOutOfRangeException(nameof(zone), zone, $"出生区编号须在 0..{ZoneCount - 1}。");
        }

        _flags[player] = zone;
    }

    /// <summary>某玩家当前的旗帜位置；尚未插旗为 <c>null</c>。只供该玩家本人的界面与锁定后的对局状态读取。</summary>
    public int? FlagOf(PlayerId player) => _flags.TryGetValue(player, out int zone) ? zone : null;

    /// <summary>
    /// 锁定全部旗帜（时限到达或全员确认）。未插旗的玩家分配到旗数最少、编号最小的出生区。
    /// 返回每名玩家锁定的出生区。
    /// </summary>
    public ImmutableSortedDictionary<PlayerId, int> LockAll()
    {
        if (IsLocked)
        {
            throw new SiegeRuleException("插旗已锁定。");
        }

        foreach (PlayerId player in _players)
        {
            if (!_flags.ContainsKey(player))
            {
                _flags[player] = LeastPopulatedZone();
            }
        }

        IsLocked = true;
        return _flags.ToImmutableSortedDictionary();
    }

    /// <summary>原型替代路径：由 AI 或调试界面依次指定出生区并立即锁定，产出与同时插旗完全相同的状态。</summary>
    public ImmutableSortedDictionary<PlayerId, int> PlantSequentially(IEnumerable<(PlayerId Player, int Zone)> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        foreach ((PlayerId player, int zone) in choices)
        {
            Plant(player, zone);
        }

        return LockAll();
    }

    /// <summary>恢复存档：直接写入已锁定的结果。</summary>
    internal void RestoreLocked(IEnumerable<KeyValuePair<PlayerId, int>> flags)
    {
        foreach ((PlayerId player, int zone) in flags)
        {
            _flags[player] = zone;
        }

        IsLocked = true;
    }

    private int LeastPopulatedZone()
    {
        int[] counts = new int[ZoneCount];
        foreach (int zone in _flags.Values)
        {
            counts[zone]++;
        }

        int best = 0;
        for (int i = 1; i < counts.Length; i++)
        {
            if (counts[i] < counts[best])
            {
                best = i;
            }
        }

        return best;
    }
}
