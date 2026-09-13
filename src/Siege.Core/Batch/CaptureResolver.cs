using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Batch;

/// <summary>
/// 同时提子（设计文档 §6.1 第 4 步 / §6.3 第 3 步）的唯一实现。
/// </summary>
/// <remarks>
/// <para>这是全项目最高危的实现陷阱：提子 MUST 是集合操作。先在"整批已放置"的盘面上
/// <b>一次性</b>算出全部无气敌串的并集，再由调用方统一移除。写成"遍历敌串，无气就移除，然后继续遍历"，
/// 两条相邻的不同敌方棋串会因遍历顺序不同得到不同结果。</para>
/// <para>本类只计算、不写盘：返回的集合与候选棋串的遍历顺序无关，回归测试以种子驱动的随机顺序
/// 反复调用 <see cref="FindCaptured(GameBoard, PlayerId, IEnumerable{Group})"/> 断言结果一致。</para>
/// </remarks>
public static class CaptureResolver
{
    /// <summary>在当前盘面上找出全部无气的敌方（非 <paramref name="mover"/>）棋串的棋子并集，按坐标字典序排列。</summary>
    public static ImmutableArray<CapturedStone> FindCaptured(GameBoard board, PlayerId mover)
    {
        ArgumentNullException.ThrowIfNull(board);
        return FindCaptured(board, mover, board.AllGroups());
    }

    /// <summary>
    /// 顺序无关性的测试接缝：候选棋串按调用方给定的任意顺序遍历。
    /// 实现 MUST NOT 在遍历过程中修改盘面——每条棋串的气都基于同一个"整批已放置、尚未提子"的盘面判定。
    /// </summary>
    internal static ImmutableArray<CapturedStone> FindCaptured(GameBoard board, PlayerId mover, IEnumerable<Group> candidates)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(candidates);

        var captured = new SortedDictionary<Coord, CapturedStone>();
        foreach (Group group in candidates)
        {
            if (group.Owner == mover || !board.IsCaptured(group))
            {
                continue;
            }

            foreach (Coord stone in group.Stones)
            {
                Occupant occupant = board[stone].Occupant
                    ?? throw new SiegeRuleException($"候选棋串与盘面不一致：{stone.ToNotation()} 已为空。");
                captured[stone] = new CapturedStone(stone, occupant.Owner, occupant.Type);
            }
        }

        return [.. captured.Values];
    }
}
