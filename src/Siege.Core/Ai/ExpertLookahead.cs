using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Relics;
using Siege.Core.Scoring;

namespace Siege.Core.Ai;

/// <summary>
/// 专家难度的一层前瞻（expert-lookahead，ai-decision「专家难度的一层前瞻」）：在与高难完全相同的候选生成之后、复摆之前，
/// 对自身评价前 W 个非空候选各预演（B1）、用专家本人的高难口径（expert-opponent-model：专家本人的搜索配置、只取不扰动的一条贪心）模拟下一名对手的回应并预演（B2），按专家自己的评价器在 B2 上重打分，取最高者。
/// 构筑期决策（expert-tiebreak：下一名对手的行动大回合 ≤ 构筑期大回合数）不前瞻，直接取高难的选择。
/// </summary>
/// <remarks>
/// <para><b>不是博弈树</b>（D1）：只重排已有候选，不产生新候选、不改候选内容；只看一名对手的一次回应。</para>
/// <para><b>公平信息</b>（D5 / D6 / D8）：只读决策起点的 <see cref="MatchPublicView"/>、由它与候选预演推得的公开派生量（投影视图），以及专家本人的批次上下文。
/// 模拟对手持有"部署上限"枚普通子（公开可推得的满额）；未揭示信物在整个模拟中保持未揭示。本类不持有任何随机流（D5 (a)），每次决策新建、随决策丢弃，
/// 在信息边界守门里单列为类型闭包的根。</para>
/// <para><b>停手口径</b>（D4）：停手阈值与活形硬约束只在候选生成里生效；这里只在已过阈值的非空候选之间重排，不引入 Pass。</para>
/// <para><b>多样候选与近似两层</b>（expert-strength D2–D5）：前瞻集按落点格集合去重，不足 W 个时以排除重跑补足（重跑由调用方给出，仍是同一停手阈值下的贪心组批，零随机）；
/// 两层加分是"专家在回应后局面上的下一手最佳单点增量"这一静态评价项——不组批、不模拟之后的对手、不递归，不是第二层搜索；
/// expert-tiebreak D3 起它不再加进前瞻后分数（前瞻后分数 = 一层分数），只在一层分数的容差带（两层平局带宽）内作平局裁决。
/// 两层扫描只读 B2 的投影公开视图与专家本人决策起点的持有类型，同属本类的信息边界。</para>
/// </remarks>
internal sealed class ExpertLookahead
{
    private readonly PlayerId _me;
    private readonly MatchPublicView _view;
    private readonly BatchEvaluator _evaluator;
    private readonly EvaluationWeights _weights;
    private readonly AiSearchConfig _config;
    private readonly Func<GameBoard, LifeShapeReport>? _lifeQuery;
    private readonly bool _cacheLife;
    private int _simulatedRehearsals;
    private int _twoPlyRehearsals;

    /// <param name="me">专家本人。</param>
    /// <param name="view">决策起点的公开快照（前瞻后分数的"前"与候选生成同一份）。</param>
    /// <param name="evaluator">本次决策的同一个评价器（D2：同一量纲、同一份决策内活形缓存）。</param>
    /// <param name="weights">专家本人的评价权重——模拟对手也用它（裁决 ⑤）。</param>
    /// <param name="config">专家的搜索配置（前瞻宽度、本局生效的候选格上限与停手阈值）。</param>
    /// <param name="lifeQuery">活形查询测试接缝（与专家同一个）。</param>
    /// <param name="cacheLife">决策内活形缓存开关（模拟对手沿用）。</param>
    internal ExpertLookahead(
        PlayerId me, MatchPublicView view, BatchEvaluator evaluator, EvaluationWeights weights, AiSearchConfig config,
        Func<GameBoard, LifeShapeReport>? lifeQuery, bool cacheLife)
    {
        _me = me;
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _weights = weights ?? throw new ArgumentNullException(nameof(weights));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _lifeQuery = lifeQuery;
        _cacheLife = cacheLife;
    }

    /// <summary>前瞻集的一个成员：候选与来源（expert-strength D3）。</summary>
    internal readonly record struct LookaheadMember(CandidateBatch Candidate, CandidateSource Source);

    /// <summary>本引擎至今做过的两层单点预演次数（expert-strength D8 代理的第三部分）。</summary>
    internal int TwoPlyRehearsals => _twoPlyRehearsals;

