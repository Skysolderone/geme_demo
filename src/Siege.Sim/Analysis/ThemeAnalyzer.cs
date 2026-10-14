using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Sim.Logging;

namespace Siege.Sim.Analysis;

// 本文件属于离线分析输出层：允许浮点（裁决 14）。只读日志对象，不引用任何对局运行时对象。

/// <summary>
/// 一个主题的统计（link-map-terrain 5.2）。归属口径：名次与胜场按<b>玩家锁定的出生棋盘</b>（首部 <see cref="LogHeader.Zones"/>）归主题；
/// 匠人改造按<b>改造目标所在的棋盘</b>归主题（立栅取两端里第一个落在棋盘内的一端）；公共信物按<b>信物所在的棋盘</b>归主题。
/// </summary>
/// <param name="Theme">主题。</param>
/// <param name="Boards">纳入局里该主题的棋盘块数（每局每块计 1）。</param>
/// <param name="Seats">锁定在该主题棋盘上的玩家人次（全部纳入局，含截断局）。</param>
/// <param name="RankedSeats">其中在有名次的局（未截断、结果行有名次）里的人次——平均名次与胜场的分母。</param>
/// <param name="MeanRank">平均名次（只计 <see cref="RankedSeats"/>；没有样本为 NaN）。</param>
/// <param name="Wins">胜场：<see cref="RankedSeats"/> 里是本局胜者的人次。</param>
/// <param name="Edits">匠人改造次数，按动作名（<c>TerrainEditKind</c>：Bridge / Fence / Burn）。</param>
/// <param name="PublicRelics">纳入局里该主题棋盘上的公共信物个数之和（首部信物表的 <c>Contested</c> 档，开局坐标）。</param>
/// <param name="PublicControls">这些公共信物"变为有人控制"的次数（<c>ControlChanged</c> 事件、控制者非空；失去控制不计）。</param>
public sealed record ThemeStat(
    BoardTheme Theme,
    int Boards,
    int Seats,
    int RankedSeats,
    double MeanRank,
    int Wins,
    SortedDictionary<string, int> Edits,
    int PublicRelics,
    int PublicControls)
{
    /// <summary>匠人改造合计。</summary>
    public int EditTotal => Edits.Values.Sum();
}

/// <summary>按主题统计（link-map-terrain 5.2）。</summary>
/// <param name="Matches">纳入的带主题链接模式局数（含截断局）。</param>
/// <param name="RankedMatches">其中有名次的局数。</param>
/// <param name="Themes">各主题，按 <see cref="BoardTheme"/> 枚举序，五个主题都列出（没有样本的也列）。</param>
public sealed record ThemeSection(int Matches, int RankedMatches, IReadOnlyList<ThemeStat> Themes);

/// <summary>按主题统计的唯一实现（见 <see cref="ThemeStat"/>）。</summary>
public static class ThemeAnalyzer
{
    /// <summary>统计纳入局里首部带棋盘主题的链接模式局；一局都没有时返回 <c>null</c>（报告里整段不出现，旧日志的报告逐字节不变）。</summary>
    public static ThemeSection? Analyze(IReadOnlyList<MatchLog> included)
    {
        ArgumentNullException.ThrowIfNull(included);
        List<MatchLog> logs = [.. included.Where(l => l.IsLinkMode && l.Result is not null && l.Header.Boards is { } b && b.Any(x => x.Theme is not null))];
        if (logs.Count == 0)
        {
            return null;
        }

        BoardTheme[] all = Enum.GetValues<BoardTheme>();
        var boards = all.ToDictionary(t => t, _ => 0);
        var seats = all.ToDictionary(t => t, _ => 0);
        var ranks = all.ToDictionary(t => t, _ => new List<double>());
        var wins = all.ToDictionary(t => t, _ => 0);
        var edits = all.ToDictionary(t => t, _ => new SortedDictionary<string, int>(StringComparer.Ordinal));
        var relics = all.ToDictionary(t => t, _ => 0);
        var controls = all.ToDictionary(t => t, _ => 0);
        int ranked = 0;

        foreach (MatchLog log in logs)
        {
            List<BoardEntry> entries = log.Header.Boards!;
            BoardTheme? ThemeOf(int? zone) => zone is { } z && z >= 0 && z < entries.Count ? entries[z].Theme : null;
            foreach (BoardTheme theme in entries.Select(e => e.Theme).OfType<BoardTheme>())
            {
                boards[theme]++;
            }

            // 名次与胜场：按玩家锁定的棋盘。截断局（无名次）只计人次，不进平均名次与胜场的分母。
            LogResult result = log.Result!;
            bool hasRanks = !result.Truncated && result.Standings.Count > 0;
            ranked += hasRanks ? 1 : 0;
            foreach (int player in log.Header.Players)
            {
                if (ThemeOf(player < log.Header.Zones.Count ? log.Header.Zones[player] : null) is not { } theme)
                {
                    continue;
                }

                seats[theme]++;
                if (hasRanks && result.Standings.FirstOrDefault(s => s.Player == player) is { } standing)
                {
                    ranks[theme].Add(standing.Rank);
                    wins[theme] += result.Winners.Contains(player) ? 1 : 0;
                }
            }

            LinkLayout layout = LinkLogLayout.Of(log);

            // 匠人改造：快照里的改造按该小回合结束时（含本回合内的链接）的布局记，按目标所在棋盘归主题。
            foreach (TurnSnapshot turn in log.Turns.Where(t => t.Edits is { Count: > 0 }))
            {
                int version = LinkLogLayout.VersionAfterTurn(log, turn.Turn);
                foreach (TerrainEditEntry entry in turn.Edits!)
                {
                    int? zone = TerrainEdit.Parse(entry.Target).Cells.Select(c => layout.ZoneAt(c, version)).FirstOrDefault(z => z is not null);
                    if (ThemeOf(zone) is { } theme)
                    {
                        edits[theme][entry.Action] = edits[theme].GetValueOrDefault(entry.Action) + 1;
                    }
                }
            }

            // 公共信物：首部信物表（开局坐标）的公共区档，按所在棋盘归主题。
            HashSet<Coord> contested = [.. log.Header.Relics.Where(r => r.Zone == nameof(RelicZone.Contested)).Select(r => Coord.Parse(r.Coord))];
            foreach (Coord c in contested)
            {
                if (ThemeOf(layout.ZoneAt(c, 0)) is { } theme)
                {
                    relics[theme]++;
                }
            }

            // 被控制：ControlChanged 事件按发生时的布局记坐标；换回开局坐标后查是否公共信物。控制者为空（失去控制）不计。
            foreach (LogEvent e in log.Events.Where(e => e.Type == LogEventType.ControlChanged && e.Player is not null && e.Coords is { Count: > 0 }))
            {
                int version = LinkLogLayout.VersionAtEvent(log, e);
                Coord at = Coord.Parse(e.Coords![0]);
                if (layout.ZoneAt(at, version) is not { } zone)
                {
                    continue;
                }

                Coord now = layout.BoardAt(zone, version).Origin;
                Coord then = layout.BoardAt(zone, 0).Origin;
                if (contested.Contains(new Coord(at.X - now.X + then.X, at.Y - now.Y + then.Y)) && ThemeOf(zone) is { } theme)
                {
                    controls[theme]++;
                }
            }
        }

        return new ThemeSection(
            logs.Count,
            ranked,
            [.. all.Select(t => new ThemeStat(t, boards[t], seats[t], ranks[t].Count, Statistics.Mean(ranks[t]), wins[t], edits[t], relics[t], controls[t]))]);
    }
}
