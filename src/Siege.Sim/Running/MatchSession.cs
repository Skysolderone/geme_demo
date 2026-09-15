using System.Collections.Immutable;
using System.Diagnostics;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Core.Relics;
using Siege.Core.Scoring;
using Siege.Sim.Config;
using Siege.Sim.Logging;

namespace Siege.Sim.Running;

/// <summary>
/// 一局的跑局会话：建局、装 AI、逐小回合驱动 <see cref="MatchRunner"/>、记日志、到终局或大回合上限（裁决 13）、捕获失败。
/// 每个会话独享自己的 <see cref="GameSeed"/> 派生状态，会话之间零共享，因此可并行。
/// </summary>
/// <remarks>
/// 随机只来自 <see cref="GameSeed.Stream"/>：AI 走 <c>ai-P{n}</c>，跑局层的完整事件抽样走 <c>sim-sample</c>。
/// 墙钟（<see cref="Stopwatch"/>）只用于记录耗时，不参与任何决定。本文件不出现浮点（裁决 14）。
/// </remarks>
public sealed class MatchSession
{
    /// <summary>跑局层抽样子流名。</summary>
    public const string SampleStream = "sim-sample";

    private static readonly string[] DimensionNames = [.. Enum.GetNames<EvaluationDimension>()];

    private readonly TurnTrace _trace = new();
    private readonly List<TurnSnapshot> _turns = [];
    private readonly List<LogEvent> _events = [];
    private readonly List<long> _majorRoundMs = [];
    private readonly Stopwatch _total = new();
    private readonly EventRetention _retention;
    private readonly List<int> _firstOrder;
    private MatchPublicView _previous;
    private int _turn;
    private int _eventSeq;
    private int _flowCursor;
    private int _recruitCursor;
    private bool _finished;

    private MatchSession(MatchFlow match, RunConfig config)
    {
        Match = match ?? throw new ArgumentNullException(nameof(match));
        Config = (config ?? throw new ArgumentNullException(nameof(config))).Validated();
        if (match.Phase != MatchPhase.InProgress)
        {
            throw new SiegeRuleException("会话要求对局已完成插旗并处于进行中。");
        }

        if (match.Players.Length != config.Players.Count)
        {
            throw new SiegeRuleException($"配置了 {config.Players.Count} 名玩家，对局却有 {match.Players.Length} 名。");
        }

        // round-cap D3：跑局配置与对局配置只能有一个上限值，否则日志首部与报告会和规则层分叉。
        if (match.MaxMajorRounds != config.MaxMajorRounds)
        {
            throw new SiegeRuleException($"跑局配置的大回合上限为 {config.MaxMajorRounds}，对局配置却为 {match.MaxMajorRounds}。");
        }

        if (match.DominanceStartRound != config.DominanceStartRound)
        {
            throw new SiegeRuleException($"跑局配置的碾压起始大回合为 {config.DominanceStartRound}，对局配置却为 {match.DominanceStartRound}。");
        }

        Runner = new MatchRunner(match);
        Seed = match.Seed;
        bool sampled = Seed.Stream(SampleStream).NextPermille(config.FullEventSamplePermille);
        _retention = config.EventRetention == EventRetention.Full || sampled ? EventRetention.Full : EventRetention.SnapshotsOnly;
        _previous = match.Publish();
        _firstOrder = [.. match.ActionOrder.Select(p => p.Value)];
        CopyFlowEvents();
        for (int i = 0; i < match.Players.Length; i++)
        {
            AttachConfigured(match.Players[i], config.Players[i]);
        }
    }

    public MatchFlow Match { get; }

    public MatchRunner Runner { get; }

    public RunConfig Config { get; }

    public GameSeed Seed { get; }

    /// <summary>已完成的小回合数。</summary>
    public int TurnCount => _turn;

    // ---------- 建局 ----------

