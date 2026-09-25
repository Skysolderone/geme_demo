using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Core.Relics;
using Siege.Core.Scoring;
using Siege.Sim.Config;
using Siege.Sim.Logging;

namespace Siege.Sim.Running;

/// <summary>
/// 一局的跑局会话：建局、装 AI、逐小回合驱动 <see cref="MatchRunner"/>、记日志、到终局或小回合数截断（restore-go-core-rules D5，记 turn_limit、无名次）、捕获失败。
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
    private int _editCursor;
    private bool _finished;
    private bool _truncated;

    private MatchSession(MatchFlow match, RunConfig config, bool recorded = false)
    {
        Match = match ?? throw new ArgumentNullException(nameof(match));
        Config = (config ?? throw new ArgumentNullException(nameof(config))).Validated();
        // 回放（recorded）：首部配置里没有候选格上限 = 这局当时就是不限制（小图省略，或该项出现之前的旧日志），MUST NOT 再按地图自动取。
        CellLimit = Config.CandidateCellLimit ?? (recorded ? 0 : AiSearchConfig.DefaultCellLimitFor(match.Map.PlayableCount));
        // 停手阈值同理：首部没有该项 = 该项出现之前的旧日志，当时的保留条件是严格提高（= 0），MUST NOT 按今天的缺省阈值重建。
        PassThreshold = Config.PassThreshold ?? (recorded ? 0 : AiSearchConfig.DefaultPassThreshold);
        if (match.Phase != MatchPhase.InProgress)
        {
            throw new SiegeRuleException("会话要求对局已完成插旗并处于进行中。");
        }

        if (match.Players.Length != config.Players.Count)
        {
            throw new SiegeRuleException($"配置了 {config.Players.Count} 名玩家，对局却有 {match.Players.Length} 名。");
        }

        if (match.ArtisanWeight != config.ArtisanWeight)
        {
            throw new SiegeRuleException($"跑局配置的匠人权重为 {config.ArtisanWeight}，对局配置却为 {match.ArtisanWeight}。");
        }

        if (config.ContentSet is { } contentSet && contentSet != match.ContentSet)
        {
            throw new SiegeRuleException($"跑局配置的内容集为 {contentSet}，对局配置却为 {match.ContentSet}。");
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

    /// <summary>本局未显式配置剪枝参数的 AI 实际生效的候选格上限 K（0 = 不限制）。</summary>
    public int CellLimit { get; }

    /// <summary>本局未显式配置剪枝参数的 AI 实际生效的停手阈值（ai-eye D4）。</summary>
    public int PassThreshold { get; }

    public GameSeed Seed { get; }

    /// <summary>已完成的小回合数。</summary>
    public int TurnCount => _turn;

    // ---------- 建局 ----------

    /// <summary>按配置与种子建一局：地图静态校验、信物按 <c>relic-gen</c> 生成、原型插旗（标准图上 P<i>i</i> 插旗到出生区 <i>i</i>）。</summary>
    public static MatchSession Create(RunConfig config, ulong seed, MapData? map = null) => Create(config, seed, map, recorded: false);

    /// <summary>
    /// <paramref name="recorded"/> 为 <c>true</c> 即按日志首部的配置原样重建（回放）：不把候选格上限按地图落成缺省值，
    /// 首部没有该项就是不限制——否则该项出现之前的大图旧日志会被按新缺省 K 重跑而中途分歧，重建出的首部也会多出一项。
    /// 停手阈值同理：不落成缺省值，首部没有该项按 0（严格提高）重建。冒险概率同理：首部没有该项按 0（不冒险）重建。
    /// </summary>
    internal static MatchSession Create(RunConfig config, ulong seed, MapData? map, bool recorded)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validated();
        if (map is null && config.MapPerMatch)
        {
            // 每局换图的配置里 MapId 只是起始标识；本局是第几局只有调用方知道（BatchRunner 按序号、Replayer 按日志首部的地图标识）。
            throw new ArgumentException("每局换图的配置不能由会话自行解析地图：请由调用方按本局的地图标识解析后传入。");
        }

        map ??= MapCatalog.Resolve(config.MapId);
        if (!recorded)
        {
            config = config.ResolvedFor(map);
        }

        PlayerId[] players = config.PlayerIds();
        // 冒险概率（flag-contest D2）：新建的局已由 ResolvedFor 落成具体值；按首部重建时缺该项 = 该项出现之前的旧日志，当时没有冒险，按 0 重建。
        int flagRisk = config.FlagRisk ?? (recorded ? 0 : MatchOptions.DefaultFlagRisk);
        // 内容集（more-pieces-relics D8）同理：新建的局已落成具体值；按首部重建时缺该项 = 该项出现之前的旧日志，当时只有原六 + 六，按 v1 重建。
        ContentSet contentSet = config.ContentSet ?? (recorded ? ContentSets.Legacy : ContentSets.Default);
        MatchFlow match = MatchFlow.Create(
            map, new GameSeed(seed), players,
            MatchOptions.Immediate with { ArtisanWeight = config.ArtisanWeight, FlagRisk = flagRisk, ContentSet = contentSet });
        // 选区的唯一实现在 Core（frontier-map D4 / flag-contest D1）：此前已有旗时以冒险概率加入已有人的区；否则区数不多于人数上限时顺排
        // （p = 0 时 P<i> → 区 <i>，与此前逐项相同），多于时由种子的独立子流均匀选区。
        match.PlantPrototype();
        return new MatchSession(match, config, recorded);
    }

    /// <summary>测试接缝：对已插旗的对局（可以是未校验的合成地图）建会话。</summary>
    internal static MatchSession ForMatch(MatchFlow match, RunConfig config) => new(match, config);

    private void AttachConfigured(PlayerId player, PlayerAiConfig ai)
    {
        // 显式的剪枝参数原样生效；未配置时按难度取，候选格上限取跑局配置的值，仍未给出则按地图大小取（阈值逻辑在 Core，三个入口共用）。
        // 停手阈值同样只作用于未显式配置剪枝参数的玩家（显式的 Search 自带阈值）。
        AiSearchConfig search = ai.Search ?? (AiSearchConfig.ForMap(ai.Difficulty, Match.Map.PlayableCount, CellLimit) with { PassThreshold = PassThreshold });
        if (ai.DebugAi)
        {
            DebugTurnController.Create(Runner, player, debugMode: true, ai.Difficulty, ai.Weights, search);
            Runner.SetController(player, new LoggingController(Runner.ControllerOf(player), _trace));
        }
        else
        {
            SetController(player, HeuristicAi.Create(Match, player, ai.Difficulty, ai.Weights, search));
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

    /// <summary>跑到终局或小回合数截断；任何异常（含小回合硬停）都被捕获为失败局记录。返回完整日志。</summary>
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

        // D5 小回合数截断：跑满即停，不是终局原因、不产生名次；Finish 以 turn_limit 收尾。只在跑局驱动循环里，Core 不知道它。
        if (Config.TurnLimit > 0 && _turn >= Config.TurnLimit)
        {
            _truncated = true;
            return false;
        }

        // 防死锁硬停：不是终局原因，以异常落 failed 日志。截断为 0（仅供调试）时这是唯一兜底。
        if (_turn >= Config.MaxTurns)
        {
            throw new SimAssertionException($"对局超过 {Config.MaxTurns} 个小回合仍未结束：疑似死锁。");
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
        var placedCells = new List<Coord>();
        var captures = new List<string>();
        foreach (Coord c in after.Board.AllCoords())
        {
            Occupant? was = before.Board[c].Occupant;
            Occupant? now = after.Board[c].Occupant;
            if (was is null && now is { } placed)
            {
                placements.Add(new Placement(c, placed.Type).ToString());
                placedCells.Add(c);
            }
            else if (was is { } taken && now is null)
            {
                captures.Add(new CapturedStone(c, taken.Owner, taken.Type).ToString());
            }
        }

        // 本小回合完成的地形改造：读 Core 的留痕（含"是否直接导致提子"），MUST NOT 由日志层比对前后地形自己推。
        var edits = new List<TerrainEditEntry>();
        for (; _editCursor < Match.TerrainEdits.Count; _editCursor++)
        {
            TerrainEditRecord e = Match.TerrainEdits[_editCursor];
            edits.Add(new TerrainEditEntry
            {
                Action = e.Edit.Kind.ToString(),
                Target = e.Edit.ToString(),
                Player = e.Player.Value,
                Artisan = e.ArtisanCoord.ToNotation(),
                CausedCapture = e.CausedCapture,
            });
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
                values: new Dictionary<string, BigInteger>
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
                values: new Dictionary<string, BigInteger> { ["Attempt"] = ++attempt });
        }

        // life-shape 4.1：暂放 / 确认环节因活棋禁入或破坏活形被拒的尝试（暂放被拒读 Core 留痕 StagedBatch.Refusals，不自己重判）。
        ImmutableArray<StageRefusal> refusals = _trace.Batch is { } staged ? [.. staged.Refusals] : [];
        foreach (StageRefusal refusal in refusals.Where(r => IsLifeFailure(r.Failure)))
        {
            AddLifeRefused(turn, majorRound, pid, "stage", refusal.Tried, refusal.Failure);
        }

        foreach ((ImmutableArray<Placement> tried, BatchFailure failure) in _trace.Rejections.Where(r => IsLifeFailure(r.Failure)))
        {
            AddLifeRefused(turn, majorRound, pid, "confirm", tried, failure);
        }

        HeuristicTurnController? ai = AiOf(player);
        if (!passed)
        {
            var values = new Dictionary<string, BigInteger>
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
            Edits = edits,
            Life = LifeEntry(player, before, after, placedCells, edits.Count > 0) with
            {
                ForbiddenStaged = refusals.Count(r => r.Failure.Kind == BatchFailureKind.LifeForbidden),
                ForbiddenRehearsed = _trace.IllegalRehearsals.Count(r => r.Failure.Kind == BatchFailureKind.LifeForbidden),
                ForbiddenRejected = _trace.Rejections.Count(r => r.Failure.Kind == BatchFailureKind.LifeForbidden),
                BreaksRehearsed = _trace.IllegalRehearsals.Count(r => r.Failure.Kind == BatchFailureKind.BreaksLife),
                BreaksRejected = _trace.Rejections.Count(r => r.Failure.Kind == BatchFailureKind.BreaksLife),
            },
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

    private static bool IsLifeFailure(BatchFailure failure) =>
        failure.Kind is BatchFailureKind.LifeForbidden or BatchFailureKind.BreaksLife;

    private void AddLifeRefused(int turn, int majorRound, int? player, string stage, ImmutableArray<Placement> tried, BatchFailure failure) =>
        Add(turn, majorRound, LogEventType.LifeRefused, player, $"{stage} [{string.Join(",", tried)}] {failure.Message}",
            coords: [.. failure.Coords.Select(c => c.ToNotation())], failureKind: failure.Kind.ToString(),
            values: failure.LifeOwner is { } owner ? new Dictionary<string, BigInteger> { ["Owner"] = owner.Value } : null);

    /// <summary>"贴地形的小空区"的格数上限（R8 统计口径，只用于遥测分类）。</summary>
    internal const int SmallEyeSpaceMax = 3;

    /// <summary>
    /// 本小回合的活形记录（life-shape 4.1）：比对结算前后两份公开视图里的活形分析，按<b>棋子归属</b>而不是棋串相等来判——
    /// 己方加子合并两条活串时，旧串的子仍在同主的活串里，不算失去；新串含旧活串的子，不算新确立。
    /// 两类拒绝计数由调用方填。internal 供测试用真实盘面走写入路径。
    /// </summary>
    internal static LifeTurnEntry LifeEntry(PlayerId actor, MatchPublicView before, MatchPublicView after, IReadOnlyCollection<Coord> placed, bool edited)
    {
        LifeShapeReport was = before.LifeShape;
        LifeShapeReport now = after.LifeShape;
        var changes = new List<LifeChangeEntry>();

        foreach (GroupLife g in now.Groups.Where(g => g.Life == LifeState.Alive).OrderBy(g => g.Group.Stones.Min()))
        {
            if (!g.Group.Stones.Any(s => StillAlive(was, s, g.Group.Owner)))
            {
                changes.Add(Change(LifeChangeEntry.Established, g, actor, cause: null));
            }
        }

        foreach (GroupLife g in was.Groups.Where(g => g.Life == LifeState.Alive).OrderBy(g => g.Group.Stones.Min()))
        {
            if (g.Group.Stones.All(s => StillAlive(now, s, g.Group.Owner)))
            {
                continue;
            }

            string cause = g.Group.Owner != actor ? LifeChangeEntry.NonOwner
                : placed.Any(c => g.EyeSpaces.Any(e => e.Cells.Contains(c))) ? LifeChangeEntry.OwnerFill
                : edited ? LifeChangeEntry.OwnerEdit
                : LifeChangeEntry.OwnerOther;
            changes.Add(Change(LifeChangeEntry.Lost, g, actor, cause));
        }

        GameBoard board = after.Board;
        return new LifeTurnEntry
        {
            Changes = changes,
            Players = [.. after.Players.Select(p => p.Player).OrderBy(p => p.Value).Select(p =>
            {
                GroupLife[] alive = [.. now.Groups.Where(g => g.Life == LifeState.Alive && g.Group.Owner == p)];
                EyeSpace[] eyes = [.. now.EyeSpaces.Where(e => e.Owner == p && now.IsProtected(e))];
                return new LifePlayerEntry
                {
                    Player = p.Value,
                    AliveGroups = alive.Length,
                    SingleStoneAlive = alive.Count(g => g.Group.Size == 1),
                    EyeSpaces = eyes.Length,
                    EyeCells = eyes.Sum(e => e.Cells.Length),
                    TerrainSmallEyeSpaces = eyes.Count(e => IsTerrainSmall(board, e)),
                };
            })],
            ProtectedCells = now.EyeSpaces.Where(now.IsProtected).Sum(e => e.Cells.Length),
            PlayableCells = board.AllCoords().Count(c => board[c].Terrain == Terrain.Playable),
        };

        static bool StillAlive(LifeShapeReport report, Coord stone, PlayerId owner) =>
            report.GroupLifeAt(stone) is { Life: LifeState.Alive } l && l.Group.Owner == owner;

        static LifeChangeEntry Change(string kind, GroupLife g, PlayerId actor, string? cause)
        {
            string[] stones = [.. g.Group.Stones.Order().Select(s => s.ToNotation())];
            return new LifeChangeEntry
            {
                Kind = kind,
                Owner = g.Group.Owner.Value,
                Actor = actor.Value,
                At = stones[0],
                Stones = [.. stones],
                EyeSpaces = [.. g.EyeSpaces.Select(e => $"{string.Join(",", e.Cells.Order().Select(c => c.ToNotation()))}={e.EyeValue}")],
                Cause = cause,
            };
        }
    }

    /// <summary>
    /// R8 统计口径：眼空间不超过 <see cref="SmallEyeSpaceMax"/> 格，且至少一格在盘内某个几何方向上没有气边（岩石 / 深水 / 崖壁 / 栅栏）。
    /// 几何邻居与气边邻居的差集只用于这条遥测分类（与表现层差集原因同类），不是规则判断；棋盘外沿不在几何邻居里，不算。
    /// </summary>
    private static bool IsTerrainSmall(GameBoard board, EyeSpace space) =>
        space.Cells.Length <= SmallEyeSpaceMax
        && space.Cells.Any(c => board.Neighbors(c).Length > board.LibertyNeighbors(c).Length);

    private static string RankingText(PowerSnapshot? power) =>
        power is null ? string.Empty : string.Join(",", power.Ranking.Select(g => $"{g.Rank}:{string.Join("=", g.Players)}"));

    /// <summary>快照里的逐玩家势力明细（日志写入函数）。internal 供测试用真实盘面走写入路径（大数、领地分变化在真实跑局里未必出现）。</summary>
    internal static List<PlayerEntry> PlayerEntries(MatchPublicView view)
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
                HasEstablishedPower = state.HasEstablishedPower,
                Total = power?.Total ?? 0,
                TerritoryScore = power?.TerritoryScore ?? 0,
                Rank = view.Power?.RankOf(state.Player),
                HandTypes = hand is null ? [] : [.. hand.Types.Select(t => t.ToString())],
                Groups = power is null ? [] : [.. power.Groups.Select(g => new GroupEntry
                {
                    Stones = [.. g.Stones.Select(s => s.ToNotation())],
                    Base = g.BaseTotal,
                    LineBonus = g.LineBonus,
                    SynergyBonus = g.SynergyBonus,
                    HighGroundBonus = g.HighGroundBonus,
                    MultiplierCount = g.MultiplierCount,
                    Power = g.Power,
                    PieceCounts = PieceCountsOf(view.Board, g, view.ContentSet),
                })],
            });
        }

        return list;
    }

    /// <summary>
    /// 棋串各棋子类型的数量（本局内容集的全部类型都写，含 0，按固定类型次序），从快照盘面按棋子坐标逐枚统计。internal 供测试用真实盘面走写入路径（真实跑局样本未必出现连珠成线）。
    /// 键集合取自内容集（more-pieces-relics D8，<see cref="ContentSets.PieceTypesOf"/>）而不是整个枚举：v1 局的日志与引入新棋子之前逐字节相同，v2 局写十种。
    /// </summary>
    internal static Dictionary<string, int> PieceCountsOf(GameBoard board, GroupPower group, ContentSet contentSet)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (PieceType type in ContentSets.PieceTypesOf(contentSet))
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
                    var values = new Dictionary<string, BigInteger> { ["ActiveCount"] = report.ActiveCount };
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
        List<string>? coords = null, Dictionary<string, BigInteger>? values = null, string? failureKind = null) =>
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
            // 口径：取**开局地图**的可落子格（BaseMap），不是终局地形。地形自 artisan-terrain-edit 起可变（搭桥会加可落子格），
            // 而首部是一局一条、在终局时才写出——写终局值等于把"未来的分母"塞进第 9 项占用率。
            // 代价：本局架出来的桥不进分母，占用率会略微偏高（实测每局新增桥个位数，对 105 的基数 < 1 个百分点）。
            PlayableCells = Match.Board.BaseMap.PlayableCount,
            ZoneCount = Match.Map.BirthZones.Length,
            // 摘要与平台边长同样取开局地图：首部在终局时才写出，活地形（本局架的桥、烧掉的林）不属于"这是哪张图"。
            MapDigest = MapFile.Digest(Match.Board.BaseMap),
            ZoneSides = Config.MapPerMatch ? [.. Match.Board.BaseMap.BirthZones.Select(SideOf)] : null,
            ArtisanWeight = Match.ArtisanWeight,
            AiEvaluationVersion = EvaluationBreakdown.Version,
            Seed = Seed.ToString(),
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

    /// <summary>出生区的边长：格子外接矩形的较长边（生成图与手工边疆图的平台都是整块正方形，平台内无障碍）。</summary>
    private static int SideOf(IEnumerable<Coord> zone)
    {
        Coord[] cells = [.. zone];
        return Math.Max(cells.Max(c => c.X) - cells.Min(c => c.X), cells.Max(c => c.Y) - cells.Min(c => c.Y)) + 1;
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
        // 会话在规则层终局时收尾，名次直接取规则层结果；被截断（D5）的局没有规则层结果，记 turn_limit、名次为空。
        MatchResult? result = Match.Result;
        if (result is null && !_truncated)
        {
            throw new SimAssertionException("会话收尾时对局尚未终局。");
        }

        ImmutableArray<Standing> standings = result?.Standings ?? [];

        MultiplierPeak? peak = Match.Scoreboard.Peak;
        var logResult = new LogResult
        {
            Reason = result?.Reason.ToString() ?? LogResult.TurnLimitReason,
            // 截断局：记最后一个实际进行的小回合所在的大回合（与 MajorRoundMs 的长度一致），不是推进后的下一轮序号。
            MajorRound = result?.MajorRound ?? (_turns.Count > 0 ? _turns[^1].MajorRound : Match.MajorRound),
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
