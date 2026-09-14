using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests;

/// <summary>
/// 跑局 / 日志 / 分析测试的公共夹具。真实对局只用小样本（Easy、少量种子、小 <c>MaxMajorRounds</c>）验证机制；
/// 200 / 2000 局规模只走 CLI（<c>Siege.Sim run</c>），不进单元测试。
/// </summary>
internal static class SimFixtures
{
    /// <summary>基准图 4 人 Easy 配置。</summary>
    internal static RunConfig Config(
        int count = 1, ulong seedStart = 1, int maxRounds = 4, AiDifficulty difficulty = AiDifficulty.Easy,
        EventRetention retention = EventRetention.Full, int players = 4, int? injectFailureAtTurn = null, bool compress = false) =>
        new()
        {
            Players = [.. Enumerable.Range(0, players).Select(_ => new PlayerAiConfig { Difficulty = difficulty })],
            SeedStart = seedStart,
            Count = count,
            MaxMajorRounds = maxRounds,
            EventRetention = retention,
            FullEventSamplePermille = 0,
            InjectFailureAtTurn = injectFailureAtTurn,
            Compress = compress,
        };

    /// <summary>全部测试共用的真实样本：Easy 4 局、4 大回合、完整事件流（只算一次）。</summary>
    internal static readonly Lazy<List<MatchLog>> Sample = new(() => BatchRunner.Execute(Config(count: 4, seedStart: 11, maxRounds: 4), parallelism: 1));

    /// <summary>独立的临时目录（每次调用都清空重建）。</summary>
    internal static string TempDir(string name)
    {
        string dir = Path.Combine(Path.GetTempPath(), "siege-sim-tests", $"{name}-{Environment.ProcessId}");
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }

        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>快照投影成确定性文本（去耗时）：含 List 的 record 不能直接 Assert.Equal（引用相等）。</summary>
    internal static IEnumerable<string> TurnTexts(IEnumerable<TurnSnapshot> turns) =>
        turns.Select(t => System.Text.Json.JsonSerializer.Serialize(t with { ElapsedMs = null }));

    /// <summary>经文本往返克隆一份日志（与内存对象完全脱钩），可改种子与结果行。</summary>
    internal static MatchLog Clone(MatchLog log, ulong? seed = null, Func<LogResult, LogResult>? result = null)
    {
        MatchLog copy = MatchLog.Parse(log.FullText());
        return new MatchLog
        {
            Header = seed is { } s ? copy.Header with { Seed = new Siege.Core.Determinism.GameSeed(s).ToString() } : copy.Header,
            Turns = copy.Turns,
            Events = copy.Events,
            Result = copy.Result is { } r && result is not null ? result(r) : copy.Result,
            Failure = copy.Failure,
        };
    }

    // ---------- 合成日志（分析口径用；不跑对局） ----------

    internal static MatchLog Synthetic(
        ulong seed, IEnumerable<TurnSnapshot> turns, IEnumerable<LogEvent> events, LogResult result, int players = 4, int[]? zones = null, bool relicsConverged = true, RelicEntry[]? relics = null) =>
        new()
        {
            Header = new LogHeader
            {
                MapId = "synthetic",
                Seed = new Siege.Core.Determinism.GameSeed(seed).ToString(),
                Config = Config(players: players),
                Players = [.. Enumerable.Range(0, players)],
                Zones = [.. zones ?? Enumerable.Range(0, players)],
                FirstOrder = [.. Enumerable.Range(0, players)],
                Relics = [.. relics ?? []],
                RelicsConverged = relicsConverged,
                Retention = EventRetention.Full,
            },
            Turns = [.. turns],
            Events = [.. events],
            Result = result,
        };

    internal static TurnSnapshot Turn(
        int turn, int majorRound, int player, long[] totals, string[]? placements = null, int deployLimit = 3,
        int showCount = 5, int freePick = 3, int typeSlots = 5, GroupEntry[]? groupsOfPlayer = null, string[]? captures = null) =>
        new()
        {
            Turn = turn,
            MajorRound = majorRound,
            Player = player,
            Position = 0,
            Passed = placements is null || placements.Length == 0,
            Placements = [.. placements ?? []],
            Captures = [.. captures ?? []],
            ShowCount = showCount,
            FreePickCount = freePick,
            TypeSlots = typeSlots,
            DeployLimit = deployLimit,
            ActionOrder = [.. Enumerable.Range(0, totals.Length)],
            PlayersState = [.. totals.Select((t, i) => new PlayerEntry
            {
                Player = i,
                Status = "Active",
                Total = t,
                Groups = i == player && groupsOfPlayer is not null ? [.. groupsOfPlayer] : [],
            })],
        };

