using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Presentation.Show;
using Siege.Presentation.Text;
using Siege.Presentation.Visibility;

namespace Siege.Presentation.Diagnostics;

/// <summary>一条自检结论：步名、明细、是否通过。</summary>
public sealed record AuditLine(string Step, string Detail, bool Ok)
{
    public override string ToString() => $"{Step}：{Detail}：{(Ok ? "通过" : "失败")}";
}

/// <summary>
/// `--auto-demo` 全流程自检的判定（board-link-mode tasks 4.6 → open-map D7，规格 simulation-harness「各入口支持生成图」）：插旗锁定 → 构筑期 → 全图解禁 → 终局，
/// 每一步从对局公开记录与按座位视图模型里<b>可断言地</b>核对，而不是只打印。纯函数式：输入全是只读记录（终局结果、默认棋盘视图、合法范围集合），
/// 不碰对局对象；Godot 侧只负责在相应时机喂数据、打印 <see cref="AuditLine"/> 并按 <see cref="AllOk"/> 决定退出码。
/// 配对 / 链接 / 战场结算三步的判定（<see cref="Pairing"/> / <see cref="Linked"/> / <see cref="Settled"/>）open-map 段 A 起不再被喂到（对局里不再发生这些事件），段 B 删除。
/// </summary>
public sealed class LinkDemoAudit
{
    /// <summary>全流程必须依次见到的四步（open-map D7）。</summary>
    public static readonly ImmutableArray<string> FullFlow = ["插旗锁定", "构筑期", "全图解禁", "终局"];

    private readonly List<AuditLine> _lines = [];
    private readonly HashSet<string> _seen = [];

    /// <summary>已记录的全部结论（按时间）。</summary>
    public IReadOnlyList<AuditLine> Lines => _lines;

    /// <summary>没有一条失败。</summary>
    public bool AllOk => _lines.All(l => l.Ok);

    /// <summary>已见到的步（<see cref="FullFlow"/> 的子集）。</summary>
    public ImmutableArray<string> Seen => [.. FullFlow.Where(_seen.Contains)];

    /// <summary>
    /// 插旗锁定（open-map 裁决 O4：锁定后全图全可见）：5 块占位全部展开、本机那块标为本机，可见格 = 全图应可见的格数。
    /// <paramref name="battlefieldCells"/> 由调用方从战场导出与棋盘清单独立算出：link-map-terrain G1 起为本战场（= 全图）各棋盘外接矩形的格数之和（含岩石与未架桥深水）。
    /// </summary>
    public AuditLine FlagsLocked(DefaultBoardView board, int ownZone, int battlefieldCells)
    {
        ArgumentNullException.ThrowIfNull(board);
        int expanded = board.Placeholders.Count(p => p.Expanded);
        bool ownExpanded = board.Placeholders.Any(p => p.Zone == ownZone && p.Own && p.Expanded);
        bool ok = board.Placeholders.Length == 5 && expanded == 5 && ownExpanded && board.Cells.Length == battlefieldCells && battlefieldCells > 0;
        return Record("插旗锁定",
            $"本机 {BirthZoneLabel.Of(ownZone)} 已展开 {(ownExpanded ? "是" : "否")}，占位 {board.Placeholders.Length} 块、已展开 {expanded} 块，可见格 {board.Cells.Length} / 全图 {battlefieldCells}", ok);
    }

