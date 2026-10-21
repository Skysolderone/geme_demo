using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Core.Relics;
using Siege.Core.Scoring;

namespace Siege.Core.Ai;

/// <summary>
/// 正式对战 AI（设计文档 §15.1 / §15.2）。信息边界在类型上成立（裁决 9）：只持有 <see cref="MatchPublicView"/> 的观察委托、
/// 一条种子子流与配置；每阶段的操作只经 <see cref="ITurnController"/> 递入的本人句柄。拿不到 <c>HandLedger</c> / <c>RelicLedger</c> / <c>MatchFlow</c>。
/// </summary>
/// <remarks>
/// <para>部署（design.md D3）：对每个合法空格 × 持有类型做单点预演评价，取前 N；再以"贪心 + 种子扰动"组合出不超过 M 个候选批次，
/// 按 <see cref="CandidateSelection"/> 选优。候选评价全部靠 <c>rehearse()</c> 的零副作用预演，AI 不重算提子与军势。</para>
/// <para>征募（裁决 4）：与部署分两步，只用类型价值 + 成长亲和 + 供给匹配做前瞻，不做联合搜索。</para>
/// <para>随机只来自构造时传入的子流（建议 <see cref="HeuristicAi.StreamName"/>），绝不消费 <c>relic-gen</c> / <c>recruit</c> / <c>setup</c>。</para>
/// </remarks>
public sealed class HeuristicTurnController : ITurnController
{
    /// <summary>种子扰动的局部窗口：每个位置只与其后至多 2 个位置交换，保持"基本按单点分"的顺序。</summary>
    public const int PerturbationWindow = 3;

    private readonly Func<MatchPublicView> _observe;

    /// <summary>扰动子流；只有前瞻里的模拟对手为 <c>null</c>（它只走 k = 0 的无扰动贪心，D5 (a)），此时任何扰动都会响亮失败。</summary>
    private readonly RandomStream? _perturbation;
    private readonly Func<RelicPublicState, int>? _relicValue;
    private readonly Func<GameBoard, LifeShapeReport>? _lifeQuery;
    private readonly bool _cacheLife;
    private readonly List<string> _decisions = [];

    // ---------- 循环提子停手的记忆（cycle-stop D3）：只记本人、只读公开量 ----------

    /// <summary>本人每个大回合<b>首次部署决策</b>时采样的公开势力（大回合 → 总势力）。K = 0 时不采样。</summary>
    private readonly SortedDictionary<int, BigInteger> _powerByRound = [];

    /// <summary>
    /// 按观察所在大回合归档的"被提过的格"：相邻两次观察之间某格由有子变为空或换了主人，即视为在这期间被提过（棋子只会因提子离开盘面，判据精确；
    /// 同一主人在两次观察之间被提又补回同格的情形看不出来——它仍满足"被提过"，这里保守地不计）。纯由公开占用推导，不读任何提子记录之外的量。
    /// </summary>
    private readonly SortedDictionary<int, HashSet<Coord>> _capturedByRound = [];

    /// <summary>上一次观察（部署决策入口）时的占用：格 → 主人。</summary>
    private Dictionary<Coord, PlayerId>? _lastOccupancy;

    public HeuristicTurnController(
        PlayerId player,
        Func<MatchPublicView> observe,
        RandomStream perturbation,
        AiDifficulty difficulty = AiDifficulty.Standard,
        EvaluationWeights? weights = null,
        AiSearchConfig? config = null)
        : this(player, observe, perturbation, difficulty, weights, config, relicValue: null)
    {
    }

    /// <summary>
    /// 调试旁路（<c>internal</c>）：<paramref name="relicValue"/> 可读真实信物内容。正式构造不暴露此参数。
    /// 测试接缝：<paramref name="lifeQuery"/> 替换活形查询（计数桩），<paramref name="cacheLife"/> 为 <c>false</c> 关闭决策内活形缓存（见 <see cref="BatchEvaluator"/>）。
    /// </summary>
    internal HeuristicTurnController(
        PlayerId player,
        Func<MatchPublicView> observe,
        RandomStream perturbation,
        AiDifficulty difficulty,
        EvaluationWeights? weights,
        AiSearchConfig? config,
        Func<RelicPublicState, int>? relicValue,
        Func<GameBoard, LifeShapeReport>? lifeQuery = null,
        bool cacheLife = true)
        : this(player, observe, difficulty, weights, config, relicValue, lifeQuery, cacheLife,
            perturbation ?? throw new ArgumentNullException(nameof(perturbation)))
    {
    }

    /// <summary>构造本体。<paramref name="perturbation"/> 只在 <see cref="ForSimulation"/> 里为 <c>null</c>。</summary>
    private HeuristicTurnController(
        PlayerId player,
        Func<MatchPublicView> observe,
        AiDifficulty difficulty,
        EvaluationWeights? weights,
        AiSearchConfig? config,
        Func<RelicPublicState, int>? relicValue,
        Func<GameBoard, LifeShapeReport>? lifeQuery,
        bool cacheLife,
        RandomStream? perturbation)
    {
        Player = player;
        _observe = observe ?? throw new ArgumentNullException(nameof(observe));
        _perturbation = perturbation;
        Difficulty = difficulty;
        Weights = weights ?? EvaluationWeights.Default;
        Config = (config ?? AiSearchConfig.ForDifficulty(difficulty)).Validated();
        _relicValue = relicValue;
        _lifeQuery = lifeQuery;
        _cacheLife = cacheLife;
    }

    public PlayerId Player { get; }

    /// <summary>
    /// 预演结果的读取口径（board-link-mode 实现裁决 C4；ai-seat-vision 起为缺省）：按座位视野时由 <see cref="HeuristicAi.VisionFor"/> 给出，把对局的零副作用预演结果投影到与
    /// 观察视图相同的口径（去掉未与本座位链接的战场上的棋子），使"预演前"（观察视图）与"预演后"（预演结果）同一口径——评价代码不改。
    /// 显式全知（配置的视野项为 <c>false</c>，见 <see cref="HeuristicAi.VisionFor"/>）时为 <c>null</c>，预演结果原样使用；不经 <see cref="HeuristicAi.VisionFor"/> 直接构造的控制器同为 <c>null</c>。
    /// </summary>
    internal Func<RehearsalResult, RehearsalResult>? RehearsalView { get; init; }

    public AiDifficulty Difficulty { get; }

    public EvaluationWeights Weights { get; }

    public AiSearchConfig Config { get; }

    /// <summary>决策日志（每次弃牌 / 选取 / 部署一行），供可复现性断言与跑局日志。</summary>
    public IReadOnlyList<string> Decisions => _decisions;

    /// <summary>
    /// 停滞停手的部署决策条目（stagnation-trace D1）：停滞判定成立（<see cref="LastStagnation"/> 非空）且本次决策最终 Pass 时记它，区别于普通 Pass 的 <c>D:pass</c>；
    /// 决胜一手接管时照常记落点。跑局层据此在快照写 <c>Stalled</c>（S1），离线分析按它计停滞停手次数。
    /// </summary>
    public const string StallDecision = "D:stall";

    /// <summary>
    /// 最近一次部署里进入完整"类型 × 改造目标"枚举的格（坐标序）。候选格上限 K 未启用或合法空格不多于 K 时就是全部合法空格。
    /// </summary>
    public ImmutableArray<Coord> LastCandidateCells { get; private set; } = [];

    /// <summary>最近一次部署的单点排序（前 N）。</summary>
    public ImmutableArray<PointScore> LastPointRanking { get; private set; } = [];

