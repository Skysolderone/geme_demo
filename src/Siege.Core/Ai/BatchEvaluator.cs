using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Relics;
using Siege.Core.Scoring;

namespace Siege.Core.Ai;

/// <summary>
/// 七维启发式评价（设计文档 §15.2）。以一次预演结果（<see cref="RehearsalResult.ProjectedBoard"/>）为"落子后盘面"，
/// 与公开快照里的"落子前盘面"逐维求差。提子、军势、覆盖全部由下层算出（<see cref="PowerCalculator"/>），本类不重造规则。
/// </summary>
/// <remarks>
/// 输入只有 <see cref="MatchPublicView"/> 与本人的 <see cref="BatchContext"/>：结构上没有手牌数量、征募面板与未揭示信物内容。
/// 未揭示信物的价值经 <see cref="RelicEstimate"/> 的分区先验估计；调试 AI 可经 <c>internal</c> 构造传入读真实内容的估值函数。
/// </remarks>
public sealed class BatchEvaluator
{
    /// <summary>每提一枚敌子在"敌方损失"维度上的附加原始分。</summary>
    public const int CapturePerStone = 2;

    private readonly PlayerId _me;
    private readonly EvaluationWeights _weights;
    private readonly bool _immediateOnly;
    private readonly SiteValues _siteValues;
    private readonly Func<RelicPublicState, int> _relicValue;
    private readonly ImmutableSortedDictionary<PlayerId, PlayerStatus> _roster;
    private readonly ImmutableArray<RelicPublicState> _relics;
    private readonly PowerSnapshot _before;
    private readonly long _relicBefore;
    private readonly long _safetyBefore;
    private readonly long _growthBefore;
    private readonly int? _rankBefore;

    public BatchEvaluator(PlayerId me, MatchPublicView view, EvaluationWeights weights, bool immediateOnly)
        : this(me, view, weights, immediateOnly, relicValue: null)
    {
    }

    /// <summary>调试 AI 旁路：<paramref name="relicValue"/> 可读真实内容。正式 AI 走公开构造，估值只能来自 <see cref="RelicEstimate.Estimate"/>。</summary>
    internal BatchEvaluator(
        PlayerId me, MatchPublicView view, EvaluationWeights weights, bool immediateOnly, Func<RelicPublicState, int>? relicValue)
    {
        ArgumentNullException.ThrowIfNull(view);
        _me = me;
        _weights = weights ?? throw new ArgumentNullException(nameof(weights));
        _immediateOnly = immediateOnly;
        _relicValue = relicValue ?? RelicEstimate.Estimate;
        _roster = view.Players.ToImmutableSortedDictionary(p => p.Player, p => p.Status);
        _relics = view.Relics;
        _siteValues = view.SiteValues;
        _before = PowerCalculator.Compute(view.Board, _roster, _siteValues);
        _rankBefore = _before.RankOf(me);
        _relicBefore = RelicScore(_before.Coverage);
        _safetyBefore = SafetyOf(view.Board);
        _growthBefore = GrowthOf(view.Board);
    }

    /// <summary>落子前的势力快照（由本类按公开盘面全量算出）。</summary>
    public PowerSnapshot Before => _before;

    /// <summary>被评价的玩家。</summary>
    public PlayerId Player => _me;

    /// <summary>是否只评价即时收益（简单难度）。</summary>
    public bool ImmediateOnly => _immediateOnly;

    /// <summary>评价一个已预演合法的候选批次。空批次（Pass）除供给维度外全为 0。</summary>
    public EvaluationBreakdown Evaluate(ImmutableArray<Placement> placements, RehearsalResult result, BatchContext context)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(context);
        if (!result.IsLegal)
        {
            throw new ArgumentException("只评价合法的预演结果。", nameof(result));
        }

        var raw = new BigInteger[EvaluationBreakdown.DimensionCount];
        int placed = placements.IsDefault ? 0 : placements.Length;
        if (!_immediateOnly)
        {
            raw[(int)EvaluationDimension.Supply] = SupplyMatch(placed, context);
        }

        if (result.IsPass || result.ProjectedBoard is null)
        {
            return new EvaluationBreakdown([.. raw], _weights);
        }

