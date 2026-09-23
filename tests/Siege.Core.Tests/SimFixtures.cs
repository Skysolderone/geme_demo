using System.Numerics;
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
/// 跑局 / 日志 / 分析测试的公共夹具。真实对局只用小样本（Easy、少量种子）验证机制；
/// 200 / 2000 局规模只走 CLI（<c>Siege.Sim run</c>），不进单元测试。
/// </summary>
internal static class SimFixtures
{
    /// <summary>基准图 4 人 Easy 配置。</summary>
    internal static RunConfig Config(
        int count = 1, ulong seedStart = 1, int? turnLimit = null, AiDifficulty difficulty = AiDifficulty.Easy,
        EventRetention retention = EventRetention.Full, int players = 4, int? injectFailureAtTurn = null, bool compress = false) =>
        new()
        {
            Players = [.. Enumerable.Range(0, players).Select(_ => new PlayerAiConfig { Difficulty = difficulty })],
            SeedStart = seedStart,
            Count = count,
            // 规则层已无大回合上限：小样本改由跑局层的小回合数截断（D5）收住，缺省 4 × 人数 = 此前 4 个大回合的样本长度。
            TurnLimit = turnLimit ?? 4 * players,
            EventRetention = retention,
            FullEventSamplePermille = 0,
            InjectFailureAtTurn = injectFailureAtTurn,
            Compress = compress,
        };

    /// <summary>
    /// 全部测试共用的真实样本：Easy 4 局、4 大回合、完整事件流（只算一次）。
    /// <b>Easy 难度从不落匠人</b>（征募前瞻分只看基础军势，匠人 1 分在平手里排枚举末位；实测 4 局 × 12 大回合 2718 条棋串含匠人 0 条），
    /// 需要匠人上盘的遥测守门要自己起 <see cref="Siege.Core.Ai.AiDifficulty.Standard"/> 的小样本——见 <c>各棋子势力占比Tests.真实跑局快照的类型计数与明细自洽</c>。
    /// </summary>
    internal static readonly Lazy<List<MatchLog>> Sample = new(() => BatchRunner.Execute(Config(count: 4, seedStart: 11), parallelism: 1));

    /// <summary>
    /// 有名次的真实样本（restore-go-core-rules 段 E）：规则层删掉大回合上限与碾压之后，<see cref="Sample"/> 的 4 局全是 <c>turn_limit</c> 截断局、胜者全空，
    /// 读胜者 / 名次的报告测试在它上面是空证。这里同样是 Easy 4 人、种子 11–14 的真实对局，跑满 3 个大回合（第 3 大回合结束事件已写入）后，
    /// 让除"幸存者"外的三人弃赛 → 以规则原因「只剩一名参赛玩家」终局，名次取自规则层。幸存者按局轮换（第 i 局为 P<i>i</i>），
    /// 于是"第 3 大回合领先者是否获胜"在四局里不是恒真也不是恒假。弃赛不算污染（<see cref="MatchLog.IsContaminated"/> 只看调试 AI 与人工接管）。
    /// </summary>
    internal static readonly Lazy<List<MatchLog>> RankedSample = new(() => [.. Enumerable.Range(0, 4).Select(i => RankedMatch(11UL + (ulong)i, survivor: i))]);

