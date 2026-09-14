using System.Collections.Immutable;
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
    private readonly RandomStream _perturbation;
    private readonly Func<RelicPublicState, int>? _relicValue;
    private readonly List<string> _decisions = [];

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

    /// <summary>调试旁路（<c>internal</c>）：<paramref name="relicValue"/> 可读真实信物内容。正式构造不暴露此参数。</summary>
    internal HeuristicTurnController(
        PlayerId player,
        Func<MatchPublicView> observe,
        RandomStream perturbation,
        AiDifficulty difficulty,
        EvaluationWeights? weights,
        AiSearchConfig? config,
        Func<RelicPublicState, int>? relicValue)
    {
        Player = player;
        _observe = observe ?? throw new ArgumentNullException(nameof(observe));
        _perturbation = perturbation ?? throw new ArgumentNullException(nameof(perturbation));
        Difficulty = difficulty;
        Weights = weights ?? EvaluationWeights.Default;
        Config = (config ?? AiSearchConfig.ForDifficulty(difficulty)).Validated();
        _relicValue = relicValue;
    }

    public PlayerId Player { get; }

    public AiDifficulty Difficulty { get; }

    public EvaluationWeights Weights { get; }

    public AiSearchConfig Config { get; }

    /// <summary>决策日志（每次弃牌 / 选取 / 部署一行），供可复现性断言与跑局日志。</summary>
    public IReadOnlyList<string> Decisions => _decisions;

    /// <summary>最近一次部署的单点排序（前 N）。</summary>
    public ImmutableArray<PointScore> LastPointRanking { get; private set; } = [];

    /// <summary>最近一次部署生成的候选批次（去重后，≤ M）。</summary>
    public ImmutableArray<CandidateBatch> LastCandidates { get; private set; } = [];

    /// <summary>最近一次部署的选择；尚未部署过为 <c>null</c>。</summary>
    public CandidateBatch? LastChoice { get; private set; }

    /// <summary>为当前公开快照建立评价器（供外部检视单个批次的分解）。</summary>
    public BatchEvaluator CreateEvaluator() => new(Player, _observe(), Weights, Config.ImmediateOnly, _relicValue);

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

    /// <summary>单点筛选 → 贪心 + 种子扰动组合 → 确定性选优；最终把选中的批次摆回 <paramref name="batch"/>。</summary>
    public void Deploy(StagedBatch batch, Func<RehearsalResult> rehearse)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(rehearse);
        BatchEvaluator evaluator = CreateEvaluator();
        BatchContext context = batch.Context;
        batch.Clear();

        ImmutableArray<PointScore> ranking = RankPoints(batch, rehearse, evaluator);
        LastPointRanking = ranking;
        if (ranking.IsEmpty || context.DeployLimit == 0)
        {
            LastCandidates = [];
            LastChoice = null;
            _decisions.Add("D:pass");
            return;
        }

        var candidates = new List<CandidateBatch>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (int k = 0; k < Config.CandidateBatchCount; k++)
        {
            ImmutableArray<PointScore> order = k == 0 ? ranking : Perturb(ranking);
            CandidateBatch candidate = Greedy(order, batch, rehearse, evaluator);
            if (keys.Add(candidate.Key))
            {
                candidates.Add(candidate);
            }
        }

        LastCandidates = [.. candidates];
        CandidateBatch choice = CandidateSelection.Best(candidates);
        LastChoice = choice;

        batch.Clear();
        foreach (Placement placement in choice.Placements)
        {
            if (batch.Stage(placement.Coord, placement.Type) is { } failure)
            {
                throw new SiegeRuleException($"AI 复摆已预演通过的批次被拒绝：{failure.Message}");
            }
        }

        _decisions.Add(choice.IsPass ? "D:pass" : $"D:{choice.Key}");
    }

    /// <summary>单点评价：每个合法空格 × 每种持有类型各预演一次，取总分前 N（同分按坐标、再按类型）。</summary>
    private ImmutableArray<PointScore> RankPoints(StagedBatch batch, Func<RehearsalResult> rehearse, BatchEvaluator evaluator)
    {
        BatchContext context = batch.Context;
        ImmutableArray<PieceType> types = [.. context.Stock.Where(kv => kv.Value > 0).Select(kv => kv.Key).Order()];
        ImmutableArray<Coord> cells = [.. context.LegalRange.Where(c => batch.Board[c].IsPlayableEmpty).Order()];
        var points = new List<PointScore>();
        foreach (Coord cell in cells)
        {
            foreach (PieceType type in types)
            {
                batch.Clear();
                if (batch.Stage(cell, type) is not null)
                {
                    continue;
                }

                RehearsalResult result = rehearse();
                if (!result.IsLegal)
                {
                    continue;
                }

                points.Add(new PointScore(cell, type, evaluator.Evaluate(batch.Placements, result, context)));
            }
        }

        batch.Clear();
        return [.. points
            .OrderByDescending(p => p.Total)
            .ThenBy(p => p.Coord)
            .ThenBy(p => p.Type)
            .Take(Config.CandidatePointCount)];
    }

    /// <summary>
    /// 按给定顺序贪心加入落点：预演不合法或总分<b>没有严格提高</b>即撤回；直到部署上限。
    /// 零收益的落子一律不下——这是 AI 会 Pass、对局能以整轮 Pass 收尾的前提（实测 <c>&gt;=</c> 会让对局 600 小回合不终局）。
    /// </summary>
    private static CandidateBatch Greedy(
        ImmutableArray<PointScore> order, StagedBatch batch, Func<RehearsalResult> rehearse, BatchEvaluator evaluator)
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

            if (batch.Placements.Any(p => p.Coord == point.Coord) || batch.Stage(point.Coord, point.Type) is not null)
            {
                continue;
            }

            RehearsalResult result = rehearse();
            if (!result.IsLegal)
            {
                batch.Unstage(point.Coord);
                continue;
            }

            EvaluationBreakdown next = evaluator.Evaluate(batch.Placements, result, context);
            if (next.Total > current.Total)
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
        PointScore[] list = [.. ranking];
        for (int i = 0; i < list.Length - 1; i++)
        {
            int j = i + _perturbation.NextInt(Math.Min(PerturbationWindow, list.Length - i));
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
