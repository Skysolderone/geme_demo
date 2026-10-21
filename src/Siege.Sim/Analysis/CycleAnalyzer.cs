using System.Numerics;
using Siege.Core.Board;
using Siege.Core.Scoring;
using Siege.Sim.Logging;

namespace Siege.Sim.Analysis;

// 本文件属于离线分析输出层：允许浮点（裁决 14）。它只读日志对象（来自文件），不引用任何对局运行时对象，不回放、不消费随机（cycle-stop D1「只读日志，不改行为」）。

/// <summary>
/// 循环提子诊断（cycle-stop D1 / S1 / S2，规格 match-telemetry「循环提子诊断」）的汇总：
/// 对每局取末段窗口 <see cref="WindowRounds"/> 个大回合（截断局按截断时刻），报告窗口内每名在局玩家的落子 / 提子 / 被提数与净势力变化、
/// 被提格集合、局部键（被提格 ∪ 四邻的占用者 + 设施）逐小回合求得的最短重复周期与重复次数、被提格之外的新增落子数，
/// 并把"窗口内 ≥ 2 名在局玩家净势力 ≤ 0 且存在局部周期"的局判为循环提子。
/// </summary>
/// <param name="Logs">诊断的局数（有小回合的纳入局）。</param>
/// <param name="Truncated">其中被 <c>turn_limit</c> 截断的局数。</param>
/// <param name="WindowRounds">窗口大回合数（S1：12）。</param>
/// <param name="Entries">逐局明细（按种子）。</param>
public sealed record CycleReviewSection(int Logs, int Truncated, int WindowRounds, IReadOnlyList<CycleReviewEntry> Entries)
{
    /// <summary>判为循环提子的局数。</summary>
    public int Cyclic => Entries.Count(e => e.Cyclic);

    /// <summary>判为循环提子且被截断的局数。</summary>
    public int CyclicTruncated => Entries.Count(e => e.Cyclic && e.Truncated);

    /// <summary>
    /// 提子拖延（代理口径，只报数不判定）：窗口首在局玩家里 ≥ 2 名净势力 ≤ 0、且窗口内有落子的小回合里提子回合占比 ≥ 3/4 的局数——
    /// 覆盖"互相提子但被提格不断换位、局部键不严格回归"的拖延局，供裁决"候选全是提子"判定是否要收窄。
    /// </summary>
    public int ExchangeBound => Entries.Count(e => e.Players.Count(p => p.NetPower <= 0) >= 2 && e.MoveTurns > 0 && e.CaptureTurns * 4 >= e.MoveTurns * 3);

    /// <summary>停滞停手次数合计（stagnation-trace D2：各局 <see cref="CycleReviewEntry.Stalls"/> 之和）。</summary>
    public int Stalls => Entries.Sum(e => e.Stalls);
}

