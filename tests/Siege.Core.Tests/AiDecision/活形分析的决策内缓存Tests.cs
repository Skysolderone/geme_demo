using System.Collections.Immutable;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;
using Siege.Core.Tests.CaptureResolution;

namespace Siege.Core.Tests.AiDecision;

/// <summary>
/// 规格：ai-decision —— Requirement: 活形分析的决策内缓存（ai-eye D5、段 C 3.2）。
/// 缓存挂在 <see cref="BatchEvaluator"/> 上（每次决策新建一个），键是盘面指纹（逐格占用者 + 对局中完成的地形改造，不含棋子类型）。
/// 计数桩与开关经 internal 接缝（<c>HeuristicAi.Create(…, lifeQuery, cacheLife)</c>）换上，不进任何配置与日志。
/// </summary>
public class 活形分析的决策内缓存Tests
{
    private static readonly PlayerId Me = AiFixtures.P0;

    /// <summary>测试内独立的盘面键：逐格占用者 + 改造集合（规范序）。与实现的指纹同口径，但不调用它。</summary>
    private static string Key(GameBoard board) =>
        string.Join(",", board.AllCoords().Select(c => board[c].Occupant is { } o ? o.Owner.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "."))
        + "|" + string.Join(",", board.TerrainEdits.Select(e => e.ToString()).Order(StringComparer.Ordinal));

    /// <summary>第 5 大回合、部署上限 8、60+ 合法空格的开放局面（同「候选格上限」）。</summary>
    private static MatchFlow OpenPosition()
    {
        MatchFlow match = AiFixtures.Round5().Stones(AiFixtures.P1, "E5", "F6", "D7").Stones(AiFixtures.P2, "G3");
        match.SetDeployLimit(8);
        return match;
    }

    /// <summary>
    /// 开放局面 + P0 左下角活形（两个单格眼 A1、C1，同「活形硬约束 · 不拆自己的活形」）+ 三种持有类型：
    /// 同格不同类型（指纹不含类型）、硬约束判据与评价对同一候选、各扰动次序反复到达的同一前缀批次，三种重复都会出现。
    /// </summary>
    private static MatchFlow RichPosition()
    {
        MatchFlow match = OpenPosition().Stones(Me, "B1", "A2", "B2", "C2", "D1", "D2");
        match.Debug.SeedHand(Me, [(PieceType.Basic, 20), (PieceType.Fortress, 5), (PieceType.Line, 5)]);
        return match;
    }

    private static (HeuristicTurnController Ai, List<string> Keys) DeployCounting(MatchFlow match, bool cacheLife)
    {
        var keys = new List<string>();
        HeuristicTurnController ai = HeuristicAi.Create(
            match,
            Me,
            AiDifficulty.Standard,
            weights: null,
            AiSearchConfig.Standard,
            lifeQuery: board =>
            {
                keys.Add(Key(board));
                return LifeShapeReport.Analyze(board);
            },
            cacheLife);
        StagedBatch batch = match.OpenDeploy();
        ai.Deploy(batch, match.Rehearse);
        return (ai, keys);
    }