    /// <summary>
    /// 模拟对手的搜索配置（expert-opponent-model D1 / 裁决 1、2，取代 expert-lookahead D5 / 裁决 ① 的标准预设口径）：专家本人的高难口径——
    /// 只由专家本人本局生效的搜索配置推得：候选落点数 N、只看即时收益开关、候选格上限、停手阈值（无子豁免与落后者豁免由模拟对手按 B1 与其本人判定）随专家本人；
    /// 只取不扰动的那一条贪心（M = 1，即 k = 0，零随机）；不前瞻（前瞻宽度、多样补充上限、两层权重清零）。两层平局带宽必须一并清零，
    /// 否则显式带宽 &gt; 0 的专家派生出"带宽 &gt; 0 而两层权重 = 0"，被 <see cref="AiSearchConfig.Validated"/> 拒绝。停滞大回合数虽随专家本人，
    /// 但模拟对手每次新建、没有停滞记忆，循环提子停手在模拟中不会触发。MUST NOT 读被模拟玩家的难度、配置或控制器状态。
    /// </summary>
    internal AiSearchConfig SimulatedConfig => _config with
    {
        CandidateBatchCount = 1,
        LookaheadWidth = 0,
        DiverseSupplementLimit = 0,
        TwoPlyWeightPermille = 0,
        TwoPlyTieBand = 0,
        // ai-whole-batch D3（裁决 3）：前瞻模拟的对手不生成整批候选，保持修法 B 的口径与开销。
        WholeBatch = 0,
        // ai-bridge-candidate D2（裁决 3）：前瞻模拟的对手不生成搭桥候选，同上。
        EditSeed = 0,
    };

    /// <summary>
    /// 从去重后的候选集合里选出被选候选（第 2–9 步），并给出前瞻记录。<paramref name="batch"/> 是专家本人的批次（B1 经 <paramref name="rehearse"/> 预演得到），
    /// 返回时已清空，由调用方复摆。<paramref name="rerun"/> 是多样补充的排除重跑（排除格 → 不扰动的贪心组批结果）；为 <c>null</c> 或多样补充上限为 0 时多样候选关闭。
    /// </summary>
    internal (CandidateBatch Choice, LookaheadRecord Record) Choose(
        IReadOnlyList<CandidateBatch> candidates, StagedBatch batch, Func<RehearsalResult> rehearse, Func<Coord, CandidateBatch>? rerun = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(rehearse);
        if (IsBuildPhaseDecision(_view, _me))
        {
            // 第 3 步（expert-tiebreak D1 / D2）：构筑期决策——在建前瞻集之前判定，不做多样补充的排除重跑、不模拟、不做两层扫描。
            // 只有空批次时与高难同样 Pass；否则直接取高难的选择（与控制器在前瞻宽度 0 时同一个 CandidateSelection.Best），记"不前瞻"，候选表只含它。
            CandidateBatch best = CandidateSelection.Best(candidates);
            return best.IsPass
                ? (best, LookaheadRecord.Passed)
                : (best, new LookaheadRecord(LookaheadStatus.NotApplied, [Unsimulated(new LookaheadMember(best, CandidateSource.Original))], 0, 0));
        }

        ImmutableArray<LookaheadMember> set = LookaheadSet(candidates, Math.Max(_config.LookaheadWidth, 1), _config.DiverseSupplementLimit, rerun);
        if (set.IsEmpty)
        {
            // 第 3 步：前瞻集为空 = 候选生成只得到空批次 → 与高难同样 Pass（不做多样补充，也不做两层扫描）。
            return (CandidateSelection.Best(candidates), LookaheadRecord.Passed);
        }

        if (_config.LookaheadWidth <= 1 || set.Length == 1)
        {
            // 第 3 步：不前瞻，直接取前瞻集的第一个（= 高难的选择，D1），不模拟、不计两层加分。
            return (set[0].Candidate, new LookaheadRecord(LookaheadStatus.NotApplied, [Unsimulated(set[0])], 0, 0));
        }

        var simulations = new List<Simulation>(set.Length);
        foreach (LookaheadMember member in set)
        {
            simulations.Add(Simulate(member, batch, rehearse));
        }

        // 第 7 步：两层加分（expert-strength D4）。须等全部候选模拟完：前瞻集中至少一个候选有下一名对手时每个候选都计（没有对手的在 B1 上计），
        // 全部没有下一名对手时整次退化，不计加分（与"只剩自己时退化"相同）；两层权重为 0 时不做任何扫描。
        LookaheadEntry[] entries = [.. simulations.Select(s => s.Entry)];
        if (_config.TwoPlyWeightPermille > 0 && entries.Any(e => e.Responder is not null))
        {
            for (int i = 0; i < entries.Length; i++)
            {
                BigInteger bonus = TwoPlyBonusOn(simulations[i].TwoPlyView(_view, _me), simulations[i].History, batch.Context);
                entries[i] = entries[i] with { TwoPlyBonus = bonus };
            }
        }

        // 第 8–9 步（expert-tiebreak D3）：前瞻后分数 = 一层分数（两层加分不加进去）；一层分数不低于最高 − B 的为平局集，集内取两层加分最大者，并列取一层分数高者，再并列取前瞻集靠前者。
        int chosen = SelectIndex([.. entries.Select(e => e.ScoreAfter)], [.. entries.Select(e => e.TwoPlyBonus)], _config.TwoPlyTieBand);
        return (set[chosen].Candidate, new LookaheadRecord(LookaheadStatus.Applied, [.. entries], chosen, _simulatedRehearsals, TwoPlyRehearsals: _twoPlyRehearsals));
    }

