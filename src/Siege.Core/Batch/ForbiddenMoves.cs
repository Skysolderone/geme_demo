using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Batch;

/// <summary>单子禁手的类别：合法性预演第 6–8 步的三种失败（先后即优先级）。</summary>
public enum ForbiddenMoveKind
{
    /// <summary>破坏活形（预演第 6 步）。</summary>
    BreaksLife,

    /// <summary>自杀手（预演第 7 步）。</summary>
    Suicide,

    /// <summary>同形（预演第 8 步）。</summary>
    Superko,
}

/// <summary>
/// 一个禁手格及其原因。只带指向时要说明的量：同形带重复的历史提交序号，破坏活形带活形所有者。
/// 结构上<b>不含</b>暂放批次（不引用 <see cref="Placement"/> / <see cref="BatchFailure"/>）——它会进入默认棋盘格视图。
/// </summary>
public sealed record ForbiddenMove(Coord Coord, ForbiddenMoveKind Kind, int? DuplicateOfSequence = null, PlayerId? LifeOwner = null);

/// <summary>
/// 单子禁手查询的结果（tactical-layers「单子禁手的标示」）。<see cref="Moves"/> 按坐标序。
/// <see cref="CandidateCount"/> / <see cref="FullRehearsalCount"/> 是查询自身的口径留痕：候选格总数与其中走了完整预演的格数。
/// </summary>
public sealed record ForbiddenMoveReport(PlayerId Player, ImmutableArray<ForbiddenMove> Moves, int CandidateCount, int FullRehearsalCount)
{
    /// <summary>某格的禁手；不是禁手（含不是候选格）为 <c>null</c>。</summary>
    public ForbiddenMove? At(Coord coord)
    {
        foreach (ForbiddenMove move in Moves)
        {
            if (move.Coord == coord)
            {
                return move;
            }
        }

        return null;
    }
}

/// <summary>
/// 单子禁手查询（forbidden-marks D1–D3）：候选 = 合法落子范围内、未被暂放占用的空格；
/// 判定 = 把"当前暂放批次 + 在候选格落一枚普通子"交给合法性预演第 3–8 步（<see cref="BatchRehearsal.Settle"/>，唯一实现），
/// 因破坏活形 / 自杀手 / 同形被拒即禁手，原因取预演给出的那一个。额度与库存不参与。只读纯函数，对盘面与历史零副作用。
/// </summary>
/// <remarks>
/// <para><b>预筛（D3）</b>只决定"这一格要不要跑完整预演"，本类不判气、提子、活形与同形：气与棋串只读 <see cref="GameBoard"/> 的查询，
/// 活形只读 <see cref="LifeShapeReport"/>，同形只经 <see cref="BatchRehearsal.DuplicateOf"/>。</para>
/// <para>基准 = 暂放批次自身走第 3–8 步的结果（暂放为空时即正式盘面）。基准不合法时不预筛，全部候选跑完整预演——
/// 再落一子可能恰好把整批救活（补上一口气、提走一条敌串），没有便宜的判据。</para>
/// <para>基准合法时，候选格 c 同时满足以下四条即"安静"——落下去之后第 5–7 步的结果与基准逐项相同：</para>
/// <list type="number">
/// <item>c 在"暂放已放置、尚未提子"的副本上有一个能作为气的邻格 → c 所在棋串有气；己方其余棋串若被 c 占掉一口气，必与 c 连成一串。</item>
/// <item>c 的气边邻格里没有不多于一口气的敌串 → 提子集合与基准相同（气数按提子前的副本数：提子后才多出来的气不算）。</item>
/// <item>c 不在结算后盘面上他人的眼空间里 → 他人每条棋串的眼空间、眼值与基准相同（c 是己方棋子，贴着它的空区不可能对他人封闭）。</item>
/// <item>c 不贴批次开始前他人的已确定活形棋串（D3 的保守条款；由第 3 条已可推出安全，留作双保险）。</item>
/// </list>
/// <para>安静的候选仍 MUST 过第 8 步：不提子的一手同样可能回到历史盘面（多人局的循环、"送二还一"），规格 D3"不可能提子因而不可能同形"
/// 对全局同形不成立。做法是在基准的结算后盘面上落下 c，交给同一个同形比对。</para>
/// </remarks>
public static class ForbiddenMoves
{
    /// <summary>候选子的类型：三类判定都与类型无关，固定用普通子（D1）。</summary>
    public const PieceType ProbeType = PieceType.Basic;