    /// <summary>按配置与种子建一局：地图静态校验、信物按 <c>relic-gen</c> 生成、P<i>i</i> 插旗到出生区 <i>i</i>。</summary>
    public static MatchSession Create(RunConfig config, ulong seed, MapData? map = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validated();
        map ??= MapCatalog.Resolve(config.MapId);
        PlayerId[] players = config.PlayerIds();
        // round-cap D3：大回合上限是对局配置，跑局层只把 --max-rounds 透传进去。
        MatchFlow match = MatchFlow.Create(map, new GameSeed(seed), players, MatchOptions.Immediate with { MaxMajorRounds = config.MaxMajorRounds, DominanceStartRound = config.DominanceStartRound });
        match.PlantSequentially(players.Select((p, i) => (p, i % map.BirthZones.Length)));
        return new MatchSession(match, config);
    }

    /// <summary>测试接缝：对已插旗的对局（可以是未校验的合成地图）建会话。</summary>
    internal static MatchSession ForMatch(MatchFlow match, RunConfig config) => new(match, config);

    private void AttachConfigured(PlayerId player, PlayerAiConfig ai)
    {
        if (ai.DebugAi)
        {
            DebugTurnController.Create(Runner, player, debugMode: true, ai.Difficulty, ai.Weights, ai.Search);
            Runner.SetController(player, new LoggingController(Runner.ControllerOf(player), _trace));
        }
        else
        {
            SetController(player, HeuristicAi.Create(Match, player, ai.Difficulty, ai.Weights, ai.Search));
        }
    }

    /// <summary>替换某玩家的控制者（经日志装饰器）。未知玩家抛 <see cref="SiegeRuleException"/>。</summary>
    public void SetController(PlayerId player, ITurnController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        Runner.SetController(player, new LoggingController(controller, _trace));
    }

    /// <summary>人工接管（记录在标注中，分析默认排除）。</summary>
    public void TakeOver(PlayerId player, ITurnController manual)
    {
        ArgumentNullException.ThrowIfNull(manual);
        Runner.TakeOver(player, new LoggingController(manual, _trace));
    }

    public void HandBack(PlayerId player) => Runner.HandBack(player);

    /// <summary>某玩家当前控制者内层的启发式 AI；人工控制中为 <c>null</c>。</summary>
    public HeuristicTurnController? AiOf(PlayerId player) => (Runner.ControllerOf(player) as LoggingController)?.Heuristic;

    // ---------- 驱动 ----------

    /// <summary>跑到终局（含规则级大回合上限）；任何异常（含小回合硬停）都被捕获为失败局记录。返回完整日志。</summary>
    public MatchLog Run()
    {
        try
        {
            while (RunTurn())
            {
            }

            return Finish();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Fail(ex);
        }
    }

    /// <summary>跑一个小回合并记录快照与事件。返回是否还能继续。</summary>
    public bool RunTurn()
    {
        if (_finished)
        {
            throw new SiegeRuleException("会话已结束。");
        }

        if (Match.Phase != MatchPhase.InProgress)
        {
            return false;
        }

        // 防死锁硬停（round-cap D3）：不是终局原因，以异常落 failed 日志。上限为 0 时这是唯一兜底。
        if (_turn >= Config.MaxTurns)
        {
            throw new SimAssertionException($"对局超过 {Config.MaxTurns} 个小回合仍未结束（大回合上限 {Match.MaxMajorRounds}）：疑似死锁。");
        }

        PlayerId player = Match.CurrentPlayer ?? throw new SimAssertionException("进行中的对局没有当前玩家。");
        int majorRound = Match.MajorRound;
        int position = Match.ActionOrder.IndexOf(player);
        MatchPublicView before = _previous;
        _trace.Reset();
        int turnStartedBefore = Match.Events.Count(e => e.Kind == FlowEventKind.TurnStarted);

        _total.Start();
        var sw = Stopwatch.StartNew();
        Runner.RunTurn();
        sw.Stop();
        _total.Stop();
        AccumulateRoundTime(majorRound, sw.ElapsedMilliseconds);

        if (Match.Events.Count(e => e.Kind == FlowEventKind.TurnStarted) == turnStartedBefore)
        {
            // 小回合没有真正开始（推进时直接终局），只同步流程事件。
            CopyFlowEvents();
            _previous = Match.Publish();
            return Match.Phase == MatchPhase.InProgress;
        }

        _turn++;
        MatchPublicView after = Match.Publish();
        AssertInvariants(after);
        RecordTurn(player, majorRound, position, before, after, sw.ElapsedMilliseconds);
        _previous = after;

        if (Config.InjectFailureAtTurn == _turn)
        {
            throw new SimAssertionException($"测试专用：在第 {_turn} 个小回合注入的断言失败。");
        }

        return Match.Phase == MatchPhase.InProgress;
    }