    /// <summary>最近一次部署生成的候选批次（去重后，≤ M）。</summary>
    public ImmutableArray<CandidateBatch> LastCandidates { get; private set; } = [];

    /// <summary>最近一次部署的选择；尚未部署过为 <c>null</c>。</summary>
    public CandidateBatch? LastChoice { get; private set; }

    /// <summary>
    /// 最近一次部署是否走了决胜一手（ai-decision「链接模式下的决胜一手」）：走了即为所落批次的记录，否则（含尚未部署）为 <c>null</c>。
    /// 只是留痕，决策日志的格式不变（仍是 <c>D:&lt;批次&gt;</c>）。
    /// </summary>
    public DecisiveMove? LastDecisive { get; private set; }

    /// <summary>最近一次部署的前瞻记录（expert-lookahead）：前瞻宽度为 0（简单 / 标准 / 高难）时恒为 <c>null</c>。</summary>
    public LookaheadRecord? LastLookahead { get; private set; }

    /// <summary>
    /// 最近一次部署是否因"循环提子停手"（cycle-stop D3）视同无改善手：是即为留痕，否则（含尚未部署、K = 0）为 <c>null</c>。
    /// 决胜一手仍优先——停手成立但决胜一手落下时，这里有留痕、<see cref="LastDecisive"/> 也有。
    /// </summary>
    public StagnationStop? LastStagnation { get; private set; }

    /// <summary>测试接缝：最近一次采样所在的大回合（尚未采样 / K = 0 为 <c>null</c>）。</summary>
    internal int? LastSampledRound => _powerByRound.Count == 0 ? null : _powerByRound.Keys.Max();

    /// <summary>测试接缝：最近一次采样的本人公开势力。</summary>
    internal BigInteger? LastSampledPower => LastSampledRound is { } round ? _powerByRound[round] : null;

    /// <summary>测试接缝：第 <paramref name="round"/> 大回合决策时视为"最近 K 个大回合内被提过"的格（与停滞判定同一窗口）。</summary>
    internal ImmutableArray<Coord> RecentlyCapturedCells(int round) => [.. RecentlyCaptured(round)];

    /// <summary>
    /// 测试接缝：预置停滞记忆（本人某些大回合的采样势力、某些大回合观察到的被提格）。预置过的大回合不再被入口采样覆盖；占用差分照常从下一次观察起算。
    /// </summary>
    internal void SeedStagnationMemory(IEnumerable<(int Round, BigInteger Power)> powers, IEnumerable<(int Round, Coord Cell)> captured)
    {
        foreach ((int round, BigInteger power) in powers)
        {
            _powerByRound[round] = power;
        }

        foreach ((int round, Coord cell) in captured)
        {
            if (!_capturedByRound.TryGetValue(round, out HashSet<Coord>? cells))
            {
                _capturedByRound[round] = cells = [];
            }

            cells.Add(cell);
        }
    }

    /// <summary>
    /// 为当前公开快照建立评价器（供外部检视单个批次的分解）。<see cref="Deploy"/> 每次决策新建一个：活形分析的决策内缓存挂在评价器上，随之丢弃（ai-eye D5）。
    /// </summary>
    public BatchEvaluator CreateEvaluator() => CreateEvaluator(_observe());

    /// <summary>
    /// 前瞻里的模拟对手（expert-lookahead D5）：对 <paramref name="responder"/> 固定观察投影视图 <paramref name="view"/>，标准难度、不持有任何随机流。
    /// <paramref name="config"/> 须只取一条贪心（M = 1，只走 k = 0 的无扰动次序）——一旦走到扰动即抛出，零随机消费由此在结构上成立。
    /// </summary>
    internal static HeuristicTurnController ForSimulation(
        PlayerId responder, MatchPublicView view, EvaluationWeights weights, AiSearchConfig config,
        Func<GameBoard, LifeShapeReport>? lifeQuery, bool cacheLife)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(config);
        if (config.CandidateBatchCount != 1 || config.LookaheadWidth != 0 || config.DiverseSupplementLimit != 0 || config.TwoPlyWeightPermille != 0)
        {
            throw new ArgumentException("模拟对手只取一条无扰动贪心、不前瞻（也不做多样补充与两层加分）。", nameof(config));
        }

