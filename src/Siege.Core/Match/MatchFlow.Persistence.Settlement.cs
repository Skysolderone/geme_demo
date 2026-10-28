using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Match;

/// <summary>
/// 战场结算与出局原因的存档段（board-link-mode 实现裁决 B5；retire-legacy-rules：存档恒写模式与本段，缺模式字段的存档拒绝恢复）。
/// open-map 段 B（裁决 B1 / B2）：配对、链接、决赛倒计时 K 与等待大回合数随拼接规则删除——段名 <c>Link</c> 与模式常量照旧（不加存档格式版本号），
/// 含这些字段的旧存档在解析时明确拒绝并提示属于已移除的拼接规则（<see cref="RejectRetiredLinkFields"/>）。
/// </summary>
/// <remarks>
/// 恢复顺序：开局图建空盘 → 写入盘面（棋子、改造与信物层）→ 恢复信物刷新记录 → 其余流程状态。地图整局不变，<see cref="GameBoard.BaseMap"/> 始终是开局图。
/// </remarks>
public sealed partial class MatchFlow
{
    /// <summary>
    /// 已随拼接规则删除的存档字段（open-map 裁决 B2）：在 <c>Link</c> 段（含其嵌套对象）里读到任一即拒绝恢复并点名，MUST NOT 静默忽略——
    /// 这些字段只可能来自改动前的链接模式存档，那一局的规则今天已不存在。
    /// </summary>
    internal static readonly ImmutableHashSet<string> RetiredLinkFields = ImmutableHashSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "FirstPairingStepDone", "FinalLinked", "PendingPairing", "Pairings", "Links",
        "FinalCountdownRounds", "CountdownStart", "Deadlines", "Countdowns", "WaitingRounds", "Deadline");

    /// <summary>含已删除字段的旧存档的拒绝文案前缀（测试与入口按它识别）。</summary>
    public const string RetiredLinkFieldMessage = "拼接规则已移除：该存档属于改动前的链接模式";

    /// <summary>
    /// 扫描存档 JSON 的 <c>Link</c> 段（在地图摘要比对之前执行，旧存档先报"拼接规则已移除"而不是"地图不一致"）：只看三处固定位置——<c>Link</c> 的顶层键、
    /// <c>Link.Settlements[*].Deadline</c>、<c>Link.Players[*].WaitingRounds</c>；不做全树递归，免得日后别处新增同名字段被误伤。任一命中即抛 <see cref="FormatException"/>。
    /// </summary>
    private static void RejectRetiredLinkFields(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty(nameof(MatchSaveData.Link), out JsonElement link) || link.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        string? found = link.EnumerateObject().Select(p => p.Name).FirstOrDefault(RetiredLinkFields.Contains) is { } top ? $"Link.{top}"
            : NestedRetired(link, nameof(LinkSaveData.Settlements), "Deadline") ?? NestedRetired(link, nameof(LinkSaveData.Players), "WaitingRounds");
        if (found is not null)
        {
            throw new FormatException(
                $"{RetiredLinkFieldMessage}（存档字段 {found}：配对 / 链接 / 决赛倒计时 / 等待者已随 open-map 删除），不再支持恢复。");
        }
    }

    /// <summary><paramref name="link"/> 下数组 <paramref name="array"/> 的任一元素带属性 <paramref name="field"/> 即返回其路径；否则 <c>null</c>。</summary>
    private static string? NestedRetired(JsonElement link, string array, string field)
    {
        if (!link.TryGetProperty(array, out JsonElement items) || items.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        int index = 0;
        foreach (JsonElement item in items.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty(field, out _))
            {
                return $"Link.{array}[{index}].{field}";
            }

            index++;
        }

        return null;
    }

    /// <summary>导出战场结算段（存档恒写出）。</summary>
    private LinkSaveData ExportLink()
    {
        return new LinkSaveData
        {
            PassedThisRound = [.. _passedThisRound.Select(p => p.Value)],
            LockedFlagCounts = [.. _lockedFlagCounts.Select(kv => new ZoneCountSaveData { Zone = kv.Key, Count = kv.Value })],
            Settlements = [.. _settlements.Select(s => new SettlementSaveData
            {
                Sequence = s.Sequence,
                MajorRound = s.MajorRound,
                Battlefield = s.Battlefield,
                Reason = s.Reason,
                Winner = s.Winner.Value,
                EliminationOrder = s.EliminationOrder,
                Entries = [.. s.Entries.Select(SettlementEntrySaveData.From)],
            })],
            SettledBattlefields = [.. _settledBattlefields],
            Players = [.. _players.Select(p => new LinkPlayerSaveData
            {
                Player = p.Value,
                EliminationReason = _records[p].EliminationReason,
                Defeat = _records[p].Defeat is { } d ? SettlementEntrySaveData.From(d) : null,
            })],
        };
    }

    /// <summary>
    /// 恢复战场结算段（<see cref="RestoreCore"/> 在建好对局对象之后、写回玩家记录之前调用）：写入盘面（含信物层）→ 恢复信物刷新记录 → 结算流程状态。
    /// 盘面与地图不符抛 <see cref="FormatException"/>（MUST NOT 静默恢复）。
    /// </summary>
    private void RestoreLink(MatchSaveData data)
    {
        LinkSaveData link = data.Link ?? throw new FormatException("链接模式的存档缺少流程状态（Link 段）。");
        try
        {
            Board.LoadState(data.Board ?? throw new FormatException("存档缺少盘面。"));
        }
        catch (SiegeRuleException ex)
        {
            throw new FormatException($"存档盘面与地图不符：{ex.Message}", ex);
        }

        RestoreRelics(data.RelicSpawn);

        _passedThisRound.UnionWith(link.PassedThisRound.Select(v => new PlayerId(v)));
        _lockedFlagCounts = link.LockedFlagCounts.ToImmutableSortedDictionary(z => z.Zone, z => z.Count);
        _settlements.AddRange(link.Settlements.Select(s => new BattlefieldSettlementRecord(
            s.Sequence, s.MajorRound, s.Battlefield, s.Reason, new PlayerId(s.Winner), [.. s.Entries.Select(e => e.ToEntry())], s.EliminationOrder)));
        _settledBattlefields.Clear();
        _settledBattlefields.UnionWith(link.SettledBattlefields);
        foreach (LinkPlayerSaveData saved in link.Players)
        {
            PlayerRecord record = Require(new PlayerId(saved.Player));
            record.EliminationReason = saved.EliminationReason;
            record.Defeat = saved.Defeat?.ToEntry();
        }
    }
}