    private void AccumulateRoundTime(int majorRound, long ms)
    {
        while (_majorRoundMs.Count < majorRound)
        {
            _majorRoundMs.Add(0);
        }

        _majorRoundMs[majorRound - 1] += ms;
    }

    private void AssertInvariants(MatchPublicView after)
    {
        if (after.Stage != TurnStage.Idle)
        {
            throw new SimAssertionException($"小回合结束后阶段应为 Idle，实际 {after.Stage}。");
        }

        int started = Match.Events.Count(e => e.Kind == FlowEventKind.TurnStarted);
        int ended = Match.Events.Count(e => e.Kind == FlowEventKind.TurnEnded);
        if (started != ended)
        {
            throw new SimAssertionException($"小回合开始 {started} 次、结束 {ended} 次，不配对。");
        }
    }

    // ---------- 记录 ----------

    private void RecordTurn(PlayerId player, int majorRound, int position, MatchPublicView before, MatchPublicView after, long elapsedMs)
    {
        var placements = new List<string>();
        var captures = new List<string>();
        foreach (Coord c in after.Board.AllCoords())
        {
            Occupant? was = before.Board[c].Occupant;
            Occupant? now = after.Board[c].Occupant;
            if (was is null && now is { } placed)
            {
                placements.Add(new Placement(c, placed.Type).ToString());
            }
            else if (was is { } taken && now is null)
            {
                captures.Add(new CapturedStone(c, taken.Owner, taken.Type).ToString());
            }
        }

        RecruitTurnRecord? recruit = null;
        if (Match.Hands.Records.Count > _recruitCursor)
        {
            recruit = Match.Hands.Records[^1];
            _recruitCursor = Match.Hands.Records.Count;
        }

        bool passed = recruit?.Passed ?? placements.Count == 0;
        int turn = _turn;
        int? pid = player.Value;

        if (recruit is not null)
        {
            Add(turn, majorRound, LogEventType.Recruit, pid,
                $"candidates={string.Join(",", recruit.Candidates)} picks={string.Join(",", recruit.PickedTypes)} discards={string.Join(",", recruit.Discarded)}",
                values: new Dictionary<string, long>
                {
                    ["Recruited"] = recruit.RecruitedCount,
                    ["Revoked"] = recruit.RevokedCount,
                    ["Deployed"] = recruit.DeployedCount,
                });
        }

        foreach ((ImmutableArray<Placement> tried, BatchFailure failure) in _trace.IllegalRehearsals)
        {
            Add(turn, majorRound, LogEventType.Rehearsal, pid, $"[{string.Join(",", tried)}] {failure.Message}",
                coords: [.. failure.Coords.Select(c => c.ToNotation())], failureKind: failure.Kind.ToString());
        }

        int attempt = 0;
        foreach ((ImmutableArray<Placement> tried, BatchFailure failure) in _trace.Rejections)
        {
            Add(turn, majorRound, LogEventType.Rejected, pid, $"[{string.Join(",", tried)}] {failure.Message}",
                coords: [.. failure.Coords.Select(c => c.ToNotation())], failureKind: failure.Kind.ToString(),
                values: new Dictionary<string, long> { ["Attempt"] = ++attempt });
        }

        HeuristicTurnController? ai = AiOf(player);
        if (!passed)
        {
            var values = new Dictionary<string, long>
            {
                ["Captures"] = captures.Count,
                ["SuperkoPassed"] = 1,
                ["Rehearsals"] = _trace.Rehearsals,
            };
            if (ai?.LastChoice is { } choice)
            {
                for (int i = 0; i < EvaluationBreakdown.DimensionCount; i++)
                {
                    values[DimensionNames[i]] = choice.Evaluation.Raw[i];
                }

                values["Total"] = choice.Total;
            }

            Add(turn, majorRound, LogEventType.Settled, pid,
                $"placed=[{string.Join(",", placements)}] captured=[{string.Join(",", captures)}]",
                coords: placements.Select(p => p.Split(':')[0]).ToList(), values: values);
        }

        if (ai is not null && !ai.LastCandidates.IsDefaultOrEmpty)
        {
            Add(turn, majorRound, LogEventType.Candidates, pid, string.Join(" | ", ai.LastCandidates));
        }

        foreach (RelicPublicState now in after.Relics)
        {
            RelicPublicState was = before.Relics.First(r => r.Coord == now.Coord);
            if (was.RevealedInMajorRound is null && now.RevealedInMajorRound is { } round)
            {
                Add(turn, round, LogEventType.Reveal, pid, now.Content!.Value.ToString(), coords: [now.Coord.ToNotation()]);
            }

            if (was.Control != now.Control)
            {
                Add(turn, majorRound, LogEventType.ControlChanged, now.Control.Holder is { } h ? h.Value : null,
                    $"{now.Coord.ToNotation()}: {was.Control} -> {now.Control}", coords: [now.Coord.ToNotation()]);
            }
        }

        string rankBefore = RankingText(before.Power);
        string rankAfter = RankingText(after.Power);
        if (rankBefore != rankAfter)
        {
            Add(turn, majorRound, LogEventType.RankChanged, pid, $"{rankBefore} -> {rankAfter}");
        }

        CopyFlowEvents();

        _turns.Add(new TurnSnapshot
        {
            Turn = turn,
            MajorRound = majorRound,
            Player = player.Value,
            Position = position,
            Passed = passed,
            Placements = placements,
            Captures = captures,
            Rejections = _trace.Rejections.Count,
            ShowCount = _trace.ShowCount,
            FreePickCount = _trace.FreePickCount,
            TypeSlots = _trace.TypeSlots,
            DeployLimit = _trace.DeployLimit,
            ActionOrder = [.. after.ActionOrder.Select(p => p.Value)],
            PassStreak = after.PassStreak,
            PlayersState = PlayerEntries(after),
            Relics = [.. after.Relics.Select(r => new RelicStateEntry
            {
                Coord = r.Coord.ToNotation(),
                Revealed = r.IsRevealed,
                Control = r.Control.Kind.ToString(),
                Holder = r.Control.Holder?.Value,
            })],
            ElapsedMs = elapsedMs,
        });
    }