    [Fact]
    public void 同一决策内重复盘面只分析一次()
    {
        // 关缓存：每次请求都落到桩上，记下全部被请求的盘面。开缓存：每个不同的盘面恰好分析一次——同格不同类型（指纹不含类型）、
        // 硬约束判据与评价对同一候选、单点排序与贪心组批的各扰动次序反复到达的同一盘面，都只算一次。
        // 变异 M-C2（缓存形同虚设：每次查找前清空）→ 红 1（本测试）。变异 M-C5（指纹带上棋子类型）→ 红 1（本测试：同格不同类型不再共用，结果不变所以别处不红）。
        (HeuristicTurnController off, List<string> offKeys) = DeployCounting(RichPosition(), cacheLife: false);
        (HeuristicTurnController on, List<string> onKeys) = DeployCounting(RichPosition(), cacheLife: true);

        int distinct = offKeys.Distinct(StringComparer.Ordinal).Count();
        Assert.True(offKeys.Count > 2 * distinct, $"关缓存 {offKeys.Count} 次请求、{distinct} 个不同盘面——样本里重复太少，钉不住缓存");
        Assert.Equal(distinct, onKeys.Count);
        Assert.Equal(onKeys.Count, onKeys.Distinct(StringComparer.Ordinal).Count());

        // 结果不变：同一局面开 / 关缓存的排行、候选与选择逐项相同。
        Assert.Equal(off.Decisions, on.Decisions);
        Assert.Equal(
            off.LastPointRanking.Select(p => $"{p.Coord.ToNotation()}:{p.Type}:{p.Edit}:{p.Evaluation}"),
            on.LastPointRanking.Select(p => $"{p.Coord.ToNotation()}:{p.Type}:{p.Edit}:{p.Evaluation}"));
        Assert.Equal(off.LastCandidates.Select(c => $"{c.Key}={c.Evaluation}"), on.LastCandidates.Select(c => $"{c.Key}={c.Evaluation}"));
    }

    [Fact]
    public void 缓存不跨决策()
    {
        // 同一盘面上连续两次决策：每次决策的评价器都要自己分析批次开始前的盘面，不得命中上一次决策留下的结果。
        // 跨决策复用在同一局内只是省了计算，但指纹不含底图——跨对局两张同尺寸的图可有相同指纹，复用即拿到另一张图的活形。
        // 变异 M-C3（缓存改为静态字段，跨决策、跨对局共享）→ 红 4：本测试，以及并行的其他测试拿到别的图 / 别的对局的活形分析而走法改变
        //（候选格上限黄金哈希、首部缺停手阈值的旧日志按0回放、未显式配置权重的跑局用默认权重表）——这正是不得跨决策的理由。
        // 候选批次数取 1：只走排序本身的次序、不消费扰动子流，两次决策到达的盘面完全相同，分析次数可以逐次比较。
        MatchFlow match = OpenPosition();
        var keys = new List<string>();
        HeuristicTurnController ai = HeuristicAi.Create(
            match,
            Me,
            AiDifficulty.Standard,
            weights: null,
            AiSearchConfig.Standard with { CandidateBatchCount = 1 },
            lifeQuery: board =>
            {
                keys.Add(Key(board));
                return LifeShapeReport.Analyze(board);
            },
            cacheLife: true);
        string before = Key(match.Board);

        ai.CreateEvaluator();
        ai.CreateEvaluator();
        Assert.Equal([before, before], keys);

        // 两次完整决策（盘面未变）：第二次仍从批次前盘面分析起，且两次各自的分析次数相同（第二次没有任何命中上一次的缓存）。
        keys.Clear();
        StagedBatch batch = match.OpenDeploy();
        ai.Deploy(batch, match.Rehearse);
        List<string> first = [.. keys];
        string firstChoice = ai.Decisions[^1];
        keys.Clear();
        ai.Deploy(batch, match.Rehearse);   // Deploy 先清空批次，从同一盘面重新决策
        Assert.True(first.Count > 1, $"第一次决策只分析了 {first.Count} 次");
        Assert.Equal(before, first[0]);
        Assert.Equal(first, keys);
        Assert.Equal(firstChoice, ai.Decisions[^1]);
    }

