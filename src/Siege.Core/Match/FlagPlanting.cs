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

    // restore-go-core-rules 裁决 #4 / #7 / #15：旧的"固定总轮数终局"、碾压起始大回合、落后者征募补偿开关三项配置整体删除。
    // 终局只剩「只剩一名参赛玩家 / 最后战场结算」两类（见 EndReason），对局 MUST NOT 以固定轮数作为正常终局，也 MUST NOT 因势力领先幅度提前结束。
    // 批量跑局的防死循环截断属 Siege.Sim 的技术设施，不是对局配置，MUST NOT 回到这里。
    // cycle-stop 裁决 C2 / C3 / D4：另有一道"大回合上限"安全网（RoundCap）——不是终局条件，而是战场结算判定链的第 (c) 条：到达上限时按势力强制结算，
    // 终局原因仍是"最后战场结算"。只在无人收尾（整轮 Pass / 填满都到不了）时生效，正常对局 SHOULD 在到达前收尾。

    /// <summary>小图的大回合上限安全网缺省值（cycle-stop 裁决 D4：简单难度自然终局最长 94 个大回合，安全网只兜不收尾的局）。</summary>
    public const int DefaultRoundCap = 100;

    /// <summary>大图的缺省对局时长（big-map 裁决 6 原为 60；large-60，2026-11-07：大图缩到 60×60 后沿用小图上限 <see cref="DefaultRoundCap"/>），到点按势力结算，沿用上限安全网机制。</summary>
    public const int DefaultRoundCapLarge = DefaultRoundCap;

    /// <summary>按地图档位取大回合上限的缺省值（big-map D6）：小图 <see cref="DefaultRoundCap"/> 100、大图 <see cref="DefaultRoundCapLarge"/>（large-60 起同为 100）。</summary>
    public static int DefaultRoundCapFor(MapTier tier) => tier == MapTier.Large ? DefaultRoundCapLarge : DefaultRoundCap;

    /// <summary>
    /// 大回合上限安全网（cycle-stop D2，match-setup「大回合上限配置」；整数 ≥ 0，0 = 不启用）：第 R 大回合结束时若 R ≥ 上限、战场未结算且在局 ≥ 2，
    /// 按 battlefield「战场结算」同一判定链强制结算，原因记"上限"（<see cref="SettlementReason.RoundCap"/>）。
    /// 属于对局配置：开局固定、始终公开，入存档、日志首部与批次配置；负数建局报错；恢复缺该字段的旧存档按 <see cref="DefaultRoundCap"/>
    /// 回填（<see cref="MatchFlow.RoundCapBackfilled"/> 留痕——安全网默认开启，与 open-map 之前决赛倒计时 K 按 0 的做法相反）。
    /// big-map D6：<c>null</c>（缺省）= 建局时按地图档位取 <see cref="DefaultRoundCapFor"/>（小图 100、大图 60），显式给值优先；<see cref="MatchFlow.RoundCap"/> 给出解析后的值，
    /// 存档 / 日志 / 批次配置里存的仍是解析后的整数。不随难度自动变化。
    /// </summary>
    public int? RoundCap { get; init; }

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

    /// <summary>本局计分规则；新局默认采用阵型阶数。</summary>
    public ScoringVersion ScoringVersion { get; init; } = ScoringVersions.Default;

    /// <summary>
    /// 带入带出开关（carry-in-out，match-setup「带入带出配置」）：是否对本局进行带出结算。缺省关闭。
    /// 属于对局配置：开局固定、始终公开，入存档；恢复缺该字段的旧存档按关闭（<see cref="MatchFlow.CarryInOutBackfilled"/> 留痕）。
    /// </summary>
    public bool CarryInOut { get; init; }

    /// <summary>
    /// 各玩家的带入清单（relic-redesign R14 / R15，D5：每人至多 5 件、其中至多 2 件保险；缺省全员不带入）。关闭时 MUST 为空，空清单不列出。
    /// 清单是私有信息（R22）：挂出之前只有本人可见，不进任何公开视图；存档与跑局日志记录它（二者不是对局内的公开信息）。
    /// 关闭时，或开启但全员不带入时，对局与引入带入之前逐步相同。
    /// </summary>
    public ImmutableSortedDictionary<PlayerId, PieceRelics.RelicLoadout> CarryIns { get; init; } = ImmutableSortedDictionary<PlayerId, PieceRelics.RelicLoadout>.Empty;
}