    /// <summary>查询。<paramref name="legalRange"/> 取合法落子范围契约（已扣除禁入格）；<paramref name="staged"/> 为当前暂放批次（已通过预演第 1 步）。</summary>
    public static ForbiddenMoveReport Query(
        GameBoard board, PlayerId player, IReadOnlySet<Coord> legalRange, IReadOnlyList<Placement> staged, BoardHistory history) =>
        Query(board, player, legalRange, staged, history, prefilter: true);

    /// <summary>
    /// <paramref name="prefilter"/> 为 <c>false</c> 时全部候选都走完整预演（第 3–8 步）：对照测试与耗时实测用，结果 MUST 与预筛版逐格相同。
    /// </summary>
    internal static ForbiddenMoveReport Query(
        GameBoard board, PlayerId player, IReadOnlySet<Coord> legalRange, IReadOnlyList<Placement> staged, BoardHistory history, bool prefilter)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(legalRange);
        ArgumentNullException.ThrowIfNull(staged);
        ArgumentNullException.ThrowIfNull(history);

        var occupied = new HashSet<Coord>(staged.Select(p => p.Coord));
        Coord[] candidates = [.. legalRange.Where(c => board.Contains(c) && board[c].IsPlayableEmpty && !occupied.Contains(c)).Order()];
        if (candidates.Length == 0)
        {
            return new ForbiddenMoveReport(player, [], 0, 0);
        }

        // 批次开始前的正式盘面活形：与预演第 6 步同一份输入，全部候选共用。
        LifeShapeReport before = LifeShapeReport.Analyze(board);
        Quiet? quiet = prefilter ? Quiet.TryCreate(board, player, staged, history, before) : null;

        ImmutableArray<ForbiddenMove>.Builder moves = ImmutableArray.CreateBuilder<ForbiddenMove>();
        var batch = new Placement[staged.Count + 1];
        for (int i = 0; i < staged.Count; i++)
        {
            batch[i] = staged[i];
        }

        int full = 0;
        foreach (Coord c in candidates)
        {
            if (quiet is not null && quiet.IsQuiet(c))
            {
                if (quiet.DuplicateWith(c) is { } sequence)
                {
                    moves.Add(new ForbiddenMove(c, ForbiddenMoveKind.Superko, DuplicateOfSequence: sequence));
                }

                continue;
            }

            full++;
            batch[^1] = new Placement(c, ProbeType);
            if (BatchRehearsal.Settle(board, player, batch, history, before).Failure is { } failure)
            {
                moves.Add(new ForbiddenMove(c, KindOf(failure), failure.DuplicateOfSequence, failure.LifeOwner));
            }
        }