    [Fact]
    public void 地形改造不同的盘面不共用分析()
    {
        // 同一格落匠人，带不带改造：占用者网格完全相同，气边不同（同「活形硬约束 · 用改造拆自己的活形也被淘汰」的盘面：
        // 预置栅栏 A1–A2、J1–J2，P0 一排 B1…H1 两端各一个单格眼；匠人落 D2 不改造 → 活形不变；落 D2 并立栅 D1–E1 → 切成两条未定棋串）。
        // 先评价不改造的、再评价立栅的：指纹若不含地形，后者命中前者的分析，判不出失活。
        // 变异 M-C6（指纹去掉改造段）→ 红 5：本测试、活形硬约束 · 用改造拆自己的活形也被淘汰、缓存开关不改变决策序列、
        // 候选格上限黄金哈希、阈值为0时零变化（v5 实战里匠人改造常见，漏掉改造段即整局走法改变）。
        MatchFlow match = MatchFixtures
            .Started(TestMaps.Terrain(fences: [("A1", "A2"), ("J1", "J2")]))
            .AtRound(5, [Me, AiFixtures.P1, AiFixtures.P2, AiFixtures.P3])
            .Stones(Me, "B1", "C1", "D1", "E1", "F1", "G1", "H1");
        TerrainEdit cut = TerrainEdit.Fence(TestMaps.At("D1"), TestMaps.At("E1"));
        (StagedBatch batch, SettlementDriver driver) = 活形硬约束Tests.Staging(match, PieceType.Artisan, 1, "D2");
        RehearsalResult plain = 活形硬约束Tests.Rehearse(batch, driver, BatchFixtures.Artisan("D2", cut) with { Edit = null });
        RehearsalResult split = 活形硬约束Tests.Rehearse(batch, driver, BatchFixtures.Artisan("D2", cut));
        Assert.True(plain.IsLegal && split.IsLegal);
        Assert.Equal(Key(plain.ProjectedBoard!).Split('|')[0], Key(split.ProjectedBoard!).Split('|')[0]);   // 前提：占用者网格相同
        Assert.NotEqual(Key(plain.ProjectedBoard!), Key(split.ProjectedBoard!));

        HeuristicTurnController ai = HeuristicAi.Create(match, Me, AiDifficulty.Standard, weights: null, AiSearchConfig.Standard, lifeQuery: null, cacheLife: true);
        HeuristicTurnController reference = HeuristicAi.Create(match, Me, AiDifficulty.Standard, weights: null, AiSearchConfig.Standard, lifeQuery: null, cacheLife: false);
        BatchEvaluator cached = ai.CreateEvaluator();
        ImmutableArray<Placement> plainBatch = [BatchFixtures.Artisan("D2", cut) with { Edit = null }];
        ImmutableArray<Placement> splitBatch = [BatchFixtures.Artisan("D2", cut)];

        Assert.True(cached.TryEvaluate(plainBatch, plain, batch.Context, out EvaluationBreakdown? plainScore));
        Assert.False(cached.TryEvaluate(splitBatch, split, batch.Context, out _));
        Assert.Equal(reference.CreateEvaluator().Evaluate(plainBatch, plain, batch.Context), plainScore);
        Assert.Equal(reference.CreateEvaluator().Evaluate(splitBatch, split, batch.Context), cached.Evaluate(splitBatch, split, batch.Context));
        Assert.NotEqual(plainScore.RawOf(EvaluationDimension.Eye), cached.Evaluate(splitBatch, split, batch.Context).RawOf(EvaluationDimension.Eye));
    }

    // ---------- 开 / 关缓存：决策序列逐步相同 ----------

    /// <summary>
    /// 按 <paramref name="config"/> 与种子跑一局，全部玩家换成开 / 关缓存的正式 AI（剪枝参数与会话缺省同一取法），返回日志。
    /// <paramref name="cacheLife"/> 为 <c>null</c> 即不替换（会话自己装的 AI）——用来证明替换本身不改变走法。
    /// </summary>
    internal static MatchLog Play(RunConfig config, ulong seed, bool? cacheLife)
    {
        MatchSession session = MatchSession.Create(config, seed);
        if (cacheLife is { } cache)
        {
            for (int i = 0; i < session.Match.Players.Length; i++)
            {
                PlayerId player = session.Match.Players[i];
                PlayerAiConfig ai = session.Config.Players[i];
                AiSearchConfig search = ai.Search
                    ?? (AiSearchConfig.ForMap(ai.Difficulty, session.Match.Map.PlayableCount, session.CellLimit) with { PassThreshold = session.PassThreshold });
                session.SetController(player, HeuristicAi.Create(session.Match, player, ai.Difficulty, ai.Weights, search, lifeQuery: null, cache));
            }
        }

        return session.Run();
    }

