using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Relics;
using Siege.Core.Scoring;

namespace Siege.Core.Ai;

/// <summary>
/// 九维启发式评价（设计文档 §15.2；ai-eye 追加眼位、威胁）。以一次预演结果（<see cref="RehearsalResult.ProjectedBoard"/>）为"落子后盘面"，
/// 与公开快照里的"落子前盘面"逐维求差——每一维都是增量，MUST NOT 取结算后的绝对值。
/// 提子、军势、覆盖全部由下层算出（<see cref="PowerCalculator"/>）；眼、眼值与活形状态只来自 <see cref="LifeShapeReport"/>，本类不重造规则。
/// </summary>
/// <remarks>
/// 输入只有 <see cref="MatchPublicView"/> 与本人的 <see cref="BatchContext"/>：结构上没有手牌数量、征募面板与未揭示信物内容。
/// 未揭示信物的价值经 <see cref="RelicEstimate"/> 的分区先验估计；调试 AI 可经 <c>internal</c> 构造传入读真实内容的估值函数。
/// </remarks>
public sealed class BatchEvaluator
{
    /// <summary>每提一枚敌子在"敌方损失"维度上的附加原始分。</summary>
    public const int CapturePerStone = 2;

    /// <summary>眼位维度里每条己方已确定活形棋串的附加原始分（ai-eye D1，沿用基准文档系数）。</summary>
    public const int AliveGroupEyeBonus = 3;

    private readonly PlayerId _me;
    private readonly EvaluationWeights _weights;
    private readonly bool _immediateOnly;
    private readonly Func<RelicPublicState, int> _relicValue;
    private readonly ImmutableSortedDictionary<PlayerId, PlayerStatus> _roster;
    private readonly ImmutableArray<RelicPublicState> _relics;
    private readonly PowerSnapshot _before;
    private readonly long _relicBefore;
    private readonly long _safetyBefore;
    private readonly long _growthBefore;
    private readonly long _eyeBefore;
    private readonly long _threatBefore;
    private readonly int? _rankBefore;
    private readonly ImmutableArray<Coord> _ownAliveStones;
    private readonly ImmutableHashSet<Coord> _ownSingleEyes;
    private readonly Func<GameBoard, LifeShapeReport> _lifeQuery;
    private readonly Dictionary<string, LifeShapeReport>? _lifeCache;
    private readonly long _safetyBeforeWithoutLife;

    public BatchEvaluator(PlayerId me, MatchPublicView view, EvaluationWeights weights, bool immediateOnly)
        : this(me, view, weights, immediateOnly, relicValue: null)
    {
    }

    /// <summary>
    /// 调试 AI 旁路：<paramref name="relicValue"/> 可读真实内容。正式 AI 走公开构造，估值只能来自 <see cref="RelicEstimate.Estimate"/>。
    /// 测试接缝：<paramref name="lifeQuery"/> 替换活形查询（计数桩；缺省即 <see cref="LifeShapeReport.Analyze"/>），
    /// <paramref name="cacheLife"/> 为 <c>false</c> 时关闭决策内缓存（开 / 关比对用；缓存 MUST NOT 影响结果）。
    /// </summary>
    internal BatchEvaluator(
        PlayerId me,
        MatchPublicView view,
        EvaluationWeights weights,
        bool immediateOnly,
        Func<RelicPublicState, int>? relicValue,
        Func<GameBoard, LifeShapeReport>? lifeQuery = null,
        bool cacheLife = true)
    {
        ArgumentNullException.ThrowIfNull(view);
        _me = me;
        _weights = weights ?? throw new ArgumentNullException(nameof(weights));
        _immediateOnly = immediateOnly;
        _relicValue = relicValue ?? RelicEstimate.Estimate;
        _lifeQuery = lifeQuery ?? LifeShapeReport.Analyze;
        _lifeCache = cacheLife ? new Dictionary<string, LifeShapeReport>(StringComparer.Ordinal) : null;
        _roster = view.Players.ToImmutableSortedDictionary(p => p.Player, p => p.Status);
        _relics = view.Relics;
        _before = PowerCalculator.Compute(view.Board, _roster);
        _rankBefore = _before.RankOf(me);
        _relicBefore = RelicScore(_before.Coverage);

        // 批次开始前的活形查询：活形硬约束与眼位维（全部难度，眼位下放到简单难度见 ai-eye R26）、安全 / 威胁两维（非简单难度）共用，每个盘面一次。
        LifeShapeReport life = Life(view.Board);
        _ownAliveStones = [.. life.Groups
            .Where(g => g.Group.Owner == me && g.Life == LifeState.Alive)
            .SelectMany(g => g.Group.Stones)
            .Order()];
        _ownSingleEyes = [.. life.EyeSpaces.Where(e => e.Owner == me && e.Cells.Length == 1).Select(e => e.Cells[0])];
        _eyeBefore = EyeOf(life);
        if (!immediateOnly)
        {
            _safetyBefore = SafetyOf(view.Board, life);
            _threatBefore = ThreatOf(view.Board, life);
            _safetyBeforeWithoutLife = SafetyWithoutLifeOf(view.Board);
        }

        _growthBefore = GrowthOf(view.Board);
    }

