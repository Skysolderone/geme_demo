using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Recruit;
using Siege.Core.Scoring;

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

    // retire-legacy-rules：对局模式（原 Mode，board-link-mode 实现裁决 B1）删除——对局只有链接规则一套，建局恒要求链接图、恰 4 人。

    // restore-go-core-rules 裁决 #4 / #7 / #15：大回合上限、碾压起始大回合、落后者征募补偿开关三项配置整体删除。
    // 终局只剩「只剩一名参赛玩家 / 最后战场结算」两类（见 EndReason），对局 MUST NOT 设置固定总轮数，也 MUST NOT 因势力领先幅度提前结束。
    // 批量跑局的防死循环截断属 Siege.Sim 的技术设施，不是对局配置，MUST NOT 回到这里。

    /// <summary>标准局的匠人征募权重初值（artisan-terrain-edit 裁决 T-1 / R-2：10，待扫档校准）。</summary>
    public const int DefaultArtisanWeight = RecruitWeights.DefaultArtisanWeight;

    /// <summary>
    /// 匠人的征募权重（artisan-terrain-edit R-2；非负整数，0 = 匠人不进池）。其余五种类型的基础权重固定，不随它变化。
    /// 属于对局配置：开局固定、公开、入存档；对局进行中不可改。
    /// </summary>
    public int ArtisanWeight { get; init; } = DefaultArtisanWeight;

    // retire-legacy-maps 3.1（裁决 L1）：原型插旗的冒险概率（FlagRisk / DefaultFlagRisk，flag-contest）整体删除——
    // 原型路径恒为盲选（PrototypeZoneAssignment.AssignBlind），不看已插的旗。

    /// <summary>
    /// 对局内容集（more-pieces-relics D8，match-setup「对局内容集」）：v1 = 原六种棋子 + 原六类信物，v2 = 十 + 十。新局缺省 v2（<see cref="ContentSets.Default"/>）。
    /// 属于对局配置：开局固定、始终公开，入存档、日志首部与批次配置；恢复缺该字段的旧存档按 v1（<see cref="MatchFlow.ContentSetBackfilled"/> 留痕）。
    /// </summary>
    public ContentSet ContentSet { get; init; } = ContentSets.Default;

    /// <summary>
    /// 带入带出开关（carry-in-out，match-setup「带入带出配置」）：是否对本局进行带出结算。缺省关闭。
    /// 属于对局配置：开局固定、始终公开，入存档；恢复缺该字段的旧存档按关闭（<see cref="MatchFlow.CarryInOutBackfilled"/> 留痕）。
    /// </summary>
    public bool CarryInOut { get; init; }

    /// <summary>
    /// 各玩家的带入（每人至多 1 件，缺省全员无带入）。关闭时 MUST 为空。征召签的类型在建局时抽出并写回（<see cref="MatchFlow.Options"/> 里是解析后的值）。
    /// 关闭时，或开启但全员无带入时，对局与引入带入带出之前逐步相同。
    /// </summary>
    public ImmutableSortedDictionary<PlayerId, Carry.CarryIn> CarryIns { get; init; } = ImmutableSortedDictionary<PlayerId, Carry.CarryIn>.Empty;

    /// <summary>决赛倒计时大回合数的缺省值（final-countdown 裁决 W2：2）。</summary>
    public const int DefaultFinalCountdownRounds = 2;

    /// <summary>
    /// 决赛倒计时大回合数 K（final-countdown，match-setup「决赛倒计时配置」、battlefield「决赛倒计时」）：出现等待决赛的玩家后，其余未结算的多人战场最多再打 K 个大回合，
    /// 到期按战场结算的同一比较链强制结算（原因 <see cref="SettlementReason.Expired"/>）。整数 ≥ 0，缺省 2；0 = 不启用（与引入之前逐步相同）；负数建局报错。
    /// 属于对局配置：开局固定、始终公开，入存档（链接段）、日志首部与批次配置；恢复缺该字段的旧存档按 0（<see cref="MatchFlow.FinalCountdownBackfilled"/> 留痕）。
    /// </summary>
    public int FinalCountdownRounds { get; init; } = DefaultFinalCountdownRounds;
}

/// <summary>
/// 插旗阶段的公开视图：只有是否已锁定，<b>没有</b>任何旗数与身份字段——盲选下旗帜互不可见，公开视图 MUST NOT 暴露任何旗数（match-setup「匿名同时插旗」）；
/// 本座位能看到的只有自己的那面旗，见 <see cref="FlagPlanting.ViewFor"/>。retire-legacy-rules：旧模式的每区旗数（<c>FlagCountByZone</c>）随旧模式删除。
/// </summary>
public sealed record FlagPublicView(bool IsLocked);

