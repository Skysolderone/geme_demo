using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Batch;

/// <summary>
/// 一个小回合内的暂放批次（设计文档 §5.4）。与正式盘面完全分离：只读取盘面用于校验落点，
/// 从不写入；暂放、撤销、换位、替换次数不限，且不扣手牌。
/// </summary>
/// <remarks>
/// <para>暂放时只做预演第 1–2 步（落点与数量/库存），自杀手与同形留到确认时的完整预演——
/// 一枚此刻看似无气的暂放棋子，可能因整批提子而合法。</para>
/// <para>本对象生命周期限于一个小回合：新小回合创建新的批次并携带新的 <see cref="BatchContext"/>，
/// 未使用的部署额度不会跨小回合保存。</para>
/// </remarks>
public sealed class StagedBatch
{
    private readonly List<Placement> _placements = [];

    public StagedBatch(GameBoard board, BatchContext context)
    {
        Board = board ?? throw new ArgumentNullException(nameof(board));
        Context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>被校验的正式盘面。本类只读它。</summary>
    public GameBoard Board { get; }

    /// <summary>本小回合的上游输入。</summary>
    public BatchContext Context { get; }

    /// <summary>当前暂放，按批次内落子顺序。</summary>
    public ImmutableArray<Placement> Placements => [.. _placements];

    public int Count => _placements.Count;

    /// <summary>暂放一枚。返回 <c>null</c> 表示接受，否则为拒绝原因，暂放状态不变。</summary>
    public BatchFailure? Stage(Coord coord, PieceType type) =>
        TryApply([.. _placements, new Placement(coord, type)]);

    /// <summary>换位：把 <paramref name="from"/> 上的暂放移到 <paramref name="to"/>，保留其批次内顺序与类型。</summary>
    public BatchFailure? Move(Coord from, Coord to)
    {
        int index = IndexOf(from);
        var candidate = new List<Placement>(_placements)
        {
            [index] = _placements[index] with { Coord = to },
        };
        return TryApply(candidate);
    }

    /// <summary>替换：把 <paramref name="coord"/> 上的暂放改用另一种手牌类型，保留其批次内顺序。</summary>
    public BatchFailure? Replace(Coord coord, PieceType type)
    {
        int index = IndexOf(coord);
        var candidate = new List<Placement>(_placements)
        {
            [index] = _placements[index] with { Type = type },
        };
        return TryApply(candidate);
    }

    /// <summary>撤销一枚暂放。该格没有暂放时返回 <c>false</c>。</summary>
    public bool Unstage(Coord coord)
    {
        int index = _placements.FindIndex(p => p.Coord == coord);
        if (index < 0)
        {
            return false;
        }

        _placements.RemoveAt(index);
        return true;
    }

    /// <summary>清空全部暂放。</summary>
    public void Clear() => _placements.Clear();

    private int IndexOf(Coord coord)
    {
        int index = _placements.FindIndex(p => p.Coord == coord);
        return index >= 0
            ? index
            : throw new ArgumentException($"该格没有暂放棋子：{coord.ToNotation()}。", nameof(coord));
    }

    private BatchFailure? TryApply(List<Placement> candidate)
    {
        BatchFailure? failure = BatchRehearsal.ValidateShape(Board, Context, candidate);
        if (failure is null)
        {
            _placements.Clear();
            _placements.AddRange(candidate);
        }

        return failure;
    }
}