        return new HeuristicTurnController(
            responder, () => view, AiDifficulty.Standard, weights, config, relicValue: null, lifeQuery, cacheLife, perturbation: null);
    }

    private BatchEvaluator CreateEvaluator(MatchPublicView view) => new(Player, view, Weights, Config.ImmediateOnly, _relicValue, _lifeQuery, _cacheLife);

    /// <summary>
    /// 本次决策实际生效的停手阈值（pass-threshold-first-stone D1，无子豁免）：批次开始前的正式盘面上该玩家没有任何棋子时为 0，
    /// 否则为配置值 <see cref="AiSearchConfig.PassThreshold"/>。只在决策入口按公开快照算一次，单点排序与贪心组批共用，
    /// 组批过程中不因已暂放的棋子而改变。<see cref="Config"/> 与日志首部仍记配置值（D2：豁免是盘面的确定性函数，回放按同一实现重建）。
    /// <para>链接模式的落后者豁免（board-link-mode 裁决 C15，ai-decision「链接模式下落后者的停手阈值」）与无子豁免在这里同一处判定：
    /// 见 <see cref="TrailsInBattlefield"/>。专家前瞻的模拟对手（<see cref="ForSimulation"/>）走同一个 <see cref="Deploy"/>、读投影视图，
    /// 多样补充的排除重跑沿用本次算定的阈值——「前瞻中的停手口径」的复用点都经过这里，不另写一份。</para>
    /// </summary>
    private int EffectivePassThreshold(MatchPublicView view) =>
        view.Board.GroupsOf(Player).IsEmpty || TrailsInBattlefield(view, Player) ? 0 : Config.PassThreshold;

    /// <summary>
    /// 裁决 C15：链接模式下 <paramref name="player"/> 所在战场还有其他在局玩家，且其当前总势力严格低于其中某一人（并列最高视为最高）。
    /// 尚无势力快照、未锁定出生棋盘、独自在单人战场等待时一律为 <c>false</c>（沿用缺省阈值）。同战场口径与按座位投影相同：
    /// 出生棋盘落在本战场里的在局玩家（<see cref="PublicRules.BattlefieldOf"/>）。只读公开快照，按座位视野（缺省）下同战场的势力仍完整可见。
    /// </summary>
    internal static bool TrailsInBattlefield(MatchPublicView view, PlayerId player)
    {
        if (view.Power is not { } power)
        {
            return false;
        }

        // 同战场口径只经 RivalsInBattlefield 一处（ai-seat-vision check：原为同一过滤条件的内联副本，行为不变）；未锁定出生棋盘时它为空 → false。
        BigInteger mine = power.Of(player).Total;
        return RivalsInBattlefield(view, player).Any(p => power.Of(p).Total > mine);
    }

    // ---------- 第 2 阶段 ----------

    /// <summary>超限时逐类弃掉"数量 × 基础军势"最小的一类；不超限不弃。</summary>
    public void OrganizeHand(PlayerHandAccess hand, int overflow)
    {
        ArgumentNullException.ThrowIfNull(hand);
        for (int i = 0; i < overflow; i++)
        {
            HandPrivateView view = hand.PrivateView();
            PieceType? victim = null;
            long victimValue = long.MaxValue;
            foreach (PieceType type in view.Types)
            {
                long value = (long)view.CountOf(type) * PieceEffects.BasePower(type);
                if (value < victimValue)
                {
                    victim = type;
                    victimValue = value;
                }
            }

            if (victim is not { } t)
            {
                return;
            }

            hand.Discard(t);
            _decisions.Add($"O:{t}");
        }
    }

    // ---------- 第 3 阶段 ----------

    /// <summary>按类型分选满免费选取数：分数高者先，同分按候选位下标。</summary>
    public void Recruit(PlayerHandAccess hand, RecruitPanelView panel)
    {
        ArgumentNullException.ThrowIfNull(hand);
        ArgumentNullException.ThrowIfNull(panel);
        MatchPublicView view = _observe();
        var picked = new List<string>();
        while (hand.Panel().PicksRemaining > 0)
        {
            RecruitPanelView current = hand.Panel();
            HandPrivateView mine = hand.PrivateView();
            RecruitCandidateView? best = null;
            int bestScore = int.MinValue;
            foreach (RecruitCandidateView candidate in current.Candidates)
            {
                if (!candidate.IsSelectable)
                {
                    continue;
                }

                int score = RecruitScore(candidate.Type, view, mine, current);
                if (score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            if (best is null)
            {
                break;
            }

            hand.Pick(best.Index);
            picked.Add($"{best.Index}:{best.Type}");
        }

        _decisions.Add(picked.Count == 0 ? "R:-" : $"R:{string.Join(",", picked)}");
    }

    /// <summary>征募前瞻分：基础军势；非简单难度再加成长亲和（倍增 / 连珠 / 协同）与供给匹配（槽位紧张时偏好已持有类型）。</summary>
    private int RecruitScore(PieceType type, MatchPublicView view, HandPrivateView mine, RecruitPanelView panel)
    {
        int score = PieceEffects.BasePower(type);
        if (Config.ImmediateOnly)
        {
            return score;
        }

        ImmutableArray<Group> groups = view.Board.GroupsOf(Player);
        switch (type)
        {
            case PieceType.Multiplier:
                score += groups.IsEmpty ? 0 : groups.Max(g => g.Size);
                break;
            case PieceType.Line:
                score += 2 * groups.Sum(g => g.Stones.Count(s => view.Board[s].Occupant!.Value.Type == PieceType.Line));
                break;
            case PieceType.Synergy:
                score += 2 * groups.SelectMany(g => g.Stones).Select(s => view.Board[s].Occupant!.Value.Type).Distinct().Count();
                break;
            default:
                break;
        }

        if (mine.CountOf(type) > 0 && panel.OccupiedSlots >= panel.TypeSlots - 1)
        {
            score += 2;
        }

        return score;
    }

    // ---------- 第 4 阶段 ----------

    /// <summary>
    /// 单点筛选 → 贪心 + 种子扰动组合 → 确定性选优；最终把选中的批次摆回 <paramref name="batch"/>。
    /// 前瞻宽度大于 0（专家）时，"确定性选优"换成一层前瞻（<see cref="ExpertLookahead"/>）：候选生成（含扰动子流的消费）与高难完全相同，
    /// 前瞻在去重之后、复摆之前接入，只在非空候选之间重排，并留下前瞻记录（<see cref="LastLookahead"/>）。
    /// 多样补充上限大于 0 时，前瞻集不足前瞻宽度个即以排除重跑补足（expert-strength D2 / D3）：把某一格的全部单点从本次决策的原排序（k = 0，不扰动）里拿掉，
    /// 同一停手阈值、同一活形硬约束、同一部署上限下重跑一次贪心组批——贪心本身不改，不消费任何随机。
    /// </summary>
    public void Deploy(StagedBatch batch, Func<RehearsalResult> rehearse)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(rehearse);
        if (RehearsalView is { } project)
        {
            Func<RehearsalResult> real = rehearse;
            rehearse = () => project(real());
        }

        MatchPublicView view = _observe();
        ObserveForStagnation(view);
        BatchEvaluator evaluator = CreateEvaluator(view);
        int passThreshold = EffectivePassThreshold(view);
        BatchContext context = batch.Context;
        batch.Clear();

        ImmutableArray<PointScore> ranking = RankPoints(batch, rehearse, evaluator, out ImmutableArray<PointScore> ranked);
        LastPointRanking = ranking;
        LastDecisive = null;
        LastStagnation = null;
        if (ranking.IsEmpty || context.DeployLimit == 0)
        {
            LastCandidates = [];
            LastChoice = null;
            LastLookahead = Config.LookaheadWidth > 0 ? LookaheadRecord.Passed : null;
            _decisions.Add("D:pass");
            return;
        }

        CandidateBatch choice;
        // 循环提子停手（cycle-stop D3，ai-decision「循环提子停手」）：停滞成立即视同"没有任何落点被保留"——不组批、不扰动（K = 0 时整段不进入，
        // 一次额外预演、一次额外随机消费都没有，与引入之前逐步相同）；Pass 候选照常进下面的决胜一手检查，决胜一手优先。
        if (Config.StagnationRounds > 0 && StagnationHolds(view, ranking) is { } stagnation)
        {
            LastStagnation = stagnation;
            LastCandidates = [];
            LastLookahead = Config.LookaheadWidth > 0 ? LookaheadRecord.Passed : null;
            choice = new CandidateBatch([], evaluator.Evaluate([], rehearse(), context));
        }
        else
        {
            choice = ChooseByCandidates(ranking, batch, rehearse, evaluator, passThreshold, view);
        }

        // 决胜一手（ai-pass-review 2.1，P7 = A 窄版）：只在"本将 Pass、这次 Pass 会让本战场整轮 Pass 结算、自己落后"时检查；
        // 条件不满足时这里什么都不算，决策与引入之前逐字节相同。不消费任何随机流。
        if (choice.IsPass && DecisiveTriggered(view, Player) && DecisiveMoveFor(view, ranked, ranking, batch, rehearse, evaluator) is { } decisive)
        {
            choice = decisive.Choice;
            LastDecisive = decisive.Record;
        }

        LastChoice = choice;

        batch.Clear();
        foreach (Placement placement in choice.Placements)
        {
            // 复摆 MUST 带上改造目标：漏了会静默丢掉改造（匠人照落，地形不动），而候选评分是按带改造算的。
            if (batch.Stage(placement.Coord, placement.Type, placement.Edit) is { } failure)
            {
                throw new SiegeRuleException($"AI 复摆已预演通过的批次被拒绝：{failure.Message}");
            }
        }

        // stagnation-trace D1（ai-decision「循环提子停手」留痕）：停滞成立且最终 Pass 记 D:stall，决胜一手接管照常记落点；只改条目文案，不改任何决策与随机消费。
        _decisions.Add(choice.IsPass ? (LastStagnation is null ? "D:pass" : StallDecision) : $"D:{choice.Key}");
    }

    /// <summary>
    /// 候选生成与选优（<see cref="Deploy"/> 原有的中段，cycle-stop 2.2 原样抽出、行为不变）：贪心 + 种子扰动组合出不超过 M 个候选批次，
    /// 前瞻宽度大于 0 时交给一层前瞻重排，否则确定性选优。
    /// </summary>
    private CandidateBatch ChooseByCandidates(
        ImmutableArray<PointScore> ranking, StagedBatch batch, Func<RehearsalResult> rehearse, BatchEvaluator evaluator, int passThreshold, MatchPublicView view)
    {
        var candidates = new List<CandidateBatch>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (int k = 0; k < Config.CandidateBatchCount; k++)
        {
            ImmutableArray<PointScore> order = k == 0 ? ranking : Perturb(ranking);
            CandidateBatch candidate = Greedy(order, batch, rehearse, evaluator, passThreshold);
            if (keys.Add(candidate.Key))
            {
                candidates.Add(candidate);
            }
        }

        LastCandidates = [.. candidates];
        CandidateBatch choice;
        if (Config.LookaheadWidth > 0)
        {
            int supplementRehearsals = 0;
            RehearsalResult CountedRehearse()
            {
                supplementRehearsals++;
                return rehearse();
            }

            CandidateBatch Rerun(Coord excluded) =>
                Greedy([.. ranking.Where(p => p.Coord != excluded)], batch, CountedRehearse, evaluator, passThreshold);

            var lookahead = new ExpertLookahead(Player, view, evaluator, Weights, Config, _lifeQuery, _cacheLife);
            (choice, LookaheadRecord record) = lookahead.Choose(candidates, batch, rehearse, Config.DiverseSupplementLimit > 0 ? Rerun : null);
            LastLookahead = supplementRehearsals == 0 ? record : record with { SupplementRehearsals = supplementRehearsals };
        }
        else
        {
            choice = CandidateSelection.Best(candidates);
            LastLookahead = null;
        }

        return choice;
    }

    // ---------- 循环提子停手（cycle-stop D3；ai-decision「循环提子停手」） ----------

    /// <summary>
    /// 部署决策入口的观察（K &gt; 0 才做）：本人在本大回合的首次决策采样一次公开势力；与上一次观察比对占用，由有子变为空或换了主人的格归档为"本大回合观察到被提过"。
    /// 只读公开快照（盘面占用、势力榜），不读任何隐藏量；不消费随机流。<see cref="ReviewBestMove"/>（复盘）不经这里，不改记忆。
    /// </summary>
    private void ObserveForStagnation(MatchPublicView view)
    {
        if (Config.StagnationRounds <= 0)
        {
            return;
        }

        int round = view.MajorRound;
        if (!_powerByRound.ContainsKey(round))
        {
            _powerByRound[round] = view.Power?.Of(Player).Total ?? BigInteger.Zero;
        }

        var now = new Dictionary<Coord, PlayerId>();
        foreach (Coord cell in view.Board.AllCoords())
        {
            if (view.Board[cell].Occupant is { } occupant)
            {
                now[cell] = occupant.Owner;
            }
        }

        if (_lastOccupancy is not null)
        {
            foreach ((Coord cell, PlayerId owner) in _lastOccupancy)
            {
                if (!now.TryGetValue(cell, out PlayerId current) || current != owner)
                {
                    if (!_capturedByRound.TryGetValue(round, out HashSet<Coord>? cells))
                    {
                        _capturedByRound[round] = cells = [];
                    }

                    cells.Add(cell);
                }
            }
        }

        _lastOccupancy = now;
    }

    /// <summary>
    /// 复盘用的停滞判定（<see cref="ReviewBestMove"/>）：在本人停滞记忆的副本上做一次与 <see cref="Deploy"/> 入口相同的观察再判定，结束后记忆原样还原——
    /// 复盘 MUST NOT 改变对局决策（ai-pass-review「复盘不改对局」）。K = 0 为 <c>null</c>。
    /// </summary>
    private StagnationStop? PreviewStagnation(MatchPublicView view, ImmutableArray<PointScore> ranking)
    {
        if (Config.StagnationRounds <= 0)
        {
            return null;
        }

        var powers = new SortedDictionary<int, BigInteger>(_powerByRound);
        var captured = _capturedByRound.ToDictionary(kv => kv.Key, kv => new HashSet<Coord>(kv.Value));
        Dictionary<Coord, PlayerId>? last = _lastOccupancy;
        try
        {
            ObserveForStagnation(view);
            return StagnationHolds(view, ranking);
        }
        finally
        {
            _powerByRound.Clear();
            foreach ((int round, BigInteger power) in powers)
            {
                _powerByRound[round] = power;
            }

            _capturedByRound.Clear();
            foreach ((int round, HashSet<Coord> cells) in captured)
            {
                _capturedByRound[round] = cells;
            }

            _lastOccupancy = last;
        }
    }

    /// <summary>
    /// 停滞判定（cycle-stop D3）：盘上有己方棋子（无子豁免沿用）；最近 K + 1 个大回合（本大回合及之前 K 个）都采样过且最近 K 个大回合的采样逐一不高于前一个
    /// （连续 K 个大回合无净增，与 <c>CycleAnalyzer.StagnationOf</c> 同口径——那边取上一大回合结束时的势力，这里取本人在该大回合首次决策时的势力，相差一个采样点）；
    /// 且 <paramref name="ranking"/>（排序后的前 N 个候选单点）每一个要么会提子、要么落在最近 K 个大回合（本大回合及之前 K − 1 个）内观察到被提过的格上。
    /// 成立返回留痕，否则 <c>null</c>。只读本次候选与本人记忆。
    /// </summary>
    private StagnationStop? StagnationHolds(MatchPublicView view, ImmutableArray<PointScore> ranking)
    {
        int k = Config.StagnationRounds;
        int round = view.MajorRound;
        if (view.Board.GroupsOf(Player).IsEmpty)
        {
            return null;
        }

        var powers = new BigInteger[k + 1];
        for (int i = 0; i <= k; i++)
        {
            if (!_powerByRound.TryGetValue(round - k + i, out powers[i]))
            {
                return null;
            }
        }

        for (int i = 1; i <= k; i++)
        {
            if (powers[i] > powers[i - 1])
            {
                return null;
            }
        }

        SortedSet<Coord> recent = RecentlyCaptured(round);
        foreach (PointScore point in ranking)
        {
            if (point.Captures == 0 && !recent.Contains(point.Coord))
            {
                return null;
            }
        }

        return new StagnationStop(k, [.. powers], [.. recent], [.. ranking.Select(p => p.Coord).Distinct()]);
    }

    /// <summary>最近 K 个大回合（第 <paramref name="round"/> 大回合及之前 K − 1 个）内观察到被提过的格（坐标序）。</summary>
    private SortedSet<Coord> RecentlyCaptured(int round)
    {
        var recent = new SortedSet<Coord>();
        foreach ((int observed, HashSet<Coord> cells) in _capturedByRound)
        {
            if (observed > round - Config.StagnationRounds && observed <= round)
            {
                recent.UnionWith(cells);
            }
        }

        return recent;
    }

    /// <summary>
    /// 停手复盘（ai-pass-review 1.1，规格 match-telemetry「战场落败复盘与等待者评估」）：在本玩家即将部署的局面上，按<b>正式决策的同一实现</b>
    /// （同一观察视图与预演口径、同一评价器、同一实际停手阈值、同一候选格预筛 / 单点枚举 / 活形硬约束 / 单点排序）取"最好一手"，
    /// 并在该手的预演结果上重算势力，判断对手不回应时能否追平或反超本战场其他在局玩家的最高势力（"本可翻盘"）。只诊断，不做任何决定。
    /// </summary>
    /// <remarks>
    /// <para>最好一手 = 单点排序第一名：它的"评价收益"（单点总分 − Pass 总分）正是贪心组批加第一枚时与停手阈值比较的那个量。
    /// 排序第一名的收益不过阈值时，贪心在任何扰动次序下都从空批次起步、每一枚都不过阈值，正式决策必然 Pass；反之必然落子——
    /// 所以复盘只需 k = 0 的无扰动排序，<b>不消费扰动子流</b>（也不需要它的副本），对局逐字节不变。</para>
    /// <para>另给一条"强制落子批次"：同一排序前 N、同一贪心组批（<see cref="Greedy"/>），但把保留条件放宽到"预演合法且未被活形硬约束淘汰即保留"，
    /// 直到部署上限——回答"若不许 Pass，AI 自己的首选落法能否翻盘"。</para>
    /// <para>势力口径与评价器相同（批次开始前已揭示的信物内容，<see cref="BatchEvaluator.PowerOf"/>）：本批首次揭示的计分信物不计入，偏保守；
    /// "对手不回应"偏乐观（design Risks）。</para>
    /// <para><paramref name="batch"/> 应是与正式暂放批次分离的影子批次（同一盘面与上下文），<paramref name="rehearse"/> 对它做零副作用预演；
    /// 本方法只在其上暂放 / 清空，不写 <see cref="Decisions"/>、<c>Last*</c> 留痕与任何随机流。</para>
    /// </remarks>
    public BestMoveReview ReviewBestMove(StagedBatch batch, Func<RehearsalResult> rehearse)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(rehearse);
        if (RehearsalView is { } project)
        {
            Func<RehearsalResult> real = rehearse;
            rehearse = () => project(real());
        }

        MatchPublicView view = _observe();
        BatchEvaluator evaluator = CreateEvaluator(view);
        int passThreshold = EffectivePassThreshold(view);
        BatchContext context = batch.Context;
        batch.Clear();

        int stock = context.Stock.Values.Sum();
        int legalCells = context.LegalRange.Count(c => batch.Board[c].IsPlayableEmpty);
        BigInteger passTotal = evaluator.Evaluate([], rehearse(), context).Total;
        var rejections = new SortedDictionary<string, int>(StringComparer.Ordinal);
        (ImmutableArray<Coord> cells, List<PointScore> points) = ScorePoints(batch, rehearse, evaluator, rejections);
        ImmutableArray<PointScore> ranked = [.. Rank(points)];

        ImmutableArray<PlayerId> rivals = RivalsInBattlefield(view, Player);
        BigInteger powerBefore = evaluator.Before.Of(Player).Total;
        BigInteger? rivalBefore = rivals.IsEmpty ? null : rivals.Max(r => evaluator.Before.Of(r).Total);

        MoveOutcome Project(ImmutableArray<Placement> placements, BigInteger total) =>
            Outcome(placements, total - passTotal, batch, rehearse, evaluator, rivals);

        MoveOutcome? best = null;
        int captureCandidates = 0, comebackCandidates = 0;
        foreach (PointScore point in ranked)
        {
            MoveOutcome outcome = Project([point.Placement], point.Total);
            best ??= outcome;
            captureCandidates += outcome.Captures > 0 ? 1 : 0;
            comebackCandidates += outcome.ComesBack == true ? 1 : 0;
        }

        MoveOutcome? forced = null;
        if (!ranked.IsEmpty && context.DeployLimit > 0)
        {
            CandidateBatch candidate = Greedy([.. ranked.Take(Config.CandidatePointCount)], batch, rehearse, evaluator, int.MinValue);
            forced = candidate.IsPass ? null : Project(candidate.Placements, candidate.Total);
        }

        // cycle-stop D3：正式决策在停滞成立时视同无改善手 Pass——复盘按同一判定给出（只读预演，不改本人停滞记忆）。
        StagnationStop? stagnation = ranked.IsEmpty || context.DeployLimit == 0 ? null : PreviewStagnation(view, [.. ranked.Take(Config.CandidatePointCount)]);

        batch.Clear();
        return new BestMoveReview(
            Player, passThreshold, context.DeployLimit, stock, legalCells, cells.Length, ranked.Length,
            powerBefore, rivalBefore, best, forced, captureCandidates, comebackCandidates)
        {
            Rejections = rejections.ToImmutableSortedDictionary(StringComparer.Ordinal),
            Stagnation = stagnation,
        };
    }

    /// <summary>
    /// 匠人改造诊断（ai-terrain-review 1.1，规格 match-telemetry「AI 改造与主题诊断」第 1 条）：在本玩家即将部署的局面上，按<b>正式决策的同一实现</b>
    /// （同一观察视图与预演口径、同一评价器、同一候选格预筛 / 单点枚举 / 活形硬约束 / 单点排序）统计立栅 / 搭桥 / 烧林三类改造各自的
    /// 规则合法目标数、进入完整枚举的目标数、合法候选数与最好一手，并给出排序第一名。只诊断，不做任何决定。
    /// </summary>
    /// <remarks>
    /// <para>三层计数（每类各一份，口径见 <see cref="ArtisanKindReview"/>）：① 合法落子范围内<b>全部</b>可落子空格上的规则合法目标（唯一实现
    /// <see cref="TerrainEditRules.LegalTargets(MapData, Coord, bool)"/>，不预演）；② 其中落在"进入完整枚举的格"（<see cref="PrefilterCells"/> 之后）上的目标；
    /// ③ ② 里经暂放、预演与活形硬约束后留下的匠人单点（<see cref="ScorePoints"/> 的产物）。① &gt; 0 而 ② = 0 即"候选生成阶段排除"。</para>
    /// <para>预筛外的格（① − ②）上的搭桥 / 烧林另经同一个 <see cref="ScoreOne"/> 打分（<see cref="ArtisanKindReview.Outside"/>），回答"若不被预筛掉，评价分是多少"；
    /// 立栅每格 16 条边，开销大，不做这一项。</para>
    /// <para>与 <see cref="ReviewBestMove"/> 同：只走 k = 0 的无扰动排序，<b>不消费扰动子流</b>；<paramref name="batch"/> 应是影子批次，
    /// 本方法只在其上暂放 / 清空，不写 <see cref="Decisions"/>、<c>Last*</c> 留痕与任何随机流。实际选择由调用方在正式决策之后读 <see cref="LastChoice"/>。</para>
    /// </remarks>
    public ArtisanEditReview ReviewArtisanEdits(StagedBatch batch, Func<RehearsalResult> rehearse)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(rehearse);
        if (RehearsalView is { } project)
        {
            Func<RehearsalResult> real = rehearse;
            rehearse = () => project(real());
        }

        MatchPublicView view = _observe();
        BatchEvaluator evaluator = CreateEvaluator(view);
        int passThreshold = EffectivePassThreshold(view);
        BatchContext context = batch.Context;
        batch.Clear();

        MapData map = batch.Board.Map;
        ImmutableArray<Coord> legal = [.. context.LegalRange.Where(c => batch.Board[c].IsPlayableEmpty).Order()];
        BigInteger passTotal = evaluator.Evaluate([], rehearse(), context).Total;
        (ImmutableArray<Coord> cells, List<PointScore> points) = ScorePoints(batch, rehearse, evaluator);
        ImmutableArray<PointScore> ranked = [.. Rank(points)];
        var enumerated = new HashSet<Coord>(cells);
        bool holdsEditor = context.Stock.GetValueOrDefault(TerrainEditRules.EditorType) > 0;

        (PointScore Point, int Rank)? FirstOf(Func<PointScore, bool> match)
        {
            for (int i = 0; i < ranked.Length; i++)
            {
                if (match(ranked[i]))
                {
                    return (ranked[i], i + 1);
                }
            }

            return null;
        }

        var kinds = ImmutableArray.CreateBuilder<ArtisanKindReview>(3);
        foreach (TerrainEditKind kind in ArtisanEditReview.Kinds)
        {
            int rangeTargets = 0, enumeratedTargets = 0;
            var outside = new List<PointScore>();
            foreach (Coord cell in legal)
            {
                ImmutableArray<TerrainEdit> targets = [.. TerrainEditRules.LegalTargets(map, cell, context.WorkshopActive, context.EditScope).Where(e => e.Kind == kind)];
                rangeTargets += targets.Length;
                if (enumerated.Contains(cell))
                {
                    enumeratedTargets += targets.Length;
                    continue;
                }

                if (holdsEditor && kind != TerrainEditKind.Fence)
                {
                    foreach (TerrainEdit edit in targets)
                    {
                        if (ScoreOne(batch, rehearse, evaluator, cell, TerrainEditRules.EditorType, edit, rejections: null) is { } point)
                        {
                            outside.Add(point);
                        }
                    }
                }
            }

            int candidates = points.Count(p => p.Type == TerrainEditRules.EditorType && p.Edit?.Kind == kind);
            (PointScore Point, int Rank)? best = FirstOf(p => p.Type == TerrainEditRules.EditorType && p.Edit?.Kind == kind);
            PointScore? outsideBest = Rank(outside).FirstOrDefault();
            PointScore? plain = best is { } bb ? points.FirstOrDefault(p => p.Coord == bb.Point.Coord && p.Type == TerrainEditRules.EditorType && p.Edit is null) : null;
            kinds.Add(new ArtisanKindReview(
                kind, rangeTargets, enumeratedTargets, candidates,
                best is { } b ? new ArtisanOption(b.Point.Placement, b.Point.Total - passTotal, b.Rank) : null,
                kind == TerrainEditKind.Fence ? null : outside.Count,
                outsideBest is null ? null : new ArtisanOption(outsideBest.Placement, outsideBest.Total - passTotal, Rank: null))
            {
                EditEffect = best is { } e && plain is not null
                    ? [.. Enum.GetValues<EvaluationDimension>().Select(d => e.Point.Evaluation.ContributionOf(d) - plain.Evaluation.ContributionOf(d))]
                    : [],
            });
        }

        batch.Clear();
        (PointScore Point, int Rank)? none = FirstOf(p => p.Type == TerrainEditRules.EditorType && p.Edit is null);
        PointScore? top = ranked.IsEmpty ? null : ranked[0];
        return new ArtisanEditReview(
            Player, passThreshold, context.DeployLimit, context.Stock.GetValueOrDefault(TerrainEditRules.EditorType), legal.Length, cells.Length,
            Config.CandidatePointCount, ranked.Length,
            top is null ? null : new ArtisanOption(top.Placement, top.Total - passTotal, 1),
            none is { } n ? new ArtisanOption(n.Point.Placement, n.Point.Total - passTotal, n.Rank) : null,
            kinds.MoveToImmutable());
    }

    /// <summary>
    /// 决胜一手的触发条件（ai-decision「链接模式下的决胜一手」）：本战场其他在局玩家在本大回合都已 Pass（本次 Pass 会让本战场整轮 Pass 而结算）；
    /// 自己不是本战场势力最高（并列最高视为最高，与 C15 同一判定 <see cref="TrailsInBattlefield"/>）。只读公开快照。
    /// </summary>
    internal static bool DecisiveTriggered(MatchPublicView view, PlayerId player)
    {
        if (!TrailsInBattlefield(view, player))
        {
            return false;
        }

        ImmutableArray<PlayerId> rivals = RivalsInBattlefield(view, player);
        return !rivals.IsEmpty && rivals.All(view.PassedThisRound.Contains);
    }

    /// <summary>
    /// 决胜一手的选择：在单点排序的<b>全部</b>合法单点（按排序次序）与"同一排序前 N 的强制贪心批次（合法即保留，至部署上限）"之中，
    /// 取预演后"己方势力 − 本战场其他在局者最高势力"最大且 ≥ 0 者；并列取先出现者（单点按排序在前，强制批次排最后）。没有即 <c>null</c>（照常 Pass）。
    /// 候选、预演、活形硬约束与势力计算全部复用停手复盘的同一路径（<see cref="Outcome"/>、<see cref="Greedy"/>、<see cref="BatchEvaluator.PowerOf"/>），
    /// 不看评价分（P9）。不消费任何随机流。
    /// </summary>
    private (CandidateBatch Choice, DecisiveMove Record)? DecisiveMoveFor(
        MatchPublicView view, ImmutableArray<PointScore> ranked, ImmutableArray<PointScore> ranking, StagedBatch batch, Func<RehearsalResult> rehearse, BatchEvaluator evaluator)
    {
        ImmutableArray<PlayerId> rivals = RivalsInBattlefield(view, Player);
        BatchContext context = batch.Context;
        batch.Clear();
        BigInteger passTotal = evaluator.Evaluate([], rehearse(), context).Total;
        var options = new List<(CandidateBatch Batch, bool Forced)>(ranked.Length + 1);
        options.AddRange(ranked.Select(p => (new CandidateBatch([p.Placement], p.Evaluation), false)));
        CandidateBatch forced = Greedy(ranking, batch, rehearse, evaluator, int.MinValue);
        if (!forced.IsPass)
        {
            options.Add((forced, true));
        }

        (CandidateBatch Batch, bool Forced, MoveOutcome Outcome)? best = null;
        BigInteger bestMargin = BigInteger.MinusOne;
        foreach ((CandidateBatch option, bool isForced) in options)
        {
            MoveOutcome outcome = Outcome(option.Placements, option.Total - passTotal, batch, rehearse, evaluator, rivals);
            BigInteger margin = outcome.PowerAfter - outcome.RivalAfter!.Value;
            if (margin >= 0 && (best is null || margin > bestMargin))
            {
                best = (option, isForced, outcome);
                bestMargin = margin;
            }
        }

        batch.Clear();
        if (best is not { } chosen)
        {
            return null;
        }

        BigInteger rivalBefore = rivals.Max(r => evaluator.Before.Of(r).Total);
        return (chosen.Batch, new DecisiveMove(chosen.Outcome, chosen.Forced, evaluator.Before.Of(Player).Total, rivalBefore));
    }

    /// <summary>
    /// 把一手摆上暂放批次、零副作用预演，并按评价器势力口径算落下后己方与本战场对手最高的势力。停手复盘与决胜一手共用这一份。
    /// 落点都来自已通过预演与活形硬约束的候选，复摆被拒即响亮失败。结束时批次里留着这一手，调用方负责清空。
    /// </summary>
    private MoveOutcome Outcome(
        ImmutableArray<Placement> placements, BigInteger gain, StagedBatch batch, Func<RehearsalResult> rehearse, BatchEvaluator evaluator, ImmutableArray<PlayerId> rivals)
    {
        batch.Clear();
        foreach (Placement placement in placements)
        {
            if (batch.Stage(placement.Coord, placement.Type, placement.Edit) is { } failure)
            {
                throw new SiegeRuleException($"复摆已预演通过的落点被拒绝：{failure.Message}");
            }
        }

        RehearsalResult result = rehearse();
        GameBoard after = result.ProjectedBoard ?? throw new SiegeRuleException("候选预演没有结算后盘面。");
        PowerSnapshot power = evaluator.PowerOf(after);
        BigInteger? rivalAfter = rivals.IsEmpty ? null : rivals.Max(r => power.Of(r).Total);
        return new MoveOutcome(placements, gain, result.Captures.Length, power.Of(Player).Total, rivalAfter);
    }

    /// <summary>
    /// 与 <see cref="TrailsInBattlefield"/> 同一战场口径：出生棋盘落在 <paramref name="player"/> 所在战场里的其他在局玩家（升序）。未锁定出生棋盘为空。
    /// 只读公开项（在局状态、出生棋盘、地形划出的战场），全知与按座位视野下结果相同。同战场口径的唯一实现：C15（<see cref="TrailsInBattlefield"/>）、决胜一手与专家前瞻的名册（ai-seat-vision 裁决 V2）都经它。
    /// </summary>
    internal static ImmutableArray<PlayerId> RivalsInBattlefield(MatchPublicView view, PlayerId player)
    {
        if (view.Players.FirstOrDefault(p => p.Player == player)?.BirthZone is not int zone)
        {
            return [];
        }

        Battlefield field = PublicRules.BattlefieldOf(view.Board, zone);
        return [.. view.Players
            .Where(p => p.Player != player && p.IsActive && p.BirthZone is int z && field.BirthZones.Contains(z))
            .Select(p => p.Player)
            .Order()];
    }

    /// <summary>
    /// 单点评价：每个合法空格 × 每种持有类型 × 改造选项各预演一次，取总分前 N（同分按坐标、再按类型、再按改造记法）。
    /// 预演合法之后先过活形硬约束（<see cref="BatchEvaluator.TryEvaluate"/>），被淘汰的候选不打分、不进排行。
    /// 配置了候选格上限 K 且合法空格多于 K 时，先经 <see cref="PrefilterCells"/> 把"每个合法空格"收窄到 K 格。
    /// </summary>
    /// <remarks>
    /// 改造不新增评估维度（design D-J）：匠人的每个合法改造目标只是多一个候选暂放，
    /// "提子""通路""覆盖变化"由既有的 <see cref="EvaluationWeights"/> 维度经预演结果自然反映。
    /// </remarks>
    private ImmutableArray<PointScore> RankPoints(StagedBatch batch, Func<RehearsalResult> rehearse, BatchEvaluator evaluator, out ImmutableArray<PointScore> ranked)
    {
        (ImmutableArray<Coord> cells, List<PointScore> points) = ScorePoints(batch, rehearse, evaluator);
        LastCandidateCells = cells;
        ranked = [.. Rank(points)];
        return [.. ranked.Take(Config.CandidatePointCount)];
    }

    /// <summary>
    /// 单点排序的确定性次序（总分降序 → 坐标 → 类型 → 改造记法）。<see cref="RankPoints"/> 与停手复盘（<see cref="ReviewBestMove"/>）共用这一份，
    /// 复盘的"最好一手"因此就是正式决策单点排序的第一名。
    /// </summary>
    private static IOrderedEnumerable<PointScore> Rank(IEnumerable<PointScore> points) => points
        .OrderByDescending(p => p.Total)
        .ThenBy(p => p.Coord)
        .ThenBy(p => p.Type)
        .ThenBy(p => p.Edit?.ToString() ?? string.Empty, StringComparer.Ordinal);

    /// <summary>
    /// 单点评价的枚举本体（<see cref="RankPoints"/> 的前半段，ai-pass-review 1.1 抽出、行为不变）：返回进入完整枚举的格与全部通过预演和活形硬约束的单点（未排序、未截断）。
    /// 不写 <see cref="LastCandidateCells"/>——那是正式决策的留痕，复盘 MUST NOT 改它。不消费任何随机流。
    /// </summary>
    /// <param name="rejections">复盘用的落选计数（暂放被拒 / 预演不合法按失败种类、活形硬约束淘汰）；正式决策传 <c>null</c>，只计数、不改变任何分支。</param>
    private (ImmutableArray<Coord> Cells, List<PointScore> Points) ScorePoints(
        StagedBatch batch, Func<RehearsalResult> rehearse, BatchEvaluator evaluator, SortedDictionary<string, int>? rejections = null)
    {
        BatchContext context = batch.Context;
        ImmutableArray<PieceType> types = [.. context.Stock.Where(kv => kv.Value > 0).Select(kv => kv.Key).Order()];
        ImmutableArray<Coord> cells = [.. context.LegalRange.Where(c => batch.Board[c].IsPlayableEmpty).Order()];
        if (Config.CandidateCellLimit > 0 && cells.Length > Config.CandidateCellLimit && !types.IsEmpty)
        {
            cells = PrefilterCells(cells, types, batch, rehearse, evaluator, Config.CandidateCellLimit);
        }

        var points = new List<PointScore>();
        foreach (Coord cell in cells)
        {
            foreach (PieceType type in types)
            {
                foreach (TerrainEdit? edit in EditOptions(batch.Board.Map, cell, type, context.WorkshopActive, context.EditScope))
                {
                    if (ScoreOne(batch, rehearse, evaluator, cell, type, edit, rejections) is { } point)
                    {
                        points.Add(point);
                    }
                }
            }
        }

        batch.Clear();
        return (cells, points);
    }

    /// <summary>
    /// 一个单点（格 × 类型 × 改造选项）的打分：暂放 → 零副作用预演 → 活形硬约束 → 九维评价。<see cref="ScorePoints"/> 的循环体（ai-terrain-review 1.1 原样抽出、行为不变），
    /// 改造诊断（<see cref="ReviewArtisanEdits"/>）对预筛外的格复用同一份。落选返回 <c>null</c> 并按原因计入 <paramref name="rejections"/>（可为 <c>null</c>）。
    /// 结束时批次里可能留着这一枚，调用方负责清空（与抽出之前相同：下一次调用先 <c>Clear</c>）。
    /// </summary>
    private static PointScore? ScoreOne(
        StagedBatch batch, Func<RehearsalResult> rehearse, BatchEvaluator evaluator, Coord cell, PieceType type, TerrainEdit? edit, SortedDictionary<string, int>? rejections)
    {
        BatchContext context = batch.Context;
        batch.Clear();
        if (batch.Stage(cell, type, edit) is { } refused)
        {
            Tally(rejections, $"暂放被拒:{refused.Kind}");
            return null;
        }

        RehearsalResult result = rehearse();

        // 预演合法之后、打分之前：活形硬约束淘汰的候选不打分、不进排行（ai-eye D3）。
        if (!result.IsLegal || !evaluator.TryEvaluate(batch.Placements, result, context, out EvaluationBreakdown? evaluation))
        {
            Tally(rejections, result.IsLegal ? "活形硬约束" : $"预演不合法:{result.Failure?.Kind}");
            return null;
        }

        return new PointScore(cell, type, edit, evaluation, result.Captures.Length);

        static void Tally(SortedDictionary<string, int>? counts, string key)
        {
            if (counts is not null)
            {
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        }
    }

    /// <summary>
    /// 候选格预筛（frontier-map 裁决 12）：用代表类型（持有类型里枚举序最前的一种——枚举序即固定类型次序：普通、堡垒、连珠、倍增、协同、匠人、
    /// 旗手、铁链、哨兵、界碑，more-pieces-relics D9）、不带改造，对每格预演一次得格分，
    /// 取前 K（<paramref name="cellLimit"/>）格（同分按坐标序），按坐标序返回。不消费随机流。
    /// 专家的两层扫描（<see cref="ExpertLookahead.BestSingleGain"/>）复用同一实现（expert-strength D4：候选格上限按单点排序同一预筛口径）。
    /// 格分只算既有七维、不做活形查询（ai-eye D5，<see cref="BatchEvaluator.EvaluatePrefilter"/>）；进入完整枚举的 ≤ K 格才算九维。
    /// 代表类型在某格落不下（自杀手等）而手里有匠人时，该格退而用匠人逐个合法改造目标预演，格分取其中最高的合法总分。
    /// </summary>
    /// <remarks>
    /// <para>提子点与救命点不另设豁免：提子走"敌方损失"维、补气走"安全"维（预筛口径下安全维只看气数、分散气与危险，见 <see cref="GroupSafety.AnalyzeWithoutLife"/>），两维都与落下的类型无关，代表类型的格分已经把它们排在前面
    /// （由 <c>候选格上限Tests.小K下仍找到妙手</c> 守门）。</para>
    /// <para>匠人回退：自杀手判定与类型无关（六种类型在盘面上气的口径相同），不带改造时代表类型落不下的格，别的类型同样落不下；
    /// 唯一的例外是匠人的改造先于自杀手判定（terrain-edit T-3：搭桥补气、立栅切断敌串）。回退的目标枚举与完整枚举同样带本小回合快照的工坊标记。这类"只有带改造的匠人才落得下"的格
    /// 不回退就会被预筛整格漏掉。回退只发生在代表类型落不下的格上（通常寥寥数个），开销可忽略；枚举次序即
    /// <see cref="TerrainEditRules.LegalTargets"/> 的确定性次序。</para>
    /// <para>仍有的偏差：代表类型落得下的格只按"不带改造"计分，改造带来的额外收益不进格分（那是完整枚举的事）；
    /// 同形禁则比对的盘面序列化含棋子类型，代表类型因同形被拒而别的类型不被拒的格会漏掉（极罕见，未处理）。</para>
    /// </remarks>
    internal static ImmutableArray<Coord> PrefilterCells(
        ImmutableArray<Coord> cells, ImmutableArray<PieceType> types, StagedBatch batch, Func<RehearsalResult> rehearse, BatchEvaluator evaluator, int cellLimit)
    {
        BatchContext context = batch.Context;
        PieceType representative = types[0];
        bool holdsEditor = types.Contains(TerrainEditRules.EditorType);
        var scored = new List<(Coord Cell, BigInteger Total)>();

        BigInteger? Score(Coord cell, PieceType type, TerrainEdit? edit)
        {
            batch.Clear();
            if (batch.Stage(cell, type, edit) is not null)
            {
                return null;
            }

            RehearsalResult result = rehearse();
            return result.IsLegal ? evaluator.EvaluatePrefilter(batch.Placements, result, context).Total : null;
        }

        foreach (Coord cell in cells)
        {
            BigInteger? total = Score(cell, representative, null);
            if (total is null && holdsEditor)
            {
                foreach (TerrainEdit edit in TerrainEditRules.LegalTargets(batch.Board.Map, cell, context.WorkshopActive, context.EditScope))
                {
                    if (Score(cell, TerrainEditRules.EditorType, edit) is { } edited && (total is null || edited > total))
                    {
                        total = edited;
                    }
                }
            }

            if (total is { } best)
            {
                scored.Add((cell, best));
            }
        }

        batch.Clear();
        return [.. scored
            .OrderByDescending(s => s.Total)
            .ThenBy(s => s.Cell)
            .Take(cellLimit)
            .Select(s => s.Cell)
            .Order()];
    }

    /// <summary>
    /// 该落点该类型要枚举的改造选项（design D-J）：匠人为"不改造 + 全部合法目标"，其余九种只有"不改造"。
    /// 合法目标经 <see cref="TerrainEditRules.LegalTargets(MapData, Coord, bool)"/> 取得（唯一实现），AI MUST NOT 自己判目标合法性，
    /// 也 MUST NOT 自行扩展或收窄范围：<paramref name="workshop"/> 是本小回合快照的工坊标记（经批次上下文传入，more-pieces-relics D5），
    /// 为真时隔一格的格目标由唯一实现一并给出。
    /// </summary>
    private static IEnumerable<TerrainEdit?> EditOptions(MapData map, Coord cell, PieceType type, bool workshop, IReadOnlySet<Coord>? scope)
    {
        yield return null;
        if (type != TerrainEditRules.EditorType)
        {
            yield break;
        }

        foreach (TerrainEdit edit in TerrainEditRules.LegalTargets(map, cell, workshop, scope))
        {
            yield return edit;
        }
    }

    /// <summary>
    /// 按给定顺序贪心加入落点：预演不合法、整批被活形硬约束淘汰、或这一枚使总分的提升<b>不大于</b>停手阈值
    /// （<paramref name="passThreshold"/>：决策入口按正式盘面算定的实际阈值，见 <see cref="EffectivePassThreshold"/>；ai-eye D4）即撤回；直到部署上限。
    /// 阈值作用在每一枚的边际提升上，不是整批总分。阈值 0 即"严格提高"：零收益的落子一律不下——这是 AI 会 Pass、
    /// 对局能以整轮 Pass 收尾的前提（实测 <c>&gt;=</c> 会让对局 600 小回合不终局）。
    /// </summary>
    private CandidateBatch Greedy(
        ImmutableArray<PointScore> order, StagedBatch batch, Func<RehearsalResult> rehearse, BatchEvaluator evaluator, int passThreshold)
    {
        BatchContext context = batch.Context;
        batch.Clear();
        EvaluationBreakdown current = evaluator.Evaluate([], rehearse(), context);
        foreach (PointScore point in order)
        {
            if (batch.Count >= context.DeployLimit)
            {
                break;
            }

            // 同一落点只取排序里最靠前的那个改造选项：后面那些"同格不同改造"的候选在这里被跳过。
            if (batch.Placements.Any(p => p.Coord == point.Coord) || batch.Stage(point.Coord, point.Type, point.Edit) is not null)
            {
                continue;
            }

            // 预演不合法、或整批被活形硬约束淘汰（ai-eye D3：批次层判——同批次另有提子即放行）→ 撤回这一枚。
            RehearsalResult result = rehearse();
            if (!result.IsLegal || !evaluator.TryEvaluate(batch.Placements, result, context, out EvaluationBreakdown? next))
            {
                batch.Unstage(point.Coord);
                continue;
            }

            if (next.Total - current.Total > passThreshold)
            {
                current = next;
            }
            else
            {
                batch.Unstage(point.Coord);
            }
        }

        var candidate = new CandidateBatch(batch.Placements, current);
        batch.Clear();
        return candidate;
    }

    /// <summary>种子驱动的局部扰动：位置 i 与 [i, i + 窗口) 内的一个位置交换。消费次数只取决于 N。</summary>
    private ImmutableArray<PointScore> Perturb(ImmutableArray<PointScore> ranking)
    {
        RandomStream perturbation = _perturbation ?? throw new InvalidOperationException("前瞻的模拟对手不持有随机流，不得扰动（expert-lookahead D5）。");
        PointScore[] list = [.. ranking];
        for (int i = 0; i < list.Length - 1; i++)
        {
            int j = i + perturbation.NextInt(Math.Min(PerturbationWindow, list.Length - i));
            (list[i], list[j]) = (list[j], list[i]);
        }

        return [.. list];
    }

    // ---------- 第 5 阶段 ----------

    /// <summary>候选已全部预演过，被拒绝极罕见；补救策略是撤掉最后一枚再试，空批次即 Pass。</summary>
    public bool OnRejected(StagedBatch batch, BatchFailure failure)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(failure);
        _decisions.Add($"X:{failure.Kind}");
        if (batch.Count == 0)
        {
            return false;
        }

        batch.Unstage(batch.Placements[^1].Coord);
        return true;
    }
}
