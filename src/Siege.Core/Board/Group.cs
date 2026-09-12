using System.Collections.Immutable;

namespace Siege.Core.Board;

/// <summary>
/// 棋串：同一玩家通过四邻接直接或间接相连的全部棋子。
/// 棋子类型 MUST NOT 影响棋串归属——不同类型的己方棋子相邻时合并为同一棋串并共享气。
/// </summary>
/// <remarks>
/// 棋串是派生量，每次盘面变化后重算，不做增量维护。
/// 规格：openspec/changes/add-board-core/specs/board-topology —— Requirement: 棋串构成
/// </remarks>
public sealed class Group
{
    internal Group(PlayerId owner, ImmutableArray<Coord> stones)
    {
        Owner = owner;
        Stones = stones;
    }

    /// <summary>棋串所有者。</summary>
    public PlayerId Owner { get; }

    /// <summary>棋串包含的棋子坐标，按确定性字典序排列。</summary>
    public ImmutableArray<Coord> Stones { get; }

    /// <summary>棋串大小。</summary>
    public int Size => Stones.Length;

    public override string ToString() =>
        $"{Owner}[{string.Join(",", Stones.Select(s => s.ToNotation()))}]";
}