    /// <summary>落子前的势力快照（由本类按公开盘面全量算出）。</summary>
    public PowerSnapshot Before => _before;

    /// <summary>被评价的玩家。</summary>
    public PlayerId Player => _me;

    /// <summary>是否只评价即时收益与眼位（简单难度，ai-eye R26）。</summary>
    public bool ImmediateOnly => _immediateOnly;

    /// <summary>评价一个已预演合法的候选批次（完整口径：九维）。空批次（Pass）除供给维度外全为 0。</summary>
    public EvaluationBreakdown Evaluate(ImmutableArray<Placement> placements, RehearsalResult result, BatchContext context) =>
        Evaluate(placements, result, context, prefilter: false);

    /// <summary>
    /// 候选格预筛口径（ai-eye D5、段 C）：只算既有七维，<b>不做活形查询</b>——眼位、威胁两维记 0；安全维按
    /// <see cref="GroupSafety.AnalyzeWithoutLife"/>（眼值之和记 0、不视为已确定活形，前后同一口径取差）。其余五维与完整口径逐项相同。
    /// 只用来给候选格排名、收窄到 K 格；进入完整枚举的格一律按 <see cref="Evaluate(ImmutableArray{Placement}, RehearsalResult, BatchContext)"/> 打分。
    /// </summary>
    internal EvaluationBreakdown EvaluatePrefilter(ImmutableArray<Placement> placements, RehearsalResult result, BatchContext context) =>
        Evaluate(placements, result, context, prefilter: true);

