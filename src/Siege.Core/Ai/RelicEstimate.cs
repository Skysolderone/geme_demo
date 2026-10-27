using Siege.Core.Board;
using Siege.Core.PieceRelics;
using Siege.Core.Scoring;

namespace Siege.Core.Ai;

/// <summary>
/// AI 评价"信物"维的盘面总分（设计文档 §15.1，维度 3）。relic-redesign 接线段 W1：旧十类地图信物（揭示 / 控制 / 分区先验）随旧系统删除，
/// 这里只给<b>按新信物层的最简估计</b>——挂在己方棋子上的每件 +<see cref="UncalibratedFlatValue"/>、挂在参赛中敌方棋子上的每件 −<see cref="UncalibratedFlatValue"/>，
/// 地图信物（空格上）与弃赛 / 出局者棋子上的不计。<b>段 C 重写</b>（design.md D5：按"拾取后势力增量 / 提走后掉落可被拾取"估值、重标权重）。
/// </summary>
/// <remarks>
/// 信物的计分效果（战鼓 / 号令 / 百变 / 军旗、旗手子）已经经唯一的势力计算进入"即时势力增量"维：盘面副本带信物层，预演即可看到拾取后的势力。
/// 本维只反映"持有"本身的价值，输入只有公开盘面（信物层全公开，R22），不读任何私有信息。
/// </remarks>
public static class RelicEstimate
{
    /// <summary>每件持有信物的价值：<b>未校准的初值 5</b>（不分种类；段 C 重写）。</summary>
    public const int UncalibratedFlatValue = 5;

    /// <summary>
    /// 盘面总分：Σ 挂在 <paramref name="me"/> 棋子上的信物 × 价值 − Σ 挂在参赛中敌方棋子上的信物 × 价值。
    /// <paramref name="roster"/> 里不在册或非参赛中的持有者不计（与旧维度"弃赛者封锁 0"同口径）。
    /// </summary>
    public static long Score(GameBoard board, PlayerId me, IReadOnlyDictionary<PlayerId, PlayerStatus> roster)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(roster);
        if (!board.HasRelics)
        {
            return 0;
        }

        long score = 0;
        foreach (BoardRelic relic in board.RelicsOnBoard())
        {
            if (board[relic.Cell].Occupant is not { } holder)
            {
                continue;
            }

            if (holder.Owner == me)
            {
                score += UncalibratedFlatValue;
            }
            else if (roster.TryGetValue(holder.Owner, out PlayerStatus status) && status == PlayerStatus.Active)
            {
                score -= UncalibratedFlatValue;
            }
        }

        return score;
    }
}