    private static string RankingText(PowerSnapshot? power) =>
        power is null ? string.Empty : string.Join(",", power.Ranking.Select(g => $"{g.Rank}:{string.Join("=", g.Players)}"));

    private static List<PlayerEntry> PlayerEntries(MatchPublicView view)
    {
        var list = new List<PlayerEntry>();
        foreach (PlayerFlowState state in view.Players)
        {
            PlayerPower? power = view.Power?.Players.FirstOrDefault(p => p.Player == state.Player);
            HandPublicView? hand = view.Hands.FirstOrDefault(h => h.Player == state.Player);
            list.Add(new PlayerEntry
            {
                Player = state.Player.Value,
                Status = state.Status.ToString(),
                Protection = state.HasOpeningProtection,
                Total = power?.Total ?? 0,
                Territory = power?.TerritoryScore ?? 0,
                Rank = view.Power?.RankOf(state.Player),
                HandTypes = hand is null ? [] : [.. hand.Types.Select(t => t.ToString())],
                Groups = power is null ? [] : [.. power.Groups.Select(g => new GroupEntry
                {
                    Stones = [.. g.Stones.Select(s => s.ToNotation())],
                    Base = g.BaseTotal,
                    LineBonus = g.LineBonus,
                    SynergyBonus = g.SynergyBonus,
                    MultiplierCount = g.MultiplierCount,
                    EffectiveMultiplierCount = g.EffectiveMultiplierCount,
                    Power = g.Power,
                    PieceCounts = PieceCountsOf(view.Board, g),
                })],
            });
        }

        return list;
    }

    /// <summary>棋串各棋子类型的数量（五种全写，含 0，按枚举顺序），从快照盘面按棋子坐标逐枚统计。</summary>
    private static Dictionary<string, int> PieceCountsOf(GameBoard board, GroupPower group)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (PieceType type in Enum.GetValues<PieceType>())
        {
            counts[type.ToString()] = 0;
        }