    /// <summary>一局有名次的真实对局：跑满 3 个大回合后，除 <paramref name="survivor"/> 外全部弃赛。</summary>
    internal static MatchLog RankedMatch(ulong seed, int survivor)
    {
        // 走法依赖 AI 权重：写死当前取值（testing.md「依赖 AI 实际怎么走的断言要把权重写死」），调默认权重不会让领先者胜数的样本口径断言翻掉。
        var weights = new EvaluationWeights(PowerGain: 10, EnemyLoss: 8, Relic: 6, Safety: 35, Growth: 4, Initiative: 20, Supply: 2, Eye: 0, Threat: 0);
        RunConfig config = Config(turnLimit: 0) with { Players = [.. Enumerable.Range(0, 4).Select(_ => new PlayerAiConfig { Difficulty = AiDifficulty.Easy, Weights = weights })] };
        MatchSession session = MatchSession.Create(config, seed);
        while (session.Match.MajorRound <= 3 && session.RunTurn())
        {
        }

        foreach (PlayerId p in session.Match.Players.Where(p => p.Value != survivor))
        {
            session.Match.Resign(p);
        }

        return session.Run();
    }

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
        ulong seed, IEnumerable<TurnSnapshot> turns, IEnumerable<LogEvent> events, LogResult result, int players = 4, int[]? zones = null, bool relicsConverged = true, RelicEntry[]? relics = null,
        int? playableCells = null, int? artisanWeight = null, int? zoneCount = null) =>
        new()
        {
            Header = new LogHeader
            {
                MapId = "synthetic",
                Seed = new Siege.Core.Determinism.GameSeed(seed).ToString(),
                PlayableCells = playableCells,
                ZoneCount = zoneCount,
                ArtisanWeight = artisanWeight,
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
        int showCount = 5, int freePick = 3, int typeSlots = 5, GroupEntry[]? groupsOfPlayer = null, string[]? captures = null,
        TerrainEditEntry[]? edits = null, bool legacyNoEdits = false, int[]? territory = null, LifeTurnEntry? life = null, bool legacyNoLife = false) =>
        new()
        {
            Turn = turn,
            MajorRound = majorRound,
            Player = player,
            Position = 0,
            Passed = placements is null || placements.Length == 0,
            Placements = [.. placements ?? []],
            Captures = [.. captures ?? []],

            // 改造字段：新日志一律写出（没有改造就是空表 []）；<paramref name="legacyNoEdits"/> 造的是
            // artisan-terrain-edit 之前的**旧日志**（字段缺失 → null），分析时整局排除并计数（R-6），MUST NOT 回填成空表。
            Edits = legacyNoEdits ? null : [.. edits ?? []],

            // 活形字段：新日志一律写出（缺省为无变化、无拒绝的空记录）；legacyNoLife 造的是 life-shape 之前的旧日志（null），活形分析整局排除并计数。
            Life = legacyNoLife ? null : life ?? new LifeTurnEntry(),
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

                // 领地分：缺省 null = restore-go-core-rules 段 E 之前的旧日志（领地分占比整局排除并计数），MUST NOT 回填。
                TerritoryScore = territory?[i],
                Groups = i == player && groupsOfPlayer is not null ? [.. groupsOfPlayer] : [],
            })],
        };

    internal static LogResult ResultOf(
        int majorRound, int[] winners, bool converged = true, int players = 4, PeakEntry? peak = null, bool? peakDestroyed = null, int[]? ranks = null,
        string? reason = null) =>
        new()
        {
            // 未收敛样本用旧日志的「达大回合上限」原因名（规则层已删除该原因，只剩旧日志会出现；它有规则名次，仍纳入胜率类指标）。
            // 跑局层截断见 TruncatedResult：那种局没有名次与胜者。
            Reason = reason ?? (converged ? nameof(EndReason.AllPassed) : LogResult.LegacyMajorRoundLimit),
            MajorRound = majorRound,
            TurnCount = majorRound * players,
            Standings = [.. Enumerable.Range(0, players).Select(p => new StandingEntry
            {
                Rank = ranks is not null ? ranks[p] : winners.Contains(p) ? 1 : 2,
                Player = p,
                Group = nameof(StandingGroup.Finisher),
                Status = "Active",
            })],
            Winners = [.. winners],
            Peak = peak,
            PeakDestroyed = peakDestroyed,
        };

    /// <summary>
    /// 跑局层截断的结果行（restore-go-core-rules D5）：结束原因 <c>turn_limit</c>、名次与胜者为空——与 <c>MatchSession.Finish</c> 写出的形状一致。
    /// 这是胜率 / 名次类口径测试里"被排除的样本"（testing.md「统计口径测试必须放一个被排除的样本」）。
    /// </summary>
    internal static LogResult TruncatedResult(int majorRound, int players = 4) =>
        new()
        {
            Reason = LogResult.TurnLimitReason,
            MajorRound = majorRound,
            TurnCount = majorRound * players,
        };

    /// <summary>大回合结束事件：各玩家的名次与下一轮位置。</summary>
    internal static LogEvent RoundEnded(int completedRound, int turn, params (int Player, int Rank, int Next)[] entries)
    {
        var values = new Dictionary<string, BigInteger> { ["ActiveCount"] = entries.Length };
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
            Values = new Dictionary<string, BigInteger> { ["Recruited"] = picks.Split(',', StringSplitOptions.RemoveEmptyEntries).Length, ["Revoked"] = 0, ["Deployed"] = 1 },
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