        return new ForbiddenMoveReport(player, moves.ToImmutable(), candidates.Length, full);
    }

    /// <summary>预演失败类别 → 禁手类别。第 3–8 步只会给出这三类；其余类别属于第 1–2 步，出现即说明候选或暂放不满足前提。</summary>
    private static ForbiddenMoveKind KindOf(BatchFailure failure) => failure.Kind switch
    {
        BatchFailureKind.BreaksLife => ForbiddenMoveKind.BreaksLife,
        BatchFailureKind.Suicide => ForbiddenMoveKind.Suicide,
        BatchFailureKind.Superko => ForbiddenMoveKind.Superko,
        _ => throw new SiegeRuleException($"单子禁手查询遇到第 3–8 步之外的失败类别 {failure.Kind}：{failure.Message}"),
    };

    /// <summary>预筛用的一次性底稿：每次查询建一份，全部候选共用。</summary>
    private sealed class Quiet
    {
        private readonly GameBoard _placed;
        private readonly GameBoard _settled;
        private readonly PlayerId _player;
        private readonly BoardHistory _history;
        private readonly int _width;
        private readonly int[] _enemyLiberties;
        private readonly bool[] _aliveEnemy;
        private readonly LifeShapeReport? _settledLife;

        private Quiet(
            GameBoard placed, GameBoard settled, PlayerId player, BoardHistory history,
            int[] enemyLiberties, bool[] aliveEnemy, LifeShapeReport? settledLife)
        {
            _placed = placed;
            _settled = settled;
            _player = player;
            _history = history;
            _width = placed.Width;
            _enemyLiberties = enemyLiberties;
            _aliveEnemy = aliveEnemy;
            _settledLife = settledLife;
        }

        /// <summary>基准不合法时返回 <c>null</c>（不预筛）。</summary>
        internal static Quiet? TryCreate(
            GameBoard board, PlayerId player, IReadOnlyList<Placement> staged, BoardHistory history, LifeShapeReport before)
        {
            // 基准：暂放自身的第 3–8 步。暂放为空时基准就是正式盘面（空批次不走第 3–8 步）。
            GameBoard placed = BatchRehearsal.Project(board, player, staged);
            GameBoard settled = placed;
            if (staged.Count > 0)
            {
                RehearsalResult baseline = BatchRehearsal.Settle(board, player, staged, history, before);
                if (!baseline.IsLegal)
                {
                    return null;
                }

                settled = baseline.ProjectedBoard!;
            }

            // 每格所在敌串的气数（提子前的副本上；空格与己方棋子记 -1）。
            int width = placed.Width;
            int[] enemyLiberties = new int[width * placed.Height];
            Array.Fill(enemyLiberties, -1);
            foreach (Group group in placed.AllGroups())
            {
                if (group.Owner == player)
                {
                    continue;
                }

                int liberties = placed.LibertiesOf(group).Length;
                foreach (Coord stone in group.Stones)
                {
                    enemyLiberties[(stone.Y * width) + stone.X] = liberties;
                }
            }

            // 批次开始前他人的已确定活形棋串的棋子。没有这样的棋串时第 6 步不可能触发，也就不必分析结算后盘面。
            bool[] aliveEnemy = new bool[enemyLiberties.Length];
            bool anyAlive = false;
            foreach (GroupLife life in before.Groups)
            {
                if (life.Life != LifeState.Alive || life.Group.Owner == player)
                {
                    continue;
                }

                anyAlive = true;
                foreach (Coord stone in life.Group.Stones)
                {
                    aliveEnemy[(stone.Y * width) + stone.X] = true;
                }
            }

            LifeShapeReport? settledLife = !anyAlive ? null : staged.Count == 0 ? before : LifeShapeReport.Analyze(settled);
            return new Quiet(placed, settled.Clone(), player, history, enemyLiberties, aliveEnemy, settledLife);
        }

        internal bool IsQuiet(Coord c)
        {
            bool breathes = false;
            foreach (Coord n in _placed.LibertyNeighbors(c))
            {
                int i = (n.Y * _width) + n.X;
                if (_enemyLiberties[i] is >= 0 and <= 1 || _aliveEnemy[i])
                {
                    return false;
                }

                breathes |= _placed.GivesLiberty(n);
            }

            return breathes && !(_settledLife?.EyeSpaceAt(c) is { } space && space.Owner != _player);
        }

        /// <summary>安静候选的第 8 步：基准的结算后盘面上落下 c，交给同一个同形比对；比完即撤掉。</summary>
        internal int? DuplicateWith(Coord c)
        {
            if (_history.Count == 0)
            {
                return null;
            }

            _settled.Place(c, _player, ProbeType);
            int? sequence = BatchRehearsal.DuplicateOf(_history, _settled);
            _settled.Clear(c);
            return sequence;
        }
    }
}