    /// <summary>开 / 关缓存各跑一次，逐行比对确定性文本（含候选、预演、落子分解事件）；返回比对过的小回合数与事件数。</summary>
    private static (int Turns, int Events) AssertCacheNeutral(RunConfig config, ulong seed)
    {
        MatchLog on = Play(config, seed, cacheLife: true);
        MatchLog off = Play(config, seed, cacheLife: false);
        Assert.False(on.IsFailed, on.Failure?.ToString());
        ReplayResult diff = Replayer.Compare(off, on);
        Assert.True(diff.Identical, $"种子 {seed}：{diff}");
        Assert.Equal(SimFixtures.TurnTexts(off.Turns), SimFixtures.TurnTexts(on.Turns));
        return (on.Turns.Count, on.Events.Count);
    }

    [Fact]
    public void 缓存开关不改变决策序列()
    {
        // 缩小版（默认套件）：v5、种子 31、4 名 Standard AI、24 个小回合、完整事件流。完整版见下面两条慢测试（种子 1–20：标准图跑到终局，边疆图截断 80 个小回合）。
        // 变异 M-C11（指纹漏掉第 1 行）→ 红 11（含本测试、同一决策内重复盘面只分析一次与眼位 / 活形中性 / 硬约束的多条算例）：这类"缓存改变结果"的错误在这里以整局分歧出现。
        // 变异 M-C10（指纹只记有子 / 无子、不记所有者）→ 0 红，是等价变异：同一决策内的候选盘面都是"同一个批次前盘面 + 本人落子 − 被提的子"，
        // 占用格集合相同则所有者必相同；所有者只在跨决策时才区分得开——又一条缓存不得跨决策的理由。所有者仍留在指纹里。
        // 替换 AI 本身不改变走法：会话自己装的 AI 跑出同一份日志。
        RunConfig config = SimFixtures.Config(seedStart: 31, turnLimit: 24, difficulty: AiDifficulty.Standard);
        (int turns, int events) = AssertCacheNeutral(config, 31);
        Assert.True(turns >= 24, $"小回合 {turns}");
        Assert.True(events >= 100, $"事件 {events}");
        Assert.Equal(Play(config, 31, cacheLife: null).DeterministicText(), Play(config, 31, cacheLife: true).DeterministicText());
    }

    [SlowFact]
    [Trait("Category", "Slow")]
    public void 缓存开关不改变决策序列_标准图种子1至20() => RunSeeds(FourPlayerBaseMap.Id, turnLimit: 600);

    [SlowFact]
    [Trait("Category", "Slow")]
    public void 缓存开关不改变决策序列_边疆图种子1至20() => RunSeeds(FrontierMapV2.Id, turnLimit: 80);

    /// <summary>
    /// 种子 1–20、4 名 Standard AI、完整事件流。标准图跑到终局（跑局缺省截断 600，实测每局 27–33 个小回合）；
    /// 边疆图截断在 80 个小回合（20 个大回合）：段 C 实测每小回合约 2.5 s，跑满 600 小回合的 20 × 2 局要数小时。
    /// </summary>
    private static void RunSeeds(string mapId, int turnLimit)
    {
        RunConfig config = SimFixtures.Config(turnLimit: turnLimit, difficulty: AiDifficulty.Standard) with { MapId = mapId };
        var results = new (int Turns, int Events)[20];
        Parallel.For(0, 20, new ParallelOptions { MaxDegreeOfParallelism = 10 }, i => results[i] = AssertCacheNeutral(config, (ulong)(i + 1)));
        Assert.All(results, r => Assert.True(r.Turns > 0));
    }
}