/// <summary>
/// 插旗阶段的公开视图：只有是否已锁定，<b>没有</b>任何旗数与身份字段——盲选下旗帜互不可见，公开视图 MUST NOT 暴露任何旗数（match-setup「匿名同时插旗」）；
/// 本座位能看到的只有自己的那面旗，见 <see cref="FlagPlanting.ViewFor"/>。retire-legacy-rules：旧模式的每区旗数（<c>FlagCountByZone</c>）随旧模式删除。
/// </summary>
public sealed record FlagPublicView(bool IsLocked);

/// <summary>
/// 某一座位在插旗阶段看到的旗帜（board-link-mode 实现裁决 B2，match-setup「匿名同时插旗」）：出生棋盘数、本座位自己选的那块（未选为 <c>null</c>；
/// 锁定后为改派后的座位）；全部锁定后另含"与自己同一块棋盘的玩家"（含本人，升序），锁定前为空。MUST NOT 含任何其他棋盘上的旗与旗数。
/// seat-gap-25 E4 起座位互斥，锁定后这一项只有本人（测试夹具经调试入口直接写入同座位的局面除外）。
/// </summary>
public sealed record SeatFlagView(PlayerId Seat, int ZoneCount, int? OwnZone, bool IsLocked, ImmutableArray<PlayerId> SameBoard)
{
    /// <summary>大图（big-map D3）：本座位自己插的旗格（锁定后为改派后的旗格）；未插或小图为 <c>null</c>。与 <see cref="OwnZone"/> 同一信息边界——只给本人。</summary>
    public Coord? OwnCell { get; init; }
}

/// <summary>
/// 盲选插旗（设计文档 §4.1，board-link-mode 实现裁决 B2，match-setup「匿名同时插旗」）：选中即锁定、旗帜互不可见、公开视图不含旗数；
/// 身份只在锁定后进入对局状态。插旗时仍允许选别人已选的座位（盲选不泄露信息），锁定时座位互斥（seat-gap-25 E4）；时限可配置，单机与 AI 路径配置为立即。
/// </summary>
/// <remarks>
/// 本类不计时：联网宿主在时限到达时调用 <see cref="LockAll"/>；单机路径直接调用。锁定时按插旗先后，撞座位的后插旗者与到时仍未插旗的玩家
/// 由 <c>flag-pick</c> 子流改派到空座位（按编号升序、每人一次，<see cref="PrototypeZoneAssignment.BlindPicks"/>），避免联网掉线让整局卡死。原型的依次插旗（<see cref="PlantSequentially"/>）
/// 产出与同时插旗完全相同的锁定结果——两条路径只是输入方式不同。retire-legacy-rules：旧模式的非盲选路径（看得见旗数、锁定前可改、超时分到旗数最少的区）删除。
/// </remarks>
public sealed class FlagPlanting
{
    private readonly SortedDictionary<PlayerId, int> _flags = [];

    /// <summary>大图（big-map D3）：各玩家的旗格；锁定后为改派后的旗格。小图恒空。</summary>
    private readonly SortedDictionary<PlayerId, Coord> _cells = [];

    /// <summary>插旗先后（<see cref="Plant"/> / <see cref="PlantAt"/> 的调用顺序）：锁定时撞座位 / 旗太近按它判先后（seat-gap-25 E4；big-map D3）。</summary>
    private readonly List<PlayerId> _plantOrder = [];

    private ImmutableArray<Coord>? _legalCells;

    private readonly ImmutableArray<PlayerId> _players;

    private readonly GameSeed _seed;

    internal FlagPlanting(MapData map, ImmutableArray<PlayerId> players, TimeSpan timeLimit, GameSeed seed = default)
    {
        Map = map;
        _players = players;
        TimeLimit = timeLimit;
        _seed = seed;
    }

    /// <summary>地图（地形、出生区全部公开）。</summary>
    public MapData Map { get; }

    /// <summary>配置的时限；<see cref="TimeSpan.Zero"/> 为立即。</summary>
    public TimeSpan TimeLimit { get; }

    /// <summary>是否立即模式（无需等待）。</summary>
    public bool IsImmediate => TimeLimit == TimeSpan.Zero;

    /// <summary>是否已锁定。</summary>
    public bool IsLocked { get; private set; }

    /// <summary>出生区数量：小图 = 地图的出生座位数；大图锁定时才物化出生区，这里按人数给出（每人一个）。</summary>
    public int ZoneCount => IsLarge ? _players.Length : Map.BirthZones.Length;

