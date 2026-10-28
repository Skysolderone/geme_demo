using System.Numerics;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Sim.Logging;

namespace Siege.Sim.Analysis;

// 本文件属于离线分析输出层：允许浮点（裁决 14）。只读日志对象，不引用任何对局运行时对象。

/// <summary>
/// 一个主题的统计（link-map-terrain 5.2）。归属口径：名次与胜场按<b>玩家锁定的出生棋盘</b>（首部 <see cref="LogHeader.Zones"/>）归主题；
/// 匠人改造按<b>改造目标所在的棋盘</b>归主题（立栅取两端里第一个落在棋盘内的一端）。relic-redesign 起地图没有静态公共信物，原"公共信物 / 被控制"两列删除。
/// </summary>
/// <param name="Theme">主题。</param>
/// <param name="Boards">纳入局里该主题的棋盘块数（每局每块计 1）。</param>
/// <param name="Seats">锁定在该主题棋盘上的玩家人次（全部纳入局，含截断局）。</param>
/// <param name="RankedSeats">其中在有名次的局（未截断、结果行有名次）里的人次——平均名次与胜场的分母。</param>
/// <param name="MeanRank">平均名次（只计 <see cref="RankedSeats"/>；没有样本为 NaN）。</param>
/// <param name="Wins">胜场：<see cref="RankedSeats"/> 里是本局胜者的人次（open-map B6："主题表现"的整局胜率 = Wins / RankedSeats，原"首轮战场胜率"随决赛删除）。</param>
/// <param name="Edits">匠人改造次数，按动作名（<c>TerrainEditKind</c>：Bridge / Fence / Burn）。</param>
public sealed record ThemeStat(
    BoardTheme Theme,
    int Boards,
    int Seats,
    int RankedSeats,
    double MeanRank,
    int Wins,
    SortedDictionary<string, int> Edits)
{
    /// <summary>匠人改造合计。</summary>
    public int EditTotal => Edits.Values.Sum();

    /// <summary>第 3 大回合结束时（该大回合最后一条快照）锁定在该主题棋盘上的玩家势力，升序；没有第 3 大回合快照的局不计。</summary>
    public IReadOnlyList<BigInteger> Round3Powers { get; init; } = [];

    /// <summary>同上各人在本局四人里的势力名次（1 = 最高，并列取最高名次）。</summary>
    public IReadOnlyList<int> Round3PowerRanks { get; init; } = [];
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
        var r3Powers = all.ToDictionary(t => t, _ => new List<BigInteger>());
        var r3Ranks = all.ToDictionary(t => t, _ => new List<int>());
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
            TurnSnapshot? round3 = log.Turns.LastOrDefault(t => t.MajorRound == Round3);
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

                // 主题表现（ai-terrain-review 1.3 → open-map B6）：整局胜率取上面的 wins / ranks，另记第 3 大回合结束时势力。
                if (round3?.PlayersState.FirstOrDefault(p => p.Player == player) is { } state)
                {
                    r3Powers[theme].Add(state.Total);
                    r3Ranks[theme].Add(1 + round3.PlayersState.Count(p => p.Total > state.Total));
                }
            }

            // open-map：地图整局不变，坐标归棋盘只看首部的开局棋盘清单（唯一的棋盘几何来源，不重建地图）。
            int? ZoneAt(Coord c)
            {
                for (int z = 0; z < entries.Count; z++)
                {
                    Coord origin = Coord.Parse(entries[z].Origin);
                    if (c.X >= origin.X && c.X < origin.X + entries[z].Width && c.Y >= origin.Y && c.Y < origin.Y + entries[z].Height)
                    {
                        return z;
                    }
                }

                return null;
            }

            // 匠人改造：按目标所在棋盘归主题。
            foreach (TurnSnapshot turn in log.Turns.Where(t => t.Edits is { Count: > 0 }))
            {
                foreach (TerrainEditEntry entry in turn.Edits!)
                {
                    int? zone = TerrainEdit.Parse(entry.Target).Cells.Select(ZoneAt).FirstOrDefault(z => z is not null);
                    if (ThemeOf(zone) is { } theme)
                    {
                        edits[theme][entry.Action] = edits[theme].GetValueOrDefault(entry.Action) + 1;
                    }
                }
            }
        }

        return new ThemeSection(
            logs.Count,
            ranked,
            [.. all.Select(t => new ThemeStat(t, boards[t], seats[t], ranks[t].Count, Statistics.Mean(ranks[t]), wins[t], edits[t])
            {
                Round3Powers = [.. r3Powers[t].Order()],
                Round3PowerRanks = [.. r3Ranks[t].Order()],
            })]);
    }

    /// <summary>"第 3 大回合结束时势力"取的大回合（= 构筑期最后一个大回合，<see cref="LinkModeAnalyzer.BuildRounds"/>）。</summary>
    public const int Round3 = LinkModeAnalyzer.BuildRounds;
}