/// <summary>一局的循环提子诊断。</summary>
/// <param name="Seed">对局种子（十六进制）。</param>
/// <param name="Truncated">是否被跑局层截断。</param>
/// <param name="EndReason">结束原因（<c>turn_limit</c> 或 <c>EndReason</c> 名）。</param>
/// <param name="EndMajorRound">末条快照所在的大回合（截断局 = 截断时刻）。</param>
/// <param name="WindowFirstRound">窗口首个大回合。</param>
/// <param name="WindowTurns">窗口内的小回合数。</param>
/// <param name="Players">窗口首在局（Active）的玩家各自的窗口统计（按玩家编号；窗口内出局的也列出，并记末状态）。</param>
/// <param name="CapturedCells">窗口内被提过的格集合 C（规范记法，按坐标顺序：行优先、再按列）。</param>
/// <param name="LocalCells">局部键覆盖的格数（C ∪ 四邻）。</param>
/// <param name="Period">局部键的最短重复周期（小回合数）；窗口内局部键从未回到先前状态为 <c>null</c>。</param>
/// <param name="Repeats">局部键回到先前某一状态的次数（只数局部键实际变化后的回归；连续 Pass 导致的相同状态不计）。</param>
/// <param name="OutsidePlacements">窗口内落在 C 之外的落子数（全局盘面为何不重复的归因之一）。</param>
/// <param name="OutsideChanges">窗口内局部键之外的盘面占用发生变化的小回合数（归因之二）。</param>
/// <param name="LocalEdits">窗口内新增的、目标触及局部格的改造数（归因之三：同形比对键含改造段，局部每多一道栅栏 / 烧一格林，占用相同的盘面也算新局面）。</param>
/// <param name="MoveTurns">窗口内有落子的小回合数。</param>
/// <param name="CaptureTurns">其中提了子的小回合数（"候选全是提子"判定的代理口径）。</param>
/// <param name="TotalPlacements">窗口内落子总数。</param>
/// <param name="RecapturePlacements">其中落在"本窗口内此前被提过的格"上的落子数（"落在最近被提过的格"判定的代理口径）。</param>
/// <param name="Cyclic">是否判为循环提子。</param>
/// <param name="Stalls">整局的停滞停手次数（stagnation-trace D2 / S1：快照 <c>Stalled</c> 为真的小回合数，即 AI 部署决策条目 <c>D:stall</c> 的次数；不限窗口，缺字段的旧日志计 0）。</param>
public sealed record CycleReviewEntry(
    string Seed,
    bool Truncated,
    string EndReason,
    int EndMajorRound,
    int WindowFirstRound,
    int WindowTurns,
    IReadOnlyList<CyclePlayerEntry> Players,
    IReadOnlyList<string> CapturedCells,
    int LocalCells,
    int? Period,
    int Repeats,
    int OutsidePlacements,
    int OutsideChanges,
    int LocalEdits,
    int MoveTurns,
    int CaptureTurns,
    int TotalPlacements,
    int RecapturePlacements,
    bool Cyclic,
    int Stalls);

/// <summary>一名在局玩家在窗口内的统计。</summary>
/// <param name="Player">玩家编号。</param>
/// <param name="EndStatus">窗口末的状态（<c>Active</c> / <c>Eliminated</c> / …）。</param>
/// <param name="Placements">窗口内落子数。</param>
/// <param name="Captures">窗口内提子数（提走的他人棋子数）。</param>
/// <param name="Captured">窗口内被提数（本人被提走的棋子数）。</param>
/// <param name="PowerStart">窗口首势力（窗口前最后一条快照的总势力；没有即 0）。</param>
/// <param name="PowerEnd">窗口末势力。</param>
/// <param name="StagnationAtEnd">终局时的"连续无净增大回合数"（D3 口径：每个大回合首的势力 ≤ 前一大回合首；整局算，不限窗口）。</param>
/// <param name="MaxStagnation">整局里最长的一段"连续无净增大回合数"。</param>
public sealed record CyclePlayerEntry(
    int Player,
    string EndStatus,
    int Placements,
    int Captures,
    int Captured,
    BigInteger PowerStart,
    BigInteger PowerEnd,
    int StagnationAtEnd,
    int MaxStagnation)
{
    /// <summary>净势力变化 = 窗口末 − 窗口首。</summary>
    public BigInteger NetPower => PowerEnd - PowerStart;
}

/// <summary>循环提子诊断的唯一实现（见 <see cref="CycleReviewSection"/>）。</summary>
public static class CycleAnalyzer
{
    /// <summary>窗口大回合数缺省（cycle-stop S1）。</summary>
    public const int DefaultWindowRounds = 12;