        foreach (Coord stone in group.Stones)
        {
            PieceType type = (board[stone].Occupant ?? throw new InvalidOperationException($"势力明细与快照盘面不一致：{stone.ToNotation()} 为空。")).Type;
            counts[type.ToString()]++;
        }

        return counts;
    }

    /// <summary>把流程层新增的事件（不含阶段噪声）复制进日志；大回合结束附先手值明细。</summary>
    private void CopyFlowEvents()
    {
        IReadOnlyList<FlowEvent> events = Match.Events;
        for (; _flowCursor < events.Count; _flowCursor++)
        {
            FlowEvent e = events[_flowCursor];
            switch (e.Kind)
            {
                case FlowEventKind.MajorRoundEnded:
                    // 流程层发此事件时 MajorRound 已推进到下一轮；日志记"完成的那一轮"。
                    int completed = e.MajorRound - 1;
                    InitiativeReport report = Match.InitiativeReports.Last(r => r.CompletedMajorRound == completed);
                    var values = new Dictionary<string, long> { ["ActiveCount"] = report.ActiveCount };
                    foreach (InitiativeEntry entry in report.Entries)
                    {
                        values[$"{entry.Player}.Rank"] = entry.Rank;
                        values[$"{entry.Player}.Power"] = entry.Power;
                        values[$"{entry.Player}.Bonus"] = entry.Bonus;
                        values[$"{entry.Player}.Value"] = entry.Value;
                        values[$"{entry.Player}.Prev"] = entry.PreviousPosition ?? -1;
                        values[$"{entry.Player}.Next"] = report.NextOrder.IndexOf(entry.Player);
                    }

                    Add(_turn, completed, LogEventType.MajorRoundEnded, null, e.Detail, values: values);
                    break;
                case FlowEventKind.ProtectionLifted:
                case FlowEventKind.PlayerEliminated:
                case FlowEventKind.PlayerResigned:
                case FlowEventKind.MatchEnded:
                case FlowEventKind.FlagsLocked:
                    Add(_turn, e.MajorRound, e.Kind.ToString(), e.Player?.Value, e.Detail);
                    break;
                default:
                    break;
            }
        }
    }

    private void Add(
        int turn, int majorRound, string type, int? player, string detail,
        List<string>? coords = null, Dictionary<string, long>? values = null, string? failureKind = null) =>
        _events.Add(new LogEvent
        {
            Seq = ++_eventSeq,
            Turn = turn,
            MajorRound = majorRound,
            Type = type,
            Player = player,
            Detail = detail,
            Coords = coords,
            Values = values,
            FailureKind = failureKind,
        });

    // ---------- 收尾 ----------

    private LogHeader BuildHeader(EventRetention retention)
    {
        RelicGenerationRecord gen = Match.Relics.Generation;
        return new LogHeader
        {
            MapId = Match.Map.Id,
            Seed = Seed.ToString(),
            MaxMajorRounds = Match.MaxMajorRounds,
            DominanceStartRound = Match.DominanceStartRound,
            Config = Config,
            Players = [.. Match.Players.Select(p => p.Value)],
            Zones = [.. Match.Players.Select(p => Match.StateOf(p).BirthZone ?? -1)],
            FirstOrder = [.. _firstOrder],
            Relics = [.. gen.Placements.Select(r => new RelicEntry
            {
                Coord = r.Coord.ToNotation(),
                Zone = r.Spec.Zone.ToString(),
                Budget = r.Spec.Budget.ToString(),
                Type = r.Content.Type.ToString(),
                Magnitude = r.Content.Magnitude,
                EmblemPiece = r.Content.EmblemPiece?.ToString(),
            })],
            RelicsConverged = gen.Converged,
            RelicRerolls = gen.Rerolls,
            DebugAiPlayers = [.. Runner.Annotations.DebugAiPlayers.Select(p => p.Value)],
            Retention = retention,
        };
    }

    private List<TakeoverEntry> TakeoverEntries() =>
        [.. Runner.Annotations.Takeovers.Select(t => new TakeoverEntry
        {
            Sequence = t.Sequence,
            Player = t.Player.Value,
            MajorRound = t.MajorRound,
            Stage = t.Stage.ToString(),
            Kind = t.Kind.ToString(),
        })];

    private MatchLog Finish()
    {
        _finished = true;
        MatchPublicView view = _previous;
        // round-cap：会话只在规则层终局时收尾（达上限也是规则级 EndReason.MajorRoundLimit），名次直接取规则层结果。
        MatchResult result = Match.Result ?? throw new SimAssertionException("会话收尾时对局尚未终局。");
        ImmutableArray<Standing> standings = result.Standings;

        MultiplierPeak? peak = Match.Scoreboard.Peak;
        var logResult = new LogResult
        {
            Reason = result.Reason.ToString(),
            MajorRound = result.MajorRound,
            TurnCount = _turn,
            Standings = [.. standings.Select(s => new StandingEntry
            {
                Rank = s.Rank,
                Player = s.Player.Value,
                Group = s.Group.ToString(),
                Status = s.Input.Status.ToString(),
                Power = s.Input.Power,
                ControlledRelics = s.Input.ControlledRelics,
                ExclusiveCells = s.Input.ExclusiveCells,
                Stones = s.Input.Stones,
                EliminationOrder = s.Input.EliminationOrder,
            })],
            Winners = [.. standings.Where(s => s.Rank == 1).Select(s => s.Player.Value)],
            UsedDebugAi = Runner.Annotations.UsedDebugAi,
            DebugAiPlayers = [.. Runner.Annotations.DebugAiPlayers.Select(p => p.Value)],
            Takeovers = TakeoverEntries(),
            RelicReveals = [.. view.Relics.Select(r => new RelicRevealEntry { Coord = r.Coord.ToNotation(), RevealedInMajorRound = r.RevealedInMajorRound })],
            Peak = peak is null ? null : new PeakEntry
            {
                MultiplierCount = peak.MultiplierCount,
                EffectiveMultiplierCount = peak.EffectiveMultiplierCount,
                MajorRound = peak.MajorRound,
                Player = peak.Player.Value,
                Power = peak.Power,
                Stones = [.. peak.Stones.Select(s => s.ToNotation())],
            },
            PeakDestroyed = peak is null ? null : PeakDestroyed(peak),
            DeployLimitPeak = Match.Relics.DeployLimitPeak?.DeployLimit,
            TotalMs = _total.ElapsedMilliseconds,
            MajorRoundMs = [.. _majorRoundMs],
        };

        return new MatchLog
        {
            Header = BuildHeader(_retention),
            Turns = _turns,
            Events = _retention == EventRetention.Full ? _events : [.. _events.Where(e => !LogEventType.IsFineGrained(e.Type))],
            Result = logResult,
        };
    }

    /// <summary>裁决 6：峰值串是否在之后的某个快照里不再完整属于原主（被摧毁）。逐快照比对，不进 scoreboard。</summary>
    private bool PeakDestroyed(MultiplierPeak peak)
    {
        var stones = new HashSet<string>(peak.Stones.Select(s => s.ToNotation()));
        bool formed = false;
        foreach (TurnSnapshot turn in _turns)
        {
            PlayerEntry owner = turn.PlayersState.First(p => p.Player == peak.Player.Value);
            bool intact = owner.Groups.Any(g => stones.IsSubsetOf(g.Stones));
            if (!formed)
            {
                formed = intact && owner.Groups.Any(g => g.MultiplierCount == peak.MultiplierCount && stones.IsSubsetOf(g.Stones));
            }
            else if (!intact)
            {
                return true;
            }
        }

        return false;
    }

    private MatchLog Fail(Exception ex)
    {
        _finished = true;
        try
        {
            CopyFlowEvents();
        }
        catch (Exception inner) when (inner is not OutOfMemoryException)
        {
            Add(_turn, Match.MajorRound, "FlowEventCopyFailed", null, inner.Message);
        }

        return new MatchLog
        {
            Header = BuildHeader(EventRetention.Full),
            Turns = _turns,
            Events = _events,
            Failure = new LogFailure
            {
                Turn = _turn,
                MajorRound = Match.MajorRound,
                ExceptionType = ex.GetType().FullName ?? ex.GetType().Name,
                Message = ex.Message,
                StackTrace = ex.StackTrace,
                UsedDebugAi = Runner.Annotations.UsedDebugAi,
                Takeovers = TakeoverEntries(),
                ElapsedMs = _total.ElapsedMilliseconds,
            },
        };
    }
}
