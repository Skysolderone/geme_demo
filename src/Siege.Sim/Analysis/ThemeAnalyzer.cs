using System.Numerics;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
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

    /// <summary>
    /// 主题表现（ai-terrain-review 1.3）：锁定在该主题棋盘上、有首轮战场结果的人次——"首轮战场" = 该玩家出现在名单里的第一次战场结算，且早于决赛配对事件
    /// （没有决赛配对的局不设上限）；没有这样的结算、但在决赛配对之前已出局（势力归零等）的，记为首轮战场落败（<see cref="FirstFieldEliminated"/>）。全部纳入局（含截断局）。
    /// </summary>
    public int FirstFieldSeats { get; init; }

    /// <summary>其中赢下首轮战场的人次（结算胜者是本人）。</summary>
    public int FirstFieldWins { get; init; }

    /// <summary><see cref="FirstFieldSeats"/> 里未经战场结算、在决赛配对之前就已出局的人次（计为落败）。</summary>
    public int FirstFieldEliminated { get; init; }

    /// <summary>锁定在该主题棋盘上、但决赛之前既没有经历战场结算也没有出局的人次（3+1 的单人棋盘、首轮未配对的等待者、截断前未结算等），不进首轮胜率的分母。</summary>
    public int NoFirstField { get; init; }

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
        var relics = all.ToDictionary(t => t, _ => 0);
        var controls = all.ToDictionary(t => t, _ => 0);
        var fieldSeats = all.ToDictionary(t => t, _ => 0);
        var fieldWins = all.ToDictionary(t => t, _ => 0);
        var noField = all.ToDictionary(t => t, _ => 0);
        var fieldEliminated = all.ToDictionary(t => t, _ => 0);
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

                // 主题表现（ai-terrain-review 1.3）：首轮战场与第 3 大回合结束时势力。
                if (FirstField(log, player) is { } first)
                {
                    fieldSeats[theme]++;
                    fieldWins[theme] += first.Winner == player ? 1 : 0;
                }
                else if (EliminatedBeforeFinal(log, player))
                {
                    fieldSeats[theme]++;
                    fieldEliminated[theme]++;
                }
                else
                {
                    noField[theme]++;
                }

                if (round3?.PlayersState.FirstOrDefault(p => p.Player == player) is { } state)
                {
                    r3Powers[theme].Add(state.Total);
                    r3Ranks[theme].Add(1 + round3.PlayersState.Count(p => p.Total > state.Total));
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
            [.. all.Select(t => new ThemeStat(t, boards[t], seats[t], ranks[t].Count, Statistics.Mean(ranks[t]), wins[t], edits[t], relics[t], controls[t])
            {
                FirstFieldSeats = fieldSeats[t],
                FirstFieldWins = fieldWins[t],
                FirstFieldEliminated = fieldEliminated[t],
                NoFirstField = noField[t],
                Round3Powers = [.. r3Powers[t].Order()],
                Round3PowerRanks = [.. r3Ranks[t].Order()],
            })]);
    }

    /// <summary>"第 3 大回合结束时势力"取的大回合（= 构筑期最后一个大回合，<see cref="LinkModeAnalyzer.BuildRounds"/>）。</summary>
    public const int Round3 = LinkModeAnalyzer.BuildRounds;

    /// <summary>
    /// 玩家的首轮战场结算：名单里有该玩家的第一次战场结算（按事件序），且早于决赛配对事件；没有为 <c>null</c>（见 <see cref="ThemeStat.FirstFieldSeats"/>）。
    /// </summary>
    internal static SettlementLogEntry? FirstField(MatchLog log, int player)
    {
        long final = FinalSeq(log);
        return log.Events
            .Where(e => e.Seq < final && e.Settlement is { } s && s.Entries.Any(x => x.Player == player))
            .OrderBy(e => e.Seq)
            .Select(e => e.Settlement)
            .FirstOrDefault();
    }

    /// <summary>玩家在决赛配对事件之前出局（任何出局原因）。</summary>
    internal static bool EliminatedBeforeFinal(MatchLog log, int player)
    {
        long final = FinalSeq(log);
        return log.Events.Any(e => e.Seq < final && e.Type == LogEventType.PlayerEliminated && e.Player == player);
    }

    /// <summary>决赛配对事件的事件序；没有决赛配对为 <see cref="long.MaxValue"/>。</summary>
    private static long FinalSeq(MatchLog log) =>
        log.Events.Where(e => e.Pairing is { Kind: nameof(PairingKind.Final) }).Select(e => (long)e.Seq).DefaultIfEmpty(long.MaxValue).Min();
}