    /// <summary>
    /// 全图解禁（open-map D7）：第 4 大回合起本机合法落子范围 = 全图可落子格扣除本人禁入格，且格数严格大于自己出生棋盘的可落子格数。
    /// <paramref name="playable"/> 为全图可落子格，<paramref name="forbidden"/> 为本机此刻的禁入格（都读按座位视图模型），<paramref name="ownPlayable"/> 为自己出生棋盘的可落子格数。
    /// </summary>
    public AuditLine Unlocked(int majorRound, IReadOnlySet<Coord> legalRange, IReadOnlyCollection<Coord> playable, IReadOnlyCollection<Coord> forbidden, int ownPlayable)
    {
        ArgumentNullException.ThrowIfNull(legalRange);
        ArgumentNullException.ThrowIfNull(playable);
        ArgumentNullException.ThrowIfNull(forbidden);
        int outside = legalRange.Count(c => !playable.Contains(c));
        int forbiddenInRange = legalRange.Count(forbidden.Contains);
        int missing = playable.Count(c => !legalRange.Contains(c) && !forbidden.Contains(c));
        bool ok = majorRound > PublicRules.BuildProtectionRounds && outside == 0 && forbiddenInRange == 0 && missing == 0 && legalRange.Count > ownPlayable;
        return Record("全图解禁",
            $"第 {majorRound} 大回合合法落子范围 {legalRange.Count} 格，全图可落子 {playable.Count} 格、禁入 {forbidden.Count} 格（范围外 {outside}、含禁入 {forbiddenInRange}、漏 {missing}），自己棋盘可落子 {ownPlayable} 格", ok);
    }

    /// <summary>构筑期（第 1–3 大回合）：本机的合法落子范围非空且全在自己的出生棋盘内。</summary>
    public AuditLine BuildingRange(int majorRound, IReadOnlySet<Coord> legalRange, IReadOnlyCollection<Coord> ownBoard)
    {
        ArgumentNullException.ThrowIfNull(legalRange);
        ArgumentNullException.ThrowIfNull(ownBoard);
        int outside = legalRange.Count(c => !ownBoard.Contains(c));
        bool ok = majorRound >= 1 && majorRound <= PublicRules.BuildProtectionRounds && legalRange.Count > 0 && outside == 0;
        return Record("构筑期", $"第 {majorRound} 大回合合法落子范围 {legalRange.Count} 格，出生棋盘 {ownBoard.Count} 格，棋盘之外 {outside} 格", ok);
    }

    /// <summary>配对事件：来源 = 固定脚本；各对都出自候选且互不重复；首轮候选为奇数时恰一名等待者（也出自候选且不在对里），决赛无等待者且全部候选链成一个；首轮在决赛之前。</summary>
    public AuditLine Pairing(PairingRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        string step = record.Kind == PairingKind.FirstRound ? "首轮配对" : "决赛链接";
        HashSet<int> candidates = [.. record.Candidates];
        int[] members = [.. record.Pairs.SelectMany(p => new[] { p.X, p.Y })];
        bool fromCandidates = members.All(candidates.Contains) && members.Distinct().Count() == members.Length;
        bool waiterOk = record.Kind == PairingKind.FirstRound
            ? (record.Candidates.Length % 2 == 1) == (record.Waiter is not null) && (record.Waiter is not { } w || (candidates.Contains(w) && !members.Contains(w)))
            : record.Waiter is null && members.Distinct().Count() == record.Candidates.Length && record.Pairs.Length == record.Candidates.Length - 1;
        bool orderOk = record.Kind == PairingKind.FirstRound ? !_seen.Contains("决赛链接") : _seen.Contains("首轮配对") || _seen.Contains("链接");
        bool ok = record.Source == ScriptedPairingSource.ScriptSource && record.Candidates.Length >= 2 && record.Pairs.Length >= 1 && fromCandidates && waiterOk && orderOk;
        string pairs = string.Join(" ", record.Pairs.Select(p => $"({BirthZoneLabel.Of(p.X)},{BirthZoneLabel.Of(p.Y)})"));
        return Record(step,
            $"#{record.Sequence} 第 {record.MajorRound} 大回合 候选 {string.Join(" ", record.Candidates.Select(BirthZoneLabel.Of))}；配对 {pairs}；等待者 {(record.Waiter is { } wt ? BirthZoneLabel.Of(wt) : "无")}；来源 {record.Source}", ok);
    }