/// <summary>
/// 某一座位在插旗阶段看到的旗帜（board-link-mode 实现裁决 B2，match-setup「匿名同时插旗」）：出生棋盘数、本座位自己选的那块（未选为 <c>null</c>）；
/// 全部锁定后另含"与自己同一块棋盘的玩家"（含本人，升序），锁定前为空。MUST NOT 含任何其他棋盘上的旗与旗数。
/// </summary>
public sealed record SeatFlagView(PlayerId Seat, int ZoneCount, int? OwnZone, bool IsLocked, ImmutableArray<PlayerId> SameBoard);

/// <summary>
/// 盲选插旗（设计文档 §4.1，board-link-mode 实现裁决 B2，match-setup「匿名同时插旗」）：选中即锁定、旗帜互不可见、公开视图不含旗数；
/// 身份只在锁定后进入对局状态。允许多人同区；时限可配置，单机与 AI 路径配置为立即。
/// </summary>
/// <remarks>
/// 本类不计时：联网宿主在时限到达时调用 <see cref="LockAll"/>；单机路径直接调用。到时仍未插旗的玩家由 <c>flag-pick</c> 子流在全部出生棋盘里均匀指定
/// （每名玩家一次、按编号升序，与他人选了什么无关），避免联网掉线让整局卡死。原型的依次插旗（<see cref="PlantSequentially"/>）
/// 产出与同时插旗完全相同的锁定结果——两条路径只是输入方式不同。retire-legacy-rules：旧模式的非盲选路径（看得见旗数、锁定前可改、超时分到旗数最少的区）删除。
/// </remarks>
public sealed class FlagPlanting
{
    private readonly SortedDictionary<PlayerId, int> _flags = [];
    private readonly ImmutableArray<PlayerId> _players;

    private readonly GameSeed _seed;

    internal FlagPlanting(MapData map, ImmutableArray<PlayerId> players, TimeSpan timeLimit, GameSeed seed = default)
    {
        Map = map;
        _players = players;
        TimeLimit = timeLimit;
        _seed = seed;
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

    /// <summary>公开视图：不含任何旗数。</summary>
    public FlagPublicView PublicView() => new(IsLocked);

    /// <summary>插旗。选中即锁定：已选过的玩家改选、全部锁定后再插都拒绝。</summary>
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

        if (_flags.TryGetValue(player, out int locked))
        {
            // 盲选下选中即锁定（match-setup「匿名同时插旗」，design D4 / Q8）：改选会让玩家看到第二块棋盘的布局。
            throw new SiegeRuleException($"盲选插旗选中即锁定：玩家 {player} 已锁定出生棋盘 {BirthZoneLabel.Of(locked)}，不能改选 {BirthZoneLabel.Of(zone)}。");
        }

        _flags[player] = zone;
    }

    /// <summary>
    /// 某一座位能看到的旗帜（实现裁决 B2）：只有自己那面旗；全部锁定后另含与自己同一块棋盘的玩家。
    /// </summary>
    public SeatFlagView ViewFor(PlayerId seat)
    {
        if (!_players.Contains(seat))
        {
            throw new SiegeRuleException($"玩家 {seat} 不在本局名单中。");
        }

        int? own = FlagOf(seat);
        ImmutableArray<PlayerId> same = IsLocked && own is { } zone
            ? [.. _flags.Where(kv => kv.Value == zone).Select(kv => kv.Key)]
            : [];
        return new SeatFlagView(seat, ZoneCount, own, IsLocked, same);
    }

    /// <summary>某玩家当前的旗帜位置；尚未插旗为 <c>null</c>。只供该玩家本人的界面与锁定后的对局状态读取。</summary>
    public int? FlagOf(PlayerId player) => _flags.TryGetValue(player, out int zone) ? zone : null;

    /// <summary>
    /// 锁定全部旗帜（时限到达或全员确认）。未插旗的玩家由 <c>flag-pick</c> 子流均匀指定。
    /// 返回每名玩家锁定的出生区。
    /// </summary>
    public ImmutableSortedDictionary<PlayerId, int> LockAll()
    {
        if (IsLocked)
        {
            throw new SiegeRuleException("插旗已锁定。");
        }

        // 时限到时未选者用 flag-pick 子流在全部出生棋盘里均匀指定（每名玩家一次、按编号升序，与他人选了什么无关）。
        ImmutableArray<int> picks = PrototypeZoneAssignment.BlindPicks(_seed, _players, ZoneCount);
        for (int i = 0; i < _players.Length; i++)
        {
            PlayerId player = _players[i];
            if (!_flags.ContainsKey(player))
            {
                _flags[player] = picks[i];
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
}
