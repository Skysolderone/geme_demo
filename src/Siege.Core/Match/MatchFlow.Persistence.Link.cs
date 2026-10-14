using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json.Serialization;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Relics;
using Siege.Core.Scoring;

namespace Siege.Core.Match;

/// <summary>
/// 链接模式的存档（board-link-mode 实现裁决 B5，段 A 接线契约 3）。旧模式不写任何新字段（存档逐字节同前），无模式字段的存档按旧模式读入。
/// </summary>
/// <remarks>
/// 恢复顺序：开局图建空盘 → 按存档的链接记录逐个重放拼接（地图重裁 + 信物账本坐标平移，核对版本号、地图摘要与各战场平移向量）→ 再写入盘面（棋子与改造，坐标已是拼接后的）
/// → 再恢复信物揭示（坐标同样是拼接后的）→ 其余流程状态。<see cref="GameBoard.BaseMap"/> 始终是开局图。
/// </remarks>
public sealed partial class MatchFlow
{
    /// <summary>导出链接模式的流程状态；旧模式返回 <c>null</c>（不写入存档）。</summary>
    private LinkSaveData? ExportLink()
    {
        if (Mode != MatchMode.Link)
        {
            return null;
        }

        return new LinkSaveData
        {
            FirstPairingStepDone = _firstPairingStepDone,
            FinalLinked = _finalLinked,
            PendingPairing = _pendingPairing is { } pending
                ? new PairingRequestSaveData { Kind = pending.Kind, Candidates = [.. pending.Candidates], MajorRound = pending.MajorRound }
                : null,
            PassedThisRound = [.. _passedThisRound.Select(p => p.Value)],
            LockedFlagCounts = [.. _lockedFlagCounts.Select(kv => new ZoneCountSaveData { Zone = kv.Key, Count = kv.Value })],
            Pairings = [.. _pairings.Select(p => new PairingSaveData
            {
                Sequence = p.Sequence,
                Kind = p.Kind,
                MajorRound = p.MajorRound,
                Candidates = [.. p.Candidates],
                Pairs = [.. p.Pairs.Select(x => new List<int> { x.X, x.Y })],
                Waiter = p.Waiter,
                Source = p.Source,
            })],
            Links = [.. _links.Select(l => new LinkSaveData.Entry
            {
                Sequence = l.Sequence,
                PairingSequence = l.PairingSequence,
                X = l.X,
                Y = l.Y,
                Side = l.Side,
                MapVersion = l.MapVersion,
                SharedEdge = l.SharedEdge,
                MapDigest = l.MapDigest,
                Shifts = [.. l.Shifts.Select(s => new BattlefieldShiftSaveData { Battlefield = s.Battlefield, Dx = s.Dx, Dy = s.Dy })],
            })],
            Settlements = [.. _settlements.Select(s => new SettlementSaveData
            {
                Sequence = s.Sequence,
                MajorRound = s.MajorRound,
                Battlefield = s.Battlefield,
                Reason = s.Reason,
                Winner = s.Winner.Value,
                EliminationOrder = s.EliminationOrder,
                Entries = [.. s.Entries.Select(SettlementEntrySaveData.From)],
                Deadline = s.Deadline,
            })],
            FinalCountdownRounds = FinalCountdownRounds,
            CountdownStart = _countdownStart,
            Deadlines = [.. _deadlines.Select(kv => new BattlefieldDeadlineSaveData { Battlefield = kv.Key, Deadline = kv.Value })],
            Countdowns = [.. _countdowns.Select(c => new FinalCountdownSaveData
            {
                Sequence = c.Sequence,
                Battlefield = c.Battlefield,
                Start = c.Start,
                Deadline = c.Deadline,
                AssignedInMajorRound = c.AssignedInMajorRound,
            })],
            SettledBattlefields = [.. _settledBattlefields],
            Players = [.. _players.Select(p => new LinkPlayerSaveData
            {
                Player = p.Value,
                EliminationReason = _records[p].EliminationReason,
                Defeat = _records[p].Defeat is { } d ? SettlementEntrySaveData.From(d) : null,
                WaitingRounds = _records[p].WaitingRounds,
            })],
        };
    }