    /// <summary>
    /// 链接（拼接，board-link-mode F.4）：本机参与时 可见格 = 本战场应可见的格数（G1：各棋盘外接矩形之和）= 拼接前本机可见格 + 新展开的对方出生棋盘格数（拼接不增不减格，两战场格数之和守恒）、
    /// 且有新展开的占位；本机不参与时可见格数不变（坐标可能随收拢平移，格数不变）。<paramref name="expandedBefore"/> 为拼接前已展开的出生棋盘编号。
    /// </summary>
    public AuditLine Linked(LinkRecord link, bool mine, int cellsBefore, DefaultBoardView after, int battlefieldCells, IReadOnlyCollection<int> expandedBefore)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(expandedBefore);
        BoardPlaceholderView[] joined = [.. after.Placeholders.Where(p => p.Expanded && !expandedBefore.Contains(p.Zone))];
        int joinedCells = joined.Sum(p => p.Width * p.Height);
        int expanded = after.Placeholders.Count(p => p.Expanded);
        bool ok = mine
            ? after.Cells.Length == battlefieldCells && battlefieldCells == cellsBefore + joinedCells && joined.Length > 0
            : after.Cells.Length == cellsBefore;
        return Record("链接",
            $"#{link.Sequence} 战场 {BirthZoneLabel.Of(link.X)} + {BirthZoneLabel.Of(link.Y)}{(mine ? "（本机参与）" : "（他人）")}：可见格 {cellsBefore} + 拼入 {joinedCells} → {after.Cells.Length}，本战场 {battlefieldCells} 格，已展开占位 {expanded}", ok);
    }

    /// <summary>
    /// 战场结算：本机在该战场在局名单里且对局未终局 → 本回合的横幅里有"战场胜出" / "战场落败（第 N 名）"；本机不在 → 新提示条里有"战场 X 已结算，胜者 Y"；对局随之终局 → 两者都没有。
    /// </summary>
    public AuditLine Settled(BattlefieldSettlementRecord record, PlayerId me, bool matchEnded, ImmutableArray<string> banners, ImmutableArray<string> notices)
    {
        ArgumentNullException.ThrowIfNull(record);
        SettlementEntry? mine = record.Entries.FirstOrDefault(e => e.Player == me);
        string expected;
        bool ok;
        if (matchEnded)
        {
            expected = "终局：不出战场横幅与提示条";
            ok = !banners.Any(b => b.StartsWith("战场", StringComparison.Ordinal)) && !notices.Any(n => n.StartsWith("战场", StringComparison.Ordinal));
        }
        else if (mine is { } entry)
        {
            expected = record.Winner == me ? LinkShowBeats.BattlefieldWonText : LinkShowBeats.BattlefieldLostText(entry.Rank);
            ok = banners.Contains(expected);
        }
        else
        {
            expected = LinkShowBeats.SettledElsewhereText(record.Battlefield, record.Winner);
            ok = notices.Contains(expected);
        }

        return Record("战场结算",
            $"#{record.Sequence} 战场 {BirthZoneLabel.Of(record.Battlefield)}（{record.Reason}）胜者 {Labels.Player(record.Winner)}，本机{(mine is null ? "不在" : "在")}名单；应出「{expected}」，横幅 [{string.Join("｜", banners)}]，提示条 [{string.Join("｜", notices)}]", ok);
    }

    /// <summary>终局（open-map D7）：原因是"最后战场结算"（唯一战场整轮 Pass / 填满）或"只剩一名参赛玩家"。</summary>
    public AuditLine Ended(MatchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Record("终局", $"第 {result.MajorRound} 大回合，{result.Reason}，名次 {result.Standings.Length} 人",
            result.Reason is EndReason.LastBattlefieldSettled or EndReason.LastPlayerStanding);
    }

    /// <summary>完整性：四步都见过（只在跑到终局的全流程要求）。</summary>
    public AuditLine Completeness()
    {
        string[] missing = [.. FullFlow.Where(s => !_seen.Contains(s))];
        var line = new AuditLine("完整性", missing.Length == 0 ? $"四步齐全：{string.Join(" → ", FullFlow)}" : $"缺：{string.Join("、", missing)}", missing.Length == 0);
        _lines.Add(line);
        return line;
    }

    private AuditLine Record(string step, string detail, bool ok)
    {
        var line = new AuditLine(step, detail, ok);
        _lines.Add(line);
        if (ok)
        {
            _seen.Add(step);
        }

        return line;
    }
}