/// <summary>链接模式的流程状态存档段（<see cref="MatchSaveData.Link"/>）：本大回合 Pass 名单、锁定旗数、战场结算记录、已结算战场与各玩家的出局原因 / 落败比较量。</summary>
public sealed class LinkSaveData
{
    /// <summary>本大回合内已 Pass 的在局玩家编号。</summary>
    public List<int> PassedThisRound { get; set; } = [];

    /// <summary>锁定时每块出生棋盘上的旗数（只含有旗的棋盘）。</summary>
    public List<ZoneCountSaveData> LockedFlagCounts { get; set; } = [];

    /// <summary>战场结算记录，按发生顺序。</summary>
    public List<SettlementSaveData> Settlements { get; set; } = [];

    /// <summary>已结算的战场标识。</summary>
    public List<int> SettledBattlefields { get; set; } = [];

    /// <summary>各玩家的出局原因与落败比较量。</summary>
    public List<LinkPlayerSaveData> Players { get; set; } = [];
}

public sealed class ZoneCountSaveData
{
    public int Zone { get; set; }

    public int Count { get; set; }
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
}

public sealed class SettlementEntrySaveData
{
    public int Player { get; set; }

    public int Rank { get; set; }

    public BigInteger Power { get; set; }

    public int HeldRelics { get; set; }

    public int ExclusiveCells { get; set; }

    public int Stones { get; set; }

    internal static SettlementEntrySaveData From(SettlementEntry e) => new()
    {
        Player = e.Player.Value,
        Rank = e.Rank,
        Power = e.Power,
        HeldRelics = e.HeldRelics,
        ExclusiveCells = e.ExclusiveCells,
        Stones = e.Stones,
    };

    internal SettlementEntry ToEntry() => new(new PlayerId(Player), Rank, Power, HeldRelics, ExclusiveCells, Stones);
}

/// <summary>一名玩家的出局原因与战场落败时的比较量。</summary>
public sealed class LinkPlayerSaveData
{
    public int Player { get; set; }

    public EliminationReason? EliminationReason { get; set; }

    public SettlementEntrySaveData? Defeat { get; set; }
}
