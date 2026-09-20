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
            // 复摆 MUST 带上改造目标：漏了会静默丢掉改造（匠人照落，地形不动），而候选评分是按带改造算的。
            if (batch.Stage(placement.Coord, placement.Type, placement.Edit) is { } failure)
            {
                throw new SiegeRuleException($"AI 复摆已预演通过的批次被拒绝：{failure.Message}");
            }
        }

        _decisions.Add(choice.IsPass ? "D:pass" : $"D:{choice.Key}");
    }

    /// <summary>
    /// 单点评价：每个合法空格 × 每种持有类型 × 改造选项各预演一次，取总分前 N（同分按坐标、再按类型、再按改造记法）。
    /// 配置了候选格上限 K 且合法空格多于 K 时，先经 <see cref="PrefilterCells"/> 把"每个合法空格"收窄到 K 格。
    /// </summary>
    /// <remarks>
    /// 改造不新增评估维度（design D-J）：匠人的每个合法改造目标只是多一个候选暂放，
    /// "提子""通路""覆盖变化"由既有的 <see cref="EvaluationWeights"/> 维度经预演结果自然反映。
    /// </remarks>
    private ImmutableArray<PointScore> RankPoints(StagedBatch batch, Func<RehearsalResult> rehearse, BatchEvaluator evaluator)
    {
        BatchContext context = batch.Context;
        ImmutableArray<PieceType> types = [.. context.Stock.Where(kv => kv.Value > 0).Select(kv => kv.Key).Order()];
        ImmutableArray<Coord> cells = [.. context.LegalRange.Where(c => batch.Board[c].IsPlayableEmpty).Order()];
        if (Config.CandidateCellLimit > 0 && cells.Length > Config.CandidateCellLimit && !types.IsEmpty)
        {
            cells = PrefilterCells(cells, types, batch, rehearse, evaluator);
        }

        LastCandidateCells = cells;
        var points = new List<PointScore>();
        foreach (Coord cell in cells)
        {
            foreach (PieceType type in types)
            {
                foreach (TerrainEdit? edit in EditOptions(batch.Board.Map, cell, type))
                {
                    batch.Clear();
                    if (batch.Stage(cell, type, edit) is not null)
                    {
                        continue;
                    }

                    RehearsalResult result = rehearse();
                    if (!result.IsLegal)
                    {
                        continue;
                    }

                    points.Add(new PointScore(cell, type, edit, evaluator.Evaluate(batch.Placements, result, context)));
                }
            }
        }

        batch.Clear();
        return [.. points
            .OrderByDescending(p => p.Total)
            .ThenBy(p => p.Coord)
            .ThenBy(p => p.Type)
            .ThenBy(p => p.Edit?.ToString() ?? string.Empty, StringComparer.Ordinal)
            .Take(Config.CandidatePointCount)];
    }

    /// <summary>
    /// 候选格预筛（frontier-map 裁决 12）：用代表类型（持有类型里枚举序最前的一种）、不带改造，对每格预演一次得格分，
    /// 取前 K 格（同分按坐标序），按坐标序返回。评估函数原样复用；不消费随机流。
    /// 代表类型在某格落不下（自杀手等）而手里有匠人时，该格退而用匠人逐个合法改造目标预演，格分取其中最高的合法总分。
    /// </summary>
    /// <remarks>
    /// <para>提子点与救命点不另设豁免：提子走"敌方损失"维、补气走"安全"维，两维都与落下的类型无关，代表类型的格分已经把它们排在前面
    /// （由 <c>候选格上限Tests.小K下仍找到妙手</c> 守门）。</para>
    /// <para>匠人回退：自杀手判定与类型无关（六种类型在盘面上气的口径相同），不带改造时代表类型落不下的格，别的类型同样落不下；
    /// 唯一的例外是匠人的改造先于自杀手判定（terrain-edit T-3：搭桥补气、立栅切断敌串）。这类"只有带改造的匠人才落得下"的格
    /// 不回退就会被预筛整格漏掉。回退只发生在代表类型落不下的格上（通常寥寥数个），开销可忽略；枚举次序即
    /// <see cref="TerrainEditRules.LegalTargets"/> 的确定性次序。</para>
    /// <para>仍有的偏差：代表类型落得下的格只按"不带改造"计分，改造带来的额外收益不进格分（那是完整枚举的事）；
    /// 同形禁则比对的盘面序列化含棋子类型，代表类型因同形被拒而别的类型不被拒的格会漏掉（极罕见，未处理）。</para>
    /// </remarks>
    private ImmutableArray<Coord> PrefilterCells(
        ImmutableArray<Coord> cells, ImmutableArray<PieceType> types, StagedBatch batch, Func<RehearsalResult> rehearse, BatchEvaluator evaluator)
    {
        BatchContext context = batch.Context;
        PieceType representative = types[0];
        bool holdsEditor = types.Contains(TerrainEditRules.EditorType);
        var scored = new List<(Coord Cell, long Total)>();

        long? Score(Coord cell, PieceType type, TerrainEdit? edit)
        {
            batch.Clear();
            if (batch.Stage(cell, type, edit) is not null)
            {
                return null;
            }

            RehearsalResult result = rehearse();
            return result.IsLegal ? evaluator.Evaluate(batch.Placements, result, context).Total : null;
        }

        foreach (Coord cell in cells)
        {
            long? total = Score(cell, representative, null);
            if (total is null && holdsEditor)
            {
                foreach (TerrainEdit edit in TerrainEditRules.LegalTargets(batch.Board.Map, cell))
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
            .Take(Config.CandidateCellLimit)
            .Select(s => s.Cell)
            .Order()];
    }

    /// <summary>
    /// 该落点该类型要枚举的改造选项（design D-J）：匠人为"不改造 + 全部合法目标"，其余五种只有"不改造"。
    /// 合法目标经 <see cref="TerrainEditRules.LegalTargets"/> 取得（唯一实现），AI MUST NOT 自己判目标合法性。
    /// </summary>
    private static IEnumerable<TerrainEdit?> EditOptions(MapData map, Coord cell, PieceType type)
    {
        yield return null;
        if (type != TerrainEditRules.EditorType)
        {
            yield break;
        }

        foreach (TerrainEdit edit in TerrainEditRules.LegalTargets(map, cell))
        {
            yield return edit;
        }
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

            // 同一落点只取排序里最靠前的那个改造选项：后面那些"同格不同改造"的候选在这里被跳过。
            if (batch.Placements.Any(p => p.Coord == point.Coord) || batch.Stage(point.Coord, point.Type, point.Edit) is not null)
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
