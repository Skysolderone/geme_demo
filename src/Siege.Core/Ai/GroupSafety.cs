using Siege.Core.Board;

namespace Siege.Core.Ai;

/// <summary>
/// 棋串安全度的启发式近似（裁决 1）：气数、气的分布是否分散、是否存在两个互不相邻的单点气。不做完整死活判定。
/// 邻接只走 <see cref="GameBoard.Neighbors"/>，气只走 <see cref="GameBoard.LibertiesOf"/>。
/// </summary>
/// <param name="Liberties">气数。</param>
/// <param name="EyePoints">单点气数：该气的每个邻格都是己方棋子或障碍（棋盘外沿同义）。两个单点气必然互不相邻——若相邻，彼此就是对方的空邻格。</param>
/// <param name="DispersedLiberties">分散气数：不与本串任何其他气相邻的气。</param>
/// <param name="Size">棋串大小。</param>
public readonly record struct GroupSafety(int Liberties, int EyePoints, int DispersedLiberties, int Size)
{
    /// <summary>两眼潜力：互不相邻的单点气数，封顶 2。</summary>
    public int TwoEyePotential => Math.Min(2, EyePoints);

    /// <summary>敌方一个小回合至少能落这么多子（基础部署上限），气数不超过它的棋串一手就能被提光。</summary>
    public const int DangerLiberties = 3;

    /// <summary>
    /// 安全分：气数（封顶 4）+ 两眼潜力 × 6 + 分散气（封顶 2）− 危险。批次制下敌方一回合落 3 子，
    /// 所以危险阈值是 <see cref="DangerLiberties"/> 而不是围棋的"打吃"：1 气 大小 × 12、2 气 大小 × 8、3 气 大小 × 4。
    /// 故意不随棋子数线性增长，避免评价偏向把棋子撒开。
    /// </summary>
    public long Score
    {
        get
        {
            long danger = Liberties switch
            {
                <= 1 => (long)Size * 12,
                2 => (long)Size * 8,
                DangerLiberties => (long)Size * 4,
                _ => 0,
            };
            return Math.Min(Liberties, 4) + (TwoEyePotential * 6) + Math.Min(DispersedLiberties, 2) - danger;
        }
    }

    /// <summary>分析一条棋串。</summary>
    public static GroupSafety Analyze(GameBoard board, Group group)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(group);
        var liberties = board.LibertiesOf(group).ToHashSet();
        int eyes = 0;
        int dispersed = 0;
        foreach (Coord liberty in liberties)
        {
            bool isEye = true;
            bool touchesLiberty = false;
            foreach (Coord n in board.Neighbors(liberty))
            {
                Cell cell = board[n];
                if (liberties.Contains(n))
                {
                    touchesLiberty = true;
                }

                bool ownWall = cell.Terrain == Terrain.Obstacle || (cell.Occupant is { } o && o.Owner == group.Owner);
                if (!ownWall)
                {
                    isEye = false;
                }
            }

            if (isEye)
            {
                eyes++;
            }

            if (!touchesLiberty)
            {
                dispersed++;
            }
        }

        return new GroupSafety(liberties.Count, eyes, dispersed, group.Size);
    }
}
