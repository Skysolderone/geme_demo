using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Preview;

/// <summary>一条棋串的气：所有者、棋子坐标（字典序）与气位（字典序）。设计文档 §13.1「棋串结构与可推导的气」的公开载荷。</summary>
public sealed record GroupLiberties(PlayerId Owner, ImmutableArray<Coord> Stones, ImmutableArray<Coord> Liberties)
{
    /// <summary>气数。</summary>
    public int Count => Liberties.Length;
}

/// <summary>
/// 全盘棋串与气的快照（tactical-ui 裁决 9）：表现层的气层只消费它，不自己遍历邻接。
/// 本类只是把 <see cref="GameBoard.AllGroups"/> 与 <see cref="GameBoard.LibertiesOf"/> 的结果装箱，不含第二套气的实现。
/// </summary>
public static class LibertySnapshot
{
    /// <summary>对盘面全量计算，按棋串最小坐标字典序排列。纯函数，不写盘。</summary>
    public static ImmutableArray<GroupLiberties> Compute(GameBoard board)
    {
        ArgumentNullException.ThrowIfNull(board);
        return [.. board.AllGroups().Select(g => new GroupLiberties(g.Owner, g.Stones, board.LibertiesOf(g)))];
    }
}