    /// <summary>是否大图自由插旗（big-map D3）：旗是旗格而不是座位编号，入口只允许 <see cref="PlantAt"/>。</summary>
    public bool IsLarge => Map.Tier == MapTier.Large;

    /// <summary>大图的全部合规旗格（<see cref="FlagRules.LegalFlagCells"/>，按基图一次算出；小图为空）。地图派生量、全公开。</summary>
    public ImmutableArray<Coord> LegalCells => _legalCells ??= FlagRules.LegalFlagCells(Map);

    /// <summary>公开视图：不含任何旗数。</summary>
    public FlagPublicView PublicView() => new(IsLocked);

    /// <summary>插旗（小图：出生座位编号）。选中即锁定：已选过的玩家改选、全部锁定后再插都拒绝。大图拒绝（用 <see cref="PlantAt"/>）。</summary>
    public void Plant(PlayerId player, int zone)
    {
        if (IsLarge)
        {
            throw new SiegeRuleException($"地图 {Map.Id} 是大图，插旗须给旗格（PlantAt），不是出生座位编号 {BirthZoneLabel.Of(zone)}。");
        }

        RequireCanPlant(player);
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
        _plantOrder.Add(player);
    }

    /// <summary>
    /// 大图自由插旗（big-map 裁决 4，D3）：点任一合规旗格（<see cref="FlagRules.IsLegalFlagCell"/>）即旗，选中即锁定。插旗时不看他人的旗（与已插旗者太近照样接受，锁定时按先后改派）。
    /// 小图拒绝（用 <see cref="Plant"/>）。
    /// </summary>
    public void PlantAt(PlayerId player, Coord cell)
    {
        if (!IsLarge)
        {
            throw new SiegeRuleException($"地图 {Map.Id} 是小图，插旗须给出生座位编号（Plant），不是旗格 {cell.ToNotation()}。");
        }

        RequireCanPlant(player);
        if (FlagRules.FlagCellProblem(Map, cell) is { } problem)
        {
            // relic-sites 裁决 9：原因文案由 FlagRules 唯一给出（压到遗址 / 压到平台 / 出生圈不足……各自可分辨），这里不复刻判据。
            throw new SiegeRuleException($"{cell.ToNotation()} 不是合规旗格：{problem}");
        }

        if (_cells.TryGetValue(player, out Coord plantedCell))
        {
            // 盲选下选中即锁定（match-setup「匿名同时插旗」）：与小图 Plant 同一道门，放在参数校验之后。
            throw new SiegeRuleException($"盲选插旗选中即锁定：玩家 {player} 已把旗插在 {plantedCell.ToNotation()}，不能改选 {cell.ToNotation()}。");
        }

        _cells[player] = cell;
        _plantOrder.Add(player);
    }

    /// <summary>两档共用的前置：未锁定、玩家在名单。"已插过"的判定留在各入口的参数校验之后——小图 <see cref="Plant"/> 的判定顺序与报文与引入大图之前逐字相同。</summary>
    private void RequireCanPlant(PlayerId player)
    {
        if (IsLocked)
        {
            throw new SiegeRuleException("插旗已锁定，不能再更换位置。");
        }

        if (!_players.Contains(player))
        {
            throw new SiegeRuleException($"玩家 {player} 不在本局名单中。");
        }
    }