    /// <summary>
    /// 对纳入局逐局诊断。没有带小回合的局时返回 <c>null</c>（报告整段不出现）。
    /// 只读日志快照：占用者取各玩家 <c>Groups[*].Stones</c>，提子取 <c>Captures</c>（<c>坐标:P编号:类型</c>），设施取累计的 <c>Edits</c>；不回放对局。
    /// </summary>
    public static CycleReviewSection? Review(IReadOnlyList<MatchLog> included, int windowRounds = DefaultWindowRounds)
    {
        ArgumentNullException.ThrowIfNull(included);
        if (windowRounds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowRounds), windowRounds, "窗口大回合数必须为正。");
        }

        List<MatchLog> logs = [.. included.Where(l => l.Result is not null && l.Turns.Count > 0).OrderBy(l => l.Seed)];
        if (logs.Count == 0)
        {
            return null;
        }

        List<CycleReviewEntry> entries = [.. logs.Select(l => ReviewMatch(l, windowRounds))];
        return new CycleReviewSection(logs.Count, logs.Count(l => l.Result!.Truncated), windowRounds, entries);
    }

    /// <summary>单局诊断（窗口 = 最后 <paramref name="windowRounds"/> 个大回合）。</summary>
    public static CycleReviewEntry ReviewMatch(MatchLog log, int windowRounds = DefaultWindowRounds)
    {
        ArgumentNullException.ThrowIfNull(log);
        if (log.Turns.Count == 0 || log.Result is null)
        {
            throw new ArgumentException($"种子 {log.Header.Seed}：没有小回合或结果行，无法诊断。");
        }

        List<TurnSnapshot> turns = log.Turns;
        TurnSnapshot last = turns[^1];
        int endRound = last.MajorRound;
        int firstRound = Math.Max(1, endRound - windowRounds + 1);
        int windowStart = turns.FindIndex(t => t.MajorRound >= firstRound);
        if (windowStart < 0)
        {
            throw new FormatException($"种子 {log.Header.Seed}：快照的大回合序号不单调，找不到窗口起点。");
        }

        TurnSnapshot? baseline = windowStart > 0 ? turns[windowStart - 1] : null;
        List<TurnSnapshot> window = turns.GetRange(windowStart, turns.Count - windowStart);

        // 在局玩家 = 窗口首（窗口前最后一条快照；没有则窗口第一条快照）里 Active 的玩家：正常终局的最后一次结算会把落败者整批出局，
        // 若按窗口末取，规则终局的局只剩胜者一人，"≥ 2 名净势力 ≤ 0"永远不成立、判定也就没被真正检验过。出局玩家的末势力仍按末快照读（快照保留其势力）。
        TurnSnapshot windowHead = baseline ?? window[0];
        List<int> inPlay = [.. windowHead.PlayersState.Where(p => p.Status == nameof(PlayerStatus.Active)).Select(p => p.Player).Order()];

        // 窗口内的提子：被提格集合 C、每名玩家提 / 被提数。
        var captured = new SortedSet<Coord>();
        var capturedBefore = new List<SortedSet<Coord>>();   // 每条窗口快照结算之前已被提过的格（"落在最近被提过的格"的代理）
        var capturesBy = new Dictionary<int, int>();
        var capturedOf = new Dictionary<int, int>();
        var placementsBy = new Dictionary<int, int>();
        foreach (TurnSnapshot turn in window)
        {
            capturedBefore.Add(new SortedSet<Coord>(captured));
            placementsBy[turn.Player] = placementsBy.GetValueOrDefault(turn.Player) + turn.Placements.Count;
            foreach (string capture in turn.Captures)
            {
                (Coord cell, int owner) = ParseCapture(log.Header.Seed, capture);
                captured.Add(cell);
                capturesBy[turn.Player] = capturesBy.GetValueOrDefault(turn.Player) + 1;
                capturedOf[owner] = capturedOf.GetValueOrDefault(owner) + 1;
            }
        }

        // 局部键的格集合 = C ∪ 几何四邻（Adjacency.Neighbors 是唯一的四邻实现；宽高取日志里出现过的坐标上界 + 2，越界格不可能被占用）。
        List<Dictionary<Coord, int>> occupancy = [.. window.Select(OccupancyOf)];
        int width = 1, height = 1;
        foreach (Coord c in captured.Concat(occupancy.SelectMany(o => o.Keys)))
        {
            width = Math.Max(width, c.X + 2);
            height = Math.Max(height, c.Y + 2);
        }

        var local = new SortedSet<Coord>(captured);
        foreach (Coord c in captured)
        {
            local.UnionWith(Adjacency.Neighbors(width, height, c));
        }

        // 设施：从第 1 小回合起累计全部改造（桥 / 栅栏 / 烧林只做加法），窗口内逐条快照取"目标触及局部格"的那些。
        List<string> facilities = [];
        for (int i = 0; i < windowStart; i++)
        {
            facilities.AddRange(FacilitiesOf(turns[i], local));
        }

        int localEdits = 0;
        List<string> keys = [];
        var outsideBefore = new Dictionary<Coord, int>(baseline is null ? [] : OccupancyOf(baseline).Where(kv => !local.Contains(kv.Key)));
        int outsideChanges = 0;
        for (int i = 0; i < window.Count; i++)
        {
            List<string> added = [.. FacilitiesOf(window[i], local)];
            localEdits += added.Count;
            facilities.AddRange(added);
            keys.Add(LocalKey(local, occupancy[i], facilities));
            var outside = new Dictionary<Coord, int>(occupancy[i].Where(kv => !local.Contains(kv.Key)));
            if (!SameOccupancy(outsideBefore, outside))
            {
                outsideChanges++;
            }

            outsideBefore = outside;
        }

        (int? period, int repeats) = ShortestPeriod(keys);

        int moveTurns = 0, captureTurns = 0, totalPlacements = 0, recapture = 0, outsidePlacements = 0;
        for (int i = 0; i < window.Count; i++)
        {
            TurnSnapshot turn = window[i];
            if (turn.Placements.Count > 0)
            {
                moveTurns++;
                captureTurns += turn.Captures.Count > 0 ? 1 : 0;
            }

            foreach (string placement in turn.Placements)
            {
                Coord cell = ParsePlacement(log.Header.Seed, placement);
                totalPlacements++;
                recapture += capturedBefore[i].Contains(cell) ? 1 : 0;
                outsidePlacements += captured.Contains(cell) ? 0 : 1;
            }
        }

        var players = new List<CyclePlayerEntry>();
        foreach (int player in inPlay)
        {
            (int atEnd, int max) = StagnationOf(turns, player, endRound);
            players.Add(new CyclePlayerEntry(
                player,
                last.PlayersState.FirstOrDefault(p => p.Player == player)?.Status ?? "?",
                placementsBy.GetValueOrDefault(player),
                capturesBy.GetValueOrDefault(player),
                capturedOf.GetValueOrDefault(player),
                baseline is null ? BigInteger.Zero : TotalOf(baseline, player),
                TotalOf(last, player),
                atEnd,
                max));
        }

        bool cyclic = players.Count(p => p.NetPower <= 0) >= 2 && period is not null;
        return new CycleReviewEntry(
            log.Header.Seed,
            log.Result.Truncated,
            log.Result.Reason,
            endRound,
            firstRound,
            window.Count,
            players,
            [.. captured.Select(c => c.ToNotation())],
            local.Count,
            period,
            repeats,
            outsidePlacements,
            outsideChanges,
            localEdits,
            moveTurns,
            captureTurns,
            totalPlacements,
            recapture,
            cyclic,
            turns.Count(t => t.Stalled == true));
    }

    /// <summary>
    /// 局部键序列的最短重复周期：先把连续相同的键压成一个状态（连续 Pass 不算"回到先前状态"），再在压缩后的序列里找
    /// 相同状态对 (i, j)、j &gt; i + 1，周期 = 两者小回合序号之差的最小值；重复次数 = 压缩序列里"回到先前出现过的状态"的次数。
    /// </summary>
    internal static (int? Period, int Repeats) ShortestPeriod(IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var states = new List<(string Key, int Index)>();
        for (int i = 0; i < keys.Count; i++)
        {
            if (states.Count == 0 || !string.Equals(states[^1].Key, keys[i], StringComparison.Ordinal))
            {
                states.Add((keys[i], i));
            }
        }

        int? period = null;
        int repeats = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int j = 0; j < states.Count; j++)
        {
            if (!seen.Add(states[j].Key))
            {
                repeats++;
            }

            for (int i = 0; i < j - 1; i++)
            {
                if (string.Equals(states[i].Key, states[j].Key, StringComparison.Ordinal))
                {
                    int distance = states[j].Index - states[i].Index;
                    period = period is { } p && p <= distance ? p : distance;
                }
            }
        }

        return (period, repeats);
    }

    /// <summary>
    /// 玩家的"连续无净增大回合数"（D3 口径）：第 r 大回合首的势力 = 最后一条 <c>MajorRound &lt; r</c> 的快照里的总势力（没有即 0）；
    /// 第 r 大回合"无净增" = 第 r 大回合首 ≤ 第 r − 1 大回合首。返回终局时的连续长度与整局最大连续长度。
    /// </summary>
    internal static (int AtEnd, int Max) StagnationOf(IReadOnlyList<TurnSnapshot> turns, int player, int endRound)
    {
        ArgumentNullException.ThrowIfNull(turns);
        var starts = new BigInteger[endRound + 1];
        BigInteger current = BigInteger.Zero;
        int next = 0;
        for (int r = 1; r <= endRound; r++)
        {
            while (next < turns.Count && turns[next].MajorRound < r)
            {
                current = TotalOf(turns[next], player);
                next++;
            }

            starts[r] = current;
        }

        int streak = 0, max = 0;
        for (int r = 2; r <= endRound; r++)
        {
            streak = starts[r] <= starts[r - 1] ? streak + 1 : 0;
            max = Math.Max(max, streak);
        }

        return (streak, max);
    }

    private static BigInteger TotalOf(TurnSnapshot turn, int player) =>
        turn.PlayersState.FirstOrDefault(p => p.Player == player)?.Total ?? BigInteger.Zero;

    /// <summary>快照结算后的盘面占用：坐标 → 占用者（各玩家棋串的棋子，不含类型——与同形比对键同口径）。</summary>
    private static Dictionary<Coord, int> OccupancyOf(TurnSnapshot turn)
    {
        var occupancy = new Dictionary<Coord, int>();
        foreach (PlayerEntry player in turn.PlayersState)
        {
            foreach (string stone in player.Groups.SelectMany(g => g.Stones))
            {
                occupancy[Coord.Parse(stone)] = player.Player;
            }
        }

        return occupancy;
    }

    private static bool SameOccupancy(Dictionary<Coord, int> a, Dictionary<Coord, int> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out int owner) && owner == kv.Value);

    private static string LocalKey(SortedSet<Coord> local, Dictionary<Coord, int> occupancy, List<string> facilities) =>
        string.Join(";", local.Select(c => occupancy.TryGetValue(c, out int owner) ? $"{c.ToNotation()}={owner}" : $"{c.ToNotation()}=."))
        + "|" + string.Join(",", facilities.Order(StringComparer.Ordinal));

    /// <summary>本小回合完成的、目标触及局部格的改造（<c>动作:目标</c>）。目标记法 <c>B:F8</c> / <c>X:F8</c> / <c>F:F8-F9</c>，冒号后按 <c>-</c> 拆成格。</summary>
    private static IEnumerable<string> FacilitiesOf(TurnSnapshot turn, SortedSet<Coord> local)
    {
        foreach (TerrainEditEntry edit in turn.Edits ?? [])
        {
            string target = edit.Target;
            int colon = target.IndexOf(':', StringComparison.Ordinal);
            string cells = colon < 0 ? target : target[(colon + 1)..];
            if (cells.Split('-').Any(token => Coord.TryParse(token, out Coord c) && local.Contains(c)))
            {
                yield return $"{edit.Action}:{edit.Target}";
            }
        }
    }

    /// <summary>快照提子记录 <c>坐标:P编号:类型</c> → 格与原占用者；格式不符即响亮失败。</summary>
    internal static (Coord Cell, int Owner) ParseCapture(string seed, string capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        string[] parts = capture.Split(':');
        if (parts.Length < 2 || parts[1].Length < 2 || parts[1][0] != 'P' || !int.TryParse(parts[1][1..], System.Globalization.CultureInfo.InvariantCulture, out int owner))
        {
            throw new FormatException($"种子 {seed}：提子记录 \"{capture}\" 不是「坐标:P编号:类型」。");
        }

        return (Coord.Parse(parts[0]), owner);
    }

    /// <summary>快照落子记录 <c>坐标:类型</c>（匠人可带 <c>+改造</c>）→ 格。</summary>
    private static Coord ParsePlacement(string seed, string placement)
    {
        int colon = placement.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0)
        {
            throw new FormatException($"种子 {seed}：落子记录 \"{placement}\" 不是「坐标:类型」。");
        }

        return Coord.Parse(placement[..colon]);
    }
}