    internal static LogResult ResultOf(int majorRound, int[] winners, bool converged = true, int players = 4, PeakEntry? peak = null, bool? peakDestroyed = null) =>
        new()
        {
            Reason = converged ? nameof(EndReason.AllPassed) : SimEndReason.MaxMajorRoundsReached,
            Converged = converged,
            MajorRound = majorRound,
            TurnCount = majorRound * players,
            Standings = [.. Enumerable.Range(0, players).Select(p => new StandingEntry
            {
                Rank = winners.Contains(p) ? 1 : 2,
                Player = p,
                Group = nameof(StandingGroup.Finisher),
                Status = "Active",
            })],
            Winners = [.. winners],
            Peak = peak,
            PeakDestroyed = peakDestroyed,
        };

    /// <summary>大回合结束事件：各玩家的名次与下一轮位置。</summary>
    internal static LogEvent RoundEnded(int completedRound, int turn, params (int Player, int Rank, int Next)[] entries)
    {
        var values = new Dictionary<string, long> { ["ActiveCount"] = entries.Length };
        foreach ((int player, int rank, int next) in entries)
        {
            values[$"P{player}.Rank"] = rank;
            values[$"P{player}.Next"] = next;
            values[$"P{player}.Power"] = 0;
            values[$"P{player}.Bonus"] = 0;
            values[$"P{player}.Value"] = entries.Length - rank;
            values[$"P{player}.Prev"] = -1;
        }

        return new LogEvent { Seq = turn, Turn = turn, MajorRound = completedRound, Type = LogEventType.MajorRoundEnded, Values = values };
    }

    internal static LogEvent Recruit(int turn, int player, string candidates, string picks) =>
        new()
        {
            Seq = turn,
            Turn = turn,
            MajorRound = 1,
            Type = LogEventType.Recruit,
            Player = player,
            Detail = $"candidates={candidates} picks={picks} discards=",
            Values = new Dictionary<string, long> { ["Recruited"] = picks.Split(',', StringSplitOptions.RemoveEmptyEntries).Length, ["Revoked"] = 0, ["Deployed"] = 1 },
        };
}

/// <summary>只在部署阶段摆指定落点、不预演、被拒绝即 Pass 的控制者（用于制造被判定为非法的提交）。</summary>
internal sealed class BlindController(params string[] cells) : ITurnController
{
    public void OrganizeHand(PlayerHandAccess hand, int overflow)
    {
        for (int i = 0; i < overflow; i++)
        {
            hand.Discard(hand.PrivateView().Types.First());
        }
    }

    public void Recruit(PlayerHandAccess hand, RecruitPanelView panel)
    {
    }

    public void Deploy(StagedBatch batch, Func<RehearsalResult> rehearse)
    {
        foreach (string cell in cells)
        {
            if (batch.Stage(Coord.Parse(cell), PieceType.Basic) is { } failure)
            {
                throw new InvalidOperationException($"夹具暂放失败：{failure.Message}");
            }
        }
    }

    public bool OnRejected(StagedBatch batch, BatchFailure failure)
    {
        batch.Clear();
        return false;
    }
}

/// <summary>包一层：征募阶段改为不选任何候选，其余转发给内层 AI。</summary>
internal sealed class NoRecruitController(ITurnController inner) : ITurnController
{
    public void OrganizeHand(PlayerHandAccess hand, int overflow) => inner.OrganizeHand(hand, overflow);

    public void Recruit(PlayerHandAccess hand, RecruitPanelView panel)
    {
    }

    public void Deploy(StagedBatch batch, Func<RehearsalResult> rehearse) => inner.Deploy(batch, rehearse);

    public bool OnRejected(StagedBatch batch, BatchFailure failure) => inner.OnRejected(batch, failure);
}
