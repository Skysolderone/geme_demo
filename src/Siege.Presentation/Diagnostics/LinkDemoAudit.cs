using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Siege.Core.Board;
using Siege.Core.Match;
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
    /// 插旗锁定（open-map 裁决 O4：锁定后全图全可见）：全部占位展开（占位数 = 棋盘清单块数）、本机那块标为本机，可见格 = 全图应可见的格数。
    /// <paramref name="battlefieldCells"/> 由调用方从地图尺寸独立算出：map-expand-25 段 B 起为整张图的格数（宽 × 高，含平台之间的低地与画布留白；原 G1 为各棋盘外接矩形的格数之和）。
    /// map-expand-25 段 A2：浮空岛的平台数随人数与种子变化（6–12 块），原写死的"5 块"改读视图模型的棋盘清单块数。
    /// </summary>
    public AuditLine FlagsLocked(DefaultBoardView board, int ownZone, int battlefieldCells)
    {
        ArgumentNullException.ThrowIfNull(board);
        int boards = board.Boards.Length;
        int expanded = board.Placeholders.Count(p => p.Expanded);
        bool ownExpanded = board.Placeholders.Any(p => p.Zone == ownZone && p.Own && p.Expanded);
        bool ok = boards > 0 && board.Placeholders.Length == boards && expanded == boards && ownExpanded && board.Cells.Length == battlefieldCells && battlefieldCells > 0;
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