        GameBoard after = result.ProjectedBoard;
        PowerSnapshot afterPower = PowerCalculator.Compute(after, _roster, _siteValues);

        raw[(int)EvaluationDimension.PowerGain] = afterPower.Of(_me).Total - _before.Of(_me).Total;

        BigInteger enemyLoss = (BigInteger)result.Captures.Length * CapturePerStone;
        foreach ((PlayerId player, PlayerStatus status) in _roster)
        {
            if (player != _me && status == PlayerStatus.Active)
            {
                enemyLoss += _before.Of(player).Total - afterPower.Of(player).Total;
            }
        }

        raw[(int)EvaluationDimension.EnemyLoss] = enemyLoss;

        if (!_immediateOnly)
        {
            raw[(int)EvaluationDimension.Relic] = checked(RelicScore(afterPower.Coverage) - _relicBefore);
            raw[(int)EvaluationDimension.Safety] = checked(SafetyOf(after) - _safetyBefore);
            raw[(int)EvaluationDimension.Growth] = checked(GrowthOf(after) - _growthBefore);
            raw[(int)EvaluationDimension.Initiative] = InitiativeShift(afterPower);
        }

        return new EvaluationBreakdown([.. raw], _weights);
    }

    /// <summary>
    /// 维度 6：名次数字变小即上升（设计文档 §11.2：先手值 = 参赛人数 − 势力名次 + 修正，名次每升一位先手值 +1）。
    /// </summary>
    private long InitiativeShift(PowerSnapshot afterPower)
    {
        int? after = afterPower.RankOf(_me);
        return _rankBefore is int before && after is int a ? before - a : 0;
    }

    /// <summary>
    /// 维度 7：落完后剩余库存不够下回合部署上限的缺口，取负；库存充足为 0。只罚不奖——
    /// 若按落子数给正分，AI 会为凑分填自己的领地与眼位，对局无法收敛（实测 600 小回合不终局）。
    /// </summary>
    private static long SupplyMatch(int placed, BatchContext context)
    {
        long stock = 0;
        foreach (int count in context.Stock.Values)
        {
            stock += count;
        }

        long remaining = stock - placed;
        return -Math.Max(0, context.DeployLimit - remaining);
    }

    /// <summary>
    /// 维度 3 的盘面总分：己方控制（占据或独占覆盖）+V，参赛中的敌方控制 −V，争议 / 中立 / 弃赛者封锁 0；
    /// 未揭示信物首次进入任何覆盖（将被揭示）再 +V/2 作为发现价值。V 由估值函数给出。
    /// </summary>
    private long RelicScore(CoverageMap coverage)
    {
        long score = 0;
        foreach (RelicPublicState relic in _relics)
        {
            int value = _relicValue(relic);
            CellOwnership ownership = coverage.OwnershipOf(relic.Coord);
            if (ownership.Owner is { } holder && _roster.TryGetValue(holder, out PlayerStatus status) && status == PlayerStatus.Active)
            {
                score += holder == _me ? value : -value;
            }

            if (!relic.IsRevealed && ownership.Kind != OwnershipKind.Neutral && ownership.Kind != OwnershipKind.Obstacle)
            {
                score += value / 2;
            }
        }

        return score;
    }

    /// <summary>维度 4 的盘面总分：己方全部棋串的安全分之和。</summary>
    private long SafetyOf(GameBoard board)
    {
        long total = 0;
        foreach (Group group in board.GroupsOf(_me))
        {
            total += GroupSafety.Analyze(board, group).Score;
        }

        return total;
    }

    /// <summary>维度 5 的盘面总分：倍增子数 × 棋串大小 + 连珠子数 × 2 + 协同加值，对己方全部棋串求和。</summary>
    private long GrowthOf(GameBoard board)
    {
        long total = 0;
        foreach (Group group in board.GroupsOf(_me))
        {
            int lineStones = 0;
            foreach (Coord stone in group.Stones)
            {
                if (board[stone].Occupant!.Value.Type == PieceType.Line)
                {
                    lineStones++;
                }
            }

            total += ((long)PieceEffects.MultiplierCount(board, group) * group.Size)
                + (lineStones * 2L)
                + PieceEffects.SynergyBonus(board, group);
        }

        return total;
    }
}