    private EvaluationBreakdown Evaluate(ImmutableArray<Placement> placements, RehearsalResult result, BatchContext context, bool prefilter)
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
        PowerSnapshot afterPower = PowerCalculator.Compute(after, _roster);

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
            raw[(int)EvaluationDimension.Growth] = checked(GrowthOf(after) - _growthBefore);
            raw[(int)EvaluationDimension.Initiative] = InitiativeShift(afterPower);
            if (prefilter)
            {
                raw[(int)EvaluationDimension.Safety] = checked(SafetyWithoutLifeOf(after) - _safetyBeforeWithoutLife);
            }
        }

        // 眼位对全部难度生效（ai-eye R26：简单难度 = 即时势力增量、敌方损失、眼位三维）；预筛口径一律记 0、不做活形查询（D5）。
        if (!prefilter)
        {
            LifeShapeReport life = Life(after);
            raw[(int)EvaluationDimension.Eye] = checked(EyeOf(life) - _eyeBefore);
            if (!_immediateOnly)
            {
                raw[(int)EvaluationDimension.Safety] = checked(SafetyOf(after, life) - _safetyBefore);
                raw[(int)EvaluationDimension.Threat] = checked(ThreatOf(after, life) - _threatBefore);
            }
        }

        return new EvaluationBreakdown([.. raw], _weights);
    }

    /// <summary>
    /// 活形硬约束 + 评价（ai-eye D3）：对一个已预演合法的候选，先判是否被活形硬约束淘汰，未被淘汰才打分。
    /// 淘汰即返回 <c>false</c>、<paramref name="evaluation"/> 为 <c>null</c>——被淘汰的候选 MUST NOT 以扣分的形式留在候选集里。
    /// 单点排序与贪心组批都经这里；对全部难度生效（简单难度同样淘汰，只是打分只用即时势力增量、敌方损失、眼位三维）。
    /// </summary>
    public bool TryEvaluate(
        ImmutableArray<Placement> placements, RehearsalResult result, BatchContext context, [NotNullWhen(true)] out EvaluationBreakdown? evaluation)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (ViolatesLifeConstraint(placements, result))
        {
            evaluation = null;
            return false;
        }

        evaluation = Evaluate(placements, result, context);
        return true;
    }

    /// <summary>
    /// 活形硬约束的判据（ai-eye D3）：本批次<b>无提子</b>，且下列任一成立——
    /// ① 批次开始前己方某条已确定活形棋串，其任一棋子在预演结算后所在的棋串不再是已确定活形（按棋子归属判，与规则层「破坏活形」同一口径，只是对象是己方）；
    /// ② 任一落点是批次开始前己方的一个<b>单格眼</b>（格数为 1 的眼空间）。
    /// 落进己方多格眼空间不淘汰（直三点中间是在做眼），交给眼位维度与停手阈值。有提子即放行（裁决 R1），交给打分。
    /// 眼与活形只来自 <see cref="LifeShapeReport"/>。
    /// </summary>
    private bool ViolatesLifeConstraint(ImmutableArray<Placement> placements, RehearsalResult result)
    {
        if (!result.IsLegal || result.IsPass || result.ProjectedBoard is null || placements.IsDefaultOrEmpty || !result.Captures.IsEmpty)
        {
            return false;
        }

        foreach (Placement placement in placements)
        {
            if (_ownSingleEyes.Contains(placement.Coord))
            {
                return true;
            }
        }

        if (_ownAliveStones.IsEmpty)
        {
            return false;
        }

        LifeShapeReport after = Life(result.ProjectedBoard);
        foreach (Coord stone in _ownAliveStones)
        {
            if (after.GroupLifeAt(stone) is not { Life: LifeState.Alive } life || life.Group.Owner != _me)
            {
                return true;
            }
        }

        return false;
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

    /// <summary>
    /// 活形查询（ai-eye D5 决策内缓存）：同一盘面在本次决策里只分析一次——单点排序、贪心组批的多个扰动次序、硬约束判据与评价
    /// 会反复到达同一盘面（同格不同类型、同一前缀批次）。本类由 <see cref="HeuristicTurnController.Deploy"/> 每次决策新建一个，
    /// 缓存随之丢弃，MUST NOT 跨决策；缓存只省重复计算，MUST NOT 影响结果（开 / 关缓存决策序列逐步相同）。
    /// </summary>
    private LifeShapeReport Life(GameBoard board)
    {
        if (_lifeCache is null)
        {
            return _lifeQuery(board);
        }

        string key = Fingerprint(board);
        if (!_lifeCache.TryGetValue(key, out LifeShapeReport? report))
        {
            report = _lifeQuery(board);
            _lifeCache.Add(key, report);
        }

        return report;
    }

    /// <summary>
    /// 活形分析的盘面指纹：逐格的占用者（不含棋子类型——活形查询只读棋串归属与气边，棋串归属不看类型）+ 对局中完成的地形改造（规范序）。
    /// 地形改造 MUST 在内：同一格落匠人、带不带改造，占用者网格相同而气边不同。只在单次决策内成立——底图（<see cref="GameBoard.BaseMap"/>）
    /// 不进指纹，同一次决策里它恒为同一份；跨对局两张不同的图可以有相同指纹，这也是缓存不得跨决策的原因之一。
    /// </summary>
    private static string Fingerprint(GameBoard board)
    {
        var sb = new StringBuilder((board.Width * board.Height) + 16);
        foreach (Coord c in board.AllCoords())
        {
            sb.Append(board[c].Occupant is { } occupant ? (char)('A' + occupant.Owner.Value) : '.');
        }

        ImmutableArray<TerrainEdit> edits = board.TerrainEdits;
        if (!edits.IsEmpty)
        {
            sb.Append('|').AppendJoin(',', edits.Select(e => e.ToString()).Order(StringComparer.Ordinal));
        }

        return sb.ToString();
    }

    /// <summary>维度 4 的预筛口径：己方全部棋串按 <see cref="GroupSafety.AnalyzeWithoutLife"/> 的安全分之和（不查活形）。</summary>
    private long SafetyWithoutLifeOf(GameBoard board)
    {
        long total = 0;
        foreach (Group group in board.GroupsOf(_me))
        {
            total += GroupSafety.AnalyzeWithoutLife(board, group).Score;
        }

        return total;
    }

    /// <summary>维度 4 的盘面总分：己方全部棋串的安全分之和（已确定活形取常数，见 <see cref="GroupSafety.AliveScore"/>）。</summary>
    private long SafetyOf(GameBoard board, LifeShapeReport life)
    {
        long total = 0;
        foreach (GroupLife group in life.Groups)
        {
            if (group.Group.Owner == _me)
            {
                total += GroupSafety.Analyze(board, group).Score;
            }
        }

        return total;
    }

    /// <summary>
    /// 维度 8 的盘面总分：己方眼空间的眼值之和（每个眼空间只计一次——一块眼空间只有一个所有者，但可同时是多条己方棋串的眼空间）
    /// + 己方已确定活形棋串数 × <see cref="AliveGroupEyeBonus"/>。
    /// </summary>
    private long EyeOf(LifeShapeReport life)
    {
        long total = 0;
        foreach (EyeSpace space in life.EyeSpaces)
        {
            if (space.Owner == _me)
            {
                total += space.EyeValue;
            }
        }

        foreach (GroupLife group in life.Groups)
        {
            if (group.Group.Owner == _me && group.Life == LifeState.Alive)
            {
                total += AliveGroupEyeBonus;
            }
        }

        return total;
    }

    /// <summary>
    /// 维度 9 的盘面总分：参赛敌方的<b>非</b>已确定活形、气数 ≤ <see cref="GroupSafety.DangerLiberties"/> 的棋串棋子总数。
    /// 已确定活形提不动，MUST 排除（ai-eye D2）；"参赛敌方"与维度 2 敌方损失同一口径（弃赛 / 出局者的遗留棋子不计）。
    /// </summary>
    private long ThreatOf(GameBoard board, LifeShapeReport life)
    {
        long total = 0;
        foreach (GroupLife group in life.Groups)
        {
            PlayerId owner = group.Group.Owner;
            if (owner == _me || group.Life == LifeState.Alive
                || !_roster.TryGetValue(owner, out PlayerStatus status) || status != PlayerStatus.Active)
            {
                continue;
            }

            if (board.LibertiesOf(group.Group).Length <= GroupSafety.DangerLiberties)
            {
                total += group.Group.Size;
            }
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