    /// <summary>
    /// 构筑期决策的判定（ai-decision「专家难度的一层前瞻」第 3 步，expert-tiebreak D1）：设 R = <see cref="PublicRules.BuildProtectionRounds"/>、r = 决策起点的大回合，
    /// r &lt; R，或 r = R 且本大回合行动顺序中专家之后仍有在局玩家（参赛中：未弃赛、未出局）时为构筑期决策——等价于"按决策起点的名册，下一名对手的行动大回合 ≤ R"。
    /// 只读决策起点公开快照中的大回合、行动顺序与参赛状态（公平信息）；不看 Pass 名单（Pass 不移出顺序）。名册口径同 <see cref="NextOpponent"/> 第 1–2 步（同战场的在局玩家）。
    /// </summary>
    internal static bool IsBuildPhaseDecision(MatchPublicView view, PlayerId me)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (view.MajorRound < PublicRules.BuildProtectionRounds)
        {
            return true;
        }

        if (view.MajorRound > PublicRules.BuildProtectionRounds)
        {
            return false;
        }

        // 在局玩家 = 同战场的参赛中玩家（RivalsInBattlefield 已去掉弃赛、出局者与专家本人；唯一战场上即全部其他在局玩家）。
        HashSet<PlayerId> rivals = [.. HeuristicTurnController.RivalsInBattlefield(view, me)];
        for (int i = view.ActionOrder.IndexOf(me) + 1; i < view.ActionOrder.Length; i++)
        {
            if (rivals.Contains(view.ActionOrder[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 第 2 步（多样候选关闭时的原规则）：去掉空批次（Pass），按 <see cref="CandidateSelection"/> 的确定性规则排序，取前 <paramref name="width"/> 个。
    /// 空批次 MUST NOT 进入前瞻集（D4）。
    /// </summary>
    internal static ImmutableArray<CandidateBatch> LookaheadSet(IEnumerable<CandidateBatch> candidates, int width) =>
        [.. LookaheadSet(candidates, width, supplementLimit: 0, rerun: null).Select(m => m.Candidate)];

    /// <summary>
    /// 前瞻集（ai-decision「前瞻集的多样候选」，expert-strength D3）：
    /// <list type="number">
    /// <item>去掉空批次、按 <see cref="CandidateSelection"/> 排序；多样补充上限 <paramref name="supplementLimit"/> 为 0（或没有重跑）时即取前 <paramref name="width"/> 个——与 expert-lookahead 逐项相同。</item>
    /// <item>否则按落点格集合去重（只比格，不比类型与改造目标），每个格集合只留排序最靠前的一个，取前 W 个，来源记为原排序。</item>
    /// <item>不足 W 个时补足：锚依次取前瞻集成员（含补足中新追加的），对锚的每个落点格（按锚批次的落点次序，即贪心接受的次序）做一次排除重跑；
    /// 结果非空且格集合与已有成员都不同即追加到末尾，来源记为多样补充。达到 W 个、重跑次数达到 S、或锚与格都已用尽即停。</item>
    /// </list>
    /// 原排序成员在前、补充成员按追加次序在后：前瞻集的第一个始终是高难的选择。零随机：<paramref name="rerun"/> 由调用方以不扰动的原排序实现。
    /// </summary>
    internal static ImmutableArray<LookaheadMember> LookaheadSet(
        IEnumerable<CandidateBatch> candidates, int width, int supplementLimit, Func<Coord, CandidateBatch>? rerun)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        List<CandidateBatch> nonEmpty = [.. candidates.Where(c => !c.IsPass)];
        nonEmpty.Sort(CandidateSelection.Compare);
        if (supplementLimit <= 0 || rerun is null)
        {
            return [.. nonEmpty.Take(width).Select(c => new LookaheadMember(c, CandidateSource.Original))];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var set = new List<LookaheadMember>(width);
        foreach (CandidateBatch candidate in nonEmpty)
        {
            if (set.Count >= width)
            {
                break;
            }

            if (seen.Add(CellSetKey(candidate)))
            {
                set.Add(new LookaheadMember(candidate, CandidateSource.Original));
            }
        }

        int reruns = 0;
        for (int anchor = 0; anchor < set.Count && set.Count < width && reruns < supplementLimit; anchor++)
        {
            foreach (Placement placement in set[anchor].Candidate.Placements)
            {
                if (set.Count >= width || reruns >= supplementLimit)
                {
                    break;
                }

                CandidateBatch result = rerun(placement.Coord);
                reruns++;
                if (!result.IsPass && seen.Add(CellSetKey(result)))
                {
                    set.Add(new LookaheadMember(result, CandidateSource.Supplement));
                }
            }
        }

        return [.. set];
    }

    /// <summary>落点格集合的键：只看格（坐标序），不看类型与改造目标。</summary>
    private static string CellSetKey(CandidateBatch candidate) => string.Join(",", candidate.SortedCoords.Select(c => c.ToNotation()));

    /// <summary>两层权重为 0 时的第 9 步：一层分数最高者的下标；同分取靠前者（即两层加分全为 0、带宽 0 的平局裁决）。</summary>
    internal static int SelectIndex(IReadOnlyList<BigInteger> scoresAfter)
    {
        ArgumentNullException.ThrowIfNull(scoresAfter);
        return SelectIndex(scoresAfter, [.. scoresAfter.Select(_ => BigInteger.Zero)], BigInteger.Zero);
    }

    /// <summary>
    /// 第 9 步（expert-tiebreak D3，两层平局裁决）：s* = 一层分数的最大值；一层分数不低于 s* − <paramref name="band"/> 的候选组成平局集，
    /// 取集内两层加分最大者；两层加分并列时取一层分数最高者（负责人裁决 2026-10-21）；仍并列取前瞻集下标最小者。两层加分 MUST NOT 与一层分数相加（原"相加"口径已删除）。
    /// 前瞻集按自身总分排序，下标 0 不一定在平局集内（一层分数可能后者更高），故从"尚未选定"起算。
    /// </summary>
    /// <param name="oneLayer">各候选的一层分数（= 前瞻后分数）。</param>
    /// <param name="twoPly">各候选的两层加分（两层权重为 0 或不扫描时全为 0）。</param>
    /// <param name="band">两层平局带宽 B（非负）。</param>
    internal static int SelectIndex(IReadOnlyList<BigInteger> oneLayer, IReadOnlyList<BigInteger> twoPly, BigInteger band)
    {
        ArgumentNullException.ThrowIfNull(oneLayer);
        ArgumentNullException.ThrowIfNull(twoPly);
        if (oneLayer.Count == 0)
        {
            throw new ArgumentException("前瞻集为空。", nameof(oneLayer));
        }

        if (twoPly.Count != oneLayer.Count)
        {
            throw new ArgumentException("两层加分与一层分数的个数不同。", nameof(twoPly));
        }

        BigInteger floor = oneLayer.Max() - band;
        int best = -1;
        for (int i = 0; i < oneLayer.Count; i++)
        {
            if (oneLayer[i] >= floor && (best < 0 || twoPly[i] > twoPly[best] || (twoPly[i] == twoPly[best] && oneLayer[i] > oneLayer[best])))
            {
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// 一个候选的模拟结果：一层前瞻的记录项（尚未加两层加分）、B1 的投影视图、B2（回应为 Pass 或不做模拟时为 <c>null</c>）与同形历史（决策起点盘面 + B1）。
    /// 每次决策内新建、随决策丢弃；在信息边界守门里单列为类型闭包的根（不经本类的字段可达）。
    /// </summary>
    internal sealed record Simulation(LookaheadEntry Entry, MatchPublicView B1, GameBoard? B2, BoardHistory History)
    {
        /// <summary>两层加分所在的局面（D4）：有回应落子时为 B2 的投影视图（并把 B2 记入同形历史），否则即 B1。出局判定口径同 <see cref="Project(MatchPublicView, GameBoard, PlayerId)"/>。</summary>
        internal MatchPublicView TwoPlyView(MatchPublicView view, PlayerId me)
        {
            if (B2 is null)
            {
                return B1;
            }

            MatchPublicView after = Project(view, B2, me);
            History.Record(after.BoardSerialized);
            return after;
        }
    }

    /// <summary>第 4–6 步：候选 c 的 B1 → 下一名对手 → 模拟回应 → B2 → 一层分数。</summary>
    private Simulation Simulate(LookaheadMember member, StagedBatch batch, Func<RehearsalResult> rehearse)
    {
        CandidateBatch candidate = member.Candidate;
        batch.Clear();
        foreach (Placement placement in candidate.Placements)
        {
            if (batch.Stage(placement.Coord, placement.Type, placement.Edit) is { } failure)
            {
                throw new SiegeRuleException($"前瞻复摆已预演通过的候选被拒绝：{failure.Message}");
            }
        }

        RehearsalResult b1 = rehearse();
        batch.Clear();
        if (!b1.IsLegal || b1.ProjectedBoard is null)
        {
            throw new SiegeRuleException($"前瞻预演已通过的候选不合法：{b1.Failure?.Message}");
        }

        MatchPublicView projected = Project(_view, b1.ProjectedBoard, _me);
        var history = new BoardHistory();
        history.Record(_view.BoardSerialized);
        history.Record(projected.BoardSerialized);
        if (NextOpponent(_view, _me, projected) is not { } next)
        {
            // 第 4 条（下一名对手）：名册为空 → 不做模拟，B2 即 B1。
            return new Simulation(Unsimulated(member), projected, null, history);
        }

        BatchContext context = SimulatedContext(projected, next.Responder, next.MajorRound);
        (CandidateBatch? response, GameBoard b2, ImmutableArray<CapturedStone> responseCaptures) = Respond(projected, context, history);
        var after = new RehearsalResult(IsLegal: true, IsPass: false, Failure: null, b1.Captures, b2);

        // 第 6 步：同一评价器、决策起点为"前"、B2 为"后"、c 的落点与 c 本批的提子；直接打分，MUST NOT 在 B2 上重判活形硬约束（D2）。
        EvaluationBreakdown evaluation = _evaluator.Evaluate(candidate.Placements, after, batch.Context);
        BigInteger scoreAfter = WithLostStonesInDanger(evaluation, CountLostAtStart(responseCaptures)).Total;
        var entry = new LookaheadEntry(
            candidate.Key, candidate.Total, next.Responder, next.MajorRound, context.DeployLimit, response?.Key ?? string.Empty, scoreAfter, member.Source);
        return new Simulation(entry, projected, response is null ? null : b2, history);
    }

    /// <summary>
    /// 两层加分（ai-decision「前瞻的近似两层加分」，expert-strength D4）：⌊λ‰ × max(0, 专家在 <paramref name="after"/> 上的下一手最佳单点增量) / 1000⌋。
    /// 专家在 <paramref name="after"/> 上已出局、没有出生区、下一大回合公开部署上限为 0 或没有持有类型时为 0（不做扫描）。
    /// </summary>
    /// <param name="after">B2（或无对手时 B1）的投影公开视图。</param>
    /// <param name="history">同形历史：决策起点盘面、B1、B2。</param>
    /// <param name="mine">专家本人决策起点的批次上下文（只取其中数量 &gt; 0 的类型）。</param>
    internal BigInteger TwoPlyBonusOn(MatchPublicView after, BoardHistory history, BatchContext mine)
    {
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(mine);
        if (NextMoveContext(after, mine) is not { } next)
        {
            return BigInteger.Zero;
        }

        return TwoPlyBonus(_config.TwoPlyWeightPermille, BestSingleGain(after, next, history) ?? BigInteger.Zero);
    }

    /// <summary>加分 = ⌊λ‰ × max(0, 最大单点增量) / 1000⌋：先取下限 0、再乘权重、再向下取整，全程整数（D4）。</summary>
    internal static BigInteger TwoPlyBonus(int permille, BigInteger maxGain) =>
        BigInteger.Max(BigInteger.Zero, maxGain) * permille / 1000;

    /// <summary>
    /// 专家下一手的单点上下文（D4 / 公平信息第 7 项）：行动大回合 = 决策起点的大回合 + 1（每名玩家每个大回合只行动一次）；
    /// 合法落子范围与部署上限与模拟对手同一实现（<see cref="PublicRules.LegalRange"/>、<see cref="PublicRelicEffects.Deploy"/>，主体换成专家本人，按 <paramref name="after"/> 的公开状态）；
    /// 类型 = 决策起点手中数量 &gt; 0 的类型（不扣除候选用掉的枚数：下一大回合的征募不可知，D5 第 1 条），每种给"部署上限"枚——与实验 E5 相同的口径，
    /// 只为让单点预演的库存足额（维度 7 供给匹配因此不罚单点），不是对真实数量的估计。专家出局、没有出生区、部署上限为 0 或没有持有类型时返回 <c>null</c>。
    /// </summary>
    internal BatchContext? NextMoveContext(MatchPublicView after, BatchContext mine)
    {
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(mine);
        PlayerFlowState state = after.Players.Single(p => p.Player == _me);
        if (!state.IsActive || state.BirthZone is not { } zone)
        {
            return null;
        }

        int round = _view.MajorRound + 1;
        PublicDeployEffects effects = PublicRelicEffects.Deploy(_me, round, after.Relics);
        ImmutableArray<PieceType> types = [.. mine.Stock.Where(kv => kv.Value > 0).Select(kv => kv.Key).Order()];
        if (effects.DeployLimit == 0 || types.IsEmpty)
        {
            return null;
        }

        return new BatchContext
        {
            Player = _me,
            DeployLimit = effects.DeployLimit,
            LegalRange = PublicRules.LegalRange(after.Board, zone, _me, after.LifeShape, round),
            EditScope = PublicRules.EditScope(after.Board, zone, round),
            Stock = types.ToDictionary(t => t, _ => effects.DeployLimit),
            WorkshopActive = effects.WorkshopActive,
        };
    }

    /// <summary>
    /// 最大单点增量（D4）：以 <paramref name="after"/> 为"前"新建评价器（同一权重、同一难度口径、同一活形缓存设置），对 <paramref name="next"/> 的每个可落子空格 × 每种类型
    /// （不带改造）预演一次；预演合法且通过活形硬约束（<see cref="BatchEvaluator.TryEvaluate"/>）者取九维加权总分，返回最大值；没有合法单点为 <c>null</c>。
    /// MUST NOT 施加停手阈值（下限 0 是裁决口径）。本局候选格上限生效且合法空格多于它时，先按单点排序同一预筛口径收窄（<see cref="HeuristicTurnController.PrefilterCells"/>）。
    /// </summary>
    internal BigInteger? BestSingleGain(MatchPublicView after, BatchContext next, BoardHistory history)
    {
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(history);
        var evaluator = new BatchEvaluator(_me, after, _weights, _evaluator.ImmediateOnly, relicValue: null, _lifeQuery, _cacheLife);
        var staged = new StagedBatch(after.Board, next);
        RehearsalResult Rehearse()
        {
            _twoPlyRehearsals++;
            return BatchRehearsal.Rehearse(after.Board, next, staged.Placements, history);
        }

        ImmutableArray<PieceType> types = [.. next.Stock.Where(kv => kv.Value > 0).Select(kv => kv.Key).Order()];
        ImmutableArray<Coord> cells = [.. next.LegalRange.Where(c => after.Board[c].IsPlayableEmpty).Order()];
        if (_config.CandidateCellLimit > 0 && cells.Length > _config.CandidateCellLimit && !types.IsEmpty)
        {
            cells = HeuristicTurnController.PrefilterCells(cells, types, staged, Rehearse, evaluator, _config.CandidateCellLimit);
        }

        BigInteger? best = null;
        foreach (Coord cell in cells)
        {
            foreach (PieceType type in types)
            {
                staged.Clear();
                if (staged.Stage(cell, type, null) is not null)
                {
                    continue;
                }

                RehearsalResult result = Rehearse();
                if (result.IsLegal && evaluator.TryEvaluate(staged.Placements, result, next, out EvaluationBreakdown? evaluation)
                    && (best is null || evaluation.Total > best.Value))
                {
                    best = evaluation.Total;
                }
            }
        }

        staged.Clear();
        return best;
    }

    /// <summary>模拟回应提走的、决策起点就在盘上的专家本人棋子数（c 本批落下又被提走的不计）。</summary>
    private int CountLostAtStart(ImmutableArray<CapturedStone> responseCaptures) =>
        responseCaptures.Count(s => s.Owner == _me && _view.Board[s.Coord].Occupant is { } occupant && occupant.Owner == _me);

    /// <summary>
    /// 裁决（2026-09-27 段 A 中）对 D2 的修正：被提走的己方棋子按 ≤ 1 口气的危险计入安全维——每枚在安全维原始值上再扣 <see cref="GroupSafety.AtariDangerPerStone"/>。
    /// 否则危险棋串被提走后其危险扣分随之消失，"被提走"会比"被叫吃"得分更高（专家弃串）。只作用于前瞻后分数，权重不变，前后两个分数同一量纲；
    /// 安全维不计的难度（只看即时收益）不扣。
    /// </summary>
    private EvaluationBreakdown WithLostStonesInDanger(EvaluationBreakdown evaluation, int lost)
    {
        if (lost == 0 || _evaluator.ImmediateOnly)
        {
            return evaluation;
        }

        int safety = (int)EvaluationDimension.Safety;
        return evaluation with { Raw = evaluation.Raw.SetItem(safety, evaluation.Raw[safety] - ((BigInteger)lost * GroupSafety.AtariDangerPerStone)) };
    }

    /// <summary>不做模拟的候选：一层分数等于自身总分。</summary>
    private static LookaheadEntry Unsimulated(LookaheadMember member) =>
        new(member.Candidate.Key, member.Candidate.Total, Responder: null, ResponderRound: null, SimulatedDeployLimit: null, ResponseKey: null, member.Candidate.Total, member.Source);

    /// <summary>
    /// 第 5 步：模拟对手在投影视图（B1）上的回应。模拟对手是只在本次决策内存在、按专家本人的高难口径（<see cref="SimulatedConfig"/>）的启发式实例，不持有随机流；
    /// 预演用 <see cref="BatchRehearsal.Rehearse"/> 与局部同形历史 <paramref name="history"/>（决策起点盘面 + B1，D8 的已知近似）。返回回应（Pass 为 <c>null</c>）、B2 与回应的提子。
    /// </summary>
    private (CandidateBatch? Response, GameBoard B2, ImmutableArray<CapturedStone> Captures) Respond(MatchPublicView projected, BatchContext context, BoardHistory history)
    {
        var batch = new StagedBatch(projected.Board, context);
        RehearsalResult Rehearse()
        {
            _simulatedRehearsals++;
            return BatchRehearsal.Rehearse(projected.Board, context, batch.Placements, history);
        }

        HeuristicTurnController opponent = HeuristicTurnController.ForSimulation(
            context.Player, projected, _weights, SimulatedConfig, _lifeQuery, _cacheLife);
        opponent.Deploy(batch, Rehearse);
        if (opponent.LastChoice is not { IsPass: false } response)
        {
            return (null, projected.Board, []);
        }

        RehearsalResult b2 = Rehearse();
        if (!b2.IsLegal || b2.ProjectedBoard is null)
        {
            throw new SiegeRuleException($"模拟对手的回应复算不合法：{b2.Failure?.Message}");
        }

        return (response, b2.ProjectedBoard, b2.Captures);
    }

    /// <summary>
    /// 投影公开视图（D8）：由决策起点的公开快照与盘面 <paramref name="board"/>（B1）构造同类型视图——盘面换成副本；势力经 <see cref="PowerCalculator"/> 重算
    /// （已知信物内容只取决策起点已揭示的）；信物控制经 <see cref="RelicControl.Of"/> 重算；揭示状态不变（未揭示的保持未揭示、内容为空）；
    /// 流程状态按出局判据的唯一实现（<see cref="PublicRules.Eliminated"/>）更新；活形分析重算；其余字段（阶段、顺序、手牌类型集合等）不变。
    /// 名册口径与真实结算第 5 步相同（出局检查之前的状态），因此在不涉及新揭示的局面上与真实结算后发布的快照逐字段相等。
    /// </summary>
    internal static MatchPublicView Project(MatchPublicView view, GameBoard board) => Project(view, board, judged: null);

    /// <summary>
    /// 同 <see cref="Project(MatchPublicView, GameBoard)"/>，出局判定只对专家 <paramref name="me"/> 所在战场的玩家做（ai-seat-vision 裁决 V2，修 C12）：
    /// 候选与回应只改变本战场的盘面，他战场玩家的势力不会因此变化；按座位视野下他们的势力在投影里为 0（不可见，不是归零），
    /// MUST NOT 因此被判出局。全知视图下他战场玩家的势力与决策起点相同、不会新出局，两种口径结果一致。
    /// </summary>
    internal static MatchPublicView Project(MatchPublicView view, GameBoard board, PlayerId me)
    {
        ArgumentNullException.ThrowIfNull(view);
        return Project(view, board, [me, .. HeuristicTurnController.RivalsInBattlefield(view, me)]);
    }

    /// <summary>投影本体：<paramref name="judged"/> 为 <c>null</c> 时对全体玩家做出局判定，否则只对其中的玩家做。</summary>
    private static MatchPublicView Project(MatchPublicView view, GameBoard board, ImmutableHashSet<PlayerId>? judged)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(board);
        GameBoard copy = board.Clone();
        ImmutableSortedDictionary<PlayerId, PlayerStatus> roster = view.Players.ToImmutableSortedDictionary(p => p.Player, p => p.Status);
        PowerSnapshot power = PowerCalculator.Compute(copy, roster, RevealedRelics.Of(view.Relics), view.ScoringVersion);
        ImmutableArray<RelicPublicState> relics = [.. view.Relics.Select(r => r with { Control = RelicControl.Of(power.Coverage, r.Coord, roster) })];

        // 「曾建立正势力」置位在出局检查之前（与 MatchFlow 的结算顺序一致；置位只会让总势力 > 0 的玩家变真，不改变谁出局）。
        ImmutableArray<PlayerFlowState> marked =
            [.. view.Players.Select(p => p with { HasEstablishedPower = p.HasEstablishedPower || power.Of(p.Player).Total > BigInteger.Zero })];
        ImmutableArray<PlayerId> eliminated = [.. PublicRules.Eliminated(marked, power).Where(p => judged is null || judged.Contains(p))];
        int order = (view.Players.Max(p => p.EliminationOrder) ?? 0) + 1;
        ImmutableArray<PlayerFlowState> players =
        [
            .. marked.Select(p => eliminated.Contains(p.Player)
                ? p with { Status = PlayerStatus.Eliminated, EliminationOrder = order, EliminatedInMajorRound = view.MajorRound }
                : p),
        ];

        return view with
        {
            Board = copy,
            BoardSerialized = copy.Serialize(),
            Power = power,
            Relics = relics,
            Players = players,
            LifeShape = LifeShapeReport.Analyze(copy),
        };
    }

    /// <summary>
    /// 下一名对手（ai-decision「前瞻中的下一名对手」）：名册 = 投影视图（B1）上仍参赛的玩家去掉专家本人，只取与专家同一战场的在局玩家
    /// （ai-seat-vision 裁决 V2，修 C12：他战场玩家不进名册，也不因势力在按座位投影里不可见而被当作出局；同战场口径与
    /// <see cref="HeuristicTurnController.RivalsInBattlefield"/> 同一实现）。
    /// 本大回合顺序中排在专家之后的第一名名册玩家即是（行动大回合 = 当前大回合）；没有时用 <see cref="InitiativeOrder.Generate"/> 按 B1 上的公开输入预测下一大回合顺序，
    /// 取其中第一名名册玩家（行动大回合 + 1；专家居首时自然跳过自己）。种子兜底顺序不公开，以玩家编号代替（D3 的已知近似）。名册为空返回 <c>null</c>。
    /// </summary>
    internal static (PlayerId Responder, int MajorRound)? NextOpponent(MatchPublicView view, PlayerId me, MatchPublicView projected)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(projected);
        HashSet<PlayerId> roster = [.. projected.Players.Where(p => p.IsActive && p.Player != me).Select(p => p.Player)];
        roster.IntersectWith(HeuristicTurnController.RivalsInBattlefield(view, me));

        if (roster.Count == 0)
        {
            return null;
        }

        for (int i = view.ActionOrder.IndexOf(me) + 1; i < view.ActionOrder.Length; i++)
        {
            if (roster.Contains(view.ActionOrder[i]))
            {
                return (view.ActionOrder[i], view.MajorRound);
            }
        }

        foreach (PlayerId player in PredictNextOrder(view, projected, [me, .. roster]))
        {
            if (roster.Contains(player))
            {
                return (player, view.MajorRound + 1);
            }
        }

        return null;
    }

    /// <summary>
    /// 按 B1 预测下一大回合顺序：与大回合结束时同一组输入与同一个实现——势力名次（出局检查之后的名册）、公开先锋修正（B1 上重算控制）、
    /// 本大回合的行动位置（第 1 大回合结束时不计），公式与同值链走 <see cref="InitiativeOrder"/>；种子兜底以玩家编号代替。
    /// 只在 <paramref name="among"/>（专家与名册玩家）之间比较先后（ai-seat-vision 裁决 V2）：其余玩家按不参与排名处理
    /// （名次与先手值的参赛人数都只数这些玩家），不需要他战场玩家的势力。
    /// </summary>
    private static ImmutableArray<PlayerId> PredictNextOrder(MatchPublicView view, MatchPublicView projected, ImmutableHashSet<PlayerId> among)
    {
        ImmutableSortedDictionary<PlayerId, PlayerStatus> roster = projected.Players.ToImmutableSortedDictionary(
            p => p.Player, p => among.Contains(p.Player) || !p.IsActive ? p.Status : PlayerStatus.Eliminated);
        ImmutableArray<PlayerId> active = [.. projected.Players.Where(p => p.IsActive && among.Contains(p.Player)).Select(p => p.Player)];
        PowerSnapshot power = PowerCalculator.Compute(projected.Board, roster, RevealedRelics.Of(view.Relics), view.ScoringVersion);
        ImmutableArray<RelicPublicState> relics = [.. projected.Relics.Select(r => r with { Control = RelicControl.Of(power.Coverage, r.Coord, roster) })];
        ImmutableSortedDictionary<PlayerId, int> bonuses = PublicRelicEffects.InitiativeBonuses(active, relics);

        ImmutableArray<InitiativeEntry>.Builder entries = ImmutableArray.CreateBuilder<InitiativeEntry>(active.Length);
        foreach (PlayerId player in active)
        {
            int rank = power.RankOf(player) ?? throw new SiegeRuleException($"参赛玩家 {player} 没有势力名次。");
            int bonus = bonuses[player];
            int? previous = view.MajorRound >= 2 ? view.ActionOrder.IndexOf(player) : null;
            entries.Add(new InitiativeEntry(player, rank, power.Of(player).Total, bonus,
                InitiativeOrder.ValueOf(active.Length, rank, bonus), previous, SeedRank: player.Value));
        }

        return InitiativeOrder.Generate(view.MajorRound, entries.MoveToImmutable()).NextOrder;
    }

    /// <summary>
    /// 模拟对手的批次上下文（ai-decision「前瞻模拟的公平信息」第 1–3 项）：应答者、D 枚普通子、D = 公开部署上限（按应答者的行动大回合，军令控制在 B1 上重算、
    /// 内容未知的按不提供计）、合法落子范围（与对局流程同一实现，按应答者的行动大回合）、工坊标记（B1 上是否控制内容已知的工坊）。
    /// </summary>
    internal static BatchContext SimulatedContext(MatchPublicView projected, PlayerId responder, int majorRound)
    {
        ArgumentNullException.ThrowIfNull(projected);
        PlayerFlowState state = projected.Players.Single(p => p.Player == responder);
        int zone = state.BirthZone ?? throw new SiegeRuleException($"玩家 {responder} 尚未锁定出生区。");
        PublicDeployEffects effects = PublicRelicEffects.Deploy(responder, majorRound, projected.Relics);
        return new BatchContext
        {
            Player = responder,
            DeployLimit = effects.DeployLimit,
            LegalRange = PublicRules.LegalRange(projected.Board, zone, responder, projected.LifeShape, majorRound),
            EditScope = PublicRules.EditScope(projected.Board, zone, majorRound),
            Stock = new Dictionary<PieceType, int> { [PieceType.Basic] = effects.DeployLimit },
            WorkshopActive = effects.WorkshopActive,
        };
    }
}