    /// <summary>
    /// 某一座位能看到的旗帜（实现裁决 B2）：只有自己那面旗；全部锁定后另含与自己同一块棋盘的玩家（seat-gap-25 E4 起座位互斥，正式路径锁定后只有本人）。
    /// 大图另给本人的旗格（<see cref="SeatFlagView.OwnCell"/>）；他人的旗格在锁定前对任何人不可见（big-map D3）。
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
        return new SeatFlagView(seat, ZoneCount, own, IsLocked, same) { OwnCell = FlagCellOf(seat) };
    }

    /// <summary>某玩家当前的旗帜位置（出生区编号）；尚未插旗为 <c>null</c>（大图锁定前恒为 <c>null</c>，锁定时才分到出生区编号）。只供该玩家本人的界面与锁定后的对局状态读取。</summary>
    public int? FlagOf(PlayerId player) => _flags.TryGetValue(player, out int zone) ? zone : null;

    /// <summary>大图：某玩家当前的旗格；尚未插旗或小图为 <c>null</c>。锁定前只供本人读取，锁定后随出生区一起公开。</summary>
    public Coord? FlagCellOf(PlayerId player) => _cells.TryGetValue(player, out Coord cell) ? cell : null;

    /// <summary>大图锁定后按出生区编号排列的旗格（出生区 z 的旗心）；小图或未锁定为空。</summary>
    public ImmutableArray<Coord> LockedCells =>
        IsLocked && IsLarge ? [.. _flags.OrderBy(kv => kv.Value).Select(kv => _cells[kv.Key])] : [];

    /// <summary>
    /// 锁定全部旗帜（时限到达或全员确认）。座位互斥（seat-gap-25 E4）：按插旗先后，撞座位的后插旗者与未插旗的玩家由 <c>flag-pick</c> 子流改派到空座位
    /// （唯一实现 <see cref="PrototypeZoneAssignment.BlindPicks"/>）。返回每名玩家锁定的出生区。
    /// </summary>
    public ImmutableSortedDictionary<PlayerId, int> LockAll()
    {
        if (IsLocked)
        {
            throw new SiegeRuleException("插旗已锁定。");
        }

        if (IsLarge)
        {
            LockAllLarge();
        }
        else
        {
            ImmutableArray<int> seats = PrototypeZoneAssignment.BlindPicks(_seed, _players, ZoneCount, [.. _plantOrder.Select(p => (p, _flags[p]))]);
            for (int i = 0; i < _players.Length; i++)
            {
                _flags[_players[i]] = seats[i];
            }
        }

        IsLocked = true;
        return _flags.ToImmutableSortedDictionary();
    }

    /// <summary>
    /// 大图的锁定（big-map D3）：① 到时仍未插旗的玩家按编号升序各从 <c>flag-pick</c> 子流在合规旗格里均匀抽一格（与小图"未选者由 flag-pick 指定"同口径；没有未插旗者不消费），
    /// 排在已插旗者之后；② 按插旗先后逐个确认 / 改派（<see cref="FlagRules.Reassign"/>，不消费随机）；③ 按玩家编号升序给出生区编号 z = 0..n−1（D4）。
    /// </summary>
    private void LockAllLarge()
    {
        ImmutableArray<Coord> legal = LegalCells;
        var order = new List<PlayerId>(_plantOrder);
        PlayerId[] pending = [.. _players.Where(p => !_cells.ContainsKey(p)).Order()];
        if (pending.Length > 0)
        {
            if (legal.IsEmpty)
            {
                throw new SiegeRuleException($"地图 {Map.Id} 没有任何合规旗格，无法为未插旗的玩家指定旗格。");
            }

            RandomStream pick = _seed.Stream(GameSeed.FlagPick);
            foreach (PlayerId player in pending)
            {
                _cells[player] = legal[pick.NextInt(legal.Length)];
                order.Add(player);
            }
        }

        ImmutableArray<Coord> confirmed = FlagRules.Reassign(legal, [.. order.Select(p => _cells[p])]);
        for (int i = 0; i < order.Count; i++)
        {
            _cells[order[i]] = confirmed[i];
        }

        for (int i = 0; i < _players.Length; i++)
        {
            _flags[_players[i]] = i;
        }
    }

    /// <summary>原型替代路径（小图）：由 AI 或调试界面依次指定出生区并立即锁定，产出与同时插旗完全相同的状态。</summary>
    public ImmutableSortedDictionary<PlayerId, int> PlantSequentially(IEnumerable<(PlayerId Player, int Zone)> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        foreach ((PlayerId player, int zone) in choices)
        {
            Plant(player, zone);
        }

        return LockAll();
    }

    /// <summary>原型替代路径（大图，big-map D3）：依次按旗格插旗并立即锁定，产出与同时插旗完全相同的状态。</summary>
    public ImmutableSortedDictionary<PlayerId, int> PlantSequentiallyAt(IEnumerable<(PlayerId Player, Coord Cell)> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        foreach ((PlayerId player, Coord cell) in choices)
        {
            PlantAt(player, cell);
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

    /// <summary>恢复存档（大图）：直接写入已锁定的出生区编号与旗格。</summary>
    internal void RestoreLockedCells(IEnumerable<(PlayerId Player, int Zone, Coord Cell)> flags)
    {
        foreach ((PlayerId player, int zone, Coord cell) in flags)
        {
            _flags[player] = zone;
            _cells[player] = cell;
        }

        IsLocked = true;
    }
}