    /// <summary>
    /// 恢复链接模式（<see cref="RestoreCore"/> 在建好对局对象之后、写回玩家记录之前调用）：重放链接 → 写入盘面 → 恢复揭示 → 链接流程状态。
    /// 任一项与存档记录不符抛 <see cref="FormatException"/>（链接重放结果不一致即生成器或种子变了，MUST NOT 静默恢复）。
    /// </summary>
    private void RestoreLink(MatchSaveData data)
    {
        LinkSaveData link = data.Link ?? throw new FormatException("链接模式的存档缺少链接流程状态（Link 段）。");
        foreach (LinkSaveData.Entry saved in link.Links.OrderBy(l => l.Sequence))
        {
            LinkRecord replayed = ExecuteLink(saved.X, saved.Y, saved.PairingSequence, saved.Side, replay: true);
            if (replayed.Sequence != saved.Sequence || replayed.MapVersion != saved.MapVersion || replayed.SharedEdge != saved.SharedEdge
                || !string.Equals(replayed.MapDigest, saved.MapDigest, StringComparison.Ordinal)
                || !replayed.Shifts.Select(s => (s.Battlefield, s.Dx, s.Dy)).SequenceEqual(saved.Shifts.Select(s => (s.Battlefield, s.Dx, s.Dy))))
            {
                throw new FormatException(
                    $"链接 #{saved.Sequence}（战场 {BirthZoneLabel.Of(saved.X)} + {BirthZoneLabel.Of(saved.Y)}，贴{(saved.Side == LinkSide.East ? "东" : "北")}）重放结果与存档不一致："
                    + $"地图版本 {replayed.MapVersion} / {saved.MapVersion}，摘要 {replayed.MapDigest} / {saved.MapDigest}，"
                    + $"平移 [{string.Join(" ", replayed.Shifts)}] / [{string.Join(" ", saved.Shifts.Select(s => $"{BirthZoneLabel.Of(s.Battlefield)}({s.Dx:+#;-#;0},{s.Dy:+#;-#;0})"))}]；未恢复。");
            }
        }

        // 段 A 接线契约 3：先重放链接、再写盘面——盘面段的棋子与改造（坐标已是拼接后的）写进重裁后的地图。
        try
        {
            Board.LoadState(data.Board ?? throw new FormatException("存档缺少盘面。"));
        }
        catch (SiegeRuleException ex)
        {
            throw new FormatException($"存档盘面与重放链接后的地图不符：{ex.Message}", ex);
        }

        Relics.RestoreState(data.Relics ?? throw new FormatException("存档缺少信物账本。"));

        _firstPairingStepDone = link.FirstPairingStepDone;
        _finalLinked = link.FinalLinked;
        _pendingPairing = link.PendingPairing is { } pending
            ? new PairingRequest(pending.Kind, [.. pending.Candidates], pending.MajorRound)
            : null;
        _passedThisRound.UnionWith(link.PassedThisRound.Select(v => new PlayerId(v)));
        _lockedFlagCounts = link.LockedFlagCounts.ToImmutableSortedDictionary(z => z.Zone, z => z.Count);
        _pairings.AddRange(link.Pairings.Select(p => new PairingRecord(
            p.Sequence, p.Kind, p.MajorRound, [.. p.Candidates], [.. p.Pairs.Select(x => (x[0], x[1]))], p.Waiter, p.Source ?? PairingDecision.ExternalSource)));
        _settlements.AddRange(link.Settlements.Select(s => new BattlefieldSettlementRecord(
            s.Sequence, s.MajorRound, s.Battlefield, s.Reason, new PlayerId(s.Winner), [.. s.Entries.Select(e => e.ToEntry())], s.EliminationOrder)
        {
            Deadline = s.Deadline,
        }));
        // final-countdown：起点、当前期限与倒计时记录原样取自存档，不重算（旧存档缺这几项 → K = 0、全空，见 RestoreCore）。
        _countdownStart = link.CountdownStart;
        foreach (BattlefieldDeadlineSaveData d in link.Deadlines ?? [])
        {
            _deadlines[d.Battlefield] = d.Deadline;
        }

        _countdowns.AddRange((link.Countdowns ?? []).Select(c => new FinalCountdownRecord(c.Sequence, c.Battlefield, c.Start, c.Deadline, c.AssignedInMajorRound)));
        _settledBattlefields.Clear();
        _settledBattlefields.UnionWith(link.SettledBattlefields);
        foreach (LinkPlayerSaveData saved in link.Players)
        {
            PlayerRecord record = Require(new PlayerId(saved.Player));
            record.EliminationReason = saved.EliminationReason;
            record.Defeat = saved.Defeat?.ToEntry();
            record.WaitingRounds = saved.WaitingRounds;
        }
    }
}

/// <summary>链接模式的流程状态存档段（<see cref="MatchSaveData.Link"/>）。旧模式不写。</summary>
public sealed class LinkSaveData
{
    public bool FirstPairingStepDone { get; set; }

    public bool FinalLinked { get; set; }

    /// <summary>待处理的配对请求（只在待配对 / 待决赛链接阶段存在）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PairingRequestSaveData? PendingPairing { get; set; }

    /// <summary>本大回合内已 Pass 的玩家（小回合边界存档时，整轮 Pass 结算要接着算）。</summary>
    public List<int> PassedThisRound { get; set; } = [];

    /// <summary>锁定时每块出生棋盘的旗数（首轮配对候选的依据）。</summary>
    public List<ZoneCountSaveData> LockedFlagCounts { get; set; } = [];

    public List<PairingSaveData> Pairings { get; set; } = [];

    /// <summary>链接事件：恢复时按序号重放（贴边取自记录）并逐项核对版本号、共享边、地图摘要与各战场平移向量。</summary>
    public List<Entry> Links { get; set; } = [];

    public List<SettlementSaveData> Settlements { get; set; } = [];

    public List<int> SettledBattlefields { get; set; } = [];

    public List<LinkPlayerSaveData> Players { get; set; } = [];

    /// <summary>
    /// 决赛倒计时大回合数 K（final-countdown，match-setup「决赛倒计时配置」）。新存档恒写出（K = 0 也写）；
    /// 旧存档无此字段（<c>null</c>）→ 恢复时按 0（不启用），<see cref="MatchFlow.FinalCountdownBackfilled"/> 为 <c>true</c>。
    /// </summary>
    public int? FinalCountdownRounds { get; set; }

    /// <summary>倒计时起点 R；尚未出现等待决赛的玩家为 <c>null</c>（不写出）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CountdownStart { get; set; }

    /// <summary>当前处于倒计时中的战场与期限（升序）。旧存档无此字段 → 空。</summary>
    public List<BattlefieldDeadlineSaveData>? Deadlines { get; set; }

    /// <summary>全部倒计时记录（按赋期限的先后）。旧存档无此字段 → 空。</summary>
    public List<FinalCountdownSaveData>? Countdowns { get; set; }

    /// <summary>一次链接的存档记录。</summary>
    public sealed class Entry
    {
        public int Sequence { get; set; }

        public int PairingSequence { get; set; }

        public int X { get; set; }

        public int Y { get; set; }

        /// <summary>贴边（首轮东、决赛北）：重放时传给生成器。</summary>
        public LinkSide Side { get; set; }

        public int MapVersion { get; set; }

        public int SharedEdge { get; set; }

        public string MapDigest { get; set; } = string.Empty;

        /// <summary>拼接前每个战场的平移向量（按标识升序，含锚点的零向量）。</summary>
        public List<BattlefieldShiftSaveData> Shifts { get; set; } = [];
    }
}

/// <summary>一个战场在一次链接里的平移向量。</summary>
public sealed class BattlefieldShiftSaveData
{
    public int Battlefield { get; set; }

    public int Dx { get; set; }

    public int Dy { get; set; }
}

public sealed class PairingRequestSaveData
{
    public PairingKind Kind { get; set; }

    public List<int> Candidates { get; set; } = [];

    public int MajorRound { get; set; }
}

public sealed class ZoneCountSaveData
{
    public int Zone { get; set; }

    public int Count { get; set; }
}

public sealed class PairingSaveData
{
    public int Sequence { get; set; }

    public PairingKind Kind { get; set; }

    public int MajorRound { get; set; }

    public List<int> Candidates { get; set; } = [];

    /// <summary>规范次序的配对，每对两个战场标识。</summary>
    public List<List<int>> Pairs { get; set; } = [];

    public int? Waiter { get; set; }

    public string? Source { get; set; }
}

public sealed class SettlementSaveData
{
    public int Sequence { get; set; }

    public int MajorRound { get; set; }

    public int Battlefield { get; set; }

    public SettlementReason Reason { get; set; }

    public int Winner { get; set; }

    public int? EliminationOrder { get; set; }

    public List<SettlementEntrySaveData> Entries { get; set; } = [];

    /// <summary>结算时该战场的决赛倒计时期限（final-countdown）；不在倒计时中为 <c>null</c>（不写出）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Deadline { get; set; }
}

/// <summary>一个处于决赛倒计时中的战场与其期限大回合（final-countdown）。</summary>
public sealed class BattlefieldDeadlineSaveData
{
    public int Battlefield { get; set; }

    public int Deadline { get; set; }
}

/// <summary>一条决赛倒计时记录（final-countdown，<see cref="FinalCountdownRecord"/>）。</summary>
public sealed class FinalCountdownSaveData
{
    public int Sequence { get; set; }

    public int Battlefield { get; set; }

    public int Start { get; set; }

    public int Deadline { get; set; }

    public int AssignedInMajorRound { get; set; }
}

public sealed class SettlementEntrySaveData
{
    public int Player { get; set; }

    public int Rank { get; set; }

    public BigInteger Power { get; set; }

    public int ControlledRelics { get; set; }

    public int ExclusiveCells { get; set; }

    public int Stones { get; set; }

    internal static SettlementEntrySaveData From(SettlementEntry e) => new()
    {
        Player = e.Player.Value,
        Rank = e.Rank,
        Power = e.Power,
        ControlledRelics = e.ControlledRelics,
        ExclusiveCells = e.ExclusiveCells,
        Stones = e.Stones,
    };

    internal SettlementEntry ToEntry() => new(new PlayerId(Player), Rank, Power, ControlledRelics, ExclusiveCells, Stones);
}

/// <summary>链接模式下每名玩家的附加记录：出局原因、战场落败时的比较量、等待大回合数。</summary>
public sealed class LinkPlayerSaveData
{
    public int Player { get; set; }

    public EliminationReason? EliminationReason { get; set; }

    public SettlementEntrySaveData? Defeat { get; set; }

    public int WaitingRounds { get; set; }
}
